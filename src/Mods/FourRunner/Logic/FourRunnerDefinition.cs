using OverTheHillPlus.Cars;

namespace OverTheHillPlus.FourRunner;

/// <summary>
/// A 4th-gen (2003-2009) 4Runner, the 2006-2009 facelift, lifted on mud-terrain tyres, for core's car framework. The
/// model: "Toyota 4Runner" by sadiqminhas (Sketchfab, CC BY 4.0), prepared by oth-car-toolkit's profiles/4runner4_prep.py
/// and converted with profiles/4runner4.py. Built on the Burro like the Tacomas (the 4Runner shares the Tacoma's frame),
/// on the model's own wheels and the Burro's rigged axles; anchors from the Hilo 22. The identity values (item id, guid,
/// registry key, car name) are part of the network protocol and the save sidecar (oth.4runner4.&lt;profile&gt;.sidecar):
/// don't change them once you have a save with the car in it.
/// </summary>
public static class FourRunnerDefinition
{
    public const string Id = "4runner4";
    public const string DisplayName = "4Runner (4th gen)";
    /// <summary>Hand-picked next to the Baja Bug's 0xB7E2A8A1, away from the OTH+ cars' 0xB7E2A0xx; checked against
    /// the live catalog at startup.</summary>
    public const uint ItemId = 0xB7E2A9A1;
    public const string CarName = "FourRunner4";
    public const string Guid = "5d259d7cbee744c4b7b61e4877554dbc";
    public const int Protocol = 2;

    /// <summary>The model's credit (CC BY 4.0 asks for it wherever the car goes).</summary>
    public const string Credit = "Model: \"Toyota 4Runner\" by sadiqminhas (Sketchfab), CC BY 4.0, " +
                                 "https://sketchfab.com/3d-models/toyota-4runner-3afb8daa0a4a4bfea99e1af74eca1bdb, prepared and converted.";

    public static CarDefinition Create() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Description = "A 2006-2009 4Runner Limited, lifted on 33\" mud-terrains: 4.7 V8, full-time four-wheel drive, " +
                      "running boards and a roof rack.",
        PageNote = "A 4th-gen 4Runner on the Burro's skeleton. Buy it in the garage, or try it from here. Everyone on a " +
                   "server needs this mod to see it. " + Credit,
        ItemId = ItemId,
        CarName = CarName,
        FusionGuid = Guid,
        RegistryKey = "4runner4",
        Protocol = Protocol,
        DefaultDonor = "Car_Burro",
        DefaultGarageBase = "Burro",
        AssetName = "4runner4",
        GearSet = "close5",
        Diffs = new byte[] { 1, 1, 3 },
        // the model's own axles are left out (they ride up into its closed belly and skid): the Burro's rigged axles move
        // with the wheels
        KeepDonorAxles = true,
        // the Burro's mudflaps sit where its own arches are; its rear spare samples our atlas and shows white (the
        // 4Runner's spare hangs under the floor anyway)
        HiddenItems = new[]
        {
            "Visual/Fender/Mudflap_FL", "Visual/Fender/Mudflap_FR", "Visual/Fender/Mudflap_RL", "Visual/Fender/Mudflap_RR",
            "Visual/Rear/SpareWheel",
        },
        Cosmetics = CarCosmetics.Build(CarCosmetics.SlotAnchors, CarCosmetics.ItemAnchors),
        // First pass, next to the 3rd-gen Tacoma's (same donor, about the same tyres): a little heavier (about 2.2 t), the
        // V8's extra torque, a bit less travel than the TRD Pro. Easy to change after a drive.
        Tuning = new CarTuningProfile
        {
            Mass = 0.9f, CentreOfMassDrop = 0.02f, Torque = 1.12f, Power = 0.92f, TopSpeed = 1.4f, RevRange = 1.0f, GearRatios = 0.62f, FinalDrive = 1.0f,
            SuspensionTravel = 1.08f, SpringRate = 0.95f, DamperRate = 0.9f, TyreGrip = 1.05f, SteeringLock = 0.95f, TowKg = 3300f,
        },
        // the 2006-2009 colours
        Paints = new[]
        {
            new PaintPreset("Galactic Gray", new CarRgb(84, 88, 92)),
            new PaintPreset("Salsa Red", new CarRgb(122, 20, 26)),
            new PaintPreset("Shoreline Blue", new CarRgb(44, 70, 96)),
            new PaintPreset("Driftwood", new CarRgb(150, 136, 112)),
            new PaintPreset("Titanium", new CarRgb(160, 160, 156)),
            new PaintPreset("Natural White", new CarRgb(236, 236, 232)),
            new PaintPreset("Black", new CarRgb(18, 18, 20)),
            new PaintPreset("Army Green", new CarRgb(74, 84, 62)),
        },
        PaintCustomDefault = "#54585C",
        Trims = new[]
        {
            new TrimPreset("Black", new CarRgb(26, 26, 28)),
            new TrimPreset("Dark Grey", new CarRgb(60, 62, 66)),
            TrimPreset.Body(),
        },
        TrimCustomDefault = "#1A1A1C",
        TrimWhat = "Black plastic (grille, roof rack, running boards)",
        TrimHint = "The black plastic: grille, lower grille, roof rack and running boards.",
        EngineSounds = new[]
        {
            new EngineSound("Burro", "Burro (pickup)"),
            new EngineSound("Cumbre", "Cumbre (V6)"),
            new EngineSound("Protector", "Protector (utility four)"),
            new EngineSound("TrailBurner", "TrailBurner"),
        },
        DefaultEngine = 1,
        EngineHint = "Which stock car's engine your 4Runner sounds like.",
    };
}
