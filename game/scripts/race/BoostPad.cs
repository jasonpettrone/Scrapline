using Godot;

namespace Scrapline.Game.Race;

/// <summary>
/// A track boost pad the car drives over (GDD §4 — "two kinds: some refill the boost meter,
/// some give an instant speed kick"). Three kinds:
/// • <see cref="PadKind.Small"/> — adds a chunk of meter fuel, then respawns on a cooldown.
/// • <see cref="PadKind.Large"/> — fills the meter to full, then respawns (longer cooldown).
/// • <see cref="PadKind.Launch"/> — instant forward impulse in the pad's facing direction,
///   independent of the meter; always active (environmental, no cooldown).
///
/// Self-contained: builds its own collision + placeholder visual in code so the RaceScene can
/// drop pads in like it drops walls. Pad values are world/track properties (exported), not car
/// stats. Placement is hardcoded for M1; seeded placement from RaceConfig comes later (docs/02).
/// </summary>
public partial class BoostPad : Area2D
{
    public enum PadKind { Small, Large, Launch }

    [Export] public PadKind Kind { get; set; } = PadKind.Small;

    /// <summary>Meter fuel a <see cref="PadKind.Small"/> pad grants (ignored by Large/Launch).</summary>
    [Export] public float Fill { get; set; } = 35f;

    /// <summary>Speed (px/s) a <see cref="PadKind.Launch"/> pad flings the car to along its facing.
    /// Set well above a car's MaxSpeed (~600) so the surge is unmistakable.</summary>
    [Export] public float LaunchSpeed { get; set; } = 1000f;

    /// <summary>Seconds before a consumed meter pad reactivates (Launch pads never go down).</summary>
    [Export] public float RespawnSeconds { get; set; } = 5f;

    /// <summary>Half-size of the square pad footprint, world pixels.</summary>
    [Export] public float HalfExtent { get; set; } = 55f;

    private bool _active = true;
    private double _cooldown;
    private Polygon2D _visual = null!;

    public override void _Ready()
    {
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(HalfExtent * 2f, HalfExtent * 2f) },
        });

        _visual = BuildVisual();
        AddChild(_visual);

        if (Kind == PadKind.Large)
            RespawnSeconds = Mathf.Max(RespawnSeconds, 8f); // big payoff → longer wait

        BodyEntered += OnBodyEntered;
    }

    public override void _Process(double delta)
    {
        if (_active)
            return;

        _cooldown -= delta;
        if (_cooldown <= 0.0)
            SetActive(true);
    }

    private void OnBodyEntered(Node body)
    {
        if (!_active || body is not CarController car)
            return;

        switch (Kind)
        {
            case PadKind.Small:
                car.RefillBoost(Fill);
                GoOnCooldown();
                break;
            case PadKind.Large:
                car.RefillBoostToFull();
                GoOnCooldown();
                break;
            case PadKind.Launch:
                car.Launch(Vector2.Right.Rotated(GlobalRotation), LaunchSpeed); // always active
                break;
        }
    }

    private void GoOnCooldown()
    {
        SetActive(false);
        _cooldown = RespawnSeconds;
    }

    private void SetActive(bool on)
    {
        _active = on;
        _visual.Modulate = on ? Colors.White : new Color(1f, 1f, 1f, 0.18f);
    }

    /// <summary>Placeholder colour-coded visual: cyan small, gold large, green launch arrow.</summary>
    private Polygon2D BuildVisual()
    {
        float e = HalfExtent;
        if (Kind == PadKind.Launch)
        {
            // Chevron pointing along +X (the launch direction) so the kick reads at a glance.
            return new Polygon2D
            {
                Color = new Color(0.3f, 0.95f, 0.45f),
                Polygon = new[]
                {
                    new Vector2(-e, -e), new Vector2(0f, -e), new Vector2(e, 0f),
                    new Vector2(0f, e), new Vector2(-e, e), new Vector2(0f, 0f),
                },
            };
        }

        Color color = Kind == PadKind.Large
            ? new Color(1f, 0.82f, 0.25f)   // gold
            : new Color(0.25f, 0.7f, 1f);   // cyan
        return new Polygon2D
        {
            Color = color,
            Polygon = new[]
            {
                new Vector2(-e, -e), new Vector2(e, -e), new Vector2(e, e), new Vector2(-e, e),
            },
        };
    }
}
