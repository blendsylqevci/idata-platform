using Npgsql;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Barrierë DB-OBSERVABLE për testet e konkurrencës: pret derisa PostgreSQL
/// vetë raportojë se një backend konkret po BLLOKOHET nga një lock
/// (`pg_blocking_pids(pid)` jo-bosh). ZERO `Task.Delay` si "provë" bllokimi —
/// gjendja lexohet nga katalogët e vërtetë të serverit, me deadline të
/// kufizuar që testi të DËSHTOJË në vend që të varet.
/// </summary>
public static class PostgresLockObserver
{
    /// <summary>Deadline i kufizuar — dështim i qartë në vend të varjes.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    public static async Task<int> BackendPidAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return (int)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Pret derisa `pg_blocking_pids(@pid)` të jetë JO-bosh, pra derisa
    /// serveri të konfirmojë se ky backend po pret një lock. Çdo iteracion
    /// është një round-trip real te DB (jo sleep); deadline-i e kufizon.
    /// </summary>
    public static async Task WaitUntilBlockedAsync(
        NpgsqlConnection observer, int blockedPid, CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < Deadline)
        {
            await using var command = observer.CreateCommand();
            command.CommandText = "SELECT cardinality(pg_blocking_pids(@pid))";
            command.Parameters.AddWithValue("pid", blockedPid);
            var blockers = (int)(await command.ExecuteScalarAsync(cancellationToken))!;
            if (blockers > 0)
            {
                return;
            }
        }

        Assert.Fail($"Backend-i nuk u bllokua brenda {Deadline.TotalSeconds}s — barriera s'u vendos.");
    }

    /// <summary>Pret derisa backend-i të MOS jetë më i bllokuar (lock-u u lirua).</summary>
    public static async Task WaitUntilUnblockedAsync(
        NpgsqlConnection observer, int blockedPid, CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < Deadline)
        {
            await using var command = observer.CreateCommand();
            command.CommandText = "SELECT cardinality(pg_blocking_pids(@pid))";
            command.Parameters.AddWithValue("pid", blockedPid);
            if ((int)(await command.ExecuteScalarAsync(cancellationToken))! == 0)
            {
                return;
            }
        }

        Assert.Fail($"Backend-i mbeti i bllokuar mbi {Deadline.TotalSeconds}s.");
    }
}
