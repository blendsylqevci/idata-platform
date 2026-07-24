using Idata.Platform.Modules.PlatformStatus.Application;
using Idata.Platform.Modules.PlatformStatus.Domain;
using Idata.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Idata.Platform.Modules.PlatformStatus.Infrastructure;

/// <summary>
/// Probe gatishmërie kundrejt target PostgreSQL-it — PA false-positive:
/// <see cref="DatabaseState.Ready"/> kthehet VETËM kur (1) serveri është i
/// arritshëm, (2) i GJITHË seti i migrimeve të assembly-t është i aplikuar
/// (⇒ pending = 0 dhe historia jo bosh) dhe (3) tabela sentinel
/// `platform.migration_smoke_check` ekziston. DB e arritshme por e
/// pamigruar → Unavailable/DEGRADED. Probe-i është READ-ONLY (asnjë
/// auto-migrim) dhe FAIL-SAFE: çdo dështim → Unavailable, kurrë exception
/// dhe kurrë detaje lidhjeje në output.
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

            await using var context = PlatformDbContextFactory.Create(connectionString);

            var assemblyIds = context.Database.GetMigrations().ToArray();
            if (assemblyIds.Length == 0)
            {
                return DatabaseState.Unavailable; // pa katalog migrimesh s'ka gatishmëri të provueshme
            }

            var applied = (await context.Database.GetAppliedMigrationsAsync(timeout.Token).ConfigureAwait(false))
                .ToHashSet(StringComparer.Ordinal);
            if (!assemblyIds.All(applied.Contains))
            {
                return DatabaseState.Unavailable; // histori bosh ose migrime pending
            }

            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync(timeout.Token).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT to_regclass('platform.migration_smoke_check') IS NOT NULL";
            var sentinelPresent = await command.ExecuteScalarAsync(timeout.Token).ConfigureAwait(false) is true;
            return sentinelPresent ? DatabaseState.Ready : DatabaseState.Unavailable;
        }
        catch (Exception)
        {
            // Gjendje e kontrolluar — statusi raporton Unavailable/degraded.
            return DatabaseState.Unavailable;
        }
    }
}
