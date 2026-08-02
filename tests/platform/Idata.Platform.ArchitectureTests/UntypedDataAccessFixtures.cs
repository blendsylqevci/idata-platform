using System.Data.Common;
using Idata.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Fixtures SINTETIKE për ENG-007 (§7). Asnjëri prej tyre NUK ekzekutohet
/// kurrë: ekzistojnë vetëm që kompajleri të prodhojë IL/metadata REALE mbi të
/// cilat skaneri provohet. Zero kredencial, zero host, zero të dhënë reale —
/// vetëm emra tabelash sintetikë.
///
/// Fixture-t RED duhet të KAPEN nga skaneri me atribuim të saktë; fixture-t
/// GREEN duhet të MOS prodhojnë asnjë gjetje ENG-007.
/// </summary>
internal static class RawSinkFixtures
{
    // ═════════════ RED 1 — ADO.NET jo-owner + konstant `identity.` ═════════════

    internal const string IdentityTableConstant = "SELECT id FROM identity.user_account WHERE id = @id";

    internal static async Task<object?> AdoNetIdentityConstantAsync(DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = IdentityTableConstant;
        return await command.ExecuteScalarAsync();
    }

    // ═════════════ RED 2 — schema/tabelë e ndërtuar nga fragmente ═════════════

    internal static async Task<object?> FragmentBuiltSchemaAsync(DbConnection connection)
    {
        var fragments = new[] { "iden", "tity", ".", "user_", "account" };
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM " + string.Concat(fragments);
        return await command.ExecuteScalarAsync();
    }

    // ═══ RED 3 — mixed-case/quoted/interpolated/parameterized identity command ═══

    internal static async Task<int> MixedCaseQuotedParameterizedAsync(NpgsqlConnection connection, Guid userId)
    {
        var quoted = $"""SELECT count(*) FROM "IDENTITY"."User_Account" WHERE "Id" = @user_id""";
        await using var command = connection.CreateCommand();
        command.CommandText = quoted;
        command.Parameters.AddWithValue("user_id", userId);
        return await command.ExecuteNonQueryAsync();
    }

    // ═════════════ RED 4 — FromSqlRaw / FromSqlInterpolated ═════════════

    internal static IQueryable<BusinessRecord> FromSqlRawFixture(PlatformDbContext context)
        => context.Businesses.FromSqlRaw("SELECT * FROM identity.user_account");

    internal static IQueryable<BusinessRecord> FromSqlInterpolatedFixture(PlatformDbContext context, Guid id)
        => context.Businesses.FromSqlInterpolated($"SELECT * FROM identity.user_account WHERE id = {id}");

    // ═════════════ RED 5 — ExecuteSqlRaw / ExecuteSqlInterpolated ═════════════

    internal static int ExecuteSqlRawFixture(PlatformDbContext context)
        => context.Database.ExecuteSqlRaw("DELETE FROM identity.user_claim");

    internal static int ExecuteSqlInterpolatedFixture(PlatformDbContext context, Guid id)
        => context.Database.ExecuteSqlInterpolated($"DELETE FROM identity.user_claim WHERE id = {id}");

    // ═════════════ RED 6 — SqlQueryRaw / SqlQuery ═════════════

    internal static IQueryable<Guid> SqlQueryRawFixture(PlatformDbContext context)
        => context.Database.SqlQueryRaw<Guid>("SELECT id FROM identity.user_account");

    internal static IQueryable<Guid> SqlQueryFixture(PlatformDbContext context, Guid id)
        => context.Database.SqlQuery<Guid>($"SELECT id FROM identity.user_account WHERE id = {id}");

    // ═════ RED 7 — NpgsqlCommand / NpgsqlBatch / NpgsqlDataSource / import ═════

