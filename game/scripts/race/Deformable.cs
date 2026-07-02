using System;
using System.Collections.Generic;
using Godot;
using Scrapline.Core.Combat;
using Scrapline.Core.Destruction;
using SysVec2 = System.Numerics.Vector2;

namespace Scrapline.Game.Race;

/// <summary>
/// The Godot side of destruction (docs/09 §4.2/§4.4), shared by cars and walls: owns a pure-Core
/// <see cref="DeformableSilhouette"/> and renders it onto a <see cref="Polygon2D"/> visual AND onto a
/// deforming <see cref="CollisionPolygon2D"/> hitbox. It holds no game rules — the crumple math is all
/// in Core and unit-tested; this is the thin glue that feeds hits in and pushes the eased shape back
/// onto the mesh and the collision proxy. Owned by composition (a <c>CarController</c> or a
/// <c>DeformableWall</c>).
///
/// The hitbox is the docs' flagged risk: a dented silhouette is concave, which the solver can't use
/// directly, so we hand the polygon to a <c>CollisionPolygon2D</c> in <c>Solids</c> build mode — it
/// decomposes the concave shape into convex pieces. To keep that cheap it's rebuilt at <b>low cadence</b>
/// from a <b>decimated</b> vertex set, never per-frame. <see cref="VisualOnly"/> is the kill-switch back
/// to the doc's fallback (visuals dent, hitbox stays rigid).
/// </summary>
public sealed class Deformable
{
    // Below this frame-to-frame change in overall crumple, the ease is "settled" and we stop
    // refreshing — no per-frame work on an idle body.
    private const float SettleEpsilon = 0.0004f;

    // Collision proxy is rebuilt at most this often, and only once the shape has moved this much
    // since the last rebuild — the perf lever for the deforming hitbox (docs/09 §6/§7).
    private const float HitboxRebuildInterval = 0.1f;   // ≤10 Hz
    private const float HitboxCrumpleEpsilon = 0.006f;
    private const int HitboxDecimation = 2;             // every 2nd vertex → coarser convex-decomposed proxy

    private readonly DeformableSilhouette _silhouette;
    private readonly Polygon2D _visual;
    private readonly CollisionPolygon2D? _hitbox;
    private readonly Vector2[] _visualScratch;          // reused Godot buffer for the mesh
    private Vector2[] _hitboxScratch = Array.Empty<Vector2>();

    private bool _active;
    private float _lastAmount;
    private float _hitboxTimer;
    private float _hitboxLastAmount;

    /// <summary>When true the visual dents but the hitbox stays rigid — the doc's blessed fallback
    /// if the deforming collision proves a perf/balance problem.</summary>
    public bool VisualOnly { get; set; }

    public Deformable(DeformableSilhouette silhouette, Polygon2D visual, CollisionPolygon2D? hitbox, bool visualOnly = false)
    {
        _silhouette = silhouette;
        _visual = visual;
        _hitbox = hitbox;
        VisualOnly = visualOnly;
        _visualScratch = new Vector2[_silhouette.VertexCount];

        IReadOnlyList<SysVec2> rest = _silhouette.CurrentVertices();
        WriteVisual(rest);
        RebuildHitbox(rest); // install the denser rest ring on both mesh and collider
    }

    /// <summary>The Core silhouette, exposed for the readout and tests.</summary>
    public DeformableSilhouette Silhouette => _silhouette;

    /// <summary>Register a shaped hit: dent the struck zone by <paramref name="magnitude"/>, with the
    /// dent's footprint set by <paramref name="indenter"/> (flat slam → wide flat dent, corner → sharp
    /// V). The dent eases in over the next <see cref="Step"/> calls.</summary>
    public void OnImpact(in Indenter indenter, float magnitude, ImpactZone zone)
    {
        _silhouette.ApplyHit(indenter, magnitude, zone);
        _active = true;
    }

    /// <summary>Full destruction — sheds every remaining panel (debris spawns in Phase 3).</summary>
    public void Shatter() => _silhouette.ShedAllPanels();

    /// <summary>Ease the visible shape toward its accumulated target, refresh the mesh, and (on a
    /// slower cadence) rebuild the collision proxy. Cheap to call every frame: it no-ops once the
    /// crumple has settled until the next hit.</summary>
    public void Step(float dt)
    {
        if (!_active)
            return;

        _silhouette.Step(dt);
        IReadOnlyList<SysVec2> verts = _silhouette.CurrentVertices();
        WriteVisual(verts);

        float amount = _silhouette.CrumpleAmount;
        bool settled = Mathf.Abs(amount - _lastAmount) < SettleEpsilon;
        _lastAmount = amount;

        // Update the hitbox on its own (slower) cadence, plus one guaranteed sync when the dent settles
        // so the resting collider exactly matches the resting visual.
        _hitboxTimer += dt;
        if (settled
            || (_hitboxTimer >= HitboxRebuildInterval && Mathf.Abs(amount - _hitboxLastAmount) >= HitboxCrumpleEpsilon))
        {
            RebuildHitbox(verts);
            _hitboxTimer = 0f;
            _hitboxLastAmount = amount;
        }

        if (settled)
            _active = false;
    }

    /// <summary>Restore the pristine shape — a full repair, or a rival respawning clean.</summary>
    public void Repair()
    {
        _silhouette.Repair();
        IReadOnlyList<SysVec2> rest = _silhouette.CurrentVertices();
        WriteVisual(rest);
        RebuildHitbox(rest);
        _active = false;
        _lastAmount = 0f;
        _hitboxTimer = 0f;
        _hitboxLastAmount = 0f;
    }

    private void WriteVisual(IReadOnlyList<SysVec2> verts)
    {
        for (int i = 0; i < _visualScratch.Length; i++)
            _visualScratch[i] = new Vector2(verts[i].X, verts[i].Y);
        _visual.Polygon = _visualScratch;
    }

    private void RebuildHitbox(IReadOnlyList<SysVec2> verts)
    {
        if (VisualOnly || _hitbox is null)
            return;

        // Decimate to keep the convex decomposition cheap (docs/09 §4.4): a coarser collision shape
        // that still tracks the dents a car can drive into.
        int count = verts.Count;
        int n = (count + HitboxDecimation - 1) / HitboxDecimation;
        if (_hitboxScratch.Length != n)
            _hitboxScratch = new Vector2[n];

        int w = 0;
        for (int i = 0; i < count && w < n; i += HitboxDecimation)
            _hitboxScratch[w++] = new Vector2(verts[i].X, verts[i].Y);

        _hitbox.Polygon = _hitboxScratch;
    }
}
