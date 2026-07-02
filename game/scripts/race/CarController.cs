using System.Collections.Generic;
using Godot;
using Scrapline.Core.Cars;
using Scrapline.Core.Combat;
using Scrapline.Core.Destruction;
using SysVec2 = System.Numerics.Vector2;

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
/// • Drift — a pure cornering/positioning tool (loosens grip + adds turn authority). No boost.
/// • Boost — a single free-spend meter, filled by passive trickle + pickup/launch pads,
///   spent by holding the boost button.
///
/// M1 damage (docs/08 §2): handled in <see cref="_IntegrateForces"/>. Impacts are measured by
/// CLOSING SPEED — captured from each car's own velocity the instant a contact begins, NOT from
/// the physics solver's per-contact impulse/normal (those are unreliable on a fast contact's first
/// frame, which made damage inconsistent). One contact = one discrete hit; the Core
/// <see cref="DamageModel"/> turns it into HP. A car pinned against a wall on its far side takes a
/// harder (amplified) discrete hit — the physical "sandwich", not a continuous grind.
/// </summary>
public partial class CarController : RigidBody2D
{
    /// <summary>Emitted once when this car is wrecked. <c>hitForce</c> (the closing speed of the
    /// killing blow) lets the scene scale the respawn delay and the aftertouch slow-mo.</summary>
    [Signal]
    public delegate void WreckedEventHandler(float hitForce);

    /// <summary>Emitted on every damaging contact so presentation systems (deformation, VFX, camera
    /// shake — docs/09) can react. <c>damage</c> is the HP sustained (the crumple driver), <c>force</c>
    /// the closing speed, <c>zone</c> the struck <see cref="ImpactZone"/> as an int, <c>localPoint</c>
    /// the contact in the car's local frame, and <c>wrecked</c> whether this hit was the kill.
    /// Handlers MUST NOT mutate physics state synchronously — this can fire inside the physics flush
    /// (defer it, like <see cref="RaiseWrecked"/>).</summary>
    [Signal]
    public delegate void ImpactEventHandler(float damage, float force, int zone, Vector2 localPoint, bool wrecked);

    /// <summary>How long a launch-pad surge holds before the normal speed cap reels it back in.</summary>
    private const float LaunchWindowSeconds = 0.9f;

    /// <summary>How aligned a wall contact must be with the side away from the attacker for the
    /// victim to count as "wall-backed" (pinned) and take an amplified hit. 1 = a wall dead behind.</summary>
    private const float WallBackedDot = 0.25f;

    /// <summary>Speed into a wall (px/s) below which we don't bother denting it — a light scrape
    /// leaves no mark.</summary>
    private const float WallDeformMinSpeed = 90f;

    /// <summary>Lockout (seconds) after the player takes a wall scrape, so one slam (which may bounce
    /// and re-touch) is one tiny hit. Kept separate from enemy i-frames so dabbing a wall can't dodge
    /// a rival.</summary>
    private const float PlayerWallCooldownSeconds = 0.5f;

    private const float DamageFlashSeconds = 0.25f;
    private static readonly Color DamageFlashColor = new(1f, 0.25f, 0.25f);

    /// <summary>Minimum crumple "damage" fed to the deformer on a one-shot wreck, so the killing
    /// blow always looks devastating even if its raw HP figure is modest (docs/09 §4.2).</summary>
    private const float OneShotCrumpleDamage = 80f;

    /// <summary>TEMP (M1): takedowns/wrecks are disabled while we tune their balance (they're currently
    /// far too easy) and build the split-in-half theatrics. Cars still take damage and deform fully —
    /// they just never wreck, vanish, or respawn. Flip back on for the takedown pass (docs/08 §3).</summary>
    private const bool TakedownsEnabled = false;

    /// <summary>HP floor while <see cref="TakedownsEnabled"/> is off, so nothing dies during testing.</summary>
    private const float TakedownDisabledHpFloor = 1f;

    private CarStats _stats = CarStats.Default;
    private DamageRules _damageRules = DamageRules.Default;
    private BoostMeter _boost = new(CarStats.Default.BoostCapacity);

    private bool _boosting;

