namespace Scrapline.Core.Destruction;

/// <summary>
/// The tunables that make one car's crumple <em>sober</em> and another's <em>exaggerated</em>
/// (docs/09 §5) — the presentation asymmetry keyed off role. Pure data the Godot layer reads,
/// living here so the deformation math (<see cref="DeformableSilhouette"/>) is unit-testable.
///
/// Same hit, different profile: the player accumulates damage soberly and only breaks apart on
/// death; enemies crumple hard, shed panels through a fight, and split on a kill. This is the
/// per-car deformation profile docs/02 anticipated carrying on <c>RaceConfig</c>.
///
/// IMPORTANT — the magnitude fed to <see cref="DeformableSilhouette.ApplyHit"/> is the <b>HP
/// damage sustained</b> in the hit (tens of HP), not raw closing speed, so "high damage = high
/// deformation" (docs/09 §2). <see cref="CrumpleScale"/> is calibrated to that HP scale: a ~40 HP
/// ram should visibly crush an exaggerated car. Distances are world pixels. Starting values are
/// first guesses — expect to move them in playtesting (docs/09 §8).
/// </summary>
public sealed record DeformationProfile
{
    /// <summary>Inward vertex push (px) per <b>HP of damage</b> sustained, before falloff. The
    /// headline "high damage = high crumple" knob. A 40 HP hit × 0.5 = 20 px of dent (then capped).
    /// Low-ish for the player, high for enemies.</summary>
    public float CrumpleScale { get; init; } = 0.4f;

    /// <summary>Hard cap (px) on how far any one vertex can be pushed in, so a battered car still
    /// reads as a car. Also clamped to a fraction of the vertex's distance to the centre so a dent
    /// can never cross the middle.</summary>
    public float MaxCrumpleDepth { get; init; } = 16f;

    /// <summary>How far (px) a <em>flat</em> impactor's dent ramps from full depth to zero past its
    /// flat core — small, for crisp rectangular edges.</summary>
    public float FlatEdgeFalloff { get; init; } = 5f;

    /// <summary>How far (px) a <em>corner</em> impactor's dent ramps from the peak to zero — larger,
    /// so a sharp hit reads as a clean triangular V rather than a pinprick.</summary>
    public float SharpReach { get; init; } = 16f;

    /// <summary>How fast the current shape eases toward the accumulated target each second
    /// (framerate-independent). High = a snappy crunch; low = a slow, groaning bend.</summary>
    public float EaseRate { get; init; } = 12f;

    /// <summary>Accumulated <b>HP damage</b> on one zone (front/side/rear) at which that zone's panel
    /// tears off as debris. Low for enemies (shed through a fight); effectively unreachable for the
    /// player (it only breaks apart on death, via <see cref="DeformableSilhouette.ShedAllPanels"/>).</summary>
    public float PanelShedThreshold { get; init; } = 60f;

    /// <summary>Whether panels can shed while the car is still alive. Enemies: true. Player: false
    /// (sober — full destruction is reserved for death).</summary>
    public bool ShedsWhileAlive { get; init; } = true;

    /// <summary>HP fraction below which the Godot layer starts the sparks/smoke danger tell. The
    /// player rides at a low value (a "one more hit" warning); enemies emit always (1.0).</summary>
    public float SparksBelowHpFraction { get; init; } = 1f;

    /// <summary>Restrained accumulation, near-unbreakable panels, late danger tell — the player.
    /// Lower scale and a shallow cap keep dents readable as they build over a run.</summary>
    public static DeformationProfile Sober => new()
    {
        CrumpleScale = 0.3f,
        MaxCrumpleDepth = 9f,
        PanelShedThreshold = float.PositiveInfinity, // never shed mid-fight; only ShedAllPanels() on death
        ShedsWhileAlive = false,
        SparksBelowHpFraction = 0.2f,
    };

    /// <summary>Deep crumple, eager panel shedding, always-on sparks — the enemies. A solid ram
    /// noticeably caves the struck side; a kill caves it to the cap.</summary>
    public static DeformationProfile Exaggerated => new()
    {
        CrumpleScale = 0.5f,
        MaxCrumpleDepth = 20f,
        PanelShedThreshold = 45f,
        ShedsWhileAlive = true,
        SparksBelowHpFraction = 1f,
    };

    /// <summary>Walls/obstacles: they dent where struck but never shed panels, and dents stay shallow
    /// so a thin wall can't fold through itself. Damage is deformation-only (no HP/wreck).</summary>
    public static DeformationProfile Wall => new()
    {
        CrumpleScale = 0.03f, // fed by speed-into-wall (px/s), so a smaller scale than the HP-fed cars
        MaxCrumpleDepth = 14f,
        PanelShedThreshold = float.PositiveInfinity,
        ShedsWhileAlive = false,
        SparksBelowHpFraction = 1f,
    };

    public static DeformationProfile Default => new();
}
