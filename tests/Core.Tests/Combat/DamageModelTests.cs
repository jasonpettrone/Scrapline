using Scrapline.Core.Combat;
using Xunit;

namespace Scrapline.Core.Tests.Combat;

public class DamageModelTests
{
    // Round-number fixture so the math is obvious:
    // base = 0.1 * (closing - 100); clean ×2; mass clamp ±2×; one-shot when the attacker's
    // SPEED ≥ 1000; wall = 0.1 * (approach - 50).
    private static DamageRules Rules => new()
    {
        MinImpactSpeed = 100f,
        DamagePerSpeed = 0.1f,
        CleanHitMultiplier = 2f,
        MaxMassFactor = 2f,
        OneShotSpeed = 1000f,
        WallMinSpeed = 50f,
        WallDamagePerSpeed = 0.1f,
        CrushDamagePerSecond = 30f,
        PlayerHitMaxSpeed = 600f,           // player-hit band: 100 → min, 600 → max
        PlayerWallMinSpeed = 50f,
        PlayerWallReferenceSpeed = 500f,    // player wall: 0 → 0, 500+ → cap
        PlayerWallMaxDamage = 5f,
    };

    // ── Car ↔ car ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_soft_tap_below_the_threshold_does_no_damage()
    {
        // I'm clipped at a crawl: below the floor, nothing happens to me.
        var (damage, wrecked) = DamageModel.ResolveCarDamage(
            Rules, closingSpeed: 80f,
            selfZone: ImpactZone.Side, otherZone: ImpactZone.Front,
            selfSpeed: 0f, otherSpeed: 80f, selfMass: 1f, otherMass: 1f);

        Assert.Equal(0f, damage);
        Assert.False(wrecked);
    }

    [Fact]
    public void Landing_a_clean_hit_costs_the_attacker_nothing()
    {
        // My front into their side while I'm faster → I'm the clean attacker, I pay nothing.
        var (damage, wrecked) = DamageModel.ResolveCarDamage(
            Rules, closingSpeed: 600f,
            selfZone: ImpactZone.Front, otherZone: ImpactZone.Side,
            selfSpeed: 600f, otherSpeed: 100f, selfMass: 1f, otherMass: 1f);

        Assert.Equal(0f, damage);
        Assert.False(wrecked);
    }

    [Fact]
    public void Suffering_a_clean_side_hit_is_amplified()
    {
        // Their front into my side while they're faster → I'm the victim, base × clean multiplier.
        var (damage, wrecked) = DamageModel.ResolveCarDamage(
            Rules, closingSpeed: 600f,
            selfZone: ImpactZone.Side, otherZone: ImpactZone.Front,
            selfSpeed: 100f, otherSpeed: 600f, selfMass: 1f, otherMass: 1f);

        Assert.Equal(50f * 2f, damage, 4);   // base 50 × clean 2
        Assert.False(wrecked);               // attacker 600 < one-shot speed
    }

    [Fact]
    public void Being_rear_ended_by_a_faster_rival_is_also_a_clean_hit_on_me()
    {
        var (damage, _) = DamageModel.ResolveCarDamage(
            Rules, closingSpeed: 400f,
            selfZone: ImpactZone.Rear, otherZone: ImpactZone.Front,
            selfSpeed: 50f, otherSpeed: 400f, selfMass: 1f, otherMass: 1f);

        Assert.True(damage > 0f);
    }

    [Fact]
    public void A_clean_hit_by_a_fast_enough_attacker_wrecks_me_outright()
    {
        // Clean on me and the attacker's SPEED is at/above the one-shot threshold → wrecked.
        var (_, wrecked) = DamageModel.ResolveCarDamage(
            Rules, closingSpeed: 600f,
            selfZone: ImpactZone.Side, otherZone: ImpactZone.Front,
            selfSpeed: 0f, otherSpeed: 1200f, selfMass: 1f, otherMass: 1f);

        Assert.True(wrecked);
    }

    [Fact]
    public void A_clean_attacker_is_never_wrecked_by_their_own_hit()
    {
        var (damage, wrecked) = DamageModel.ResolveCarDamage(
            Rules, closingSpeed: 600f,
            selfZone: ImpactZone.Front, otherZone: ImpactZone.Side,
            selfSpeed: 1200f, otherSpeed: 0f, selfMass: 1f, otherMass: 1f);

        Assert.Equal(0f, damage);
        Assert.False(wrecked);
    }

    [Fact]
    public void A_hard_but_botched_head_on_does_not_wreck()
    {
        // Fast, but front-to-front (not clean) → no instant wreck, just the base trade.
        var (damage, wrecked) = DamageModel.ResolveCarDamage(
            Rules, closingSpeed: 1200f,
            selfZone: ImpactZone.Front, otherZone: ImpactZone.Front,
            selfSpeed: 1200f, otherSpeed: 1200f, selfMass: 1f, otherMass: 1f);

        Assert.False(wrecked);
        Assert.True(damage > 0f);
    }

    [Fact]
    public void Head_on_is_the_base_trade()
    {
        var (damage, _) = DamageModel.ResolveCarDamage(
            Rules, closingSpeed: 600f,
            selfZone: ImpactZone.Front, otherZone: ImpactZone.Front,
            selfSpeed: 300f, otherSpeed: 300f, selfMass: 1f, otherMass: 1f);

        Assert.Equal(50f, damage, 4);
    }

