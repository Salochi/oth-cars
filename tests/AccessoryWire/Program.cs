using OverTheHillPlus.Accessories;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}

AccessoryLayout.FitValues Fit(params float[] v)
{
    var f = new AccessoryLayout.FitValues();
    Array.Copy(AccessoryLayout.FitValues.Default.V, f.V, 10);
    for (int i = 0; i < v.Length; i++) f.V[i] = v[i];
    return f;
}

// A typical layout: two stacked slots, moved items, wheel values.
var l = new AccessoryLayout { CarId = "tacoma3" };
l.Stacks["Visual/Roof"] = new SortedSet<string>(StringComparer.Ordinal) { "RoofRack", "RoofTent" };
l.Stacks["Visual/Bed"] = new SortedSet<string>(StringComparer.Ordinal) { "BedLoad_A" };
l.Fits["Visual/Roof/RoofTent"] = Fit(0f, 0.12f, -0.3f, 0f, 2.5f, 0f, 1f, 1.1f, 1f, 1f);
l.Fits["Visual/Front/Bullbar"] = Fit(0f, 0f, 0.05f);
l.Values["wheels.size"] = 1.1f;
l.Values["car.offset"] = 0.04f;

var bytes = l.Encode(out int dropped);
Check(dropped == 0 && bytes.Length > 0 && bytes.Length <= AccessoryLayout.MaxBytes, "A normal layout encodes whole.");
Check(AccessoryLayout.TryDecode(bytes, out var back), "A normal layout decodes.");
Check(back.SameAs(l), "Round trip keeps stacks, fits and values.");
Check(back.IsStacked("Visual/Roof", "RoofTent") && !back.IsStacked("Visual/Roof", "Snorkel") && !back.IsStacked("Visual/Side", "RoofTent"),
      "IsStacked answers per slot and item.");
Check(back.FitOf("Visual/Rear/Hitch").IsDefault, "Unlisted items get the default fit.");
Check(back.FitOf("Visual/Roof/RoofTent").V[4] == 2.5f, "Fit values come back exactly.");

// Default fits are never sent.
var withDefault = new AccessoryLayout { CarId = "x" };
withDefault.Fits["a/b"] = Fit();
Check(AccessoryLayout.TryDecode(withDefault.Encode(out _), out var wd) && wd.Fits.Count == 0, "Default fits are left out.");

// Hostile numbers are clamped into the editor's range; non-finite ones fall back to the default.
var wild = new AccessoryLayout { CarId = "x" };
wild.Fits["a/b"] = Fit(1000f, float.NaN, -50f, 99999f, float.PositiveInfinity, 0f, 0f, 1000f, -1f, 1f);
Check(AccessoryLayout.TryDecode(wild.Encode(out _), out var w), "Wild numbers still decode.");
var wf = w.FitOf("a/b").V;
Check(wf[0] == AccessoryLayout.MaxOffset && wf[1] == 0f && wf[2] == -AccessoryLayout.MaxOffset, "Offsets are clamped (NaN: default).");
Check(wf[3] == 360f && wf[4] == 0f, "Angles are clamped (infinity: default).");
Check(wf[6] == AccessoryLayout.MinSize && wf[7] == AccessoryLayout.MaxSize && wf[8] == AccessoryLayout.MinSize, "Sizes can't vanish or explode.");
var nanValue = new AccessoryLayout { CarId = "x" };
nanValue.Values["k"] = float.NaN;
Check(AccessoryLayout.TryDecode(nanValue.Encode(out _), out var nv) && nv.Values.Count == 0, "Non-finite values aren't sent or kept.");

// Malformed messages are rejected whole.
Check(!AccessoryLayout.TryDecode(null, out _) && !AccessoryLayout.TryDecode(new byte[] { 1 }, out _), "Empty or tiny payloads fail.");
for (int cut = 1; cut < bytes.Length; cut++)
    Check(!AccessoryLayout.TryDecode(bytes.Take(cut).ToArray(), out _), $"A layout cut at {cut} bytes fails.");
var wrongVersion = (byte[])bytes.Clone(); wrongVersion[0] = 99;
Check(!AccessoryLayout.TryDecode(wrongVersion, out _), "Another wire version fails.");
Check(!AccessoryLayout.TryDecode(bytes.Concat(new byte[] { 0 }).ToArray(), out _), "Trailing bytes fail.");
Check(!AccessoryLayout.TryDecode(new byte[AccessoryLayout.MaxBytes + 1], out _), "Oversized payloads fail.");
// A string index past the table: version 1, car "x", empty table, one stack pointing at string 5.
var badIndex = new byte[] { 1, 1, (byte)'x', 0, 0, 1, 0, 5, 0, 0, 0, 0, 0, 0 };
Check(!AccessoryLayout.TryDecode(badIndex, out _), "Indexes past the string table fail.");
// No car id.
Check(!AccessoryLayout.TryDecode(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 }, out _), "A layout without a car id fails.");
// Invalid UTF-8 in a string.
Check(!AccessoryLayout.TryDecode(new byte[] { 1, 1, 0xFF, 0, 0, 0, 0, 0, 0, 0 }, out _), "Invalid UTF-8 fails.");
// Random garbage never throws.
var rng = new Random(7);
for (int i = 0; i < 2000; i++)
{
    var junk = new byte[rng.Next(2, 300)];
    rng.NextBytes(junk);
    if (rng.Next(2) == 0) junk[0] = AccessoryLayout.Version;
    AccessoryLayout.TryDecode(junk, out _);
}
checks++;

// Too big for one message: stacks and values stay, fits are dropped until it fits.
var big = new AccessoryLayout { CarId = "tacoma3" };
big.Stacks["Visual/Roof"] = new SortedSet<string>(StringComparer.Ordinal) { "RoofTent" };
big.Values["wheels.size"] = 1.2f;
for (int i = 0; i < AccessoryLayout.MaxFits; i++)
    big.Fits[$"Visual/Slot_{i:000}/A_rather_long_item_name_to_fill_the_message_{i:000}"] = Fit(0.01f * (i % 50));
var bigBytes = big.Encode(out int bigDropped);
Check(bigBytes.Length > 0 && bigBytes.Length <= AccessoryLayout.MaxBytes, "An oversized layout is cut to fit one message.");
Check(bigDropped > 0, "Cutting reports what was dropped.");
Check(AccessoryLayout.TryDecode(bigBytes, out var bigBack) && bigBack.IsStacked("Visual/Roof", "RoofTent") &&
      bigBack.Values["wheels.size"] == 1.2f && bigBack.Fits.Count + bigDropped <= AccessoryLayout.MaxFits, "Stacks and values survive the cut.");

// SameAs notices any change.
var changed = new AccessoryLayout { CarId = "tacoma3" };
foreach (var kv in l.Stacks) changed.Stacks[kv.Key] = new SortedSet<string>(kv.Value, StringComparer.Ordinal);
foreach (var kv in l.Fits) changed.Fits[kv.Key] = kv.Value;
foreach (var kv in l.Values) changed.Values[kv.Key] = kv.Value;
Check(changed.SameAs(l), "An equal copy is the same.");
changed.Values["car.offset"] = 0.05f;
Check(!changed.SameAs(l), "A changed value is noticed.");

Console.WriteLine($"Passed {checks} accessory layout checks.");
