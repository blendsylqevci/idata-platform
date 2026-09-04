namespace Idata.Platform.Modules.Identity.Application;

/// <summary>
/// Porta e verifikimit të kredencialeve — implementohet VETËM në Infrastructure
/// mbi store-in real ASP.NET Core Identity (lockout i aktivizuar). Application-i
/// nuk njeh as provider, as store, as hashing.
/// </summary>
public interface IInteractiveCredentialsVerifier
{
    Task<InteractiveSignInResult> VerifyAsync(InteractiveSignInCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Porta e kufirit të PROTOKOLLIT (OAuth 2.0) — implementohet VETËM në
/// Infrastructure mbi pipeline-in real OpenIddict. Vendimi çfarë jepet nuk vjen
/// KURRË nga formulari: `GrantAsync` lëshon kod për scope-t e kërkesës AKTUALE
/// të validuar nga serveri, prandaj consent-i nuk mund të shtojë scope.
/// </summary>
public interface IAuthorizationProtocolGateway
{
    /// <summary>Kërkesa aktuale e autorizimit, ose `null` nëse konteksti i protokollit mungon.</summary>
    Task<AuthorizationRequestSnapshot?> DescribeCurrentRequestAsync(CancellationToken cancellationToken);

    /// <summary>Aprovim: kod autorizimi për subjektin dhe scope-t EKZAKTE të kërkesës aktuale.</summary>
    Task<AuthorizationGrantOutcome> GrantAsync(string subjectId, CancellationToken cancellationToken);

    /// <summary>Refuzim i shprehur: përgjigje standarde OAuth `access_denied`, zero kod.</summary>
    Task<AuthorizationGrantOutcome> DenyAsync(CancellationToken cancellationToken);
}
