namespace PriceRadar.Core.Domain;

/// <summary>A canonical book: one per normalised (author, title) pair (dw.Book).</summary>
public class Book
{
    public int BookId { get; set; }

    /// <summary>Display form with diacritics.</summary>
    public required string Title { get; set; }
    public string? Author { get; set; }

    public required string NormTitle { get; set; }

    /// <summary>Empty string when the author is unknown, never null, so <see cref="NormKey"/> is always defined.</summary>
    public string NormAuthor { get; set; } = string.Empty;

    /// <summary>Computed by the database as <c>NormAuthor + '|' + NormTitle</c>.</summary>
    public string NormKey { get; private set; } = string.Empty;

    public BookOrigin Origin { get; set; }
    public bool NeedsReview { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
