using OverTheHillPlus.Cars;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}

var blue = new PaintPreset("Blue Crush", new CarRgb(20, 70, 150));
var white = new PaintPreset("Ice Cap", new CarRgb(238, 238, 236));
var paints = new[] { blue, white };
var stripe = new LiveryPreset("stripe", "Stripe", "A transparent stripe.");

// The new feature must work on cars with no bundled PNG liveries at all.
var plain = CarLiveries.EntryIds(null, paints);
Check(plain.Count == 3 && plain[0] == CarLiveries.PaintId, "Paint-only cars need custom paint and every preset.");
Check(plain[1] == CarLiveries.PaintEntryId(blue.Name) && plain[2] == CarLiveries.PaintEntryId(white.Name), "Paint order follows the palette.");

// Retain existing image-livery ids and positions when adding the palette.
var bundled = CarLiveries.EntryIds(new[] { stripe });
var combined = CarLiveries.EntryIds(new[] { stripe }, paints);
Check(combined.Take(bundled.Count).SequenceEqual(bundled), "Adding paint must retain the existing livery prefix.");
Check(combined.Count == 4, "Image liveries and paint entries must coexist.");
Check(CarLiveries.Check(new[] { stripe }) == null, "Normal bundled liveries must remain valid.");
Check(CarLiveries.Check(new[] { new LiveryPreset(plain[1], "Collision", "") }) != null, "Bundled liveries must not claim generated paint ids.");

// Names can contain punctuation and Unicode; ids still fit the game's item-id rules.
var reversed = CarLiveries.EntryIds(null, paints.Reverse().ToArray());
Check(reversed[2] == plain[1], "Reordering paints must not change a paint's persistent item identity.");
Check(CarLiveries.PaintEntryId("BLUE CRUSH") == plain[1], "Changing label case must not change item identity.");
Check(CarLiveries.IsValidId(CarLiveries.PaintEntryId("Crème / owner's blue — metallic")), "Display names must produce valid ids.");
Check(CarLiveries.ItemId("tacoma", plain[1]) != CarLiveries.ItemId("bajabug", plain[1]), "Each car must own its own paint items.");

// The limit on bundled image liveries must not truncate a generated palette.
var largePalette = Enumerable.Range(0, 40).Select(i => new PaintPreset($"Colour {i}", new CarRgb((byte)i, 0, 0))).ToArray();
var many = CarLiveries.EntryIds(null, largePalette);
Check(many.Count == 41 && many.Distinct().Count() == 41, "Palettes larger than 16 must retain all colours.");
var itemIds = many.Select(id => CarLiveries.ItemId("testcar", id)).ToArray();
var existing = Enumerable.Range(1, 254).Select(i => (uint)i).ToArray();
var bits = CarLiveries.PlanBits(existing, itemIds);
Check(bits[0] == 254 && bits[1] == 255 && bits.Skip(2).All(i => i == -1), "Paints must respect the 256-bit capacity.");
Check(CarLiveries.MountedEntry(bits, b => b == 255) == 1, "A paint mounted at the last usable bit must resolve.");
var allParts = existing.Concat(itemIds.Take(2)).ToArray();
Check(CarLiveries.PlanBits(allParts, itemIds).SequenceEqual(bits), "Preparing again must reuse existing paint bits.");

// A body-colour trim must follow the hovered paint; fixed/custom trims must retain their colour.
var trims = new[] { TrimPreset.Body(), new TrimPreset("Black", new CarRgb(26, 26, 28)) };
Check(CarPaint.TrimColour(trims, CarPaint.TrimBody, "#123456", white.Rgb) == white.Rgb, "Body-colour trim must follow the preview colour.");
Check(CarPaint.TrimColour(trims, "Black", "#123456", white.Rgb) == trims[1].Rgb, "Fixed trim must survive paint previews.");
Check(CarPaint.TrimColour(trims, CarPaint.Custom, "#123456", white.Rgb).ToHex() == "#123456", "Custom trim must survive paint previews.");
Check(CarPaint.MainColour(paints, CarPaint.Custom, "#123456").ToHex() == "#123456", "Custom paint must use the editable RGB colour.");

// Paint changes beneath an alpha livery must remain visible through its transparent areas.
var under = new byte[4];
CarPaint.Recolour(new byte[] { 0, 0, 0, 255 }, new byte[] { 255, 0, 0, 255 }, blue.Rgb, white.Rgb, under);
CarPaint.Overlay(under, new byte[] { 255, 255, 255, 0 }, under);
Check(under.SequenceEqual(new byte[] { blue.Rgb.R, blue.Rgb.G, blue.Rgb.B, 255 }), "A transparent livery must preserve the selected paint.");

Console.WriteLine($"Passed {checks} garage paint checks.");
