using Godot;
using Scrapline.Core.Combat;
using Scrapline.Core.Destruction;
using SysVec2 = System.Numerics.Vector2;

namespace Scrapline.Game.Race;

/// <summary>
/// A wall/barrier that deforms like a car (docs/09): a <see cref="StaticBody2D"/> wrapping a shared
/// <see cref="Deformable"/>, so its visual <em>and</em> its collision shape cave where a car slams it
/// — and the carved space is drivable, exactly like a dented car. Walls take no HP and never wreck;
/// the damage they exchange with cars is the existing car-side wall damage (this just adds the wall's
/// own visible/physical destruction). Dents persist for the race (reset per node, later).
///
/// The struck car detects the contact and calls <see cref="TakeImpact"/> — the wall converts to its
/// local frame and builds an <see cref="Indenter"/> (flat head-on slam → wide dent; glancing/corner →
/// sharp). Cars own deformation entirely; the wall just renders what it's told.
/// </summary>
public partial class DeformableWall : StaticBody2D
{
    private const float WallVertexSpacing = 55f;   // ~one dent-vertex per this many px of edge
    private const int MaxWallVertsPerEdge = 72;    // cap so huge boundary walls stay cheap (the
                                                   // adaptive hitbox only keeps dented vertices, so
                                                   // a denser visual ring no longer costs collision)
    private const float WallFlatHalfWidth = 26f;   // a square-on slam caves about a car's width
    private const float WallCornerHalfWidth = 4f;  // a glancing/corner hit pokes a narrow dent

    /// <summary>How much of the wall's thickness repeated slams can carve away (docs/09: keep
    /// crashing into the same spot and the indent keeps growing) — replaces the old flat 14px cap
    /// (≈ one slam) with a per-wall, thickness-aware budget. The default 0.45 is safe for walls
    /// reachable from BOTH sides: two opposing dents (0.45 + 0.45) can never meet and cross the
    /// ring. Walls only ever hit from one side (the arena boundary) can raise it toward ~0.8.</summary>
    public float CarveBudgetFraction { get; set; } = 0.45f;

    /// <summary>Wall dimensions (px). Set by the builder before the node enters the tree.</summary>
    public Vector2 Size { get; set; } = new(100f, 100f);

    /// <summary>Fill colour for the wall's visual.</summary>
    public Color Color { get; set; } = new(0.45f, 0.47f, 0.55f);

    private Deformable? _deformer;

    /// <summary>The wall's deformer, exposed for the integration tests (dent localization).</summary>
    public Deformable? Deformer => _deformer;

    public override void _Ready()
    {
        float hw = Size.X / 2f, hh = Size.Y / 2f;
        // Depth budget scales with the wall's thickness: a 50px boundary wall carves ~40px deep over
        // repeated slams, the central block hundreds — the environment stays carvable all race.
        var profile = DeformationProfile.Wall with
        {
            MaxCrumpleDepth = CarveBudgetFraction * Mathf.Min(Size.X, Size.Y),
        };
        var silhouette = DeformableSilhouette.BoxWithSpacing(hw, hh, WallVertexSpacing,
            profile, MaxWallVertsPerEdge);

        var visual = new Polygon2D { Color = Color };
        AddChild(visual);
        var hitbox = new CollisionPolygon2D();
        AddChild(hitbox);

        // Deformable installs the rest silhouette onto both the visual and the collider in its ctor.
        _deformer = new Deformable(silhouette, visual, hitbox);
    }

    public override void _Process(double delta) => _deformer?.Step((float)delta);

    /// <summary>Dent the wall where a car struck it. <paramref name="worldContact"/> is the contact in
    /// world space, <paramref name="impactorVelocity"/> the car's incoming velocity (world), and
    /// <paramref name="magnitude"/> the speed into the wall (px/s) that scales the dent depth.</summary>
    public void TakeImpact(Vector2 worldContact, Vector2 impactorVelocity, float magnitude)
    {
        if (_deformer is null || magnitude <= 0f)
            return;

        // Pick the struck face by which edge the contact is NEAREST — not by its direction from the
        // wall's centre. On a long wall (3600×50) almost every contact points along the length, which
        // used to resolve to the wall's END CAP: the dent appeared hundreds of pixels from the hit.
        float hw = Size.X / 2f, hh = Size.Y / 2f;
        Vector2 local = ToLocal(worldContact);
        float slackX = hw - Mathf.Abs(local.X);          // distance in from the ±X (end) faces
        float slackY = hh - Mathf.Abs(local.Y);          // distance in from the ±Y (long) faces
        Vector2 faceNormal = slackX < slackY
            ? new Vector2(local.X >= 0f ? 1f : -1f, 0f)
            : new Vector2(0f, local.Y >= 0f ? 1f : -1f); // outward face the car hit
        Vector2 contact = faceNormal.X != 0f             // snap the contact onto that face
            ? new Vector2(faceNormal.X * hw, Mathf.Clamp(local.Y, -hh, hh))
            : new Vector2(Mathf.Clamp(local.X, -hw, hw), faceNormal.Y * hh);
        Vector2 pushDir = -faceNormal;                   // dent drives into the wall

        // Square-on hits flatten a wide patch; glancing hits poke a sharp dent. Derive from how
        // aligned the car's velocity is with the struck face normal.
        Vector2 localVel = impactorVelocity.Rotated(-GlobalRotation);
        float align = localVel.LengthSquared() > 0f ? Mathf.Abs(localVel.Normalized().Dot(faceNormal)) : 1f;
        float sharpness = Mathf.Clamp(1f - align, 0f, 1f);
        float halfWidth = Mathf.Lerp(WallFlatHalfWidth, WallCornerHalfWidth, sharpness);

        _deformer.OnImpact(new Indenter(Sys(contact), Sys(pushDir), halfWidth, sharpness), magnitude, ImpactZone.Front);
    }

    private static SysVec2 Sys(Vector2 v) => new(v.X, v.Y);
}