    // A launch pad queues a request consumed in _PhysicsProcess (modifying velocity from the
    // Area2D signal callback is timing-fragile; doing it in the physics step is reliable).
    private bool _launchQueued;
    private Vector2 _launchDir;
    private float _launchSpeed;
    private float _launchTimer;
    private float _launchTopSpeed;

    // Damage bookkeeping.
    private float _flashTimer;
    private CollisionPolygon2D? _collisionPolygon;

    // Deformation (docs/09). Owns a pure-Core silhouette and renders it onto the Body polygon and the
    // deforming hitbox; profile (sober/exaggerated) is chosen from IsPlayer in _Ready.
    private Deformable? _deformer;

    // The body's APPROACH velocity for this step — snapshotted at the top of _PhysicsProcess, before
    // collisions resolve. Read by both this car and the car it hits to measure closing speed
    // reliably (the solver's own contact data is not reliable, and a post-collision velocity would
    // read ~0 for a car stopped dead against a wall — losing the impact burst).
    private Vector2 _approachVelocity;

    // Colliders touched last step, so we can fire damage only when a NEW contact begins (one impact
    // = one hit). Swapped each step to avoid per-frame allocation.
    private HashSet<ulong> _contacts = new();
    private HashSet<ulong> _contactsPrev = new();

    // Unit directions (world space) from this car's centre to each wall contact this step — lets a
    // car impact tell if a wall is backing the victim (the physical sandwich). Filled in pass 1.
    private readonly Vector2[] _wallDirs = new Vector2[8];
    private int _wallDirCount;

    // Remaining invulnerability (seconds) after a forgiving hit — the player's i-frames.
    private float _invulnTimer;

    // Remaining lockout (seconds) on player wall-scrape damage (one slam = one tiny hit).
    private float _wallDamageCooldown;

    /// <summary>When false, the car ignores input and self-driving forces (a practice dummy).</summary>
    public bool InputEnabled { get; set; } = true;

    /// <summary>Selects the forgiving damage model (docs/08 §2): fixed-chunk hits only when a rival
    /// out-aggresses you or a hazard bites, i-frames after each, and no self-inflicted damage from
    /// ramming or walls. Rivals/AI leave this false and use the full physics model.</summary>
    public bool IsPlayer { get; set; }

    /// <summary>Base body tint the damage flash returns to (lets a dummy read as a different colour).</summary>
    public Color BaseTint { get; set; } = Colors.White;

    /// <summary>Current hit points; reaches 0 when wrecked.</summary>
    public float CurrentHp { get; private set; } = CarStats.Default.MaxHp;

    /// <summary>HP as 0–1 of max (for the HUD/debug readout).</summary>
    public float HpFraction => _stats.MaxHp > 0f ? CurrentHp / _stats.MaxHp : 0f;

    /// <summary>True once HP has hit 0.</summary>
    public bool IsWrecked { get; private set; }

    /// <summary>True during the player's i-frames (no further forgiving damage lands).</summary>
    public bool IsInvulnerable => _invulnTimer > 0f;

    /// <summary>Boost meter fill, 0–1 (for the HUD/debug readout).</summary>
    public float BoostFraction => _boost.Fraction;

    /// <summary>True while the drift button is held (cornering mode), for the readout.</summary>
    public bool IsDrifting { get; private set; }

    /// <summary>True while boost is being spent this frame.</summary>
    public bool IsBoosting => _boosting;

    /// <summary>True during a launch-pad surge.</summary>
    public bool IsLaunching => _launchTimer > 0f;

    /// <summary>Overall body crumple, 0 (pristine) → 1 (fully crushed) — a playtest/debug readout.</summary>
    public float CrumpleFraction => _deformer?.Silhouette.CrumpleAmount ?? 0f;

    /// <summary>This car's velocity going into the current step's collisions (read by a rival to
    /// measure closing speed). Public so the other car in a contact can sample it.</summary>
    public Vector2 ApproachVelocity => _approachVelocity;

    /// <summary>This car's damage range against the player when it rams them (read by the player to
    /// scale the hit). Per-car so enemy types are individually tunable.</summary>
    public (float Min, float Max) RamDamage => (_stats.RamDamageMin, _stats.RamDamageMax);

