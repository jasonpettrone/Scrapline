using Godot;

namespace Scrapline.Game.Race;

/// <summary>
/// One pooled debris panel (docs/09 §4.3): a low-mass <see cref="RigidBody2D"/> carrying a torn-off
/// polygon strip. Flung on spawn, it skids around as a collidable hazard for a few seconds — light
/// enough that cars plow through it (the scatter asymmetry comes from mass, not layers) — then fades
/// out and returns to the pool. A distinct type so <c>CarController</c> can tell debris from walls:
/// bouncing a shed bumper must neither chip HP nor count as a "pinning wall" for the sandwich rule.
/// Lifecycle is owned by <see cref="DebrisPool"/>; inert pieces are frozen, hidden, and collider-off.
/// </summary>
public partial class Debris : RigidBody2D
{
    private const float DebrisMass = 0.08f;   // ~1/12 of a car: plow & scatter (docs/09 §2)
    private const float SkidDamp = 2.2f;      // debris skids to rest rather than sliding forever
    private const float SpinDamp = 3f;
    private const float FadeSeconds = 0.8f;   // alpha tween at the end of the linger window

    private Polygon2D _visual = null!;
    private Polygon2D _decal = null!;   // optional second polygon riding the piece (the nose marker)
    private CollisionPolygon2D _shape = null!;
    private float _life;

    /// <summary>False while parked in the pool. Public so the pool can pick a free piece.</summary>
    public bool Active { get; private set; }

    /// <summary>Seconds of linger remaining — the pool recycles the piece with the least left
    /// when the cap is hit (oldest first, docs/09 §4.3).</summary>
    public float RemainingLife => _life;

    public override void _Ready()
    {
        GravityScale = 0f;
        Mass = DebrisMass;
        LinearDamp = SkidDamp;
        AngularDamp = SpinDamp;
        ContinuousCd = CcdMode.CastRay; // fast light pieces must not tunnel through arena walls

        _visual = new Polygon2D();
        AddChild(_visual);
        _decal = new Polygon2D { Visible = false };
        _visual.AddChild(_decal); // child of the visual so the fade modulate covers it too
        _shape = new CollisionPolygon2D();
        AddChild(_shape);

        Deactivate();
    }

    /// <summary>Bring the piece to life as <paramref name="polygon"/> (local space) at the shedding
    /// body's transform, flung with <paramref name="velocity"/> and <paramref name="spin"/>, alive
    /// for <paramref name="lifeSeconds"/> before fading back into the pool. A <paramref name="decal"/>
    /// (e.g. the car's nose marker) rides ON this piece as a second polygon — never as its own body:
    /// two overlapping rigid bodies get blown apart by the solver's depenetration.</summary>
    public void Activate(Vector2[] polygon, Color color, Transform2D sourceTransform,
        Vector2 velocity, float spin, float lifeSeconds,
        Vector2[]? decal = null, Color decalColor = default)
    {
        _visual.Polygon = polygon;
        _visual.Color = color;
        _visual.Modulate = Colors.White;
        _decal.Visible = decal is not null;
        if (decal is not null)
        {
            _decal.Polygon = decal;
            _decal.Color = decalColor;
        }
        _shape.Polygon = polygon;
        _shape.SetDeferred(CollisionPolygon2D.PropertyName.Disabled, false);

        Freeze = false;
        Sleeping = false;
        GlobalTransform = sourceTransform;
        LinearVelocity = velocity;
        AngularVelocity = spin;

        Visible = true;
        _life = lifeSeconds;
        Active = true;
    }

    /// <summary>Park the piece back in the pool: frozen, hidden, collider off.</summary>
    public void Deactivate()
    {
        Active = false;
        Freeze = true;
        Visible = false;
        LinearVelocity = Vector2.Zero;
        AngularVelocity = 0f;
        _shape.SetDeferred(CollisionPolygon2D.PropertyName.Disabled, true);
    }

    public override void _Process(double delta)
    {
        if (!Active)
            return;

        _life -= (float)delta;
        if (_life <= 0f)
        {
            Deactivate();
            return;
        }

        if (_life < FadeSeconds)
        {
            Color m = _visual.Modulate;
            m.A = _life / FadeSeconds;
            _visual.Modulate = m;
        }
    }
}
