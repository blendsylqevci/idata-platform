using System.Security.Claims;
using Idata.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Override i autentikimit VETËM në test assembly (§8): middleware që lexon
/// headers sintetikë dhe mbush ClaimsPrincipal-in provider-neutral.
/// Production host-i NUK e përmban këtë kod — pa headers principal-i mbetet
/// bosh → deny-by-default. Header-i `X-Test-Unauthenticated` krijon një
/// ClaimsIdentity PA authenticationType (IsAuthenticated=false) me claims të
/// vlefshme — për të provuar që claims të pa-autentikuara refuzohen.
/// </summary>
public class AccessControlApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new TestActorStartupFilter()));

    private sealed class TestActorStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    var claims = new List<Claim>();
                    if (context.Request.Headers.TryGetValue("X-Test-UserId", out var userId))
                    {
                        claims.Add(new Claim(Modules.AccessControl.Presentation.ActorClaimTypes.UserId, userId.ToString()));
                    }

                    if (context.Request.Headers.TryGetValue("X-Test-BusinessId", out var businessId))
                    {
                        claims.Add(new Claim(Modules.AccessControl.Presentation.ActorClaimTypes.BusinessId, businessId.ToString()));
                    }

                    if (claims.Count > 0)
                    {
                        var unauthenticated = context.Request.Headers.ContainsKey("X-Test-Unauthenticated");
                        // authenticationType = null → IsAuthenticated=false.
                        var identity = unauthenticated
                            ? new ClaimsIdentity(claims)
                            : new ClaimsIdentity(claims, "test-only");
                        context.User = new ClaimsPrincipal(identity);
                    }

                    await nextMiddleware();
                });
                next(app);
            };
    }
}

/// <summary>
/// Seed SINTETIK deterministik i F1.1 (GUID hex-shkronja; zero PII/digit-run):
/// biznese active/disabled, users active/disabled, role dhe memberships që
/// mbulojnë matricat lifecycle (actor & target). Shkruhet me EF mbi record-et
/// e Persistence-it (pas verifikimit §6A të target-it). memberships nisin me
/// version=0.
/// </summary>
public static class AccessControlSeed
{
    public static readonly Guid BusinessA = Guid.Parse("aaaaaaaa-face-4ace-8ace-aaaaaaaaaaaa");
    public static readonly Guid BusinessB = Guid.Parse("bbbbbbbb-face-4ace-8ace-bbbbbbbbbbbb");
    public static readonly Guid BusinessDisabled = Guid.Parse("dddddddd-face-4ace-8ace-dddddddddddd");

    public static readonly Guid UserA = Guid.Parse("caaaaaaa-cafe-4afe-8afe-caaaaaaaaaaa");
    public static readonly Guid UserB = Guid.Parse("cbbbbbbb-cafe-4afe-8afe-cbbbbbbbbbbb");
    public static readonly Guid UserC = Guid.Parse("cccccccc-cafe-4afe-8afe-cccccccccccc");
    public static readonly Guid UserActorDisabled = Guid.Parse("cddddddd-cafe-4afe-8afe-cddddddddddd");
    public static readonly Guid UserInDisabledBiz = Guid.Parse("ceeeeeee-cafe-4afe-8afe-ceeeeeeeeeee");
    public static readonly Guid UserTargetDisabled = Guid.Parse("cfffffff-cafe-4afe-8afe-cfffffffffff");
    public static readonly Guid UserForDisabledMembership = Guid.Parse("cabcabca-cafe-4afe-8afe-cabcabcabcab");
    /// <summary>
    /// User i ÇAKTIVIZUAR PA membership në asnjë business — lejon të provohet
    /// ripikëzimi i `business_membership.user_id` te një user jo-aktiv pa
    /// shkelur unique index-in `(business_id, user_id)`.
    /// </summary>
    public static readonly Guid UserDisabledUnattached = Guid.Parse("cbadcbad-cafe-4afe-8afe-cbadcbadcbad");

    public static readonly Guid RoleA = Guid.Parse("daaaaaaa-fade-4ade-8ade-daaaaaaaaaaa");
    public static readonly Guid RoleA2 = Guid.Parse("dddddaaa-fade-4ade-8ade-dddddaaaaaaa");
    public static readonly Guid RoleB = Guid.Parse("dbbbbbbb-fade-4ade-8ade-dbbbbbbbbbbb");
    public static readonly Guid RoleDisabledBiz = Guid.Parse("deeeeeee-fade-4ade-8ade-deeeeeeeeeee");
    /// <summary>Rol i business-it A me IsSystem=true — NUK caktohet nga endpoint-i F1.1.</summary>
    public static readonly Guid RoleSystemA = Guid.Parse("dcdcdcdc-fade-4ade-8ade-dcdcdcdcdcdc");

