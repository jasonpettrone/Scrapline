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

    // Arena geometry (world pixels). Big enough to build real momentum into a hit (M1 feel pass).
    private const float ArenaWidth = 3600f;
    private const float ArenaHeight = 2200f;
    private const float WallThickness = 50f;
    private static readonly Vector2 CentralBlockSize = new(1500f, 900f); // the loop's middle island

    // Takedown aftertouch + respawn (docs/08 §3). Tunable; the slow-mo intensity will read from
    // the accessibility settings in Task 7.
    private const float SlowMoScale = 0.35f;       // how slow time gets at the peak of a takedown
    private const ulong SlowMoDurationMs = 800;    // real-time length of the slow-mo ramp
    private const float RespawnMinDelay = 1.5f;    // seconds; a glancing wreck respawns soonest
    private const float RespawnMaxDelay = 4.0f;    // a huge slam sets the rival back longest
    private const float RespawnForceScale = 0.0025f;
    private const float RespawnDistance = 420f;     // how far behind the player a rival reappears
    private const float RespawnMargin = 200f;       // keep respawns this far inside walls/obstacles

    // Start transforms (shared by initial spawn and the debug reset).
    private static readonly Vector2 PlayerStart = new(ArenaWidth / 2f, ArenaHeight - 180f);
    private const float PlayerStartRotation = 0f;                       // facing right
    private static readonly Vector2 DummyStart = new(ArenaWidth / 2f + 700f, ArenaHeight - 180f);
    private const float DummyStartRotation = -Mathf.Pi / 2f;           // facing across the player's path

    private RaceConfig _config = RaceConfig.Default;
    private CarController? _car;
    private CarController? _dummy;
    private Camera2D? _camera;
    private Label? _debugReadout;

    private ulong _slowMoEndMs;   // wall-clock end of the current slow-mo (0 = none)
    private ulong _respawnAtMs;   // wall-clock time to respawn the dummy (0 = none)

    public override void _Ready()
    {
        BuildArena();
        SpawnBoostPads();
        SpawnHazards();
        SpawnCar(_config.PlayerCar);
        SpawnPracticeDummy();
        SetUpCamera();
        SetUpDebugReadout();

        // Proves the seam on boot (and is what the headless smoke test asserts).
        GD.Print($"Race ready on '{_config.TrackId}' — car max speed {_config.PlayerCar.MaxSpeed} px/s.");
    }

    public override void _Process(double delta)
    {
        if (Input.IsPhysicalKeyPressed(Key.Escape))
            GetTree().Quit();

        if (Input.IsActionJustPressed("reset_scene"))
            ResetScene();

        if (_camera is not null && _car is not null)
            _camera.GlobalPosition = _car.GlobalPosition; // upright follow (camera doesn't rotate with the car)

        UpdateSlowMo();
        UpdateRespawn();
        UpdateDebugReadout();
    }

    public override void _ExitTree()
    {
        // No race-end flow yet (laps/win-lose are Task 6), so "race over" = the scene exiting. We
        // still produce a RaceResult to close the Core->Engine->Core loop. HP is now real (Task 2);
        // placement is still a placeholder until lap counting lands.
        RaceResult result = BuildResult();
        GD.Print($"Race result: placement={result.Placement} hp={result.HpRemaining} outcome={result.Outcome}");
    }

    /// <summary>Real HP/outcome from the player car (placement is still a Task-6 placeholder).</summary>
    private RaceResult BuildResult() => new()
    {
        Placement = 1,
        HpRemaining = _car is null ? 0 : Mathf.CeilToInt(_car.CurrentHp),
        Outcome = _car is { IsWrecked: true } ? RaceOutcome.Wrecked : RaceOutcome.Won,
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
        AddWall(walls, new Vector2(w / 2, h / 2), CentralBlockSize,
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
        car.IsPlayer = true;                   // the forgiving damage model (docs/08 §2)
        car.Position = PlayerStart;            // bottom straight
        car.Rotation = PlayerStartRotation;    // facing right
        AddChild(car);
        _car = car;
    }

    /// <summary>One M1 hazard so the player's forgiving damage model is playtestable: drive through
    /// the orange patch to feel the fixed chunk + i-frame blink (walls and self-rams cost nothing).
    /// Tucked on the left corridor, out of the start line. Seeded placement comes later (docs/02).</summary>
    private void SpawnHazards()
    {
        AddChild(new Hazard { Position = new Vector2(350f, ArenaHeight - 450f) });
    }

    /// <summary>
    /// A throwaway target so the damage model is playtestable before the real AI lands (Task 5):
    /// an inert, blue-tinted car parked on the bottom straight, facing across the player's path so
    /// you can hit its side/rear (clean) or circle to its front (botched). It still takes damage
    /// and gets shoved like any RigidBody2D.
    /// </summary>
    private void SpawnPracticeDummy()
    {
        var dummy = CarScene.Instantiate<CarController>();
        dummy.Configure(_config.PlayerCar);
        dummy.InputEnabled = false;
        dummy.BaseTint = new Color(0.5f, 0.65f, 1f); // distinct from the red player
        dummy.Position = DummyStart;
        dummy.Rotation = DummyStartRotation;         // facing "up", across the player's approach
        dummy.Wrecked += OnDummyWrecked;
        AddChild(dummy);
        _dummy = dummy;
    }

    /// <summary>
    /// A rival takedown (docs/08 §3): freeze the wreck out of play, fire the aftertouch slow-mo on
    /// big hits, and schedule a respawn whose delay scales with how hard it was hit.
    /// </summary>
    private void OnDummyWrecked(float hitForce)
    {
        if (_dummy is null)
            return;

        _dummy.SetInert(true); // freeze, hide, AND disable its collider so the wreck doesn't block traffic

        // Every takedown earns the aftertouch slow-mo (it IS the celebration); the respawn delay
        // scales with how hard the rival was hit, so a big slam buys a longer tempo swing.
        TriggerSlowMo();

        float delay = Mathf.Clamp(RespawnMinDelay + hitForce * RespawnForceScale, RespawnMinDelay, RespawnMaxDelay);
        _respawnAtMs = Time.GetTicksMsec() + (ulong)(delay * 1000f);
    }

    private void TriggerSlowMo()
    {
        Engine.TimeScale = SlowMoScale;
        _slowMoEndMs = Time.GetTicksMsec() + SlowMoDurationMs;
    }

    /// <summary>Ease time back to normal over the slow-mo window (real-time, so TimeScale-proof).</summary>
    private void UpdateSlowMo()
    {
        if (_slowMoEndMs == 0)
            return;

        ulong now = Time.GetTicksMsec();
        if (now >= _slowMoEndMs)
        {
            Engine.TimeScale = 1.0;
            _slowMoEndMs = 0;
            return;
        }

        float remaining = (_slowMoEndMs - now) / (float)SlowMoDurationMs; // 1 → 0
        Engine.TimeScale = Mathf.Lerp(1.0f, SlowMoScale, remaining);
    }

    private void UpdateRespawn()
    {
        if (_respawnAtMs == 0 || _car is null || _dummy is null)
            return;
        if (Time.GetTicksMsec() < _respawnAtMs)
            return;

        // Reappear behind the player along its heading (the "racing line" stand-in until the AI
        // and a real track path land in Task 5/6), clamped into the drivable area so a player
        // hugging a wall can't fling the rival out of bounds.
        Vector2 behind = _car.GlobalPosition - Vector2.Right.Rotated(_car.Rotation) * RespawnDistance;
        _dummy.Respawn(ClampToDrivable(behind), _car.Rotation);
        _respawnAtMs = 0;
    }

    /// <summary>Push a point inside the playable ring: clamp to the outer walls, then, if it landed
    /// in the central block, shove it out to the nearer straight (top or bottom corridor).</summary>
    private static Vector2 ClampToDrivable(Vector2 p)
    {
        float lo = WallThickness + RespawnMargin;
        p.X = Mathf.Clamp(p.X, lo, ArenaWidth - lo);
        p.Y = Mathf.Clamp(p.Y, lo, ArenaHeight - lo);

        // Central block is centred in the arena (see BuildArena).
        Vector2 c = new(ArenaWidth / 2f, ArenaHeight / 2f);
        float halfW = CentralBlockSize.X / 2f + RespawnMargin, halfH = CentralBlockSize.Y / 2f + RespawnMargin;
        if (Mathf.Abs(p.X - c.X) < halfW && Mathf.Abs(p.Y - c.Y) < halfH)
            p.Y = p.Y < c.Y ? c.Y - halfH : c.Y + halfH; // out to the nearer straight

        return p;
    }

    /// <summary>Debug reset (D-pad Up / R): drop the player and rival back on their marks and clear
    /// any in-flight slow-mo / respawn so the scene is testable without restarting. Not the real
    /// race reset (that's Task 6) — just a playtest convenience.</summary>
    private void ResetScene()
    {
        Engine.TimeScale = 1.0;
        _slowMoEndMs = 0;
        _respawnAtMs = 0;

        _car?.Respawn(PlayerStart, PlayerStartRotation);
        _dummy?.Respawn(DummyStart, DummyStartRotation);
    }

    private void SetUpCamera()
    {
        _camera = new Camera2D { Zoom = new Vector2(0.5f, 0.5f) };
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
        int hp = Mathf.CeilToInt(_car.CurrentHp);
        string drift = _car.IsDrifting ? "DRIFT" : "—";
        string boost = _car.IsLaunching ? "LAUNCH" : _car.IsBoosting ? "BOOST" : "—";
        string iframes = _car.IsInvulnerable ? "  [I-FRAMES]" : string.Empty;
        string wrecked = _car.IsWrecked ? "  ** WRECKED **" : string.Empty;
        string dummyHp = _dummy is null ? string.Empty : $"      Dummy HP {Mathf.CeilToInt(_dummy.CurrentHp),3}";
        _debugReadout.Text = $"HP {hp,3}   Boost {pct,3}%   {drift}   {boost}{iframes}{dummyHp}{wrecked}";
    }
}
