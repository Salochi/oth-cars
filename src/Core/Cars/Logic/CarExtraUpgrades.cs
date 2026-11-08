using System;
using System.Collections.Generic;
using System.Text;

namespace OverTheHillPlus.Cars;

/// <summary>
/// A garage upgrade a car mod adds on top of the base car's: a copy of one of the base car's own upgrade parts
/// (<see cref="CopyFrom"/>, for its icon and every field the garage reads) with its own item id, name, description and
/// price, moved to an upgrade type the car doesn't otherwise use (<see cref="Type"/>, so it doesn't compete with the
/// stock track it was copied from) and with no effect of the game's own: the car mod reads whether it is mounted
/// (<see cref="CarExtraParts.IsMounted"/>) and does the work itself. Upgrades of one type are levels of one track, in
/// list order. Part of the network layout (they take bits in the mounted-parts bitset).
/// </summary>
public sealed class ExtraUpgrade
{
    /// <summary>1 to 24 characters from a-z, 0-9 and '_'. Part of the network layout: never changes.</summary>
    public string Id;
    /// <summary>The part's item id: unique, never changes (it is in the save sidecar).</summary>
    public uint ItemId;
    public string Name, Description;
    /// <summary>Price in pebbles.</summary>
    public int Price;
    /// <summary>The base car's part to copy, by its asset name (e.g. "Tire_2").</summary>
    public string CopyFrom;
    /// <summary>The game's UpgradeType (as its number) the upgrade goes under.</summary>
    public int Type;
    /// <summary>The garage's name for that type's category (null: the game's own).</summary>
    public string TypeLabel;

    /// <summary>The layout part for core's join hash: nothing without extras.</summary>
    public static string LayoutPart(IReadOnlyList<ExtraUpgrade> list)
    {
        if (list == null || list.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var u in list) sb.Append(u.Id).Append(':').Append(u.ItemId.ToString("x8")).Append(':').Append(u.Type).Append(',');
        uint h = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(sb.ToString())) { h ^= b; h *= 16777619; }
        return $"|xup{list.Count}:{h:x8}";
    }
}
