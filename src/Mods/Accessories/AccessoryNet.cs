using System;
using System.Collections.Generic;
using Fusion;
using OverTheHillPlus.Cars;
using UnityEngine;
using Net = OverTheHillPlus.Features.Public.ModNet.ModNet;

namespace OverTheHillPlus.Accessories;

// After the namespace line on purpose: the game has its own global ModManager (mod.io).
using OverTheHillPlus.Core.Manager;

/// <summary>
/// Each player's accessory layout for each modded car (<see cref="AccessoryLayout"/>: stacks, fits, wheel values), over
/// the mod network, so everyone with the editor sees everyone's stacked and moved accessories. Built like core's car
/// look sync: for each car the session runs, you announce your layout once connected (asking the others to answer
/// with theirs) and again whenever it changes, at most once a second; the host also sends them to each guest that
/// joins; guests reach each other through the host. Receivers keep each player's last layout per car and
/// <see cref="CarAccessories"/> shows it on that player's car and lobby copy. Players without the editor send nothing
/// and see the game's fit; nobody needs it to join.
///
/// Delivery doesn't depend on any one message: the mod network drops a send that's over its rate budget, and only the
/// host hears that a player left. So every layout is sent again every <see cref="Refresh"/> seconds even when unchanged
/// (a dropped send, a newcomer's missed answer, all made good within that), and a layout not heard again within
/// <see cref="Expiry"/> seconds is forgotten (a player who left, switched the editor or the mod network off, or whose
/// slot someone else now has: their car goes back to the game's fit).
/// </summary>
internal static class AccessoryNet
{
    /// <summary>Below 1000, unused by core (its list in ModNet.Channels) and the Salochi fixes (46, 47).</summary>
    public const ushort Channel = 48;
    public const byte MsgLayout = 1, MsgLayoutAskBack = 2;
    private const int MaxPeers = 64;
    private const float SendGap = 1f, AskBackGap = 3f;
    /// <summary>Seconds between sends of an unchanged layout, and after which an unrefreshed one is dropped.</summary>
    private const float Refresh = 15f, Expiry = 40f;

    private static readonly Dictionary<(int player, string car), AccessoryLayout> Layouts = new();
    private static readonly Dictionary<(int player, string car), float> HeardAt = new();
    private static float _nextExpiry;
    private static readonly Dictionary<(int, string), float> LastReply = new();
    private static readonly HashSet<int> Players = new();

    private sealed class Sent
    {
        public byte[] Payload;
        public bool Announced;
        public float Next, Again;
    }

    private static readonly Dictionary<string, Sent> Mine = new(StringComparer.OrdinalIgnoreCase);
    private static bool _registered;
    private static readonly HashSet<string> LoggedOnce = new();

