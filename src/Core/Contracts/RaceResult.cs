namespace Scrapline.Core.Contracts;

/// <summary>
/// The Engine→Core half of the seam (docs/02): what the Godot race scene hands back
/// when a race ends. The Core applies it to run state (HP, Scrap, map progression).
/// Plain data — no engine types ever cross here.
/// </summary>
public sealed record RaceResult
{
    /// <summary>Finishing position; 1 = first.</summary>
    public required int Placement { get; init; }

    /// <summary>Player HP left when the race ended (0 = wrecked).</summary>
    public required int HpRemaining { get; init; }

    /// <summary>How the race ended for the player.</summary>
    public required RaceOutcome Outcome { get; init; }

    /// <summary>Opponents the player took down this race.</summary>
    public int Takedowns { get; init; }

    /// <summary>Scrap earned this race (incl. takedown/style bonuses).</summary>
    public int ScrapEarned { get; init; }
}
