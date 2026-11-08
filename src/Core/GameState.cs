using System;
using Fusion;
using Il2CppInterop.Runtime;
using UnityEngine;
using VehiclePhysics;
using Object = UnityEngine.Object;

namespace OverTheHillPlus;

/// <summary>Cached lookups of the network session and the local player's car, refreshed once per second.</summary>
internal static class GameState
{
    public static NetworkRunner Runner { get; private set; }
    public static VPVehicleController LocalCar { get; private set; }
    public static IntPtr LocalCarPtr { get; private set; }

    /// <summary>Your own player in the session, or null when there is no session.</summary>
    public static PlayerRef? LocalPlayer
    {
        get
        {
            try { return Runner != null ? Runner.LocalPlayer : null; }
            catch (Exception ex) { DebugLog.Error("core", ex, "GameState.LocalPlayer"); return null; }
        }
    }

    private static float _nextRefresh;

    private static FusionPlayer[] _fusionPlayers = Array.Empty<FusionPlayer>();
    private static float _fusionPlayersAt = -100f;

    /// <summary>
    /// Every FusionPlayer (player car object) in the scene, from one shared scan at most once a second, so features
    /// don't each walk all loaded objects. Up to a second old: entries can be destroyed already (check with the Unity
    /// null test, e.g. <c>fp == null</c>) and a car spawned in the last second can be missing. Main thread only.
    /// </summary>
    public static FusionPlayer[] FusionPlayers
    {
        get
        {
            float now = Time.unscaledTime;
            if (now - _fusionPlayersAt < 1f && now >= _fusionPlayersAt) return _fusionPlayers;
            _fusionPlayersAt = now;
            try
            {
                var list = new System.Collections.Generic.List<FusionPlayer>();
                foreach (var o in Behaviours(Il2CppType.Of<FusionPlayer>()))
                {
                    var fp = o?.TryCast<FusionPlayer>();
                    if (fp != null) list.Add(fp);
                }
                _fusionPlayers = list.ToArray();
            }
            catch (Exception e)
            {
                DebugLog.Error("core", e, "GameState.FusionPlayers");
                Core.CorePlugin.Logger?.LogDebug("FusionPlayers: " + e.Message);
                _fusionPlayers = Array.Empty<FusionPlayer>();
            }
            return _fusionPlayers;
        }
    }

    /// <summary>A session property as text (string properties as-is, others via ToString); null and found = false when absent.</summary>
    public static string SessionString(SessionInfo session, string key, out bool found)
    {
        found = false;
        var props = session?.Properties;
        if (props == null || !props.ContainsKey(key)) return null;
        var prop = props[key];
        found = true;
        if (prop == null) return null;
        return prop.IsString ? (string)prop : prop.PropertyValue?.ToString();
    }

    /// <summary>True when the joined session is private (room code / friends only).</summary>
    public static bool JoinedPrivateSession { get; private set; }

    /// <summary>Offline, single-player, or hosting: this game runs the physics for your car.</summary>
    public static bool IsAuthority
    {
        get
        {
            // Asked many times per frame (every physics step of the local car, per menu widget, ...): each answer
            // costs several interop calls on the runner, so it's computed once per frame.
            int frame = Time.frameCount;
            if (frame == _authorityFrame) return _authority;
            _authority = ComputeAuthority();
            _authorityFrame = frame;
            return _authority;
        }
    }

    private static int _authorityFrame = -1;
    private static bool _authority;

    private static bool ComputeAuthority()
    {
        var r = Runner;
        if (r == null) return true;
        try
        {
            if (r.IsSinglePlayer || r.IsServer) return true;
            if (r.GameMode == GameMode.Shared && r.IsSharedModeMasterClient) return true;
        }
        catch (Exception ex) { DebugLog.Error("core", ex, "GameState.ComputeAuthority"); }
        return false;
    }

    /// <summary>A guest in a public lobby: commands that affect other players are blocked here.</summary>
    public static bool IsPublicGuest => !IsAuthority && !JoinedPrivateSession;

    /// <summary>Short badge for the menu header.</summary>
    public static string Badge
    {
        get
        {
            var r = Runner;
            if (r == null) return "MENU";
            try
            {
                if (r.IsSinglePlayer) return "SOLO";
                if (IsAuthority) return "HOST";
                return JoinedPrivateSession ? "FRIENDS" : "PUBLIC";
            }
            catch (Exception ex) { DebugLog.Error("core", ex, "GameState.Badge"); return "?"; }
        }
    }

    /// <summary>
    /// Whether this game simulates car physics (solo or hosting). The host simulates every car, so on a joined
    /// client any local physics change would just be overwritten by the host's result.
    /// </summary>
    public static bool PhysicsAllowed => IsAuthority;

    public static string SessionDescription
    {
        get
        {
            var r = Runner;
            if (r == null) return "offline / menu";
            try
            {
                if (r.IsSinglePlayer) return "single-player";
                if (r.IsServer) return "hosting";
                if (r.GameMode == GameMode.Shared && r.IsSharedModeMasterClient) return "hosting (shared)";
                return JoinedPrivateSession ? "joined a private (friends) session" : "joined a public session";
            }
            catch (Exception ex) { DebugLog.Error("core", ex, "GameState.SessionDescription"); return "unknown"; }
        }
    }

    public static void Tick()
    {
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 1f;
        Runner = FindRunner();
        _authorityFrame = -1; // runner may have changed: recompute IsAuthority on next use
        JoinedPrivateSession = Runner != null && IsPrivate(Runner);
        LocalCar = FindLocalCar();
        LocalCarPtr = LocalCar == null ? IntPtr.Zero : LocalCar.Pointer;
    }

