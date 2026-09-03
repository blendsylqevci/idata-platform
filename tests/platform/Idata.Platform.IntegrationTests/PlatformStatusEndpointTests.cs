using System.Net;
using System.Net.Http.Json;
using Idata.Platform.Modules.PlatformStatus.Application;
using Idata.Platform.Modules.PlatformStatus.Domain;
using Idata.Platform.Modules.PlatformStatus.Presentation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Kontrata e endpoint-it + kompozimi DI përmes host-it real (WebApplicationFactory).
/// Probe-i zëvendësohet me fake për skenarët ready/unavailable — testi me
/// PostgreSQL real jeton te testet e migration path-it.
/// </summary>
public sealed class PlatformStatusEndpointTests
{
    private sealed class FakeProbe(DatabaseState state) : IDatabaseReadinessProbe
    {
        public Task<DatabaseState> CheckAsync(CancellationToken cancellationToken)
            => Task.FromResult(state);
    }

    private static WebApplicationFactory<Program> HostWithProbe(DatabaseState state) =>
        new PlatformTestHostFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IDatabaseReadinessProbe>(
                    new FakeProbe(state)))));

    [Fact]
    public async Task Get_platform_status_kthen_kontraten_e_tipizuar_kur_db_ready()
    {
        await using var factory = HostWithProbe(DatabaseState.Ready);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/platform/status", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PlatformStatusResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("OK", payload.Status);
        Assert.Equal("platform-v1", payload.ApiVersion);
        Assert.Equal("READY", payload.Database);
        Assert.Equal("NOT_CONFIGURED_F1_PENDING", payload.Capabilities.Identity);
    }

    [Fact]
    public async Task Database_unavailable_kthen_pergjigje_te_kontrolluar_jo_exception()
    {
        await using var factory = HostWithProbe(DatabaseState.Unavailable);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/platform/status", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PlatformStatusResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("DEGRADED", payload.Status);
        Assert.Equal("UNAVAILABLE", payload.Database);
    }

    [Fact]
    public async Task Hosti_ekspozon_vetem_status_dhe_openapi_zero_rruge_biznesi_ose_fiskale()
    {
        await using var factory = HostWithProbe(DatabaseState.Ready);
        using var client = factory.CreateClient();

        foreach (var forbidden in (string[])["/ca/verify", "/pos/coupon", "/citizen/coupon", "/api/accounting"])
        {
            var response = await client.GetAsync(
                new Uri(forbidden, UriKind.Relative),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
