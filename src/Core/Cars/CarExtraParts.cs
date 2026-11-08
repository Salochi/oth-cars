using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OverTheHillPlus.Cars;

/// <summary>
/// A modded car's extra garage upgrades (<see cref="ExtraUpgrade"/>) as runtime parts, built like its liveries
/// (<see cref="CarLiveryParts"/>): appended to the car's upgrade list right after them (same bits on every peer with the
/// car mod), put in the item catalog with the car, kept out of the game's save with it (CarSave's own parts), and named
/// in the garage through a patch on UpgradeTypeExtensions.ToLocalizedString.
/// </summary>
internal sealed class CarExtraParts
{
    private sealed class Entry
    {
        public ExtraUpgrade Def;
        public VehicleUpgrade Part;
        public int Bit = -1;
    }

    private readonly RuntimeCar _car;
    private readonly List<Entry> _entries = new();
    private readonly HashSet<uint> _ids = new();

    /// <summary>Category names by upgrade type, from every car's extras.</summary>
    private static readonly Dictionary<int, string> Labels = new();
    private static Harmony _harmony;

    public CarExtraParts(RuntimeCar car)
    {
        _car = car;
        foreach (var u in car.Def.ExtraUpgrades ?? Array.Empty<ExtraUpgrade>())
        {
            if (u == null || u.ItemId == 0) continue;
            _entries.Add(new Entry { Def = u });
            _ids.Add(u.ItemId);
            if (!string.IsNullOrEmpty(u.TypeLabel)) Labels[u.Type] = u.TypeLabel;
        }
    }

    public bool Any => _entries.Count > 0;
    public IReadOnlyCollection<uint> ItemIds => _ids;

