using Godot;
using Scrapline.Core.Cars;
using Scrapline.Core.Contracts;

namespace Scrapline.Game.Race;

/// <summary>
/// Hosts a single race. For M0 it builds a grey-box walled arena in code, spawns the
/// player car configured from a <see cref="RaceConfig"/>, and follows it with a camera.
///
/// It runs standalone (F6, or as the main scene) using <see cref="RaceConfig.Default"/>,
/// which is the debug/playtest harness pattern: later, GameDirector will hand it a real
/// config instead. Keeping the scene independently launchable is what makes encounters
/// fast to playtest (and integration-testable).
/// </summary>
public partial class RaceScene : Node2D
{
    /// <summary>The car scene to instance. Assigned to res://scenes/race/Car.tscn.</summary>
    [Export] public PackedScene CarScene { get; set; } = null!;

    // Arena geometry (world pixels).
    private const float ArenaWidth = 2000f;
    private const float ArenaHeight = 1200f;
    private const float WallThickness = 40f;

    private RaceConfig _config = RaceConfig.Default;
    private CarController? _car;
    private Camera2D? _camera;
    private Label? _debugReadout;

    public override void _Ready()
    {
        BuildArena();
        SpawnBoostPads();
        SpawnCar(_config.PlayerCar);
        SetUpCamera();
        SetUpDebugReadout();

        // Proves the seam on boot (and is what the headless smoke test asserts).
        GD.Print($"Race ready on '{_config.TrackId}' — car max speed {_config.PlayerCar.MaxSpeed} px/s.");
    }

    public override void _Process(double delta)
    {
        if (Input.IsPhysicalKeyPressed(Key.Escape))
            GetTree().Quit();

        if (_camera is not null && _car is not null)
            _camera.GlobalPosition = _car.GlobalPosition; // upright follow (camera doesn't rotate with the car)

        UpdateDebugReadout();
    }

    public override void _ExitTree()
    {
        // M0 has no real race-end logic yet, so "race over" = the scene exiting. We still
        // produce a RaceResult to close the Core->Engine->Core loop: the engine builds the
        // Core seam contract and hands it back. M2's GameDirector will consume this for real.
        RaceResult result = BuildResult();
        GD.Print($"Race result: placement={result.Placement} hp={result.HpRemaining} outcome={result.Outcome}");
    }

    /// <summary>Placeholder result for M0 (no laps/HP yet) — proves the seam, not the rules.</summary>
    private static RaceResult BuildResult() => new()
    {
        Placement = 1,
        HpRemaining = 100,
        Outcome = RaceOutcome.Won,
    };

    private void BuildArena()
    {
        // Floor (drawn first so everything sits on top of it).
        AddChild(new Polygon2D
        {
            Color = new Color(0.13f, 0.14f, 0.18f),
            Polygon = new[]
            {
                Vector2.Zero, new Vector2(ArenaWidth, 0),
                new Vector2(ArenaWidth, ArenaHeight), new Vector2(0, ArenaHeight),
            },
        });

        var walls = new StaticBody2D { Name = "Walls" };
        AddChild(walls);

        var wallColor = new Color(0.45f, 0.47f, 0.55f);
        float w = ArenaWidth, h = ArenaHeight, t = WallThickness;

        // Four boundary walls.
        AddWall(walls, new Vector2(w / 2, t / 2), new Vector2(w, t), wallColor);           // top
        AddWall(walls, new Vector2(w / 2, h - t / 2), new Vector2(w, t), wallColor);       // bottom
        AddWall(walls, new Vector2(t / 2, h / 2), new Vector2(t, h), wallColor);           // left
        AddWall(walls, new Vector2(w - t / 2, h / 2), new Vector2(t, h), wallColor);       // right

        // Central block — turns the arena into a loop you drive around.
        AddWall(walls, new Vector2(w / 2, h / 2), new Vector2(900f, 500f),
            new Color(0.30f, 0.32f, 0.40f));
    }

    private void AddWall(StaticBody2D parent, Vector2 center, Vector2 size, Color color)
    {
        var collider = new CollisionShape2D
        {
            Position = center,
            Shape = new RectangleShape2D { Size = size },
        };
        parent.AddChild(collider);

        parent.AddChild(new Polygon2D
        {
            Position = center,
            Color = color,
            Polygon = new[]
            {
                new Vector2(-size.X / 2, -size.Y / 2), new Vector2(size.X / 2, -size.Y / 2),
                new Vector2(size.X / 2, size.Y / 2), new Vector2(-size.X / 2, size.Y / 2),
            },
        });
    }

    /// <summary>
    /// Hardcoded M1 pad layout around the loop (the ring between the outer walls and the centre
    /// block): launch + small pads on the straights, a large pad on the far side as a reward for
    /// committing to the top route. Seeded placement from RaceConfig comes later (docs/02).
    /// </summary>
    private void SpawnBoostPads()
    {
        // Launch pad on the bottom straight, flinging you along the direction of travel (+X).
        AddPad(BoostPad.PadKind.Launch, new Vector2(700f, ArenaHeight - 150f), rotation: 0f);

        // Small meter pads tucked into the side corridors.
        AddPad(BoostPad.PadKind.Small, new Vector2(ArenaWidth - 250f, ArenaHeight / 2f));
        AddPad(BoostPad.PadKind.Small, new Vector2(250f, ArenaHeight / 2f));

        // Large (fill-to-full) pad on the top straight — the high-value pickup.
        AddPad(BoostPad.PadKind.Large, new Vector2(ArenaWidth / 2f, 200f));
    }

    private void AddPad(BoostPad.PadKind kind, Vector2 position, float rotation = 0f)
    {
        AddChild(new BoostPad
        {
            Kind = kind,
            Position = position,
            Rotation = rotation,
        });
    }

    private void SpawnCar(CarStats stats)
    {
        var car = CarScene.Instantiate<CarController>();
        car.Configure(stats);
        car.Position = new Vector2(ArenaWidth / 2, ArenaHeight - 150f); // bottom straight
        car.Rotation = 0f;                                             // facing right
        AddChild(car);
        _car = car;
    }

    private void SetUpCamera()
    {
        _camera = new Camera2D { Zoom = new Vector2(0.7f, 0.7f) };
        AddChild(_camera);
        _camera.MakeCurrent();
    }

    /// <summary>
    /// A throwaway on-screen readout of the drift→boost loop (boost %, mini-turbo tier, drift/
    /// boost state). This is a playtest aid, NOT the real HUD (which comes later): you can't
    /// honestly judge whether the drift→boost feel is fun without seeing the meter fill and drain.
    /// </summary>
    private void SetUpDebugReadout()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        _debugReadout = new Label
        {
            Position = new Vector2(16, 16),
            Theme = null,
        };
        _debugReadout.AddThemeFontSizeOverride("font_size", 22);
        layer.AddChild(_debugReadout);
    }

    private void UpdateDebugReadout()
    {
        if (_debugReadout is null || _car is null)
            return;

        int pct = Mathf.RoundToInt(_car.BoostFraction * 100f);
        string drift = _car.IsDrifting ? "DRIFT" : "—";
        string boost = _car.IsLaunching ? "LAUNCH" : _car.IsBoosting ? "BOOST" : "—";
        _debugReadout.Text = $"Boost {pct,3}%   {drift}   {boost}";
    }
}
