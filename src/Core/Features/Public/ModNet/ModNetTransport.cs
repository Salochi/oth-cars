using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fusion;
using Fusion.Sockets;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace OverTheHillPlus.Features.Public.ModNet;

/// <summary>
/// The wire under <see cref="ModNet"/>: Fusion reliable data (NetworkRunner.SendReliableDataToServer / ToPlayer), the
/// same path the game uses for its room code. The game's receiver (OverlandNetworkRunner.OnReliableDataReceived,
/// decoded with Cpp2IL) switches on the first int of the ReliableKey (1 = RuntimePlayer, 2 = room code) and throws
/// for anything else, so a Harmony prefix takes every message whose first word is <see cref="Magic"/> before the
/// game sees it, and leaves every other message alone.
///
/// The ReliableKey carries one marker only: its first word is <see cref="KeyMarker"/>. Fusion does not deliver a key's
/// 16 bytes as sent (the first multiplayer test, 2026-11-05: FromInts("OTHP", ...) arrived with a first word of 0x50,
/// the low byte alone; with this layout the received key reads 50-00-00-00-00-00-00-00 and then 8 bytes of whatever
/// was in memory, so only the first 8 bytes can be trusted), so the routing header travels at the start of the
/// payload instead, 12 bytes little-endian:
///   [0] channel (bits 0-15) | message type (bits 16-23) | message version (bits 24-31)
///   [1] wire protocol (bits 0-7) | flags (bits 8-15): 1 = relay request (client asks the host to forward),
///       2 = relayed (forwarded by the host)
///   [2] relay request: target player (RawEncoded, 0 = every modded player); relayed: the original sender
/// Wire protocol 3 is this layout; 1 and 2 put the header in the key (never worked across the network: their
/// messages reach a protocol 3 game with the old magic key and are dropped, unread).
///
/// The prefix is installed only while a session can actually carry mod messages (see <see cref="ModNet"/>) and is
/// removed when leaving. If reading a key ever fails it is removed for the rest of the game run; if handling fails
/// it keeps swallowing our messages but stops handling them, so the game's receiver never sees one.
/// </summary>
internal static class ModNetTransport
{
    internal const int Magic = 0x4F544850; // "OTHP": protocols 1-2's key (dropped if one ever arrives whole)
    /// <summary>The key's first word on our messages: 0x50 ('P'), a value that arrives intact (the game uses 1 and 2).</summary>
    internal const int KeyMarker = 0x50;
    private const int HeaderBytes = 12;
    private static int _ourKeysLogged;
    internal const int FlagRelayRequest = 1, FlagRelayed = 2;

    /// <summary>The game's receiver as the native code calls it. ReliableKey and ReadOnlySpan are 16-byte structs, so
    /// IL2CPP passes both by pointer: a Harmony patch (whose managed trampoline takes the key by value) garbled the
    /// game's own messages, so the host never got its own player data and the world never finished loading. This hook
    /// reads the key straight from memory and hands every other message to the game exactly as it came.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReceiveFn(IntPtr self, IntPtr runner, int player, IntPtr key, IntPtr data, IntPtr method);

    private static ReceiveFn _hookFn, _original;
    private static BepInEx.Unity.IL2CPP.Hook.INativeDetour _detour;
    private static volatile bool _active;
    private static bool _installed;
    private static int _gameKeysLogged;

    /// <summary>Our messages are taken (the hook is in and switched on).</summary>
    internal static bool Installed => _installed;
    /// <summary>The hook could not be put in: the mod network is off until the game restarts.</summary>
    internal static bool Faulted { get; private set; }
    /// <summary>Handling failed once: our messages are still swallowed, but no longer handled.</summary>
    internal static bool SwallowOnly { get; private set; }
    internal static string LastError { get; private set; }

    // Keep sent buffers referenced for a moment in case Fusion reads them after the call returns.
    private static readonly Queue<(float time, Il2CppStructArray<byte> arr, Il2CppSystem.ReadOnlySpan<byte> span)> Recent = new();

    // ---------------------------------------------------------------- hook

    internal static void SetInstalled(bool want)
    {
        if (Faulted) want = false;
        if (want == _installed) return;
        if (want && !EnsureDetour()) return;
        _active = want;
        _installed = want;
        Core.CorePlugin.Logger?.LogInfo(want ? "[ModNet] Receive hook on" : "[ModNet] Receive hook off");
    }

