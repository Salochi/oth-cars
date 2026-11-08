using OverTheHillPlus.Cars;

namespace OverTheHillPlus.BajaBug;

/// <summary>
/// A Baja Bug (an air-cooled rear-engined beetle, lifted, the flat four out in the open in a tube basket, a small front
/// bar with rally lamps) for core's car framework. The model: "VW Beetle" by 3DDomino (Sketchfab, CC BY 4.0), made into
/// a Baja by oth-car-toolkit's profiles/bajabug_3dd_prep.py and converted with profiles/bajabug_3dd.py. Built on the Brick: the stock car
/// with the nearest wheelbase (2.36 m against the model's 2.40 m). The identity values (item id, guid, registry key, car
/// name) are part of the network protocol and the save sidecar (oth.bajabug.&lt;profile&gt;.sidecar): don't change them
/// once you have a save with the car in it.
/// </summary>
public static class BajaBugDefinition
{
    public const string Id = "bajabug";
    public const string DisplayName = "Baja Bug";
    /// <summary>Hand-picked next to the Tacoma's 0xB7E2A7A1, away from the OTH+ cars' 0xB7E2A0xx; checked against the
    /// live catalog at startup.</summary>
    public const uint ItemId = 0xB7E2A8A1;
    public const string CarName = "BajaBug";
    public const string Guid = "260415d1f99d4a279d5af8d38373bf23";
    /// <summary>2: the Brick's own wheels, the drivetrain retuned (it wouldn't pull away in High). 3: wheel colliders 0.20 m up. 4: hubs cleared for the
    /// Brick's wheels, the body collider in six convex slices. 5: the CC BY Beetle model (cut into a Baja). 6: fenderless with
    /// tube bumpers (the collider reaches them), the body collider's own lift. 7: arched wells, the engine 0.18 m forward,
    /// the wheels 0.10 m lower. 8: arches following the wheels, the bumpers and skid plate pulled in, a detailed engine. 9: the 3DDomino Beetle (fenders
    /// kept, the body 0.16 m higher over the hubs, the engine in a tube basket, a front bar with rally lamps). 10: the bay, the louvre and the underside closed
    /// and dark, greyer engine, an open stinger, the front bar mounted, the leaf springs' mounts lowered. 11: the source's inside-out
    /// pieces turned round (trim, mirror, lamps), the louvre backed, dark wells, the front spring eye under the bar. 12: reverse
    /// lamps on the basket, the hood's seams without black AO lines, the bay's walls following the body. 13: the bay's side walls up to
    /// the lid's sloped lip (the gap seen through at either side), kept whole by the decimation. 14: the bay's walls kept under
    /// the paint where it has gaps (the firewall's corners came out beside the louvre's ends). 15: the nose panel's flanks under the
    /// front fenders in body colour (the well rule had made them dark). 16: the rest of the flanks and the nose's lower lip in body
    /// colour too.</summary>
    public const int Protocol = 16;

    public static CarDefinition Create() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Description = "A lifted beetle on big tyres and long travel, the flat four out in the open in a tube " +
                      "basket and rally lamps up front. Light, nimble and happy to fly.",
        PageNote = "A Baja Bug on the Brick's skeleton. Buy it in the garage, or try it from here. " +
                   "Model: \"VW Beetle\" by 3DDomino (CC BY 4.0). " +
                   "Everyone on a server would need this exact build to see it.",
        ItemId = ItemId,
        CarName = CarName,
        FusionGuid = Guid,
        RegistryKey = "bajabug",
        Protocol = Protocol,
        DefaultDonor = "Car_Brick",
        DefaultGarageBase = "Brick",
        AssetName = "bajabug",
        // The real car's 4-speed. The Brick's 4x4 drive is kept (the framework has no rear-drive switch): open front,
        // limited-slip rear, open centre.
        GearSet = "truck4",
        Diffs = new byte[] { 1, 2, 1 },
        // the game's own wheels (the Brick's): the model's 35-inch gold spokes looked too big on it
        KeepDonorWheels = true,
        // At rest the wheels hung 0.20 m below the old model's hubs (the soft long-travel suspension; [Wheels] report of
        // 2026-11-04: FL 0.200, FR 0.206, RL 0.203, RR 0.208): lifted back. On the CC model that sat the wheels high in
        // the fenderless wells, the Brick's leaf springs inside the body under compression: half of it, so the bug rides
        // 0.10 m taller.
        WheelColliderLift = 0.10f,
        // the model has no suspension of its own (and its wheels are left out): the Brick's rigged axles move with the
        // wheels
        KeepDonorAxles = true,
        // the Brick's spring mounts (0.62 m up) came through the beetle's floor (0.46 m) and the front bar
        DonorAxleMountDrop = 0.20f,
        // the front spring's front eye sits under the front bar (bottom 0.56 m): just under it, not down in the air
        DonorAxleNoseMountDrop = 0.11f,
        // The beetle's floor pan is low and flat: the donor's lift (0.15 m over the body's bottom) let rocks sink into it.
        BodyColliderLift = 0.03f,
        // the Brick's rear spare samples our atlas (white, as on the Tacoma), and the car has nowhere to hang it
        HiddenItems = new[] { "Visual/Rear/SpareWheel" },
        // Relative to the Brick (about 1.6 t, ~50 kW): a 0.85 t car with a ~45 kW flat four, long soft travel for the
        // weight. The torque at the wheels has to stay near the Brick's: at 0.55 of it (torque 0.62 on the model's taller
        // tyres) it wouldn't pull away in High (gas only lit the brakes; Low drove). Now torque 0.85 x final drive 1.25 =
        // 1.06 of the Brick's in either range.
        Tuning = new CarTuningProfile
        {
            Mass = 0.53f, CentreOfMassDrop = 0.03f, Torque = 0.85f, Power = 0.85f, TopSpeed = 1.0f, RevRange = 1.0f, GearRatios = 1.0f, FinalDrive = 1.25f,
            SuspensionTravel = 1.25f, SpringRate = 0.62f, DamperRate = 0.7f, TyreGrip = 1.05f, SteeringLock = 1.05f, TowKg = 600f,
        },
        Paints = new[]
        {
            new PaintPreset("Faded Blue", new CarRgb(52, 80, 104)),
            new PaintPreset("Baja Orange", new CarRgb(214, 104, 30)),
            new PaintPreset("Desert Sand", new CarRgb(196, 172, 122)),
            new PaintPreset("Lime", new CarRgb(132, 176, 46)),
            new PaintPreset("Signal Red", new CarRgb(158, 28, 26)),
            new PaintPreset("Off White", new CarRgb(226, 222, 206)),
            new PaintPreset("Matte Black", new CarRgb(30, 30, 32)),
        },
        PaintCustomDefault = "#345068",
        Trims = new[]
        {
            new TrimPreset("Black", new CarRgb(40, 40, 48)),
            new TrimPreset("Dark Grey", new CarRgb(74, 76, 80)),
            TrimPreset.Body(),
        },
        TrimCustomDefault = "#282830",
        TrimWhat = "Tube bar and basket",
        TrimHint = "The front bar and the basket round the engine.",
        // the small cars' engines (none of them is a flat four; easy to change)
        EngineSounds = new[]
        {
            new EngineSound("Polyana", "Polyana (small four)"),
            new EngineSound("Tinny", "Tinny"),
            new EngineSound("Lynx", "Lynx"),
            new EngineSound("Brick", "Brick (donor)"),
        },
        DefaultEngine = 0,
        EngineHint = "Which stock car's engine your Baja Bug sounds like.",
    };
}
