using System.Data.Common;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Idata.Platform.Modules.Identity.Application;
using Idata.Platform.Persistence;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Idata.Platform.Modules.Identity.Infrastructure;

/// <summary>
/// Wiring i VETËM i interaksionit të authority-t (ADR-028, Increment B.2b) —
/// konsumohet nga composition root-i pa asnjë referencë protokolli aty:
///  - **login REAL** mbi store-in ekzistues ASP.NET Core Identity
///    (<c>SignInManager.CheckPasswordSignInAsync</c> me <c>lockoutOnFailure</c>),
///    me kufij lockout-i EKSPLICITË dhe të konfigurueshëm;
///  - **session cookie e VEÇANTË** (skemë e emërtuar, `__Host-`, HttpOnly,
///    Secure=Always, SameSite=Lax, lifetime i kufizuar, sliding OFF) me validim
///    stamp-i sigurie në çdo kërkesë — kurrë skema default e API-së, kurrë
///    bearer credential;
///  - **antiforgery** për çdo POST formulari dhe **rate limiting** për POST-in
///    e login-it, të dyja me kufij të konfigurueshëm;
///  - adapterat e porteve të Application-it: verifikuesi i kredencialeve dhe
///    kufiri i protokollit OAuth 2.0 (kod autorizimi / refuzim `access_denied`).
/// Product-neutral: zero client/audience/scope/rol specifik produkti; zero
/// `openid`, ID token, UserInfo ose logout në këtë increment.
/// </summary>
public static class IdentityInteractiveAuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityInteractiveAuthorization(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var authority = configuration.GetSection(IdentityAuthorityOptions.SectionName).Get<IdentityAuthorityOptions>()
            ?? new IdentityAuthorityOptions();
        var interaction = authority.Interaction;

        // SignInManager mbi store-in ROLELESS të B.1 (zero AddRoles, zero Identity UI,
        // zero skemë cookie e Identity-t: skemat e Identity-t nuk regjistrohen kurrë).
        new IdentityBuilder(typeof(UserAccountRecord), services).AddSignInManager();
        services.Configure<IdentityOptions>(options =>
        {
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = interaction.MaxFailedAccessAttempts;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(interaction.LockoutMinutes);
        });

        services.AddHttpContextAccessor();
        services.AddScoped<IInteractiveCredentialsVerifier, IdentityInteractiveCredentialsVerifier>();
        services.AddScoped<IAuthorizationProtocolGateway, OpenIddictAuthorizationProtocolGateway>();
        services.AddScoped<InteractiveSignInHandler>();
        services.AddScoped<InteractiveConsentHandler>();

        // Eventet e cookie-t rezolvohen nga framework-u si shërbim SCOPED
        // (`EventsType`) — zero rezolvim dinamik shërbimesh në kodin tonë.
        services.AddScoped<InteractiveSessionCookieEvents>();

        services.AddAuthentication().AddCookie(
            InteractiveAuthorizationContract.CookieAuthenticationScheme,
            options =>
            {
                options.Cookie.Name = InteractiveAuthorizationContract.CookieName;
                options.Cookie.Path = "/";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.IsEssential = true;
                options.SlidingExpiration = false;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(interaction.SessionMinutes);
                options.LoginPath = "/" + InteractiveAuthorizationContract.LoginPath;
                options.AccessDeniedPath = "/" + InteractiveAuthorizationContract.LoginPath;
                options.ReturnUrlParameter = InteractiveAuthorizationContract.ReturnUrlParameter;
                options.EventsType = typeof(InteractiveSessionCookieEvents);
            });

        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = InteractiveAuthorizationContract.AntiforgeryCookieName;
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.FormFieldName = InteractiveAuthorizationContract.AntiforgeryFieldName;
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(
                InteractiveAuthorizationContract.RateLimitPolicy,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown-remote",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = interaction.LoginPermitLimit,
                        Window = TimeSpan.FromSeconds(interaction.LoginWindowSeconds),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }
}

