using Hangfire;
using Hangfire.Common;
using Hangfire.SqlServer;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Jobs;
using PriceRadar.IntegrationTests.Infrastructure;

namespace PriceRadar.IntegrationTests.Jobs;

/// <summary>
/// RunStateFilter end to end through real Hangfire SQL storage and the jobs' own [AutomaticRetry] rules: a failure that
/// will be retried leaves the run Running; only the final failure marks it Failed and frees the source (spec §9.3).
/// </summary>
[Collection(SqlCollection.Name)]
public sealed class RunStateFilterTests(MsSqlFixture sql) : IAsyncLifetime
{
    private SqlServerStorage _storage = null!;
    private RunStateFilter _filter = null!;

    public async Task InitializeAsync()
    {
        await sql.ResetAsync();
        _storage = new SqlServerStorage(sql.ConnectionString, new SqlServerStorageOptions { SchemaName = HangfireSetup.Schema });
        _filter = new RunStateFilter(sql.ConnectionString);
        GlobalJobFilters.Filters.Add(_filter);
    }

    public Task DisposeAsync()
    {
        GlobalJobFilters.Filters.Remove(_filter);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task FirstFailure_IsRetried_RunStaysRunning()
    {
        var runId = await CreateRunningRunAsync();
        var jobId = CreateJob(runId, retriesUsed: 0);

        new BackgroundJobClient(_storage).ChangeState(jobId, new FailedState(new TimeoutException("SQL timeout")));

        JobState(jobId).ShouldBe(ScheduledState.StateName);   // ScrapeJob: [AutomaticRetry(Attempts = 3)]
        (await LoadRunAsync(runId)).Status.ShouldBe(RunStatus.Running);
    }

    [Fact]
    public async Task FailureAfterLastRetry_MarksRunFailedWithMessage()
    {
        var runId = await CreateRunningRunAsync();
        var jobId = CreateJob(runId, retriesUsed: 3);

        new BackgroundJobClient(_storage).ChangeState(jobId, new FailedState(new FileNotFoundException("fixture vanished")));

        JobState(jobId).ShouldBe(FailedState.StateName);
        var run = await LoadRunAsync(runId);
        run.Status.ShouldBe(RunStatus.Failed);
        run.ErrorMessage.ShouldBe("ScrapeJob: FileNotFoundException: fixture vanished");
        run.FinishedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task FailedRun_FreesTheSourceForANewRun()
    {
        var runId = await CreateRunningRunAsync();
        var jobId = CreateJob(runId, retriesUsed: 3);
        new BackgroundJobClient(_storage).ChangeState(jobId, new FailedState(new InvalidOperationException("boom")));

        // UX_ScrapeRun_ActivePerSource only covers Queued/Running, so a second run for the same source is accepted now.
        await using var db = sql.CreateDbContext();
        await new TestData(db).RunAsync(DateTime.UtcNow, RunStatus.Queued);
        (await db.ScrapeRuns.CountAsync()).ShouldBe(2);
    }

    private string CreateJob(long runId, int retriesUsed)
    {
        var jobId = new BackgroundJobClient(_storage).Create(
            Job.FromExpression<ScrapeJob>(j => j.ExecuteAsync(runId, CancellationToken.None)), new EnqueuedState());

        // AutomaticRetry keeps its counter in the "RetryCount" job parameter.
        using var connection = _storage.GetConnection();
        connection.SetJobParameter(jobId, "RetryCount", retriesUsed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return jobId;
    }

    private string JobState(string jobId)
    {
        using var connection = _storage.GetConnection();
        return connection.GetJobData(jobId)!.State;
    }

    private async Task<long> CreateRunningRunAsync()
    {
        await using var db = sql.CreateDbContext();
        return (await new TestData(db).RunAsync(DateTime.UtcNow, RunStatus.Running)).ScrapeRunId;
    }

    private async Task<ScrapeRun> LoadRunAsync(long runId)
    {
        await using var db = sql.CreateDbContext();
        return await db.ScrapeRuns.AsNoTracking().SingleAsync(r => r.ScrapeRunId == runId);
    }
}
