using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OverTheHillPlus.Accessories;

/// <summary>
/// One player's accessory layout for one car, as it travels between players (<see cref="AccessoryNet"/>): the items
/// stacked per slot, every moved item's fit, and named values (the standard WHEELS tab's size and offset, and values a
/// car mod syncs through <see cref="AccessorySync"/>). Pure (unit-tested): no game types.
///
/// Wire, little-endian: version byte; the car id; a string table (every slot and item path once); the stacks (slot,
/// items); the fits (path, ten floats: X Y Z metres, yaw pitch roll degrees, size, size X Y Z); the values (key, float).
/// Decoding checks every count and index against what is left and clamps every number to what the editor allows, so a
/// bad message can't place an item kilometres away or shrink it to nothing.
/// </summary>
public sealed class AccessoryLayout
{
    public const byte Version = 1;
    /// <summary>The mod network's hard payload limit.</summary>
    public const int MaxBytes = 8192;
    public const int MaxStrings = 1024, MaxStringBytes = 200, MaxStacks = 128, MaxStackItems = 32, MaxFits = 256, MaxValues = 32;
    public const float MaxOffset = 1.5f, MinSize = 0.05f, MaxSize = 5f;

    /// <summary>X, Y, Z (metres), yaw, pitch, roll (degrees), size, size X, size Y, size Z.</summary>
    public sealed class FitValues : IEquatable<FitValues>
    {
        public readonly float[] V = new float[10];
        public static readonly FitValues Default = new() { V = { [6] = 1f, [7] = 1f, [8] = 1f, [9] = 1f } };

        public bool IsDefault => Equals(Default);
        public string Signature => string.Join(",", V.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

        /// <summary>Every number into the editor's range (non-finite ones to the default).</summary>
        public FitValues Clamped()
        {
            var c = new FitValues();
            for (int i = 0; i < 10; i++)
            {
                float d = Default.V[i], f = float.IsFinite(V[i]) ? V[i] : d;
                c.V[i] = i < 3 ? Math.Clamp(f, -MaxOffset, MaxOffset)
                       : i < 6 ? Math.Clamp(f, -360f, 360f)
                       : Math.Clamp(f, MinSize, MaxSize);
            }
            return c;
        }

        public bool Equals(FitValues o) => o != null && V.SequenceEqual(o.V);
        public override bool Equals(object o) => Equals(o as FitValues);
        public override int GetHashCode() => Signature.GetHashCode();
    }

    public string CarId = "";
    /// <summary>Slot path to the extra items stacked in it (item names).</summary>
    public readonly SortedDictionary<string, SortedSet<string>> Stacks = new(StringComparer.Ordinal);
    /// <summary>Item (or sub-slot) path to its fit; default fits are left out.</summary>
    public readonly SortedDictionary<string, FitValues> Fits = new(StringComparer.Ordinal);
    public readonly SortedDictionary<string, float> Values = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> StackOf(string slotPath) =>
        Stacks.TryGetValue(slotPath, out var s) ? s : (IReadOnlyCollection<string>)Array.Empty<string>();

    public FitValues FitOf(string path) => Fits.TryGetValue(path, out var f) ? f : FitValues.Default;

    public bool IsStacked(string slotPath, string item) => Stacks.TryGetValue(slotPath, out var s) && s.Contains(item);

    // ------------------------------------------------------------------ encode

    /// <summary>The bytes to send. A layout too big for one message keeps its stacks and values and drops the fits that
    /// don't fit (<paramref name="dropped"/> says how many).</summary>
    public byte[] Encode(out int dropped)
    {
        dropped = 0;
        var fits = Fits.Where(kv => !kv.Value.IsDefault).ToList();
        while (true)
        {
            var bytes = EncodeWith(fits);
            if (bytes.Length <= MaxBytes || fits.Count == 0) return bytes.Length <= MaxBytes ? bytes : Array.Empty<byte>();
            int cut = Math.Max(1, fits.Count / 8);
            fits.RemoveRange(fits.Count - cut, cut);
            dropped += cut;
        }
    }

