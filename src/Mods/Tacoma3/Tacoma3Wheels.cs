using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;
using OverTheHillPlus.Cars;
using Object = UnityEngine.Object;

namespace OverTheHillPlus.Tacoma3;

/// <summary>
/// Tyre size and wheel offset for the 3rd-gen Tacoma, applied live (no restart):
/// <list type="bullet">
/// <item>Size: stock (the model's ~32"), 33" or 35". The live car's wheel colliders take the new radius (VPP then rides
///   the truck higher by the difference, gearing gets taller like real big tyres) and the drawn wheels (rim and tyre,
///   under Axles/WheelMeshes) scale with it. The garage model, which has no physics, is lifted by the difference.</item>
/// <item>Offset: the wheels move outward (or in) by the same amount on both sides: the colliders too, so the track the
///   physics drives on is the track you see.</item>
/// </list>
/// Your own truck and the garage model get your choice. Other players' trucks get the size they picked in their F8
/// panel and their offset, when they run the accessory editor 1.1.0 or later (sent with their layout), else the size
/// mounted in their garage and no offset. A picked size never goes above the one mounted (bought).
/// </summary>
internal static class Tacoma3Wheels
{
    /// <summary>Tyre diameters in inches; 0 = the model's own tyre.</summary>
    internal static readonly int[] Sizes = { 0, 33, 35 };
    internal static readonly string[] SizeLabels = { "Stock (32\")", "33\"", "35\"" };
    private static readonly string[] Corners = { "FL", "FR", "RL", "RR" };

    /// <summary>The F8 panel's size: -1 follows the garage upgrades (Tyre Size), else an index into <see cref="Sizes"/>.</summary>
    internal static ConfigEntry<int> Size;
    internal static ConfigEntry<float> Offset;
    private static float _nextRemote;
    private static readonly List<GameObject> Remote = new();

    private sealed class Base
    {
        public Vector3 Pos, Scale;
        public float Radius, Inertia;
    }

    private static readonly Dictionary<IntPtr, Base> Bases = new();
    private static float _modelRadius = 0.406f, _next;
    private static bool _catalogLogged;

    public static void Bind(ConfigFile cfg)
    {
        Size = cfg.Bind("Wheels", "TyreOverride", -1, "Tyre size: -1 = the largest you own, 0 = stock (~32 inch), 1 = 33 inch, 2 = 35 inch (bought in the garage: Upgrades > Tyre Size)");
        Offset = cfg.Bind("Wheels", "Offset", 0f, "Wheel offset in metres, outward on both sides (negative: inward)");
        Features.Registry.OnTick(Tick, Tacoma3Definition.Id);
    }

    /// <summary>The tyre radius for the chosen size (metres).</summary>
    private static float Radius(int size)
    {
        int inch = Sizes[Math.Clamp(size, 0, Sizes.Length - 1)];
        return inch == 0 ? _modelRadius : inch * 0.0254f * 0.5f;
    }

    /// <summary>The garage upgrade mounted on a car: 0 none, 1 = 33 inch, 2 = 35 inch.</summary>
    internal static int Mounted(GameObject root)
    {
        foreach (var car in Cars.ModdedCars.All)
        {
            if (car?.Def?.Id != Tacoma3Definition.Id || car.Extras == null) continue;
            if (car.Extras.IsMounted(root, Tacoma3Definition.Tyres35)) return 2;
            if (car.Extras.IsMounted(root, Tacoma3Definition.Tyres33)) return 1;
        }
        return 0;
    }

    /// <summary>Whether you can fit a size: stock always, 33 / 35 once bought in the garage (Upgrades > Tyre Size). The
    /// garage track is a progression (only the highest owned stage counts as mounted), so the shop unlocks sizes and the
    /// F8 panel picks among them.</summary>
    internal static bool Owned(int size)
    {
        if (size <= 0) return true;
        var car = Tacoma();
        return car?.Extras != null && car.Extras.IsOwned(size == 1 ? Tacoma3Definition.Tyres33 : Tacoma3Definition.Tyres35);
    }

    /// <summary>The largest size you own.</summary>
    internal static int Largest() => Owned(2) ? 2 : Owned(1) ? 1 : 0;

    /// <summary>The size for your own truck or the garage model: the panel's pick when you own it, else the largest owned.</summary>
    private static int Mine(GameObject root) => Size.Value >= 0 && Owned(Size.Value) ? Math.Clamp(Size.Value, 0, Sizes.Length - 1) : Largest();

