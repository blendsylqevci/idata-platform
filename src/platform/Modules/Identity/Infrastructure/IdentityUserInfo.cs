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
