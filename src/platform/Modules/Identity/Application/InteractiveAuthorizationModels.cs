namespace Idata.Platform.Modules.Identity.Application;

/// <summary>
/// Klasa e refuzimit — VETËM për telemetri/diagnostikë të brendshme. Transporti
/// e kthen TË NJËJTËN përgjigje për çdo klasë refuzimi (zero user enumeration):
/// subjekt i panjohur, kredencial i gabuar, llogari e bllokuar dhe llogari që
/// kërkon faktor shtesë janë të padallueshme nga jashtë.
/// </summary>
public enum InteractiveSignInRejection
{
    None = 0,
    UnknownSubject,
    InvalidCredential,
    LockedOut,
    AdditionalFactorRequired,
    NotPermitted,
    Unavailable,
}

/// <summary>Kredencialet e paraqitura nga formulari i login-it (asnjëherë të logohen).</summary>
public sealed record InteractiveSignInCommand(string? Username, string? Credential);

/// <summary>
/// Rezultati i verifikimit: në sukses mbart VETËM identifikuesin e subjektit dhe
/// stamp-in e sesionit (zero PII, zero material kredenciali).
/// </summary>
public sealed class InteractiveSignInResult
{
    private InteractiveSignInResult(bool succeeded, string? subjectId, string? sessionStamp, InteractiveSignInRejection rejection)
    {
        Succeeded = succeeded;
        SubjectId = subjectId;
        SessionStamp = sessionStamp;
        Rejection = rejection;
    }

    public bool Succeeded { get; }

    public string? SubjectId { get; }

    public string? SessionStamp { get; }

    public InteractiveSignInRejection Rejection { get; }

    public static InteractiveSignInResult Success(string subjectId, string sessionStamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionStamp);
        return new InteractiveSignInResult(true, subjectId, sessionStamp, InteractiveSignInRejection.None);
    }

    public static InteractiveSignInResult Rejected(InteractiveSignInRejection rejection)
        => rejection == InteractiveSignInRejection.None
            ? throw new ArgumentOutOfRangeException(nameof(rejection))
            : new InteractiveSignInResult(false, null, null, rejection);
}

/// <summary>Vendimi i shprehur i përdoruesit — çdo vlerë jo-`approve` = `Deny` (fail-closed).</summary>
public enum ConsentDecision
{
    Deny = 0,
    Approve = 1,
}

/// <summary>
/// Fotografi NEUTRALE e kërkesës aktuale të autorizimit për faqen e consent-it:
/// vetëm identiteti i klientit dhe scope-t E KËRKUARA (asnjë sekret, asnjë
/// parametër protokolli i interpretuar nga UI-ja).
/// </summary>
public sealed record AuthorizationRequestSnapshot(
    string ClientId, string ClientDisplayName, IReadOnlyList<string> Scopes);

/// <summary>Rezultati i vendimit të consent-it në kufirin e protokollit.</summary>
public enum AuthorizationGrantOutcome
{
    Granted = 0,
    Denied = 1,
    Unavailable = 2,
}