/// <summary>
/// Adapter i verifikimit të kredencialeve mbi store-in REAL: lockout i
/// aktivizuar, llogaritë me faktor të dytë NUK marrin sesion (MFA jashtë scope-it
/// të B.2b), dhe çdo dështim infrastrukture kthehet i klasifikuar — asnjëherë si
/// sukses. Klasa e refuzimit mbetet e brendshme: transporti kthen përgjigje
/// UNIFORME (zero user enumeration).
/// </summary>
internal sealed class IdentityInteractiveCredentialsVerifier(
    SignInManager<UserAccountRecord> signInManager, UserManager<UserAccountRecord> userManager)
    : IInteractiveCredentialsVerifier
{
    public async Task<InteractiveSignInResult> VerifyAsync(
        InteractiveSignInCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var account = await userManager.FindByNameAsync(command.Username!);
            if (account is null)
            {
                return InteractiveSignInResult.Rejected(InteractiveSignInRejection.UnknownSubject);
            }

            var outcome = await signInManager.CheckPasswordSignInAsync(
                account, command.Credential!, lockoutOnFailure: true);
            if (outcome.IsLockedOut)
            {
                return InteractiveSignInResult.Rejected(InteractiveSignInRejection.LockedOut);
            }

            if (outcome.IsNotAllowed)
            {
                return InteractiveSignInResult.Rejected(InteractiveSignInRejection.NotPermitted);
            }

            if (!outcome.Succeeded)
            {
                return InteractiveSignInResult.Rejected(InteractiveSignInRejection.InvalidCredential);
            }

            // Faktori i dytë NUK është implementuar në B.2b → asnjë sesion, asnjë kod.
            if (outcome.RequiresTwoFactor || await userManager.GetTwoFactorEnabledAsync(account))
            {
                return InteractiveSignInResult.Rejected(InteractiveSignInRejection.AdditionalFactorRequired);
            }

            var stamp = await userManager.GetSecurityStampAsync(account);
            return string.IsNullOrEmpty(stamp)
                ? InteractiveSignInResult.Rejected(InteractiveSignInRejection.NotPermitted)
                : InteractiveSignInResult.Success(account.Id.ToString(), stamp);
        }
        catch (Exception exception) when (IdentityInteractiveFailure.IsInfrastructure(exception))
        {
            return InteractiveSignInResult.Rejected(InteractiveSignInRejection.Unavailable);
        }
    }
}

/// <summary>
/// Kufiri i PROTOKOLLIT: përkthen kërkesën reale OpenIddict në një fotografi
/// neutrale dhe e materializon vendimin përmes pipeline-it REAL (sign-in →
/// kod autorizimi; forbid → `access_denied`). Scope-t e dhëna janë GJITHMONË
/// scope-t e kërkesës së validuar nga serveri — formulari nuk mund t'i zgjerojë.
/// </summary>
internal sealed class OpenIddictAuthorizationProtocolGateway(
    IHttpContextAccessor httpContextAccessor,
    IOpenIddictApplicationManager applications,
    IOptionsMonitor<IdentityAuthorityOptions> options)
    : IAuthorizationProtocolGateway
{
    public async Task<AuthorizationRequestSnapshot?> DescribeCurrentRequestAsync(CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var request = httpContext?.GetOpenIddictServerRequest();
        if (httpContext is null || request?.ClientId is not { Length: > 0 } clientId)
        {
            return null;
        }

        try
        {
            var application = await applications.FindByClientIdAsync(clientId, cancellationToken);
            var displayName = application is null
                ? clientId
                : await applications.GetDisplayNameAsync(application, cancellationToken) ?? clientId;
            return new AuthorizationRequestSnapshot(clientId, displayName, [.. request.GetScopes()]);
        }
        catch (Exception exception) when (IdentityInteractiveFailure.IsInfrastructure(exception))
        {
            return null;
        }
    }

    public async Task<AuthorizationGrantOutcome> GrantAsync(string subjectId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        cancellationToken.ThrowIfCancellationRequested();

        var httpContext = httpContextAccessor.HttpContext;
        var request = httpContext?.GetOpenIddictServerRequest();
        if (httpContext is null || request is null)
        {
            return AuthorizationGrantOutcome.Unavailable;
        }

        var identity = new ClaimsIdentity(
            authenticationType: InteractiveAuthorizationContract.CookieAuthenticationScheme,
            nameType: Claims.Name,
            roleType: Claims.Role);
        identity.SetClaim(Claims.Subject, subjectId);

        // Vetëm scope-t E KËRKUARA dhe të validuara nga serveri; audiencat vijnë
        // nga konfigurimi i jashtëm (zero audience e sajuar në kod).
        identity.SetScopes(request.GetScopes());
        identity.SetResources(options.CurrentValue.Audiences);
        identity.SetDestinations(static claim => claim.Type switch
        {
            Claims.Subject => [Destinations.AccessToken],
            _ => [],
        });

        try
        {
            await httpContext.SignInAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return AuthorizationGrantOutcome.Granted;
        }
        catch (Exception exception) when (IdentityInteractiveFailure.IsInfrastructure(exception))
        {
            return AuthorizationGrantOutcome.Unavailable;
        }
    }

    public async Task<AuthorizationGrantOutcome> DenyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return AuthorizationGrantOutcome.Unavailable;
        }

        var properties = new AuthenticationProperties(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.AccessDenied,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                "The authorization was denied by the resource owner.",
        });

        await httpContext.ForbidAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, properties);
        return AuthorizationGrantOutcome.Denied;
    }
}

