using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Usage:
//   dotnet run --project tools/Probe -- measure http://127.0.0.1:5311 results/measure.txt
//   dotnet run --project tools/Probe -- hostile http://127.0.0.1:5312 results/hostile.txt
// "measure" needs the server in Development (X-Sql-Commands header).
// "hostile" is meant for a server in Production (introspection off).
if (args.Length != 3 || args[0] is not ("measure" or "hostile"))
{
    Console.Error.WriteLine("usage: Probe measure|hostile <baseUrl> <outputFile>");
    return 2;
}

var http = new HttpClient { BaseAddress = new Uri(args[1]) };
var output = new List<string>();

void Log(string line)
{
    Console.WriteLine(line);
    output.Add(line);
}

async Task<Reply> SendAsync(HttpMethod method, string path, string? body = null, string? contentType = "application/json", string? accept = null)
{
    using var request = new HttpRequestMessage(method, path);
    if (body is not null)
    {
        request.Content = new StringContent(body, Encoding.UTF8);
        request.Content.Headers.ContentType = contentType is null ? null : MediaTypeHeaderValue.Parse(contentType);
    }

    if (accept is not null)
    {
        request.Headers.Accept.ParseAdd(accept);
    }

    using var response = await http.SendAsync(request);
    var bytes = await response.Content.ReadAsByteArrayAsync();
    string? Header(string name) =>
        response.Headers.TryGetValues(name, out var v) ? string.Join(",", v)
        : response.Content.Headers.TryGetValues(name, out var c) ? string.Join(",", c) : null;

    return new Reply(
        (int)response.StatusCode, bytes.Length, Encoding.UTF8.GetString(bytes),
        Header("X-Sql-Commands"), Header("Cache-Control"), Header("Content-Type"), Header("Vary"), Header("Allow"));
}

Task<Reply> GraphQLAsync(string query, string? accept = null) =>
    SendAsync(HttpMethod.Post, "/graphql", JsonSerializer.Serialize(new { query }), accept: accept);

// First GraphQL error code (or "-") and the first data key, to keep result lines short.
string Summary(Reply r)
{
    try
    {
        var json = JsonNode.Parse(r.Body);
        if (json is JsonArray array)
        {
            return $"items={array.Count}";
        }

        // GraphQL errors are an array; Problem Details "errors" is an object keyed by field.
        if (json?["errors"] is JsonArray { Count: > 0 } errors)
        {
            var error = errors[0]!;
            var code = error["extensions"]?["code"]?.ToString() ?? "-";
            return $"error code={code} message=\"{error["message"]}\"";
        }

        if (json?["data"] is JsonObject data)
        {
            return "data=" + string.Join(",", data.Select(kv => kv.Key));
        }

        if (json?["title"] is not null)
        {
            return $"problem title=\"{json["title"]}\" errors={json["errors"]?.ToJsonString() ?? "-"}";
        }
    }
    catch (JsonException)
    {
    }

    return $"body=\"{(r.Body.Length > 80 ? r.Body[..80] + "..." : r.Body)}\"";
}

int Sql(Reply r) => int.Parse(r.SqlCommands ?? "-1", CultureInfo.InvariantCulture);

