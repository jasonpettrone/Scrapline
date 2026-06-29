namespace Scrapline.Core.Combat;

/// <summary>
/// Which face of a car a collision landed on, relative to that car's heading. Drives the
/// clean-vs-botched call: landing your <see cref="Front"/> into a rival's <see cref="Side"/> or
/// <see cref="Rear"/> (while faster) is a clean takedown; front-to-front is a botched head-on.
/// </summary>
public enum ImpactZone
{
    Front,
    Side,
    Rear,
}
