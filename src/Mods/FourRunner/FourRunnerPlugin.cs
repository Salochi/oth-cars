using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using OverTheHillPlus.Cars;

namespace OverTheHillPlus.FourRunner;

// After the namespace line on purpose: the game has its own global ModManager (mod.io).
using OverTheHillPlus.Core.Manager;

/// <summary>The 4th-gen 4Runner as a new networked car, built by core's car framework on the Burro (the accessory
/// editor, F8, comes with the OTH.Accessories mod).</summary>
[BepInPlugin(Guid, Name, Version)]
[BepInDependency(Core.CorePlugin.Guid, ">=1.5.3")]
public class FourRunnerPlugin : BasePlugin
{
    public const string Guid = "com.nick.oth.4runner4";
    public const string Name = "4Runner (4th gen)";
    public const string Version = "0.1.1";

    internal const string ModId = FourRunnerDefinition.Id;
    internal static ManualLogSource Logger;

    public override void Load()
    {
        Logger = Log;
        ModManager.Register(new ModInfo
        {
            Id = ModId,
            Side = ModSide.Server,
            Name = Name,
            Description = "A lifted 2006-2009 4Runner Limited as a new car. " + FourRunnerDefinition.Credit,
            Version = Version,
            DrawSimple = FourRunnerCar.DrawPage,
        });
        if (!ModManager.IsEnabled(ModId))
        {
            Logger.LogInfo($"{Name} is switched off on the MODS page.");
            return;
        }
        try
        {
            var cfg = ModManager.ConfigFor(ModId);
            string why = FourRunnerCar.Start(typeof(FourRunnerPlugin).Assembly, cfg, Logger);
            if (why == null) Logger.LogInfo($"{Name} {Version} started.");
        }
        catch (Exception e)
        {
            ModManager.MarkFailed(ModId, e.Message);
            Logger.LogError($"{Name} failed to start: {e}");
        }
    }
}

internal static class FourRunnerCar
{
    private static CarDefinition _def;
    internal static CarDefinition Def => _def ??= FourRunnerDefinition.Create();

    public static void DrawPage() => ModdedCars.DrawPage(Def);

    public static string Start(System.Reflection.Assembly assets, BepInEx.Configuration.ConfigFile config, ManualLogSource log) =>
        ModdedCars.Start(Def, assets, config, log, new CarHooks());
}
