namespace Scrapline.Core.Combat;

/// <summary>
/// Tunable thresholds and scaling for the collision damage model. These are world/physics
/// rules (not car identity, which lives in <c>CarStats</c>), so they live in one tunable place
/// the <see cref="DamageModel"/> reads. All speeds are px/s, damage is HP.
///
/// The model is deliberately simple: every impact is measured by <em>closing speed</em> — the
/// relative speed at which the two bodies came together — captured from each car's own velocity
/// the instant a contact begins. (We do NOT read the physics solver's per-contact impulse/normal:
/// those are unreliable on the first frame of a fast contact, which made damage inconsistent.)
/// Impacts are discrete (one per contact), so nothing grinds HP frame-after-frame; a steady
/// crush against a wall is the one continuous case, handled by <see cref="CrushDamagePerSecond"/>.
///
/// These numbers are first-guess starting points — Task 2 is a feel pass, so expect to move them
/// in playtesting. The model they feed is exhaustively unit-tested; the *values* are not.
/// </summary>
public sealed record DamageRules
{
    // ── Car ↔ car ──
    /// <summary>Closing speed below which a car-to-car bump does no damage at all (a love tap).</summary>
    public float MinImpactSpeed { get; init; } = 120f;

    /// <summary>HP per px/s of closing speed above <see cref="MinImpactSpeed"/> (the base trade).</summary>
    public float DamagePerSpeed { get; init; } = 0.08f;

    /// <summary>A clean takedown hits harder than a botched trade — multiplies the victim's damage.</summary>
    public float CleanHitMultiplier { get; init; } = 1.6f;

    /// <summary>Clamp on the mass advantage/disadvantage applied to damage (heavier resists, lighter
    /// eats more), so an extreme mass ratio can't trivialise or nullify a hit.</summary>
    public float MaxMassFactor { get; init; } = 2.0f;

    /// <summary>Attacker speed (px/s) at or above which a <em>clean</em> hit one-shot wrecks the
    /// victim regardless of HP (the takedown payoff — docs/08 §3).</summary>
    public float OneShotSpeed { get; init; } = 750f;

    // ── Car ↔ wall ──
    /// <summary>Speed into a wall below which the contact is a free scrape/lean (no self-damage),
    /// so brushing or parking against walls doesn't chip HP. Higher than a car bump's floor
    /// because you graze walls constantly.</summary>
    public float WallMinSpeed { get; init; } = 200f;

    /// <summary>HP per px/s of speed into a wall above <see cref="WallMinSpeed"/>.</summary>
    public float WallDamagePerSpeed { get; init; } = 0.05f;

    // ── Crush (the "smush") ──
    /// <summary>Continuous HP/sec dealt to a car pinned between another car and a wall — the only
    /// non-discrete damage. It's how momentum-smushing a rival into a wall stays lethal even once
    /// everything has stopped moving (so closing-speed impacts alone wouldn't register).</summary>
    public float CrushDamagePerSecond { get; init; } = 35f;

    // ── Player (forgiving) ──
    // The player's car uses a deliberately different, kinder model (docs/08 §2): i-frames after
    // every hit, no self-punishment for ramming, and only a rival out-aggressing them — a hazard,
    // or a (very minor) wall scrape — chips them, to encourage aggressive play. A rival's ram deals
    // a value scaled by closing speed within THAT rival's own [RamDamageMin, RamDamageMax] range
    // (CarStats), so enemies are individually tunable.

    /// <summary>Closing speed (px/s) at or above which a rival's ram deals its MAX range value to
    /// the player; at <see cref="MinImpactSpeed"/> it deals the MIN, scaling linearly between.</summary>
    public float PlayerHitMaxSpeed { get; init; } = 700f;

    /// <summary>Fixed HP the player loses from a track hazard (an intentional damage zone, so not
    /// as forgiving as walls).</summary>
    public float HazardDamage { get; init; } = 12f;

    /// <summary>Invulnerability window (seconds) after the player takes a rival/hazard hit — no
    /// further such damage lands until it elapses. (Minor wall scrapes don't grant i-frames.)</summary>
    public float InvulnSeconds { get; init; } = 1.0f;

    // ── Player ↔ wall (deliberately trivial) ──
    // Walls barely scratch the player: fractional for taps, hard-capped even at a full-speed slam,
    // and NO i-frames (so you can't dab a wall to dodge a rival). Enemies get the real wall model.

    /// <summary>Speed into a wall below which the player takes no scrape damage at all.</summary>
    public float PlayerWallMinSpeed { get; init; } = 40f;

    /// <summary>Speed into a wall at which the player's wall damage reaches its cap.</summary>
    public float PlayerWallReferenceSpeed { get; init; } = 600f;

    /// <summary>Hard cap on player wall damage — the most a full-speed slam can cost (kept tiny).</summary>
    public float PlayerWallMaxDamage { get; init; } = 5f;

    public static DamageRules Default => new();
}