    private byte[] EncodeWith(List<KeyValuePair<string, FitValues>> fits)
    {
        var stacks = Stacks.Where(kv => kv.Value.Count > 0).Take(MaxStacks).ToList();
        var values = Values.Where(kv => float.IsFinite(kv.Value)).Take(MaxValues).ToList();
        if (fits.Count > MaxFits) fits = fits.Take(MaxFits).ToList();

        var table = new List<string>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        int Idx(string s)
        {
            if (index.TryGetValue(s, out int i)) return i;
            index[s] = table.Count;
            table.Add(s);
            return table.Count - 1;
        }
        foreach (var kv in stacks) { Idx(kv.Key); foreach (var it in kv.Value.Take(MaxStackItems)) Idx(it); }
        foreach (var kv in fits) Idx(kv.Key);
        foreach (var kv in values) Idx(kv.Key);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write(Version);
        WriteString(w, CarId);
        w.Write((ushort)table.Count);
        foreach (var s in table) WriteString(w, s);
        w.Write((ushort)stacks.Count);
        foreach (var kv in stacks)
        {
            var items = kv.Value.Take(MaxStackItems).ToList();
            w.Write((ushort)index[kv.Key]);
            w.Write((byte)items.Count);
            foreach (var it in items) w.Write((ushort)index[it]);
        }
        w.Write((ushort)fits.Count);
        foreach (var kv in fits)
        {
            w.Write((ushort)index[kv.Key]);
            foreach (float f in kv.Value.V) w.Write(f);
        }
        w.Write((byte)values.Count);
        foreach (var kv in values)
        {
            w.Write((ushort)index[kv.Key]);
            w.Write(kv.Value);
        }
        w.Flush();
        return ms.ToArray();
    }

    private static void WriteString(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s ?? "");
        if (b.Length > MaxStringBytes) Array.Resize(ref b, MaxStringBytes);
        w.Write((byte)b.Length);
        w.Write(b);
    }

    // ------------------------------------------------------------------ decode

    /// <summary>A received layout, checked and clamped; false for anything malformed (nothing partial is returned).</summary>
    public static bool TryDecode(byte[] data, out AccessoryLayout layout)
    {
        layout = null;
        if (data == null || data.Length < 2 || data.Length > MaxBytes) return false;
        try
        {
            using var r = new BinaryReader(new MemoryStream(data, false), Encoding.UTF8);
            if (r.ReadByte() != Version) return false;
            var l = new AccessoryLayout { CarId = ReadString(r) };
            if (l.CarId.Length == 0) return false;
            int n = r.ReadUInt16();
            if (n > MaxStrings) return false;
            var table = new string[n];
            for (int i = 0; i < n; i++) table[i] = ReadString(r);
            string S(int i) => i < table.Length ? table[i] : throw new InvalidDataException("string index");

            int stacks = r.ReadUInt16();
            if (stacks > MaxStacks) return false;
            for (int i = 0; i < stacks; i++)
            {
                string slot = S(r.ReadUInt16());
                int items = r.ReadByte();
                if (items > MaxStackItems) return false;
                var set = new SortedSet<string>(StringComparer.Ordinal);
                for (int k = 0; k < items; k++) set.Add(S(r.ReadUInt16()));
                if (set.Count > 0) l.Stacks[slot] = set;
            }
            int fits = r.ReadUInt16();
            if (fits > MaxFits) return false;
            for (int i = 0; i < fits; i++)
            {
                string path = S(r.ReadUInt16());
                var f = new FitValues();
                for (int k = 0; k < 10; k++) f.V[k] = r.ReadSingle();
                f = f.Clamped();
                if (!f.IsDefault) l.Fits[path] = f;
            }
            int values = r.ReadByte();
            if (values > MaxValues) return false;
            for (int i = 0; i < values; i++)
            {
                string key = S(r.ReadUInt16());
                float v = r.ReadSingle();
                if (float.IsFinite(v)) l.Values[key] = v;
            }
            if (r.BaseStream.Position != data.Length) return false; // trailing bytes: not ours
            layout = l;
            return true;
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentException or DecoderFallbackException)
        {
            return false;
        }
    }

    private static string ReadString(BinaryReader r)
    {
        int len = r.ReadByte();
        if (len > MaxStringBytes) throw new InvalidDataException("string length");
        var b = r.ReadBytes(len);
        if (b.Length != len) throw new EndOfStreamException();
        return new UTF8Encoding(false, true).GetString(b);
    }

    /// <summary>Same content (what a receiver would show).</summary>
    public bool SameAs(AccessoryLayout o)
    {
        if (o == null || CarId != o.CarId || Stacks.Count != o.Stacks.Count || Fits.Count != o.Fits.Count || Values.Count != o.Values.Count) return false;
        foreach (var kv in Stacks) if (!o.Stacks.TryGetValue(kv.Key, out var s) || !s.SetEquals(kv.Value)) return false;
        foreach (var kv in Fits) if (!o.Fits.TryGetValue(kv.Key, out var f) || !f.Equals(kv.Value)) return false;
        foreach (var kv in Values) if (!o.Values.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
        return true;
    }
}
