using System;
using System.Collections.Generic;
using UnityEngine;

namespace OverTheHillPlus.Cars;

/// <summary>The actual copies drawn in the menu, lobby and shop, with the loadout and owner each copy represents.
/// Source dummies are never customized. Registration also gives the accessory editor and wheel mods these copies.</summary>
internal static class CarDisplays
{
    internal sealed class Display
    {
        public RuntimeCar Car;
        public GameObject Root;
        public LocalVehicleCustomizationController Controller;
        public Car Equipped;
        public bool Mine;
        public int Owner;
        public StateMerchantMenu Merchant;
        internal string PartsKey;
        internal Transform Parts, Axles;
        internal Vector3 PartsBase, AxlesBase;
        internal float TyreLift, SuspensionLift;
    }

    private static readonly Dictionary<int, Display> Displays = new();
    private static readonly Dictionary<int, (GameObject root, Car equipped)> LobbyLoadouts = new();
    private static MainMenuCarSpawner _menu;
    private static StateMerchantMenu _merchant;
    private static float _next;

    internal static IEnumerable<Display> All => Displays.Values;

    internal static Display Of(GameObject root) => root != null && Displays.TryGetValue(root.GetInstanceID(), out var display) ? display : null;

    internal static IEnumerable<GameObject> RemoteModelsOf(string carId)
    {
        foreach (var display in Displays.Values)
            if (!display.Mine && display.Root != null && display.Car.Id == carId) yield return display.Root;
    }

    internal static void MenuOpened(MainMenuCarSpawner menu) { _menu = menu; _next = 0f; }
    internal static void MerchantOpened(StateMerchantMenu menu) { _merchant = menu; _next = 0f; }
    internal static void LobbyCustomized(GameObject root, Car equipped)
    {
        if (root != null && equipped != null) LobbyLoadouts[root.GetInstanceID()] = (root, equipped);
        _next = 0f;
    }

    internal static void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.25f;
        var gone = new List<int>();
        foreach (var kv in Displays) if (kv.Value.Root == null) gone.Add(kv.Key);
        foreach (int id in gone) Displays.Remove(id);
        gone.Clear();
        foreach (var kv in LobbyLoadouts) if (kv.Value.root == null) gone.Add(kv.Key);
        foreach (int id in gone) LobbyLoadouts.Remove(id);

