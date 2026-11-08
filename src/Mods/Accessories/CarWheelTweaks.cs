using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using OverTheHillPlus.Cars;
using UnityEngine;
using VehiclePhysics;

namespace OverTheHillPlus.Accessories;

/// <summary>
/// The F8 panel's standard WHEELS tab (every <see cref="CarAccessories"/> car that has no wheels tab of its own): wheel
/// size and offset for your own car and its garage model, applied live.
/// <list type="bullet">
/// <item>Size: a multiplier on the car's own wheels. The live car's wheel colliders take the new radius (VPP rides the
///   car higher or lower by the difference) and the drawn wheels (Axles/WheelMeshes) scale with it; the garage model,
///   which has no physics, is lifted or lowered by the difference.</item>
/// <item>Offset: the wheels move outward (or in) by the same amount on both sides, the colliders too, so the track the
///   physics drives on is the track you see.</item>
/// </list>
/// Saved in the car mod's config, [Wheels], and sent with your layout (<see cref="AccessoryNet"/>): other players with
/// the editor see your size and offset, and you see theirs (on the host, where their car is simulated, the bigger
/// wheels also drive).
/// </summary>
internal sealed class CarWheelTweaks
{
    private static readonly string[] Corners = { "FL", "FR", "RL", "RR" };
    public const float MinSize = 0.75f, MaxSize = 1.25f, MinOffset = -0.10f, MaxOffset = 0.25f;
    /// <summary>Other players' cars we changed the wheels of.</summary>
    private readonly HashSet<IntPtr> _others = new();

    private readonly CarAccessories _acc;
    public readonly ConfigEntry<float> Size, Offset;
    /// <summary>Off when the car brings its own wheel code (the size and offset are then left alone).</summary>
    public bool Enabled = true;
    private float _next;

    private sealed class Base
    {
        public Vector3 Pos, Scale;
        public float Radius, Inertia;
    }

    private readonly Dictionary<IntPtr, Base> _bases = new();
    /// <summary>Per car object: the radius its wheels have at size 1 (the colliders', or the garage model's tyre meshes').</summary>
    private readonly Dictionary<IntPtr, float> _radius = new();

    public CarWheelTweaks(CarAccessories acc, ConfigFile cfg)
    {
        _acc = acc;
        Size = cfg.Bind("Wheels", "Size", 1f, $"Wheel and tyre size, times the car's own ({MinSize} to {MaxSize}); other players with the editor see it");
        Offset = cfg.Bind("Wheels", "Offset", 0f, "Wheel offset in metres, outward on both sides (negative: inward); other players with the editor see it");
        Features.Registry.OnTick(Tick, AccessoriesPlugin.ModId);
    }

    private float K => Mathf.Clamp(Size.Value, MinSize, MaxSize);

