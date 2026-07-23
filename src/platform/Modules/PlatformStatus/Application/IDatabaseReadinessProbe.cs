using Idata.Platform.Modules.PlatformStatus.Domain;

namespace Idata.Platform.Modules.PlatformStatus.Application;

/// <summary>
/// Port i Application-it për gatishmërinë e target database-it. Implementimi
/// jeton në Infrastructure; kontrata është fail-safe — kthen gjendje,
/// nuk hedh exception drejt handler-it.
/// </summary>
public interface IDatabaseReadinessProbe
{
    Task<DatabaseState> CheckAsync(CancellationToken cancellationToken);
}
