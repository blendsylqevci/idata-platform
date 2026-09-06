namespace Idata.Platform.Modules.Identity.Application;

/// <summary>
/// Rezultati i autentikimit të kërkesës UserInfo, siç e vendos adapter-i i
/// protokollit (Infrastructure) mbi principal-in e access token-it të validuar
/// nga pipeline-i REAL i authority-t. Kjo shtresë nuk dekodon token, nuk lexon
/// cookie dhe nuk beson asnjë parametër të kërkesës.
/// </summary>
public enum UserInfoAuthenticationOutcome
{
    /// <summary>Bearer i munguar, i pavlefshëm, i skaduar, i revokuar ose i llojit të gabuar.</summary>
    Unauthenticated = 0,

    /// <summary>Access token i vlefshëm, por pa scope-in <see cref="UserInfoContract.RequiredScope"/>.</summary>
    InsufficientScope = 1,

    /// <summary>Access token i vlefshëm me scope-in e kërkuar dhe subjekt jo-bosh.</summary>
    Authenticated = 2,
}

/// <summary>Rezultati i portit — subjekti ekziston VETËM kur <see cref="Outcome"/> = Authenticated.</summary>
public sealed record UserInfoAuthentication(UserInfoAuthenticationOutcome Outcome, string? Subject)
{
    public static UserInfoAuthentication Unauthenticated() => new(UserInfoAuthenticationOutcome.Unauthenticated, null);

    public static UserInfoAuthentication InsufficientScope() => new(UserInfoAuthenticationOutcome.InsufficientScope, null);

    public static UserInfoAuthentication Authenticated(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        return new UserInfoAuthentication(UserInfoAuthenticationOutcome.Authenticated, subject);
    }
}

/// <summary>
/// Port NEUTRAL (implementohet në Infrastructure): kthen subjektin e autentikuar
/// të kërkesës aktuale UserInfo nga principal-i i access token-it të validuar
/// nga pipeline-i real i protokollit — kurrë nga query/body, cookie ose një
/// <c>HttpContext.User</c> arbitrar.
/// </summary>
public interface IUserInfoSubjectResolver
{
    Task<UserInfoAuthentication> ResolveAsync(CancellationToken cancellationToken);
}
