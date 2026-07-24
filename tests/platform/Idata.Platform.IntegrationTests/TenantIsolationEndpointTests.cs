using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Idata.Platform.Modules.AccessControl.Presentation;
using Idata.Platform.Persistence;
using Npgsql;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Izolimi i tenant-ëve END-TO-END (§10.D) me seed shumë-tenant sintetik:
/// leximi i kontekstit vetjak; deny-by-default; 404 CROSS-TENANT = 404
/// INEKZISTENT në fushat publike STABILE (anti existence-oracle); request-i
/// s'mund të override-ojë tenant-in e aktorit; composite FK-të cross-tenant
/// dështojnë NË VETË DB-në (ENG-003).
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class TenantIsolationEndpointTests
{
    private static async Task WithHostAsync(Func<HttpClient, string, Task> body, [CallerMemberName] string label = "")
    {
        await using var db = await PlatformTestDatabase.CreateAsync(label);
        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db.DatabaseName);
        try
        {
            await using var factory = await AccessControlSeed.MigrateSeedAndCreateFactoryAsync(db.DatabaseName);
            using var client = factory.CreateClient();
            await body(client, db.DatabaseName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    internal static HttpRequestMessage ContextRequest(Guid? userId, Guid? businessId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/platform/access-context", UriKind.Relative));
        AddActor(request, userId, businessId);
        return request;
    }

    internal static HttpRequestMessage RoleUpdateRequest(
        Guid actorUser, Guid actorBusiness, Guid membershipId, Guid roleId, string idempotencyKey, long expectedVersion = 0)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put, new Uri($"/api/platform/memberships/{membershipId:D}/role", UriKind.Relative))
        {
            Content = JsonContent.Create(new { roleId, expectedVersion }),
        };
        AddActor(request, actorUser, actorBusiness);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    internal static void AddActor(HttpRequestMessage request, Guid? userId, Guid? businessId)
    {
        if (userId is { } user)
        {
            request.Headers.Add("X-Test-UserId", user.ToString("D"));
        }

        if (businessId is { } business)
        {
            request.Headers.Add("X-Test-BusinessId", business.ToString("D"));
        }
    }

    /// <summary>Fushat publike STABILE të ProblemDetails (pa trace id).</summary>
    internal static async Task<(string? Type, string? Title, int Status, string? ErrorCode)> StableProblemAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        return (
            root.TryGetProperty("type", out var type) ? type.GetString() : null,
            root.TryGetProperty("title", out var title) ? title.GetString() : null,
            root.TryGetProperty("status", out var status) ? status.GetInt32() : 0,
            root.TryGetProperty("errorCode", out var code) ? code.GetString() : null);
    }

    internal static string ConnectionStringFor(string databaseName)
        => PlatformDatabaseConnection.Parse(
            Environment.GetEnvironmentVariable("PGHOST") ?? "localhost",
            Environment.GetEnvironmentVariable("PGPORT") ?? "5432",
            Environment.GetEnvironmentVariable("PGUSER") ?? "idata",
            databaseName).ToConnectionString();

    internal static async Task<Guid> MembershipRoleAsync(string databaseName, Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor(databaseName));
        await connection.OpenAsync();
        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT role_id FROM platform.business_membership WHERE id = '{membershipId:D}'";
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Aktori_lexon_kontekstin_e_vet_dhe_vetem_ate()
        => await WithHostAsync(async (client, _) =>
        {
            using var response = await client.SendAsync(
                ContextRequest(AccessControlSeed.UserA, AccessControlSeed.BusinessA),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<AccessContextResponse>(
                TestContext.Current.CancellationToken);
            Assert.NotNull(body);
            Assert.Equal(AccessControlSeed.UserA, body.UserId);
            Assert.Equal(AccessControlSeed.BusinessA, body.BusinessId);
            Assert.Equal(["platform.users_roles:R", "platform.users_roles:U"], body.Permissions);
            Assert.Equal("PROVIDER_NEUTRAL_ADR028_OPEN", body.IdentityStatus);
        });

    [Fact]
    public async Task Cross_tenant_404_eshte_i_padallueshem_nga_inekzistent_404()
        => await WithHostAsync(async (client, databaseName) =>
        {
            using var crossTenant = await client.SendAsync(
                RoleUpdateRequest(AccessControlSeed.UserA, AccessControlSeed.BusinessA,
                    AccessControlSeed.MembershipB, AccessControlSeed.RoleA, "key-cross-tenant"),
                TestContext.Current.CancellationToken);
            using var nonexistent = await client.SendAsync(
                RoleUpdateRequest(AccessControlSeed.UserA, AccessControlSeed.BusinessA,
                    AccessControlSeed.NonexistentId, AccessControlSeed.RoleA, "key-nonexistent"),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, nonexistent.StatusCode);
            Assert.Equal(await StableProblemAsync(crossTenant), await StableProblemAsync(nonexistent));
            Assert.Equal(AccessControlSeed.RoleB, await MembershipRoleAsync(databaseName, AccessControlSeed.MembershipB));
        });

    [Fact]
    public async Task Request_i_nuk_zgjedh_dot_business_id_dhe_rol_cross_tenant_nuk_perdoret()
        => await WithHostAsync(async (client, databaseName) =>
        {
            // Rol i tenant-it B mbi membership të A-së → 404 (lookup i skopuar në A).
            using var response = await client.SendAsync(
                RoleUpdateRequest(AccessControlSeed.UserA, AccessControlSeed.BusinessA,
                    AccessControlSeed.MembershipC, AccessControlSeed.RoleB, "key-foreign-role"),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(AccessControlSeed.RoleA, await MembershipRoleAsync(databaseName, AccessControlSeed.MembershipC));
        });

    [Fact]
    public async Task Composite_FK_refuzon_lidhjet_cross_tenant_ne_vete_DB_ne()
        => await WithHostAsync(async (_, databaseName) =>
        {
            await using var connection = new NpgsqlConnection(ConnectionStringFor(databaseName));
            await connection.OpenAsync();
            await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, databaseName);

            var linkViolation = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
                $"INSERT INTO platform.role_permission (business_id, role_id, permission_id) VALUES ('{AccessControlSeed.BusinessA:D}', '{AccessControlSeed.RoleB:D}', '{AccessControlSeed.PermissionRead:D}')"));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, linkViolation.SqlState);

            var membershipViolation = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
                $"INSERT INTO platform.business_membership (id, business_id, user_id, role_id, version, status, created_at) VALUES ('{AccessControlSeed.NonexistentId:D}', '{AccessControlSeed.BusinessA:D}', '{AccessControlSeed.UserInDisabledBiz:D}', '{AccessControlSeed.RoleB:D}', 0, 'active', now())"));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, membershipViolation.SqlState);

            var updateViolation = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
                $"UPDATE platform.business_membership SET business_id = '{AccessControlSeed.BusinessB:D}' WHERE id = '{AccessControlSeed.MembershipA:D}'"));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, updateViolation.SqlState);
        });

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
