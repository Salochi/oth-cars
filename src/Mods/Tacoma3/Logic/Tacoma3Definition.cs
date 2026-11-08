using System.Linq;
using OverTheHillPlus.Cars;

namespace OverTheHillPlus.Tacoma3;

/// <summary>
/// 3rd-gen (2016-2023) Tacoma TRD Pro double cab, short bed, for core's car framework: a car of its own next to the 4th
/// gen (OTH.Tacoma), and the one to publish (its model is CC BY; the 4th gen's licence forbids redistribution). Built on
/// the Burro like the 4th gen, anchors from the Hilo 22. The identity values (item ids, guid, registry key, car name) are
/// part of the network protocol and the save sidecar (oth.tacoma3.&lt;profile&gt;.sidecar): don't change them once you
/// have a save with the truck in it.
/// </summary>
public static class Tacoma3Definition
{
    public const string Id = "tacoma3";
    public const string DisplayName = "Tacoma TRD Pro (3rd gen)";
    /// <summary>Hand-picked next to the 4th gen's 0xB7E2A7A1, away from the OTH+ cars' 0xB7E2A0xx; checked against the
    /// live catalog at startup.</summary>
    public const uint ItemId = 0xB7E2A7C1;
    public const string CarName = "Tacoma3";
    public const string Guid = "2c71ea87e3644a888e5bba44e34dd2d6";
    public const int Protocol = 1;

    /// <summary>The tyre-size garage upgrades (their ids never change: they are in the save sidecar).</summary>
    public const string Tyres33 = "tyres_33", Tyres35 = "tyres_35";

    /// <summary>The model's credit (CC BY 4.0 asks for it wherever the car goes).</summary>
    public const string Credit = "Model: \"Toyota Tacoma TRD\" by LagzDesign (Sketchfab), CC BY 4.0, refined and converted.";

    public static CarDefinition Create() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Description = "A 2016-2023 double-cab, short-bed off-road pickup: 3.5 V6, Fox shocks, skid plate and a short " +
                      "open bed.",
        PageNote = "A 3rd-gen double-cab off-road pickup on the Burro's skeleton. Buy it in the garage, or try it from " +
                   "here. Everyone on a server needs this mod to see it. " + Credit,
        ItemId = ItemId,
        CarName = CarName,
        FusionGuid = Guid,
        RegistryKey = "tacoma3",
        Protocol = Protocol,
        DefaultDonor = "Car_Burro",
        DefaultGarageBase = "Burro",
        AssetName = "tacoma3",
        GearSet = "close5",
        Diffs = new byte[] { 1, 1, 3 },
        HasBed = true,
        // the model has no frame or suspension under it: the Burro's rigged axles move with the wheels
        KeepDonorAxles = true,
        // the Burro's mudflaps sit where its own arches are, not the Tacoma's; the rear spare wheel samples our atlas
        // and shows white (the 4th gen, the Badger and the Marmot hide it too)
        HiddenItems = new[]
        {
            "Visual/Fender/Mudflap_FL", "Visual/Fender/Mudflap_FR", "Visual/Fender/Mudflap_RL", "Visual/Fender/Mudflap_RR",
            "Visual/Rear/SpareWheel",
        },
        // bigger tyres as their own garage track (the game's Headlight type, unused on the Burro's list), next to the
        // stock Tire_2 / Tire_3 grip upgrades; Tacoma3Wheels does the sizing
        ExtraUpgrades = new[]
        {
            new ExtraUpgrade { Id = Tyres33, ItemId = 0xB7E2A7D1, Name = "33\" All-Terrain Tyres", Price = 25, CopyFrom = "Tire_2", Type = 4, TypeLabel = "Tyre Size",
                Description = "Unlocks 33-inch tyres on beadlock wheels: about 1.5 cm more clearance, slightly taller gearing. Pick your size in the F8 panel (Wheels)." },
            new ExtraUpgrade { Id = Tyres35, ItemId = 0xB7E2A7D2, Name = "35\" Mud-Terrain Tyres", Price = 50, CopyFrom = "Tire_3", Type = 4, TypeLabel = "Tyre Size",
                Description = "Unlocks 35-inch tyres on beadlock wheels: about 4 cm more clearance, taller gearing. Pick your size in the F8 panel (Wheels)." },
        },
        Cosmetics = CarCosmetics.Build(CarCosmetics.SlotAnchors.Append(("cosm_Bed", "Visual/Bed")), CarCosmetics.ItemAnchors),
        // First pass, next to the 4th gen's: a little lighter (about 2.1 t), the V6 revs higher with less torque than the
        // 4th gen's hybrid, a bit less travel. Easy to change after a drive.
        Tuning = new CarTuningProfile
        {
            Mass = 0.85f, CentreOfMassDrop = 0.02f, Torque = 1.05f, Power = 0.9f, TopSpeed = 1.45f, RevRange = 1.1f, GearRatios = 0.62f, FinalDrive = 1.0f,
            SuspensionTravel = 1.12f, SpringRate = 0.92f, DamperRate = 0.88f, TyreGrip = 1.05f, SteeringLock = 0.95f, TowKg = 2900f,
        },
        // the TRD Pro's colours over the years
        Paints = new[]
        {
            new PaintPreset("Army Green", new CarRgb(74, 84, 62)),
            new PaintPreset("Lunar Rock", new CarRgb(150, 150, 140)),
            new PaintPreset("Electric Lime", new CarRgb(180, 200, 40)),
            new PaintPreset("Voodoo Blue", new CarRgb(20, 70, 150)),
            new PaintPreset("Cavalry Blue", new CarRgb(40, 58, 84)),
            new PaintPreset("Cement", new CarRgb(120, 124, 122)),
            new PaintPreset("Inferno", new CarRgb(200, 72, 20)),
            new PaintPreset("Super White", new CarRgb(238, 238, 236)),
            new PaintPreset("Midnight Black", new CarRgb(20, 20, 22)),
            new PaintPreset("Barcelona Red", new CarRgb(140, 18, 24)),
        },
        PaintCustomDefault = "#4A543E",
        Trims = new[]
        {
            new TrimPreset("Black", new CarRgb(26, 26, 28)),
            new TrimPreset("Dark Grey", new CarRgb(60, 62, 66)),
            TrimPreset.Body(),
        },
        TrimCustomDefault = "#1A1A1C",
        TrimWhat = "Black plastic (flares, bumpers, grille, mirrors)",
        TrimHint = "The black plastic: fender flares, bumpers, grille surround, mirrors and steps.",
        EngineSounds = new[]
        {
            new EngineSound("Burro", "Burro (pickup)"),
            new EngineSound("Cumbre", "Cumbre (V6)"),
            new EngineSound("Protector", "Protector (utility four)"),
            new EngineSound("TrailBurner", "TrailBurner"),
        },
        DefaultEngine = 1,
        EngineHint = "Which stock car's engine your Tacoma sounds like.",
    };
}