    /// <summary>Puts the native hook in once (it stays for the game run; switched on and off with a flag).</summary>
    private static bool EnsureDetour()
    {
        if (_detour != null) return true;
        try
        {
            System.Reflection.FieldInfo field = null;
            foreach (var f in typeof(OverlandNetworkRunner).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                if (f.FieldType == typeof(IntPtr) && f.Name.StartsWith("NativeMethodInfoPtr_OnReliableDataReceived", StringComparison.Ordinal)) { field = f; break; }
            if (field == null) { Fault("OverlandNetworkRunner.OnReliableDataReceived not found; the mod network is off"); return false; }
            IntPtr methodInfo = (IntPtr)field.GetValue(null);
            IntPtr code = methodInfo == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(methodInfo);
            if (code == IntPtr.Zero) { Fault("OverlandNetworkRunner.OnReliableDataReceived has no code pointer; the mod network is off"); return false; }
            _hookFn = Receive;
            _detour = BepInEx.Unity.IL2CPP.Hook.INativeDetour.CreateAndApply(code, _hookFn, out _original);
            Core.CorePlugin.Logger?.LogInfo("[ModNet] native receive hook in (" + field.Name + ")");
            return true;
        }
        catch (Exception e)
        {
            Fault("could not hook OnReliableDataReceived: " + e.Message);
            return false;
        }
    }

    private static void Fault(string why)
    {
        Faulted = true;
        LastError = why;
        Core.CorePlugin.Logger?.LogWarning("[ModNet] " + why);
    }

    /// <summary>Takes our messages (first key word <see cref="Magic"/>) before the game's handler, which throws for
    /// unknown keys; passes everything else to the game untouched. Never lets an exception out (native caller).</summary>
    private static void Receive(IntPtr self, IntPtr runner, int player, IntPtr key, IntPtr data, IntPtr method)
    {
        int k0 = 0, k1 = 0, k2 = 0, k3 = 0;
        bool read = false;
        try
        {
            if (key != IntPtr.Zero)
            {
                k0 = Marshal.ReadInt32(key);
                k1 = Marshal.ReadInt32(key + 4);
                k2 = Marshal.ReadInt32(key + 8);
                k3 = Marshal.ReadInt32(key + 12);
                read = true;
            }
        }
        catch { read = false; }

        if (read && k0 == Magic) return;   // an old protocol's message (header in the key): never the game's
        if (read && k0 == KeyMarker)
        {
            if (!_active || SwallowOnly) return; // never let the game see ours (it would throw)
            try
            {
                if (_ourKeysLogged < 3)
                {
                    _ourKeysLogged++;
                    var raw = new byte[16]; Marshal.Copy(key, raw, 0, 16);
                    DebugLog.Info("core", "[ModNet] our key as received: " + BitConverter.ToString(raw));
                }
                byte[] all = ReadSpanValue(data);
                if (all == null) return;
                if (all.Length < HeaderBytes)
                {
                    ModNet.LogOnce("short", $"[ModNet] Ignoring a message of {all.Length} bytes (shorter than the header)");
                    return;
                }
                int h1 = BitConverter.ToInt32(all, 0), h2 = BitConverter.ToInt32(all, 4), h3 = BitConverter.ToInt32(all, 8);
                int proto = h2 & 0xFF;
                if (proto != ModNet.WireProtocol)
                {
                    ModNet.LogOnce("proto" + proto, $"[ModNet] Ignoring a message on wire protocol {proto} (this version speaks {ModNet.WireProtocol}): the other player runs a different version of the mod");
                    return;
                }
                var bytes = new byte[all.Length - HeaderBytes];
                Buffer.BlockCopy(all, HeaderBytes, bytes, 0, bytes.Length);
                ModNet.OnWire(new NetworkRunner(runner), System.Runtime.CompilerServices.Unsafe.As<int, PlayerRef>(ref player), h1, h2, h3, bytes);
            }
            catch (Exception e)
            {
                SwallowOnly = true;
                LastError = "receive failed (messages are now ignored)";
                ModNet.LogOnce("recv", "[ModNet] Receive failed; mod messages are ignored from now on: " + e);
            }
            return;
        }

        if (_gameKeysLogged < 6)
        {
            _gameKeysLogged++;
            try { Core.CorePlugin.Logger?.LogInfo($"[ModNet] game message (key {k0}, {(read ? "read" : "unreadable")}) passed to the game"); } catch { }
        }
        try { _original(self, runner, player, key, data, method); }
        catch (Exception e)
        {
            ModNet.LogOnce("game-recv" + k0, $"[ModNet] the game's own receiver failed on a key {k0} message: {e.Message}");
        }
    }

    // ---------------------------------------------------------------- send

    internal static bool Send(NetworkRunner r, PlayerRef to, bool toServer, int k1, int k2, int k3, byte[] payload)
    {
        try
        {
            var key = ReliableKey.FromInts(KeyMarker, 0, 0, 0);
            var body = payload ?? Array.Empty<byte>();
            var framed = new byte[HeaderBytes + body.Length];
            BitConverter.GetBytes(k1).CopyTo(framed, 0);
            BitConverter.GetBytes(k2).CopyTo(framed, 4);
            BitConverter.GetBytes(k3).CopyTo(framed, 8);
            Buffer.BlockCopy(body, 0, framed, HeaderBytes, body.Length);
            var arr = new Il2CppStructArray<byte>(framed);
            var span = MakeSpan(arr);
            if (toServer) r.SendReliableDataToServer(key, span);
            else r.SendReliableDataToPlayer(to, key, span);
            Recent.Enqueue((Time.unscaledTime, arr, span));
            return true;
        }
        catch (Exception e)
        {
            LastError = "send failed";
            ModNet.LogOnce("send" + (k1 & 0xFFFF), $"[ModNet] Sending on channel {k1 & 0xFFFF} failed: {e.Message}");
            return false;
        }
    }

    internal static void PruneRecent()
    {
        while (Recent.Count > 0 && (Time.unscaledTime - Recent.Peek().time > 10f || Recent.Count > 128)) Recent.Dequeue();
    }

    internal static void ClearRecent() => Recent.Clear();

    // ---------------------------------------------------------------- span helpers

    private static Il2CppSystem.ReadOnlySpan<byte> MakeSpan(Il2CppStructArray<byte> arr)
    {
        try { return new Il2CppSystem.ReadOnlySpan<byte>(arr); }
        catch (Exception e)
        {
            // Fallback: build the 16-byte span value by hand ({data pointer, length}, as the game's SendRoomCode does:
            // the data of an IL2CPP array starts 32 bytes into the object, its length at 24).
            ModNet.LogOnce("spanctor", "[ModNet] ReadOnlySpan constructor unavailable, building it by hand: " + e.Message);
            IntPtr cls = Il2CppClassPointerStore<Il2CppSystem.ReadOnlySpan<byte>>.NativeClassPtr;
            IntPtr boxed = IL2CPP.il2cpp_object_new(cls);
            IntPtr value = IL2CPP.il2cpp_object_unbox(boxed);
            Marshal.WriteIntPtr(value, arr.Pointer + 32);
            Marshal.WriteInt32(value + IntPtr.Size, arr.Length);
            return new Il2CppSystem.ReadOnlySpan<byte>(boxed);
        }
    }

    /// <summary>Copies the bytes out of a span value ({data pointer, int length}) at <paramref name="value"/>.</summary>
    private static byte[] ReadSpanValue(IntPtr value)
    {
        if (value == IntPtr.Zero) return null;
        IntPtr data = Marshal.ReadIntPtr(value);
        int length = Marshal.ReadInt32(value + IntPtr.Size);
        if (length < 0 || length > ModNet.HardMaxPayload + HeaderBytes)
        {
            ModNet.CountDrop(ModNet.DropReason.Size);
            ModNet.LogOnce("len", $"[ModNet] Ignoring a message of {length} bytes");
            return null;
        }
        var bytes = new byte[length];
        if (length > 0)
        {
            // An access violation here could not be caught, so refuse anything that is not a plausible user-mode
            // address range (null page, kernel half, or a range that wraps).
            ulong start = (ulong)data.ToInt64();
            const ulong MinAddress = 0x10000, MaxUserAddress = 0x00007FFFFFFFFFFF;
            if (start < MinAddress || start > MaxUserAddress - (ulong)length)
            {
                ModNet.LogOnce("ptr", "[ModNet] Ignoring a message with an implausible data pointer");
                return null;
            }
            Marshal.Copy(data, bytes, 0, length);
        }
        return bytes;
    }
}
