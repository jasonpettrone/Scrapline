using System.Collections.Generic;
using Godot;

namespace Scrapline.Game.Race;

/// <summary>
/// The force-scaled feedback layer (docs/09 §4.5): camera shake + a brief hitstop on solid hits,
/// spark bursts at the contact point, and the big-takedown slow-mo — all reading the same impact
/// force off <c>CarController.Impact</c>. This node is the single owner of <c>Engine.TimeScale</c>
/// (hitstop and slow-mo are two channels composed here, so they can't fight over it).
///
/// Impact signals can fire inside the physics flush, so handlers only record state; every node
/// mutation (particles, camera offset, TimeScale) happens in <see cref="_Process"/>.
///
/// The player danger tell (docs/09 §2): sparks for the player only fire below the Sober profile's
/// HP threshold — a readable "one more hit" warning; rivals spark on every solid hit.
/// </summary>
public partial class ImpactFeedback : Node2D
{
    // Shake: impacts add "trauma", the offset is trauma² (small hits barely tremble, big slams kick).
    private const float ShakeMaxOffsetPx = 22f;
    private const float ShakeDecayPerSecond = 2.4f;
    private const float ShakeForceRef = 900f;          // force at which a hit adds full trauma
    private const float ShakeDistanceRef = 900f;       // off-screen hits shake less

    // Hitstop: a real-time freeze scaled by force (docs/09: ~40–90 ms on solid hits).
    private const float HitstopMinForce = 500f;
    private const float HitstopMaxForce = 1300f;
    private const ulong HitstopMinMs = 40;
    private const ulong HitstopMaxMs = 90;
    private const float HitstopScale = 0.05f;

    // Slow-mo (the takedown aftertouch, moved from RaceScene so TimeScale has one owner).
    private const float SlowMoScale = 0.35f;
    private const ulong SlowMoDurationMs = 800;

    private const int SparkPoolSize = 8;

    /// <summary>The camera to shake. Set by the scene once the camera exists.</summary>
    public Camera2D? Camera { get; set; }

    private readonly RandomNumberGenerator _rng = new();
    private readonly List<CpuParticles2D> _sparks = new();
    private int _nextSpark;
    private float _trauma;
    private ulong _hitstopEndMs;
    private ulong _slowMoEndMs;

    private struct SparkRequest
    {
        public Vector2 Position;
        public Vector2 Direction;
        public float Severity;
    }

    private readonly List<SparkRequest> _pendingSparks = new();

    public override void _Ready()
    {
        for (int i = 0; i < SparkPoolSize; i++)
        {
            var p = new CpuParticles2D
            {
                Emitting = false,
                OneShot = true,
                Amount = 14,
                Lifetime = 0.35f,
                Explosiveness = 0.95f,
                Spread = 55f,
                Gravity = Vector2.Zero,
                InitialVelocityMin = 120f,
                InitialVelocityMax = 360f,
                ScaleAmountMin = 1.5f,
                ScaleAmountMax = 3f,
                Color = new Color(1f, 0.78f, 0.3f), // hot orange placeholder sparks
            };
            AddChild(p);
            _sparks.Add(p);
        }
    }

    /// <summary>Subscribe to a car's impacts. Call once per car after it enters the tree.</summary>
    public void RegisterCar(CarController car)
    {
        car.Impact += (damage, force, zone, localPoint, wrecked) => OnImpact(car, force, localPoint, wrecked);
    }

    /// <summary>The takedown aftertouch: ease time down and back over the slow-mo window.</summary>
    public void TriggerSlowMo() => _slowMoEndMs = Time.GetTicksMsec() + SlowMoDurationMs;

    /// <summary>Debug-reset hook: drop all feel state and restore real time.</summary>
    public void ResetFeel()
    {
        _trauma = 0f;
        _hitstopEndMs = 0;
        _slowMoEndMs = 0;
        _pendingSparks.Clear();
        Engine.TimeScale = 1.0;
        if (Camera is not null)
            Camera.Offset = Vector2.Zero;
    }

