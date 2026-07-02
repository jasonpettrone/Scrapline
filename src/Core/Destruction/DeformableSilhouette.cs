using System;
using System.Collections.Generic;
using System.Numerics;
using Scrapline.Core.Combat;

namespace Scrapline.Core.Destruction;

/// <summary>
/// The pure crumple geometry for one car (docs/09 §4.1): a ring of local-space vertices that dent
/// when hit. A hit drives the vertices on the <em>struck-facing half</em> along the impactor's
/// push direction, by a depth that follows the impactor's <em>shape</em> across the surface — a
/// flat face caves a wide flat-bottomed dent, a corner caves a narrow V. Dents accumulate across a
/// fight and ease in over time so a slam <em>crunches</em> rather than snapping.
///
/// Godot-free (<see cref="Vector2"/> is <c>System.Numerics</c>), so the math is unit-tested like
/// <see cref="DamageModel"/>; the Godot layer renders <see cref="CurrentVertices"/>, rebuilds the
/// collision proxy from it, and spawns debris for the panels this reports as shed.
///
/// Local frame: <c>+X</c> forward (matching <c>CarController.ZoneFacing</c>), so a vertex's angle
/// from the centre tags it Front/Side/Rear. Distances are world pixels; the hit "magnitude" is the
/// <b>HP damage sustained</b> (docs/09 §2) — a harder hit is a deeper dent.
/// </summary>
public sealed class DeformableSilhouette
{
    // A dent can reach at most this fraction of a vertex's distance to the centre, so even a
    // pulverised panel can't fold through the middle and invert the shape.
    private const float MaxDepthFraction = 0.85f;

    // Fold guard: a vertex's accumulated slide ALONG the surface (tangentially) is capped at this
    // fraction of its rest distance to the nearer neighbour. Two neighbours sliding toward each
    // other by 0.45× each still leaves a 0.1× gap, so vertices can never pass one another and the
    // ring can never self-intersect — which broke both Godot's polygon triangulation (the car's
    // body mesh vanished) and the collider's convex decomposition under repeated angled hits.
    private const float TangentSlideFraction = 0.45f;

    // Default footprint for the convenience ApplyHit (no impactor geometry): a modest, slightly
    // rounded poke. Used by the crush pin and tests.
    private const float DefaultHalfWidth = 8f;
    private const float DefaultSharpness = 0.35f;

    private readonly Vector2[] _rest;          // rest local-space vertices (immutable shape)
    private readonly ImpactZone[] _zones;      // which face each vertex belongs to
    private readonly float[] _maxDepth;        // per-vertex displacement cap (px)
    private readonly Vector2[] _tangent;       // rest surface direction at each vertex (unit)
    private readonly float[] _tangentLimit;    // per-vertex cap on accumulated tangential slide (px)
    private readonly Vector2[] _target;        // accumulated target offset per vertex (px)
    private readonly Vector2[] _current;       // eased current offset per vertex (px)

    private readonly Dictionary<ImpactZone, float> _zoneDamage = new();
    private readonly HashSet<ImpactZone> _shed = new();
    private readonly List<ImpactZone> _newlyShed = new();

    public DeformationProfile Profile { get; }

    /// <summary>The car's centre (centroid of the rest shape), in local space.</summary>
    public Vector2 Centroid { get; }

    public int VertexCount => _rest.Length;

    public DeformableSilhouette(IReadOnlyList<Vector2> restVertices, DeformationProfile profile)
    {
        if (restVertices is null || restVertices.Count < 3)
            throw new ArgumentException("A silhouette needs at least 3 vertices.", nameof(restVertices));

        Profile = profile;
        int n = restVertices.Count;
        _rest = new Vector2[n];
        for (int i = 0; i < n; i++)
            _rest[i] = restVertices[i];

        Vector2 sum = Vector2.Zero;
        for (int i = 0; i < n; i++)
            sum += _rest[i];
        Centroid = sum / n;

        _zones = new ImpactZone[n];
        _maxDepth = new float[n];
        _tangent = new Vector2[n];
        _tangentLimit = new float[n];
        _target = new Vector2[n];
        _current = new Vector2[n];

        for (int i = 0; i < n; i++)
        {
            Vector2 fromCentre = _rest[i] - Centroid;
            _maxDepth[i] = MathF.Min(profile.MaxCrumpleDepth, fromCentre.Length() * MaxDepthFraction);
            _zones[i] = ZoneOfDirection(fromCentre);

            // The rest surface direction and neighbour spacing at this vertex, for the fold guard.
            Vector2 prev = _rest[(i + n - 1) % n];
            Vector2 next = _rest[(i + 1) % n];
            Vector2 along = next - prev;
            _tangent[i] = along.LengthSquared() > 0f ? Vector2.Normalize(along) : Vector2.Zero;
            float nearestNeighbour = MathF.Min((_rest[i] - prev).Length(), (next - _rest[i]).Length());
            _tangentLimit[i] = nearestNeighbour * TangentSlideFraction;
        }
    }

