using System;

namespace Scrapline.Core.Combat;

/// <summary>
/// The collision damage rules (docs/08 §2) — pure, so the clean-vs-botched call and the HP math
/// are exhaustively unit-tested while the physics (detecting the hit, measuring closing speed and
/// the impact zone) stay in the Godot <c>CarController</c>. This is the "takedown resolution" the
/// architecture (docs/02) keeps in Core.
///
/// Each function resolves the damage for ONE car (the one taking the hit), from its point of view.
/// Both cars in a collision call it independently with their own zone/speed — there's no shared
/// result to split, which keeps it simple and avoids any double-counting.
///
/// Clean (free): land your <c>Front</c> into a rival's <c>Side</c>/<c>Rear</c> while you're the
/// faster car → they take amplified damage, you take none. Botched: a head-on (front-to-front),
/// a glancing trade, or being the slower car → you take damage. Heavier cars resist; lighter cars
/// eat more (so a heavier car wins a symmetric ram — docs/02).
/// </summary>
public static class DamageModel
{
    /// <summary>
    /// HP damage to ONE car from a car-to-car collision, plus whether it's wrecked outright.
    /// <paramref name="closingSpeed"/> is how fast the two bodies came together (px/s);
    /// <paramref name="selfZone"/>/<paramref name="otherZone"/> are which face each car presents at
    /// the contact, and the speeds decide who's faster. You take nothing when YOU land the clean
    /// hit; you take amplified damage (and are wrecked if the attacker is travelling at/above
    /// <see cref="DamageRules.OneShotSpeed"/>) when the rival lands one on you.
    /// </summary>
    public static (float damage, bool wrecked) ResolveCarDamage(
        DamageRules rules,
        float closingSpeed,
        ImpactZone selfZone, ImpactZone otherZone,
        float selfSpeed, float otherSpeed,
        float selfMass, float otherMass)
    {
        if (closingSpeed < rules.MinImpactSpeed)
            return (0f, false);

        float baseDamage = rules.DamagePerSpeed * (closingSpeed - rules.MinImpactSpeed);

        // I land a clean hit (my front into their side/rear while I'm faster) → I pay nothing.
        if (IsCleanHit(selfZone, otherZone, selfSpeed, otherSpeed))
            return (0f, false);

        // I suffer a clean hit (their front into my side/rear while they're faster) → amplified,
        // and wrecked outright if they're travelling hard enough.
        if (IsCleanHit(otherZone, selfZone, otherSpeed, selfSpeed))
            return (baseDamage * rules.CleanHitMultiplier * MassFactor(rules, selfMass, otherMass),
                    otherSpeed >= rules.OneShotSpeed);

        // Botched / glancing / head-on → I eat the base trade, scaled by my mass.
        return (baseDamage * MassFactor(rules, selfMass, otherMass), false);
    }

    /// <summary>Self-damage from driving into a wall at <paramref name="approachSpeed"/> (px/s,
    /// the component of velocity into the wall). Free below the threshold (a scrape). Used by
    /// rivals; the player uses the far gentler <see cref="ResolvePlayerWallDamage"/>.</summary>
    public static float ResolveWallDamage(DamageRules rules, float approachSpeed)
    {
        if (approachSpeed < rules.WallMinSpeed)
            return 0f;
        return rules.WallDamagePerSpeed * (approachSpeed - rules.WallMinSpeed);
    }

    /// <summary>
    /// The damage a rival's clean ram deals the PLAYER: a value within the rival's own
    /// [<paramref name="minDamage"/>, <paramref name="maxDamage"/>] range, scaled by
    /// <paramref name="closingSpeed"/> — <see cref="DamageRules.MinImpactSpeed"/> → min, and
    /// <see cref="DamageRules.PlayerHitMaxSpeed"/> (and above) → max. Below the impact floor it's 0.
    /// </summary>
    public static float ResolvePlayerHit(DamageRules rules, float closingSpeed, float minDamage, float maxDamage)
    {
        if (closingSpeed < rules.MinImpactSpeed)
            return 0f;
        float t = Severity(closingSpeed, rules.MinImpactSpeed, rules.PlayerHitMaxSpeed);
        return minDamage + (maxDamage - minDamage) * t;
    }

    /// <summary>
    /// The PLAYER's trivial wall-scrape damage at <paramref name="approachSpeed"/>: 0 below
    /// <see cref="DamageRules.PlayerWallMinSpeed"/>, then scaling up to (and capped at)
    /// <see cref="DamageRules.PlayerWallMaxDamage"/> at <see cref="DamageRules.PlayerWallReferenceSpeed"/>.
    /// Fractional for light touches, hard-capped even at a full-speed slam.
    /// </summary>
    public static float ResolvePlayerWallDamage(DamageRules rules, float approachSpeed)
    {
        if (approachSpeed < rules.PlayerWallMinSpeed)
            return 0f;
        float t = Severity(approachSpeed, 0f, rules.PlayerWallReferenceSpeed);
        return rules.PlayerWallMaxDamage * t;
    }

    /// <summary>Continuous crush damage for a car pinned between a rival and a wall, for a slice of
    /// time <paramref name="dt"/> (seconds). This is the "smush": once a momentum-slam has stopped,
    /// the sustained pin keeps dealing damage that closing-speed impacts no longer would.</summary>
    public static float ResolveCrushDamage(DamageRules rules, float dt) =>
        rules.CrushDamagePerSecond * dt;

    /// <summary>
    /// Whether a rival is the aggressor against the player — the only car-to-car case in which the
    /// forgiving player model takes damage. True when the rival lands a clean hit on the player
    /// (their front into the player's side/rear while they're the faster car). When the player is
    /// the one ramming (or it's a head-on, or the player is faster), this is false → the player is
    /// unscathed, so aggression is never self-punishing.
    /// </summary>
    public static bool EnemyIsAggressor(
        ImpactZone enemyZone, ImpactZone playerZone, float enemySpeed, float playerSpeed) =>
        IsCleanHit(enemyZone, playerZone, enemySpeed, playerSpeed);

    /// <summary>Normalised 0–1 severity of <paramref name="value"/> across the [<paramref name="low"/>,
    /// <paramref name="high"/>] band (clamped). A degenerate band collapses to full severity.</summary>
    private static float Severity(float value, float low, float high)
    {
        float span = high - low;
        return span <= 0f ? 1f : Math.Clamp((value - low) / span, 0f, 1f);
    }

    /// <summary>You land a clean hit when your front strikes their side/rear and you're faster.</summary>
    private static bool IsCleanHit(ImpactZone self, ImpactZone other, float selfSpeed, float otherSpeed) =>
        self == ImpactZone.Front
        && other is ImpactZone.Side or ImpactZone.Rear
        && selfSpeed > otherSpeed;

    /// <summary>
    /// Damage scaling for the car taking the hit: heavier than the other → resists (&lt;1),
    /// lighter → eats more (&gt;1), equal → 1. Clamped so extreme ratios stay sane.
    /// </summary>
    private static float MassFactor(DamageRules rules, float selfMass, float otherMass)
    {
        if (selfMass <= 0f)
            return rules.MaxMassFactor;
        float factor = otherMass / selfMass;
        return Math.Clamp(factor, 1f / rules.MaxMassFactor, rules.MaxMassFactor);
    }
}
