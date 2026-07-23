using Idata.Platform.Modules.PlatformStatus.Domain;

namespace Idata.Platform.Modules.PlatformStatus.Application;

/// <summary>
/// Query/handler i thjeshtë i smoke slice-it (pa CQRS ceremonial — ADR-024
/// lejon CQRS vetëm ku justifikohet). Një pyetje: statusi i platformës.
/// </summary>
public sealed class GetPlatformStatusHandler(IDatabaseReadinessProbe databaseProbe)
{
    public async Task<PlatformStatusReport> HandleAsync(CancellationToken cancellationToken)
    {
        var database = await databaseProbe.CheckAsync(cancellationToken).ConfigureAwait(false);
        return PlatformStatusReport.From(database);
    }
}
