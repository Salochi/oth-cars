using System;

namespace OverTheHillPlus.Cars;

/// <summary>Static garage pose relative to the donor dummy. Suspension extends below the body; larger tyres raise
/// both body and axles. Keep those contributions separate so wheels stay on the floor when a lift is mounted.</summary>
internal static class CarDisplayStance
{
    /// <summary>VPP places the extension limit at anchor * travel below the wheel's reference. The load compresses
    /// the spring from that limit (plus preload). Increasing travel therefore raises the body by anchor * delta,
    /// not by the entire extra travel. Compare equilibrium poses to account for a car's mass and spring tuning too.</summary>
    internal static float Extension(float travel, float anchor, float preload, float springRate, float cornerMass)
    {
        travel = Math.Max(0f, Finite(travel));
        anchor = Math.Clamp(Finite(anchor), 0f, 1f);
        float compression = springRate > 0f ? Math.Max(0f, Finite(cornerMass)) * 9.81f / springRate : 0f;
        return Math.Clamp(anchor * travel + Finite(preload) - compression, -(1f - anchor) * travel, anchor * travel);
    }

    internal static (float body, float axles) Offsets(float tyreLift, float suspensionLift) =>
        (Finite(tyreLift) + Finite(suspensionLift), Finite(tyreLift));

    private static float Finite(float value) => float.IsFinite(value) ? value : 0f;
}
