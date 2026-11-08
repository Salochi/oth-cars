using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using OverTheHillPlus.Cars;
using OverTheHillPlus.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace OverTheHillPlus.Accessories;

/// <summary>
/// The live accessory editor: a panel at the side of the screen (F8) over the garage or the world, so the car stays in
/// view while you work. One panel for every car framework car: it shows the car you're in, else the one open in the
/// garage. Tabs: Fit (move, turn and size the shown items, one at a time), Stack (pick a slot, switch its items on and
/// off), WHEELS and the car's own (<see cref="AccessoryTabs"/>, drawn with the public helpers here).
/// </summary>
public static class AccessoryEditor
{
    private const Key OpenKey = Key.F8;
    public const string KeyName = "F8";

    private const float W = 460f, Pad = 14f, RowH = 34f;
    private static readonly List<CarAccessories> Cars = new();
    private static bool _open, _left, _hooked;
    private static int _tab; // 0 fit, 1 stack, 2.. the car's own
    private static string _item, _slot; // selected item path / slot path
    private static Vector2 _scroll;
    private static Rect _panel; // in screen pixels, top-left origin (for the mouse check)
    private static EventSystem _paused;

    internal static IReadOnlyList<CarAccessories> All => Cars;

    internal static void Register(CarAccessories car)
    {
        Cars.Add(car);
        if (_hooked) return;
        _hooked = true;
        Features.Registry.OnTick(Tick, AccessoriesPlugin.ModId);
        Features.Registry.OnGui(Gui, AccessoriesPlugin.ModId);
    }

    public static void OpenFromPage()
    {
        try { ModsPage.Close(); } catch { }
        _open = true;
    }

    private static void Warn(string s) { try { AccessoriesPlugin.Logger?.LogWarning(s); } catch { } }

    /// <summary>The car the panel works on: the one you're driving, else the one shown in the garage.</summary>
    internal static (CarAccessories car, Transform tree) Current()
    {
        foreach (var c in Cars) { var t = c.LiveTree(); if (t != null) return (c, t); }
        foreach (var c in Cars) { var t = c.GarageTree(); if (t != null) return (c, t); }
        return (null, null);
    }

    /// <summary>The car object the panel is working on (for a car's own tabs), or null.</summary>
    public static GameObject CurrentRoot() => Current().tree?.parent?.parent?.gameObject;

    private static void Tick()
    {
        try
        {
            var kb = Keyboard.current;
            bool typing = ModsPage.IsOpen && UI.GameMenu.GameModsTab.Typing;
            if (kb != null && !typing && kb[OpenKey].wasPressedThisFrame) _open = !_open;
            if (_open && ModsPage.IsOpen) _open = false; // the page took over
            if (!_open) { Unpause(); return; }
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            // clicks on the panel must not also press the game's buttons underneath (the garage's)
            var m = Mouse.current;
            bool over = false;
            if (m != null)
            {
                var p = m.position.ReadValue();
                over = _panel.Contains(new Vector2(p.x, Screen.height - p.y));
            }
            if (over && _paused == null)
            {
                var es = EventSystem.current;
                if (es != null && es.enabled) { es.enabled = false; _paused = es; }
            }
            else if (!over) Unpause();
        }
        catch (Exception e) { Warn("editor tick: " + e.Message); }
    }

    private static void Unpause()
    {
        if (_paused == null) return;
        try { _paused.enabled = true; } catch { }
        _paused = null;
    }

    // ------------------------------------------------------------------ drawing

    private static float _y;
    private static Rect _r;

    /// <summary>The next row of the panel (for a car's own tab).</summary>
    public static Rect Row(float h = RowH)
    {
        var r = new Rect(_r.x + Pad, _y, _r.width - Pad * 2, h);
        _y += h + 4f;
        return r;
    }