    /// <summary>A rectangular car silhouette: <paramref name="perEdge"/> vertices along each of the
    /// four edges (so 4×perEdge total), walked as a closed ring. More vertices = a higher-resolution
    /// dent. The placeholder default — real cars later pass their own outline to the constructor.</summary>
    public static DeformableSilhouette Box(float halfWidth, float halfHeight, int perEdge, DeformationProfile profile)
    {
        if (perEdge < 1)
            throw new ArgumentOutOfRangeException(nameof(perEdge), "Need at least one vertex per edge.");

        var pts = new List<Vector2>(perEdge * 4);
        // Four corners walked clockwise in Godot's Y-down space; each edge contributes its start
        // corner plus interior points, dropping the end corner (the next edge's start) to avoid dupes.
        Vector2[] corners =
        {
            new(-halfWidth, -halfHeight), // top-left
            new(halfWidth, -halfHeight),  // top-right
            new(halfWidth, halfHeight),   // bottom-right
            new(-halfWidth, halfHeight),  // bottom-left
        };
        for (int e = 0; e < 4; e++)
        {
            Vector2 a = corners[e];
            Vector2 b = corners[(e + 1) % 4];
            for (int s = 0; s < perEdge; s++)
                pts.Add(Vector2.Lerp(a, b, s / (float)perEdge));
        }
        return new DeformableSilhouette(pts, profile);
    }

    /// <summary>A rectangular silhouette with <em>even vertex density</em>: vertices spaced ~every
    /// <paramref name="spacing"/> px along each edge (so a long thin wall isn't starved on its length
    /// or crammed on its thickness), capped at <paramref name="maxPerEdge"/> for perf. Used for walls,
    /// whose aspect ratios are extreme.</summary>
    public static DeformableSilhouette BoxWithSpacing(
        float halfWidth, float halfHeight, float spacing, DeformationProfile profile, int maxPerEdge = 40)
    {
        if (spacing <= 0f)
            throw new ArgumentOutOfRangeException(nameof(spacing), "Spacing must be positive.");

        int perH = Math.Clamp((int)MathF.Round(2f * halfWidth / spacing), 1, maxPerEdge);  // top/bottom edges
        int perV = Math.Clamp((int)MathF.Round(2f * halfHeight / spacing), 1, maxPerEdge);  // left/right edges

        Vector2[] corners =
        {
            new(-halfWidth, -halfHeight), new(halfWidth, -halfHeight),
            new(halfWidth, halfHeight), new(-halfWidth, halfHeight),
        };
        int[] perEdge = { perH, perV, perH, perV };

        var pts = new List<Vector2>(2 * (perH + perV));
        for (int e = 0; e < 4; e++)
        {
            Vector2 a = corners[e];
            Vector2 b = corners[(e + 1) % 4];
            for (int s = 0; s < perEdge[e]; s++)
                pts.Add(Vector2.Lerp(a, b, s / (float)perEdge[e]));
        }
        return new DeformableSilhouette(pts, profile);
    }

    /// <summary>The rest (undamaged) position of vertex <paramref name="i"/>.</summary>
    public Vector2 RestVertex(int i) => _rest[i];

    /// <summary>The current (eased) offset of vertex <paramref name="i"/> from its rest position.</summary>
    public Vector2 Offset(int i) => _current[i];

    /// <summary>The current (eased) deformed position of vertex <paramref name="i"/> in local space —
    /// the allocation-free counterpart of <see cref="CurrentVertices"/> for per-frame readers.</summary>
    public Vector2 CurrentVertex(int i) => _rest[i] + _current[i];

    /// <summary>Which face vertex <paramref name="i"/> belongs to.</summary>
    public ImpactZone ZoneOf(int i) => _zones[i];