    private void Tick()
    {
        if (!Enabled || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.25f;
        try
        {
            // four times a second: cheap (four wheels), and it puts the size back if the game resets a radius
            foreach (var root in _acc.FittedRoots())
            {
                if (root == null) continue;
                Apply(root.transform, K, Offset.Value);
                ReportOnce(root.transform);
            }
            // other players' cars: the size and offset they sent; back to stock once they send none
            var alive = new HashSet<IntPtr>();
            foreach (var (root, layout) in _acc.OthersWithLayouts())
            {
                alive.Add(root.Pointer);
                bool has = layout != null && layout.Values.ContainsKey("wheels.size");
                if (!has && !_others.Contains(root.Pointer)) continue; // they don't run the editor: untouched
                float k = has ? Mathf.Clamp(layout.Values["wheels.size"], MinSize, MaxSize) : 1f;
                float off = has && layout.Values.TryGetValue("wheels.offset", out var o) ? Mathf.Clamp(o, MinOffset, MaxOffset) : 0f;
                Apply(root.transform, k, off);
                if (has) _others.Add(root.Pointer); else _others.Remove(root.Pointer);
            }
            _others.RemoveWhere(p => !alive.Contains(p));
        }
        catch (Exception e) { AccessoriesPlugin.Logger?.LogWarning("[Wheels] " + e.Message); }
    }

    private void Apply(Transform rt, float k, float off)
    {
        var meshes = rt.Find("Axles/WheelMeshes");
        var colliders = rt.Find("Axles/WheelColliders");
        if (meshes == null) return;
        bool live = colliders != null && colliders.GetComponentInChildren<VPFunselektor3DRaycastWheelCollider>(true) != null;
        float r0 = 0f;
        for (int i = 0; i < 4; i++)
        {
            float outward = i % 2 == 0 ? -1f : 1f; // FL, RL on the left (x < 0)
            var wm = meshes.Find(Corners[i]);
            if (wm != null)
            {
                var b = BaseOf(wm, null);
                if (i == 0 && !live && !_radius.ContainsKey(rt.Pointer)) _radius[rt.Pointer] = MeshRadius(wm);
                wm.localScale = b.Scale * k;
                if (!live) wm.localPosition = b.Pos + wm.parent.InverseTransformVector(rt.TransformVector(new Vector3(outward * off, 0f, 0f)));
            }
            if (!live) continue;
            var wct = colliders.Find("Wheel" + Corners[i]);
            var wc = wct == null ? null : wct.GetComponent<VPFunselektor3DRaycastWheelCollider>();
            if (wc == null) continue;
            var bc = BaseOf(wct, wc);
            if (i == 0) r0 = bc.Radius;
            wct.localPosition = bc.Pos + wct.parent.InverseTransformVector(rt.TransformVector(new Vector3(outward * off, 0f, 0f)));
            float want = bc.Radius * k;
            if (Mathf.Abs(wc.radius - want) > 0.0005f) wc.radius = want;
            float inertia = bc.Inertia * k * k; // a bigger, heavier wheel spins up slower
            if (Mathf.Abs(wc.inertia - inertia) > 0.001f) wc.inertia = inertia;
        }
        if (!live && _radius.TryGetValue(rt.Pointer, out r0) && r0 > 0.05f)
        {
            CarDisplays.SetTyreLift(rt.gameObject, r0 * (k - 1f));
        }
    }

    private readonly Dictionary<IntPtr, float> _seen = new();
    private readonly HashSet<IntPtr> _reported = new();

    /// <summary>Verbose log only ([Debug] Level=Verbose): once per car object, 6 s after it is first seen (settled): where each drawn wheel sits against the
    /// model's wheel anchors, for lining the wheels up with the model's suspension.</summary>
    private void ReportOnce(Transform rt)
    {
        if (_reported.Contains(rt.Pointer) || !DebugLog.IsVerbose) return;
        if (!_seen.TryGetValue(rt.Pointer, out float t0)) { _seen[rt.Pointer] = Time.unscaledTime; return; }
        if (Time.unscaledTime - t0 < 6f) return;
        _reported.Add(rt.Pointer);
        try
        {
            CarLayout lay = null;
            foreach (var c in ModdedCars.All) if (c?.Def?.Id == _acc.Def.Id) lay = c.Layout;
            var meshes = rt.Find("Axles/WheelMeshes");
            var colliders = rt.Find("Axles/WheelColliders");
            bool live = colliders != null && colliders.GetComponentInChildren<VPFunselektor3DRaycastWheelCollider>(true) != null;
            static string V(Vector3 v) => $"({v.x:0.000}, {v.y:0.000}, {v.z:0.000})";
            var sb = new System.Text.StringBuilder($"[Wheels] {_acc.Label} {(live ? "car" : "garage model")} {rt.name}, size x{K:0.00}, offset {Offset.Value:0.000}:");
            for (int i = 0; i < 4 && meshes != null; i++)
            {
                var wm = meshes.Find(Corners[i]);
                if (wm == null) continue;
                sb.Append($"\n  {Corners[i]}: anchor {(lay == null ? "?" : V(lay.Wheels[i]))}, pivot {V(rt.InverseTransformPoint(wm.position))}");
                foreach (var r in wm.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (r == null) continue;
                    var b = r.bounds;
                    sb.Append($"; {r.name} centre {V(rt.InverseTransformPoint(b.center))} size {V(b.size)}");
                }
                var wct = colliders == null ? null : colliders.Find("Wheel" + Corners[i]);
                var wc = wct == null ? null : wct.GetComponent<VPFunselektor3DRaycastWheelCollider>();
                if (wc != null) sb.Append($"; collider {V(rt.InverseTransformPoint(wct.position))} r {wc.radius:0.000} travel {wc.suspensionTravel:0.000}");
            }
            AccessoriesPlugin.Logger?.LogInfo(sb.ToString());
        }
        catch (Exception e) { AccessoriesPlugin.Logger?.LogWarning("[Wheels] report: " + e.Message); }
    }

    /// <summary>A wheel's tyre radius from its drawn meshes (world-space height of its renderers, half), before scaling.</summary>
    private static float MeshRadius(Transform wheel)
    {
        bool any = false;
        var b = new Bounds();
        foreach (var r in wheel.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (r == null) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any ? b.size.y * 0.5f : 0f;
    }

    private Base BaseOf(Transform t, VPFunselektor3DRaycastWheelCollider wc)
    {
        if (_bases.TryGetValue(t.Pointer, out var b)) return b;
        b = new Base { Pos = t.localPosition, Scale = t.localScale, Radius = wc != null ? wc.radius : 0f, Inertia = wc != null ? wc.inertia : 0f };
        _bases[t.Pointer] = b;
        return b;
    }

    /// <summary>The tab.</summary>
    public void DrawTab()
    {
        AccessoryEditor.Heading("SIZE");
        AccessoryEditor.Slider(Size, "Size", MinSize, MaxSize, 0.01f, "0.00", "x");
        AccessoryEditor.Note("Scales the wheels and tyres (the ride height follows). 1 = the car's own.", 20f);
        AccessoryEditor.Heading("OFFSET");
        AccessoryEditor.Slider(Offset, "Outward", MinOffset, MaxOffset, 0.005f, "+0.000;-0.000;0", " m");
        AccessoryEditor.Note("Moves all four wheels out from the body (the driving track too). Saved; other players with the editor see it.", 40f);
        if (AccessoryEditor.Button("Stock size, no offset")) { Size.Value = 1f; Offset.Value = 0f; }
    }
}