    [Fact]
    public void Hitting_a_side_while_slower_is_still_botched_for_me()
    {
        // My front into their side, but I'm the SLOWER car → not clean, I eat the base trade.
        var (damage, _) = DamageModel.ResolveCarDamage(
            Rules, closingSpeed: 500f,
            selfZone: ImpactZone.Front, otherZone: ImpactZone.Side,
            selfSpeed: 100f, otherSpeed: 400f, selfMass: 1f, otherMass: 1f);

        Assert.True(damage > 0f);
    }

    [Fact]
    public void In_a_botched_trade_the_heavier_car_takes_less()
    {
        var (heavy, _) = DamageModel.ResolveCarDamage(
            Rules, closingSpeed: 600f,
            selfZone: ImpactZone.Front, otherZone: ImpactZone.Front,
            selfSpeed: 300f, otherSpeed: 300f, selfMass: 2f, otherMass: 1f);
        var (light, _) = DamageModel.ResolveCarDamage(
            Rules, closingSpeed: 600f,
            selfZone: ImpactZone.Front, otherZone: ImpactZone.Front,
            selfSpeed: 300f, otherSpeed: 300f, selfMass: 1f, otherMass: 2f);

        Assert.True(heavy < light, $"heavy {heavy} should be < light {light}");
    }

    [Fact]
    public void Mass_factor_is_clamped_so_extreme_ratios_stay_sane()
    {
        var (heavy, _) = DamageModel.ResolveCarDamage(
            Rules, closingSpeed: 600f,
            selfZone: ImpactZone.Front, otherZone: ImpactZone.Front,
            selfSpeed: 300f, otherSpeed: 300f, selfMass: 100f, otherMass: 1f);

        Assert.Equal(50f / 2f, heavy, 4);   // base 50, clamped to half
    }

    // ── Player aggressor test (the forgiving model's only car-hit trigger) ────────

    [Fact]
    public void A_faster_rival_into_the_players_side_is_the_aggressor()
    {
        Assert.True(DamageModel.EnemyIsAggressor(
            enemyZone: ImpactZone.Front, playerZone: ImpactZone.Side,
            enemySpeed: 600f, playerSpeed: 100f));
    }

    [Fact]
    public void The_player_ramming_a_rival_is_not_being_aggressed()
    {
        // Player's front into the rival's side while faster → the rival is NOT the aggressor.
        Assert.False(DamageModel.EnemyIsAggressor(
            enemyZone: ImpactZone.Side, playerZone: ImpactZone.Front,
            enemySpeed: 100f, playerSpeed: 600f));
    }

    [Fact]
    public void A_head_on_does_not_aggress_the_player()
    {
        Assert.False(DamageModel.EnemyIsAggressor(
            enemyZone: ImpactZone.Front, playerZone: ImpactZone.Front,
            enemySpeed: 600f, playerSpeed: 600f));
    }

    [Fact]
    public void A_rival_clipping_the_players_side_while_slower_is_not_the_aggressor()
    {
        Assert.False(DamageModel.EnemyIsAggressor(
            enemyZone: ImpactZone.Front, playerZone: ImpactZone.Side,
            enemySpeed: 100f, playerSpeed: 600f));
    }

    // ── Car ↔ wall ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(40f, 0f)]      // below threshold → free scrape
    [InlineData(50f, 0f)]      // exactly at threshold → still free
    [InlineData(250f, 20f)]    // 0.1 * (250 - 50)
    public void Wall_self_damage_scales_with_speed_into_the_wall(float approach, float expected)
    {
        Assert.Equal(expected, DamageModel.ResolveWallDamage(Rules, approach), 4);
    }

    // ── Player hit: scaled within the rival's range ───────────────────────────────

    [Theory]
    [InlineData(80f, 0f)]      // below the impact floor → nothing
    [InlineData(100f, 0f)]     // at the floor → min of the range (0)
    [InlineData(350f, 5f)]     // halfway up the 100..600 band → halfway up 0..10
    [InlineData(600f, 10f)]    // at the top → max
    [InlineData(900f, 10f)]    // above the top → still capped at max
    public void A_rivals_player_hit_scales_within_its_range_by_closing_speed(float closing, float expected)
    {
        Assert.Equal(expected, DamageModel.ResolvePlayerHit(Rules, closing, minDamage: 0f, maxDamage: 10f), 4);
    }

    [Fact]
    public void A_rivals_player_hit_respects_a_nonzero_minimum()
    {
        // A barely-clean ram still deals the rival's MIN, not zero.
        Assert.Equal(2f, DamageModel.ResolvePlayerHit(Rules, closingSpeed: 100f, minDamage: 2f, maxDamage: 12f), 4);
    }

    // ── Player wall: trivial and capped ───────────────────────────────────────────

    [Theory]
    [InlineData(40f, 0f)]      // below the floor → no scrape
    [InlineData(50f, 0.5f)]    // light touch → fractional
    [InlineData(250f, 2.5f)]   // half the reference speed → half the cap
    [InlineData(500f, 5f)]     // at the reference → the cap
    [InlineData(900f, 5f)]     // full-speed slam → still capped (deliberately trivial)
    public void Player_wall_damage_is_fractional_and_hard_capped(float approach, float expected)
    {
        Assert.Equal(expected, DamageModel.ResolvePlayerWallDamage(Rules, approach), 4);
    }

    // ── Crush ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Crush_damage_accrues_with_time()
    {
        Assert.Equal(30f * 0.5f, DamageModel.ResolveCrushDamage(Rules, 0.5f), 4);
    }
}
