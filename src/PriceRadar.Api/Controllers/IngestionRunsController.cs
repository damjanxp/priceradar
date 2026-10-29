using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceRadar.Api.Problems;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Ingestion;
using PriceRadar.Infrastructure.Persistence;

namespace PriceRadar.Api.Controllers;

[ApiController]
[Route("api/ingestion/runs")]
[Produces("application/json")]
public sealed class IngestionRunsController(IngestionService ingestion, PriceRadarDbContext db) : ControllerBase
{
    /// <summary>Triggers an ingestion run; the pipeline executes asynchronously in the Worker (FR-2).</summary>
    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    [ProducesResponseType<RunDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<RunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateRunRequest request,
        [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey,
        CancellationToken ct)
    {
        var result = await ingestion.CreateRunAsync(
            new CreateRunCommand(request.Source, request.Query, request.MaxPages, request.FixtureSet),
            idempotencyKey, requestedBy: "anonymous", ct);

        return result.Outcome switch
        {
            CreateRunOutcome.Created => AcceptedAtAction(nameof(Get), new { id = result.Run!.ScrapeRunId }, await ToDtoAsync(result.Run, ct)),
            CreateRunOutcome.ExistingForIdempotencyKey => Ok(await ToDtoAsync(result.Run!, ct)),
            CreateRunOutcome.SourceNotFound => NotFound(ProblemTypes.Create(HttpContext, 404, ProblemTypes.NotFound, "Source not found.", result.Error)),
            CreateRunOutcome.SourceDisabled => Conflict(ProblemTypes.Create(HttpContext, 409, ProblemTypes.SourceDisabled, "The source is disabled.", result.Error)),
            CreateRunOutcome.AlreadyActive => Conflict(WithActiveRun(ProblemTypes.Create(HttpContext, 409, ProblemTypes.RunAlreadyActive,
                "An ingestion run is already active for this source.", result.Error), result.ActiveRunId)),
            _ => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["fixtureSet"] = [result.Error!] })
            {
                Type = ProblemTypes.Validation,
            }),
        };
    }

    /// <summary>Lists runs, newest first (FR-17).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<RunDto>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<RunDto>> List(
        [FromQuery] string? source,
        [FromQuery] RunStatus? status,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = db.ScrapeRuns.AsNoTracking().Include(r => r.Source).AsQueryable();
        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(r => r.Source!.Code == source);
        }

        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        var total = await query.CountAsync(ct);
        var runs = await query.OrderByDescending(r => r.ScrapeRunId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<RunDto>(runs.Select(RunDto.From).ToList(), page, pageSize, total);
    }

    /// <summary>One run with stage, counters and the last error (FR-17).</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<RunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var run = await db.ScrapeRuns.AsNoTracking().Include(r => r.Source).SingleOrDefaultAsync(r => r.ScrapeRunId == id, ct);
        return run is null
            ? NotFound(ProblemTypes.Create(HttpContext, 404, ProblemTypes.NotFound, "Run not found.", $"No ingestion run with id {id}."))
            : Ok(RunDto.From(run));
    }

    private async Task<RunDto> ToDtoAsync(ScrapeRun run, CancellationToken ct)
    {
        run.Source ??= await db.Sources.AsNoTracking().SingleAsync(s => s.SourceId == run.SourceId, ct);
        return RunDto.From(run);
    }

    private static ProblemDetails WithActiveRun(ProblemDetails problem, long? activeRunId)
    {
        problem.Extensions["activeRunId"] = activeRunId;
        return problem;
    }
}

public sealed record CreateRunRequest
{
    [Required, StringLength(32, MinimumLength = 1)]
    public string Source { get; init; } = string.Empty;

    [StringLength(200)]
    public string? Query { get; init; }

    [Range(1, 10)]
    public short? MaxPages { get; init; }

    [StringLength(32), RegularExpression("^[a-z0-9-]+$", ErrorMessage = "fixtureSet may contain only a-z, 0-9 and '-'.")]
    public string? FixtureSet { get; init; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record RunDto(
    long RunId, string Source, string ScopeKey, string? Query, string? FixtureSet, RunStatus Status, RunStage Stage,
    bool IsComplete, short PagesFetched, int ListingsFetched, int ListingsRejected, int ListingsLoaded, int AiCalls,
    DateTime CreatedAtUtc, DateTime? StartedAtUtc, DateTime? FinishedAtUtc, double? DurationMs, string? ErrorMessage,
    string? HangfireJobId)
{
    public static RunDto From(ScrapeRun r) => new(
        r.ScrapeRunId, r.Source?.Code ?? r.SourceId.ToString(System.Globalization.CultureInfo.InvariantCulture), r.ScopeKey, r.Query, r.FixtureSet,
        r.Status, r.Stage, r.IsComplete, r.PagesFetched, r.ListingsFetched, r.ListingsRejected, r.ListingsLoaded, r.AiCalls,
        r.CreatedAtUtc, r.StartedAtUtc, r.FinishedAtUtc,
        r.FinishedAtUtc is { } f && r.StartedAtUtc is { } s ? Math.Round((f - s).TotalMilliseconds) : null,
        r.ErrorMessage, r.HangfireJobId);
}
