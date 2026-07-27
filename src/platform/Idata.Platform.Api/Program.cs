using Idata.Platform.Modules.AccessControl.Application;
using Idata.Platform.Modules.AccessControl.Infrastructure;
using Idata.Platform.Modules.AccessControl.Presentation;
using Idata.Platform.Modules.PlatformStatus.Application;
using Idata.Platform.Modules.PlatformStatus.Infrastructure;
using Idata.Platform.Modules.PlatformStatus.Presentation;
using Idata.Platform.Persistence;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI: dokumenti "platform-v1" — burimi i vetëm i kontratës së klientëve.
builder.Services.AddOpenApi("platform-v1", options =>
    options.AddOperationTransformer<Idata.Platform.Api.IdempotencyKeyRequiredTransformer>());

// Composition root (ADR-024): host-i lidh module portet me adapters.
// API NUK auto-migron database-in në startup — migrimi bëhet vetëm nga
// DbMigrator-i i veçantë. Context-i këtu shërben VETËM për query.
builder.Services.AddDbContext<PlatformDbContext>(
    options => PlatformDbContextFactory.Configure(options, PlatformDatabaseConnection.FromEnvironment()));
builder.Services.AddSingleton<IDatabaseReadinessProbe>(
    _ => new NpgsqlDatabaseReadinessProbe(PlatformDatabaseConnection.FromEnvironment()));
builder.Services.AddScoped<GetPlatformStatusHandler>();

// AccessControl (F1.1) — provider-neutral: host-i NUK regjistron asnjë
// authentication scheme (ADR-028 OPEN) → principal bosh → deny-by-default;
// override auth lejohet VETËM në integration-test assembly.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAccessControlStore, EfAccessControlStore>();
builder.Services.AddScoped<IMembershipRoleUnitOfWork, EfMembershipRoleUnitOfWork>();
builder.Services.AddScoped<GetAccessContextHandler>();
builder.Services.AddScoped<UpdateMembershipRoleHandler>();

var app = builder.Build();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapPlatformStatus();
app.MapAccessControl();

app.Run();

/// <summary>I dukshëm për WebApplicationFactory në integration tests.</summary>
public partial class Program;