    /// <summary>
    /// Register a shaped hit: drive the struck-facing vertices along <see cref="Indenter.PushDir"/>
    /// by <paramref name="damage"/> × profile scale, with the depth profile across the surface set by
    /// the impactor (flat-bottomed for a face, V for a corner). Only the half of the body facing the
    /// push is affected — the far side never moves. The dent eases in over subsequent
    /// <see cref="Step"/> calls.
    /// </summary>
    public void ApplyHit(in Indenter indenter, float force, ImpactZone zone)
    {
        if (force <= 0f)
            return;

        Vector2 push = indenter.PushDir.LengthSquared() > 0f ? Vector2.Normalize(indenter.PushDir) : Vector2.Zero;
        if (push == Vector2.Zero)
        {
            AccrueZone(zone, force); // still counts toward shedding even if direction is degenerate
            return;
        }

        float depth = MathF.Min(force * Profile.CrumpleScale, Profile.MaxHitDepth);
        Vector2 tangent = new(-push.Y, push.X);
        float core = MathF.Max(0f, indenter.HalfWidth * (1f - Clamp01(indenter.Sharpness)));
        float reach = MathF.Max(0.001f, Lerp(Profile.FlatEdgeFalloff, Profile.SharpReach, Clamp01(indenter.Sharpness)));

        for (int i = 0; i < _rest.Length; i++)
        {
            // Only the struck-facing half dents: a vertex whose outward direction opposes the push.
            // This is the fix for the far side caving in.
            Vector2 outward = _rest[i] - Centroid;
            if (Vector2.Dot(outward, push) >= 0f)
                continue;

            // Depth across the surface follows the impactor's footprint: full inside the flat core,
            // then a straight ramp to zero over `reach` (crisp edges for a face, a clean V for a corner).
            float lateral = MathF.Abs(Vector2.Dot(_rest[i] - indenter.ContactPoint, tangent));
            float shape = lateral <= core ? 1f : MathF.Max(0f, 1f - (lateral - core) / reach);
            if (shape <= 0f)
                continue;

            Vector2 next = _target[i] + push * (depth * shape);

            // Fold guard: cap the accumulated slide along the rest surface so this vertex can never
            // pass its neighbours (a self-intersecting ring breaks Godot's triangulation/decomposition).
            float slide = Vector2.Dot(next, _tangent[i]);
            float limit = _tangentLimit[i];
            if (slide > limit)
                next -= _tangent[i] * (slide - limit);
            else if (slide < -limit)
                next -= _tangent[i] * (slide + limit);

            float len = next.Length();
            if (len > _maxDepth[i])
                next *= _maxDepth[i] / len; // clamp accumulated depth so it can't fold through centre
            _target[i] = next;
        }

        ResolveCrossings();
        AccrueZone(zone, force);
    }

