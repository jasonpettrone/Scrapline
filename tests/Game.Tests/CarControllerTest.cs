namespace Scrapline.Game.Tests;

using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Scrapline.Core.Cars;
using Scrapline.Core.Combat;
using Scrapline.Game.Race;

using static GdUnit4.Assertions;

/// <summary>
/// The first in-engine integration test: proves the GdUnit4 harness runs under the
/// Godot runtime and the game assembly loads. Scene-loading round-trips come with M0-4.
/// </summary>
[TestSuite]
public class CarControllerTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void Car_instantiates_as_a_rigidbody_and_accepts_stats()
    {
        var car = AutoFree(new CarController());
        car!.Configure(CarStats.Default);

        AssertObject(car).IsNotNull();
        AssertBool(car is RigidBody2D).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Car_starts_with_an_empty_meter_and_no_drift_or_boost()
    {
        // Feel is playtested, not asserted; this just guards the state the HUD/seam read.
        var car = AutoFree(new CarController());
        car!.Configure(CarStats.Default);

        AssertFloat(car.BoostFraction).IsEqual(0f);
        AssertBool(car.IsDrifting).IsFalse();
        AssertBool(car.IsBoosting).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Boost_pads_refill_the_meter()
    {
        // The pad entry points (what BoostPad calls) move the meter as expected.
        var car = AutoFree(new CarController());
        car!.Configure(CarStats.Default with { BoostCapacity = 100f });

        car.RefillBoost(40f);
        AssertFloat(car.BoostFraction).IsEqualApprox(0.4f, 0.0001f);

        car.RefillBoostToFull();
        AssertFloat(car.BoostFraction).IsEqual(1f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Car_starts_at_full_hp_and_unwrecked()
    {
        var car = AutoFree(new CarController());
        car!.Configure(CarStats.Default with { MaxHp = 120f });

        AssertFloat(car.CurrentHp).IsEqual(120f);
        AssertFloat(car.HpFraction).IsEqual(1f);
        AssertBool(car.IsWrecked).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Ramming_the_practice_dummy_damages_it_within_bounds()
    {
        // Physics sanity per docs/04: assert a real collision chips HP and stays in range — not
        // exact values (physics isn't deterministic). Built in code (the test project can't load
        // game/ scenes) and stepped with real physics frames.
        var tree = (SceneTree)Engine.GetMainLoop();
        var root = AutoFree(new Node2D())!;
        tree.Root.AddChild(root);

        CarController player = MakeCar(CarStats.Default);
        CarController dummy = MakeCar(CarStats.Default);
        dummy.InputEnabled = false;
        root.AddChild(player);
        root.AddChild(dummy);

        player.GlobalPosition = new Vector2(0f, 0f);
        dummy.GlobalPosition = new Vector2(200f, 0f);
        dummy.Rotation = -Mathf.Pi / 2f;               // facing across the player's path → a side hit

        await PhysicsFrames(tree, 2);                  // let _Ready run (ContactMonitor on, HP set)
        float dummyStartHp = dummy.CurrentHp;
        player.LinearVelocity = new Vector2(1300f, 0f); // straight into the dummy

        await PhysicsFrames(tree, 60);

        AssertFloat(dummy.CurrentHp).IsLess(dummyStartHp);   // the hit landed
        AssertFloat(dummy.CurrentHp).IsGreaterEqual(0f);     // never negative
        AssertFloat(player.CurrentHp).IsBetween(0f, 100f);   // player HP stays in range (default MaxHp = 100)
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Slamming_a_car_into_a_wall_chips_hp()
    {
        // Driving into a wall with real speed is a genuine impact (closing speed well above the
        // wall threshold) and must chip HP — while a gentle lean does not (covered by the Core model).
        var tree = (SceneTree)Engine.GetMainLoop();
        var root = AutoFree(new Node2D())!;
        tree.Root.AddChild(root);

        var wall = new StaticBody2D();
        wall.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(40f, 400f) } });
        root.AddChild(wall);
        wall.GlobalPosition = new Vector2(300f, 0f);

        CarController car = MakeCar(CarStats.Default);
        car.InputEnabled = false; // no self-driving forces; we drive it ourselves
        root.AddChild(car);
        car.GlobalPosition = new Vector2(150f, 0f); // left of the wall, facing it, with room to build speed
        car.Rotation = 0f;

        await PhysicsFrames(tree, 2);
        float startHp = car.CurrentHp;

        car.LinearVelocity = new Vector2(800f, 0f);  // slam into the wall
        await PhysicsFrames(tree, 30);

        AssertFloat(car.CurrentHp).IsLess(startHp);       // the slam chipped HP
        AssertFloat(car.CurrentHp).IsGreaterEqual(0f);    // never negative
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Ramming_a_wall_backed_car_still_delivers_the_impact_burst()
    {
        // Regression: a car pinned against a wall is stopped dead the frame it's hit, so the impact
        // burst must come from the attacker's APPROACH speed (snapshotted pre-collision), not a
        // post-collision read. A hard clean ram should one-shot it — which the slow crush alone
        // could never do inside this short window, so reaching a wreck proves the burst fired.
        var tree = (SceneTree)Engine.GetMainLoop();
        var root = AutoFree(new Node2D())!;
        tree.Root.AddChild(root);

        var wall = new StaticBody2D();
        wall.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(40f, 400f) } });
        root.AddChild(wall);
        wall.GlobalPosition = new Vector2(300f, 0f);

        CarController dummy = MakeCar(CarStats.Default);
        dummy.InputEnabled = false;
        root.AddChild(dummy);
        dummy.GlobalPosition = new Vector2(234f, 0f);   // backed against the wall (left of x=280)
        dummy.Rotation = -Mathf.Pi / 2f;                // side facing the player's approach (a clean hit)

        CarController player = MakeCar(CarStats.Default);
        root.AddChild(player);
        player.GlobalPosition = new Vector2(0f, 0f);

        await PhysicsFrames(tree, 2);
        player.LinearVelocity = new Vector2(1300f, 0f); // full-speed ram into the pinned car

        await PhysicsFrames(tree, 60);

        AssertBool(dummy.IsWrecked).IsTrue();            // the burst landed (and one-shot the wreck)
        AssertBool(player.IsWrecked).IsFalse();          // the clean attacker is unscathed
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task The_player_takes_a_scaled_capped_hit_with_iframes_when_a_rival_rams_them()
    {
        // Forgiving model: a faster rival into the player's side deals ONE chunk scaled within the
        // rival's ram range (here a hard ram → near the cap), then i-frames suppress further hits.
        var tree = (SceneTree)Engine.GetMainLoop();
        var root = AutoFree(new Node2D())!;
        tree.Root.AddChild(root);

        CarController player = MakeCar(CarStats.Default);
        player.IsPlayer = true;
        player.InputEnabled = false;                 // hold the player still; it still takes damage
        root.AddChild(player);
        player.GlobalPosition = new Vector2(0f, 0f);
        player.Rotation = 0f;                         // facing +X → its side faces ±Y

        CarController rival = MakeCar(CarStats.Default);
        rival.InputEnabled = false;
        root.AddChild(rival);
        rival.GlobalPosition = new Vector2(0f, -260f);
        rival.Rotation = Mathf.Pi / 2f;              // front faces +Y, toward the player's side

        await PhysicsFrames(tree, 2);
        float startHp = player.CurrentHp;
        rival.LinearVelocity = new Vector2(0f, 900f); // ram the player's side, fast (the aggressor)

        await PhysicsFrames(tree, 30);

        float maxHit = CarStats.Default.RamDamageMax;
        AssertFloat(player.CurrentHp).IsLess(startHp);                  // a hit landed
        AssertFloat(player.CurrentHp).IsGreaterEqual(startHp - maxHit); // never more than the rival's cap
        AssertBool(player.IsInvulnerable).IsTrue();                     // i-frames are running
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task The_player_takes_no_damage_ramming_a_rival()
    {
        // Aggression is never self-punishing: the player rams a rival and ends the run untouched.
        var tree = (SceneTree)Engine.GetMainLoop();
        var root = AutoFree(new Node2D())!;
        tree.Root.AddChild(root);

        CarController player = MakeCar(CarStats.Default);
        player.IsPlayer = true;
        root.AddChild(player);
        player.GlobalPosition = new Vector2(0f, 0f);

        CarController rival = MakeCar(CarStats.Default);
        rival.InputEnabled = false;
        root.AddChild(rival);
        rival.GlobalPosition = new Vector2(240f, 0f);
        rival.Rotation = -Mathf.Pi / 2f;

        await PhysicsFrames(tree, 2);
        player.LinearVelocity = new Vector2(1300f, 0f); // full-speed ram into the rival

        await PhysicsFrames(tree, 60);

        AssertFloat(player.CurrentHp).IsEqual(CarStats.Default.MaxHp); // unscathed by ramming
        AssertBool(player.IsWrecked).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task The_player_takes_only_tiny_capped_damage_from_walls()
    {
        // Walls barely scratch the player: a full-speed slam costs at most PlayerWallMaxDamage.
        var tree = (SceneTree)Engine.GetMainLoop();
        var root = AutoFree(new Node2D())!;
        tree.Root.AddChild(root);

        var wall = new StaticBody2D();
        wall.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(40f, 400f) } });
        root.AddChild(wall);
        wall.GlobalPosition = new Vector2(300f, 0f);

        CarController player = MakeCar(CarStats.Default);
        player.IsPlayer = true;
        player.InputEnabled = false;
        root.AddChild(player);
        player.GlobalPosition = new Vector2(150f, 0f);

        await PhysicsFrames(tree, 2);
        player.LinearVelocity = new Vector2(800f, 0f); // slam the wall well above the cap speed

        await PhysicsFrames(tree, 30);

        float cap = DamageRules.Default.PlayerWallMaxDamage;
        AssertFloat(player.CurrentHp).IsLess(CarStats.Default.MaxHp);          // a scrape registered
        AssertFloat(player.CurrentHp).IsGreaterEqual(CarStats.Default.MaxHp - cap); // but tiny, capped
    }

    /// <summary>A CarController with a collision shape, built in code (no game/ scene needed).</summary>
    private static CarController MakeCar(CarStats stats)
    {
        var car = new CarController();
        car.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(60f, 30f) } });
        car.Configure(stats);
        return car;
    }

    private static async Task PhysicsFrames(SceneTree tree, int count)
    {
        for (int i = 0; i < count; i++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    }
}
