using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PriceRadar.Infrastructure.Queries;

namespace PriceRadar.Api.Controllers;

[ApiController]
[Route("api/catalog")]
public sealed class CatalogController(CatalogQuery catalog) : ControllerBase
{
    /// <summary>
    /// The catalogue: every book with its copies on sale now, their min / median / max asking price and the sold proxy.
    /// <c>q</c> is normalised like titles; <c>inStock=false</c> also lists books with nothing on sale.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<CatalogPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<CatalogPage> List(
        [FromQuery, StringLength(100, MinimumLength = 2)] string? q,
        [FromQuery] bool inStock = true,
        [FromQuery, Range(1, 100_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 24,
        [FromQuery, RegularExpression("^(title|-active|median|-median|-lastSeen)$",
            ErrorMessage = "sort must be one of: title, -active, median, -median, -lastSeen.")]
        string sort = "-active",
        CancellationToken ct = default)
    {
        var order = sort switch
        {
            "title" => CatalogSort.Title,
            "median" => CatalogSort.MedianAsc,
            "-median" => CatalogSort.MedianDesc,
            "-lastSeen" => CatalogSort.LastSeenDesc,
            _ => CatalogSort.ActiveDesc,
        };
        var (items, total, totals) = await catalog.GetPageAsync(q, inStock, page, pageSize, order, ct);
        return new CatalogPage(items, page, pageSize, total, totals);
    }
}

public sealed record CatalogPage(IReadOnlyList<CatalogItem> Items, int Page, int PageSize, int TotalCount, CatalogTotals Totals);
