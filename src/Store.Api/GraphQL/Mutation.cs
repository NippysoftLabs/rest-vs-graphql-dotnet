using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Store.Api.Data;

namespace Store.Api.GraphQL;

[MutationType]
public static partial class Mutation
{
    // Same rules as POST /api/products/{id}/reviews (see Limits.ValidateReview).
    // Weight 100: cost analysis rejects an operation whose field cost exceeds the default MaxFieldCost of 1,000,
    // so one operation holds at most 10 addReview aliases.
    [Cost(100)]
    public static async Task<Review> AddReviewAsync(
        int productId, int rating, string comment,
        IDbContextFactory<StoreDbContext> dbFactory, CancellationToken ct)
    {
        var error = Limits.ValidateReview(rating, comment);
        if (error is not null)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage(error)
                .SetCode("STORE_INVALID_REVIEW")
                .Build());
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct))
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage($"Product {productId} not found.")
                .SetCode("STORE_PRODUCT_NOT_FOUND")
                .Build());
        }

        var review = new Review { ProductId = productId, Rating = rating, Comment = comment };
        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
        return review;
    }
}
