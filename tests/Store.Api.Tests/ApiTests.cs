using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Store.Api.Tests;

// WebApplicationFactory runs the app in the Development environment unless a test changes it,
// so the X-Sql-Commands header is on and introspection is allowed.
public sealed class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(HttpResponseMessage Response, JsonNode Body)> PostGraphQLAsync(HttpClient client, string query)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query }, Ct);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
        return (response, body);
    }

    private static int SqlCommands(HttpResponseMessage response) =>
        int.Parse(response.Headers.GetValues("X-Sql-Commands").Single());

    [Theory]
    [InlineData(1, 2)]
    [InlineData(10, 11)]
    [InlineData(50, 51)]
    public async Task Naive_brand_resolver_sends_one_query_per_product(int first, int expectedSql)
    {
        var (response, _) = await PostGraphQLAsync(factory.CreateClient(), $"{{ products(first: {first}) {{ brandNaive {{ name }} }} }}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedSql, SqlCommands(response));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    public async Task DataLoader_brand_resolver_sends_two_queries_for_any_list_size(int first)
    {
        var (response, body) = await PostGraphQLAsync(factory.CreateClient(), $"{{ products(first: {first}) {{ brand {{ name }} }} }}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(first, body["data"]!["products"]!.AsArray().Count);
        Assert.Equal(2, SqlCommands(response));
    }

    [Fact]
    public async Task Rest_list_is_cacheable_by_shared_caches()
    {
        var response = await factory.CreateClient().GetAsync("/api/products", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=60", response.Headers.CacheControl!.ToString());
        Assert.Equal(1, SqlCommands(response));
    }

    [Fact]
    public async Task Rest_rejects_only_the_parameter_that_is_out_of_range()
    {
        var response = await factory.CreateClient().GetAsync("/api/products?pageSize=51", Ct);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = body["errors"]!.AsObject();
        Assert.True(errors.ContainsKey("pageSize"));
        Assert.False(errors.ContainsKey("page"));
    }

    // Development sets RouteHandlerOptions.ThrowOnBadRequest to true; Program.cs turns it off so these stay 400, not 500.
    [Theory]
    [InlineData("/api/products?page=abc")]
    [InlineData("/api/products?page=")]
    [InlineData("/api/products?pageSize=1.5")]
    [InlineData("/api/products?page=1&page=2")]
    public async Task Rest_answers_400_when_a_query_parameter_cannot_be_bound(string path)
    {
        var response = await factory.CreateClient().GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    [InlineData(1000)]
    public async Task GraphQL_rejects_first_out_of_range(int first)
    {
        var (_, body) = await PostGraphQLAsync(factory.CreateClient(), $"{{ products(first: {first}) {{ name }} }}");

        Assert.Equal("STORE_INVALID_FIRST", body["errors"]![0]!["extensions"]!["code"]!.ToString());
        Assert.Null(body["data"]?["products"]);
    }

    [Fact]
    public async Task GraphQL_rejects_queries_deeper_than_six_levels()
    {
        var (response, body) = await PostGraphQLAsync(factory.CreateClient(),
            "{ products(first: 1) { brand { products { brand { products { brand { products { name } } } } } } } }");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("execution depth of 8", body["errors"]![0]!["message"]!.ToString());
    }

    [Fact]
    public async Task Mutation_over_GET_is_refused()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/graphql-response+json");
        var query = Uri.EscapeDataString("mutation { addReview(productId: 1, rating: 5, comment: \"x\") { id } }");

        var response = await client.GetAsync($"/graphql?query={query}", Ct);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public async Task One_operation_holds_at_most_ten_addReview_aliases(int count, bool accepted)
    {
        var aliases = string.Join(" ", Enumerable.Range(1, count)
            .Select(i => $"r{i}: addReview(productId: 3, rating: 4, comment: \"batch\") {{ id }}"));

        var (_, body) = await PostGraphQLAsync(factory.CreateClient(), "mutation { " + aliases + " }");

        if (accepted)
        {
            Assert.Equal(count, body["data"]!.AsObject().Count);
        }
        else
        {
            Assert.Equal("HC0047", body["errors"]![0]!["extensions"]!["code"]!.ToString());
        }
    }

    [Fact]
    public async Task Rest_does_not_allow_shared_caching_when_the_request_has_authorization()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer demo");

        var list = await client.GetAsync("/api/products", Ct);
        var detail = await client.GetAsync("/api/products/1", Ct);
        var brand = await client.GetAsync("/api/brands/1", Ct);

        // HttpClient parses Cache-Control, so compare its parts, not the header text.
        foreach (var (response, maxAge) in new[] { (list, 60), (detail, 60), (brand, 300) })
        {
            var cacheControl = response.Headers.CacheControl!;
            Assert.True(cacheControl.Private);
            Assert.False(cacheControl.Public);
            Assert.Equal(TimeSpan.FromSeconds(maxAge), cacheControl.MaxAge);
        }
    }

    [Fact]
    public async Task Nitro_ide_is_served_only_in_development()
    {
        using var production = factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/graphql/");
        request.Headers.Accept.ParseAdd("text/html");
        using var devRequest = new HttpRequestMessage(HttpMethod.Get, "/graphql/");
        devRequest.Headers.Accept.ParseAdd("text/html");

        var inProduction = await production.CreateClient().SendAsync(request, Ct);
        var inDevelopment = await factory.CreateClient().SendAsync(devRequest, Ct);

        Assert.NotEqual("text/html", inProduction.Content.Headers.ContentType?.MediaType);
        Assert.Equal("text/html", inDevelopment.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Introspection_is_refused_in_production()
    {
        using var production = factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));

        var (_, body) = await PostGraphQLAsync(production.CreateClient(), "{ __schema { types { name } } }");

        Assert.Equal("HC0046", body["errors"]![0]!["extensions"]!["code"]!.ToString());
    }
}
