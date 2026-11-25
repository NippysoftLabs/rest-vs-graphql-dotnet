# Results

Run on 2026-11-24, Windows 11, .NET SDK 10.0.401, ASP.NET Core runtime 10.0.12,
Hot Chocolate 16.7.7, EF Core 10.0.15 with in-memory SQLite. Server and probe on the same machine (loopback).
Each file comes from a freshly started server.

| File | Server environment | What it holds |
|---|---|---|
| `measure.txt` | Development | Requests, response bytes and SQL commands per screen (M1 to M5) |
| `hostile.txt` | Production | Hostile and edge-case inputs for both APIs (H1 to H7) |
| `tests.txt` | Development (WebApplicationFactory) | `dotnet test` summary |

Byte counts are response bodies only, without HTTP headers or compression.
The M1 list happens to cover 10 distinct brands (product n belongs to brand ((n - 1) mod 10) + 1),
which is the worst case for the REST client: one extra request per product.