    public static readonly Guid PermissionRead = Guid.Parse("faaaaaaa-feed-4eed-8eed-faaaaaaaaaaa");
    public static readonly Guid PermissionUpdate = Guid.Parse("fbbbbbbb-feed-4eed-8eed-fbbbbbbbbbbb");

    public static readonly Guid MembershipA = Guid.Parse("eaeaeaea-bead-4ead-8ead-eaeaeaeaeaea");
    public static readonly Guid MembershipB = Guid.Parse("ebbbbbbb-bead-4ead-8ead-ebbbbbbbbbbb");
    public static readonly Guid MembershipC = Guid.Parse("eccccccc-bead-4ead-8ead-eccccccccccc");
    public static readonly Guid MembershipActorDisabled = Guid.Parse("eddddddd-bead-4ead-8ead-eddddddddddd");
    public static readonly Guid MembershipTargetDisabled = Guid.Parse("eeeeeeee-bead-4ead-8ead-eeeeeeeeeeee");
    public static readonly Guid MembershipTargetUserDisabled = Guid.Parse("efffffff-bead-4ead-8ead-efffffffffff");
    public static readonly Guid MembershipInDisabledBiz = Guid.Parse("faffffff-bead-4ead-8ead-faffffffffff");
    /// <summary>
    /// FIXTURE DISKRIMINUESE (§3.A): membership AKTIV në business AKTIV me rol
    /// që KA `users_roles:U` — i vetmi ndryshim është `app_user.status =
    /// disabled`. Pa guard-in e user-status-it ky aktor do të autorizohej.
    /// </summary>
    public static readonly Guid MembershipDisabledUserActor = Guid.Parse("fbeefbee-bead-4ead-8ead-fbeefbeefbee");

    /// <summary>GUID target deterministik që NUK ekziston në seed (për 404).</summary>
    public static readonly Guid NonexistentId = Guid.Parse("deaddead-dead-4dea-8dea-deaddeaddead");

    public static async Task SeedAsync(string databaseName)
    {
        var settings = PlatformDatabaseConnection.Parse(
            Environment.GetEnvironmentVariable("PGHOST") ?? "localhost",
            Environment.GetEnvironmentVariable("PGPORT") ?? "5432",
            Environment.GetEnvironmentVariable("PGUSER") ?? "idata",
            databaseName);

        await using var context = PlatformDbContextFactory.Create(settings.ToConnectionString());
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();
        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, databaseName);

