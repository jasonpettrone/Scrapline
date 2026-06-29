using Godot;

namespace Scrapline.Game.Race;

/// <summary>
/// A static track hazard (docs/08 §2): a patch that damages any car driving through it. Damage is
/// the forgiving, i-frame-gated kind (see <see cref="CarController.TakeHazardDamage"/>), so a car
/// sitting in it ticks at the i-frame cadence rather than being deleted — and for the player it's
/// the only non-rival way to lose HP (walls and self-rams are free).
///
/// Self-contained like <see cref="BoostPad"/>: builds its own collision + placeholder visual so the
/// RaceScene can drop hazards in like walls. We damage on overlap each physics frame (not just on
/// entry) so the i-frames govern the rate uniformly.
/// </summary>
public partial class Hazard : Area2D
{
    /// <summary>Half-size of the square hazard footprint, world pixels.</summary>
    [Export] public float HalfExtent { get; set; } = 110f;

    public override void _Ready()
    {
        Monitoring = true;
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(HalfExtent * 2f, HalfExtent * 2f) },
        });
        AddChild(BuildVisual());
    }

    public override void _PhysicsProcess(double delta)
    {
        // TakeHazardDamage is i-frame-gated, so calling it every frame for everyone inside just
        // produces one chunk per i-frame window — exactly the cadence we want.
        foreach (Node2D body in GetOverlappingBodies())
            if (body is CarController car)
                car.TakeHazardDamage();
    }

    /// <summary>Placeholder hazard visual: an angry orange/red hatched square.</summary>
    private Polygon2D BuildVisual()
    {
        float e = HalfExtent;
        return new Polygon2D
        {
            Color = new Color(0.9f, 0.3f, 0.15f, 0.85f),
            Polygon = new[]
            {
                new Vector2(-e, -e), new Vector2(e, -e), new Vector2(e, e), new Vector2(-e, e),
            },
        };
    }
}