    internal static async Task<object?> NpgsqlCommandFixtureAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT 1 FROM identity.user_token", connection);
        return await command.ExecuteScalarAsync();
    }

    internal static async Task<int> NpgsqlBatchFixtureAsync(NpgsqlConnection connection)
    {
        await using var batch = new NpgsqlBatch(connection);
        batch.BatchCommands.Add(new NpgsqlBatchCommand("SELECT 1 FROM identity.user_login"));
        return await batch.ExecuteNonQueryAsync();
    }

    internal static async Task<object?> NpgsqlDataSourceFixtureAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("SELECT 1 FROM identity.oidc_token");
        return await command.ExecuteScalarAsync();
    }

    internal static object NpgsqlImportFixture(NpgsqlConnection connection)
        => connection.BeginBinaryImport("COPY identity.user_account FROM STDIN (FORMAT BINARY)");

    // ═════════════ RED 8 — indirektim përmes helper-i ═════════════

    internal static async Task<object?> HelperIndirectionEntryAsync(DbConnection connection)
        => await RawHelperAsync(connection, "SELECT 1 FROM identity.user_account");

    private static async Task<object?> RawHelperAsync(DbConnection connection, string statement)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = statement;
        return await command.ExecuteScalarAsync();
    }

    // ═════════════ RED 9 — alias delegate/ldftn ═════════════

    /// <summary>Method-group → `ldftn` mbi sink: atribuim i saktë te KJO metodë.</summary>
    internal static Func<DbCommand> DelegateAliasFixture(DbConnection connection)
        => connection.CreateCommand;

    /// <summary>
    /// Lambda statike → sink-u përfundon në një tip compiler-generated pa
    /// state machine: atribuimi NUK provohet dot ⇒ `unattributed-compiler-generated`
    /// (fail-closed), kurrë hamendje dhe kurrë PASS i heshtur.
    /// </summary>
    internal static Func<DbConnection, DbCommand> LambdaAliasFixture()
        => static connection => connection.CreateCommand();

    // ═════════════ RED 15 — familje/anëtar i PANJOHUR mbi tip raw ═════════════

    /// <summary>
    /// `GetSchema` NUK është në listën e emrave të ekzekutimit raw; kapet
    /// SEPSE tipi prind është tip me kapacitet raw (§4.2: i panjohur = gjetje).
    /// </summary>
    internal static object UnknownSinkMemberFixture(NpgsqlConnection connection)
        => connection.GetSchema();

    // ═════════════ GREEN 17 — EF LINQ i tipizuar, pa sink ═════════════

    internal static IQueryable<BusinessRecord> TypedLinqFixture(PlatformDbContext context)
        => context.Businesses.Where(business => business.Status == RecordStatus.Active);

    // ═════════════ GREEN 19 — store Identity i tipizuar ═════════════

    internal static object TypedIdentityStoreFixture(PlatformDbContext context)
        => context.Users.Where(user => user.Id != Guid.Empty);

    // ═════════════ GREEN 20 — string `identity.*` pa sink ekzekutues ═════════════

    internal static string IdentitySchemaLiteralFixture()
        => "identity.user_account · identity.user_claim · identity.oidc_token";

    // ═════════════ GREEN 21 — reflection dinamik = ENG-006 OPEN ═════════════

    /// <summary>
    /// Emri i tipit raw ndërtohet VËRTET në runtime nga fragmente: assembly-ja
    /// nuk përmban as TypeRef as MemberRef ndaj tij, prandaj skaneri statik
    /// ENG-007 NUK e sheh. Kjo NUK është provë "PASS" — është demonstrimi
    /// mekanik i kufirit **ENG-006 OPEN**.
    /// </summary>
    internal static object? DynamicRawAccessResidualPoC()
    {
        var typeParts = new[] { "System", ".", "Data", ".", "Common", ".", "Db", "Command" };
        var assemblyParts = new[] { ", ", "System", ".", "Data", ".", "Common" };
        return Type.GetType(string.Concat(typeParts) + string.Concat(assemblyParts));
    }
}

/// <summary>
/// RED 14 — escape hatch PUBLIK i ekzekutimit raw (§6). Ekziston vetëm si
/// objektiv i guard-it të sipërfaqes publike; asnjë production owner nuk e ka.
/// </summary>
public static class OwnerPublicEscapeHatchFixtures
{
    /// <summary>Pranon DHE dorëzon kapacitet raw.</summary>
    public static DbCommand OpenCommand(DbConnection connection) => connection.CreateCommand();

    /// <summary>Pranon SQL string të papërpunuar (emër parametri nga fjalori §6).</summary>
    public static void RunStatement(string sql) => _ = sql.Length;

    /// <summary>Service locator që jep komandë raw Npgsql.</summary>
    public static NpgsqlCommand CommandLocator(NpgsqlConnection connection) => connection.CreateCommand();
}

/// <summary>GREEN 18 — API publik i ndërmjetësuar, plotësisht i tipizuar.</summary>
public static class TypedOwnerMediatedApiFixture
{
    public static string DescribeBusiness(Guid businessId) => businessId.ToString("D");

    public static IReadOnlyList<Guid> EmptyMembershipIds() => [];
}