    private static bool IsPrivate(NetworkRunner r)
    {
        try
        {
            var props = r.SessionInfo?.Properties;
            return props != null && new OverlandRoomProperties(props).PrivateServer;
        }
        catch (Exception ex) { DebugLog.Error("core", ex, "GameState.IsPrivate"); return false; }
    }

    /// <summary>
    /// Called from physics-step patches for every car (several times per tick with client prediction), so the cheap
    /// pointer test runs first and the Unity null check (a native call) only when there is a local car at all.
    /// </summary>
    public static bool IsLocalCar(VehicleBase v) =>
        LocalCarPtr != IntPtr.Zero && v is not null && v.Pointer == LocalCarPtr && v != null;

    private static NetworkRunner FindRunner()
    {
        try
        {
            // Fusion's own list of runners: Object.FindObjectsOfType walks every script in the open world (8+ ms, once a
            // second, felt as a rhythmic stutter).
            var all = NetworkRunner.Instances;
            if (all == null) return null;
            int n = all.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<NetworkRunner>>().Count;
            for (int i = 0; i < n; i++)
            {
                var r = all[i];
                if (r != null && r.IsRunning) return r;
            }
        }
        catch (Exception e) { DebugLog.Error("core", e, "GameState.FindRunner"); Core.CorePlugin.Logger?.LogDebug("FindRunner: " + e.Message); }
        return null;
    }

    /// <summary>The running session's network behaviours of a type (subclasses too), from the runner's own table of
    /// network objects and each one's behaviour list (no scene walk); none without a session (no car can be driven
    /// then). Not the runner's GetAllBehaviours(Type): that matches the exact type only and missed the game's cars.</summary>
    private static System.Collections.Generic.List<Object> Behaviours(Il2CppSystem.Type type)
    {
        var result = new System.Collections.Generic.List<Object>();
        if (_scanMode) { foreach (var o in Object.FindObjectsOfType(type)) if (o != null) result.Add(o); return result; }
        var r = Runner;
        if (r == null) return result;
        var objects = r.GetAllNetworkObjects();
        if (objects == null) return result;
        for (int i = 0; i < objects.Count; i++)
        {
            var no = objects[i];
            if (no == null) continue;
            var behaviours = no.NetworkedBehaviours;
            if (behaviours == null) continue;
            foreach (var b in behaviours)
                if (b != null && type.IsAssignableFrom(b.GetIl2CppType())) result.Add(b);
        }
        return result;
    }

    /// <summary>Every car (VPVehicleController) in the running session, refreshed with <see cref="Tick"/> once a second;
    /// entries may be destroyed since (check with the Unity null test).</summary>
    public static VPVehicleController[] Vehicles { get; private set; } = Array.Empty<VPVehicleController>();

    /// <summary>The runner's tables missed a car that a scene scan found: scan (the old, slower way) from then on.</summary>
    private static bool _scanMode;
    private static float _noCarSince = -1f, _nextCheckScan;

    private static VPVehicleController FindLocalCar()
    {
        try
        {
            var cars = Cars(Behaviours(Il2CppType.Of<VPVehicleController>()));
            Vehicles = cars.ToArray();
            var mine = Mine(cars);
            if (mine != null || _scanMode || Runner == null) { _noCarSince = -1f; return mine; }
            // In a session with no car of ours for 5 s (normal on the map before spawning): check once with a scene
            // scan that the runner's tables aren't missing it, then only every 30 s.
            float now = Time.unscaledTime;
            if (_noCarSince < 0f) { _noCarSince = now; _nextCheckScan = now + 5f; }
            if (now < _nextCheckScan) return null;
            _nextCheckScan = now + 30f;
            var scanned = new System.Collections.Generic.List<Object>();
            foreach (var o in Object.FindObjectsOfType(Il2CppType.Of<VPVehicleController>())) if (o != null) scanned.Add(o);
            var all = Cars(scanned);
            var found = Mine(all);
            if (found == null) return null;
            _scanMode = true;
            Vehicles = all.ToArray();
            const string why = "the network runner's tables did not list your car; finding cars by scanning the scene from now on (slower: may stutter once a second)";
            DebugLog.Info("core", why);
            Core.CorePlugin.Logger?.LogWarning("[GameState] " + why);
            return found;
        }
        catch (Exception e) { DebugLog.Error("core", e, "GameState.FindLocalCar"); Core.CorePlugin.Logger?.LogDebug("FindLocalCar: " + e.Message); }
        return null;
    }

    private static System.Collections.Generic.List<VPVehicleController> Cars(System.Collections.Generic.List<Object> found)
    {
        var cars = new System.Collections.Generic.List<VPVehicleController>();
        foreach (var o in found)
        {
            var c = o.TryCast<VPVehicleController>();
            if (c != null) cars.Add(c);
        }
        return cars;
    }

    /// <summary>Yours by input authority; single-player, also the only active car around.</summary>
    private static VPVehicleController Mine(System.Collections.Generic.List<VPVehicleController> cars)
    {
        VPVehicleController only = null;
        int count = 0;
        foreach (var v in cars)
        {
            if (v == null || !v.isActiveAndEnabled) continue;
            count++;
            only = v;
            try { if (v.Object != null && v.HasInputAuthority) return v; } catch (Exception ex) { DebugLog.Error("core", ex, "GameState.FindLocalCar"); }
        }
        return count == 1 && Runner != null && Runner.IsSinglePlayer ? only : null;
    }
}