    private static void Gui()
    {
        if (!_open) return;
        try
        {
            Theme.Ensure();
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float sh = Screen.height / scale, sw = Screen.width / scale;
            float h = Mathf.Min(sh - 80f, 760f);
            _r = new Rect(_left ? 24f : sw - W - 24f, 40f, W, h);
            _panel = new Rect(_r.x * scale, _r.y * scale, _r.width * scale, _r.height * scale);
            Theme.Fill(_r, new Color(Theme.Bg.r, Theme.Bg.g, Theme.Bg.b, 0.93f), 10);
            _y = _r.y + Pad;

            var (car, cosm) = Current();
            // header
            var hr = Row(26f);
            string title = car == null ? "ACCESSORIES" : car.Label.ToUpperInvariant() + " ACCESSORIES";
            GUI.Label(new Rect(hr.x, hr.y, hr.width - 120f, hr.height), title, Theme.Title);
            if (GUI.Button(new Rect(hr.xMax - 112f, hr.y, 52f, hr.height), _left ? ">" : "<", Theme.Chip)) _left = !_left;
            if (GUI.Button(new Rect(hr.xMax - 54f, hr.y, 54f, hr.height), "X " + KeyName, Theme.Chip)) _open = false;

            if (car == null)
            {
                string names = string.Join(" or ", Cars.Select(c => c.Label));
                GUI.Label(Row(48f), $"Get in your {names}, or open it in the garage.", Theme.Small);
                return;
            }
            var slots = car.Slots(cosm).ToList();

            // tabs
            var tabs = new List<string> { "FIT", "STACK" };
            tabs.AddRange(car.Tabs.Select(t => t.Title));
            if (_tab >= tabs.Count) _tab = 0;
            var tr = Row(30f);
            float tw = (tr.width - 6f * (tabs.Count - 1)) / tabs.Count;
            for (int i = 0; i < tabs.Count; i++)
                if (GUI.Button(new Rect(tr.x + (tw + 6f) * i, tr.y, tw, tr.height), tabs[i], _tab == i ? Theme.TabActive : Theme.Tab)) _tab = i;
            _y += 4f;

            if (_tab == 0) DrawFit(car, cosm);
            else if (_tab == 1) DrawStack(car, slots);
            else car.Tabs[_tab - 2].Draw();
        }
        catch (Exception e) { Warn("editor draw: " + e.Message); }
    }

    /// <summary>"&lt; text &gt;" picker; returns -1, 0 or +1.</summary>
    public static int Picker(string title, string sub)
    {
        var r = Row(sub == null ? 34f : 48f);
        Theme.Fill(r, Theme.Card, 6);
        int d = 0;
        if (GUI.Button(new Rect(r.x + 4f, r.y + 4f, 36f, r.height - 8f), "<", Theme.Chip)) d = -1;
        if (GUI.Button(new Rect(r.xMax - 40f, r.y + 4f, 36f, r.height - 8f), ">", Theme.Chip)) d = 1;
        var c = new Rect(r.x + 48f, r.y + 4f, r.width - 96f, 24f);
        GUI.Label(c, title, Theme.Label);
        if (sub != null) GUI.Label(new Rect(c.x, c.y + 20f, c.width, 18f), sub, Theme.Small);
        return d;
    }

    private static void DrawFit(CarAccessories car, Transform cosm)
    {
        var shown = new List<(CarAccessories.Slot slot, Transform it, string path, string label)>();
        foreach (var m in car.Movables(cosm))
            if (m.t.gameObject.activeInHierarchy) shown.Add((m.slot, m.t, m.path, m.label));
        if (shown.Count == 0) { GUI.Label(Row(), "No accessories fitted.", Theme.Small); return; }

        int i = Math.Max(0, shown.FindIndex(x => x.path == _item));
        var cur = shown[i];
        bool whole = cur.it == cur.slot.T;
        bool stacked = !whole && cur.slot.Stackable && car.StackOf(cur.slot.Path).Contains(cur.it.name);
        int d = Picker(cur.label,
                       $"{i + 1} of {shown.Count} fitted items - {(whole ? "moves everything on this side" : stacked ? "stacked" : "fitted in the garage")}");
        if (d != 0) { i = (i + d + shown.Count) % shown.Count; cur = shown[i]; }
        _item = cur.path;
        car.Describe(cur.it, cur.path);

        var f = car.FitOf(cur.path);
        Heading("POSITION");
        Slider(f.X, "Right", -1.5f, 1.5f, 0.01f, "+0.00;-0.00;0", " m");
        Slider(f.Y, "Up", -1.5f, 1.5f, 0.01f, "+0.00;-0.00;0", " m");
        Slider(f.Z, "Forward", -1.5f, 1.5f, 0.01f, "+0.00;-0.00;0", " m");
        Heading("ROTATION");
        Slider(f.Yaw, "Turn", -180f, 180f, 1f, "+0;-0;0", "°");
        Slider(f.Pitch, "Tip", -90f, 90f, 1f, "+0;-0;0", "°");
        Slider(f.Roll, "Lean", -90f, 90f, 1f, "+0;-0;0", "°");
        Heading("SIZE");
        Slider(f.Size, "Overall", 0.3f, 2f, 0.01f, "0.00", "x");
        Slider(f.SX, "Width", 0.3f, 2f, 0.01f, "0.00", "x");
        Slider(f.SY, "Height", 0.3f, 2f, 0.01f, "0.00", "x");
        Slider(f.SZ, "Length", 0.3f, 2f, 0.01f, "0.00", "x");
        _y += 4f;
        var br = Row(30f);
        float bw = stacked ? (br.width - 6f) / 2f : br.width;
        if (GUI.Button(new Rect(br.x, br.y, bw, br.height), "Reset this item", Theme.Button)) f.Reset();
        if (stacked && GUI.Button(new Rect(br.x + bw + 6f, br.y, bw, br.height), "Take off the stack", Theme.ChipDanger))
            car.SetStacked(cur.slot.Path, cur.it.name, false);
    }