        // The menu sets these fields in a delayed coroutine, after SpawnMenuCar returns. Keep observing the spawner
        // rather than hooking the coroutine factory (whose postfix runs before any of its work).
        try
        {
            if (_menu != null && _menu.spawnedObject != null && _menu.menuCar != null)
                Observe(_menu.spawnedObject, _menu.menuCar, mine: true, owner: 0, merchant: null);
        }
        catch (Exception e) { DebugLog.Error("core", e, "CarDisplays menu"); }
        try
        {
            if (_merchant != null && _merchant.dummyChosenCarGameObject != null && _merchant.currentCar != null)
                Observe(_merchant.dummyChosenCarGameObject, _merchant.currentCar, mine: true, owner: 0, _merchant);
        }
        catch (Exception e) { DebugLog.Error("core", e, "CarDisplays shop"); }
        try
        {
            var lobby = OverlandSessionPlayers.Instance;
            var local = GameState.LocalPlayer;
            if (lobby?.playerRefToDummy != null && local.HasValue)
                foreach (var kv in lobby.playerRefToDummy)
                {
                    var root = kv.Value;
                    var controller = root == null ? null : root.GetComponentInChildren<LocalVehicleCustomizationController>(true);
                    var car = controller == null ? null : ModdedCars.ByItem(controller.currentCarInfo?.ItemId ?? 0);
                    if (car == null) continue;
                    bool mine = kv.Key == local.Value;
                    // The game's lobby already applies remote parts. Keep the Car passed to its customization method;
                    // your inventory must never be used to customize another player's copy.
                    var equipped = mine ? new Car(car.Catalog.Info, null)
                        : LobbyLoadouts.TryGetValue(root.GetInstanceID(), out var loadout) ? loadout.equipped : null;
                    if (equipped != null) Observe(root, equipped, mine, kv.Key.RawEncoded, merchant: null);
                }
        }
        catch (Exception e) { DebugLog.Error("core", e, "CarDisplays lobby"); }
    }

    private static Car OwnEquipment(RuntimeCar car) => new(car.Catalog.Info, InventoryUtility.GetAllMountedParts(car.Catalog.Info));

    private static void Observe(GameObject root, Car equipped, bool mine, int owner, StateMerchantMenu merchant)
    {
        var car = ModdedCars.ByItem(equipped?.CarInfo?.ItemId ?? 0);
        if (car == null || !car.Ready || root == null || root == car.Dummy) return;
        var controller = root.GetComponentInChildren<LocalVehicleCustomizationController>(true);
        if (controller == null || controller.totalPartsDictionary == null) return; // Awake has not initialized this copy yet
        int id = root.GetInstanceID();
        if (!Displays.TryGetValue(id, out var display))
        {
            var rt = root.transform;
            display = new Display
            {
                Car = car, Root = root, Controller = controller, Parts = rt.Find("Parts"), Axles = rt.Find("Axles"),
            };
            if (display.Parts != null) display.PartsBase = display.Parts.localPosition;
            if (display.Axles != null) display.AxlesBase = display.Axles.localPosition;
            Displays.Add(id, display);
            car.Info($"display copy {root.name}: {(mine ? "your" : "another player's")} loadout.");
        }
        display.Mine = mine;
        display.Owner = owner;
        display.Merchant = merchant;
        // The menu's Car may have been made before the sidecar restored the inventory. Read the live inventory,
        // just as spawning your driving car does. Leave shop previews under the shop's own control.
        display.Equipped = mine && merchant == null ? OwnEquipment(car) : equipped;
        if (merchant == null && mine)
        {
            string key = PartsKey(display.Equipped);
            if (display.PartsKey != key)
            {
                controller.RefreshMountedUpgrades(display.Equipped);
                display.PartsKey = key;
            }
        }
        // Capture accessory baselines after the game's normal equipment pass, before applying wheel/body offsets.
        if (mine) CarObjects.RegisterModel(car, root);
        display.SuspensionLift = SuspensionLift(car, display.Equipped);
        ApplyStance(display);
    }

    private static string PartsKey(Car equipped)
    {
        var ids = new List<uint>();
        if (equipped?.MountedParts != null)
            foreach (var part in equipped.MountedParts) if (part != null) ids.Add(part.ItemId);
        ids.Sort();
        return string.Join(",", ids);
    }

    private static float SuspensionLift(RuntimeCar car, Car equipped)
    {
        float modifier = 0f;
        if (equipped?.MountedParts != null)
            foreach (var part in equipped.MountedParts)
                if (part?.Effects != null)
                    foreach (var effect in part.Effects)
                    {
                        var lift = effect?.TryCast<SuspensionLiftUpgradeEffect>();
                        if (lift != null) modifier = lift.suspensionModifier;
                    }
        var donor = car.Catalog.Base?.suspensionSetup;
        var own = car.Catalog.Info?.suspensionSetup;
        if (donor == null || own == null) return 0f;
        float mass = car.Catalog.Base.carCategorySetup?.mass ?? 0f;
        float before = CarDisplayStance.Extension(donor.suspensionTravel, donor.suspensionAnchor,
            donor.preloadDistance, donor.springRate, mass * 0.25f);
        float after = CarDisplayStance.Extension(own.suspensionTravel + modifier, own.suspensionAnchor,
            own.preloadDistance, own.springRate, mass * car.Tuning.Profile.Mass * 0.25f);
        return after - before - car.Def.WheelColliderLift;
    }

    /// <summary>Wheel providers contribute only their radius difference. One writer composes it with suspension lift,
    /// so changing tyres cannot erase the suspension upgrade or accumulate offsets on repeated ticks.</summary>
    internal static void SetTyreLift(GameObject root, float metres)
    {
        if (root == null || !Displays.TryGetValue(root.GetInstanceID(), out var display)) return;
        display.TyreLift = metres;
        ApplyStance(display);
    }

    private static void ApplyStance(Display display)
    {
        var offsets = CarDisplayStance.Offsets(display.TyreLift, display.SuspensionLift);
        if (display.Parts != null) display.Parts.localPosition = display.PartsBase + Vector3.up * offsets.body;
        if (display.Axles != null) display.Axles.localPosition = display.AxlesBase + Vector3.up * offsets.axles;
    }
}
