using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using OverTheHillPlus.Api;
using OverTheHillPlus.Cars;
using UnityEngine;

namespace OverTheHillPlus.Accessories;

/// <summary>
/// A modded car's accessory editor data, on its MODS page and in the F8 panel (<see cref="AccessoryEditor"/>):
/// <list type="bullet">
/// <item>Stacking: any number of extra items per Visual slot shown next to the one the game has fitted (two bed loads,
/// a roof rack and a roof tent...). Saved in the car mod's config, [Stack], as a comma list of item names per slot.</item>
/// <item>Fitment: every shown item moved (car metres: X right, Y up, Z forward), turned (degrees, car axes) and sized,
/// on top of where the car framework placed it. Saved in [Items] per item.</item>
/// </list>
/// The menus are built from the live cosmetic tree (group / slot / item under the car's *_CosmeticParts), so every item
/// the game has shows up. Applied to your garage models and your own car, and sent to the other players
/// (<see cref="AccessoryNet"/>): everyone with the editor sees everyone's layout on their car and lobby copy; players
/// without it see the game's normal fit. One per running car framework car, made by <see cref="AccessoriesPlugin"/>;
/// the data goes in that car mod's own config file.
/// </summary>
public sealed class CarAccessories
{
    private const float MaxOffset = 1.5f;

    public readonly CarDefinition Def;
    /// <summary>What the panel calls the car (its display name).</summary>
    public readonly string Label;
    private CarWheelTweaks _wheels;
    /// <summary>Extra F8 panel tabs after FIT and STACK (title, draw with the <see cref="AccessoryEditor"/> helpers).</summary>
    public readonly List<(string Title, Action Draw)> Tabs = new();

    internal sealed class Fit
    {
        public ConfigEntry<float> X, Y, Z, Yaw, Pitch, Roll, Size, SX, SY, SZ;
        public string Signature => $"{X.Value},{Y.Value},{Z.Value},{Yaw.Value},{Pitch.Value},{Roll.Value},{Size.Value},{SX.Value},{SY.Value},{SZ.Value}";
        public bool IsDefault => X.Value == 0f && Y.Value == 0f && Z.Value == 0f && Yaw.Value == 0f && Pitch.Value == 0f &&
                                 Roll.Value == 0f && Size.Value == 1f && SX.Value == 1f && SY.Value == 1f && SZ.Value == 1f;
        public void Reset()
        {
            X.Value = Y.Value = Z.Value = Yaw.Value = Pitch.Value = Roll.Value = 0f;
            Size.Value = SX.Value = SY.Value = SZ.Value = 1f;
        }
        public AccessoryLayout.FitValues Values()
        {
            var f = new AccessoryLayout.FitValues();
            float[] v = { X.Value, Y.Value, Z.Value, Yaw.Value, Pitch.Value, Roll.Value, Size.Value, SX.Value, SY.Value, SZ.Value };
            Array.Copy(v, f.V, 10);
            return f;
        }
    }

    private sealed class Base { public Vector3 Pos, Scale; public Quaternion Rot; }

    private readonly ConfigFile _cfg;
    private readonly ManualLogSource _log;
    private readonly HashSet<string> _hidden;
    private readonly Dictionary<string, Fit> _fits = new();
    private readonly Dictionary<string, ConfigEntry<string>> _stacks = new();
    private readonly Dictionary<string, HashSet<string>> _stackSets = new();

    /// <summary>Roots we fit: the garage model(s) and your own live car.</summary>
    private readonly List<GameObject> _roots = new();
    private readonly Dictionary<IntPtr, Base> _bases = new();
    /// <summary>Items we switched on for a stack (so taking one off a stack only switches off what we switched on).</summary>
    private readonly HashSet<IntPtr> _onByUs = new();
    private IntPtr _liveCar;
    private float _nextScan;
    private int _version, _applied = -1;