    public static void Heading(string text)
    {
        var r = Row(18f);
        GUI.Label(r, text, Theme.Section);
    }

    public static void Note(string text, float h = 20f) => GUI.Label(Row(h), text, Theme.Small);

    public static bool Button(string text) => GUI.Button(Row(30f), text, Theme.Button);

    public static void Slider(ConfigEntry<float> e, string label, float min, float max, float step, string fmt, string suffix)
    {
        var r = Row(26f);
        GUI.Label(new Rect(r.x, r.y, 78f, r.height), label, Theme.Label);
        var track = new Rect(r.x + 82f, r.y + 6f, r.width - 82f - 150f, 14f);
        float v = GUI.HorizontalSlider(track, e.Value, min, max, Theme.Slider, Theme.Thumb);
        GUI.Label(new Rect(track.xMax + 8f, r.y, 70f, r.height), e.Value.ToString(fmt) + suffix, Theme.Value);
        if (GUI.Button(new Rect(r.xMax - 64f, r.y, 30f, r.height), "-", Theme.Chip)) v = e.Value - step;
        if (GUI.Button(new Rect(r.xMax - 30f, r.y, 30f, r.height), "+", Theme.Chip)) v = e.Value + step;
        v = Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
        if (Math.Abs(v - e.Value) > step * 0.01f) e.Value = v;
    }

    private static void DrawStack(CarAccessories car, List<CarAccessories.Slot> slots)
    {
        var stackable = slots.Where(s => s.Stackable && car.Items(s).Any()).ToList();
        if (stackable.Count == 0) { GUI.Label(Row(), "No slots to stack.", Theme.Small); return; }
        int i = Math.Max(0, stackable.FindIndex(s => s.Path == _slot));
        var cur = stackable[i];
        var items = car.Items(cur).ToList();
        int d = Picker(cur.Sub ? cur.Name : $"{cur.Name} slot",
                       $"{i + 1} of {stackable.Count} slots - {items.Count(x => x.gameObject.activeSelf)} of {items.Count} items shown");
        if (d != 0) { i = (i + d + stackable.Count) % stackable.Count; cur = stackable[i]; items = car.Items(cur).ToList(); _scroll = Vector2.zero; }
        _slot = cur.Path;
        GUI.Label(Row(18f), "Press an item to show or hide it. Fitted = chosen in the garage.", Theme.Small);

        var set = car.StackOf(cur.Path);
        float listH = _r.yMax - Pad - _y;
        var view = new Rect(_r.x + Pad, _y, _r.width - Pad * 2, listH);
        var content = new Rect(0f, 0f, view.width - 16f, items.Count * (RowH + 4f));
        _scroll = GUI.BeginScrollView(view, _scroll, content);
        float y = 0f;
        foreach (var it in items)
        {
            bool st = set.Contains(it.name);
            bool fitted = it.gameObject.activeSelf && !st;
            var r = new Rect(0f, y, content.width, RowH);
            y += RowH + 4f;
            Theme.Fill(r, st ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.22f) : Theme.Card, 6);
            GUI.Label(new Rect(r.x + 12f, r.y + 6f, r.width - 200f, 22f), CarAccessories.Pretty(it.name), fitted ? Theme.Muted_ : Theme.Label);
            if (fitted || st)
            {
                // jump to its fit
                if (GUI.Button(new Rect(r.xMax - 184f, r.y + 4f, 86f, r.height - 8f), "Adjust", Theme.Chip))
                { _item = cur.Path + "/" + it.name; _tab = 0; }
            }
            if (fitted)
                GUI.Label(new Rect(r.xMax - 92f, r.y + 6f, 86f, 22f), "FITTED", Theme.Value);
            else if (GUI.Button(new Rect(r.xMax - 92f, r.y + 4f, 86f, r.height - 8f), st ? "Hide" : "Show", st ? Theme.ChipDanger : Theme.Chip))
                car.SetStacked(cur.Path, it.name, !st);
        }
        GUI.EndScrollView();
    }
}
