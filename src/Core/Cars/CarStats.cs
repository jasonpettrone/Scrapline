namespace Scrapline.Core.Cars;

/// <summary>
/// The handling characteristics that define how a car drives. This is pure data:
/// the Godot <c>CarController</c> reads these values and applies them via physics.
/// Cars are "characters" precisely because they're different <see cref="CarStats"/>,
/// which is why this lives in testable Core rather than the engine layer.
///
/// Units are world pixels and seconds (Godot's 2D space).
/// </summary>
public sealed record CarStats
{
    /// <summary>Top forward speed, px/sec.</summary>
    public float MaxSpeed { get; init; } = 600f;

    /// <summary>Throttle acceleration, px/sec².</summary>
    public float Acceleration { get; init; } = 900f;

    /// <summary>Passive deceleration when off the throttle, px/sec².</summary>
    public float Friction { get; init; } = 500f;

    /// <summary>Braking / reverse acceleration, px/sec².</summary>
    public float BrakingForce { get; init; } = 1400f;

    /// <summary>Maximum steering rate at full speed, radians/sec.</summary>
    public float TurnSpeed { get; init; } = 3.2f;

    /// <summary>Reverse top speed as a fraction of <see cref="MaxSpeed"/> (0–1).</summary>
    public float ReverseSpeedFactor { get; init; } = 0.45f;

    /// <summary>The grey-box default used for M0 prototyping.</summary>
    public static CarStats Default => new();

    /// <summary>
    /// Cheap integrity check (a seed for the content-validation tests in docs/04):
    /// every car must have positive movement values and a sane reverse fraction.
    /// </summary>
    public bool IsWellFormed =>
        MaxSpeed > 0f &&
        Acceleration > 0f &&
        Friction > 0f &&
        BrakingForce > 0f &&
        TurnSpeed > 0f &&
        ReverseSpeedFactor is > 0f and <= 1f;
}
