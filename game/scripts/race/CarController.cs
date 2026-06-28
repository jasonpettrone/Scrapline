using Godot;
using Scrapline.Core.Cars;
using Scrapline.Core.Util;

namespace Scrapline.Game.Race;

/// <summary>
/// Top-down arcade car. Controller-first: left stick steers, right trigger is gas,
/// left trigger is brake/reverse — all analog. Keyboard (WASD) still works as a
/// digital fallback. The tunable numbers come from <see cref="CarStats"/> in Core;
/// this script just turns input + stats into motion via the physics engine.
/// </summary>
public partial class CarController : CharacterBody2D
{
    private const float StickDeadzone = 0.2f;    // ignore thumbstick drift
    private const float TriggerDeadzone = 0.05f; // ignore resting-trigger noise

    private CarStats _stats = CarStats.Default;

    /// <summary>Injected by the RaceScene from the RaceConfig before the car drives.</summary>
    public void Configure(CarStats stats) => _stats = stats;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        (float throttle, float brake, float steer) = ReadControls();

        // Signed speed along the way we're currently pointing.
        Vector2 forward = Vector2.Right.Rotated(Rotation);
        float speed = Velocity.Dot(forward);

        // Gas accelerates forward; brake decelerates then reverses; both scale with
        // how hard the trigger is pulled. If both are released, friction bleeds speed off.
        if (throttle > 0f)
            speed = Mathf.MoveToward(speed, _stats.MaxSpeed, _stats.Acceleration * throttle * dt);
        if (brake > 0f)
            speed = Mathf.MoveToward(speed, -_stats.MaxSpeed * _stats.ReverseSpeedFactor, _stats.BrakingForce * brake * dt);
        if (throttle <= 0f && brake <= 0f)
            speed = Mathf.MoveToward(speed, 0f, _stats.Friction * dt);

        // Steering scales with signed speed: no turning when stopped, reversed when backing up.
        float steerScale = Mathf.Clamp(speed / _stats.MaxSpeed, -1f, 1f);
        Rotation += steer * _stats.TurnSpeed * steerScale * dt;

        Velocity = Vector2.Right.Rotated(Rotation) * speed;
        MoveAndSlide();
    }

    /// <summary>Returns (throttle 0..1, brake 0..1, steer -1..1) from pad + keyboard.</summary>
    private (float throttle, float brake, float steer) ReadControls()
    {
        // Keyboard fallback (digital).
        float throttle = Input.IsPhysicalKeyPressed(Key.W) ? 1f : 0f;
        float brake = Input.IsPhysicalKeyPressed(Key.S) ? 1f : 0f;
        float steer = (Input.IsPhysicalKeyPressed(Key.D) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.A) ? 1f : 0f);

        // Controller (analog), first connected pad. Godot maps Xbox/PS/etc. onto these axes.
        Godot.Collections.Array<int> pads = Input.GetConnectedJoypads();
        if (pads.Count > 0)
        {
            int device = pads[0];
            float gas = AnalogInput.ApplyDeadzone(Input.GetJoyAxis(device, JoyAxis.TriggerRight), TriggerDeadzone);
            float brk = AnalogInput.ApplyDeadzone(Input.GetJoyAxis(device, JoyAxis.TriggerLeft), TriggerDeadzone);
            float stick = AnalogInput.ApplyDeadzone(Input.GetJoyAxis(device, JoyAxis.LeftX), StickDeadzone);

            throttle = Mathf.Max(throttle, gas);
            brake = Mathf.Max(brake, brk);
            if (stick != 0f)
                steer = Mathf.Clamp(steer + stick, -1f, 1f);
        }

        return (throttle, brake, steer);
    }
}
