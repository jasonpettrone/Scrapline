using Godot;
using Scrapline.Core.Cars;

namespace Scrapline.Game.Race;

/// <summary>
/// Top-down arcade car. The "feel" is intentionally simple for M0: throttle moves you
/// along your facing, steering rotates you (more effective the faster you go and inverted
/// in reverse, like a real car). All the tunable numbers come from <see cref="CarStats"/>
/// in Core — this script just turns input + stats into motion via the physics engine.
/// </summary>
public partial class CarController : CharacterBody2D
{
    private CarStats _stats = CarStats.Default;

    /// <summary>Injected by the RaceScene from the RaceConfig before the car drives.</summary>
    public void Configure(CarStats stats) => _stats = stats;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        float throttle =
            (Input.IsPhysicalKeyPressed(Key.W) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.S) ? 1f : 0f);
        float steer =
            (Input.IsPhysicalKeyPressed(Key.D) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.A) ? 1f : 0f);

        // Signed speed along the way we're currently pointing.
        Vector2 forward = Vector2.Right.Rotated(Rotation);
        float speed = Velocity.Dot(forward);

        if (throttle > 0f)
            speed = Mathf.MoveToward(speed, _stats.MaxSpeed, _stats.Acceleration * dt);
        else if (throttle < 0f)
            speed = Mathf.MoveToward(speed, -_stats.MaxSpeed * _stats.ReverseSpeedFactor, _stats.BrakingForce * dt);
        else
            speed = Mathf.MoveToward(speed, 0f, _stats.Friction * dt);

        // Steering scales with signed speed: no turning when stopped, reversed when backing up.
        float steerScale = Mathf.Clamp(speed / _stats.MaxSpeed, -1f, 1f);
        Rotation += steer * _stats.TurnSpeed * steerScale * dt;

        // Re-derive forward after rotating, then drive and resolve collisions against walls.
        Velocity = Vector2.Right.Rotated(Rotation) * speed;
        MoveAndSlide();
    }
}
