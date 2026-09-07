using Idata.Platform.Modules.Identity.Application;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Idata.Platform.Modules.Identity.Infrastructure;

/// <summary>Forma e header-it ORIGJINAL `Authorization` të një kërkese UserInfo (klasifikim PUR, para autentikimit).</summary>
internal enum AuthorizationHeaderShape
{
    /// <summary>Asnjë vlerë jo-bosh — kredencial i munguar (challenge 401 i bibliotekës).</summary>
    Absent,

    /// <summary>Saktësisht një vlerë me skemën `Bearer` dhe një kredencial të vetëm — vlefshmërinë e vendos biblioteka (200/401).</summary>
    SingleBearer,

    /// <summary>Një vlerë e vetme me skemë tjetër (p.sh. `Basic`) — për UserInfo = kredencial bearer i munguar (401).</summary>
    OtherScheme,

    /// <summary>Kredencial i përsëritur, listë e bashkuar ose skema të përziera — transport i paqartë (400 `invalid_request`).</summary>
    Ambiguous,
}

/// <summary>
/// Kufi i NGUSHTË i transportit të credential-it te UserInfo (B.2c — correction C1, plotësuar në C2).
///
/// **Profil LOKAL sigurie** (nuk pretendohet ndalim universal i OIDC): për këtë authority
/// credential-i i UserInfo pranohet VETËM si NJË kredencial `Bearer` në header-in `Authorization`.
/// Refuzohen me `invalid_request` (400), PARA autentikimit dhe pa zgjedhje “i pari/i fundit fiton”:
///  - prania e parametrit `access_token` në query-string ose në form body — **sipas PRANISË, jo
///    sipas vlerës**: vlerë bosh, e dyfishtë ose bashkëekzistencë me një header valid refuzohen njësoj;
///  - header `Authorization` i PËRSËRITUR (më shumë se një vlerë, të njëjtat ose të ndryshme), i
///    BASHKUAR (një vlerë e vetme me listë të ndarë me presje — nënshkrimi i bashkimit RFC 9110 §5.3;
///    një `b64token` i RFC 6750 §2.1 nuk përmban presje) ose me SKEMA TË PËRZIERA (p.sh. `Basic` +
///    `Bearer`, që arrijnë si vlera të shumëfishta).
/// Një header i VETËM `Bearer` me kredencial të pavlefshëm NUK është transport i shumëfishtë: mbetet
/// refuzimi ekzistues 401 `invalid_token` i bibliotekës; skema `Bearer` krahasohet case-insensitive
/// (RFC 9110 §11.1). Kontrolli lexon KANALET ORIGJINALE të kërkesës HTTP (header-at, query dhe form),
/// jo mesazhin e normalizuar ku header-i mund ta ketë mbishkruar trupin ose ku vlerat e shumëfishta
/// janë bashkuar tashmë.
///
/// **Rendi:** regjistrohet në fazën `ValidateUserInfoRequest` PARA
/// <c>OpenIddictServerHandlers.UserInfo.ValidateAccessTokenParameter</c> (dhe rrjedhimisht para
/// autentikimit), me `SetOrder` të derivuar nga descriptor-i REAL i bibliotekës — jo numër magjik.
/// Vlen VETËM për UserInfo: te `/connect/token` parametrat e grant-it në body janë normalë.
///
/// **Lidhja me politikën e log-eve:** refuzimi nuk e zhbën një rresht log-u të shkruar më herët nga
/// një reverse proxy i jashtëm për një kredencial në URL — ai mbetet kufi i deklaruar; log-et e
/// host-it që kontrollojmë (`Microsoft.AspNetCore.Hosting.Diagnostics`) mbulohen nga
/// <see cref="IdentityProtocolLoggingPolicy"/> (C2) PARA shkrimit te sink-et.
/// </summary>
internal static class UserInfoTransportGuard
{
    /// <summary>Emri i parametrit të ndaluar në query/body për UserInfo.</summary>
    internal const string ForbiddenParameter = "access_token";

