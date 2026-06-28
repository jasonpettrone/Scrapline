using Godot;
using Scrapline.Core.Cars;

namespace Scrapline.Game.Race;

/// <summary>
/// Top-down arcade car on a <see cref="RigidBody2D"/>, so collisions impart real
/// momentum — the physical foundation takedowns need. Controller-first, all inputs are
/// remappable Godot actions (steer/throttle/brake/drift/boost). Tunable numbers come from
/// <see cref="CarStats"/> in Core.
///
/// M1 driving feel (docs/08 §1):
/// • Lateral grip — sideways velocity is bled toward the car's heading each step, so the
///   car is *planted* and momentum matters. Holding drift drops the grip so it slides.
/// • Drift — a pure cornering/positioning tool (loosens grip + adds turn authority to set up
///   tight lines and ram angles). It does NOT earn boost — that proved exploitable.
/// • Boost — a single free-spend meter, filled by a slow passive trickle plus pickup/launch
///   pads on the track (<see cref="BoostPad"/>), and spent by holding the boost button.
/// The meter math lives in Core (<see cref="BoostMeter"/>); this script applies the physics.
/// </summary>
public partial class CarController : RigidBody2D
{
    /// <summary>How long a launch-pad surge holds before the normal speed cap reels it back in.</summary>
    private const float LaunchWindowSeconds = 0.9f;

    private CarStats _stats = CarStats.Default;
    private BoostMeter _boost = new(CarStats.Default.BoostCapacity);

    private bool _boosting;

    // A launch pad queues a request consumed in _PhysicsProcess (modifying velocity from the
    // Area2D signal callback is timing-fragile; doing it in the physics step is reliable).
    private bool _launchQueued;
    private Vector2 _launchDir;
    private float _launchSpeed;
    private float _launchTimer;     // seconds left in the relaxed-cap window
    private float _launchTopSpeed;  // raised speed cap during that window

    /// <summary>Boost meter fill, 0–1 (for the HUD/debug readout).</summary>
    public float BoostFraction => _boost.Fraction;

    /// <summary>True while the drift button is held (cornering mode), for the readout.</summary>
    public bool IsDrifting { get; private set; }

    /// <summary>True while boost is being spent this frame.</summary>
    public bool IsBoosting => _boosting;

    /// <summary>True during a launch-pad surge.</summary>
    public bool IsLaunching => _launchTimer > 0f;

    /// <summary>Injected by the RaceScene from the RaceConfig before the car drives.</summary>
    public void Configure(CarStats stats)
    {
        _stats = stats;
        _boost = new BoostMeter(stats.BoostCapacity);
    }

    // ── Boost sources called by track pads (BoostPad) ───────────────────────────

    /// <summary>Add meter fuel (a small pad). Clamps at full.</summary>
    public void RefillBoost(float amount) => _boost.Add(amount);

    /// <summary>Fill the meter to full (a large pad).</summary>
    public void RefillBoostToFull() => _boost.Add(_stats.BoostCapacity);

    /// <summary>
    /// Fling the car along <paramref name="direction"/> up to <paramref name="speed"/> (px/s),
    /// independent of the boost meter — a launch pad. The surge is held briefly by a relaxed
    /// speed cap (otherwise LinearDamp would erase it within a frame), then settles back.
    /// </summary>
    public void Launch(Vector2 direction, float speed)
    {
        if (direction.LengthSquared() <= 0f)
            return;
        _launchDir = direction.Normalized();
        _launchSpeed = speed;
        _launchQueued = true;
    }

    public override void _Ready()
    {
        GravityScale = 0f;                                  // top-down: no gravity
        Mass = _stats.Mass;
        LinearDamp = _stats.Acceleration / _stats.MaxSpeed; // terminal speed settles near MaxSpeed
        AngularDamp = 8f;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        (float throttle, float brake, float steer, bool driftHeld, bool boost) = ReadControls();
        IsDrifting = driftHeld;

        Vector2 forward = Vector2.Right.Rotated(Rotation);

        _boost.Add(_stats.BoostRegenRate * dt); // passive trickle, up to full
        ConsumeQueuedLaunch();

        float forwardSpeed = LinearVelocity.Dot(forward);
        ApplyLateralGrip(forward, driftHeld, dt);
        ApplyDrive(forward, forwardSpeed, throttle, brake, boost, dt);
        ApplySteering(steer, forwardSpeed, driftHeld);
    }

