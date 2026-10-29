using PriceRadar.Core.Domain;
using PriceRadar.Core.Presence;

namespace PriceRadar.UnitTests.Presence;

public class PresencePolicyTests
{
    private static readonly DateTime Run1 = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly ListingPresence Active = new(ListingStatus.Active, 0, Run1, null);

    [Fact]
    public void Apply_SeenInRun_ResetsToActive()
    {
        var disappeared = new ListingPresence(ListingStatus.Disappeared, 2, Run1, Run1.AddDays(14));

        var next = PresencePolicy.Apply(disappeared, seenInRun: true, runIsComplete: true, Run1.AddDays(21));

        next.ShouldBe(new ListingPresence(ListingStatus.Active, 0, Run1.AddDays(21), null));
    }

    [Fact]
    public void Apply_FirstMiss_StaysActiveWithOneMiss()
    {
        var next = PresencePolicy.Apply(Active, seenInRun: false, runIsComplete: true, Run1.AddDays(7));

        next.Status.ShouldBe(ListingStatus.Active);
        next.MissedRunCount.ShouldBe((byte)1);
    }

    [Fact]
    public void Apply_SecondConsecutiveMiss_Disappears()
    {
        var afterOne = PresencePolicy.Apply(Active, false, true, Run1.AddDays(7));

        var afterTwo = PresencePolicy.Apply(afterOne, false, true, Run1.AddDays(14));

        afterTwo.Status.ShouldBe(ListingStatus.Disappeared);
        afterTwo.DisappearedAtUtc.ShouldBe(Run1.AddDays(14));
    }

    [Fact]
    public void Apply_IncompleteRun_ProvesNothing()
    {
        var next = PresencePolicy.Apply(Active, seenInRun: false, runIsComplete: false, Run1.AddDays(7));

        next.ShouldBe(Active);
    }

    [Fact]
    public void Apply_StaleListing_IsDatedAtLastSighting()
    {
        var stale = Active with { MissedRunCount = 1 };

        var next = PresencePolicy.Apply(stale, false, true, Run1.AddDays(45));

        next.Status.ShouldBe(ListingStatus.Disappeared);
        next.DisappearedAtUtc.ShouldBe(Run1);   // outside the 30-day window → not a "recent sale"
    }

    [Fact]
    public void Apply_FixtureScenario_Gives121ActiveAnd6Disappeared()
    {
        // Spec §7 scenario; L037, L064 and L088 have unusable prices and never reach the warehouse.
        int[] rejected = [37, 64, 88];
        HashSet<int>[] runs =
        [
            [.. Enumerable.Range(1, 120)],
            [.. Enumerable.Range(1, 112), .. Enumerable.Range(121, 6)],
            [.. Enumerable.Range(1, 110), 113, 114, .. Enumerable.Range(121, 10)],
        ];

        var listings = new Dictionary<int, ListingPresence>();
        for (var r = 0; r < runs.Length; r++)
        {
            var at = Run1.AddDays(7 * r);
            foreach (var id in listings.Keys.ToList())
            {
                listings[id] = PresencePolicy.Apply(listings[id], runs[r].Contains(id), runIsComplete: true, at);
            }

            foreach (var id in runs[r].Where(id => !listings.ContainsKey(id) && !rejected.Contains(id)))
            {
                listings[id] = new ListingPresence(ListingStatus.Active, 0, at, null);
            }
        }

        listings.Count(l => l.Value.Status == ListingStatus.Active).ShouldBe(121);
        listings.Where(l => l.Value.Status == ListingStatus.Disappeared).Select(l => l.Key).Order()
            .ShouldBe([115, 116, 117, 118, 119, 120]);
        listings.Where(l => l.Value is { Status: ListingStatus.Active, MissedRunCount: 1 }).Select(l => l.Key).Order()
            .ShouldBe([111, 112]);
    }
}
