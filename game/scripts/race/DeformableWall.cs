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
    private const int MaxWallVertsPerEdge = 40;    // cap so huge boundary walls stay cheap
    private const float WallFlatHalfWidth = 26f;   // a square-on slam caves about a car's width
    private const float WallCornerHalfWidth = 4f;  // a glancing/corner hit pokes a narrow dent

    /// <summary>Wall dimensions (px). Set by the builder before the node enters the tree.</summary>
    public Vector2 Size { get; set; } = new(100f, 100f);

    /// <summary>Fill colour for the wall's visual.</summary>
    public Color Color { get; set; } = new(0.45f, 0.47f, 0.55f);

    private Deformable? _deformer;

    public override void _Ready()
    {
        float hw = Size.X / 2f, hh = Size.Y / 2f;
        var silhouette = DeformableSilhouette.BoxWithSpacing(hw, hh, WallVertexSpacing,
            DeformationProfile.Wall, MaxWallVertsPerEdge);

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

        float hw = Size.X / 2f, hh = Size.Y / 2f;
        Vector2 local = ToLocal(worldContact);
        Vector2 dir = local.LengthSquared() > 0f ? local.Normalized() : Vector2.Right;
        Vector2 faceNormal = FaceNormal(dir);            // outward face the car hit
        Vector2 contact = EdgePoint(dir, hw, hh);        // snap onto that face
        Vector2 pushDir = -faceNormal;                   // dent drives into the wall

        // Square-on hits flatten a wide patch; glancing hits poke a sharp dent. Derive from how
        // aligned the car's velocity is with the struck face normal.
        Vector2 localVel = impactorVelocity.Rotated(-GlobalRotation);
        float align = localVel.LengthSquared() > 0f ? Mathf.Abs(localVel.Normalized().Dot(faceNormal)) : 1f;
        float sharpness = Mathf.Clamp(1f - align, 0f, 1f);
        float halfWidth = Mathf.Lerp(WallFlatHalfWidth, WallCornerHalfWidth, sharpness);

        _deformer.OnImpact(new Indenter(Sys(contact), Sys(pushDir), halfWidth, sharpness), magnitude, ImpactZone.Front);
    }

    private static Vector2 FaceNormal(Vector2 dir) =>
        Mathf.Abs(dir.X) >= Mathf.Abs(dir.Y)
            ? new Vector2(Mathf.Sign(dir.X), 0f)
            : new Vector2(0f, Mathf.Sign(dir.Y));

    private static Vector2 EdgePoint(Vector2 dir, float halfWidth, float halfHeight)
    {
        float ax = Mathf.Abs(dir.X), ay = Mathf.Abs(dir.Y);
        float tx = ax > 1e-5f ? halfWidth / ax : float.MaxValue;
        float ty = ay > 1e-5f ? halfHeight / ay : float.MaxValue;
        return dir * Mathf.Min(tx, ty);
    }

    private static SysVec2 Sys(Vector2 v) => new(v.X, v.Y);
}
