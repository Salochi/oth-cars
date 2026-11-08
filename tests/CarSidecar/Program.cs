using OverTheHillPlus.Cars;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}

const uint Anchor = 2317836723, Jack = 1619438792, Compass = 2436578994;

// The toolset round-trips with everything else, in order (duplicates are real: three anchors).
var s = new CarSidecar { Owned = true, MetersTravelled = 7450, InUse = true, FallbackId = 1260490138,
                         Toolset = new List<uint> { Anchor, Anchor, Anchor, Jack } };
s.States.Add(new CarSidecarState { ItemId = 3085084626, Count = 1, InUse = true });
var back = CarSidecar.Decode(s.Encode());
Check(back != null && back.Usable && back.Complete, "A new sidecar decodes whole.");
Check(back.Toolset != null && back.Toolset.SequenceEqual(new[] { Anchor, Anchor, Anchor, Jack }), "The toolset comes back in order, duplicates kept.");
Check(back.Owned && back.InUse && back.MetersTravelled == 7450 && back.FallbackId == 1260490138, "The fixed fields are unchanged.");
Check(back.States.Count == 1 && back.States[0].ItemId == 3085084626 && back.States[0].InUse, "Parts are unchanged.");

// An empty toolset is kept as empty (the player took every tool out), not as "none saved".
var empty = CarSidecar.Decode(new CarSidecar { Owned = true, Toolset = new List<uint>() }.Encode());
Check(empty.Toolset != null && empty.Toolset.Count == 0, "An empty toolset stays empty.");

// No car item at save time: no toolset line, and the reader leaves the game's toolset alone.
var none = new CarSidecar { Owned = false };
none.States.Add(new CarSidecarState { ItemId = 1, Count = 1 });
Check(!none.Encode().Contains("toolset="), "No toolset known: no line.");
Check(CarSidecar.Decode(none.Encode()).Toolset == null, "No line: null toolset.");

// A file from Core 1.5.1 and earlier (no toolset line) still decodes, with the toolset left to the game.
const string old = "vera-sidecar 1\nowned=1\nkm=7950\ninuse=1\nfallback=1260490138\nstate=2154222459,1,0\nend=1\n";
var o = CarSidecar.Decode(old);
Check(o != null && o.Usable && o.Toolset == null && o.States.Count == 1, "Old sidecars decode as before.");

// A damaged toolset line is dropped whole (never a partial list of tools).
Check(CarSidecar.Decode(old.Replace("end=1", "toolset=2317836723,x,1619438792\nend=1")).Toolset == null, "A bad id drops the line.");
Check(CarSidecar.Decode(old.Replace("end=1", "toolset=2317836723,0\nend=1")).Toolset == null, "A zero id drops the line.");
Check(CarSidecar.Decode(old.Replace("end=1", "toolset=-5\nend=1")).Toolset == null, "A negative id drops the line.");
var tooMany = string.Join(",", Enumerable.Repeat(Anchor.ToString(), CarSidecar.MaxTools + 1));
Check(CarSidecar.Decode(old.Replace("end=1", "toolset=" + tooMany + "\nend=1")).Toolset == null, "An absurdly long toolset is taken as damage.");
Check(CarSidecar.Decode(old.Replace("end=1", "toolset=" + Compass + "\r\nend=1").Replace("\n", "\r\n")).Toolset?.Single() == Compass,
      "Windows line endings are fine.");

// An older reader skips the new line: the header and every old key are unchanged, the line is a plain key=value.
var text = s.Encode();
Check(text.StartsWith("vera-sidecar 1\n") && text.TrimEnd().EndsWith("end=1"), "Header and end line unchanged.");
var lines = text.Split('\n').Where(l => l.Length > 0).ToList();
Check(lines.Count(l => l.StartsWith("toolset=")) == 1 && lines.IndexOf(lines.First(l => l.StartsWith("toolset="))) < lines.IndexOf("end=1"),
      "One toolset line, before the end line.");

Console.WriteLine($"Passed {checks} sidecar checks.");
