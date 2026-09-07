namespace Idata.Platform.Modules.Identity.Application;

/// <summary>
/// Kontrata MINIMALE e UserInfo (OpenID Connect Core §5.3) — F1 Increment B.2c.
/// Neutrale ndaj transportit dhe providerit: kjo shtresë nuk njeh as OpenIddict,
/// as ASP.NET Core, as store-in. Përgjigjja e suksesit përmban VETËM subjektin e
/// autentikuar (<c>sub</c>): zero profile/email/phone/address/roles/permissions/
/// business IDs, zero security stamp, zero claim të brendshëm.
/// </summary>
public static class UserInfoContract
{
    /// <summary>Rruga e endpoint-it të protokollit (relative ndaj rrënjës së host-it).</summary>
    public const string Path = "connect/userinfo";

    /// <summary>Scope-i i vetëm që lejon përgjigjen e UserInfo (OIDC Core §5.3.1).</summary>
    public const string RequiredScope = "openid";

    /// <summary>Anëtari i vetëm i përgjigjes së suksesit.</summary>
    public const string SubjectMember = "sub";

    /// <summary>RFC 6750 §3.1 — bearer i munguar/i pavlefshëm/i skaduar/i revokuar.</summary>
    public const string InvalidTokenError = "invalid_token";

    /// <summary>RFC 6750 §3.1 — access token pa scope-in <see cref="RequiredScope"/>.</summary>
    public const string InsufficientScopeError = "insufficient_scope";
}