    /// <summary>
    /// Apply a pending launch: boost the velocity component along the pad's direction up to the
    /// launch speed (never fighting existing momentum), then open the relaxed-cap window.
    /// </summary>
    private void ConsumeQueuedLaunch()
    {
        if (!_launchQueued)
            return;

        float along = LinearVelocity.Dot(_launchDir);
        if (along < _launchSpeed)
            LinearVelocity += _launchDir * (_launchSpeed - along);

        _launchTopSpeed = Mathf.Max(_launchSpeed, _stats.MaxSpeed);
        _launchTimer = LaunchWindowSeconds;
        _launchQueued = false;
    }

    /// <summary>
    /// Bleed sideways velocity toward the car's heading so it tracks where it points. Holding
    /// drift uses the looser <see cref="CarStats.DriftGrip"/> so the rear steps out and the car
    /// slides. Only the lateral component is corrected — forward momentum and collision knockback
    /// are untouched.
    /// </summary>
    private void ApplyLateralGrip(Vector2 forward, bool driftHeld, float dt)
    {
        Vector2 lateral = forward.Orthogonal();
        float lateralSpeed = LinearVelocity.Dot(lateral);
        float grip = driftHeld ? _stats.DriftGrip : _stats.Grip;
        float retained = Mathf.Exp(-grip * dt); // framerate-independent exponential decay
        LinearVelocity -= lateral * (lateralSpeed * (1f - retained));
    }

    /// <summary>
    /// Throttle/brake forces, the boost-meter drain, and the launch window — all of which feed a
    /// single effective top speed that sets LinearDamp (so above-cap speed always settles back).
    /// </summary>
    private void ApplyDrive(Vector2 forward, float forwardSpeed, float throttle, float brake, bool boost, float dt)
    {
        // Engine + brake as mass-scaled forces: acceleration stays consistent across cars,
        // but heavier cars carry more momentum into collisions.
        if (throttle > 0f)
            ApplyCentralForce(forward * _stats.Acceleration * _stats.Mass * throttle);

        if (brake > 0f && forwardSpeed > -_stats.MaxSpeed * _stats.ReverseSpeedFactor)
            ApplyCentralForce(-forward * _stats.BrakingForce * _stats.Mass * brake);

        // Boost: hold-to-drain. While live, raise the cap and add forward thrust.
        float topSpeed = _stats.MaxSpeed;
        _boosting = false;
        if (boost && _boost.TryDrain(dt, _stats.BoostDrainRate) > 0f)
        {
            _boosting = true;
            topSpeed = _stats.BoostMaxSpeed;
            ApplyCentralForce(forward * _stats.BoostAcceleration * _stats.Mass);
        }

        // Launch window: keep the speed cap relaxed so the surge holds, then expires.
        if (_launchTimer > 0f)
        {
            _launchTimer -= dt;
            topSpeed = Mathf.Max(topSpeed, _launchTopSpeed);
        }

        // The single cap that pulls any above-terminal speed (boost/launch) back down.
        LinearDamp = _stats.Acceleration / topSpeed;
    }

    /// <summary>
    /// Steering: angular velocity scales with signed speed (no turn when stopped, inverted in
    /// reverse), multiplied up while the drift button is held so the car points further into the
    /// corner than it's moving (the slip angle). Set directly for predictable arcade control;
    /// collisions still impart linear knockback because we never override LinearVelocity here.
    /// </summary>
    private void ApplySteering(float steer, float forwardSpeed, bool driftHeld)
    {
        float steerScale = Mathf.Clamp(forwardSpeed / _stats.MaxSpeed, -1f, 1f);
        float turnAuthority = driftHeld ? _stats.DriftTurnMultiplier : 1f;
        AngularVelocity = steer * _stats.TurnSpeed * steerScale * turnAuthority;
    }

    /// <summary>Returns (throttle 0..1, brake 0..1, steer -1..1, drift, boost) from the input actions.</summary>
    private static (float throttle, float brake, float steer, bool drift, bool boost) ReadControls()
    {
        // GetActionStrength returns analog strength (with the action's configured deadzone), so
        // triggers/stick stay analog while keyboard reads as full on/off.
        float throttle = Input.GetActionStrength("drive_throttle");
        float brake = Input.GetActionStrength("drive_brake");
        float steer = Input.GetActionStrength("steer_right") - Input.GetActionStrength("steer_left");
        bool drift = Input.IsActionPressed("drift");
        bool boost = Input.IsActionPressed("boost");

        return (throttle, brake, Mathf.Clamp(steer, -1f, 1f), drift, boost);
    }
}
