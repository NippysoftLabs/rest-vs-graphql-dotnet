# REST vs GraphQL in .NET 10

One small store catalog (brands, products, reviews) served two ways by the same ASP.NET Core app:

| API | Endpoint | Who decides the shape of the response |
|---|---|---|
| REST (minimal APIs) | `GET /api/products`, `GET /api/products/{id}`, `GET /api/brands/{id}`, `POST /api/products/{id}/reviews` | The server: one resource per URL |
| GraphQL ([Hot Chocolate](https://chillicream.com/docs/hotchocolate), MIT) | `POST /graphql` (queries also over `GET`) | The client: it selects the fields |

Data lives in an in-memory SQLite database (EF Core 10) that is created and seeded at startup:
10 brands, 50 products and 40 reviews.

## Things the code shows on purpose

- **The N+1 problem.** `Product.brandNaive` loads the brand of each product with its own SQL command, so a list of N products costs N + 1 commands. `Product.brand` uses a DataLoader and costs 2 commands for any list size (`results/measure.txt`, M2).
- **Under-fetching in REST.** A list screen that needs the brand name calls `/api/products` and then `/api/brands/{id}` once per distinct brand (M1).
- **HTTP caching.** REST `GET` responses carry `Cache-Control: public, max-age=60`, or `private, max-age=60` when the request has an `Authorization` header, so a shared cache never stores what may be user-specific data (H7p, H7q). GraphQL responses carry no `Cache-Control` here: Hot Chocolate needs `@cacheControl` metadata and a `GET` route before a shared cache can reuse them (M4).
- **Limits a public GraphQL endpoint needs.** Maximum execution depth 6, Hot Chocolate's default cost analysis (field cost 1,000, type cost 10,000), parser limit of 2,048 fields, introspection and the Nitro IDE off outside Development, `first` between 1 and 50, and a cost weight of 100 on `addReview`, so one operation holds at most 10 of them (`results/hostile.txt`, H6l and H6m).
- **The cost limit also rejects honest queries.** `products(first: 50) { brand reviews }` is rejected with `HC0047`; `first: 40` passes (M5).
- **Same input rules on both APIs.** `Limits.ValidateReview` is used by `POST /api/products/{id}/reviews` and by the `addReview` mutation.

## Run it

Requires the .NET 10 SDK (see `global.json`).

```
dotnet test --solution RestVsGraphQL.slnx
dotnet run --project src/Store.Api --urls http://127.0.0.1:5311
```

The GraphQL IDE (Nitro) is served at `/graphql` in a browser.

To repeat the measurements and the hostile inputs:

```
# terminal 1 (Development: X-Sql-Commands header on)
dotnet run --project src/Store.Api -- --environment Development --urls http://127.0.0.1:5311
# terminal 2
dotnet run --project tools/Probe -- measure http://127.0.0.1:5311 results/measure.txt

# terminal 1 (Production: introspection off)
dotnet run --project src/Store.Api -- --environment Production --urls http://127.0.0.1:5312
# terminal 2
dotnet run --project tools/Probe -- hostile http://127.0.0.1:5312 results/hostile.txt
```

## What this code does not protect against

- No authentication or authorization: every field and the `addReview` mutation are open.
- No rate limiting: cost analysis bounds one request, not how many requests a client sends. A rate limiter that counts requests sees one request for 10 `addReview` aliases.
- Cached catalog responses can be stale: a new review shows in `GET /api/products/{id}` only after the cached copy (60 s) expires.
- No HTTP caching for GraphQL: responses are computed on every request.
- `X-Sql-Commands` is a demo header, on only when `Demo:SqlCommandHeader` is `true` (Development). It counts per request and is not meant for production.
- The REST body binder uses the ASP.NET Core web JSON defaults, which read numbers from strings: `{"rating":"5"}` is accepted (`results/hostile.txt`, H7l). GraphQL rejects a string for an `Int` (H6k).
- Reviews are not paged. Cost analysis assumes up to 20 reviews per product; a product with more still returns them all.
