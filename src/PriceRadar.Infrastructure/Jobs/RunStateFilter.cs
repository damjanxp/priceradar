using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Data.SqlClient;

namespace PriceRadar.Infrastructure.Jobs;

/// <summary>
/// When a pipeline job ends up in Hangfire's Failed state (all retries used), marks its ScrapeRun Failed and stores the
/// error, which also frees the source for the next run (spec §9.3). Retries in between are not failures.
/// </summary>
public sealed class RunStateFilter(string connectionString) : IApplyStateFilter
{
    private static readonly HashSet<Type> PipelineJobs = [typeof(ScrapeJob), typeof(MatchJob), typeof(ResolveJob), typeof(LoadJob)];

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState is not FailedState failed
            || context.BackgroundJob.Job is not { } job
            || !PipelineJobs.Contains(job.Type)
            || job.Args.Count == 0 || job.Args[0] is not long runId)
        {
            return;
        }

        var message = $"{job.Type.Name}: {failed.Exception.GetType().Name}: {failed.Exception.Message}";
        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(
            """
            UPDATE ingest.ScrapeRun
            SET Status = 3, ErrorMessage = @message, FinishedAtUtc = SYSUTCDATETIME()
            WHERE ScrapeRunId = @runId AND Status IN (0, 1)
            """, connection);
        command.Parameters.AddWithValue("@runId", runId);
        command.Parameters.AddWithValue("@message", message.Length > 2000 ? message[..2000] : message);
        connection.Open();
        command.ExecuteNonQuery();
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }
}
