using Idata.Platform.Modules.AccessControl.Application;
using Idata.Platform.Modules.AccessControl.Infrastructure;
using Idata.Platform.Modules.AccessControl.Presentation;
using Idata.Platform.Modules.Identity.Infrastructure;
using Idata.Platform.Modules.Identity.Presentation;
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

// ADR-028 Increment B.1 — Identity STORE foundation: Identity roleless
// (UserOnlyStore) + OpenIddict Core/EF me Guid mbi PlatformDbContext.
builder.Services.AddIdentityStoreFoundation();

// ADR-028 Increment B.2a — authority OpenIddict CO-LOCATED (owner i wiring-ut =
// Identity.Infrastructure; host-i nuk referon asnjë tip OpenIddict): Server
// (issuer/endpoint-e/PKCE, credentials FAIL-CLOSED nga konfigurimi i jashtëm)
// + Validation lokale (UseLocalServer) + skema default = handler-i i validimit.
// Zero projection (B.3), zero flow biznesi.
builder.Services.AddIdentityAuthority(builder.Configuration, builder.Environment);

// ADR-028 Increment B.2b — interaksioni REAL i authority-t (owner i wiring-ut =
// Identity.Infrastructure): login me ASP.NET Core Identity, session cookie e
// VEÇANTË (jo skema default e API-së), consent eksplicit, antiforgery dhe rate
// limiting. Vetëm OAuth 2.0 Authorization Code + PKCE: zero `openid`, zero ID
// token, zero UserInfo, zero refresh/logout, zero MFA (mbeten OPEN).
builder.Services.AddIdentityInteractiveAuthorization(builder.Configuration);

// AccessControl (F1.1) — provider-neutral: autoriteti i autorizimit tenant mbetet
// DB-backed deny-by-default; principal-i vjen VETËM nga handler-i i validimit
// OpenIddict (B.2a) — pa token valid principal-i mbetet bosh; override auth
// lejohet VETËM në integration-test assembly.
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

// B.2a: middleware-i REAL i autentikimit/autorizimit — PARA çdo endpoint-i.
app.UseAuthentication();
app.UseAuthorization();

// B.2b: kufijtë e sipërfaqes interaktive — rate limiting i deklaruar per-endpoint
// dhe antiforgery i detyrueshëm për çdo POST formulari.
app.UseRateLimiter();
app.UseAntiforgery();

app.MapControllers();
app.MapAccessControl();
app.MapIdentityInteractiveAuthorization();

app.Run();

/// <summary>I dukshëm për WebApplicationFactory në integration tests.</summary>
public partial class Program;
