using Idata.Platform.Modules.AccessControl.Application;
using Idata.Platform.Modules.AccessControl.Infrastructure;
using Idata.Platform.Modules.AccessControl.Presentation;
using Idata.Platform.Modules.PlatformStatus.Application;
using Idata.Platform.Modules.PlatformStatus.Application.Features.GetPlatformStatus;
using Idata.Platform.Modules.PlatformStatus.Infrastructure;
using Idata.Platform.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI: dokumenti "platform-v1" — burimi i vetëm i kontratës së klientëve.
builder.Services.AddOpenApi("platform-v1", options =>
{
    options.AddOperationTransformer<Idata.Platform.Api.IdempotencyKeyRequiredTransformer>();
    options.AddDocumentTransformer<Idata.Platform.Api.CanonicalContractOrderTransformer>();
});

// Composition root (ADR-024): host-i lidh module portet me adapters.
// API NUK auto-migron database-in në startup — migrimi bëhet vetëm nga
// DbMigrator-i i veçantë. Context-i këtu shërben VETËM për query.
builder.Services.AddDbContext<PlatformDbContext>(
    options => PlatformDbContextFactory.Configure(options, PlatformDatabaseConnection.FromEnvironment()));
builder.Services.AddSingleton<IDatabaseReadinessProbe>(
    _ => new NpgsqlDatabaseReadinessProbe(PlatformDatabaseConnection.FromEnvironment()));

// ADR-029: mediator-i (martinothamar/Mediator) me lifetime SCOPED eksplicit —
// handler-at konsumojnë porte/DbContext me scope kërkese; pa u mbështetur te
// default-i Singleton. Zero pipeline behaviors në këtë pilot.
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(GetPlatformStatusQuery).Assembly];
});

// ADR-029: thin Controllers (PlatformStatus pilot); minimal API-t e
// AccessControl mbeten të paprekura deri në Increment C.
builder.Services.AddControllers();

// AccessControl (F1.1) — provider-neutral: host-i NUK regjistron asnjë
// authentication scheme (ADR-028 OPEN) → principal bosh → deny-by-default;
// override auth lejohet VETËM në integration-test assembly.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAccessControlStore, EfAccessControlStore>();
builder.Services.AddScoped<IMembershipRoleUnitOfWork, EfMembershipRoleUnitOfWork>();
builder.Services.AddScoped<GetAccessContextHandler>();
builder.Services.AddScoped<UpdateMembershipRoleHandler>();

var app = builder.Build();

// ADR-029 §10: runtime OpenAPI JSON + Scalar UI VETËM në Development —
// Staging/Production/environment i panjohur = absent, FAIL-CLOSED.
// KUFI KRITIK: AddOpenApi(...) + transformer-at MË LART mbeten TË
// PAKUSHTËZUAR — gjenerimi build-time i kontratës i konsumon ato;
// këtu guardohet VETËM ekspozimi runtime.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/{documentName}.json");
    app.MapScalarApiReference(options => options.AddDocument("platform-v1"));
}

app.MapControllers();
app.MapAccessControl();

app.Run();

/// <summary>I dukshëm për WebApplicationFactory në integration tests.</summary>
public partial class Program;