    /// <summary>The size on your own truck (what the accessory editor sends to the other players).</summary>
    internal static int MySize() => Mine(null);

    public const float MinOffset = -0.10f, MaxOffset = 0.25f;

    /// <summary>Another player's truck: their F8 pick when they sent one (never above what's mounted), else what's mounted.</summary>
    private static int TheirSize(GameObject root)
    {
        int mounted = Mounted(root);
        if (!Tacoma3Accessories.TryRemote(root, "tyres", out float picked) || !float.IsFinite(picked)) return mounted;
        return Math.Clamp((int)Math.Round(picked), 0, mounted);
    }

    private static float TheirOffset(GameObject root) =>
        Tacoma3Accessories.TryRemote(root, "offset", out float off) && float.IsFinite(off) ? Math.Clamp(off, MinOffset, MaxOffset) : 0f;

    internal static string Describe(GameObject root)
    {
        int s = root == null ? Math.Max(0, Size.Value) : Mine(root);
        float r = Radius(s);
        return $"{SizeLabels[s]} (radius {r:0.000} m, x{r / _modelRadius:0.00}), offset {Offset.Value * 100f:+0;-0;0} cm";
    }

    /// <summary>Your own truck and the garage models: what the size and offset go on.</summary>
    private static IEnumerable<GameObject> Mine()
    {
        foreach (var m in CarObjects.ModelsOf(Tacoma3Definition.Id)) yield return m;
        var live = CarObjects.LiveCar(Tacoma3Car.Def);
        if (live != null) yield return live;
    }