    public static int Rejected { get; private set; }
    public static int Known => Players.Count;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        Net.Register(Channel, new Net.ChannelOptions
        {
            Name = "accessories",
            Version = 1,
            MaxPayload = AccessoryLayout.MaxBytes,
            // per sender: a layout per car once a second at most, plus the answers to a newcomer
            RatePerSecond = 4f,
            Burst = 12,
            AllowRelay = true,
            HostOnlySender = false,
            Active = () => AccessoriesPlugin.AnyCar,
        }, OnMessage);
        Net.OnSessionReset(Reset);
        Net.OnPeerGone(p => Forget(p.RawEncoded));
        Net.OnPeerReady(p =>
        {
            // Host: a guest arrived, send it your layouts (its own announcements ask the others).
            if (!Net.IsHost) return;
            foreach (var acc in AccessoriesPlugin.All)
                if (Connected(acc.Def.Id)) Send(() => Net.SendTo(p, Channel, MsgLayout, acc.Encode()));
        });
        Features.Registry.OnTick(Tick, AccessoriesPlugin.ModId);
    }

    /// <summary>The last layout a player sent for a car (null: none, or they don't run the editor).</summary>
    public static AccessoryLayout Of(int rawPlayer, string carId) =>
        Layouts.TryGetValue((rawPlayer, carId.ToLowerInvariant()), out var l) ? l : null;

    private static bool Active(string carId)
    {
        var car = CarObjects.Car(carId);
        return car != null && car.Ready && ModManager.ServerModActive(car.Id);
    }

    private static bool Connected(string carId) =>
        Active(carId) && (Net.IsHost ? Net.HostAdvertised : Net.IsGuest && Net.HostHas(Channel));

    private static void Tick()
    {
        float now = Time.unscaledTime;
        ExpireOld(now);
        foreach (var acc in AccessoriesPlugin.All)
        {
            string id = acc.Def.Id;
            if (!Mine.TryGetValue(id, out var s)) Mine[id] = s = new Sent();
            if (!Connected(id))
            {
                s.Announced = false;
                s.Payload = null;
                continue;
            }
            if (now < s.Next) continue;
            s.Next = now + SendGap;
            var payload = acc.Encode();
            if (payload.Length == 0) { Once($"{id}: your layout is too big to send"); continue; }
            // unchanged: sent again only every Refresh seconds (a dropped send is made good then)
            if (s.Announced && s.Payload != null && Same(s.Payload, payload) && now < s.Again) continue;
            int n = Net.Broadcast(Channel, s.Announced ? MsgLayout : MsgLayoutAskBack, payload);
            // A host with nobody here yet is done (whoever joins is sent it); a guest needs the host to take it.
            if (n > 0 || Net.IsHost)
            {
                s.Announced = true;
                s.Payload = payload;
                s.Again = now + Refresh;
            }
        }
    }

    private static void OnMessage(Net.Message m)
    {
        var from = m.From;
        bool fromHost = false;
        if (m.FromHost && !m.Relayed)
        {
            // From the host itself: on a guest the transport's sender may not be the host's player ref.
            fromHost = true;
            try { var sp = OverlandSessionPlayers.Instance; if (sp != null && sp.HostPlayerRef.IsRealPlayer) from = sp.HostPlayerRef; }
            catch (Exception e) { DebugLog.Error(AccessoriesPlugin.ModId, e, "AccessoryNet.OnMessage host"); }
        }
        try
        {
            var r = GameState.Runner;
            if (r != null && r.LocalPlayer.RawEncoded == from.RawEncoded) return; // our own echo
        }
        catch (Exception e) { DebugLog.Error(AccessoriesPlugin.ModId, e, "AccessoryNet.OnMessage echo"); }
        if (m.Type != MsgLayout && m.Type != MsgLayoutAskBack) return;
        if (!AccessoryLayout.TryDecode(m.Payload, out var layout)) { Rejected++; return; }
        string carId = layout.CarId.ToLowerInvariant();
        if (!Active(carId)) return; // a car this game or this session doesn't run
        int raw = from.RawEncoded;
        if (!Players.Contains(raw) && Players.Count >= MaxPeers) return;
        Players.Add(raw);
        var k = (raw, carId);
        HeardAt[k] = Time.unscaledTime;
        if (!Layouts.TryGetValue(k, out var have) || !have.SameAs(layout))
        {
            Layouts[k] = layout;
            DebugLog.Verbose(AccessoriesPlugin.ModId, () =>
                $"[Accessories] player {raw}'s {carId}: {layout.Stacks.Count} stacked slot(s), {layout.Fits.Count} moved item(s), {layout.Values.Count} value(s)");
        }

        if (m.Type != MsgLayoutAskBack || !AccessoriesPlugin.TryGet(carId, out var acc)) return;
        float now = Time.unscaledTime;
        if (LastReply.TryGetValue(k, out float at) && now - at < AskBackGap) return;
        LastReply[k] = now;
        var mine = acc.Encode();
        if (mine.Length == 0) return;
        if (Net.IsHost) Send(() => Net.SendTo(from, Channel, MsgLayout, mine));
        else if (fromHost) Send(() => Net.SendToHost(Channel, MsgLayout, mine));
        else Send(() => Net.SendTo(from, Channel, MsgLayout, mine));
    }

    private static void Send(Func<Net.SendResult> send)
    {
        try { send(); }
        catch (Exception e) { DebugLog.Error(AccessoriesPlugin.ModId, e, "AccessoryNet.Send"); }
    }

    // ------------------------------------------------------------------ whose car is it

    /// <summary>
    /// Another player's copies of this car with the player each belongs to: their car in the session (its input
    /// authority) and their copy in the lobby. Your own car and garage models are never in here.
    /// </summary>
    public static IEnumerable<(GameObject root, int owner)> OthersCars(CarDefinition def)
    {
        var found = new List<(GameObject, int)>();
        try
        {
            foreach (var fp in GameState.FusionPlayers)
            {
                if (fp == null) continue;
                try
                {
                    if (fp.HasInputAuthority) continue;
                    var root = CarObjects.RootOf(fp.transform);
                    if (root == null || !CarObjects.IsCar(def, root)) continue;
                    found.Add((root.gameObject, fp.Object.InputAuthority.RawEncoded));
                }
                catch (Exception e) { DebugLog.Error(AccessoriesPlugin.ModId, e, "AccessoryNet.OthersCars car"); }
            }
            foreach (var d in CarDisplays.All)
                if (!d.Mine && d.Root != null && d.Car?.Id == def.Id) found.Add((d.Root, d.Owner));
        }
        catch (Exception e) { DebugLog.Error(AccessoriesPlugin.ModId, e, "AccessoryNet.OthersCars"); }
        return found;
    }

    /// <summary>The player a copy of a car belongs to (see <see cref="OthersCars"/>), or null for yours or unknown.</summary>
    public static int? OwnerOf(CarDefinition def, GameObject root)
    {
        if (root == null) return null;
        foreach (var (r, owner) in OthersCars(def))
            if (r != null && r.Pointer == root.Pointer) return owner;
        return null;
    }

    // ------------------------------------------------------------------ housekeeping

    /// <summary>Drops layouts not heard again within <see cref="Expiry"/> seconds (checked every few seconds).</summary>
    private static void ExpireOld(float now)
    {
        if (now < _nextExpiry) return;
        _nextExpiry = now + 5f;
        List<(int, string)> drop = null;
        foreach (var kv in HeardAt)
            if (now - kv.Value > Expiry) (drop ??= new List<(int, string)>()).Add(kv.Key);
        if (drop == null) return;
        foreach (var k in drop)
        {
            HeardAt.Remove(k);
            Layouts.Remove(k);
            LastReply.Remove(k);
            DebugLog.Verbose(AccessoriesPlugin.ModId, () => $"[Accessories] player {k.Item1}'s {k.Item2}: not heard for {Expiry:0} s, back to the game's fit");
        }
        Players.Clear();
        foreach (var k in Layouts.Keys) Players.Add(k.player);
    }

    private static void Forget(int raw)
    {
        Players.Remove(raw);
        var drop = new List<(int, string)>();
        foreach (var k in Layouts.Keys) if (k.player == raw) drop.Add(k);
        foreach (var k in drop) { Layouts.Remove(k); HeardAt.Remove(k); }
        drop.Clear();
        foreach (var k in LastReply.Keys) if (k.Item1 == raw) drop.Add(k);
        foreach (var k in drop) LastReply.Remove(k);
    }

    private static void Reset()
    {
        Layouts.Clear();
        HeardAt.Clear();
        LastReply.Clear();
        Players.Clear();
        foreach (var s in Mine.Values)
        {
            s.Announced = false;
            s.Payload = null;
        }
    }

    private static void Once(string text)
    {
        if (LoggedOnce.Add(text)) AccessoriesPlugin.Logger?.LogWarning("[Accessories] " + text);
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}

