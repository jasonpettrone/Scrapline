using System.Collections.Generic;
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

    // Takedown respawn pacing (docs/08 §3). The aftertouch slow-mo/hitstop/shake now live in
    // ImpactFeedback (the single owner of Engine.TimeScale). Wrecked dummies respawn on their own
    // marks (a predictable practice range); "respawn behind the player" returns with the real AI.
    private const float RespawnMinDelay = 1.5f;    // seconds; a glancing wreck respawns soonest
    private const float RespawnMaxDelay = 4.0f;    // a huge slam sets the rival back longest
    private const float RespawnForceScale = 0.0025f;

    // Start transforms (shared by initial spawn and the debug reset).
    private static readonly Vector2 PlayerStart = new(ArenaWidth / 2f, ArenaHeight - 180f);
    private const float PlayerStartRotation = 0f;                       // facing right
    private const float DummyStartRotation = -Mathf.Pi / 2f;           // facing across the player's path

    // Two practice dummies (docs/09 playtest setup): a near-unkillable one to feel out crumple/
    // panel-shed destruction, and a low-HP one to practice the damage-based takedown — one hard
    // clean ram (~700+ px/s closing) one-shots it and fires the split spectacle.
    private static readonly Vector2 CrumpleDummyStart = new(ArenaWidth / 2f + 700f, ArenaHeight - 180f);
    private const float CrumpleDummyHp = 4000f;   // effectively unkillable: pure destruction testbed
    private static readonly Vector2 TakedownDummyStart = new(ArenaWidth / 2f + 1400f, ArenaHeight - 180f);
    private const float TakedownDummyHp = 80f;    // a solid clean ram beats this in one hit

    /// <summary>One practice dummy and its home mark (spawn + wreck-respawn point).</summary>
    private sealed class DummySlot
    {
        public required CarController Car;
        public required Vector2 Start;
        public required float StartRotation;
        public required string Label;
        public ulong RespawnAtMs; // wall-clock respawn time (0 = alive)
    }

    private RaceConfig _config = RaceConfig.Default;
    private CarController? _car;
    private readonly List<DummySlot> _dummies = new();
    private Camera2D? _camera;
    private Label? _debugReadout;
    private DebrisPool? _debris;
    private ImpactFeedback? _feedback;

    public override void _Ready()
    {
        BuildArena();
        SpawnBoostPads();
        SpawnHazards();

        // Destruction support systems, created before the cars so they can be injected (docs/09).
        _debris = new DebrisPool();
        AddChild(_debris);
        _feedback = new ImpactFeedback();
        AddChild(_feedback);

        SpawnCar(_config.PlayerCar);
        SpawnPracticeDummies();
        SetUpCamera();
        if (_feedback is not null)
            _feedback.Camera = _camera;
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

        var wallColor = new Color(0.45f, 0.47f, 0.55f);
        float w = ArenaWidth, h = ArenaHeight, t = WallThickness;

        // Four boundary walls — each its own deformable body so cars carve into them (docs/09).
        // Only ever hit from inside the arena, so they take the deep one-sided carve budget.
        AddWall(new Vector2(w / 2, t / 2), new Vector2(w, t), wallColor, oneSided: true);      // top
        AddWall(new Vector2(w / 2, h - t / 2), new Vector2(w, t), wallColor, oneSided: true);  // bottom
        AddWall(new Vector2(t / 2, h / 2), new Vector2(t, h), wallColor, oneSided: true);      // left
        AddWall(new Vector2(w - t / 2, h / 2), new Vector2(t, h), wallColor, oneSided: true);  // right

        // Central block — turns the arena into a loop you drive around. Reachable from all sides,
        // so it keeps the two-sided carve budget (opposing dents must never meet).
        AddWall(new Vector2(w / 2, h / 2), CentralBlockSize, new Color(0.30f, 0.32f, 0.40f));
    }

    private void AddWall(Vector2 center, Vector2 size, Color color, bool oneSided = false)
    {
        var wall = new DeformableWall { Position = center, Size = size, Color = color };
        if (oneSided)
            wall.CarveBudgetFraction = 0.8f;
        AddChild(wall);
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
        car.DebrisPool = _debris;
        AddChild(car);
        _feedback?.RegisterCar(car);
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
    /// The M1 practice range (docs/09): two inert targets on the bottom straight, both facing
    /// across the player's approach so their side is the clean-hit surface. The first (blue) has
    /// huge HP — a pure crumple/panel-shed testbed; the second (green) has low HP — one hard clean
    /// ram one-shots it and fires the damage-based takedown split.
    /// </summary>
    private void SpawnPracticeDummies()
    {
        SpawnDummy(CrumpleDummyStart, CrumpleDummyHp, new Color(0.5f, 0.65f, 1f), "Crumple");
        SpawnDummy(TakedownDummyStart, TakedownDummyHp, new Color(0.45f, 0.9f, 0.55f), "Takedown");
    }

    private void SpawnDummy(Vector2 start, float hp, Color tint, string label)
    {
        var dummy = CarScene.Instantiate<CarController>();
        dummy.Configure(_config.PlayerCar with { MaxHp = hp });
        dummy.InputEnabled = false;
        dummy.BaseTint = tint;
        dummy.Position = start;
        dummy.Rotation = DummyStartRotation;         // facing "up", across the player's approach
        dummy.DebrisPool = _debris;

        var slot = new DummySlot { Car = dummy, Start = start, StartRotation = DummyStartRotation, Label = label };
        dummy.Wrecked += hitForce => OnDummyWrecked(slot, hitForce);
        AddChild(dummy);
        _feedback?.RegisterCar(dummy);
        _dummies.Add(slot);
    }

    /// <summary>
    /// A takedown (docs/08 §3): freeze the wreck out of play (the split halves flung by the car
    /// are the visible remains), fire the aftertouch slow-mo, and schedule a respawn on the
    /// dummy's mark with a delay that scales with how hard it was hit.
    /// </summary>
    private void OnDummyWrecked(DummySlot slot, float hitForce)
    {
        slot.Car.SetInert(true); // freeze, hide, AND disable its collider so the wreck doesn't block traffic

        // Every takedown earns the aftertouch slow-mo (it IS the celebration); the respawn delay
        // scales with how hard the rival was hit, so a big slam buys a longer tempo swing.
        _feedback?.TriggerSlowMo();

        float delay = Mathf.Clamp(RespawnMinDelay + hitForce * RespawnForceScale, RespawnMinDelay, RespawnMaxDelay);
        slot.RespawnAtMs = Time.GetTicksMsec() + (ulong)(delay * 1000f);
    }

    private void UpdateRespawn()
    {
        ulong now = Time.GetTicksMsec();
        foreach (DummySlot slot in _dummies)
        {
            if (slot.RespawnAtMs == 0 || now < slot.RespawnAtMs)
                continue;
            slot.Car.Respawn(slot.Start, slot.StartRotation);
            slot.RespawnAtMs = 0;
        }
    }

    /// <summary>Debug reset (D-pad Up / R): drop the player and rival back on their marks and clear
    /// any in-flight slow-mo / respawn so the scene is testable without restarting. Not the real
    /// race reset (that's Task 6) — just a playtest convenience.</summary>
    private void ResetScene()
    {
        _feedback?.ResetFeel();
        _debris?.Clear();

        _car?.Respawn(PlayerStart, PlayerStartRotation);
        foreach (DummySlot slot in _dummies)
        {
            slot.RespawnAtMs = 0;
            slot.Car.Respawn(slot.Start, slot.StartRotation);
        }
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

        string dummies = string.Empty;
        foreach (DummySlot slot in _dummies)
        {
            dummies += slot.Car.IsWrecked
                ? $"      {slot.Label}: TAKEDOWN!"
                : $"      {slot.Label} HP {Mathf.CeilToInt(slot.Car.CurrentHp),4}  Crumple {Mathf.RoundToInt(slot.Car.CrumpleFraction * 100f),3}%";
        }
        _debugReadout.Text = $"HP {hp,3}   Boost {pct,3}%   {drift}   {boost}{iframes}{dummies}{wrecked}";
    }
}
