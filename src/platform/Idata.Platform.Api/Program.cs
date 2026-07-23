using Idata.Platform.Api;
using Idata.Platform.Modules.PlatformStatus.Application;
using Idata.Platform.Modules.PlatformStatus.Infrastructure;
using Idata.Platform.Modules.PlatformStatus.Presentation;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI: dokumenti "platform-v1" — burimi i vetëm i kontratës së klientëve.
builder.Services.AddOpenApi("platform-v1");

// Composition root (ADR-024): host-i lidh module portet me adapters.
// API NUK auto-migron database-in në startup — migrimi bëhet vetëm nga
// DbMigrator-i i veçantë.
builder.Services.AddSingleton<IDatabaseReadinessProbe>(
    _ => new NpgsqlDatabaseReadinessProbe(PlatformDatabaseConnection.FromEnvironment()));
builder.Services.AddScoped<GetPlatformStatusHandler>();

var app = builder.Build();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapPlatformStatus();

app.Run();

/// <summary>I dukshëm për WebApplicationFactory në integration tests.</summary>
public partial class Program;