    /// <summary>Injected by the RaceScene from the RaceConfig before the car drives.</summary>
    public void Configure(CarStats stats)
    {
        _stats = stats;
        _boost = new BoostMeter(stats.BoostCapacity);
        CurrentHp = stats.MaxHp;
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

        ContactMonitor = true;       // needed to read collisions in _IntegrateForces
        MaxContactsReported = 6;

        foreach (Node child in GetChildren())
            if (child is CollisionPolygon2D poly) { _collisionPolygon = poly; break; }

        // Deformation: the player crumples soberly, rivals exaggerate (docs/09 §5). IsPlayer is set
        // by the scene before AddChild, so it's reliable here. The deformer drives both the visual
        // mesh and the deforming hitbox (the CollisionPolygon2D found above).
        if (GetNodeOrNull<Polygon2D>("Body") is { } body)
        {
            var silhouette = DeformableSilhouette.Box(CarHalfWidth, CarHalfHeight, CarVertsPerEdge,
                IsPlayer ? DeformationProfile.Sober : DeformationProfile.Exaggerated);
            _deformer = new Deformable(silhouette, body, _collisionPolygon);
        }
    }

    public override void _Process(double delta)
    {
        // Damage flash fades back to white (driven by Modulate so it tints the body sprite).
        Color tint = _flashTimer > 0f
            ? BaseTint.Lerp(DamageFlashColor, Mathf.Clamp((_flashTimer -= (float)delta) / DamageFlashSeconds, 0f, 1f))
            : BaseTint;

        // I-frames: blink the body (a steady toggle reads clearly as "invulnerable").
        if (_invulnTimer > 0f && (int)(_invulnTimer * 10f) % 2 == 0)
            tint.A *= 0.35f;

        Modulate = tint;

        // Ease the crumple toward its accumulated target (visual; no-ops once settled). Runs even for
        // an inert dummy so its damage shows. Delta scales with TimeScale, so dents crunch in slow-mo.
        _deformer?.Step((float)delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Snapshot this step's APPROACH velocity (pre-collision) for every car, before any collision
        // is resolved. _PhysicsProcess runs for all cars ahead of the physics step's _IntegrateForces,
        // so this value is consistent across bodies regardless of their integrate order — which is
        // what lets a victim read its attacker's true incoming speed even when the attacker is
        // stopped dead the same frame (e.g. ramming a car that's backed against a wall).
        _approachVelocity = LinearVelocity;

        if (_invulnTimer > 0f)
            _invulnTimer = Mathf.Max(0f, _invulnTimer - dt); // tick down i-frames (before any early-out)
        if (_wallDamageCooldown > 0f)
            _wallDamageCooldown = Mathf.Max(0f, _wallDamageCooldown - dt);

        // A practice dummy is a pure physics body: no input, no self-driving forces. It still
        // takes collision damage (handled in _IntegrateForces, which always runs).
        if (!InputEnabled)
            return;

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
    /// Resolve collisions into damage. Runs for every car; each car computes only its OWN damage
    /// (no double counting). Impacts fire once per contact, the instant it begins, sized by the
    /// CLOSING SPEED measured from the bodies' approach velocities — reliable where the solver's
    /// contact impulse/normal are not. A car pinned against a wall on its far side takes an amplified
    /// hit (the physical sandwich). The <see cref="IsPlayer"/> path is forgiving (see <see cref="ResolveContacts"/>).
    /// </summary>
    public override void _IntegrateForces(PhysicsDirectBodyState2D state)
    {
        if (!IsWrecked)
            ResolveContacts(state);
    }

    private void ResolveContacts(PhysicsDirectBodyState2D state)
    {
        _contacts.Clear();
        _wallDirCount = 0;
        int count = state.GetContactCount();

        // Pass 1: classify every contact. We record wall directions first so a car impact (pass 2)
        // can tell whether a wall is backing the victim on its far side — the physical "sandwich".
        for (int i = 0; i < count; i++)
        {
            GodotObject collider = state.GetContactColliderObject(i);
            if (collider is null)
                continue;

            _contacts.Add(collider.GetInstanceId());

            if (collider is not CarController)
            {
                Vector2 toWall = state.GetContactColliderPosition(i) - GlobalPosition;
                if (toWall.LengthSquared() > 0f && _wallDirCount < _wallDirs.Length)
                    _wallDirs[_wallDirCount++] = toWall.Normalized();
            }
        }

        // Pass 2: fire damage only on a NEW contact (one impact = one hit), and only this car's share.
        for (int i = 0; i < count && !IsWrecked; i++)
        {
            GodotObject collider = state.GetContactColliderObject(i);
            if (collider is null || _contactsPrev.Contains(collider.GetInstanceId()))
                continue;

            if (collider is CarController other)
            {
                if (IsPlayer)
                    PlayerCarImpact(other);   // forgiving: a scaled chunk only if the rival out-aggresses us
                else
                    EnemyCarImpact(other);    // full physics model
            }
            else
            {
                Vector2 contactPoint = state.GetContactColliderPosition(i);
                ApplyWallImpact(contactPoint);             // the car's own self-damage + dent
                if (collider is DeformableWall wall)
                    DeformWall(wall, contactPoint);        // dent the wall too — two-way destruction
            }
        }

        (_contactsPrev, _contacts) = (_contacts, _contactsPrev); // swap; next step reuses the buffers
    }

    /// <summary>Is a wall pinning this car on the side away from the attacker? If so the car can't
    /// recede and the hit lands harder — the physical replacement for the old time-based smush
    /// (docs/09). <paramref name="dirToAttacker"/> is the unit world direction to the other car.</summary>
    private bool IsWallBacked(Vector2 dirToAttacker)
    {
        for (int i = 0; i < _wallDirCount; i++)
            if (_wallDirs[i].Dot(-dirToAttacker) > WallBackedDot)
                return true;
        return false;
    }

    /// <summary>Dent the wall we hit, scaled by our speed into it (docs/09): walls are full
    /// participants, caving and changing their hitbox where struck. Light scrapes don't mark them.</summary>
    private void DeformWall(DeformableWall wall, Vector2 worldContact)
    {
        Vector2 toContact = worldContact - GlobalPosition;
        if (toContact.LengthSquared() <= 0f)
            return;
        float approachSpeed = Mathf.Max(0f, _approachVelocity.Dot(toContact.Normalized()));
        if (approachSpeed < WallDeformMinSpeed)
            return;
        wall.TakeImpact(worldContact, _approachVelocity, approachSpeed);
    }

    /// <summary>A rival's self-damage from beginning contact with <paramref name="other"/>, measured
    /// by closing speed along the line between centres (stable, unlike the solver's contact normal).</summary>
    private void EnemyCarImpact(CarController other)
    {
        Vector2 toOther = other.GlobalPosition - GlobalPosition;
        if (toOther.LengthSquared() <= 0f)
            return;
        Vector2 dir = toOther.Normalized();

        // Closing speed: how fast we're converging along the centre line (0 if not approaching).
        float closingSpeed = Mathf.Max(0f, (_approachVelocity - other.ApproachVelocity).Dot(dir));

        ImpactZone selfZone = ZoneFacing(this, other);
        (float damage, bool wrecked) = DamageModel.ResolveCarDamage(
            _damageRules, closingSpeed,
            selfZone, ZoneFacing(other, this),
            _approachVelocity.Length(), other.ApproachVelocity.Length(),
            Mass, other.Mass,
            victimWallBacked: IsWallBacked(dir)); // pinned against a wall on my far side → harder hit

        if (damage > 0f || wrecked)
            ApplyHit(damage, other.ApproachVelocity.Length(), wrecked, selfZone, BuildCarIndenter(other));
    }

    /// <summary>The player's forgiving car damage: a chunk scaled by closing speed within the rival's
    /// own ram range (with i-frames), ONLY when the rival is the aggressor — their front into the
    /// player's side/rear while faster. Ramming a rival, a head-on, or being the faster car all cost
    /// the player nothing, so aggression is never punished.</summary>
    private void PlayerCarImpact(CarController other)
    {
        bool aggressed = DamageModel.EnemyIsAggressor(
            ZoneFacing(other, this), ZoneFacing(this, other),
            other.ApproachVelocity.Length(), _approachVelocity.Length());
        if (!aggressed)
            return;

        Vector2 toOther = other.GlobalPosition - GlobalPosition;
        float closingSpeed = toOther.LengthSquared() <= 0f
            ? other.ApproachVelocity.Length()
            : Mathf.Max(0f, (_approachVelocity - other.ApproachVelocity).Dot(toOther.Normalized()));

        (float min, float max) = other.RamDamage;
        TryForgivingHit(DamageModel.ResolvePlayerHit(_damageRules, closingSpeed, min, max), closingSpeed,
            ZoneFacing(this, other), BuildCarIndenter(other));
    }

    /// <summary>This car's self-damage from driving into a wall, sized by the speed it carried into
    /// the wall (the component of approach velocity toward the contact point). The player takes a
    /// trivial capped scrape (no i-frames); rivals take the real wall model.</summary>
    private void ApplyWallImpact(Vector2 contactPoint)
    {
        Vector2 toWall = contactPoint - GlobalPosition;
        if (toWall.LengthSquared() <= 0f)
            return;

        float approachSpeed = Mathf.Max(0f, _approachVelocity.Dot(toWall.Normalized()));

        // Localize the dent to the struck face from the contact's position in the car's local frame.
        Vector2 localPoint = toWall.Rotated(-GlobalRotation);
        ImpactZone zone = ZoneOfLocalPoint(localPoint);
        Indenter indenter = BuildWallIndenter(localPoint);

        if (IsPlayer)
        {
            if (_wallDamageCooldown > 0f)
                return; // one slam = one tiny hit, even if it bounces and re-touches
            float scrape = DamageModel.ResolvePlayerWallDamage(_damageRules, approachSpeed);
            if (scrape > 0f)
            {
                _wallDamageCooldown = PlayerWallCooldownSeconds;
                ApplyHit(scrape, approachSpeed, oneShot: false, zone, indenter); // no i-frames: walls stay decoupled
            }
            return;
        }

        float damage = DamageModel.ResolveWallDamage(_damageRules, approachSpeed);
        if (damage > 0f)
            ApplyHit(damage, approachSpeed, oneShot: false, zone, indenter);
    }

    /// <summary>Which face of <paramref name="car"/> points at <paramref name="other"/> — i.e.
    /// the zone of <paramref name="car"/> taking the hit. Cars are small, so centre-to-centre
    /// direction is a robust proxy for the contact face.</summary>
    private static ImpactZone ZoneFacing(Node2D car, Node2D other) =>
        ZoneOfLocalPoint((other.GlobalPosition - car.GlobalPosition).Rotated(-car.GlobalRotation));

    /// <summary>The zone a point in the car's local frame falls in: within 45° of forward (+X) is
    /// Front, within 45° of rearward is Rear, the flanks are Side. Shared by the damage and
    /// deformation paths so HP localization and the visible crumple agree.</summary>
    private static ImpactZone ZoneOfLocalPoint(Vector2 local)
    {
        if (local.LengthSquared() <= 0f)
            return ImpactZone.Side;
        float angle = Mathf.Abs(Mathf.RadToDeg(Mathf.Atan2(local.Y, local.X)));
        if (angle <= 45f)
            return ImpactZone.Front;
        if (angle >= 135f)
            return ImpactZone.Rear;
        return ImpactZone.Side;
    }

    // Placeholder car body geometry (96×48 → half-extents) and dent mesh resolution. Real cars later
    // pass their own outline.
    private const float CarHalfWidth = 48f;
    private const float CarHalfHeight = 24f;
    private const int CarVertsPerEdge = 10; // 10×4 = 40 verts — enough shape for flat-vs-pointed dents

    // Impactor footprint along the struck face: wide for a flat slam, near-zero for a corner.
    private const float FlatFootprintHalfWidth = 22f;  // a flat car face caves most of the struck side
    private const float CornerFootprintHalfWidth = 3f; // a corner makes a pinpoint

    /// <summary>Build the <see cref="Indenter"/> for being struck by another car: the dent's flat-vs-
    /// pointed shape comes from the rival's orientation relative to our struck face (a parallel face
    /// → wide flat imprint; a 45°-cocked car → a corner). Push is the inward face normal so only the
    /// struck side caves.</summary>
    private Indenter BuildCarIndenter(Node2D impactor)
    {
        Vector2 dir = (impactor.GlobalPosition - GlobalPosition).Rotated(-GlobalRotation);
        dir = dir.LengthSquared() > 0f ? dir.Normalized() : Vector2.Right;
        Vector2 faceNormal = FaceNormal(dir);

        // How cocked the impactor is relative to our struck face: 0 = a face is parallel (flat),
        // 1 = a corner leads (sharpest). Box edges repeat every 90°, so fold the relative angle.
        float faceAngle = Mathf.Atan2(faceNormal.Y, faceNormal.X);
        float sharpness = Fold45ToUnit(impactor.GlobalRotation - GlobalRotation - faceAngle);
        float halfWidth = Mathf.Lerp(FlatFootprintHalfWidth, CornerFootprintHalfWidth, sharpness);

        return new Indenter(Sys(ProjectToBoxEdge(dir)), Sys(-faceNormal), halfWidth, sharpness);
    }

    /// <summary>Build the <see cref="Indenter"/> for hitting a (flat) wall: the dent's shape comes
    /// from how squarely WE meet the wall — a face-on hit caves a wide flat patch, a corner-first hit
    /// pokes a sharp dent at that corner.</summary>
    private Indenter BuildWallIndenter(Vector2 localContact)
    {
        Vector2 dir = localContact.LengthSquared() > 0f ? localContact.Normalized() : Vector2.Right;
        Vector2 faceNormal = FaceNormal(dir);

        // Residual angle between the true contact direction and the snapped face axis: 0 when we hit
        // square-on (flat), up to 45° when a corner leads (sharp).
        float residual = Mathf.Acos(Mathf.Clamp(dir.Dot(faceNormal), -1f, 1f));
        float sharpness = Mathf.Clamp(residual / (Mathf.Pi / 4f), 0f, 1f);
        float halfWidth = Mathf.Lerp(FlatFootprintHalfWidth, CornerFootprintHalfWidth, sharpness);

        return new Indenter(Sys(ProjectToBoxEdge(dir)), Sys(-faceNormal), halfWidth, sharpness);
    }

    /// <summary>The outward normal of the body-box face a local direction points at (axis-aligned),
    /// so dents push straight in along a face rather than along a diagonal.</summary>
    private static Vector2 FaceNormal(Vector2 dir) =>
        Mathf.Abs(dir.X) >= Mathf.Abs(dir.Y)
            ? new Vector2(Mathf.Sign(dir.X), 0f)
            : new Vector2(0f, Mathf.Sign(dir.Y));

    /// <summary>The point where a local direction meets the body-box perimeter — the struck edge.</summary>
    private static Vector2 ProjectToBoxEdge(Vector2 dir)
    {
        float ax = Mathf.Abs(dir.X), ay = Mathf.Abs(dir.Y);
        float tx = ax > 1e-5f ? CarHalfWidth / ax : float.MaxValue;
        float ty = ay > 1e-5f ? CarHalfHeight / ay : float.MaxValue;
        return dir * Mathf.Min(tx, ty);
    }

    /// <summary>Fold an angle into [0,45°] (box symmetry) and normalize to 0 (faces aligned) → 1
    /// (corner-on, the sharpest imprint).</summary>
    private static float Fold45ToUnit(float angleRad)
    {
        float a = Mathf.PosMod(angleRad, Mathf.Pi / 2f); // 0..90°
        if (a > Mathf.Pi / 4f)
            a = Mathf.Pi / 2f - a;                        // 0..45°
        return a / (Mathf.Pi / 4f);                       // 0..1
    }

    private static SysVec2 Sys(Vector2 v) => new(v.X, v.Y);

    /// <summary>Apply a hit: chip HP (or zero it on a one-shot), flash, dent the struck zone, emit
    /// <see cref="Impact"/>, and fire <see cref="Wrecked"/> once HP hits 0. <paramref name="hitForce"/>
    /// is reported so the scene can scale respawn/slow-mo; <paramref name="zone"/> localizes the HP/
    /// shed bookkeeping and <paramref name="indenter"/> shapes the crumple (null = a non-directional
    /// hit like a hazard: damage, but no dent).</summary>
    private void ApplyHit(float damage, float hitForce, bool oneShot, ImpactZone zone, Indenter? indenter)
    {
        if (IsWrecked)
            return;

        bool lethal = oneShot && TakedownsEnabled;
        CurrentHp = lethal ? 0f : Mathf.Max(0f, CurrentHp - damage);
        if (!TakedownsEnabled)
            CurrentHp = Mathf.Max(CurrentHp, TakedownDisabledHpFloor); // temp: nothing wrecks until takedowns return
        _flashTimer = DamageFlashSeconds;
        bool wreckedNow = TakedownsEnabled && CurrentHp <= 0f;

        // Crumple the struck zone by the damage sustained — high damage, high deformation (docs/09).
        // OnImpact only touches Core data (the mesh refresh happens in _Process), so it's safe here
        // even inside the physics flush. A one-shot forces a devastating-looking dent.
        if (indenter is { } ind)
            _deformer?.OnImpact(ind, oneShot ? Mathf.Max(damage, OneShotCrumpleDamage) : damage, zone);

        Vector2 contact = indenter is { } i2 ? new Vector2(i2.ContactPoint.X, i2.ContactPoint.Y) : Vector2.Zero;
        EmitSignal(SignalName.Impact, damage, hitForce, (int)zone, contact, wreckedNow);

        if (wreckedNow)
        {
            IsWrecked = true;
            _deformer?.Shatter(); // full destruction (Core data only; debris bodies arrive in Phase 3)
            // Deferred: ApplyHit can run inside _IntegrateForces (a physics query flush). A listener
            // that touches body state — e.g. the scene freezing the wreck — would crash if notified
            // mid-flush ("Can't change this state while flushing queries"). Raise it on the idle frame.
            CallDeferred(MethodName.RaiseWrecked, hitForce);
        }
    }

    /// <summary>A forgiving hit (the player model): a fixed <paramref name="damage"/> chunk, ignored
    /// during i-frames, that then opens a fresh i-frame window. Used for rival aggression and hazards
    /// — never speed-scaled.</summary>
    private void TryForgivingHit(float damage, float hitForce, ImpactZone zone, Indenter? indenter)
    {
        if (IsWrecked || _invulnTimer > 0f || damage <= 0f) // a 0-damage graze shouldn't burn i-frames
            return;

        _invulnTimer = _damageRules.InvulnSeconds;
        ApplyHit(damage, hitForce, oneShot: false, zone, indenter);
    }

    /// <summary>Damage from a track hazard. Forgiving + i-frame-gated for everyone, so sitting in a
    /// hazard ticks at the i-frame cadence rather than melting the car.</summary>
    public void TakeHazardDamage() =>
        TryForgivingHit(_damageRules.HazardDamage, _damageRules.HazardDamage, ImpactZone.Front, indenter: null);

    /// <summary>Emits <see cref="Wrecked"/>. Always invoked via <c>CallDeferred</c> so it never fires
    /// during a physics query flush (see <see cref="ApplyHit"/>).</summary>
    private void RaiseWrecked(float hitForce) => EmitSignal(SignalName.Wrecked, hitForce);

    /// <summary>Reset to a fresh, undamaged car at a new spot — used to respawn a wrecked rival.</summary>
    public void Respawn(Vector2 position, float rotation)
    {
        SetInert(false);
        GlobalPosition = position;
        Rotation = rotation;
        LinearVelocity = Vector2.Zero;
        AngularVelocity = 0f;
        CurrentHp = _stats.MaxHp;
        IsWrecked = false;
        _flashTimer = 0f;
        _invulnTimer = 0f;
        _wallDamageCooldown = 0f;
        _approachVelocity = Vector2.Zero;
        _contacts.Clear();
        _contactsPrev.Clear();
        _deformer?.Repair(); // a respawning rival comes back pristine (docs/09 §6)
    }

    /// <summary>Park a wrecked car out of play (or bring it back). Inert = frozen, hidden, and —
    /// crucially — its collider disabled, so the wreck doesn't sit as an invisible wall that blocks
    /// the player until it respawns. Toggled deferred so it's safe to call from a physics callback.</summary>
    public void SetInert(bool inert)
    {
        Freeze = inert;
        Visible = !inert;
        _collisionPolygon?.SetDeferred(CollisionPolygon2D.PropertyName.Disabled, inert);
    }

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
