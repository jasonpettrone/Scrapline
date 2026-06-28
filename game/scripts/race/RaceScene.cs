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
    private Node2D? _car;
    private Camera2D? _camera;

    public override void _Ready()
    {
        BuildArena();
        SpawnCar(_config.PlayerCar);
        SetUpCamera();

        // Proves the seam on boot (and is what the headless smoke test asserts).
        GD.Print($"Race ready on '{_config.TrackId}' — car max speed {_config.PlayerCar.MaxSpeed} px/s.");
    }

    public override void _Process(double delta)
    {
        if (Input.IsPhysicalKeyPressed(Key.Escape))
            GetTree().Quit();

        if (_camera is not null && _car is not null)
            _camera.GlobalPosition = _car.GlobalPosition; // upright follow (camera doesn't rotate with the car)
    }

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
}
