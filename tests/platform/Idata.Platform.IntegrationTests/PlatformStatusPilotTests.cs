using System.Net;
using System.Text.Json;
using Idata.Platform.Modules.PlatformStatus.Application;
using Idata.Platform.Modules.PlatformStatus.Application.Features.GetPlatformStatus;
using Idata.Platform.Modules.PlatformStatus.Domain;
using Mediator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Provat e pilotit të Increment A (ADR-029): kontrata RAW JSON me emra
/// EKZAKTË (jo vetëm deserializim case-insensitive), kufiri i environment-it
/// për runtime OpenAPI + Scalar (vetëm Development; fail-closed për
/// Staging/Production/environment të panjohur), dispatch real përmes
/// mediator-it të gjeneruar dhe prova mekanike e lifetime-it SCOPED.
/// </summary>
public sealed class PlatformStatusPilotTests
{
    private sealed class FakeProbe(DatabaseState state) : IDatabaseReadinessProbe
    {
        public Task<DatabaseState> CheckAsync(CancellationToken cancellationToken)
            => Task.FromResult(state);
    }

    private static WebApplicationFactory<Program> Host(DatabaseState state, string? environment = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            if (environment is not null)
            {
                builder.UseSetting(WebHostDefaults.EnvironmentKey, environment);
            }

            builder.ConfigureServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IDatabaseReadinessProbe>(
                    new FakeProbe(state))));
        });

    // ══════════ RAW JSON — emra EKZAKTË, jo vetëm deserializim ══════════

    private static async Task AssertRawContractAsync(
        DatabaseState state, string expectedStatus, string expectedDatabase)
    {
        await using var factory = Host(state);
        using var client = factory.CreateClient();
        var body = await client.GetStringAsync(
            new Uri("/api/platform/status", UriKind.Relative), TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);

        // Seti EKZAKT i top-level properties (pa varësi nga rendi, zero shtesa).
        var topLevel = root.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(["apiVersion", "capabilities", "database", "status"], topLevel);

        Assert.Equal(JsonValueKind.String, root.GetProperty("status").ValueKind);
        Assert.Equal(expectedStatus, root.GetProperty("status").GetString());
        Assert.Equal("platform-v1", root.GetProperty("apiVersion").GetString());
        Assert.Equal(expectedDatabase, root.GetProperty("database").GetString());

        // Seti EKZAKT i nested capabilities + vlerat ekzakte.
        var capabilities = root.GetProperty("capabilities");
        Assert.Equal(JsonValueKind.Object, capabilities.ValueKind);
        var capabilityNames = capabilities.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(["audit", "identity", "observability", "outbox"], capabilityNames);
        foreach (var capability in capabilities.EnumerateObject())
        {
            Assert.Equal(JsonValueKind.String, capability.Value.ValueKind);
            Assert.Equal("NOT_CONFIGURED_F1_PENDING", capability.Value.GetString());
        }

        // Zero variante PascalCase — regres i mundshëm i JSON options që
        // deserializimi case-insensitive nuk do ta kapte kurrë.
        foreach (var pascal in (string[])
                 ["ApiVersion", "Capabilities", "Database", "Status",
                  "Audit", "Identity", "Observability", "Outbox"])
        {
            Assert.DoesNotContain($"\"{pascal}\"", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public Task Raw_JSON_contract_READY_ka_emra_ekzakte_camelCase()
        => AssertRawContractAsync(DatabaseState.Ready, "OK", "READY");

    [Fact]
    public Task Raw_JSON_contract_UNAVAILABLE_ka_emra_ekzakte_camelCase()
        => AssertRawContractAsync(DatabaseState.Unavailable, "DEGRADED", "UNAVAILABLE");

    // ══════════ ENVIRONMENT MATRIX — OpenAPI/Scalar vetëm në Development ══════════

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Staging", HttpStatusCode.NotFound)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    [InlineData("PilotUnknownEnv", HttpStatusCode.NotFound)]
    public async Task Runtime_OpenAPI_dhe_Scalar_ekspozohen_vetem_ne_Development(
        string environment, HttpStatusCode docsExpected)
    {
        await using var factory = Host(DatabaseState.Ready, environment);
        using var client = factory.CreateClient();

        var openApi = await client.GetAsync(
            new Uri("/openapi/platform-v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(docsExpected, openApi.StatusCode);

        var scalar = await client.GetAsync(
            new Uri("/scalar/platform-v1", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(docsExpected, scalar.StatusCode);

        // Endpoint-i funksional i statusit punon në ÇDO environment.
        var status = await client.GetAsync(
            new Uri("/api/platform/status", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
    }

    // ══════════ ROUTE TABLE — environment-aware, zero duplicate ══════════

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public async Task Route_table_ka_saktesisht_nje_status_route_dhe_docs_vetem_ne_Development(
        string environment, bool docsExpected)
    {
        await using var factory = Host(DatabaseState.Ready, environment);
        _ = factory.CreateClient(); // ndez host-in që route table të materializohet

        // Pattern-et MVC s'kanë "/" nistore; minimal API-t e kanë — normalizohet.
        var routes = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .ToArray();

        Assert.Single(routes, route => route == "api/platform/status");
        Assert.Equal(docsExpected, routes.Any(route =>
            route.Contains("openapi", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(docsExpected, routes.Any(route =>
            route.Contains("scalar", StringComparison.OrdinalIgnoreCase)));
    }

    // ══════════ MEDIATOR — dispatch real + lifetime SCOPED ══════════

    [Fact]
    public async Task Query_dispatch_permes_mediator_it_te_gjeneruar_kthen_raportin_real()
    {
        await using var factory = Host(DatabaseState.Ready);
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var report = await mediator.Send(new GetPlatformStatusQuery(), TestContext.Current.CancellationToken);

        Assert.Equal(PlatformHealth.Ok, report.Status);
        Assert.Equal(DatabaseState.Ready, report.Database);
        Assert.Equal(BootstrapCapabilities.F1Pending, report.Capabilities);
    }

    [Fact]
    public async Task Mediator_ka_lifetime_SCOPED_jo_Singleton_dhe_jo_Transient()
    {
        await using var factory = Host(DatabaseState.Ready);
        _ = factory.CreateClient();

        using var scopeA = factory.Services.CreateScope();
        using var scopeB = factory.Services.CreateScope();
        var sameScope1 = scopeA.ServiceProvider.GetRequiredService<IMediator>();
        var sameScope2 = scopeA.ServiceProvider.GetRequiredService<IMediator>();
        var otherScope = scopeB.ServiceProvider.GetRequiredService<IMediator>();

        Assert.Same(sameScope1, sameScope2);   // jo Transient
        Assert.NotSame(sameScope1, otherScope); // jo Singleton
    }
}
