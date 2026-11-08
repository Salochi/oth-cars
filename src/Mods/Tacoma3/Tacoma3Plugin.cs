using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using OverTheHillPlus.Cars;

namespace OverTheHillPlus.Tacoma3;

// After the namespace line on purpose: the game has its own global ModManager (mod.io).
using OverTheHillPlus.Core.Manager;

/// <summary>The 3rd-gen Tacoma as a new networked car, built by core's car framework (same pattern as the 4th gen).</summary>
[BepInPlugin(Guid, Name, Version)]
[BepInDependency(Core.CorePlugin.Guid, ">=1.1.0")]
// the accessory editor, when installed, loads first so the WHEELS tab can go in (the truck runs without it)
[BepInDependency(Tacoma3Accessories.EditorGuid, BepInDependency.DependencyFlags.SoftDependency)]
public class Tacoma3Plugin : BasePlugin
{
    public const string Guid = "com.nick.oth.tacoma3";
    public const string Name = "Tacoma TRD Pro (3rd gen)";
    public const string Version = "0.2.0";

    internal const string ModId = Tacoma3Definition.Id;
    internal static ManualLogSource Logger;

    public override void Load()
    {
        Logger = Log;
        ModManager.Register(new ModInfo
        {
            Id = ModId,
            Side = ModSide.Server,
            Name = Name,
            Description = "A 3rd-gen (2016-2023) Tacoma TRD Pro double cab as a new car. " + Tacoma3Definition.Credit,
            Version = Version,
            DrawSimple = Tacoma3Car.DrawPage,
        });
        if (!ModManager.IsEnabled(ModId))
        {
            Logger.LogInfo($"{Name} is switched off on the MODS page.");
            return;
        }
        try
        {
            var cfg = ModManager.ConfigFor(ModId);
            Tacoma3Wheels.Bind(cfg);
            Tacoma3Accessories.AddWheelsTab();
            string why = Tacoma3Car.Start(typeof(Tacoma3Plugin).Assembly, cfg, Logger);
            if (why == null) Logger.LogInfo($"{Name} {Version} started.");
        }
        catch (Exception e)
        {
            ModManager.MarkFailed(ModId, e.Message);
            Logger.LogError($"{Name} failed to start: {e}");
        }
    }
}

internal static class Tacoma3Car
{
    private static CarDefinition _def;
    internal static CarDefinition Def => _def ??= Tacoma3Definition.Create();

    public static void DrawPage() => ModdedCars.DrawPage(Def);

    public static string Start(System.Reflection.Assembly assets, BepInEx.Configuration.ConfigFile config, ManualLogSource log) =>
        ModdedCars.Start(Def, assets, config, log, new CarHooks());
}

/// <summary>The 3rd-gen Tacoma's WHEELS tab in the accessory editor (the OTH.Accessories mod), and its tyre size and offset
/// sent with your layout so other players see them (editor 1.1.0 or later). Kept apart so the truck loads and runs
/// without that mod (or with an older one): its types are only touched in here, inside a try.</summary>
internal static class Tacoma3Accessories
{
    public const string EditorGuid = "com.nick.oth.accessories";
    private static bool _sync;

    public static void AddWheelsTab()
    {
        if (!BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins.ContainsKey(EditorGuid)) return;
        try { Add(); }
        catch (Exception e) { Tacoma3Plugin.Logger?.LogWarning("accessory editor: " + e.Message); }
        try { Sync(); _sync = true; }
        catch (Exception e) { Tacoma3Plugin.Logger?.LogInfo("accessory editor without layout sync (1.1.0 or later has it): " + e.Message); }
    }

    /// <summary>A value another player sent for their truck (false: yours, no editor, or they sent none).</summary>
    public static bool TryRemote(UnityEngine.GameObject root, string key, out float value)
    {
        value = 0f;
        if (!_sync) return false;
        try { return Remote(root, key, out value); }
        catch (Exception e)
        {
            _sync = false;
            Tacoma3Plugin.Logger?.LogWarning("accessory editor layout sync: " + e.Message);
            return false;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Add() =>
        OverTheHillPlus.Accessories.AccessoryTabs.Add(Tacoma3Definition.Id, "WHEELS", Tacoma3WheelsTab.Draw, replacesWheels: true);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Sync()
    {
        OverTheHillPlus.Accessories.AccessorySync.Value(Tacoma3Definition.Id, "tyres", () => Tacoma3Wheels.MySize());
        OverTheHillPlus.Accessories.AccessorySync.Value(Tacoma3Definition.Id, "offset", () => Tacoma3Wheels.Offset.Value);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool Remote(UnityEngine.GameObject root, string key, out float value) =>
        OverTheHillPlus.Accessories.AccessorySync.TryRemote(Tacoma3Car.Def, root, key, out value);
}
