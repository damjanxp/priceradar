using PriceRadar.Core.Domain;

namespace PriceRadar.Core.Presence;

/// <summary>Presence state of one listing, as stored in dw.Listing.</summary>
public sealed record ListingPresence(ListingStatus Status, byte MissedRunCount, DateTime LastSeenAtUtc, DateTime? DisappearedAtUtc);

/// <summary>
/// The sold heuristic (spec §7) as a pure function. dw.usp_LoadRun (seen listings) and dw.usp_UpdateListingPresence
/// (missed listings) implement the same rules set-based; this class documents and unit-tests them.
/// </summary>
public static class PresencePolicy
{
    /// <summary>
    /// Applies one run to a listing in the run's scope. Runs that did not cover their scope completely change nothing
    /// for listings they did not see.
    /// </summary>
    public static ListingPresence Apply(ListingPresence current, bool seenInRun, bool runIsComplete, DateTime runAtUtc,
        byte missedRunsThreshold = 2, int windowDays = 30)
    {
        if (seenInRun)
        {
            // Seen again: active, counter reset — a Disappeared listing was not sold after all.
            return new ListingPresence(ListingStatus.Active, 0, runAtUtc, null);
        }

        if (!runIsComplete || current.Status == ListingStatus.Disappeared)
        {
            return current;
        }

        var missed = (byte)Math.Min(current.MissedRunCount + 1, byte.MaxValue);
        if (missed < missedRunsThreshold)
        {
            return current with { MissedRunCount = missed };
        }

        // Last seen outside the window: disappeared "stale", dated at the last sighting so it is not counted as a
        // recent sale.
        var disappearedAt = current.LastSeenAtUtc < runAtUtc.AddDays(-windowDays) ? current.LastSeenAtUtc : runAtUtc;
        return new ListingPresence(ListingStatus.Disappeared, missed, current.LastSeenAtUtc, disappearedAt);
    }
}
