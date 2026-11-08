using System;
using System.Collections.Generic;
using UnityEngine;

namespace OverTheHillPlus.Cars;

/// <summary>
/// Finding a modded car's objects in the scene, for the mods that work on them (the accessory editor, a car's own
/// wheel code): templates and actual display copies are announced (<see cref="BodyBuilt"/>), your display models are
/// kept per car, and any car object can be told apart by its body mesh.
/// </summary>
internal static class CarObjects
{
    /// <summary>After the framework put a car's model on an object and ran the car's own AfterBody hook: the car, the
    /// object, and whether it is the networked template (never drawn) rather than your displayed garage/menu/lobby copy.
    /// The reusable source dummy is not announced.</summary>
    public static event Action<RuntimeCar, GameObject, bool> BodyBuilt;

    private static readonly Dictionary<string, List<GameObject>> Models = new(StringComparer.OrdinalIgnoreCase);

    internal static void RaiseBodyBuilt(RuntimeCar car, GameObject root, bool template)
    {
        if (car?.Def == null || root == null) return;
        var h = BodyBuilt;
        if (h == null) return;
        foreach (Action<RuntimeCar, GameObject, bool> one in h.GetInvocationList())
        {
            try { one(car, root, template); }
            catch (Exception e) { DebugLog.Error(car.Id, e, "CarObjects.BodyBuilt"); }
        }
    }

    /// <summary>Announces a displayed copy once. The reusable source dummy must stay pristine: applying saved
    /// offsets there makes later clones inherit offsets before the editor captures their baseline.</summary>
    internal static void RegisterModel(RuntimeCar car, GameObject root)
    {
        if (!Models.TryGetValue(car.Id, out var list)) Models[car.Id] = list = new List<GameObject>();
        list.RemoveAll(o => o == null);
        if (list.Exists(o => o.Pointer == root.Pointer)) return;
        list.Add(root);
        RaiseBodyBuilt(car, root, template: false);
    }

    /// <summary>Your displayed garage, menu and lobby copies (never the source dummy or another player's).</summary>
    public static IEnumerable<GameObject> ModelsOf(string carId)
    {
        if (!Models.TryGetValue(carId, out var list)) yield break;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null) yield return list[i];
    }

    /// <summary>The running modded cars' definitions.</summary>
    public static IEnumerable<CarDefinition> Definitions()
    {
        foreach (var c in ModdedCars.All)
            if (c?.Def != null) yield return c.Def;
    }

    /// <summary>The running car with this id, or null.</summary>
    public static RuntimeCar Car(string carId)
    {
        foreach (var c in ModdedCars.All)
            if (c?.Def != null && string.Equals(c.Def.Id, carId, StringComparison.OrdinalIgnoreCase)) return c;
        return null;
    }

    /// <summary>The car object (the one with "Parts") a transform belongs to, or null.</summary>
    public static Transform RootOf(Transform t)
    {
        for (int i = 0; i < 6 && t != null; i++, t = t.parent)
            if (t.Find("Parts") != null) return t;
        return null;
    }

    /// <summary>Answers per car and root object (instance ids are never reused in a session): walking every mesh of
    /// every car each time was a 20+ ms hitch.</summary>
    private static readonly Dictionary<(string, int), bool> Known = new();

    /// <summary>Whether a car object (see <see cref="RootOf"/>) is this car: its body mesh is the car's.</summary>
    public static bool IsCar(CarDefinition def, Transform root)
    {
        if (def == null || root == null) return false;
        int id = root.gameObject.GetInstanceID();
        if (Known.TryGetValue((def.Id, id), out bool known)) return known;
        var parts = root.Find("Parts");
        if (parts == null) return false; // not built yet: ask again later
        string prefix = def.CarName + "_body";
        bool yes = false;
        foreach (var mf in parts.GetComponentsInChildren<MeshFilter>(true))
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name.StartsWith(prefix, StringComparison.Ordinal)) { yes = true; break; }
        if (Known.Count > 2048) Known.Clear();
        Known[(def.Id, id)] = yes;
        return yes;
    }

    /// <summary>Your own car's object when you are driving this car, else null.</summary>
    public static GameObject LiveCar(CarDefinition def)
    {
        var vc = GameState.LocalCar;
        var root = vc == null ? null : RootOf(vc.transform);
        return root != null && IsCar(def, root) ? root.gameObject : null;
    }

    /// <summary>The session's car objects that are this car (yours and other players'), from core's car list.</summary>
    public static IEnumerable<GameObject> SessionCars(CarDefinition def)
    {
        foreach (var vc in GameState.Vehicles)
        {
            var rt = vc == null ? null : RootOf(vc.transform);
            if (rt != null && IsCar(def, rt)) yield return rt.gameObject;
        }
    }
}
