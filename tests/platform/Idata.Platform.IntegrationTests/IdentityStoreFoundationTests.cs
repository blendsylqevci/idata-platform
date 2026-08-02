using Idata.Platform.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using System.Security.Claims;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Store foundation-i (ADR-028, B.1) kundrejt PostgreSQL REAL sintetik:
/// forma e schema-s `identity` (tabelat/indekset/FK-të reale; zero AspNet*;
/// zero tabela rolesh; ZERO FK cross-schema) + round-trips sintetike përmes
/// DI-së REALE të host-it (UserManager/UserOnlyStore roleless; menaxherët
/// OpenIddict Core me Guid) — PA asnjë endpoint/protokoll. Vetëm identitete
/// sintetike; zero kredenciale reale; zero raw token material.
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class IdentityStoreFoundationTests
{
    private static async Task MigrateAsync(string databaseName)
    {
        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", databaseName);
        try
        {
            Assert.Equal(0, await Idata.Platform.DbMigrator.MigratorApp.RunAsync(["migrate"]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    private static async Task<NpgsqlConnection> OpenAsync(string databaseName)
    {
        var settings = PlatformDatabaseConnection.Parse(
            Environment.GetEnvironmentVariable("PGHOST") ?? "localhost",
            Environment.GetEnvironmentVariable("PGPORT") ?? "5432",
            Environment.GetEnvironmentVariable("PGUSER") ?? "idata",
            databaseName);
        var connection = new NpgsqlConnection(settings.ToConnectionString());
        await connection.OpenAsync();
        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, databaseName);
        return connection;
    }

    private static async Task<T?> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return result is DBNull or null ? default : (T)result;
    }

    private static async Task<string[]> ColumnAsync(NpgsqlConnection connection, string sql)
    {
        var values = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return [.. values];
    }

    [Fact]
    public async Task Schema_identity_reale_ka_ekzakt_8_tabelat_indekset_dhe_FK_te_brendshme()
    {
        await using var db = await PlatformTestDatabase.CreateAsync("identityschema");
        await MigrateAsync(db.DatabaseName);
        await using var connection = await OpenAsync(db.DatabaseName);

        // Tabelat EKZAKTE të schema-s identity (§7 i mandatit / §3 i Identity doc).
        Assert.Equal(
            ["oidc_application", "oidc_authorization", "oidc_scope", "oidc_token",
             "user_account", "user_claim", "user_login", "user_token"],
            await ColumnAsync(connection,
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'identity' ORDER BY table_name"));

        // ZERO emra default AspNet* dhe ZERO tabela rolesh — në KREJT DB-në.
        Assert.Equal(0L, await ScalarAsync<long>(connection,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_name ILIKE 'AspNet%'"));
        Assert.Equal(0L, await ScalarAsync<long>(connection,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'identity' AND table_name ILIKE '%role%'"));

        // Schema platform E PANDRYSHUAR: e njëjta listë si para B.1 + history.
        Assert.Equal(
            ["__ef_migrations_history", "app_user", "audit_log", "business", "business_membership",
             "command_receipt", "migration_smoke_check", "permission", "role", "role_permission"],
            await ColumnAsync(connection,
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'platform' ORDER BY table_name"));

        // Indekset unike reale të store-it.
        foreach (var (index, table) in (ValueTuple<string, string>[])
                 [("IX_user_account_normalized_user_name", "user_account"),
                  ("IX_oidc_application_client_id", "oidc_application"),
                  ("IX_oidc_scope_name", "oidc_scope"),
                  ("IX_oidc_token_reference_id", "oidc_token")])
        {
            Assert.True(await ScalarAsync<bool>(connection,
                $"SELECT COUNT(*) = 1 FROM pg_indexes WHERE schemaname='identity' AND tablename='{table}' AND indexname='{index}' AND indexdef LIKE 'CREATE UNIQUE INDEX%'"));
        }

        // FK-të reale BRENDA identity: 3 drejt user_account + 3 të oidc-ve.
        Assert.Equal(6L, await ScalarAsync<long>(connection,
            "SELECT COUNT(*) FROM information_schema.table_constraints WHERE table_schema='identity' AND constraint_type='FOREIGN KEY'"));

        // ZERO FK cross-schema (identity↔platform) — provë në pg_constraint.
        Assert.Equal(0L, await ScalarAsync<long>(connection,
            @"SELECT COUNT(*) FROM pg_constraint con
              JOIN pg_class child ON child.oid = con.conrelid
              JOIN pg_namespace child_ns ON child_ns.oid = child.relnamespace
              JOIN pg_class parent ON parent.oid = con.confrelid
              JOIN pg_namespace parent_ns ON parent_ns.oid = parent.relnamespace
              WHERE con.contype = 'f' AND child_ns.nspname <> parent_ns.nspname"));

        // ZERO persisted int IDs (ABSOLUT): çdo kolonë `id` = uuid — përfshirë
        // user_claim — dhe asnjë kolonë identifikuese (id / *_id) me tip int.
        Assert.Equal("uuid", await ScalarAsync<string>(connection,
            "SELECT data_type FROM information_schema.columns WHERE table_schema='identity' AND table_name='user_account' AND column_name='id'"));
        Assert.Equal("uuid", await ScalarAsync<string>(connection,
            "SELECT data_type FROM information_schema.columns WHERE table_schema='identity' AND table_name='user_claim' AND column_name='id'"));
        Assert.Equal(0L, await ScalarAsync<long>(connection,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='identity' AND column_name='id' AND data_type <> 'uuid'"));
        Assert.Equal(0L, await ScalarAsync<long>(connection,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='identity' AND (column_name = 'id' OR column_name LIKE '%\\_id') AND data_type IN ('integer','bigint','smallint')"));
    }

    [Fact]
    public async Task UserManager_roleless_kryen_round_trip_sintetik_me_uniqueness_reale()
    {
        await using var db = await PlatformTestDatabase.CreateAsync("identityusers");
        await MigrateAsync(db.DatabaseName);

        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db.DatabaseName);
        try
        {
            await using var factory = new WebApplicationFactory<Program>();
            _ = factory.CreateClient();
            using var scope = factory.Services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccountRecord>>();

            // Krijim sintetik (pa kredenciale) → Guid i gjeneruar.
            var created = await manager.CreateAsync(new UserAccountRecord { UserName = "perdorues_sintetik_b1" });
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Code)));

            var loaded = await manager.FindByNameAsync("perdorues_sintetik_b1");
            Assert.NotNull(loaded);
            Assert.NotEqual(Guid.Empty, loaded.Id);

            // Uniqueness reale mbi normalized_user_name (validator + indeks unik DB).
            var duplicate = await manager.CreateAsync(new UserAccountRecord { UserName = "perdorues_sintetik_b1" });
            Assert.False(duplicate.Succeeded);
            Assert.Contains(duplicate.Errors, error => error.Code == "DuplicateUserName");

            // user_claim round-trip sintetik.
            var claimAdd = await manager.AddClaimAsync(loaded, new Claim("etikete_sintetike", "vlere_sintetike"));
            Assert.True(claimAdd.Succeeded);
            var claims = await manager.GetClaimsAsync(loaded);
            Assert.Contains(claims, claim => claim.Type == "etikete_sintetike" && claim.Value == "vlere_sintetike");

            // user_token round-trip sintetik (artefakt account-level i Identity-t —
            // JO OAuth2/OIDC token; vlerë etikete sintetike, zero material real).
            var tokenSet = await manager.SetAuthenticationTokenAsync(
                loaded, "furnizues_sintetik", "emer_sintetik", "etikete_sintetike");
            Assert.True(tokenSet.Succeeded);
            Assert.Equal("etikete_sintetike",
                await manager.GetAuthenticationTokenAsync(loaded, "furnizues_sintetik", "emer_sintetik"));

            // Store-i REAL roleless: UserOnlyStore (jo UserStore me role).
            var store = scope.ServiceProvider.GetRequiredService<IUserStore<UserAccountRecord>>();
            Assert.StartsWith(
                "Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserOnlyStore`",
                store.GetType().GetGenericTypeDefinition().FullName ?? string.Empty,
                StringComparison.Ordinal);

            // Rreshtat përfunduan REALISHT në identity.* (jo në platform.*).
            await using var connection = await OpenAsync(db.DatabaseName);
            Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM identity.user_account"));
            Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM identity.user_claim"));
            Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM identity.user_token"));
            Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM platform.app_user"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    /// <summary>
    /// §7.2 i correction R2: PostgreSQL REAL refuzon zero-UUID në
    /// `identity.user_claim.id` (CHECK constraint) — INSERT raw me UUID bosh
    /// dështon me check_violation dhe ZERO row mbetet; UUID normal persiston.
    /// </summary>
    [Fact]
    public async Task PostgreSQL_refuzon_zero_UUID_ne_user_claim_id_dhe_zero_row_mbetet()
    {
        await using var db = await PlatformTestDatabase.CreateAsync("identityclaimck");
        await MigrateAsync(db.DatabaseName);
        await using var connection = await OpenAsync(db.DatabaseName);

        // Prind sintetik për FK-në.
        var userId = Guid.NewGuid();
        await ScalarAsync<object>(connection,
            $"INSERT INTO identity.user_account (id, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count) VALUES ('{userId}', false, false, false, false, 0) RETURNING id");

        // Zero-UUID (i ndërtuar pa literal shifrash — anti false-positive):
        // repeat('0', 32)::uuid brenda vetë INSERT-it.
        var zeroInsert =
            $"INSERT INTO identity.user_claim (id, user_id, claim_type, claim_value) VALUES (CAST(repeat('0', 32) AS uuid), '{userId}', 'tip_sintetik', 'vlere_sintetike')";
        var failure = await Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = zeroInsert;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        });
        Assert.Equal("23514", failure.SqlState); // check_violation
        Assert.Contains("ck_user_claim_id_non_empty", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM identity.user_claim"));

        // UUID normal persiston.
        await ScalarAsync<object>(connection,
            $"INSERT INTO identity.user_claim (id, user_id, claim_type, claim_value) VALUES ('{Guid.NewGuid()}', '{userId}', 'tip_sintetik', 'vlere_sintetike') RETURNING id");
        Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM identity.user_claim"));
    }

    /// <summary>
    /// §4.4 i correction pass-it: cikli i PLOTË i claims përmes
    /// UserManager/UserOnlyStore REAL (me UserClaimRecord UUID) kundrejt
    /// PostgreSQL — Add ×2 · Get · Replace · Remove · GetUsersForClaim;
    /// ClaimId non-empty + unikë të provuar në DB; fresh DI scope/fresh
    /// DbContext/re-resolution e store-it midis fazave.
    /// </summary>
    [Fact]
    public async Task User_claims_UUID_round_trip_i_plote_permes_UserOnlyStore_real()
    {
        await using var db = await PlatformTestDatabase.CreateAsync("identityclaims");
        await MigrateAsync(db.DatabaseName);

        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db.DatabaseName);
        try
        {
            await using var factory = new WebApplicationFactory<Program>();
            _ = factory.CreateClient();

            // Faza 1 (scope i parë): krijim user + DY claims.
            using (var scope = factory.Services.CreateScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccountRecord>>();
                var created = await manager.CreateAsync(new UserAccountRecord { UserName = "perdorues_claims_b1" });
                Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Code)));
                var user = (await manager.FindByNameAsync("perdorues_claims_b1"))!;
                Assert.True((await manager.AddClaimAsync(user, new Claim("tip_sintetik_1", "vlere_1"))).Succeeded);
                Assert.True((await manager.AddClaimAsync(user, new Claim("tip_sintetik_2", "vlere_2"))).Succeeded);
            }

            // ClaimId REAL në DB: 2 rreshta, `id` uuid NON-EMPTY dhe UNIKË.
            await using (var connection = await OpenAsync(db.DatabaseName))
            {
                var ids = await ColumnAsync(connection, "SELECT id::text FROM identity.user_claim ORDER BY id");
                Assert.Equal(2, ids.Length);
                var parsed = ids.Select(Guid.Parse).ToArray();
                Assert.All(parsed, id => Assert.NotEqual(Guid.Empty, id));
                Assert.Equal(2, parsed.Distinct().Count());
            }

            // Faza 2 (scope/DbContext/store I FRESKËT — re-resolution): Get + Replace + GetUsersForClaim.
            using (var scope = factory.Services.CreateScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccountRecord>>();
                var user = (await manager.FindByNameAsync("perdorues_claims_b1"))!;
                var claims = await manager.GetClaimsAsync(user);
                Assert.Equal(2, claims.Count);

                Assert.True((await manager.ReplaceClaimAsync(
                    user, new Claim("tip_sintetik_1", "vlere_1"), new Claim("tip_sintetik_1", "vlere_1_zv"))).Succeeded);

                var holders = await manager.GetUsersForClaimAsync(new Claim("tip_sintetik_1", "vlere_1_zv"));
                Assert.Contains(holders, holder => holder.UserName == "perdorues_claims_b1");
            }

            // Faza 3 (scope i tretë i freskët): Remove + gjendja finale në DB.
            using (var scope = factory.Services.CreateScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<UserAccountRecord>>();
                var user = (await manager.FindByNameAsync("perdorues_claims_b1"))!;
                Assert.True((await manager.RemoveClaimAsync(user, new Claim("tip_sintetik_2", "vlere_2"))).Succeeded);
                Assert.Single(await manager.GetClaimsAsync(user));
            }

            await using (var connection = await OpenAsync(db.DatabaseName))
            {
                Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM identity.user_claim"));
                Assert.Equal(1L, await ScalarAsync<long>(connection,
                    "SELECT COUNT(*) FROM identity.user_claim WHERE claim_type = 'tip_sintetik_1' AND claim_value = 'vlere_1_zv'"));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    [Fact]
    public async Task OpenIddict_Core_stores_kryejne_round_trip_sintetik_me_Guid()
    {
        await using var db = await PlatformTestDatabase.CreateAsync("identityoidc");
        await MigrateAsync(db.DatabaseName);

        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db.DatabaseName);
        try
        {
            await using var factory = new WebApplicationFactory<Program>();
            _ = factory.CreateClient();
            using var scope = factory.Services.CreateScope();

            // Aplikacion OIDC sintetik PUBLIC (zero sekret) → entitet me Guid.
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "klient-sintetik-b1",
                DisplayName = "Klient sintetik B.1",
                ClientType = OpenIddictConstants.ClientTypes.Public,
            }, TestContext.Current.CancellationToken);

            var application = await applications.FindByClientIdAsync(
                "klient-sintetik-b1", TestContext.Current.CancellationToken);
            var typedApplication = Assert.IsType<OpenIddictEntityFrameworkCoreApplication<Guid>>(application);
            Assert.NotEqual(Guid.Empty, typedApplication.Id);

            // Uniqueness reale e client_id (indeks unik) — krijimi i dytë dështon.
            await Assert.ThrowsAnyAsync<Exception>(() => applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "klient-sintetik-b1",
                ClientType = OpenIddictConstants.ClientTypes.Public,
            }, TestContext.Current.CancellationToken).AsTask());

            // Scope sintetik → entitet me Guid.
            var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
            await scopes.CreateAsync(
                new OpenIddictScopeDescriptor { Name = "scope.sintetik.b1" }, TestContext.Current.CancellationToken);
            var oidcScope = await scopes.FindByNameAsync("scope.sintetik.b1", TestContext.Current.CancellationToken);
            var typedScope = Assert.IsType<OpenIddictEntityFrameworkCoreScope<Guid>>(oidcScope);
            Assert.NotEqual(Guid.Empty, typedScope.Id);

            // Rreshtat REALISHT në identity.oidc_* — zero prekje platform.
            await using var connection = await OpenAsync(db.DatabaseName);
            Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM identity.oidc_application"));
            Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM identity.oidc_scope"));
            Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM identity.oidc_token"));
            Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM platform.app_user"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    [Fact]
    public async Task Route_table_nuk_fiton_asnje_endpoint_identity_oidc_nga_B1()
    {
        await using var factory = new WebApplicationFactory<Program>();
        _ = factory.CreateClient();
        var dataSource = factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>();
        var patterns = dataSource.Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Select(endpoint => (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .ToArray();

        Assert.DoesNotContain(patterns, pattern =>
            pattern.Contains("connect", StringComparison.OrdinalIgnoreCase)
            || pattern.Contains("identity", StringComparison.OrdinalIgnoreCase)
            || pattern.Contains("account", StringComparison.OrdinalIgnoreCase)
            || pattern.Contains("login", StringComparison.OrdinalIgnoreCase)
            || pattern.Contains("token", StringComparison.OrdinalIgnoreCase));
    }
}