    /// <param name="wheelsTab">Add the standard WHEELS tab (<see cref="CarWheelTweaks"/>: size and offset). False for a car
    /// that brings its own (the Tacoma's, with its garage tyre sizes: <see cref="AccessoryTabs"/>).</param>
    public CarAccessories(CarDefinition def, ConfigFile cfg, ManualLogSource log, bool wheelsTab = true)
    {
        Def = def;
        Label = def.DisplayName;
        _cfg = cfg;
        _log = log;
        _hidden = new HashSet<string>(def.HiddenItems ?? Array.Empty<string>());
        Features.Registry.OnTick(Tick, AccessoriesPlugin.ModId);
        if (wheelsTab)
        {
            _wheels = new CarWheelTweaks(this, cfg);
            Tabs.Add(("WHEELS", _wheels.DrawTab));
        }
        foreach (var root in CarObjects.ModelsOf(def.Id)) Track(root, fresh: true); // garage models built before us
        AccessoryEditor.Register(this);
    }

    /// <summary>The car brings its own wheels tab: the standard one goes and stops applying its size and offset.</summary>
    internal void DropStandardWheels()
    {
        if (_wheels == null) return;
        _wheels.Enabled = false;
        Tabs.RemoveAll(t => t.Title == "WHEELS");
        _wheels = null;
    }

    private void Bump(object sender, EventArgs args) => _version++;

    /// <summary>The garage model(s) and your own live car, as tracked here.</summary>
    public IEnumerable<GameObject> FittedRoots()
    {
        for (int i = 0; i < _roots.Count; i++)
            if (_roots[i] != null) yield return _roots[i];
    }

    private void Info(string s) { try { _log?.LogInfo("[Accessories] " + s); } catch { } }

    // ------------------------------------------------------------------ config

    /// <summary>BepInEx keys can't hold = \ " ' [ ] or line breaks.</summary>
    private static string Key(string path)
    {
        var c = path.ToCharArray();
        for (int i = 0; i < c.Length; i++)
            if ("=\\\"'[]\n\r\t".IndexOf(c[i]) >= 0) c[i] = '_';
        return new string(c);
    }

    internal Fit FitOf(string itemPath)
    {
        if (_fits.TryGetValue(itemPath, out var f)) return f;
        string k = Key(itemPath);
        ConfigEntry<float> B(string s, float d, string what)
        {
            var e = _cfg.Bind("Items", k + "." + s, d, $"{itemPath}: {what}");
            e.SettingChanged += Bump;
            return e;
        }
        f = new Fit
        {
            X = B("X", 0f, "metres right"), Y = B("Y", 0f, "metres up"), Z = B("Z", 0f, "metres forward"),
            Yaw = B("Yaw", 0f, "degrees, turn left/right"), Pitch = B("Pitch", 0f, "degrees, tip forward/back"),
            Roll = B("Roll", 0f, "degrees, lean left/right"), Size = B("Size", 1f, "overall size"),
            SX = B("SizeX", 1f, "width"), SY = B("SizeY", 1f, "height"), SZ = B("SizeZ", 1f, "length"),
        };
        _fits[itemPath] = f;
        return f;
    }

    internal HashSet<string> StackOf(string slotPath)
    {
        if (_stackSets.TryGetValue(slotPath, out var set)) return set;
        var e = _cfg.Bind("Stack", Key(slotPath), "", $"{slotPath}: extra items shown next to the fitted one (comma list)");
        _stacks[slotPath] = e;
        set = new HashSet<string>(e.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.Ordinal);
        _stackSets[slotPath] = set;
        return set;
    }

    internal void SetStacked(string slotPath, string item, bool on)
    {
        var set = StackOf(slotPath);
        if (on ? !set.Add(item) : !set.Remove(item)) return;
        _stacks[slotPath].Value = string.Join(",", set.OrderBy(s => s, StringComparer.Ordinal));
        _version++;
    }

    // ------------------------------------------------------------------ the cosmetic tree

    /// <summary>Whether a car object is this car (core's <see cref="CarObjects.IsCar"/>).</summary>
    public bool IsCar(Transform root) => CarObjects.IsCar(Def, root);

    private static Transform Cosmetics(Transform root)
    {
        var parts = root == null ? null : root.Find("Parts");
        if (parts == null) return null;
        for (int i = 0; i < parts.childCount; i++)
        {
            var c = parts.GetChild(i);
            if (c.name.EndsWith("_CosmeticParts", StringComparison.Ordinal)) return c;
        }
        return null;
    }

