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

    /// <summary>Vehicle mass (Godot units). Heavier cars win symmetric rams and resist knockback.</summary>
    public float Mass { get; init; } = 1.0f;

    /// <summary>Hit points. A run-long resource — chipped by botched collisions and hard wall
    /// hits, restored only by scarce repairs. HP→0 wrecks the car. (Armor parts scale this.)</summary>
    public float MaxHp { get; init; } = 100f;

    // ── Ram damage to the player (this car's threat as an aggressor) ──────────────
    // When this car cleanly rams the PLAYER, the player takes a value in this range scaled by the
    // hit's closing speed (gentle → min, hard → max). It's how enemies are tuned to be more or less
    // dangerous; the player's own values are unused (the player never damages itself by ramming).
    // A "basic" enemy is gentle (0–5); tougher enemies raise this range.

    /// <summary>Least HP this car deals the player on a glancing-but-clean ram.</summary>
    public float RamDamageMin { get; init; } = 0f;

    /// <summary>Most HP this car deals the player on a hard clean ram.</summary>
    public float RamDamageMax { get; init; } = 5f;

    // ── Grip & drift (M1 driving feel) ──────────────────────────────────────────
    // The car bleeds off sideways (lateral) velocity each step so it mostly goes where
    // it's pointed; momentum still matters because the bleed is partial. Drifting drops
    // the grip (the car slides) and raises turn authority (it over-rotates into the slide).

    /// <summary>Fraction of lateral velocity removed per second during normal driving (0–1).
    /// Higher = more planted; lower = looser. Drift uses <see cref="DriftGrip"/> instead.</summary>
    public float Grip { get; init; } = 10f;

    /// <summary>Lateral grip while the drift button is held — below <see cref="Grip"/> so the
    /// rear steps out and the car slides.</summary>
    public float DriftGrip { get; init; } = 2.5f;

    /// <summary>Steering angular-authority multiplier while drifting (≥1). Lets the car point
    /// further into the corner than it's actually moving — the visible slip angle. Drift is a
    /// pure cornering/positioning tool now (it no longer earns boost — see the boost sources).</summary>
    public float DriftTurnMultiplier { get; init; } = 1.6f;

    // ── Boost meter & sources ───────────────────────────────────────────────────
    // Boost is a single free-spend meter. It is NOT earned by drifting (that proved exploitable).
    // M1 sources: a slow passive trickle (all cars; Comet leans on this) plus pickup pads and
    // launch pads placed on the track. Later cars add takedown-refill (Juggernaut) and
    // weapon-hit-refill (Ordnance) into the same meter.

    /// <summary>Boost-meter capacity in abstract fuel units.</summary>
    public float BoostCapacity { get; init; } = 100f;

    /// <summary>Passive trickle (fuel/sec) generated continuously, up to full. The universal
    /// baseline so you're never bone-dry; Comet's signature cranks this up.</summary>
    public float BoostRegenRate { get; init; } = 10f;

    /// <summary>Fuel units drained per second while the boost button is held.</summary>
    public float BoostDrainRate { get; init; } = 50f;

    /// <summary>Extra forward acceleration (px/s²) applied while boosting.</summary>
    public float BoostAcceleration { get; init; } = 1100f;

    /// <summary>Raised top speed (px/s) while boosting — above <see cref="MaxSpeed"/>.</summary>
    public float BoostMaxSpeed { get; init; } = 850f;

    /// <summary>The grey-box default used for M0 prototyping.</summary>
    public static CarStats Default => new();

    /// <summary>
    /// Cheap integrity check (a seed for the content-validation tests in docs/04):
    /// every car must have positive movement values, a sane reverse fraction, grip and
    /// drift in range, and a coherent boost meter.
    /// </summary>
    public bool IsWellFormed =>
        MaxSpeed > 0f &&
        Acceleration > 0f &&
        Friction > 0f &&
        BrakingForce > 0f &&
        TurnSpeed > 0f &&
        Mass > 0f &&
        ReverseSpeedFactor is > 0f and <= 1f &&
        MaxHp > 0f &&
        Grip > 0f &&
        DriftGrip is > 0f &&
        DriftGrip <= Grip &&
        DriftTurnMultiplier >= 1f &&
        BoostCapacity > 0f &&
        BoostRegenRate >= 0f &&
        BoostDrainRate > 0f &&
        BoostAcceleration > 0f &&
        BoostMaxSpeed > MaxSpeed &&
        RamDamageMin >= 0f &&
        RamDamageMax >= RamDamageMin;
}