    /// <summary>Kanali i vetëm i lejuar (informativ për përshkrimin e gabimit).</summary>
    internal const string AllowedTransport = "Authorization: Bearer";

    /// <summary>Skema e vetme e pranuar (krahasim case-insensitive).</summary>
    internal const string BearerScheme = "Bearer";

    /// <summary>Klasifikim PUR (i testueshëm pa HTTP): a ka kredencial jashtë header-it?</summary>
    internal static bool HasForbiddenTransport(bool queryHasParameter, bool formHasParameter)
        => queryHasParameter || formHasParameter;

    /// <summary>
    /// Klasifikim PUR i vlerave ORIGJINALE të header-it `Authorization` (një hyrje për çdo vlerë
    /// header-i siç e ka marrë host-i). Vlerat bosh/vetëm hapësira injorohen (një rresht i përsëritur
    /// BOSH nuk është kredencial). Rendi: (1) më shumë se një vlerë → `Ambiguous`; (2) një vlerë e vetme
    /// që është LISTË e bashkuar — ≥ 2 segmente të ndarë me presje, secili në formën
    /// `skemë` + hapësirë + kredencial (nënshkrimi i bashkimit RFC 9110 §5.3) — → `Ambiguous`, PAVARËSISHT
    /// nga skema e parë (`Basic x, Bearer y` dhe `Bearer y, Basic x` njësoj); auth-param-e me presje të
    /// një skeme tjetër (p.sh. `Digest a="1", b="2"`) NUK janë listë kredencialesh → `OtherScheme`;
    /// (3) skema e vetme `Bearer` (case-insensitive, ndarës hapësirë ose TAB) → `SingleBearer` — vlefshmëria
    /// e kredencialit (edhe një presje e mbetur brenda tij, `Bearer a,b`) i takon bibliotekës (401);
    /// (4) skemë tjetër → `OtherScheme`. Nuk parsohet asnjë JWT, nuk validohet asnjë kredencial.
    /// </summary>
    internal static AuthorizationHeaderShape ClassifyAuthorizationHeader(IReadOnlyList<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        string? single = null;
        var count = 0;
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            count++;
            single = value;
        }

        if (count == 0)
        {
            return AuthorizationHeaderShape.Absent;
        }

        if (count > 1)
        {
            return AuthorizationHeaderShape.Ambiguous; // i përsëritur ose skema të përziera (vlera të ndara)
        }

        var trimmed = single!.Trim();
        if (trimmed.Contains(',') && CountCredentialSegments(trimmed) > 1)
        {
            return AuthorizationHeaderShape.Ambiguous; // listë e bashkuar në një vlerë, cilado skemë e parë
        }

        var separator = trimmed.IndexOfAny([' ', '\t']);
        var scheme = separator < 0 ? trimmed : trimmed[..separator];
        return string.Equals(scheme, BearerScheme, StringComparison.OrdinalIgnoreCase)
            ? AuthorizationHeaderShape.SingleBearer
            : AuthorizationHeaderShape.OtherScheme;
    }

    /// <summary>Numri i segmenteve `skemë` + hapësirë + kredencial në një vlerë të ndarë me presje (PUR).</summary>
    private static int CountCredentialSegments(string value)
    {
        var count = 0;
        foreach (var raw in value.Split(','))
        {
            var segment = raw.Trim();
            var separator = segment.IndexOfAny([' ', '\t']);
            if (separator > 0 && IsSchemeToken(segment.AsSpan(0, separator)) && segment[(separator + 1)..].Trim().Length > 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>`token` i RFC 9110 §5.6.2 (tchar) — emri i një skeme autentikimi.</summary>
    private static bool IsSchemeToken(ReadOnlySpan<char> candidate)
    {
        if (candidate.IsEmpty)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            var allowed = char.IsAsciiLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character);
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
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