    private static void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.25f;
        try
        {
            // four times a second: cheap (four wheels), and it puts the size back if the game resets a radius
            var mine = new HashSet<IntPtr>();
            foreach (var root in Mine())
            {
                if (root == null) continue;
                mine.Add(root.Pointer);
                Apply(root.transform, Mine(root), Offset.Value);
                if (!_catalogLogged) { _catalogLogged = true; LogCatalog(); }
            }
            // other players' 3rd-gen Tacomas: the size and offset they sent with their layout, else the size they mounted
            if (Time.unscaledTime >= _nextRemote)
            {
                _nextRemote = Time.unscaledTime + 1f;
                Remote.Clear();
                // the session's cars as core read them from the network runner (a scene-wide scan here hitched ~9 ms)
                foreach (var car in CarObjects.SessionCars(Tacoma3Car.Def))
                    if (!mine.Contains(car.Pointer)) Remote.Add(car);
                foreach (var model in CarDisplays.RemoteModelsOf(Tacoma3Definition.Id)) Remote.Add(model);
            }
            foreach (var r in Remote)
                if (r != null && !mine.Contains(r.Pointer)) Apply(r.transform, TheirSize(r), TheirOffset(r));
        }
        catch (Exception e) { Tacoma3Plugin.Logger?.LogWarning("[Wheels] " + e.Message); }
    }

    private static void Apply(Transform rt, int size, float off)
    {
        var layout = Tacoma()?.Layout;
        if (layout != null && layout.TyreRadius > 0.2f) _modelRadius = layout.TyreRadius;
        var meshes = rt.Find("Axles/WheelMeshes");
        var colliders = rt.Find("Axles/WheelColliders");
        if (meshes == null) return;
        float r = Radius(size), k = r / _modelRadius;
        // the off-road set for the bigger sizes: drawn at its true size, so no scaling, and the physics radius is the
        // tyre's own; when the model lacks it, the stock wheel scaled as before
        if (SwapSet(rt.gameObject, size, out float trueR)) { r = trueR; k = 1f; }
        bool live = colliders != null && colliders.GetComponentInChildren<VehiclePhysics.VPFunselektor3DRaycastWheelCollider>(true) != null;
        for (int i = 0; i < 4; i++)
        {
            float outward = i % 2 == 0 ? -1f : 1f; // FL, RL on the left (x < 0)
            var wm = meshes.Find(Corners[i]);
            if (wm != null)
            {
                var b = BaseOf(wm, null);
                wm.localScale = b.Scale * k;
                if (!live) wm.localPosition = b.Pos + wm.parent.InverseTransformVector(rt.TransformVector(new Vector3(outward * off, 0f, 0f)));
            }
            if (!live) continue;
            var wct = colliders.Find("Wheel" + Corners[i]);
            var wc = wct == null ? null : wct.GetComponent<VehiclePhysics.VPFunselektor3DRaycastWheelCollider>();
            if (wc == null) continue;
            var bc = BaseOf(wct, wc);
            wct.localPosition = bc.Pos + wct.parent.InverseTransformVector(rt.TransformVector(new Vector3(outward * off, 0f, 0f)));
            float want = r;
            if (Mathf.Abs(wc.radius - want) > 0.0005f) wc.radius = want;
            float inertia = bc.Inertia * k * k; // a bigger, heavier wheel spins up slower
            if (Mathf.Abs(wc.inertia - inertia) > 0.001f) wc.inertia = inertia;
        }
        if (!live)
        {
            CarDisplays.SetTyreLift(rt.gameObject, r - _modelRadius);
        }
    }

    /// <summary>The wheel set each car shows now (root pointer to size), so the swap only runs on a change.</summary>
    private static readonly Dictionary<IntPtr, int> Shown = new();
    private static readonly Dictionary<int, float> TrueRadius = new();
    private static readonly HashSet<string> SwapLogged = new();

    /// <summary>Puts the off-road rim and the size's tyre on (size 1 or 2), or the stock wheels back (0). True when
    /// the off-road set is on, with its tyre's radius.</summary>
    private static bool SwapSet(GameObject root, int size, out float radius)
    {
        radius = 0f;
        var car = Tacoma();
        if (car == null) return false;
        string tyre = size == 1 ? "tyre_ot33" : size == 2 ? "tyre_ot35" : null;
        if (tyre != null && !TrueRadius.TryGetValue(size, out radius))
        {
            var part = car.Assets?.Blob?.Part(tyre);
            radius = part == null ? 0f : Cars.CarWheelFit.TyreRadius(part);
            TrueRadius[size] = radius;
        }
        if (tyre != null && radius < 0.3f) return false;   // not in this model
        if (!Shown.TryGetValue(root.Pointer, out int was) || was != size)
        {
            string why = car.Body.SetWheelSet(root, tyre == null ? null : "wheel_ot", tyre);
            if (why != null)
            {
                if (SwapLogged.Add(why)) Tacoma3Plugin.Logger?.LogWarning("[Wheels] wheel set: " + why);
                return false;
            }
            Shown[root.Pointer] = size;
            Tacoma3Plugin.Logger?.LogInfo($"[Wheels] {root.name}: {(tyre == null ? "stock wheels" : $"off-road wheels, {tyre} (radius {radius:0.000} m)")}.");
        }
        return tyre != null;
    }

    private static Cars.RuntimeCar Tacoma()
    {
        foreach (var c in Cars.ModdedCars.All)
            if (c?.Def?.Id == Tacoma3Definition.Id) return c;
        return null;
    }

    private static Base BaseOf(Transform t, VehiclePhysics.VPFunselektor3DRaycastWheelCollider wc)
    {
        if (Bases.TryGetValue(t.Pointer, out var b)) return b;
        b = new Base { Pos = t.localPosition, Scale = t.localScale, Radius = wc != null ? wc.radius : 0f, Inertia = wc != null ? wc.inertia : 0f };
        Bases[t.Pointer] = b;
        return b;
    }

    /// <summary>
    /// Once: the 3rd-gen Tacoma's garage catalog (every part on its list: type, upgrade type, price, effects), to design the
    /// tyre-size garage upgrades against what the game's Upgrades screen groups and how.
    /// </summary>
    private static void LogCatalog()
    {
        try
        {
            foreach (var car in Cars.ModdedCars.All)
            {
                if (car?.Def?.Id != Tacoma3Definition.Id) continue;
                var parts = car.Catalog?.Info?.UpgradesLists?.Parts;
                if (parts == null) { Tacoma3Plugin.Logger?.LogInfo("[Wheels] catalog: no part list."); return; }
                var sb = new StringBuilder($"[Wheels] catalog: {parts.Count} part(s)");
                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i];
                    if (p == null) continue;
                    var up = p.TryCast<VehicleUpgrade>();
                    string type = up != null ? "upgrade " + up.UpgradeType : p.Type.ToString();
                    var effects = new List<string>();
                    if (p.Effects != null)
                        foreach (var e in p.Effects)
                            if (e != null) effects.Add(e.GetIl2CppType().Name);
                    sb.Append($"\n  {p.name} (0x{p.ItemId:X8}): {type}, price {p.Price}" +
                              (effects.Count > 0 ? ", effects " + string.Join("+", effects) : ""));
                }
                Tacoma3Plugin.Logger?.LogInfo(sb.ToString());
            }
        }
        catch (Exception e) { Tacoma3Plugin.Logger?.LogWarning("[Wheels] catalog log failed: " + e.Message); }
    }
}
