using System;

namespace Scrapline.Core.Cars;

/// <summary>
/// The boost meter: a single free-spend pool (GDD §4) filled — in M1 — only by mini-turbo
/// drifts, and spent by holding the boost button (hold-to-drain). Built as one shared meter
/// so later fill sources (Comet's passive trickle, the Juggernaut's takedowns) drop in
/// without rework.
///
/// Pure accounting — no Godot. The <c>CarController</c> applies the actual speed; this class
/// just tracks fuel, so the fill/drain/clamp math is unit-testable.
/// </summary>
public sealed class BoostMeter
{
    private readonly float _capacity;

    public BoostMeter(float capacity) => _capacity = capacity;

    /// <summary>Current fuel in the meter.</summary>
    public float Current { get; private set; }

    /// <summary>Fill level as 0–1, for the HUD/debug readout.</summary>
    public float Fraction => _capacity > 0f ? Current / _capacity : 0f;

    /// <summary>True when there's no fuel left to spend.</summary>
    public bool IsEmpty => Current <= 0f;

    /// <summary>Add fuel (e.g. from a drift release), clamped at capacity.</summary>
    public void Add(float amount)
    {
        if (amount > 0f)
            Current = Math.Min(_capacity, Current + amount);
    }

    /// <summary>
    /// Drain up to <paramref name="rate"/> × <paramref name="deltaSeconds"/> fuel this frame,
    /// never below empty. Returns the amount actually drained — 0 when empty — so the caller
    /// knows whether boost is live and can scale its effect by real fuel spent.
    /// </summary>
    public float TryDrain(float deltaSeconds, float rate)
    {
        float requested = rate * deltaSeconds;
        if (requested <= 0f || Current <= 0f)
            return 0f;

        float drained = Math.Min(Current, requested);
        Current -= drained;
        return drained;
    }
}
