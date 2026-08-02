namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Taksonomia ENG-007 (Pre-B2 Increment 1, §4) e RAW-DATA SINK-eve —
/// DENY-BY-DEFAULT. Autoriteti NUK është literali `identity.` dhe NUK është
/// parametrizimi: autoriteti është **aftësia për ekzekutim raw**, edhe kur
/// SQL-i ndërtohet në runtime.
///
/// Dy shtresa komplementare, të dyja të imponuara nga
/// <see cref="UntypedDataAccessScanner"/>:
///
///  (A) <see cref="RawCapabilityTypeFullNames"/> — tipet që MISHËROJNË
///      kapacitetin raw. Çdo referencë ndaj tyre (newobj/castclass/isinst/
///      ldtoken/initobj/constrained/sizeof, ose çdo anëtar i tyre — I NJOHUR
///      OSE I PANJOHUR) është gjetje. Kjo është forma operacionale e rregullit
///      §4.2 "sink/API/symbol i panjohur = finding, jo `continue`": nuk kërkohet
///      listë shteruese anëtarësh, sepse anëtarësia në tipin raw mjafton.
///
///  (B) <see cref="RawExecutionMemberNames"/> — emrat e anëtarëve që
///      përfaqësojnë ekzekutim raw PAVARËSISHT tipit deklarues (extension
///      methods të EF Core relational, variantet async/generic, accessor-ët e
///      property-ve që bartin komandën). Kjo mbulon sipërfaqet ku tipi
///      deklarues është një klasë statike `*Extensions` e padëmshme në vetvete.
///
/// Përputhja është me EMËR TË PLOTË TË TIPIT (ordinal, i plotë) dhe me EMËR
/// EKZAKT ANËTARI — asnjë `StartsWith`/`Contains`, që `NpgsqlConnection` të mos
/// e kapë `NpgsqlConnectionStringBuilder` (kufi pike; fixture GREEN i dedikuar).
/// </summary>
internal static class RawDataSinkTaxonomy
{
    /// <summary>
    /// (A) Tipet me kapacitet raw — ADO.NET + Npgsql (përfshirë rrugët e
    /// import/export COPY) + lexuesit që ekzistojnë vetëm si pasojë e
    /// ekzekutimit raw. `NpgsqlConnectionStringBuilder`/`NpgsqlParameter`
    /// NUK janë këtu: ndërtojnë konfigurim/parametra, nuk ekzekutojnë.
    /// </summary>
    internal static readonly IReadOnlySet<string> RawCapabilityTypeFullNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            // ── ADO.NET (System.Data / System.Data.Common) ──
            "System.Data.Common.DbConnection",
            "System.Data.Common.DbCommand",
            "System.Data.Common.DbBatch",
            "System.Data.Common.DbBatchCommand",
            "System.Data.Common.DbDataReader",
            "System.Data.Common.DbTransaction",
            "System.Data.Common.DbDataAdapter",
            "System.Data.Common.DbProviderFactory",
            "System.Data.IDbConnection",
            "System.Data.IDbCommand",
            "System.Data.IDataReader",
            "System.Data.IDbTransaction",
            "System.Data.IDbDataAdapter",

            // ── Npgsql ──
            "Npgsql.NpgsqlConnection",
            "Npgsql.NpgsqlCommand",
            "Npgsql.NpgsqlBatch",
            "Npgsql.NpgsqlBatchCommand",
            "Npgsql.NpgsqlDataSource",
            "Npgsql.NpgsqlMultiHostDataSource",
            "Npgsql.NpgsqlDataReader",
            "Npgsql.NpgsqlTransaction",
            "Npgsql.NpgsqlBinaryImporter",
            "Npgsql.NpgsqlBinaryExporter",
            "Npgsql.NpgsqlRawCopyStream",
            "Npgsql.NpgsqlCopyTextReader",
            "Npgsql.NpgsqlCopyTextWriter",
        };

    /// <summary>
    /// (B) Emrat e anëtarëve = ekzekutim raw, pavarësisht tipit deklarues.
    /// Përfshin EKSPLICIT variantet async dhe accessor-ët e property-ve.
    /// </summary>
    internal static readonly IReadOnlySet<string> RawExecutionMemberNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            // ── ADO.NET: krijimi i komandës dhe teksti i saj ──
            "CreateCommand", "CreateDbCommand", "CreateBatch", "CreateDbBatch",
            "CommandText", "get_CommandText", "set_CommandText",
            "ExecuteReader", "ExecuteReaderAsync",
            "ExecuteScalar", "ExecuteScalarAsync",
            "ExecuteNonQuery", "ExecuteNonQueryAsync",
            "ExecuteDbDataReader", "ExecuteDbDataReaderAsync",

            // ── EF Core relational — raw query/command surface ──
            "FromSql", "FromSqlRaw", "FromSqlInterpolated",
            "ExecuteSql", "ExecuteSqlAsync",
            "ExecuteSqlRaw", "ExecuteSqlRawAsync",
            "ExecuteSqlInterpolated", "ExecuteSqlInterpolatedAsync",
            "SqlQuery", "SqlQueryRaw",

            // ── EF Core relational — dorëzimi i handle-ve raw ──
            "GetDbConnection", "SetDbConnection", "GetDbTransaction",

            // ── Npgsql — rrugët e import/export (COPY) ──
            "BeginTextImport", "BeginBinaryImport", "BeginRawBinaryCopy",
            "BeginTextExport", "BeginBinaryExport",
        };

    /// <summary>
    /// §6 — fjalori i emrave të PARAMETRAVE që tregojnë pranim SQL-i ose
    /// identifikuesi schema/tabele në sipërfaqen publike të owner-ave.
    /// Kufi i deklaruar HAPUR: kjo është shtresë NAME-BASED (jo semantike) —
    /// autoritet mbetet grafi i tipeve; emrat e mëposhtëm janë mbulim shtesë
    /// mbi `string`-un e padallueshëm në IL. `connectionString`/`database`
    /// NUK janë këtu: konfigurim i validuar, jo SQL/schema/tabelë.
    /// </summary>
    internal static readonly IReadOnlySet<string> RawSqlParameterNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sql", "rawSql", "sqlText", "sqlQuery", "sqlStatement",
            "commandText", "command", "query", "queryString", "queryText",
            "schema", "schemaName", "table", "tableName", "tableSchema",
        };

    /// <summary>A është ky emër i plotë tipi një tip me kapacitet raw (barazi EKZAKTE)?</summary>
    internal static bool IsRawCapabilityType(string fullTypeName)
        => RawCapabilityTypeFullNames.Contains(fullTypeName);

    /// <summary>A është ky emër anëtari ekzekutim raw (barazi EKZAKTE)?</summary>
    internal static bool IsRawExecutionMember(string memberName)
        => RawExecutionMemberNames.Contains(memberName);
}