    /// <summary>May run inside the physics flush — records state only; nodes are touched in
    /// <see cref="_Process"/> (reading the car's transform here is safe, mutating physics is not).</summary>
    private void OnImpact(CarController car, float force, Vector2 localPoint, bool wrecked)
    {
        float severity = Mathf.Clamp(force / ShakeForceRef, 0f, 1.5f);

        // Shake, attenuated by how far the hit is from the camera's view. A takedown maxes it.
        float attenuation = 1f;
        if (Camera is not null)
        {
            float dist = (car.GlobalPosition - Camera.GlobalPosition).Length();
            attenuation = 1f / (1f + dist / ShakeDistanceRef);
        }
        float traumaAdd = wrecked ? 0.85f : 0.18f + 0.5f * severity;
        _trauma = Mathf.Clamp(_trauma + traumaAdd * attenuation, 0f, 1.2f);

        // Hitstop on solid hits; a takedown gets the full freeze regardless of measured force
        // (the split reads best after a hard beat).
        if (wrecked || force >= HitstopMinForce)
        {
            float t = wrecked ? 1f
                : Mathf.Clamp((force - HitstopMinForce) / (HitstopMaxForce - HitstopMinForce), 0f, 1f);
            ulong ms = HitstopMinMs + (ulong)((HitstopMaxMs - HitstopMinMs) * t);
            ulong end = Time.GetTicksMsec() + ms;
            if (end > _hitstopEndMs)
                _hitstopEndMs = end;
        }

        // Sparks at the contact — gated by the player danger tell; a takedown erupts (double burst).
        if (!car.SparksSuppressed && localPoint != Vector2.Zero)
        {
            Vector2 world = car.ToGlobal(localPoint);
            Vector2 outward = (world - car.GlobalPosition).Normalized();
            float sparkSeverity = wrecked ? 1.5f : severity;
            _pendingSparks.Add(new SparkRequest { Position = world, Direction = outward, Severity = sparkSeverity });
            if (wrecked)
                _pendingSparks.Add(new SparkRequest { Position = world, Direction = -outward, Severity = sparkSeverity });
        }
    }

    public override void _Process(double delta)
    {
        // Real (TimeScale-proof) delta: the shake must decay through a hitstop, not freeze with it.
        float realDt = (float)(delta / Mathf.Max((float)Engine.TimeScale, 0.0001f));

        DrainSparks();
        UpdateTimeScale();
        UpdateShake(realDt);
    }

    private void DrainSparks()
    {
        foreach (SparkRequest req in _pendingSparks)
        {
            CpuParticles2D p = _sparks[_nextSpark];
            _nextSpark = (_nextSpark + 1) % _sparks.Count;
            p.GlobalPosition = req.Position;
            p.Direction = req.Direction;
            p.InitialVelocityMax = 180f + 300f * req.Severity;
            p.Restart();
        }
        _pendingSparks.Clear();
    }

    /// <summary>Compose the two time channels: hitstop is a hard floor while it lasts; slow-mo eases
    /// back to real time over its window (real-clock-based, so it's TimeScale-proof).</summary>
    private void UpdateTimeScale()
    {
        ulong now = Time.GetTicksMsec();
        float scale = 1f;

        if (now < _hitstopEndMs)
            scale = HitstopScale;
        else
            _hitstopEndMs = 0;

        if (_slowMoEndMs != 0)
        {
            if (now >= _slowMoEndMs)
            {
                _slowMoEndMs = 0;
            }
            else
            {
                float remaining = (_slowMoEndMs - now) / (float)SlowMoDurationMs; // 1 → 0
                scale = Mathf.Min(scale, Mathf.Lerp(1f, SlowMoScale, remaining));
            }
        }

        Engine.TimeScale = scale;
    }

    private void UpdateShake(float realDt)
    {
        if (Camera is null)
            return;

        _trauma = Mathf.Max(0f, _trauma - ShakeDecayPerSecond * realDt);
        if (_trauma <= 0f)
        {
            Camera.Offset = Vector2.Zero;
            return;
        }

        float amp = _trauma * _trauma * ShakeMaxOffsetPx;
        Camera.Offset = new Vector2(_rng.RandfRange(-amp, amp), _rng.RandfRange(-amp, amp));
    }
}
