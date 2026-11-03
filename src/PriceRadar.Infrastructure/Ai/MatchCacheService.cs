using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PriceRadar.Core.Abstractions;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Options;
using PriceRadar.Infrastructure.Persistence;

namespace PriceRadar.Infrastructure.Ai;

/// <summary>
/// match.MatchCache (spec §6.6, D-18): every distinct question is asked once per prompt version. The key leaves out the
/// candidates on purpose: if a matching book appears later, the exact-key match finds it before the cache is consulted.
/// </summary>
public sealed class MatchCacheService(PriceRadarDbContext db, TimeProvider clock)
{
    public static byte[] Hash(string promptVersion, string normTitle, string normAuthor) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{promptVersion}|{normTitle}|{normAuthor}"));

    public Task<MatchCacheEntry?> FindAsync(byte[] hash, CancellationToken ct) =>
        db.MatchCache.AsNoTracking().SingleOrDefaultAsync(e => e.InputHash == hash, ct);

    public Task RecordHitAsync(long matchCacheId, CancellationToken ct) =>
        db.MatchCache.Where(e => e.MatchCacheId == matchCacheId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.HitCount, e => e.HitCount + 1)
                .SetProperty(e => e.LastHitAtUtc, clock.GetUtcNow().UtcDateTime), ct);

    public async Task SaveAsync(byte[] hash, AiSettings settings, AiMatchRequest request, AiMatchAnswer answer, CancellationToken ct)
    {
        db.MatchCache.Add(new MatchCacheEntry
        {
            InputHash = hash,
            PromptVersion = settings.PromptVersion,
            NormTitle = request.NormTitle,
            NormAuthor = request.NormAuthor,
            CandidateBookIds = string.Join(',', request.Candidates.Select(c => c.BookId)),
            Decision = answer.Decision,
            ResolvedBookId = answer.Decision == MatchDecision.Match ? answer.BookId : null,
            Confidence = answer.Confidence,
            Reason = answer.Reason,
            Model = answer.Model,
            InputTokens = answer.InputTokens,
            OutputTokens = answer.OutputTokens,
            LatencyMs = answer.LatencyMs,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();   // another run cached the same question a moment earlier: keep theirs
        }
    }

    /// <summary>Spend since midnight UTC, from the token counts stored with every answer (NFR-10, FR-9).</summary>
    public async Task<decimal> SpentTodayUsdAsync(AiSettings settings, CancellationToken ct)
    {
        var today = clock.GetUtcNow().UtcDateTime.Date;
        var tokens = await db.MatchCache.AsNoTracking().Where(e => e.CreatedAtUtc >= today)
            .GroupBy(_ => 1)
            .Select(g => new { In = g.Sum(e => (long)e.InputTokens), Out = g.Sum(e => (long)e.OutputTokens) })
            .SingleOrDefaultAsync(ct);
        return tokens is null ? 0m : settings.CostUsd(tokens.In, tokens.Out);
    }
}
