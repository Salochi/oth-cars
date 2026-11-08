using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using OverTheHillPlus.Api;
using OverTheHillPlus.Cars;
using UnityEngine;

namespace OverTheHillPlus.Accessories;

// After the namespace line on purpose: the game has its own global ModManager (mod.io).
using OverTheHillPlus.Core.Manager;

/// <summary>
/// The accessory editor (F8) for every car of core's car framework: stack extra accessories per slot, move, turn and
/// size each one, and set the wheels' size and offset, in a panel at the side of the screen. Each car's settings go in
/// its own mod's config file ([Stack], [Items], [Wheels]), and sent to the other players over the mod network
/// (<see cref="AccessoryNet"/>): everyone with the editor sees everyone's layout. Nobody needs it to join.
/// A car with tabs of its own adds them through <see cref="AccessoryTabs"/> (a soft dependency: the car runs without
/// this mod).
/// </summary>
[BepInPlugin(Guid, Name, Version)]
[BepInDependency(Core.CorePlugin.Guid, ">=1.5.0")]
public class AccessoriesPlugin : BasePlugin
{
    public const string Guid = "com.nick.oth.accessories";
    public const string Name = "Accessory Editor";
    public const string Version = "1.1.1";
    internal const string ModId = "accessories";
    internal static ManualLogSource Logger;

    private static readonly Dictionary<string, CarAccessories> Cars = new(StringComparer.OrdinalIgnoreCase);

    public override void Load()
    {
        Logger = Log;
        ModManager.Register(new ModInfo
        {
            Id = ModId,
            Side = ModSide.Client,
            Name = Name,
            Description = "Stack extra accessories, move, turn and size each one, and set the wheel size and offset of the " +
                          "modded cars, in a panel at the side of the screen (" + AccessoryEditor.KeyName + "). Other players with the " +
                          "editor see your changes, and you see theirs.",
            Version = Version,
            DrawSimple = DrawPage,
        });
        if (!ModManager.IsEnabled(ModId))
        {
            Logger.LogInfo($"{Name} is switched off on the MODS page.");
            return;
        }
        CarObjects.BodyBuilt += OnBody;
        Features.Registry.OnTick(EnsureCars, ModId);
        AccessoryNet.Register();
        Logger.LogInfo($"{Name} {Version} started.");
    }

    /// <summary>The editor's data for a car, made the first time the car is seen.</summary>
    internal static CarAccessories For(CarDefinition def)
    {
        if (def == null) return null;
        if (Cars.TryGetValue(def.Id, out var a)) return a;
        var cfg = ModManager.ConfigFor(def.Id);
        if (cfg == null) return null;
        a = new CarAccessories(def, cfg, Logger, wheelsTab: !AccessoryTabs.OwnWheels.Contains(def.Id));
        if (AccessoryTabs.Extra.TryGetValue(def.Id, out var tabs))
            foreach (var t in tabs) a.Tabs.Add(t);
        Cars[def.Id] = a;
        Logger.LogInfo($"editor ready for {def.DisplayName} ({def.Id}).");
        return a;
    }

    internal static bool TryGet(string carId, out CarAccessories a) => Cars.TryGetValue(carId, out a);

    internal static IEnumerable<CarAccessories> All => Cars.Values;
    internal static bool AnyCar => Cars.Count > 0;

    private static void OnBody(RuntimeCar car, GameObject root, bool template)
    {
        For(car?.Def)?.AfterBody(root, template);
    }

    private static float _nextCars;

    /// <summary>Cars that started before this mod (or whose body was built before it) still get the editor.</summary>
    private static void EnsureCars()
    {
        if (Time.unscaledTime < _nextCars) return;
        _nextCars = Time.unscaledTime + 2f;
        try { foreach (var def in CarObjects.Definitions()) For(def); }
        catch (Exception e) { Logger.LogWarning("cars: " + e.Message); }
    }

    private static void DrawPage()
    {
        Page.Heading("Accessory editor");
        Page.Note("Stack extra accessories and move, turn or size each one in a small panel at the side of the screen, so " +
                  "you can watch the car while you work. Works on every modded car. In multiplayer, other players with the " +
                  "editor (and the Mod network on) see your changes, and you see theirs.");
        Page.Row.Button("Open the accessory editor", AccessoryEditor.KeyName, "Closes this page and opens the editor. " +
                        AccessoryEditor.KeyName + " opens and closes it any time.", AccessoryEditor.OpenFromPage);
        var (car, _) = AccessoryEditor.Current();
        if (car == null) { Page.Note("Get in a modded car, or open one in the garage, to use it."); return; }
        car.DrawPage();
    }
}

/// <summary>
/// For a car mod with editor tabs of its own (the Tacoma's WHEELS tab with its garage tyre sizes). Call from the car
/// plugin's Load, before the car starts. Draw a tab with <see cref="AccessoryEditor"/>'s public helpers.
/// </summary>
public static class AccessoryTabs
{
    internal static readonly Dictionary<string, List<(string Title, Action Draw)>> Extra = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly HashSet<string> OwnWheels = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="replacesWheels">The tab is the car's wheels tab: the standard one (size, offset) stays out.</param>
    public static void Add(string carId, string title, Action draw, bool replacesWheels = false)
    {
        if (!Extra.TryGetValue(carId, out var list)) Extra[carId] = list = new List<(string, Action)>();
        list.Add((title, draw));
        if (replacesWheels) OwnWheels.Add(carId);
        if (AccessoriesPlugin.TryGet(carId, out var made))
        {
            if (replacesWheels) made.DropStandardWheels();
            made.Tabs.Add((title, draw));
        }
    }
}