if (args[0] == "measure")
{
    Log("## M1 list screen: 10 products with name, price and brand name");
    var list = await SendAsync(HttpMethod.Get, "/api/products?pageSize=10");
    var brandIds = JsonNode.Parse(list.Body)!.AsArray().Select(p => (int)p!["brandId"]!).Distinct().ToList();
    var restRequests = 1;
    var restBytes = list.Bytes;
    var restSql = Sql(list);
    foreach (var id in brandIds)
    {
        var brand = await SendAsync(HttpMethod.Get, $"/api/brands/{id}");
        restRequests++;
        restBytes += brand.Bytes;
        restSql += Sql(brand);
    }

    Log($"M1 REST    requests={restRequests} bytes={restBytes} sql={restSql} (list + {brandIds.Count} distinct brands)");
    var gqlList = await GraphQLAsync("{ products(first: 10) { name price brand { name } } }");
    Log($"M1 GraphQL requests=1 bytes={gqlList.Bytes} sql={Sql(gqlList)} status={gqlList.Status}");

    Log("## M2 the N+1 problem: SQL commands per request");
    foreach (var first in new[] { 1, 10, 50 })
    {
        var naive = await GraphQLAsync($"{{ products(first: {first}) {{ name brandNaive {{ name }} }} }}");
        var batched = await GraphQLAsync($"{{ products(first: {first}) {{ name brand {{ name }} }} }}");
        Log($"M2 first={first,2} brandNaive sql={Sql(naive)} status={naive.Status} | brand (DataLoader) sql={Sql(batched)} status={batched.Status}");
    }

    Log("## M3 detail screen: product 1 with brand and reviews");
    var restDetail = await SendAsync(HttpMethod.Get, "/api/products/1");
    Log($"M3 REST    requests=1 bytes={restDetail.Bytes} sql={Sql(restDetail)} status={restDetail.Status}");
    var gqlDetail = await GraphQLAsync("{ productById(id: 1) { name description price brand { name country } reviews { rating comment } } }");
    Log($"M3 GraphQL requests=1 bytes={gqlDetail.Bytes} sql={Sql(gqlDetail)} status={gqlDetail.Status}");

    Log("## M4 HTTP caching headers");
    Log($"M4 GET  /api/products        Cache-Control={list.CacheControl ?? "-"}");
    Log($"M4 POST /graphql             Cache-Control={gqlList.CacheControl ?? "-"} Vary={gqlList.Vary ?? "-"}");
    var gqlGet = await SendAsync(HttpMethod.Get, "/graphql?query=" + Uri.EscapeDataString("{ products(first: 10) { name } }"),
        accept: "application/graphql-response+json");
    Log($"M4 GET  /graphql?query=...   status={gqlGet.Status} Cache-Control={gqlGet.CacheControl ?? "-"} Vary={gqlGet.Vary ?? "-"}");

    Log("## M5 cost of a legitimate-looking list");
    var big = await GraphQLAsync("{ products(first: 50) { name brand { name } reviews { rating } } }");
    Log($"M5 products(first: 50) with brand and reviews -> {big.Status} {Summary(big)} sql={Sql(big)}");
    var mid = await GraphQLAsync("{ products(first: 40) { name brand { name } reviews { rating } } }");
    Log($"M5 products(first: 40) with brand and reviews -> {mid.Status} {Summary(mid)} sql={Sql(mid)}");
}
else
{
    async Task Gql(string id, string label, string query, string? accept = null)
    {
        var r = await GraphQLAsync(query, accept);
        Log($"{id} {label} -> {r.Status} {Summary(r)}");
    }

    async Task Rest(string id, string label, HttpMethod method, string path, string? body = null, string? contentType = "application/json")
    {
        var r = await SendAsync(method, path, body, contentType);
        Log($"{id} {label} -> {r.Status} {Summary(r)}");
    }

    Log("## H1 nesting depth (limit: 6)");
    await Gql("H1a", "depth 6: products.brand.products.brand.name", "{ products(first: 1) { brand { products { brand { name } } } } }");
    await Gql("H1b", "depth 8: products.brand.products.brand.products.brand.products.name",
        "{ products(first: 1) { brand { products { brand { products { brand { products { name } } } } } } } }");

    Log("## H2 alias amplification");
    var aliases = string.Join(" ", Enumerable.Range(1, 30).Select(i => $"a{i}: products(first: 50) {{ name brandNaive {{ name }} }}"));
    await Gql("H2", "30 aliases of products(first: 50) with brandNaive", "{ " + aliases + " }");

    Log("## H3 page size argument");
    await Gql("H3a", "first: 1000", "{ products(first: 1000) { name } }");
    await Gql("H3b", "first: 51", "{ products(first: 51) { name } }");
    await Gql("H3c", "first: 0", "{ products(first: 0) { name } }");
    await Gql("H3d", "first: -1", "{ products(first: -1) { name } }");
    await Gql("H3e", "first: 2147483648 (above Int)", "{ products(first: 2147483648) { name } }");

    Log("## H4 introspection in Production");
    await Gql("H4a", "__schema", "{ __schema { types { name } } }");
    await Gql("H4b", "__typename", "{ __typename }");
    var sdl = await SendAsync(HttpMethod.Get, "/graphql/schema.graphql");
    Log($"H4c GET /graphql/schema.graphql -> {sdl.Status} bytes={sdl.Bytes}");

    Log("## H5 transport");
    var getMutation = await SendAsync(HttpMethod.Get,
        "/graphql?query=" + Uri.EscapeDataString("mutation { addReview(productId: 1, rating: 5, comment: \"x\") { id } }"),
        accept: "application/graphql-response+json");
    Log($"H5a mutation over GET -> {getMutation.Status} Allow={getMutation.Allow ?? "-"} {Summary(getMutation)}");
    await Rest("H5b", "malformed JSON body", HttpMethod.Post, "/graphql", "{\"query\": ");
    await Rest("H5c", "query as text/plain", HttpMethod.Post, "/graphql", "{\"query\":\"{ __typename }\"}", "text/plain");
    await Rest("H5d", "JSON array (batch) of two operations", HttpMethod.Post, "/graphql",
        "[{\"query\":\"{ __typename }\"},{\"query\":\"{ __typename }\"}]");
    await Gql("H5e", "unknown field, Accept: application/json", "{ products { price2 } }", "application/json");
    await Gql("H5f", "unknown field, Accept: application/graphql-response+json", "{ products { price2 } }",
        "application/graphql-response+json");
    var manyFields = "{ " + string.Join(" ", Enumerable.Range(1, 3000).Select(i => $"f{i}: __typename")) + " }";
    await Gql("H5g", "3000 fields in one document", manyFields);

    Log("## H6 addReview input");
    static string Review(int productId, int rating, string comment) =>
        $"mutation {{ addReview(productId: {productId}, rating: {rating}, comment: {JsonSerializer.Serialize(comment)}) {{ id rating }} }}";
    await Gql("H6a", "rating 0", Review(1, 0, "ok"));
    await Gql("H6b", "rating 6", Review(1, 6, "ok"));
    await Gql("H6c", "empty comment", Review(1, 4, ""));
    await Gql("H6d", "comment of 3 spaces", Review(1, 4, "   "));
    await Gql("H6e", "comment of 500 chars", Review(1, 4, new string('x', 500)));
    await Gql("H6f", "comment of 501 chars", Review(1, 4, new string('x', 501)));
    await Gql("H6g", "comment of 250 emoji (500 UTF-16 units)", Review(1, 4, string.Concat(Enumerable.Repeat("\U0001F600", 250))));
    await Gql("H6h", "comment of 251 emoji (502 UTF-16 units)", Review(1, 4, string.Concat(Enumerable.Repeat("\U0001F600", 251))));
    await Gql("H6i", "productId 99999", Review(99999, 4, "ok"));
    await Gql("H6j", "comment with quotes and accents", Review(1, 4, "O'Brien said \"ñandú\""));
    await Gql("H6k", "rating as string \"5\"", "mutation { addReview(productId: 1, rating: \"5\", comment: \"ok\") { id } }");

    Log("## H7 REST input");
    await Rest("H7a", "GET /api/products?page=0", HttpMethod.Get, "/api/products?page=0");
    await Rest("H7b", "GET /api/products?page=10001", HttpMethod.Get, "/api/products?page=10001");
    await Rest("H7c", "GET /api/products?page=2147483647", HttpMethod.Get, "/api/products?page=2147483647");
    await Rest("H7d", "GET /api/products?pageSize=51", HttpMethod.Get, "/api/products?pageSize=51");
    await Rest("H7o", "GET /api/products?page=0&pageSize=0", HttpMethod.Get, "/api/products?page=0&pageSize=0");
    await Rest("H7e", "GET /api/products?page=abc", HttpMethod.Get, "/api/products?page=abc");
    await Rest("H7f", "GET /api/products?page=10000 (empty page)", HttpMethod.Get, "/api/products?page=10000");
    await Rest("H7g", "GET /api/products/abc", HttpMethod.Get, "/api/products/abc");
    await Rest("H7h", "GET /api/products/-1", HttpMethod.Get, "/api/products/-1");
    await Rest("H7i", "GET /api/products/99999", HttpMethod.Get, "/api/products/99999");
    await Rest("H7j", "POST review rating 9", HttpMethod.Post, "/api/products/1/reviews", "{\"rating\":9,\"comment\":\"ok\"}");
    await Rest("H7k", "POST review without comment", HttpMethod.Post, "/api/products/1/reviews", "{\"rating\":4}");
    await Rest("H7l", "POST review rating as string", HttpMethod.Post, "/api/products/1/reviews", "{\"rating\":\"5\",\"comment\":\"ok\"}");
    await Rest("H7m", "POST review malformed JSON", HttpMethod.Post, "/api/products/1/reviews", "{\"rating\":");
    await Rest("H7n", "POST review to product 99999", HttpMethod.Post, "/api/products/99999/reviews", "{\"rating\":4,\"comment\":\"ok\"}");
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
await File.WriteAllLinesAsync(args[2], output);
return 0;

internal sealed record Reply(
    int Status, int Bytes, string Body, string? SqlCommands, string? CacheControl, string? ContentType, string? Vary, string? Allow);
