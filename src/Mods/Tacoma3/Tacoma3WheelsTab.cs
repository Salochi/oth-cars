using System.Collections.Generic;
using OverTheHillPlus.Accessories;

namespace OverTheHillPlus.Tacoma3;

/// <summary>The accessory editor's WHEELS tab for the 3rd-gen Tacoma: tyre size (among the sizes you own) and the wheel offset.
/// Only drawn by the editor (OTH.Accessories), so only loaded when that mod is there.</summary>
internal static class Tacoma3WheelsTab
{
    public static void Draw()
    {
        // the choices: -1 (the largest you own), then stock and every size you've bought
        var root = AccessoryEditor.CurrentRoot();
        var options = new List<int> { -1 };
        for (int i = 0; i < Tacoma3Wheels.Sizes.Length; i++) if (Tacoma3Wheels.Owned(i)) options.Add(i);
        int s = Tacoma3Wheels.Size.Value;
        if (!options.Contains(s)) s = -1;
        string title = s < 0 ? $"Tyres: largest owned ({Tacoma3Wheels.SizeLabels[Tacoma3Wheels.Largest()]})" : "Tyres: " + Tacoma3Wheels.SizeLabels[s];
        var locked = new List<string>();
        for (int i = 1; i < Tacoma3Wheels.Sizes.Length; i++) if (!Tacoma3Wheels.Owned(i)) locked.Add(Tacoma3Wheels.SizeLabels[i]);
        int d = AccessoryEditor.Picker(title, locked.Count == 0 ? "Pick any size you own" : "Buy " + string.Join(" / ", locked) + " in the garage: Upgrades > Tyre Size");
        if (d != 0)
        {
            int k = options.IndexOf(s);
            Tacoma3Wheels.Size.Value = options[(k + d + options.Count) % options.Count];
        }
        AccessoryEditor.Heading("OFFSET");
        AccessoryEditor.Slider(Tacoma3Wheels.Offset, "Outward", Tacoma3Wheels.MinOffset, Tacoma3Wheels.MaxOffset, 0.005f, "+0.000;-0.000;0", " m");
        AccessoryEditor.Note("Moves all four wheels out from the body (the driving track too). Saved; other players with the editor see it.", 40f);
        AccessoryEditor.Note(Tacoma3Wheels.Describe(root));
        if (AccessoryEditor.Button("Largest owned, no offset")) { Tacoma3Wheels.Size.Value = -1; Tacoma3Wheels.Offset.Value = 0f; }
    }
}
