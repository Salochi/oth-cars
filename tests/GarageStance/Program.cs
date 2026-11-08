using OverTheHillPlus.Cars;

static void Near(float actual, float expected, string message)
{
    if (!float.IsFinite(actual) || MathF.Abs(actual - expected) > 0.00001f)
        throw new Exception($"{message}: expected {expected}, got {actual}");
}

// A 400 kg corner on a 40 kN/m spring compresses 98.1 mm. VPP's midpoint anchor leaves 225 mm of
// downward travel on a 450 mm suspension. The static wheel centre is 126.9 mm below its reference.
float stock = CarDisplayStance.Extension(0.45f, 0.5f, 0f, 40000f, 400f);
Near(stock, 0.1269f, "Loaded neutral pose");
// The game's lift increases travel; only the anchor's downward fraction changes the parked ride height.
float lifted = CarDisplayStance.Extension(0.65f, 0.5f, 0f, 40000f, 400f);
Near(lifted - stock, 0.1f, "Lift upgrade follows VPP's suspension anchor");
float anchored = CarDisplayStance.Extension(0.65f, 0.8f, 0f, 40000f, 400f);
Near(anchored, 0.4219f, "Non-midpoint anchors");
// Keeping spring stiffness proportional to weight preserves sag (the custom car tuning contract).
Near(CarDisplayStance.Extension(0.45f, 0.5f, 0f, 80000f, 800f), stock, "Mass/spring tuning preserves sag");
Near(CarDisplayStance.Extension(0.45f, 0.5f, 0.03f, 40000f, 400f), stock + 0.03f, "Spring preload");
Near(CarDisplayStance.Extension(0.45f, 0.5f, 0f, 40000f, 0f), 0.225f, "Unloaded extension limit");
Near(CarDisplayStance.Extension(0.45f, 0.5f, 0f, 40000f, 10000f), -0.225f, "Compression limit");

// Reproduce the user's lifted truck on 35-inch tyres, then switch tyres and remove the lift. Wheel bottoms
// must remain on the floor; the body-to-hub gap must keep the suspension upgrade regardless of tyre changes.
float referenceRadius = 0.395f;
foreach (float radius in new[] { 0.4445f, 0.4191f, referenceRadius, 0.4445f })
{
    var pose = CarDisplayStance.Offsets(radius - referenceRadius, lifted - stock);
    Near(referenceRadius + pose.axles - radius, 0f, "Tyres remain grounded");
    Near(pose.body - pose.axles, 0.1f, "Tyre changes preserve suspension lift");
}
var reset = CarDisplayStance.Offsets(0f, 0f);
Near(reset.body, 0f, "Removing upgrades restores body baseline");
Near(reset.axles, 0f, "Removing upgrades restores axle baseline");
var invalid = CarDisplayStance.Offsets(float.NaN, float.PositiveInfinity);
Near(invalid.body, 0f, "Invalid offsets cannot corrupt transforms");
Console.WriteLine("Garage stance regressions passed: suspension anchors, spring load, tyre contact, upgrade changes, reset and invalid input.");
