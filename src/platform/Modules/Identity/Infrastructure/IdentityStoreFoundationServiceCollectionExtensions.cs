using Idata.Platform.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Idata.Platform.Modules.Identity.Infrastructure;

/// <summary>
/// Wiring i VETËM i Identity Store Foundation (ADR-028, Increment B.1) —
/// konsumohet nga composition root-i (API host):
///  - <c>AddIdentityCore</c> ROLELESS (JO AddDefaultIdentity, JO AddRoles,
///    JO RoleManager/IdentityRole) + store EF mbi kontekstin roleless
///    (UserOnlyStore — provuar me teste DI mbi tipin real);
///  - OpenIddict <b>Core/EF store VETËM</b> me entitete Guid — ZERO
///    Server/Validation/Client, ZERO endpoint/flows/credentials (B.2);
///  - ZERO authentication scheme/cookies — deny-by-default i F1.1 mbetet;
///  - ZERO projection drejt platform.app_user (B.3).
/// </summary>
public static class IdentityStoreFoundationServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityStoreFoundation(this IServiceCollection services)
    {
        services.AddIdentityCore<UserAccountRecord>()
            .AddEntityFrameworkStores<PlatformDbContext>();

        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore()
                .UseDbContext<PlatformDbContext>()
                .ReplaceDefaultEntities<Guid>());

        return services;
    }
}