/// <summary>
/// For a car mod with editor values of its own (the 3rd-gen Tacoma's tyre size and wheel offset): sends them with your
/// layout, and reads the value another player sent for their car. Call through a guard like
/// <c>Tacoma3Accessories</c>, so the car runs without the editor. The car mod checks and clamps what it reads.
/// </summary>
public static class AccessorySync
{
    internal static readonly Dictionary<string, Dictionary<string, Func<float>>> Locals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A value of yours to send with this car's layout (read once a second; a change goes out within a second).</summary>
    public static void Value(string carId, string key, Func<float> local)
    {
        if (string.IsNullOrEmpty(carId) || string.IsNullOrEmpty(key) || local == null) return;
        if (!Locals.TryGetValue(carId, out var d)) Locals[carId] = d = new Dictionary<string, Func<float>>(StringComparer.Ordinal);
        d["car." + key] = local;
    }

    /// <summary>The value the player this copy of the car belongs to sent (false: it's yours, or they sent none).</summary>
    public static bool TryRemote(CarDefinition def, GameObject root, string key, out float value)
    {
        value = 0f;
        int? owner = AccessoryNet.OwnerOf(def, root);
        if (owner == null) return false;
        var layout = AccessoryNet.Of(owner.Value, def.Id);
        return layout != null && layout.Values.TryGetValue("car." + key, out value);
    }
}