    /// <summary>Appends the parts to the car's list (after its liveries). Main thread. A line for the log.</summary>
    public string Build(CarInfo baseInfo, UpgradesLists lists)
    {
        if (!Any) return null;
        InstallLabels();
        var parts = lists.Parts;
        var made = new List<string>();
        foreach (var e in _entries)
        {
            // a second Prepare: already in
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] != null && parts[i].ItemId == e.Def.ItemId) { e.Bit = i; e.Part = parts[i].TryCast<VehicleUpgrade>(); break; }
            if (e.Part != null) continue;
            var template = Find(baseInfo, e.Def.CopyFrom) ?? Find(lists, e.Def.CopyFrom);
            if (template == null) { made.Add($"{e.Def.Id}: no {e.Def.CopyFrom} to copy"); continue; }
            if (parts.Count >= 256) { made.Add($"{e.Def.Id}: the bitset is full"); continue; }
            var p = Object.Instantiate(template);
            p.name = $"{_car.Def.CarName}_{e.Def.Id}";
            p.ItemId = e.Def.ItemId;
            p.Price = e.Def.Price;
            p.RequiredDlc = DlcId.None;
            p.UpgradeType = (UpgradeType)e.Def.Type;
            p.Effects = new Il2CppSystem.Collections.Generic.List<UpgradeEffect>();       // the car mod does the work
            p.LinkedParts = new Il2CppSystem.Collections.Generic.List<VehiclePartSO>();
            string key = _car.Def.TextKey("UPGRADE_" + e.Def.Id.ToUpperInvariant());
            p.Name = _car.Text.Extra(template.Name, key + "_NAME", e.Def.Name);
            p.Description = _car.Text.Extra(template.Description ?? template.Name, key + "_DESCRIPTION", e.Def.Description ?? e.Def.Name);
            p.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Object.DontDestroyOnLoad(p);
            parts.Add(p);
            e.Part = p;
            e.Bit = parts.Count - 1;
            made.Add($"{e.Def.Id} (bit {e.Bit}, from {template.name})");
        }
        return "extra upgrades: " + string.Join(", ", made);
    }

    private static VehicleUpgrade Find(CarInfo info, string name) => Find(info?.UpgradesLists, name);

    private static VehicleUpgrade Find(UpgradesLists lists, string name)
    {
        var parts = lists?.Parts;
        if (parts == null || string.IsNullOrEmpty(name)) return null;
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (p != null && p.name == name) return p.TryCast<VehicleUpgrade>();
        }
        return null;
    }

    // ---------------------------------------------------------------- the item catalog

    public string Clash(InventoryItemsCatalog cat)
    {
        foreach (var e in _entries)
            if (e.Part != null && cat.IdToSO.ContainsKey(e.Def.ItemId))
            {
                var have = cat.IdToSO[e.Def.ItemId];
                if (have == null || have.Pointer != e.Part.Pointer) return $"another item already uses the upgrade {e.Def.Id}'s id 0x{e.Def.ItemId:X8}";
            }
        return null;
    }

    public void AddTo(InventoryItemsCatalog cat)
    {
        foreach (var e in _entries)
        {
            if (e.Part == null) continue;
            if (cat.IdToSO.ContainsKey(e.Def.ItemId))
            {
                var have = cat.IdToSO[e.Def.ItemId];
                if (have != null && have.Pointer != e.Part.Pointer) continue;
            }
            if (cat.NameToId.ContainsKey(e.Part.name) && cat.NameToId[e.Part.name] != e.Def.ItemId) continue;
            cat.IdToSO[e.Def.ItemId] = e.Part;
            cat.IdToName[e.Def.ItemId] = e.Part.name;
            cat.NameToId[e.Part.name] = e.Def.ItemId;
        }
    }

    public void RemoveFrom(InventoryItemsCatalog cat)
    {
        foreach (var e in _entries)
        {
            if (e.Part == null || !cat.IdToSO.ContainsKey(e.Def.ItemId) || cat.IdToSO[e.Def.ItemId]?.Pointer != e.Part.Pointer) continue;
            cat.IdToSO.Remove(e.Def.ItemId);
            cat.IdToName.Remove(e.Def.ItemId);
            cat.NameToId.Remove(e.Part.name);
        }
    }

    // ---------------------------------------------------------------- mounted?

    private readonly HashSet<string> _bitLogged = new();

    /// <summary>
    /// Whether the upgrade <paramref name="id"/> is mounted on the car <paramref name="root"/>. Your own car and the
    /// garage model: your inventory (the part's state for this car). Another player's car: its mounted-parts bitset (the
    /// game may only set bits for parts with an object on the car; logged once per part when the two disagree on yours).
    /// </summary>
    public bool IsMounted(GameObject root, string id)
    {
        var e = _entries.Find(x => x.Def.Id == id);
        if (e == null || root == null) return false;
        try
        {
            var display = CarDisplays.Of(root);
            if (display != null) return display.Equipped?.IsMounted(e.Part) ?? false;
            bool? bit = null;
            var vcc = root.GetComponentInChildren<VehicleCustomizationController>(true);
            if (vcc != null && e.Bit >= 0)
            {
                try { bit = vcc.EnabledUpgrades.IsSet(e.Bit); }
                catch (Exception ex) { DebugLog.Error(_car.Id, ex, "CarExtraParts.IsMounted bitset"); }
            }
            var net = root.GetComponentInChildren<Fusion.NetworkObject>(true);
            var local = GameState.LocalCar;
            bool own = net == null || (local != null && local.transform.IsChildOf(root.transform));
            if (!own) return bit ?? false;
            bool inv = InInventory(e.Def.ItemId);
            if (net != null && bit.HasValue && bit.Value != inv && _bitLogged.Add(id + inv))
                _car.Info($"extra upgrade {id}: your inventory says {(inv ? "mounted" : "not mounted")}, the car's bitset (bit {e.Bit}) says {(bit.Value ? "set" : "not set")}.");
            return inv;
        }
        catch (Exception ex) { DebugLog.Error(_car.Id, ex, "CarExtraParts.IsMounted"); }
        return false;
    }

    /// <summary>Whether you own the upgrade <paramref name="id"/> for this car (bought, mounted or not).</summary>
    public bool IsOwned(string id)
    {
        var e = _entries.Find(x => x.Def.Id == id);
        if (e == null) return false;
        try
        {
            var inv = PlayerInventoryManager.Instance?.Inventory;
            if (inv?.Items == null) return false;
            foreach (var b in inv.Items)
            {
                if (b == null || b.ItemId != e.Def.ItemId) continue;
                var vs = b.TryCast<VehicleSpecificItem>();
                if (vs?.VehicleStates == null) continue;
                foreach (var st in vs.VehicleStates)
                    if (st != null && st.VehicleItemId == _car.Def.ItemId && st.Count > 0) return true;
            }
        }
        catch (Exception ex) { DebugLog.Error(_car.Id, ex, "CarExtraParts.IsOwned"); }
        return false;
    }

    private bool InInventory(uint itemId)
    {
        var inv = PlayerInventoryManager.Instance?.Inventory;
        if (inv?.Items == null) return false;
        foreach (var b in inv.Items)
        {
            if (b == null || b.ItemId != itemId) continue;
            var vs = b.TryCast<VehicleSpecificItem>();
            if (vs?.VehicleStates == null) continue;
            foreach (var st in vs.VehicleStates)
                if (st != null && st.VehicleItemId == _car.Def.ItemId && st.InUse) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- the category's name

    private static void InstallLabels()
    {
        if (_harmony != null || Labels.Count == 0) return;
        try
        {
            _harmony = new Harmony("com.oth.core.cars.extraupgrades");
            var target = AccessTools.Method(typeof(UpgradeTypeExtensions), nameof(UpgradeTypeExtensions.ToLocalizedString));
            _harmony.Patch(target, postfix: new HarmonyMethod(typeof(CarExtraParts), nameof(LabelPostfix)));
        }
        catch (Exception e) { Core.CorePlugin.Logger?.LogWarning("[Cars] extra upgrade names: " + e.Message); }
    }

    private static void LabelPostfix(UpgradeType type, ref string __result)
    {
        if (Labels.TryGetValue((int)type, out var label)) __result = label;
    }
}