/// <summary>
/// Validimi i sesionit interaktiv në ÇDO kërkesë: subjekti duhet të ekzistojë
/// dhe stamp-i i sigurisë i cookie-t duhet të përputhet EKZAKT me atë të
/// store-it. Dy klasa TË NDARA (C1): (a) identitet i pavlefshëm (subjekt i
/// munguar/pa claims, stamp i ndryshuar) → principali refuzohet DHE sesioni
/// revokohet (sign-out); (b) DB e paarritshme → principali refuzohet VETËM për
/// këtë kërkesë (fail-closed, kurrë sesion i pranuar në verbëri), pa fshirë
/// cookie-n — pas rikthimit të DB-së validimi i plotë rifillon në kërkesën e
/// radhës, dhe një stamp i ndryshuar gjatë ndërprerjes kapet atëherë.
/// </summary>
internal sealed class InteractiveSessionCookieEvents(UserManager<UserAccountRecord> userManager)
    : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var subject = context.Principal?.FindFirst(InteractiveAuthorizationContract.SubjectClaimType)?.Value;
        var stamp = context.Principal?.FindFirst(InteractiveAuthorizationContract.SessionStampClaimType)?.Value;
        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(stamp))
        {
            await RejectAsync(context);
            return;
        }

        try
        {
            var account = await userManager.FindByIdAsync(subject);
            if (account is null
                || !string.Equals(await userManager.GetSecurityStampAsync(account), stamp, StringComparison.Ordinal))
            {
                await RejectAsync(context);
            }
        }
        catch (Exception exception) when (IdentityInteractiveFailure.IsInfrastructure(exception))
        {
            // Nuk dihet nëse identiteti është i vlefshëm → asnjë privilegj për këtë
            // kërkesë; sesioni NUK revokohet mbi një dështim infrastrukture.
            context.RejectPrincipal();
        }
    }

    /// <summary>Identitet i pavlefshëm: refuzim + revokim i sesionit (cookie-ja fshihet).</summary>
    private static Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        return context.HttpContext.SignOutAsync(InteractiveAuthorizationContract.CookieAuthenticationScheme);
    }
}

/// <summary>Klasifikim i vetëm i dështimeve infrastrukturore (zero detaj lidhjeje).</summary>
internal static class IdentityInteractiveFailure
{
    internal static bool IsInfrastructure(Exception exception)
        => exception is DbException or TimeoutException
           || exception.InnerException is DbException
           || exception is InvalidOperationException { InnerException: DbException };
}
