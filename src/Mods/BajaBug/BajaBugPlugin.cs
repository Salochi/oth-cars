using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using OverTheHillPlus.Cars;

namespace OverTheHillPlus.BajaBug;

// After the namespace line on purpose: the game has its own global ModManager (mod.io).
using OverTheHillPlus.Core.Manager;

/// <summary>The Baja Bug as a new networked car, built by core's car framework on the Brick (the accessory editor, F8,
/// comes with the OTH.Accessories mod).</summary>
[BepInPlugin(Guid, Name, Version)]
[BepInDependency(Core.CorePlugin.Guid, ">=1.1.0")]
public class BajaBugPlugin : BasePlugin
{
    public const string Guid = "com.nick.oth.bajabug";
    public const string Name = "Baja Bug";
    public const string Version = "1.0.0";

    internal const string ModId = BajaBugDefinition.Id;
    internal static ManualLogSource Logger;

    public override void Load()
    {
        Logger = Log;
        ModManager.Register(new ModInfo
        {
            Id = ModId,
            Side = ModSide.Server,
            Name = Name,
            Description = "A lifted Baja Bug, built on the Brick's wheels and axles. " +
                          "Model: \"VW Beetle\" by 3DDomino (Sketchfab, CC BY 4.0), " +
                          "https://sketchfab.com/3d-models/vw-beetle-1ffa1ce092184da8bab996bb3a481cf2",
            Version = Version,
            DrawSimple = BajaBugCar.DrawPage,
        });
        if (!ModManager.IsEnabled(ModId))
        {
            Logger.LogInfo($"{Name} is switched off on the MODS page.");
            return;
        }
        try
        {
            var cfg = ModManager.ConfigFor(ModId);
            string why = BajaBugCar.Start(typeof(BajaBugPlugin).Assembly, cfg, Logger);
            if (why == null) Logger.LogInfo($"{Name} {Version} started.");
        }
        catch (Exception e)
        {
            ModManager.MarkFailed(ModId, e.Message);
            Logger.LogError($"{Name} failed to start: {e}");
        }
    }
}

internal static class BajaBugCar
{
    private static CarDefinition _def;
    internal static CarDefinition Def => _def ??= BajaBugDefinition.Create();

    public static void DrawPage() => ModdedCars.DrawPage(Def);

    public static string Start(System.Reflection.Assembly assets, BepInEx.Configuration.ConfigFile config, ManualLogSource log) =>
        ModdedCars.Start(Def, assets, config, log, new CarHooks());
}
