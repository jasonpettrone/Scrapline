namespace Scrapline.Core.Contracts;

/// <summary>How a race ended for the player. Carried by <see cref="RaceResult"/>.</summary>
public enum RaceOutcome
{
    /// <summary>Finished 1st — the race is cleared.</summary>
    Won,

    /// <summary>Player HP hit 0 — the run is over.</summary>
    Wrecked,

    /// <summary>Finished, but not 1st — not cleared (the run continues).</summary>
    FinishedBehind,
}