    internal struct Slot
    {
        public string Group, Path, Name;
        public Transform T;
        /// <summary>A slot inside a slot (the Side slot's Side_Left / Side_Right, each with its own items).</summary>
        public bool Sub;
        /// <summary>A group of alternatives deep inside an item (the antenna's toppers, on the end of its swinging bone).</summary>
        public bool Nested;
        public bool Stackable => Group == "Visual";
    }

    /// <summary>
    /// Every slot with items, sub-slots included. A sub-slot is a child of a slot that holds alternatives rather than
    /// being one item: no renderer of its own, two or more children that each draw something, and at most one of them
    /// switched on. Decided the first time it's seen (before any stacking switches more on), then remembered. Groups of
    /// alternatives inside an item (<see cref="NestedGroups"/>: the antenna's toppers) come after their slot.
    /// </summary>
    internal IEnumerable<Slot> Slots(Transform cosm)
    {
        if (cosm == null) yield break;
        for (int g = 0; g < cosm.childCount; g++)
        {
            var group = cosm.GetChild(g);
            for (int s = 0; s < group.childCount; s++)
            {
                var slot = group.GetChild(s);
                if (slot.childCount == 0) continue;
                string path = group.name + "/" + slot.name;
                var top = new Slot { Group = group.name, Path = path, Name = Pretty(slot.name), T = slot };
                bool items = false;
                var nested = new List<Slot>();
                for (int i = 0; i < slot.childCount; i++)
                {
                    var c = slot.GetChild(i);
                    if (IsSubSlot(c))
                        yield return new Slot { Group = group.name, Path = path + "/" + c.name, Name = Pretty(c.name), T = c, Sub = true };
                    else if (!_hidden.Contains(path + "/" + c.name))
                    {
                        items = true;
                        foreach (var (t, rel) in NestedGroups(c))
                            nested.Add(new Slot
                            {
                                Group = group.name, Path = path + "/" + c.name + "/" + rel, T = t, Sub = true, Nested = true,
                                Name = Pretty(c.name) + " " + Words(t.name).ToLowerInvariant(),
                            });
                    }
                }
                if (items) yield return top;
                foreach (var n in nested) yield return n;
            }
        }
    }

    private static readonly Dictionary<IntPtr, List<(Transform t, string rel)>> Nested = new();

