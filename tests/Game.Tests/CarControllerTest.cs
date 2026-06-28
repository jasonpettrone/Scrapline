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
}