        var now = DateTimeOffset.UtcNow;
        context.Businesses.AddRange(
            new BusinessRecord { Id = BusinessA, Name = "biznes-sintetik-a", Status = RecordStatus.Active, CreatedAt = now },
            new BusinessRecord { Id = BusinessB, Name = "biznes-sintetik-b", Status = RecordStatus.Active, CreatedAt = now },
            new BusinessRecord { Id = BusinessDisabled, Name = "biznes-sintetik-x", Status = RecordStatus.Disabled, CreatedAt = now });
        context.AppUsers.AddRange(
            new AppUserRecord { Id = UserA, Status = RecordStatus.Active, CreatedAt = now },
            new AppUserRecord { Id = UserB, Status = RecordStatus.Active, CreatedAt = now },
            new AppUserRecord { Id = UserC, Status = RecordStatus.Active, CreatedAt = now },
            new AppUserRecord { Id = UserActorDisabled, Status = RecordStatus.Disabled, CreatedAt = now },
            new AppUserRecord { Id = UserInDisabledBiz, Status = RecordStatus.Active, CreatedAt = now },
            new AppUserRecord { Id = UserTargetDisabled, Status = RecordStatus.Disabled, CreatedAt = now },
            new AppUserRecord { Id = UserForDisabledMembership, Status = RecordStatus.Active, CreatedAt = now },
            new AppUserRecord { Id = UserDisabledUnattached, Status = RecordStatus.Disabled, CreatedAt = now });
        context.Permissions.AddRange(
            new PermissionRecord { Id = PermissionRead, FeatureKey = "platform.users_roles", Action = "R", CreatedAt = now },
            new PermissionRecord { Id = PermissionUpdate, FeatureKey = "platform.users_roles", Action = "U", CreatedAt = now });
        context.Roles.AddRange(
            new RoleRecord { Id = RoleA, BusinessId = BusinessA, Name = "rol-sintetik-a", IsSystem = false, CreatedAt = now },
            new RoleRecord { Id = RoleA2, BusinessId = BusinessA, Name = "rol-sintetik-a-dy", IsSystem = false, CreatedAt = now },
            new RoleRecord { Id = RoleB, BusinessId = BusinessB, Name = "rol-sintetik-b", IsSystem = false, CreatedAt = now },
            new RoleRecord { Id = RoleDisabledBiz, BusinessId = BusinessDisabled, Name = "rol-sintetik-x", IsSystem = false, CreatedAt = now },
            // Rol SYSTEM-managed i business-it A: valid në çdo aspekt tjetër.
            new RoleRecord { Id = RoleSystemA, BusinessId = BusinessA, Name = "rol-sintetik-sistem", IsSystem = true, CreatedAt = now });
        // Vetëm RoleA ka permission-e (R+U); rolet e tjera janë pa-permission.
        context.RolePermissions.AddRange(
            new RolePermissionRecord { BusinessId = BusinessA, RoleId = RoleA, PermissionId = PermissionRead },
            new RolePermissionRecord { BusinessId = BusinessA, RoleId = RoleA, PermissionId = PermissionUpdate });
        context.BusinessMemberships.AddRange(
            new BusinessMembershipRecord { Id = MembershipA, BusinessId = BusinessA, UserId = UserA, RoleId = RoleA, Version = 0, Status = RecordStatus.Active, CreatedAt = now },
            new BusinessMembershipRecord { Id = MembershipB, BusinessId = BusinessB, UserId = UserB, RoleId = RoleB, Version = 0, Status = RecordStatus.Active, CreatedAt = now },
            new BusinessMembershipRecord { Id = MembershipC, BusinessId = BusinessA, UserId = UserC, RoleId = RoleA, Version = 0, Status = RecordStatus.Active, CreatedAt = now },
            // Aktori me membership të çaktivizuar (user active, membership disabled).
            new BusinessMembershipRecord { Id = MembershipActorDisabled, BusinessId = BusinessA, UserId = UserB, RoleId = RoleA, Version = 0, Status = RecordStatus.Disabled, CreatedAt = now },
            // Target me membership të çaktivizuar (user active).
            new BusinessMembershipRecord { Id = MembershipTargetDisabled, BusinessId = BusinessA, UserId = UserForDisabledMembership, RoleId = RoleA, Version = 0, Status = RecordStatus.Disabled, CreatedAt = now },
            // Target me membership active por user të çaktivizuar.
            new BusinessMembershipRecord { Id = MembershipTargetUserDisabled, BusinessId = BusinessA, UserId = UserTargetDisabled, RoleId = RoleA, Version = 0, Status = RecordStatus.Active, CreatedAt = now },
            // Aktori me user active + rol me U, por business i çaktivizuar.
            new BusinessMembershipRecord { Id = MembershipInDisabledBiz, BusinessId = BusinessDisabled, UserId = UserInDisabledBiz, RoleId = RoleDisabledBiz, Version = 0, Status = RecordStatus.Active, CreatedAt = now },
            // §3.A: gjithçka aktive PËRVEÇ app_user.status të aktorit.
            new BusinessMembershipRecord { Id = MembershipDisabledUserActor, BusinessId = BusinessA, UserId = UserActorDisabled, RoleId = RoleA, Version = 0, Status = RecordStatus.Active, CreatedAt = now });
        // Rol-i i business-it të çaktivizuar merr R+U që deny të vijë NGA business-status, jo nga permission.
        context.RolePermissions.AddRange(
            new RolePermissionRecord { BusinessId = BusinessDisabled, RoleId = RoleDisabledBiz, PermissionId = PermissionRead },
            new RolePermissionRecord { BusinessId = BusinessDisabled, RoleId = RoleDisabledBiz, PermissionId = PermissionUpdate });
        await context.SaveChangesAsync();
    }

    /// <summary>Migrim + seed + factory mbi DB-në e dhënë (env serial global).</summary>
    public static async Task<AccessControlApiFactory> MigrateSeedAndCreateFactoryAsync(string databaseName)
    {
        if (await RunMigratorAsync(databaseName) != 0)
        {
            throw new InvalidOperationException("Seed: migrimi i DB-se sintetike deshtoi.");
        }

        await SeedAsync(databaseName);
        return new AccessControlApiFactory();
    }

    private static async Task<int> RunMigratorAsync(string databaseName)
    {
        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", databaseName);
        try
        {
            return await Idata.Platform.DbMigrator.MigratorApp.RunAsync(["migrate"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }
}
