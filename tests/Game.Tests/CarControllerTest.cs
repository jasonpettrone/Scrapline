namespace Scrapline.Game.Tests;

using GdUnit4;
using Godot;
using Scrapline.Core.Cars;
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
}
