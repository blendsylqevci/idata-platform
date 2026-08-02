using Idata.Platform.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OpenIddict.EntityFrameworkCore.Models;
using System.Text.RegularExpressions;
using Xunit;

namespace Idata.Platform.UnitTests;

/// <summary>
/// Modeli REAL i store foundation-it (ADR-028, B.1) — i provuar mbi
/// <c>IModel</c>-in e EF-it PA lidhje DB: schema/tabela/kolona ekzakte,
/// çelësa Guid/uuid, snake_case, roleless, pronësi e ndarë e token-ave,
/// ZERO FK cross-schema, plus prova MEKANIKE që operacionet e migrimit
/// 0003 janë EKSKLUZIVISHT brenda schema-s `identity`.
/// </summary>
public sealed class IdentityStoreModelTests
{
    private static PlatformDbContext CreateModelOnlyContext()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>();
        // Vetëm për ndërtim modeli — asnjë lidhje nuk hapet ndonjëherë.
        PlatformDbContextFactory.Configure(options, "Host=localhost;Database=model_only;Username=model");
        return new PlatformDbContext(options.Options);
    }

    private static (string? Schema, string? Table, IEntityType Entity)[] EntityTables(PlatformDbContext context)
        => [.. context.Model.GetEntityTypes().Select(entity => (entity.GetSchema(), entity.GetTableName(), entity))];

    [Fact]
    public void Konteksti_eshte_IdentityUserContext_roleless_me_Guid_jo_IdentityDbContext()
    {
        var baseTypes = new List<Type>();
        for (var type = typeof(PlatformDbContext).BaseType; type is not null; type = type.BaseType)
        {
            baseTypes.Add(type);
        }

        // Zinxhiri kalon nëpër IdentityUserContext`2 → IdentityUserContext`5
        // (roleless) — dhe KURRË nëpër IdentityDbContext (me role).
        var identityBases = baseTypes
            .Where(type => type.IsGenericType
                && type.GetGenericTypeDefinition().Name.StartsWith("IdentityUserContext`", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(identityBases);
        Assert.DoesNotContain(baseTypes, type => type.Name.StartsWith("IdentityDbContext", StringComparison.Ordinal));

        // TUser = UserAccountRecord : IdentityUser<Guid>; TKey = Guid.
        Assert.All(identityBases, type => Assert.Equal(typeof(UserAccountRecord), type.GenericTypeArguments[0]));
        Assert.All(identityBases, type => Assert.Contains(typeof(Guid), type.GenericTypeArguments));
        Assert.True(typeof(IdentityUser<Guid>).IsAssignableFrom(typeof(UserAccountRecord)));
    }

    [Fact]
    public void Schema_identity_ka_EKZAKT_8_tabelat_e_kontrolluara_me_tipet_reale()
    {
        using var context = CreateModelOnlyContext();
        var identityTables = EntityTables(context)
            .Where(row => row.Schema == PlatformDbContext.IdentitySchema)
            .ToDictionary(row => row.Table ?? string.Empty, row => row.Entity.ClrType);

        Assert.Equal(
            ["oidc_application", "oidc_authorization", "oidc_scope", "oidc_token",
             "user_account", "user_claim", "user_login", "user_token"],
            identityTables.Keys.Order().ToArray());

        Assert.Equal(typeof(UserAccountRecord), identityTables["user_account"]);
        Assert.Equal(typeof(IdentityUserLogin<Guid>), identityTables["user_login"]);
        // Claim-i = tipi CUSTOM me çelës UUID — JO default-i IdentityUserClaim<Guid>.
        Assert.Equal(typeof(UserClaimRecord), identityTables["user_claim"]);
        Assert.Equal(typeof(IdentityUserToken<Guid>), identityTables["user_token"]);
        Assert.Equal(typeof(OpenIddictEntityFrameworkCoreApplication<Guid>), identityTables["oidc_application"]);
        Assert.Equal(typeof(OpenIddictEntityFrameworkCoreAuthorization<Guid>), identityTables["oidc_authorization"]);
        Assert.Equal(typeof(OpenIddictEntityFrameworkCoreScope<Guid>), identityTables["oidc_scope"]);
        Assert.Equal(typeof(OpenIddictEntityFrameworkCoreToken<Guid>), identityTables["oidc_token"]);
    }

    [Fact]
    public void Schema_platform_mbetet_e_PANDRYSHUAR_nga_B1()
    {
        using var context = CreateModelOnlyContext();
        var platformTables = EntityTables(context)
            .Where(row => row.Schema == PlatformDbContext.Schema)
            .Select(row => row.Table ?? string.Empty)
            .Order()
            .ToArray();

        Assert.Equal(
            ["app_user", "audit_log", "business", "business_membership", "command_receipt",
             "migration_smoke_check", "permission", "role", "role_permission"],
            platformTables);
    }

    [Fact]
    public void Zero_persisted_int_IDs_celesat_jane_Guid_uuid_ose_kompozite_string_Guid()
    {
        using var context = CreateModelOnlyContext();
        foreach (var (schema, _, entity) in EntityTables(context))
        {
            if (schema != PlatformDbContext.IdentitySchema)
            {
                continue;
            }

            // ABSOLUT: asnjë çelës primar int në ASNJË entitet identity —
            // Guid ose komponente string të çelësave kompozitë REALË.
            foreach (var keyProperty in entity.FindPrimaryKey()!.Properties)
            {
                Assert.NotEqual(typeof(int), keyProperty.ClrType);
                Assert.True(keyProperty.ClrType == typeof(Guid) || keyProperty.ClrType == typeof(string));
            }

            var userIdProperty = entity.GetProperties().SingleOrDefault(p => p.Name == "UserId");
            if (userIdProperty is not null)
            {
                Assert.Equal(typeof(Guid), userIdProperty.ClrType);
                Assert.Equal("uuid", userIdProperty.GetColumnType());
            }
        }

        // user_account.Id = uuid me gjenerim on-add.
        var userAccount = context.Model.FindEntityType(typeof(UserAccountRecord))!;
        var idProperty = userAccount.FindPrimaryKey()!.Properties.Single();
        Assert.Equal(typeof(Guid), idProperty.ClrType);
        Assert.Equal("uuid", idProperty.GetColumnType());
        Assert.Equal(ValueGenerated.OnAdd, idProperty.ValueGenerated);
    }

    [Fact]
    public void User_claim_ka_PK_te_vetem_ClaimId_Guid_dhe_inherited_Id_int_ABSENT_nga_modeli()
    {
        using var context = CreateModelOnlyContext();
        var userClaim = context.Model.FindEntityType(typeof(UserClaimRecord))!;

        // PK i VETËM = ClaimId (Guid), i mapuar te kolona kanonike `id` uuid,
        // i gjeneruar në APLIKACION (kurrë nga DB).
        var keyProperty = Assert.Single(userClaim.FindPrimaryKey()!.Properties);
        Assert.Equal(nameof(UserClaimRecord.ClaimId), keyProperty.Name);
        Assert.Equal(typeof(Guid), keyProperty.ClrType);
        Assert.Equal("uuid", keyProperty.GetColumnType());
        Assert.Equal("id", keyProperty.GetColumnName(
            StoreObjectIdentifier.Table("user_claim", PlatformDbContext.IdentitySchema)));
        Assert.Equal(ValueGenerated.Never, keyProperty.ValueGenerated);

        // Inherited `IdentityUserClaim<TKey>.Id` (int) = i INJORUAR nga EF:
        // NUK ekziston si property e modelit → NUK persiston asnjë kolonë int.
        Assert.Null(userClaim.FindProperty(nameof(IdentityUserClaim<Guid>.Id)));

        // Fushat reale të claim-it mbeten (UserId FK + type/value).
        Assert.NotNull(userClaim.FindProperty("UserId"));
        Assert.NotNull(userClaim.FindProperty("ClaimType"));
        Assert.NotNull(userClaim.FindProperty("ClaimValue"));
        var foreignKey = Assert.Single(userClaim.GetForeignKeys());
        Assert.Equal(typeof(UserAccountRecord), foreignKey.PrincipalEntityType.ClrType);
    }

    /// <summary>
    /// §7.1 i correction R2: invarianti i APLIKACIONIT — ClaimId refuzon
    /// Guid.Empty në setter; default construction = UUID non-empty.
    /// </summary>
    [Fact]
    public void ClaimId_refuzon_Guid_Empty_ne_aplikacion_dhe_default_eshte_non_empty()
    {
        var record = new UserClaimRecord();
        Assert.NotEqual(Guid.Empty, record.ClaimId);

        var replacement = Guid.NewGuid();
        record.ClaimId = replacement;
        Assert.Equal(replacement, record.ClaimId);

        var failure = Assert.Throws<ArgumentException>(() => record.ClaimId = Guid.Empty);
        Assert.Contains("ClaimId", failure.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new UserClaimRecord { ClaimId = Guid.Empty });

        // Vlera e mëparshme mbetet e paprekur pas refuzimit.
        Assert.Equal(replacement, record.ClaimId);
    }

    /// <summary>
    /// §7.2 i correction R2: invarianti i DATABASE-it — CHECK constraint
    /// determinist mbi `identity.user_claim.id` që ndalon zero-UUID.
    /// </summary>
    [Fact]
    public void User_claim_ka_CHECK_constraint_non_empty_mbi_id()
    {
        // Evidencë DIREKT në operacionet e migrimit 0003 (modeli runtime
        // është read-optimized dhe s'i mban check constraints).
        var migrationType = typeof(PlatformDbContext).Assembly
            .GetTypes()
            .Single(type => type.Name == "IdentityStoreFoundation");
        var migration = (Migration)Activator.CreateInstance(migrationType)!;

        var userClaimTable = migration.UpOperations
            .OfType<CreateTableOperation>()
            .Single(operation => operation.Name == "user_claim");
        var constraint = Assert.Single(userClaimTable.CheckConstraints,
            candidate => candidate.Name == "ck_user_claim_id_non_empty");
        Assert.Contains("repeat('0', 32)", constraint.Sql, StringComparison.Ordinal);
        Assert.Contains("uuid", constraint.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void User_login_dhe_user_token_ruajne_celesat_kompozite_EKZAKTE_te_framework_ut()
    {
        using var context = CreateModelOnlyContext();

        // §4.5 i mandatit: këta çelësa janë korrektë dhe NUK ndryshohen.
        var userLogin = context.Model.FindEntityType(typeof(IdentityUserLogin<Guid>))!;
        Assert.Equal(
            ["LoginProvider", "ProviderKey"],
            userLogin.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());

        var userToken = context.Model.FindEntityType(typeof(IdentityUserToken<Guid>))!;
        Assert.Equal(
            ["UserId", "LoginProvider", "Name"],
            userToken.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
    }

    [Fact]
    public void Kolonat_e_schema_identity_jane_te_gjitha_lowercase_snake_case()
    {
        using var context = CreateModelOnlyContext();
        foreach (var (schema, table, entity) in EntityTables(context))
        {
            if (schema != PlatformDbContext.IdentitySchema)
            {
                continue;
            }

            var storeObject = StoreObjectIdentifier.Table(table!, schema);
            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName(storeObject);
                Assert.NotNull(column);
                Assert.Matches(new Regex("^[a-z][a-z0-9_]*$"), column);
            }
        }
    }

    [Fact]
    public void Mapping_i_bazes_Identity_mbijeton_remap_in_prova_qe_base_OnModelCreating_u_thirr_i_pari()
    {
        using var context = CreateModelOnlyContext();
        var userAccount = context.Model.FindEntityType(typeof(UserAccountRecord))!;

        // Nga base.OnModelCreating (Identity): max length 256 + indekset unike.
        Assert.Equal(256, userAccount.GetProperty("UserName").GetMaxLength());
        Assert.Equal(256, userAccount.GetProperty("NormalizedUserName").GetMaxLength());
        var uniqueOnUserName = Assert.Single(userAccount.GetIndexes(),
            index => index.Properties.Single().Name == "NormalizedUserName");
        Assert.True(uniqueOnUserName.IsUnique);

        // Nga remap-i ynë PAS bazës: tabela/schema e kontrolluar + zero emra
        // eksplicitë të framework-ut për indekset (UserNameIndex/EmailIndex).
        Assert.Equal("user_account", userAccount.GetTableName());
        Assert.Equal(PlatformDbContext.IdentitySchema, userAccount.GetSchema());
        foreach (var index in userAccount.GetIndexes())
        {
            var databaseName = index.GetDatabaseName();
            Assert.NotEqual("UserNameIndex", databaseName);
            Assert.NotEqual("EmailIndex", databaseName);
        }
    }

    [Fact]
    public void Zero_entitet_rolesh_dhe_zero_emra_AspNet_ne_model()
    {
        using var context = CreateModelOnlyContext();
        foreach (var (_, table, entity) in EntityTables(context))
        {
            Assert.DoesNotContain("IdentityRole", entity.ClrType.Name, StringComparison.Ordinal);
            Assert.DoesNotContain("IdentityUserRole", entity.ClrType.Name, StringComparison.Ordinal);
            Assert.DoesNotContain("IdentityRoleClaim", entity.ClrType.Name, StringComparison.Ordinal);
            Assert.False(table!.StartsWith("AspNet", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Pronesia_e_token_ave_e_ndare_dhe_zero_FK_cross_schema()
    {
        using var context = CreateModelOnlyContext();

        // user_token (Identity, account-level) ≠ oidc_token (OpenIddict).
        var userToken = context.Model.FindEntityType(typeof(IdentityUserToken<Guid>))!;
        var oidcToken = context.Model.FindEntityType(typeof(OpenIddictEntityFrameworkCoreToken<Guid>))!;
        Assert.NotEqual(userToken.GetTableName(), oidcToken.GetTableName());
        Assert.DoesNotContain(userToken.GetForeignKeys(), fk => fk.PrincipalEntityType == oidcToken);
        Assert.DoesNotContain(oidcToken.GetForeignKeys(), fk => fk.PrincipalEntityType == userToken);

        // ZERO FK cross-schema në KREJT modelin (identity↔platform të ndara).
        foreach (var entity in context.Model.GetEntityTypes())
        {
            foreach (var foreignKey in entity.GetForeignKeys())
            {
                Assert.Equal(
                    foreignKey.PrincipalEntityType.GetSchema(),
                    foreignKey.DeclaringEntityType.GetSchema());
            }
        }
    }

    [Fact]
    public void Operacionet_e_migrimit_0003_jane_EKSKLUZIVISHT_brenda_schema_identity()
    {
        var migrationType = typeof(PlatformDbContext).Assembly
            .GetTypes()
            .Single(type => type.Name == "IdentityStoreFoundation");
        var migration = (Migration)Activator.CreateInstance(migrationType)!;

        Assert.NotEmpty(migration.UpOperations);
        foreach (var operation in migration.UpOperations)
        {
            switch (operation)
            {
                case EnsureSchemaOperation ensureSchema:
                    Assert.Equal(PlatformDbContext.IdentitySchema, ensureSchema.Name);
                    break;
                case CreateTableOperation createTable:
                    Assert.Equal(PlatformDbContext.IdentitySchema, createTable.Schema);
                    Assert.All(createTable.ForeignKeys, fk =>
                        Assert.Equal(PlatformDbContext.IdentitySchema, fk.PrincipalSchema));
                    break;
                case CreateIndexOperation createIndex:
                    Assert.Equal(PlatformDbContext.IdentitySchema, createIndex.Schema);
                    break;
                default:
                    Assert.Fail($"Operacion i palejuar në 0003: {operation.GetType().Name}");
                    break;
            }
        }

        // Down: heqje VETËM e tabelave identity (rollback i guard-uar, disposable).
        Assert.All(migration.DownOperations, operation =>
        {
            var dropTable = Assert.IsType<DropTableOperation>(operation);
            Assert.Equal(PlatformDbContext.IdentitySchema, dropTable.Schema);
        });
    }
}
