using Idata.Platform.Modules.PlatformStatus.Domain;
using Mediator;

namespace Idata.Platform.Modules.PlatformStatus.Application.Features.GetPlatformStatus;

/// <summary>
/// Handler-i i query-t — i zgjidhur përmes mediator-it me lifetime Scoped
/// (ADR-029). Përdor portin ekzistues <see cref="IDatabaseReadinessProbe"/>;
/// semantika e readiness probe mbetet identike me Bootstrap-in.
/// </summary>
public sealed class GetPlatformStatusHandler(IDatabaseReadinessProbe databaseProbe)
    : IQueryHandler<GetPlatformStatusQuery, PlatformStatusReport>
{
    public async ValueTask<PlatformStatusReport> Handle(
        GetPlatformStatusQuery query, CancellationToken cancellationToken)
    {
        var database = await databaseProbe.CheckAsync(cancellationToken).ConfigureAwait(false);
        return PlatformStatusReport.From(database);
    }
}
