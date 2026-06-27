using System.Numerics;

namespace Scrapline.Core;

/// <summary>
/// Frame-rate-independent 2D movement math. This is pure, engine-independent
/// logic: the "feel" knob (speed) and the rule that diagonals aren't faster than
/// cardinals both live here, where they're cheap to unit-test. The Godot layer
/// only reads input and applies the result.
///
/// Note we use <see cref="System.Numerics.Vector2"/> (part of .NET, no Godot) so
/// Core stays engine-free. The Godot side converts to/from Godot.Vector2 at the seam.
/// </summary>
public static class Mover
{
    /// <summary>
    /// Advances <paramref name="position"/> along <paramref name="input"/> (a
    /// direction, not necessarily unit length) at <paramref name="speed"/> pixels
    /// per second over <paramref name="deltaSeconds"/>. Input longer than unit
    /// length is normalized first, so moving diagonally isn't faster than moving
    /// straight (a classic beginner bug this rule prevents).
    /// </summary>
    public static Vector2 Step(Vector2 position, Vector2 input, float speed, float deltaSeconds)
    {
        if (input.LengthSquared() > 1f)
            input = Vector2.Normalize(input);

        return position + input * speed * deltaSeconds;
    }
}
