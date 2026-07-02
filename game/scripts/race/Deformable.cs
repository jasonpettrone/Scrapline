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
    // Below this remaining per-vertex crunch (px), the ease is "settled" and we stop refreshing —
    // no per-frame work on an idle body. An absolute distance, so it behaves the same for a 40-vert
    // car and an 84-vert wall (an averaged-crumple delta does not).
    private const float SettleDistancePx = 0.05f;

    // Collision proxy is rebuilt at most this often, and only once some vertex has moved this far
    // since the last rebuild — the perf lever for the deforming hitbox (docs/09 §6/§7).
    private const float HitboxRebuildInterval = 0.1f;   // ≤10 Hz
    private const float HitboxDriftPx = 0.75f;
    private const int HitboxDecimation = 2;             // every 2nd vertex within a PRISTINE run

    // A vertex that has moved at least this far is part of a dent and is ALWAYS kept in the collider
    // — decimating dented vertices left the hitbox flat where the visual showed a carved pocket (the
    // "invisible wall": on a wall with ~90px vertex spacing, a whole dent fit between kept vertices).
    private const float DeformedKeepPx = 0.5f;

    private readonly DeformableSilhouette _silhouette;
    private readonly Polygon2D _visual;
    private readonly CollisionPolygon2D? _hitbox;
    private readonly Vector2[] _visualScratch;          // reused Godot buffer for the mesh
    private readonly bool[] _isCorner;                  // rest-shape corners, always kept in the collider
    private readonly List<Vector2> _hitboxBuild = new();// reused staging list for the collider ring
    private readonly SysVec2[] _hitboxSnapshot;         // per-vertex offsets at the last rebuild

    private bool _active;
    private float _hitboxTimer;

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
        _isCorner = new bool[_silhouette.VertexCount];
        for (int i = 0; i < _isCorner.Length; i++)
            _isCorner[i] = IsRestCorner(silhouette, i);
        _hitboxSnapshot = new SysVec2[_silhouette.VertexCount];

        WriteVisual();
        RebuildHitbox(); // install the rest ring on both mesh and collider
    }

    /// <summary>The Core silhouette, exposed for the readout and tests.</summary>
    public DeformableSilhouette Silhouette => _silhouette;

    /// <summary>The visual's fill colour — what shed panels inherit so debris reads as torn-off car.</summary>
    public Color PanelColor => _visual.Color;

    /// <summary>Register a shaped hit: dent the struck zone by <paramref name="magnitude"/>, with the
    /// dent's footprint set by <paramref name="indenter"/> (flat slam → wide flat dent, corner → sharp
    /// V). The dent eases in over the next <see cref="Step"/> calls.</summary>
    public void OnImpact(in Indenter indenter, float magnitude, ImpactZone zone)
    {
        _silhouette.ApplyHit(indenter, magnitude, zone);
        _active = true;
    }

    /// <summary>Full destruction — sheds every remaining panel and returns them so the owner can
    /// fling debris for each (docs/09 §4.3: the split-on-kill moment).</summary>
    public IReadOnlyList<ImpactZone> Shatter() => _silhouette.ShedAllPanels();

    /// <summary>Panels that crossed their shed threshold since the last call — the owner spawns a
    /// debris body per strip (see <see cref="BuildPanelStrips"/>) and deepens the tear.</summary>
    public IReadOnlyList<ImpactZone> ConsumeNewlyShedPanels() => _silhouette.ConsumeNewlyShedPanels();

    /// <summary>Ease the visible shape toward its accumulated target, refresh the mesh, and (on a
    /// slower cadence) rebuild the collision proxy. Cheap to call every frame: it no-ops once the
    /// crumple has settled until the next hit.</summary>
    public void Step(float dt)
    {
        if (!_active)
            return;

        _silhouette.Step(dt);
        WriteVisual();

        bool settled = _silhouette.MaxResidual < SettleDistancePx;

        if (_hitbox is not null && !VisualOnly)
        {
            // Update the hitbox on its own (slower) cadence, plus one guaranteed sync when the dent
            // settles so the resting collider exactly matches the resting visual.
            _hitboxTimer += dt;
            if (settled || (_hitboxTimer >= HitboxRebuildInterval && HitboxDrift() >= HitboxDriftPx))
            {
                RebuildHitbox();
                _hitboxTimer = 0f;
            }
        }

        if (settled)
            _active = false;
    }

    /// <summary>Restore the pristine shape — a full repair, or a rival respawning clean.</summary>
    public void Repair()
    {
        _silhouette.Repair();
        WriteVisual();
        RebuildHitbox();
        _active = false;
        _hitboxTimer = 0f;
    }

    /// <summary>A world-facing strip of the silhouette for one shed panel (local space): the deformed
    /// outer edge plus an inset inner edge, closed into a polygon a debris body can render and collide
    /// with. <paramref name="OutwardLocal"/> is the strip's mean outward direction (the fling line),
    /// <paramref name="AnchorLocal"/> its mean surface point, and <paramref name="HalfLength"/> half
    /// its extent along the surface — together enough to deepen the crumple where it tore off.</summary>
    public readonly record struct PanelStrip(Vector2[] Polygon, Vector2 OutwardLocal, Vector2 AnchorLocal, float HalfLength);

    /// <summary>Cut the panel strips for <paramref name="zone"/> from the current (deformed) shape —
    /// one strip per contiguous run of that zone's vertices (a car's Side zone yields both flanks).
    /// Godot spawns one debris body per strip. Empty when the ring has no distinct zones (walls).</summary>
    public List<PanelStrip> BuildPanelStrips(ImpactZone zone, float thickness)
    {
        var strips = new List<PanelStrip>();
        int n = _silhouette.VertexCount;

        // Start scanning from a vertex OUTSIDE the zone so a run that wraps the ring stays contiguous.
        int start = 0;
        while (start < n && _silhouette.ZoneOf(start) == zone)
            start++;
        if (start == n)
            return strips; // the whole ring is one zone (a wall) — no panel concept

        var run = new List<int>();
        for (int k = 1; k <= n; k++)
        {
            int i = (start + k) % n;
            if (_silhouette.ZoneOf(i) == zone)
            {
                run.Add(i);
                continue;
            }
            if (run.Count >= 2)
                strips.Add(CutStrip(run, thickness));
            run.Clear();
        }
        return strips;
    }

    private PanelStrip CutStrip(List<int> run, float thickness)
    {
        SysVec2 centroid = _silhouette.Centroid;
        int m = run.Count;
        var poly = new Vector2[m * 2];
        SysVec2 mean = SysVec2.Zero;

        for (int k = 0; k < m; k++)
        {
            SysVec2 v = _silhouette.CurrentVertex(run[k]);
            mean += v;
            SysVec2 fromCentre = v - centroid;
            SysVec2 inward = fromCentre.LengthSquared() > 0f
                ? SysVec2.Normalize(fromCentre) * -thickness
                : SysVec2.Zero;
            poly[k] = new Vector2(v.X, v.Y);
            poly[m * 2 - 1 - k] = new Vector2(v.X + inward.X, v.Y + inward.Y); // inner edge, reversed
        }

        mean /= m;
        SysVec2 outward = mean - centroid;
        Vector2 outwardG = outward.LengthSquared() > 0f
            ? new Vector2(outward.X, outward.Y).Normalized()
            : Vector2.Right;
        float halfLength = (poly[0] - poly[m - 1]).Length() / 2f;
        return new PanelStrip(poly, outwardG, new Vector2(mean.X, mean.Y), MathF.Max(halfLength, 4f));
    }

    /// <summary>Cut the current (deformed) ring into two hull halves across the body's midline
    /// (perpendicular to local +X) — the split-on-kill spectacle (docs/09 §2: enemies split on a
    /// takedown). Sides are classified on the REST shape (a perfect box, so the cut line crosses
    /// exactly two edges and each half stays one contiguous run); the emitted points are the
    /// deformed positions, so the halves carry every dent of the fight. Either half can be null if
    /// the cut degenerated — the caller falls back to panel shatter.</summary>
    public (Vector2[]? Front, Vector2[]? Rear) BuildSplitHalves()
    {
        int n = _silhouette.VertexCount;
        float cx = _silhouette.Centroid.X;
        var front = new List<Vector2>(n);
        var rear = new List<Vector2>(n);

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            SysVec2 ri = _silhouette.RestVertex(i);
            SysVec2 rj = _silhouette.RestVertex(j);
            SysVec2 di = _silhouette.CurrentVertex(i);

            bool onFront = ri.X >= cx;
            (onFront ? front : rear).Add(new Vector2(di.X, di.Y));

            if (onFront != rj.X >= cx)
            {
                // The ring crosses the midline between i and j: add the cut point to BOTH halves
                // (interpolated on the deformed edge at the rest crossing fraction).
                SysVec2 dj = _silhouette.CurrentVertex(j);
                float t = (cx - ri.X) / (rj.X - ri.X);
                var cut = new Vector2(di.X + (dj.X - di.X) * t, di.Y + (dj.Y - di.Y) * t);
                front.Add(cut);
                rear.Add(cut);
            }
        }

        return (front.Count >= 3 ? front.ToArray() : null,
                rear.Count >= 3 ? rear.ToArray() : null);
    }

    private void WriteVisual()
    {
        for (int i = 0; i < _visualScratch.Length; i++)
        {
            SysVec2 v = _silhouette.CurrentVertex(i);
            _visualScratch[i] = new Vector2(v.X, v.Y);
        }
        _visual.Polygon = _visualScratch;
    }

    /// <summary>The farthest any vertex has moved since the collider was last rebuilt — the rebuild
    /// trigger. Absolute distance per vertex, for the same reason as <see cref="SettleDistancePx"/>.</summary>
    private float HitboxDrift()
    {
        float max = 0f;
        for (int i = 0; i < _hitboxSnapshot.Length; i++)
        {
            float d = (_silhouette.Offset(i) - _hitboxSnapshot[i]).LengthSquared();
            if (d > max)
                max = d;
        }
        return MathF.Sqrt(max);
    }

    /// <summary>Rebuild the collider ring adaptively: rest-shape corners and every DENTED vertex are
    /// always kept (so the hitbox tracks the visual exactly where it matters), and only pristine
    /// straight runs are decimated to keep the convex decomposition cheap (docs/09 §4.4).</summary>
    private void RebuildHitbox()
    {
        if (VisualOnly || _hitbox is null)
            return;

        _hitboxBuild.Clear();
        int run = 0;
        for (int i = 0; i < _silhouette.VertexCount; i++)
        {
            bool dented = _silhouette.Offset(i).LengthSquared() >= DeformedKeepPx * DeformedKeepPx;
            if (_isCorner[i] || dented || ++run >= HitboxDecimation)
            {
                SysVec2 v = _silhouette.CurrentVertex(i);
                _hitboxBuild.Add(new Vector2(v.X, v.Y));
                run = 0;
            }
        }
        Vector2[] ring = _hitboxBuild.ToArray();

        // Core's fold guard keeps the full ring simple, but the decimated ring is a subsample and
        // can in rare shapes still cross itself — and a bad polygon makes the convex decomposition
        // fail loudly and leaves a broken collider. Validate first; on failure keep the last good
        // proxy (the snapshot stays stale, so the next cadence tick retries with a newer shape).
        if (Geometry2D.TriangulatePolygon(ring).Length == 0)
            return;

        _hitbox.Polygon = ring;

        for (int i = 0; i < _hitboxSnapshot.Length; i++)
            _hitboxSnapshot[i] = _silhouette.Offset(i);
    }

    private static bool IsRestCorner(DeformableSilhouette s, int i)
    {
        int n = s.VertexCount;
        SysVec2 prev = s.RestVertex((i + n - 1) % n);
        SysVec2 cur = s.RestVertex(i);
        SysVec2 next = s.RestVertex((i + 1) % n);
        SysVec2 a = cur - prev;
        SysVec2 b = next - cur;
        float cross = a.X * b.Y - a.Y * b.X;
        return MathF.Abs(cross) > 1e-3f * a.Length() * b.Length(); // any real turn = a structural corner
    }
}
