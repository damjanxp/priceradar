using System.Reflection;
using NetArchTest.Rules;
using PriceRadar.Core.Domain;
using PriceRadar.Infrastructure.Persistence;

namespace PriceRadar.UnitTests;

/// <summary>The layering rules from spec §4.4, checked on every build so they cannot erode quietly.</summary>
public class ArchitectureTests
{
    private static readonly Assembly Core = typeof(Book).Assembly;
    private static readonly Assembly Infrastructure = typeof(PriceRadarDbContext).Assembly;
    private static readonly Assembly Api = typeof(PriceRadar.Api.Controllers.BooksController).Assembly;
    private static readonly Assembly Worker = Assembly.Load("PriceRadar.Worker");

    private static string Describe(TestResult result) =>
        result.IsSuccessful ? "" : "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []);

    [Fact]
    public void Core_HasNoInfrastructureDependencies()
    {
        var result = Types.InAssembly(Core).ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore", "Dapper", "Microsoft.Data.SqlClient", "Hangfire", "Microsoft.AspNetCore",
                "System.Net.Http", "PriceRadar.Infrastructure", "PriceRadar.Api", "PriceRadar.Worker")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Theory]
    [InlineData("PriceRadar.Core", "PriceRadar.Infrastructure")]
    [InlineData("PriceRadar.Core", "PriceRadar.Api")]
    [InlineData("PriceRadar.Core", "PriceRadar.Worker")]
    [InlineData("PriceRadar.Infrastructure", "PriceRadar.Api")]
    [InlineData("PriceRadar.Infrastructure", "PriceRadar.Worker")]
    [InlineData("PriceRadar.Api", "PriceRadar.Worker")]
    [InlineData("PriceRadar.Worker", "PriceRadar.Api")]
    public void Project_DoesNotReference(string project, string forbidden)
    {
        var assembly = new[] { Core, Infrastructure, Api, Worker }.Single(a => a.GetName().Name == project);

        assembly.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain(forbidden);
    }

    [Fact]
    public void Controllers_ContainNoSql()
    {
        // Controllers call query services; SQL lives in Infrastructure (spec §4.4).
        var result = Types.InAssembly(Api).That().ResideInNamespace("PriceRadar.Api.Controllers")
            .ShouldNot().HaveDependencyOnAny("Dapper", "Microsoft.Data.SqlClient")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Jobs_TakeOnlyTheRunId()
    {
        // Hangfire stores job arguments; a primitive run id keeps them small and forces jobs to reload state (spec §9.3).
        var jobs = Infrastructure.GetTypes().Where(t => t.Namespace == "PriceRadar.Infrastructure.Jobs" && t.Name.EndsWith("Job", StringComparison.Ordinal));

        foreach (var job in jobs)
        {
            var execute = job.GetMethod("ExecuteAsync").ShouldNotBeNull($"{job.Name} has no ExecuteAsync");
            execute.GetParameters().Select(p => p.ParameterType).ShouldBe([typeof(long), typeof(CancellationToken)], job.Name);
        }
    }
}
