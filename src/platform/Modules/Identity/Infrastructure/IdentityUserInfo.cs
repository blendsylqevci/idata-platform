using Idata.Platform.Modules.Identity.Application;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Idata.Platform.Modules.Identity.Infrastructure;

/// <summary>
/// Kufi i NGUSHTË i transportit të credential-it te UserInfo (B.2c — correction C1).
///
/// **Profil LOKAL sigurie** (nuk pretendohet ndalim universal i OIDC): për këtë authority
/// credential-i i UserInfo pranohet VETËM në header-in `Authorization: Bearer`. Prania e
/// parametrit `access_token` në query-string ose në form body refuzohet me `invalid_request`
/// — **sipas PRANISË, jo sipas vlerës**: vlerë bosh, e dyfishtë ose bashkëekzistencë me një
/// header valid refuzohen njësoj (kredencial i përsëritur / transport i paqartë nuk zgjidhet
/// me “header-i fiton”). Kontrolli lexon KANALET ORIGJINALE të kërkesës HTTP (query dhe form),
/// jo mesazhin e normalizuar ku header-i mund ta ketë mbishkruar trupin.
///
/// **Rendi:** regjistrohet në fazën `ValidateUserInfoRequest` PARA
/// <c>OpenIddictServerHandlers.UserInfo.ValidateAccessTokenParameter</c> (dhe rrjedhimisht para
/// autentikimit), me `SetOrder` të derivuar nga descriptor-i REAL i bibliotekës — jo numër magjik.
/// Vlen VETËM për UserInfo: te `/connect/token` parametrat e grant-it në body janë normalë.
///
/// **Lidhja me politikën e log-eve:** refuzimi NUK e zhbën një rresht log-u të shkruar më herët
/// nga hosting/request diagnostics ose nga një reverse proxy i jashtëm për një kredencial në URL —
/// ai mbetet kufi i deklaruar (shih <see cref="IdentityProtocolLoggingPolicy"/>); pikërisht prandaj
/// transporti kufizohet te header-i.
/// </summary>
internal static class UserInfoTransportGuard
{
    /// <summary>Emri i parametrit të ndaluar në query/body për UserInfo.</summary>
    internal const string ForbiddenParameter = "access_token";

    /// <summary>Kanali i vetëm i lejuar (informativ për përshkrimin e gabimit).</summary>
    internal const string AllowedTransport = "Authorization: Bearer";

    /// <summary>Klasifikim PUR (i testueshëm pa HTTP): a ka kredencial jashtë header-it?</summary>
    internal static bool HasForbiddenTransport(bool queryHasParameter, bool formHasParameter)
        => queryHasParameter || formHasParameter;
}

/// <summary>
/// Adapter-i i portit <see cref="IUserInfoSubjectResolver"/> (F1 Increment B.2c) —
/// owner: Identity.Infrastructure. Principal-i vjen VETËM nga skema e SERVER-it
/// OpenIddict (autentikimi i access token-it i kryer nga pipeline-i real para
/// passthrough-it te controller-i): kurrë <c>HttpContext.User</c> i papërcaktuar,
/// kurrë cookie-ja e sesionit interaktiv, kurrë skema default e API-së (validimi
/// bearer), kurrë parametra të kërkesës, kurrë dekodim tokeni në kodin tonë.
/// Fail-closed: endpoint tjetër, token i llojit tjetër (ID token si bearer),
/// scope pa <c>openid</c> ose subjekt bosh → refuzim.
/// </summary>
internal sealed class OpenIddictUserInfoSubjectResolver(IHttpContextAccessor httpContextAccessor)
    : IUserInfoSubjectResolver
{
    public async Task<UserInfoAuthentication> ResolveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null
            || httpContext.GetOpenIddictServerEndpointType() != OpenIddictServerEndpointType.UserInfo)
        {
            return UserInfoAuthentication.Unauthenticated();
        }

        // VETËM skema e serverit të protokollit — rezultati i validimit REAL të bearer-it.
        var result = await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
        if (!result.Succeeded || result.Principal is not { } principal)
        {
            return UserInfoAuthentication.Unauthenticated();
        }

        // Vetëm ACCESS token — ID token-i (ose çdo artefakt tjetër) si bearer refuzohet.
        // OpenIddict e etiketon principal-in e validuar me identifikuesin RFC 8693
        // (`urn:ietf:params:oauth:token-type:access_token`), jo me hint-in `access_token`.
        if (!string.Equals(principal.GetTokenType(), TokenTypeIdentifiers.AccessToken, StringComparison.Ordinal))
        {
            return UserInfoAuthentication.Unauthenticated();
        }

        if (!principal.HasScope(UserInfoContract.RequiredScope))
        {
            return UserInfoAuthentication.InsufficientScope();
        }

        var subject = principal.GetClaim(Claims.Subject);
        return string.IsNullOrWhiteSpace(subject)
            ? UserInfoAuthentication.Unauthenticated()
            : UserInfoAuthentication.Authenticated(subject);
    }
}
