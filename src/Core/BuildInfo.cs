namespace Scrapline.Core;

/// <summary>
/// The smallest possible slice of engine-independent Core logic. It exists to
/// prove two things end-to-end: (1) the xUnit test loop runs, and (2) the Godot
/// presentation layer can call into Core across the seam. Replace/grow as M0 lands.
/// </summary>
public static class BuildInfo
{
    public const string Name = "Scrapline";

    /// <summary>A deterministic, testable line the Godot layer prints on boot.</summary>
    public static string Greeting() => $"{Name} Core online.";
}
