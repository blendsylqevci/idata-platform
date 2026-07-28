using Idata.Platform.Modules.Identity.Infrastructure;
using Idata.Platform.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Xunit;

namespace Idata.Platform.UnitTests;

/// <summary>
/// DI wiring i store foundation-it (ADR-028, B.1) — i provuar mbi
/// ServiceCollection REALE pa asnjë lidhje DB: store ROLELESS i vërtetë
/// (UserOnlyStore — tipi REAL, jo hamendësim), zero shërbime rolesh,
/// menaxherët OpenIddict Core me entitete Guid, zero authentication scheme.
/// </summary>
public sealed class IdentityStoreWiringTests
{
    private static ServiceCollection BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PlatformDbContext>(options =>
            PlatformDbContextFactory.Configure(options, "Host=localhost;Database=wiring_only;Username=wiring"));
        services.AddIdentityStoreFoundation();
        return services;
    }

    [Fact]
    public void Zero_sherbime_rolesh_ne_regjistrime_dhe_zero_authentication_scheme()
    {
        var services = BuildServices();

        foreach (var descriptor in services)
        {
            var serviceName = descriptor.ServiceType.FullName ?? string.Empty;
            var implementationName = (descriptor.IsKeyedService
                ? descriptor.KeyedImplementationType?.FullName
                : descriptor.ImplementationType?.FullName) ?? string.Empty;
            foreach (var forbidden in (string[])["RoleManager", "IRoleStore", "RoleStore", "RoleValidator", "IdentityRole"])
            {
                Assert.DoesNotContain(forbidden, serviceName, StringComparison.Ordinal);
                Assert.DoesNotContain(forbidden, implementationName, StringComparison.Ordinal);
            }

            // B.1 = STORE foundation: zero cookies/authentication protocol.
            Assert.DoesNotContain("AuthenticationScheme", serviceName, StringComparison.Ordinal);
            Assert.DoesNotContain(".Server.", serviceName, StringComparison.Ordinal);
            Assert.DoesNotContain(".Validation.", serviceName, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UserStore_i_rezolvuar_eshte_UserOnlyStore_roleless_me_Guid()
    {
        using var provider = BuildServices().BuildServiceProvider();
        using var scope = provider.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<IUserStore<UserAccountRecord>>();
        var storeType = store.GetType();

        // Tipi REAL i store-it: UserOnlyStore (roleless) — JO UserStore (me role).
        Assert.StartsWith(
            "Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserOnlyStore`",
            storeType.GetGenericTypeDefinition().FullName ?? string.Empty,
            StringComparison.Ordinal);
        Assert.Contains(typeof(UserAccountRecord), storeType.GenericTypeArguments);
        Assert.Contains(typeof(PlatformDbContext), storeType.GenericTypeArguments);
        Assert.Contains(typeof(Guid), storeType.GenericTypeArguments);

        // UserManager rezolvohet; RoleManager NUK ekziston si regjistrim.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<UserManager<UserAccountRecord>>());
    }

    [Fact]
    public void Menaxheret_OpenIddict_Core_rezolvohen_me_entitete_Guid()
    {
        using var provider = BuildServices().BuildServiceProvider();
        using var scope = provider.CreateScope();

        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        var authorizationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var tokenManager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();

        // ReplaceDefaultEntities<Guid>() → menaxherët punojnë mbi entitetet
        // EF me çelës Guid (jo default-et me string key).
        Assert.Equal(typeof(OpenIddictEntityFrameworkCoreApplication<Guid>),
            applicationManager.GetType().GenericTypeArguments.Single());
        Assert.Equal(typeof(OpenIddictEntityFrameworkCoreScope<Guid>),
            scopeManager.GetType().GenericTypeArguments.Single());
        Assert.Equal(typeof(OpenIddictEntityFrameworkCoreAuthorization<Guid>),
            authorizationManager.GetType().GenericTypeArguments.Single());
        Assert.Equal(typeof(OpenIddictEntityFrameworkCoreToken<Guid>),
            tokenManager.GetType().GenericTypeArguments.Single());
    }
}
