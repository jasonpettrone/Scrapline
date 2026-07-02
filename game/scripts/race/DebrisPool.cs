using Godot;

namespace Scrapline.Game.Race;

/// <summary>
/// The capped debris pool (docs/09 §4.3/§7): pre-builds <see cref="MaxActive"/> inert
/// <see cref="Debris"/> bodies and hands them out as cars shed panels. When every piece is live the
/// piece with the least linger left is recycled (oldest first), so a chaotic pile-up can never spawn
/// unbounded physics bodies. Owned by the <c>RaceScene</c>; cars get a reference and call
/// <see cref="Spawn"/> from their idle-frame shed handling (never inside the physics flush).
/// </summary>
public partial class DebrisPool : Node2D
{
    private const int MaxActive = 24;
    private const float LingerMinSeconds = 3f;
    private const float LingerMaxSeconds = 5f;
    private const float FlingSpeedMin = 180f;
    private const float FlingSpeedMax = 340f;
    private const float InheritVelocityShare = 0.5f;  // debris keeps some of the car's momentum
    private const float MaxSpinRadPerSec = 9f;
    private const float SpawnClearancePx = 10f;       // nudge off the shedding body's surface

    private readonly RandomNumberGenerator _rng = new();
    private Debris[] _pieces = System.Array.Empty<Debris>();

    public override void _Ready()
    {
        _pieces = new Debris[MaxActive];
        for (int i = 0; i < MaxActive; i++)
        {
            _pieces[i] = new Debris();
            AddChild(_pieces[i]);
        }
    }

    /// <summary>Fling one panel: <paramref name="polygon"/> in the shedding body's local space,
    /// spawned at its <paramref name="sourceTransform"/>, thrown along <paramref name="flingDirWorld"/>
    /// while inheriting a share of <paramref name="inheritVelocity"/> (so panels torn off at speed
    /// tumble down the track with the wreck, not against it). <paramref name="speedScale"/> scales
    /// the whole motion — hull halves use a low value so a split stays at the wreck site rather
    /// than exploding apart. <paramref name="clearancePx"/> nudges the spawn off the shedding
    /// body's surface (panel strips overlap it); halves pass 0 — they occupy the car's own,
    /// legally clear space, and a nudge could push them into an adjacent wall.</summary>
    public void Spawn(Vector2[] polygon, Color color, Transform2D sourceTransform,
        Vector2 flingDirWorld, Vector2 inheritVelocity,
        float speedScale = 1f, float clearancePx = SpawnClearancePx,
        Vector2[]? decal = null, Color decalColor = default)
    {
        Debris piece = FindReusable();
        if (piece is null)
            return; // pool not in the tree yet — nothing to fling

        Vector2 dir = flingDirWorld.LengthSquared() > 0f ? flingDirWorld.Normalized() : Vector2.Right;
        // A touch of tangent jitter so simultaneous panels scatter instead of stacking.
        Vector2 jitter = dir.Orthogonal() * _rng.RandfRange(-0.35f, 0.35f);
        Vector2 velocity = (inheritVelocity * InheritVelocityShare
            + (dir + jitter).Normalized() * _rng.RandfRange(FlingSpeedMin, FlingSpeedMax)) * speedScale;
        float spin = _rng.RandfRange(-MaxSpinRadPerSec, MaxSpinRadPerSec) * speedScale;

        Transform2D xform = sourceTransform;
        xform.Origin += dir * clearancePx;

        piece.Activate(polygon, color, xform, velocity, spin,
            _rng.RandfRange(LingerMinSeconds, LingerMaxSeconds), decal, decalColor);
    }

    /// <summary>Reclaim every live piece — the debug scene reset.</summary>
    public void Clear()
    {
        foreach (Debris piece in _pieces)
            if (piece.Active)
                piece.Deactivate();
    }

    private Debris FindReusable()
    {
        Debris oldest = null!;
        float least = float.MaxValue;
        foreach (Debris piece in _pieces)
        {
            if (!piece.Active)
                return piece;
            if (piece.RemainingLife < least)
            {
                least = piece.RemainingLife;
                oldest = piece;
            }
        }
        return oldest;
    }
}
