using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Idata.Platform.Modules.AccessControl.Application;
using Idata.Platform.Modules.AccessControl.Domain;
using Idata.Platform.Modules.AccessControl.Infrastructure;
using Idata.Platform.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Kontrata e idempotencës nën konkurrencë REALE same-key, provuar në HTTP
/// kundër PostgreSQL real.
///
/// <para>Rruga nën provë: dy kërkesa me të njëjtin Idempotency-Key e kalojnë
/// TË DYJA lookup-in fillestar të receipt-it para commit-it të fituesit
/// (e GARANTUAR strukturalisht nga një barrierë count-down PAS lookup-it —
/// asnjëra s'mund të hyjë në fazën e mutacionit para se tjetra të ketë
/// përfunduar lookup-in; zero <c>Task.Delay</c>). Humbësi merr
/// VersionConflict në fazën e mutacionit dhe DUHET të bëjë NJË rilexim
/// deterministik të receipt-it pas rollback-ut: fingerprint identik →
/// rezultati i ruajtur i fituesit; fingerprint tjetër → 409 idempotency;
/// receipt që mungon realisht → 409 CAS i vërtetë.</para>
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class ConcurrentIdempotencyTests
{
    /// <summary>
    /// Count-down latch: çdo pjesëmarrës sinjalizon PAS lookup-it fillestar
    /// dhe pret derisa TË GJITHË ta kenë kaluar — kështu provohet strukturalisht
    /// overlap-i real (të dy lexuan "receipt mungon" para çdo commit-i).
    /// Deadline i kufizuar → dështim i qartë, jo varje.
    /// </summary>
    private sealed class ReceiptLookupBarrier(int participants)
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrived;

        public int Arrived => arrived;

        public Task SignalAndWaitAsync()
        {
            if (Interlocked.Increment(ref arrived) >= participants)
            {
                release.TrySetResult();
            }

            return release.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    /// <summary>
    /// Dekorator TEST-ONLY (i regjistruar vetëm në factory-n e testit) që ndal
    /// kërkesën te barriera MENJËHERË pas lookup-it FILLESTAR të receipt-it.
    /// Rileximi i recovery-t (thirrja e dytë në të njëjtin scope) NUK pret —
    /// pra rruga e recovery-t mbetet e pandryshuar.
    /// </summary>
    private sealed class ReceiptBarrierUow(IMembershipRoleUnitOfWork inner, ReceiptLookupBarrier barrier)
        : IMembershipRoleUnitOfWork
    {
        private bool initialLookupDone;

        public async Task<CommandReceipt?> FindReceiptAsync(
            Guid businessId, string operationKey, string idempotencyKey, CancellationToken cancellationToken)
        {
            var receipt = await inner.FindReceiptAsync(businessId, operationKey, idempotencyKey, cancellationToken);
            if (!initialLookupDone)
            {
                initialLookupDone = true;
                await barrier.SignalAndWaitAsync();
            }

            return receipt;
        }

        public Task<AtomicRoleMutationResult> TryMutateRoleAsync(
            AtomicRoleMutation mutation, CancellationToken cancellationToken)
            => inner.TryMutateRoleAsync(mutation, cancellationToken);

        public void AddAudit(AuditLogEntry entry) => inner.AddAudit(entry);

        public void AddReceipt(CommandReceipt receipt) => inner.AddReceipt(receipt);

        public Task<CommandSaveOutcome> SaveAsync(CancellationToken cancellationToken)
            => inner.SaveAsync(cancellationToken);

        public Task DiscardAsync(CancellationToken cancellationToken) => inner.DiscardAsync(cancellationToken);
    }

    private sealed class ReceiptBarrierFactory(ReceiptLookupBarrier barrier) : AccessControlApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IMembershipRoleUnitOfWork>(sp =>
                    new ReceiptBarrierUow(
                        ActivatorUtilities.CreateInstance<EfMembershipRoleUnitOfWork>(sp), barrier));
            });
        }
    }

    private static async Task WithSeedAsync(Func<string, Task> body, [CallerMemberName] string label = "")
    {
        await using var db = await PlatformTestDatabase.CreateAsync(label);
        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db.DatabaseName);
        try
        {
            (await AccessControlSeed.MigrateSeedAndCreateFactoryAsync(db.DatabaseName)).Dispose();
            await body(db.DatabaseName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    private static HttpRequestMessage RoleUpdate(Guid roleId, string key, long expectedVersion = 0)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            new Uri($"/api/platform/memberships/{AccessControlSeed.MembershipC:D}/role", UriKind.Relative))
        {
            Content = JsonContent.Create(new { roleId, expectedVersion }),
        };
        request.Headers.Add("X-Test-UserId", AccessControlSeed.UserA.ToString("D"));
        request.Headers.Add("X-Test-BusinessId", AccessControlSeed.BusinessA.ToString("D"));
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static async Task<(Guid Role, long Version, long Audits, long Receipts)> StateAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(
            TenantIsolationEndpointTests.ConnectionStringFor(databaseName));
        await connection.OpenAsync();
        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT (SELECT role_id FROM platform.business_membership WHERE id = '{AccessControlSeed.MembershipC:D}'),
                   (SELECT version FROM platform.business_membership WHERE id = '{AccessControlSeed.MembershipC:D}'),
                   (SELECT COUNT(*) FROM platform.audit_log),
                   (SELECT COUNT(*) FROM platform.command_receipt)
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetGuid(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("errorCode").GetString()!;
    }

    // ══════ A. SAME KEY + SAME FINGERPRINT → 200 + 200 IDENTIKE ══════

    [Fact]
    public async Task Same_key_same_fingerprint_te_dyja_200_me_pergjigje_identike_dhe_nje_efekt()
        => await WithSeedAsync(async databaseName =>
        {
            var barrier = new ReceiptLookupBarrier(participants: 2);
            using var factory = new ReceiptBarrierFactory(barrier);
            using var client = factory.CreateClient();

            // Dy kërkesa IDENTIKE (i njëjti key + fingerprint), overlap real.
            var first = client.SendAsync(
                RoleUpdate(AccessControlSeed.RoleA2, "conc-same"), TestContext.Current.CancellationToken);
            var second = client.SendAsync(
                RoleUpdate(AccessControlSeed.RoleA2, "conc-same"), TestContext.Current.CancellationToken);
            var responses = await Task.WhenAll(first, second);

            // Barriera u kalua nga TË DYJA → të dyja lexuan "receipt mungon"
            // para commit-it të fituesit (overlap i provuar strukturalisht).
            Assert.Equal(2, barrier.Arrived);

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
            Assert.Equal(bodies[0], bodies[1]); // përgjigje publike IDENTIKE

            // Saktësisht NJË efekt: një mutation, version=1, 1 audit, 1 receipt.
            var state = await StateAsync(databaseName);
            Assert.Equal(AccessControlSeed.RoleA2, state.Role);
            Assert.Equal(1L, state.Version);
            Assert.Equal(1L, state.Audits);
            Assert.Equal(1L, state.Receipts);
        });

    // ══════ B. SAME KEY + FINGERPRINT TJETËR → 200 + 409 IDEMPOTENCY ══════

    [Fact]
    public async Task Same_key_fingerprint_tjeter_nje_200_nje_409_idempotency_kurre_CAS()
        => await WithSeedAsync(async databaseName =>
        {
            var barrier = new ReceiptLookupBarrier(participants: 2);
            using var factory = new ReceiptBarrierFactory(barrier);
            using var client = factory.CreateClient();

            // I njëjti key; payload/fingerprint të NDRYSHËM (role tjetër).
            var first = client.SendAsync(
                RoleUpdate(AccessControlSeed.RoleA2, "conc-diff"), TestContext.Current.CancellationToken);
            var second = client.SendAsync(
                RoleUpdate(AccessControlSeed.RoleA, "conc-diff"), TestContext.Current.CancellationToken);
            var responses = await Task.WhenAll(first, second);
            Assert.Equal(2, barrier.Arrived);

            var winner = responses.Single(r => r.StatusCode == HttpStatusCode.OK);
            var loser = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);

            // Humbësi është konflikt IDEMPOTENCE — KURRË CAS/version conflict.
            Assert.Equal("IDEMPOTENCY_CONFLICT", await ErrorCodeAsync(loser));

            // Saktësisht një efekt, i fituesit real.
            var winnerBody = JsonDocument.Parse(
                await winner.Content.ReadAsStringAsync()).RootElement;
            var state = await StateAsync(databaseName);
            Assert.Equal(winnerBody.GetProperty("roleId").GetGuid(), state.Role);
            Assert.Equal(1L, state.Version);
            Assert.Equal(1L, state.Audits);
            Assert.Equal(1L, state.Receipts);
        });

    // ══════ C. CAS I VËRTETË (zero receipt për key-n) → 409 CONCURRENCY ══════

    [Fact]
    public async Task Version_stale_pa_receipt_eshte_CAS_i_vertete_409_pa_efekt()
        => await WithSeedAsync(async databaseName =>
        {
            using var factory = new AccessControlApiFactory();
            using var client = factory.CreateClient();

            // Version-i lëviz 0→1 me një komandë të parë (key tjetër).
            var setup = await client.SendAsync(
                RoleUpdate(AccessControlSeed.RoleA2, "cas-setup"), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

            // Kërkesë e re me KEY TË RI dhe expectedVersion=0 STALE:
            // VersionConflict → rilexim → zero receipt për këtë key → CAS real.
            var stale = await client.SendAsync(
                RoleUpdate(AccessControlSeed.RoleA, "cas-stale", expectedVersion: 0),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("CONCURRENCY_CONFLICT", await ErrorCodeAsync(stale));

            // Zero efekt shtesë: vetëm efekti i setup-it ekziston.
            var state = await StateAsync(databaseName);
            Assert.Equal(AccessControlSeed.RoleA2, state.Role);
            Assert.Equal(1L, state.Version);
            Assert.Equal(1L, state.Audits);
            Assert.Equal(1L, state.Receipts);
        });
}