    /// <summary>The hard simplicity guarantee. The fold guard prevents the common failure (a vertex
    /// sliding past its neighbour), but deep dents arriving from TWO faces can still cross near a
    /// corner. Scan the accumulated target ring for crossing edge pairs and relax the involved
    /// vertices toward rest until simple — rest itself is simple, so this always terminates. O(n²)
    /// per pass but only on a hit, never per frame.</summary>
    private void ResolveCrossings()
    {
        const int MaxPasses = 12;
        const float Relax = 0.75f;
        int n = _rest.Length;

        for (int pass = 0; pass < MaxPasses; pass++)
        {
            bool crossed = false;
            for (int i = 0; i < n; i++)
            {
                Vector2 a1 = _rest[i] + _target[i];
                Vector2 a2 = _rest[(i + 1) % n] + _target[(i + 1) % n];
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1)
                        continue; // adjacent through the wrap
                    Vector2 b1 = _rest[j] + _target[j];
                    Vector2 b2 = _rest[(j + 1) % n] + _target[(j + 1) % n];
                    if (!SegmentsCross(a1, a2, b1, b2))
                        continue;

                    _target[i] *= Relax;
                    _target[(i + 1) % n] *= Relax;
                    _target[j] *= Relax;
                    _target[(j + 1) % n] *= Relax;
                    crossed = true;
                    a1 = _rest[i] + _target[i];
                    a2 = _rest[(i + 1) % n] + _target[(i + 1) % n];
                }
            }
            if (!crossed)
                return;
        }
    }

    private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        static float Orient(Vector2 p, Vector2 q, Vector2 r) =>
            (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        float o1 = Orient(a, b, c);
        float o2 = Orient(a, b, d);
        float o3 = Orient(c, d, a);
        float o4 = Orient(c, d, b);
        return o1 * o2 < 0f && o3 * o4 < 0f; // strictly proper crossings only
    }

    /// <summary>A generic localized poke when the impactor's geometry isn't known (the crush pin,
    /// fallbacks): pushes inward from <paramref name="contactPoint"/> toward the centre with a modest,
    /// slightly rounded footprint.</summary>
    public void ApplyHit(Vector2 contactPoint, float force, ImpactZone zone)
    {
        Vector2 inward = Centroid - contactPoint;
        Vector2 push = inward.LengthSquared() > 0f ? Vector2.Normalize(inward) : Vector2.Zero;
        ApplyHit(new Indenter(contactPoint, push, DefaultHalfWidth, DefaultSharpness), force, zone);
    }

    /// <summary>Ease the visible shape toward the accumulated target by <paramref name="dt"/>
    /// seconds — framerate-independent exponential approach (the same decay form the car's grip
    /// uses), so the crunch settles in smoothly regardless of frame rate.</summary>
    public void Step(float dt)
    {
        if (dt <= 0f)
            return;
        float t = 1f - MathF.Exp(-Profile.EaseRate * dt);
        for (int i = 0; i < _current.Length; i++)
            _current[i] += (_target[i] - _current[i]) * t;
    }

    /// <summary>The current (eased) deformed vertices in local space — what Godot renders and
    /// derives the collision proxy from. Allocates a fresh array; call it only when deformation
    /// has changed (the Godot side gates on <see cref="CrumpleAmount"/>).</summary>
    public IReadOnlyList<Vector2> CurrentVertices()
    {
        var outv = new Vector2[_rest.Length];
        for (int i = 0; i < _rest.Length; i++)
            outv[i] = _rest[i] + _current[i];
        return outv;
    }

    /// <summary>The largest remaining distance (px) between any vertex's eased position and its
    /// accumulated target — how much crunch is still in flight. 0 once the dent has fully settled.
    /// The Godot layer uses this to stop per-frame work: unlike a delta of <see cref="CrumpleAmount"/>
    /// (which averages over the whole ring and so shrinks with vertex count, freezing small dents on
    /// big walls half-eased), this is an absolute per-vertex distance and scales with nothing.</summary>
    public float MaxResidual
    {
        get
        {
            float max = 0f;
            for (int i = 0; i < _current.Length; i++)
            {
                float d = (_target[i] - _current[i]).LengthSquared();
                if (d > max)
                    max = d;
            }
            return MathF.Sqrt(max);
        }
    }

    /// <summary>Overall crumple severity, 0 (pristine) → 1 (every vertex at its cap). Drives VFX
    /// intensity and lets the Godot layer rebuild the collision proxy only when the shape has
    /// moved enough to matter (docs/09 §6).</summary>
    public float CrumpleAmount
    {
        get
        {
            float acc = 0f;
            int counted = 0;
            for (int i = 0; i < _current.Length; i++)
            {
                if (_maxDepth[i] <= 0f)
                    continue;
                acc += _current[i].Length() / _maxDepth[i];
                counted++;
            }
            return counted > 0 ? acc / counted : 0f;
        }
    }

    /// <summary>Panels that crossed their shed threshold since the last call (and haven't shed
    /// before) — Godot spawns a debris body for each and tears it from the visual. Empty for a
    /// sober (player) profile until <see cref="ShedAllPanels"/> on death.</summary>
    public IReadOnlyList<ImpactZone> ConsumeNewlyShedPanels()
    {
        if (_newlyShed.Count == 0)
            return Array.Empty<ImpactZone>();
        var outv = _newlyShed.ToArray();
        _newlyShed.Clear();
        return outv;
    }

    /// <summary>Shed every panel not already gone — the split-on-kill (enemy) / full-destruction
    /// (player death) moment. Returns the panels that newly come off so Godot can fling them all.</summary>
    public IReadOnlyList<ImpactZone> ShedAllPanels()
    {
        var outv = new List<ImpactZone>();
        foreach (ImpactZone zone in new[] { ImpactZone.Front, ImpactZone.Side, ImpactZone.Rear })
        {
            if (_shed.Add(zone))
                outv.Add(zone);
        }
        _newlyShed.Clear(); // these supersede any pending mid-fight sheds
        return outv;
    }

    /// <summary>Restore the car to pristine — a full repair (docs/09 §6). Clears all dents,
    /// accrued zone damage, and shed state.</summary>
    public void Repair()
    {
        Array.Clear(_target);
        Array.Clear(_current);
        _zoneDamage.Clear();
        _shed.Clear();
        _newlyShed.Clear();
    }

    private void AccrueZone(ImpactZone zone, float damage)
    {
        if (!Profile.ShedsWhileAlive || _shed.Contains(zone))
            return;

        _zoneDamage.TryGetValue(zone, out float total);
        total += damage;
        _zoneDamage[zone] = total;

        if (total >= Profile.PanelShedThreshold && _shed.Add(zone))
            _newlyShed.Add(zone);
    }

    private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Tag a vertex by the angle of its offset from the centre: within 45° of forward
    /// (+X) is Front, within 45° of backward is Rear, the flanks are Side — matching the damage
    /// model's <c>ZoneFacing</c> so visuals and HP localization agree.</summary>
    private static ImpactZone ZoneOfDirection(Vector2 fromCentre)
    {
        if (fromCentre.LengthSquared() <= 0f)
            return ImpactZone.Side;
        float angle = MathF.Abs(MathF.Atan2(fromCentre.Y, fromCentre.X) * (180f / MathF.PI));
        if (angle <= 45f)
            return ImpactZone.Front;
        if (angle >= 135f)
            return ImpactZone.Rear;
        return ImpactZone.Side;
    }
}
