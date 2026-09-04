namespace Idata.Platform.Modules.Identity.Application;

/// <summary>
/// Kontrata e PËRBASHKËT dhe product-neutral e interaksionit (B.2b): emrat e
/// skemës/cookie-t, path-et, fushat e formularëve dhe tipet e claims-ave që
/// Presentation-i (transport) dhe Infrastructure-i (protokoll/store) i lexojnë
/// nga NJË burim i vetëm. Zero varësi ndaj ASP.NET Core, OpenIddict ose ASP.NET
/// Core Identity; zero client/audience/scope/rol specifik produkti.
/// </summary>
public static class InteractiveAuthorizationContract
{
    /// <summary>Skema e VEÇANTË e session cookie-t interaktive — kurrë skema default e API-së.</summary>
    public const string CookieAuthenticationScheme = "Idata.Identity.Interaction";

    /// <summary>Prefiksi `__Host-` kërkon Secure + Path=/ + zero Domain (kufi i imponuar nga browser-i).</summary>
    public const string CookieName = "__Host-idata-identity-interaction";

    public const string AntiforgeryCookieName = "__Host-idata-identity-antiforgery";

    public const string AntiforgeryFieldName = "__RequestVerificationToken";

    /// <summary>Endpoint-i i autorizimit (i njëjti path që konfigurohet te OpenIddict Server).</summary>
    public const string AuthorizationPath = "connect/authorize";

    /// <summary>Sipërfaqja interaktive e login-it (authority UI teknik, JO UI produkti).</summary>
    public const string LoginPath = "identity/login";

    public const string ReturnUrlParameter = "returnUrl";

    public const string UsernameField = "username";

    /// <summary>Emri i fushës së kredencialit (i shmangur emërtimi që aktivizon privacy hook-un).</summary>
    public const string CredentialField = "credential";

    public const string DecisionField = "decision";

    public const string ApproveValue = "approve";

    public const string DenyValue = "deny";

    /// <summary>Subjekti i sesionit interaktiv (claim privat, JO `sub` i protokollit).</summary>
    public const string SubjectClaimType = "idata:identity:subject";

    /// <summary>Stamp-i i sigurisë i lidhur me sesionin — revokim i menjëhershëm në ndryshim.</summary>
    public const string SessionStampClaimType = "idata:identity:session_stamp";

    /// <summary>Politika e rate-limit-it për POST-in e login-it.</summary>
    public const string RateLimitPolicy = "idata-identity-interactive-login";

    /// <summary>Gjatësia maksimale e pranuar e një return URL-je (mbrojtje ndaj abuzimit).</summary>
    public const int MaxReturnUrlLength = 2048;

    /// <summary>
    /// Vendim PURE dhe fail-closed për destinacionin e rikthimit pas login-it:
    /// pranohet VETËM një URL LOKALE, pa authority/scheme, pa fragment, pa
    /// karaktere kontrolli dhe me path-in EKZAKT të endpoint-it të autorizimit.
    /// Çdo gjë tjetër (absolute, protocol-relative, backslash, path tjetër)
    /// refuzohet — pra `open redirect` është strukturalisht i pamundur.
    /// </summary>
    public static bool IsAuthorizationReturnUrl(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxReturnUrlLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsControl(character) || character == '\\')
            {
                return false;
            }
        }

        if (value[0] != '/' || (value.Length > 1 && value[1] == '/'))
        {
            return false;
        }

        if (value.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        var separator = value.IndexOf('?', StringComparison.Ordinal);
        var path = separator < 0 ? value : value[..separator];
        return string.Equals(path, "/" + AuthorizationPath, StringComparison.Ordinal);
    }
}
