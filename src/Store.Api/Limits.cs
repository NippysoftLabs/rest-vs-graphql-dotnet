using Store.Api.Data;

namespace Store.Api;

// One place for the limits both APIs enforce, so REST and GraphQL reject the same inputs.
public static class Limits
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 50;

    // Page numbers above this are rejected before (page - 1) * pageSize is computed.
    public const int MaxPage = 10_000;

    public const int MinRating = 1;
    public const int MaxRating = 5;

    // Returns an error message, or null when the review is valid.
    // Length is counted in UTF-16 code units (string.Length), the same unit as HasMaxLength(MaxCommentLength).
    // SQLite does not enforce that length, so this check is the only limit.
    public static string? ValidateReview(int rating, string? comment)
    {
        if (rating is < MinRating or > MaxRating)
        {
            return $"rating must be between {MinRating} and {MaxRating}.";
        }

        if (string.IsNullOrWhiteSpace(comment))
        {
            return "comment must not be empty.";
        }

        if (comment.Length > StoreDbContext.MaxCommentLength)
        {
            return $"comment must be at most {StoreDbContext.MaxCommentLength} characters.";
        }

        return null;
    }
}