    /// <summary>
    /// The groups of alternatives inside an item, with their path from it: a part with two or more children that are
    /// each an upgrade of their own (the game's VehicleUpgradeObject), like the antenna's TopperAttachments (Capybara,
    /// flags, plushies...) on the end of its swinging bone, which the garage fits one at a time. Walked once per item.
    /// </summary>
    internal static List<(Transform t, string rel)> NestedGroups(Transform item)
    {
        if (Nested.TryGetValue(item.Pointer, out var known)) return known;
        var found = new List<(Transform, string)>();
        try
        {
            foreach (var t in item.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.Pointer == item.Pointer || t.childCount < 2) continue;
                int upgrades = 0;
                for (int i = 0; i < t.childCount; i++)
                    if (t.GetChild(i).GetComponent<VehicleUpgradeObject>() != null) upgrades++;
                if (upgrades < 2) continue;
                var parts = new List<string>();
                for (var p = t; p != null && p.Pointer != item.Pointer; p = p.parent) parts.Insert(0, p.name);
                found.Add((t, string.Join("/", parts)));
            }
        }
        catch (Exception e) { AccessoriesPlugin.Logger?.LogWarning("[Accessories] nested groups of " + item.name + ": " + e.Message); }
        Nested[item.Pointer] = found;
        return found;
    }

    /// <summary>"TopperAttachments" as "Topper Attachments".</summary>
    private static string Words(string name)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]) && name[i - 1] != '_') sb.Append(' ');
            sb.Append(c == '_' ? ' ' : c);
        }
        return sb.ToString().Trim();
    }

    private static readonly Dictionary<IntPtr, bool> SubSlots = new();

    internal static bool IsSubSlot(Transform c)
    {
        if (SubSlots.TryGetValue(c.Pointer, out var known)) return known;
        bool sub = false;
        if (c.childCount >= 2 && c.GetComponent<Renderer>() == null)
        {
            int on = 0, drawing = 0;
            for (int i = 0; i < c.childCount; i++)
            {
                var k = c.GetChild(i);
                if (k.gameObject.activeSelf) on++;
                if (k.GetComponentInChildren<Renderer>(true) != null) drawing++;
            }
            sub = on <= 1 && drawing == c.childCount;
        }
        SubSlots[c.Pointer] = sub;
        return sub;
    }

    internal IEnumerable<Transform> Items(Slot slot)
    {
        for (int i = 0; i < slot.T.childCount; i++)
        {
            var it = slot.T.GetChild(i);
            if (_hidden.Contains(slot.Path + "/" + it.name)) continue;
            if (!slot.Sub && IsSubSlot(it)) continue;
            yield return it;
        }
    }

    /// <summary>What the editor can move: every item, plus each sub-slot as a whole ("the whole left side").</summary>
    internal IEnumerable<(Transform t, string path, string label, Slot slot)> Movables(Transform cosm)
    {
        foreach (var slot in Slots(cosm))
        {
            if (slot.Sub) yield return (slot.T, slot.Path, slot.Name + (slot.Nested ? " (all)" : " (whole side)"), slot);
            foreach (var it in Items(slot)) yield return (it, slot.Path + "/" + it.name, slot.Name + " > " + Pretty(it.name), slot);
        }
    }

    // ------------------------------------------------------------------ hooks and tick

    /// <summary>The car's AfterBody hook: the framework just (re)placed the cosmetics of a template or garage model.</summary>
    public string AfterBody(GameObject root, bool template)
    {
        if (template || root == null) return null; // the networked template is never drawn
        Track(root, fresh: true);
        EnforceStacks(root, LocalStacked);
        ApplyFits(root, LocalFit);
        _applied = -1;
        return null;
    }

    private void Track(GameObject root, bool fresh)
    {
        if (!_roots.Exists(r => r != null && r.Pointer == root.Pointer)) _roots.Add(root);
        if (fresh) _trees.Remove(root.Pointer);
        foreach (var m in Movables(Cosmetics(root.transform)))
            if (fresh || !_bases.ContainsKey(m.t.Pointer))
            {
                _bases[m.t.Pointer] = new Base { Pos = m.t.localPosition, Rot = m.t.localRotation, Scale = m.t.localScale };
                _appliedSig.Remove(m.t.Pointer);
            }
    }

    private void Tick()
    {
        try
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                _roots.RemoveAll(r => r == null);
                var vc = GameState.LocalCar;
                IntPtr ptr = vc == null ? IntPtr.Zero : vc.Pointer;
                if (ptr != _liveCar)
                {
                    _liveCar = ptr;
                    var root = CarObjects.LiveCar(Def);
                    if (root != null)
                    {
                        Track(root, fresh: false);
                        Info($"fitting your {Label} {root.name}");
                    }
                }
                _applied = -1; // also catches items the game just fitted
            }
            foreach (var r in _roots)
                if (r != null) EnforceStacks(r, LocalStacked);
            if (_applied != _version)
            {
                _applied = _version;
                foreach (var r in _roots)
                    if (r != null) ApplyFits(r, LocalFit);
            }
            TickOthers();
        }
        catch (Exception e) { Info("tick failed: " + e.Message); }
    }

    // ------------------------------------------------------------------ other players' cars

    /// <summary>Other players' cars and lobby copies of this car, each with its owner (refreshed twice a second).</summary>
    private List<(GameObject root, int owner)> _others = new();
    /// <summary>The layout each copy last got (by reference: a new message is a new object). Copies in here have been
    /// changed by us, so they are put back to the game's fit if their player's layout goes away.</summary>
    private readonly Dictionary<IntPtr, AccessoryLayout> _othersApplied = new();
    private static readonly AccessoryLayout Empty = new();
    private float _nextOthers;

    private void TickOthers()
    {
        if (Time.unscaledTime >= _nextOthers)
        {
            _nextOthers = Time.unscaledTime + 0.5f;
            _others = new List<(GameObject, int)>(AccessoryNet.OthersCars(Def));
            var alive = new HashSet<IntPtr>();
            foreach (var (root, _) in _others)
            {
                if (root == null) continue;
                alive.Add(root.Pointer);
                TrackOther(root);
            }
            foreach (var k in _othersApplied.Keys.Where(k => !alive.Contains(k)).ToList()) _othersApplied.Remove(k);
            // like your own car: also catches items the game just fitted
            foreach (var k in _othersApplied.Keys.ToList()) _othersApplied[k] = null;
        }
        foreach (var (root, owner) in _others)
        {
            if (root == null) continue;
            var layout = AccessoryNet.Of(owner, Def.Id);
            bool touched = _othersApplied.ContainsKey(root.Pointer);
            if (layout == null && !touched) continue; // they don't run the editor: the game's fit, untouched
            layout ??= Empty;
            EnforceStacks(root, layout.IsStacked);
            if (touched && ReferenceEquals(_othersApplied[root.Pointer], layout)) continue;
            _othersApplied[root.Pointer] = layout;
            ApplyFits(root, (path, _) => layout.FitOf(path));
        }
    }

    /// <summary>Another player's copy: its items' places as the game made them (a session car is a clone of the
    /// untouched network template, a lobby copy of the source dummy), so their fits go on top of the right base.</summary>
    private void TrackOther(GameObject root)
    {
        foreach (var m in TreeOf(root).Movables)
            if (m.t != null && !_bases.ContainsKey(m.t.Pointer))
                _bases[m.t.Pointer] = new Base { Pos = m.t.localPosition, Rot = m.t.localRotation, Scale = m.t.localScale };
    }

    /// <summary>Other players' copies of this car with the layout each shows (null: the game's fit), for the wheels.</summary>
    internal IEnumerable<(GameObject root, AccessoryLayout layout)> OthersWithLayouts()
    {
        foreach (var (root, owner) in _others)
            if (root != null) yield return (root, AccessoryNet.Of(owner, Def.Id));
    }

    // ------------------------------------------------------------------ your layout, to send

    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    /// <summary>Your layout for this car as the network sends it (empty bytes: too big even without fits).</summary>
    internal byte[] Encode()
    {
        var l = new AccessoryLayout { CarId = Def.Id };
        foreach (var kv in _stackSets)
            if (kv.Value.Count > 0) l.Stacks[kv.Key] = new SortedSet<string>(kv.Value, StringComparer.Ordinal);
        foreach (var kv in _fits)
        {
            var f = kv.Value.Values();
            if (!f.IsDefault) l.Fits[kv.Key] = f;
        }
        if (_wheels != null && _wheels.Enabled)
        {
            l.Values["wheels.size"] = _wheels.Size.Value;
            l.Values["wheels.offset"] = _wheels.Offset.Value;
        }
        if (AccessorySync.Locals.TryGetValue(Def.Id, out var extra))
            foreach (var kv in extra)
            {
                try { l.Values[kv.Key] = kv.Value(); }
                catch (Exception e) { if (_warned.Add(kv.Key)) Info($"value {kv.Key}: {e.Message}"); }
            }
        var bytes = l.Encode(out int dropped);
        if (dropped > 0 && _warned.Add("dropped")) Info($"your layout is too big to send whole: {dropped} moved item(s) left out");
        return bytes;
    }

    /// <summary>A car's cosmetic tree as the per-frame work needs it, walked once: reading every slot's and item's
    /// name through the interop each frame (and the movables every half second) was a steady 0.3 ms a frame with
    /// 4-14 ms peaks. The tree's shape is fixed once the framework has placed the cosmetics (AfterBody drops it).</summary>
    private sealed class Tree
    {
        public readonly List<(Slot slot, List<(Transform t, string name)> items)> Stackable = new();
        public readonly List<(Transform t, string path)> Movables = new();
    }

    private readonly Dictionary<IntPtr, Tree> _trees = new();

    private Tree TreeOf(GameObject root)
    {
        if (_trees.TryGetValue(root.Pointer, out var tree)) return tree;
        tree = new Tree();
        var cosm = Cosmetics(root.transform);
        foreach (var slot in Slots(cosm))
        {
            if (!slot.Stackable) continue;
            var items = new List<(Transform, string)>();
            foreach (var it in Items(slot)) items.Add((it, it.name));
            tree.Stackable.Add((slot, items));
        }
        foreach (var m in Movables(cosm)) tree.Movables.Add((m.t, m.path));
        if (cosm != null) _trees[root.Pointer] = tree; // not built yet: walk again next time
        return tree;
    }

    private bool LocalStacked(string slotPath, string item) => StackOf(slotPath).Contains(item);

    /// <summary>Keeps every stacked item switched on (the game switches a slot's other items off when it fits one), and
    /// switches off what we switched on once it leaves its stack. <paramref name="stacked"/>: whether (slot path, item
    /// name) is stacked, from your config or another player's layout.</summary>
    private void EnforceStacks(GameObject root, Func<string, string, bool> stacked)
    {
        foreach (var (slot, items) in TreeOf(root).Stackable)
        {
            if (slot.T == null) { _trees.Remove(root.Pointer); return; }
            bool any = false;
            foreach (var (it, name) in items)
            {
                if (it == null) continue;
                if (stacked(slot.Path, name))
                {
                    any = true;
                    if (!it.gameObject.activeSelf) { it.gameObject.SetActive(true); _onByUs.Add(it.Pointer); }
                }
                else if (_onByUs.Remove(it.Pointer) && it.gameObject.activeSelf) it.gameObject.SetActive(false);
            }
            // a stack in an empty slot: switch the slot (and a sub-slot's parent slot) on too
            for (var t = slot.T; any && t != null && !t.name.EndsWith("_CosmeticParts", StringComparison.Ordinal); t = t.parent)
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        }
    }

    /// <summary>The fit each object last got (so unchanged ones aren't touched: their swinging parts keep swinging).</summary>
    private readonly Dictionary<IntPtr, string> _appliedSig = new();

    /// <summary>Your fit for an item: bound only for items that are shown or already have a saved fit (keeps the config
    /// file small); null for the rest.</summary>
    private AccessoryLayout.FitValues LocalFit(string path, Transform it)
    {
        if (!_fits.TryGetValue(path, out var f))
        {
            if (!it.gameObject.activeInHierarchy && !HasSaved(path)) return null;
            f = FitOf(path);
        }
        return f.Values();
    }

    /// <summary>Moves, turns and sizes every item by its fit (<paramref name="fitOf"/>: path and item to the fit, from
    /// your config or another player's layout; null leaves the item alone).</summary>
    private void ApplyFits(GameObject root, Func<string, Transform, AccessoryLayout.FitValues> fitOf)
    {
        var rt = root.transform;
        foreach (var m in TreeOf(root).Movables)
        {
            var it = m.t;
            if (it == null) continue;
            if (!_bases.TryGetValue(it.Pointer, out var b)) continue;
            var f = fitOf(m.path, it);
            if (f == null) continue;
            string sig = f.Signature;
            if (_appliedSig.TryGetValue(it.Pointer, out var last) ? last == sig : f.IsDefault) continue;
            _appliedSig[it.Pointer] = sig;
            var v = f.V;
            var off = new Vector3(v[0], v[1], v[2]);
            it.localPosition = b.Pos + it.parent.InverseTransformVector(rt.TransformVector(off));
            // turn about the car's axes: car-space rotation conjugated into the item's parent space
            var q = Quaternion.Euler(v[4], v[3], v[5]);
            var toCar = Quaternion.Inverse(it.parent.rotation) * rt.rotation;
            it.localRotation = toCar * q * Quaternion.Inverse(toCar) * b.Rot;
            it.localScale = Vector3.Scale(b.Scale, new Vector3(v[7], v[8], v[9]) * v[6]);
            FollowJoints(it);
        }
    }

    /// <summary>
    /// Parts that hang or swing (shackles, whips) sit on physics joints whose far end was fixed where the item first
    /// was: re-anchor every joint in the moved item to where its pivot is now, and put its body there.
    /// </summary>
    private void FollowJoints(Transform it)
    {
        try
        {
            foreach (var j in it.GetComponentsInChildren<Joint>(true))
            {
                if (j == null) continue;
                var world = j.transform.TransformPoint(j.anchor);
                j.autoConfigureConnectedAnchor = false;
                j.connectedAnchor = j.connectedBody != null ? j.connectedBody.transform.InverseTransformPoint(world) : world;
                var rb = j.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.position = j.transform.position;
                    rb.rotation = j.transform.rotation;
                    if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                }
            }
        }
        catch (Exception e) { Info("joints: " + e.Message); }
    }

    private readonly HashSet<string> _dumped = new(StringComparer.Ordinal);

    /// <summary>Logs an item's parts and their components once (the editor calls it on the item it shows), so parts
    /// that don't follow a move can be told apart: joints, skinned meshes with outside bones, world-space lines.</summary>
    internal void Describe(Transform it, string path)
    {
        if (it == null || !_dumped.Add(path)) return;
        try
        {
            var sb = new System.Text.StringBuilder($"parts of {path}:");
            Walk(it, it, 0, sb);
            Info(sb.ToString());
        }
        catch (Exception e) { Info("describe failed: " + e.Message); }
    }

    private static void Walk(Transform item, Transform t, int depth, System.Text.StringBuilder sb)
    {
        var names = new List<string>();
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) continue;
            string n = c.GetIl2CppType().Name;
            if (n == "Transform") continue;
            var smr = c.TryCast<SkinnedMeshRenderer>();
            if (smr != null)
            {
                int outside = 0;
                var bones = smr.bones;
                if (bones != null)
                    foreach (var bn in bones)
                        if (bn != null && !bn.IsChildOf(item)) outside++;
                n += $"(root bone {(smr.rootBone == null ? "none" : smr.rootBone.name)}, {outside} bone(s) outside the item)";
            }
            var joint = c.TryCast<Joint>();
            if (joint != null) n += $"(to {(joint.connectedBody == null ? "world" : joint.connectedBody.name)})";
            var line = c.TryCast<LineRenderer>();
            if (line != null) n += line.useWorldSpace ? "(world space)" : "(local)";
            names.Add(n);
        }
        sb.Append('\n').Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " [off]")
          .Append(names.Count > 0 ? ": " + string.Join(", ", names) : "");
        for (int i = 0; i < t.childCount && depth < 6; i++) Walk(item, t.GetChild(i), depth + 1, sb);
    }

    private readonly HashSet<string> _savedKeys = new(StringComparer.Ordinal);
    private bool _savedRead;

    /// <summary>Whether the config file has a fit for this item (read once from the file's [Items] keys).</summary>
    private bool HasSaved(string path)
    {
        if (!_savedRead)
        {
            _savedRead = true;
            try
            {
                bool inItems = false;
                foreach (var line in System.IO.File.ReadAllLines(_cfg.ConfigFilePath))
                {
                    var l = line.Trim();
                    if (l.StartsWith("[")) { inItems = l == "[Items]"; continue; }
                    if (!inItems || l.StartsWith("#") || !l.Contains('=')) continue;
                    var key = l.Substring(0, l.IndexOf('=')).Trim();
                    int dot = key.LastIndexOf('.');
                    if (dot > 0) _savedKeys.Add(key.Substring(0, dot));
                }
            }
            catch { }
        }
        return _savedKeys.Contains(Key(path));
    }

    // ------------------------------------------------------------------ page

    internal static string Pretty(string name) => name.Replace('_', ' ').Trim();

    /// <summary>Your own car's cosmetic tree when you're driving this car, else null.</summary>
    internal Transform LiveTree()
    {
        var root = CarObjects.LiveCar(Def);
        return root != null ? Cosmetics(root.transform) : null;
    }

    /// <summary>The shown garage model's cosmetic tree, else null.</summary>
    internal Transform GarageTree()
    {
        for (int i = _roots.Count - 1; i >= 0; i--)
            if (_roots[i] != null && _roots[i].activeInHierarchy) return Cosmetics(_roots[i].transform);
        return null;
    }

    /// <summary>The tree the page and the panel show: your car when you're in it, else the garage model.</summary>
    internal Transform PageTree() => LiveTree() ?? GarageTree();

    /// <summary>The MODS page's line for this car: what is stacked and how many items are moved.</summary>
    public void DrawPage()
    {
        var cosm = PageTree();
        if (cosm == null) return;
        Page.Heading(Label);
        var stacked = new List<string>();
        int moved = 0;
        foreach (var slot in Slots(cosm))
            foreach (var it in Items(slot))
            {
                if (slot.Stackable && StackOf(slot.Path).Contains(it.name)) stacked.Add(Pretty(it.name));
                if (it.gameObject.activeSelf && !FitOf(slot.Path + "/" + it.name).IsDefault) moved++;
            }
        Page.Note((stacked.Count == 0 ? "Nothing stacked" : "Stacked: " + string.Join(", ", stacked)) +
                  $". {moved} item{(moved == 1 ? "" : "s")} moved.");
    }
}
