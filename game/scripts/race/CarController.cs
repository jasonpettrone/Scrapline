using Godot;
using Scrapline.Core.Cars;
using Scrapline.Core.Util;

namespace Scrapline.Game.Race;

/// <summary>
/// Top-down arcade car on a <see cref="RigidBody2D"/>, so collisions impart real
/// momentum — the physical foundation takedowns need. Controller-first: left stick
/// steers, right trigger is gas, left trigger is brake/reverse (all analog); WASD is a
/// digital fallback. Tunable numbers come from <see cref="CarStats"/> in Core.
///
/// This is *functional* M0 handling. The real feel pass — drift, boost, grip/slip — is M1.
/// </summary>
public partial class CarController : RigidBody2D
{
    private const float StickDeadzone = 0.2f;    // ignore thumbstick drift
    private const float TriggerDeadzone = 0.05f; // ignore resting-trigger noise

    private CarStats _stats = CarStats.Default;

    /// <summary>Injected by the RaceScene from the RaceConfig before the car drives.</summary>
    public void Configure(CarStats stats) => _stats = stats;

    public override void _Ready()
    {
        GravityScale = 0f;                                  // top-down: no gravity
        Mass = _stats.Mass;
        LinearDamp = _stats.Acceleration / _stats.MaxSpeed; // terminal speed settles near MaxSpeed
        AngularDamp = 8f;
    }

    public override void _PhysicsProcess(double delta)
    {
        (float throttle, float brake, float steer) = ReadControls();

        Vector2 forward = Vector2.Right.Rotated(Rotation);
        float forwardSpeed = LinearVelocity.Dot(forward);

        // Engine + brake as mass-scaled forces: acceleration stays consistent across cars,
        // but heavier cars carry more momentum into collisions. LinearDamp caps top speed.
        if (throttle > 0f)
            ApplyCentralForce(forward * _stats.Acceleration * _stats.Mass * throttle);

        if (brake > 0f && forwardSpeed > -_stats.MaxSpeed * _stats.ReverseSpeedFactor)
            ApplyCentralForce(-forward * _stats.BrakingForce * _stats.Mass * brake);

        // Steering: angular velocity scales with signed speed (no turn when stopped, inverted
        // in reverse). Set directly for predictable arcade control; collisions still impart
        // linear knockback because we never override LinearVelocity.
        float steerScale = Mathf.Clamp(forwardSpeed / _stats.MaxSpeed, -1f, 1f);
        AngularVelocity = steer * _stats.TurnSpeed * steerScale;
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
