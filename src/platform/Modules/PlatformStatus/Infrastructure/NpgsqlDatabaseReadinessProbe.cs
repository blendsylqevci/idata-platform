using Idata.Platform.Modules.PlatformStatus.Application;
using Idata.Platform.Modules.PlatformStatus.Domain;
using Npgsql;

namespace Idata.Platform.Modules.PlatformStatus.Infrastructure;

/// <summary>
/// Probe gatishmërie kundrejt target PostgreSQL-it (database logjike e
/// VEÇANTË nga legacy). FAIL-SAFE: çdo dështim (lidhje, timeout, anulim)
/// kthehet si <see cref="DatabaseState.Unavailable"/> — kurrë exception
/// drejt handler-it dhe kurrë detaje lidhjeje në përgjigje.
/// </summary>
public sealed class NpgsqlDatabaseReadinessProbe(string connectionString) : IDatabaseReadinessProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public async Task<DatabaseState> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(timeout.Token).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            _ = await command.ExecuteScalarAsync(timeout.Token).ConfigureAwait(false);
            return DatabaseState.Ready;
        }
        catch (Exception)
        {
            // Gjendje e kontrolluar — statusi raporton Unavailable/degraded.
            return DatabaseState.Unavailable;
        }
    }
}
