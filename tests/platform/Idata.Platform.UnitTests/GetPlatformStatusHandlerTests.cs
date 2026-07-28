using Idata.Platform.Modules.PlatformStatus.Application;
using Idata.Platform.Modules.PlatformStatus.Application.Features.GetPlatformStatus;
using Idata.Platform.Modules.PlatformStatus.Domain;
using Idata.Platform.Modules.PlatformStatus.Presentation;
using Xunit;

namespace Idata.Platform.UnitTests;

/// <summary>Handler-i i smoke slice-it + kontrata e përgjigjes (fixtures sintetike).</summary>
public sealed class GetPlatformStatusHandlerTests
{
    private sealed class FakeProbe(DatabaseState state) : IDatabaseReadinessProbe
    {
        public Task<DatabaseState> CheckAsync(CancellationToken cancellationToken)
            => Task.FromResult(state);
    }

    [Fact]
    public async Task Database_ready_prodhon_status_ok()
    {
        var handler = new GetPlatformStatusHandler(new FakeProbe(DatabaseState.Ready));
        var report = await handler.Handle(new GetPlatformStatusQuery(), TestContext.Current.CancellationToken);
        Assert.Equal(PlatformHealth.Ok, report.Status);
        Assert.Equal(DatabaseState.Ready, report.Database);
    }

    [Fact]
    public async Task Database_unavailable_prodhon_gjendje_te_kontrolluar_degraded()
    {
        var handler = new GetPlatformStatusHandler(new FakeProbe(DatabaseState.Unavailable));
        var report = await handler.Handle(new GetPlatformStatusQuery(), TestContext.Current.CancellationToken);
        Assert.Equal(PlatformHealth.Degraded, report.Status);
        Assert.Equal(DatabaseState.Unavailable, report.Database);
    }

    [Fact]
    public async Task Capabilities_e_bootstrap_it_jane_eksplicite_f1_pending()
    {
        var handler = new GetPlatformStatusHandler(new FakeProbe(DatabaseState.Ready));
        var report = await handler.Handle(new GetPlatformStatusQuery(), TestContext.Current.CancellationToken);
        Assert.Equal(BootstrapCapabilities.F1Pending, report.Capabilities);
    }

    [Fact]
    public void Pergjigja_HTTP_perkthen_gjendjet_ne_stringje_stabile_pa_te_dhena_sensitive()
    {
        var ready = PlatformStatusResponse.From(PlatformStatusReport.From(DatabaseState.Ready));
        Assert.Equal("OK", ready.Status);
        Assert.Equal("platform-v1", ready.ApiVersion);
        Assert.Equal("READY", ready.Database);
        Assert.Equal("NOT_CONFIGURED_F1_PENDING", ready.Capabilities.Identity);
        Assert.Equal("NOT_CONFIGURED_F1_PENDING", ready.Capabilities.Audit);
        Assert.Equal("NOT_CONFIGURED_F1_PENDING", ready.Capabilities.Outbox);
        Assert.Equal("NOT_CONFIGURED_F1_PENDING", ready.Capabilities.Observability);

        var degraded = PlatformStatusResponse.From(PlatformStatusReport.From(DatabaseState.Unavailable));
        Assert.Equal("DEGRADED", degraded.Status);
        Assert.Equal("UNAVAILABLE", degraded.Database);
    }
}
