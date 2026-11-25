using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Store.Api.Data;
using Store.Api.Rest;

var builder = WebApplication.CreateBuilder(args);

// In-memory SQLite: the database lives while this connection stays open.
// A unique name per app instance keeps test hosts from sharing data.
var keepAlive = new SqliteConnection($"Data Source=store-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
keepAlive.Open();
builder.Services.AddSingleton(keepAlive);

builder.Services.AddSingleton<SqlCommandCounter>();
builder.Services.AddPooledDbContextFactory<StoreDbContext>((sp, options) => options
    .UseSqlite(keepAlive.ConnectionString)
    .AddInterceptors(sp.GetRequiredService<SqlCommandCounter>()));

builder.Services.AddProblemDetails();

// Development makes minimal APIs throw on a parameter that cannot be bound; answer 400 there too.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

builder.AddGraphQL()
    .AddApiTypes()
    // brand -> products -> brand -> ... can nest without end; stop at 6 levels.
    .AddMaxExecutionDepthRule(6)
    // The schema stays readable at /graphql/schema.graphql; this only turns off __schema and __type queries.
    .DisableIntrospection(!builder.Environment.IsDevelopment());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<StoreDbContext>>();
    using var db = dbFactory.CreateDbContext();
    db.Database.EnsureCreated();
    SeedData.Fill(db);
}

// Demo only: report how many SQL commands each request sent, in an X-Sql-Commands response header.
if (app.Configuration.GetValue<bool>("Demo:SqlCommandHeader"))
{
    app.Use(async (context, next) =>
    {
        var count = SqlCommandCounter.BeginScope();
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Sql-Commands"] = count.Value.ToString(CultureInfo.InvariantCulture);
            return Task.CompletedTask;
        });
        await next(context);
    });
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapRestApi();
app.MapGraphQL();

app.Run();

public partial class Program;
