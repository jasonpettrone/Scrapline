namespace Scrapline.Core.Util;

/// <summary>
/// Pure helpers for turning raw analog input (thumbsticks, triggers) into clean
/// control values. Engine-independent so the "feel" edge cases are unit-tested
/// instead of discovered by hand on a controller.
/// </summary>
public static class AnalogInput
{
    /// <summary>
    /// Applies a radial deadzone, then rescales the remaining travel back to a full
    /// 0..1 (or -1..1) range so there's no sudden jump as the stick leaves the
    /// deadzone. Magnitudes at or below <paramref name="deadzone"/> return 0.
    /// </summary>
    public static float ApplyDeadzone(float value, float deadzone)
    {
        float magnitude = System.Math.Abs(value);
        if (magnitude <= deadzone)
            return 0f;

        float sign = System.Math.Sign(value);
        float rescaled = (magnitude - deadzone) / (1f - deadzone);
        return sign * System.Math.Clamp(rescaled, 0f, 1f);
    }
}
