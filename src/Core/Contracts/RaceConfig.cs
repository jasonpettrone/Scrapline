using Scrapline.Core.Cars;

namespace Scrapline.Core.Contracts;

/// <summary>
/// Everything the engine layer needs to set up a race. This is the Core→Engine half
/// of the seam (docs/02): Core (or a debug harness) builds a <see cref="RaceConfig"/>,
/// the Godot <c>RaceScene</c> consumes it, and eventually hands back a <c>RaceResult</c>.
///
/// For M0 it carries just the player's car and which track to load.
/// </summary>
public sealed record RaceConfig
{
    /// <summary>The player car's handling stats.</summary>
    public required CarStats PlayerCar { get; init; }

    /// <summary>Identifier of the track to load. M0 only has the grey-box arena.</summary>
    public string TrackId { get; init; } = "arena";

    /// <summary>A ready-to-drive default race (default car on the grey-box arena).</summary>
    public static RaceConfig Default => new() { PlayerCar = CarStats.Default };
}
