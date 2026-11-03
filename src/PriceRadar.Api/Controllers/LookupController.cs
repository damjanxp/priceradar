using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PriceRadar.Api.Security;
using PriceRadar.Infrastructure.Queries;

namespace PriceRadar.Api.Controllers;

[ApiController]
[Route("api/lookup")]
[EnableRateLimiting(RateLimitPolicies.Lookup)]
public sealed class LookupController(LookupService lookup) : ControllerBase
{
    /// <summary>
    /// "Is this price cheap?" — matches the book and returns cheap / fair / expensive with the supporting statistics
    /// (FR-15, spec §8.4). Never calls the Claude API.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<LookupResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<LookupResult> Get(
        [FromQuery, Required, StringLength(300, MinimumLength = 2)] string title,
        [FromQuery, StringLength(200)] string? author,
        [FromQuery, Required, Range(typeof(decimal), "1", "1000000")] decimal askingPrice,
        [FromQuery, Range(1, 365)] int windowDays = 30,
        CancellationToken ct = default) =>
        lookup.LookupAsync(title, author, askingPrice, windowDays, ct);
}
