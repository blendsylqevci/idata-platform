using System.Text;
using System.Text.Encodings.Web;
using Idata.Platform.Modules.Identity.Application;

namespace Idata.Platform.Modules.Identity.Presentation;

/// <summary>
/// Faqet server-rendered MINIMALE të authority-t (login/consent). ÇDO vlerë
/// dinamike (emri i klientit, scope-t, parametrat e kërkesës, return URL)
/// kalon nga <see cref="HtmlEncoder"/>; zero JavaScript, zero asset i jashtëm,
/// zero markup i ndërtuar nga input i pabesuar pa enkodim. Faqja e gabimit të
/// login-it është BYTE-IDENTIKE për çdo klasë refuzimi (zero enumeration).
/// </summary>
internal static class InteractiveAuthorizationPages
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Default;

    private const string Prologue =
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">"
        + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
        + "<meta name=\"robots\" content=\"noindex, nofollow\"><title>iData+ authority</title></head><body>";

    private const string Epilogue = "</body></html>";

    internal static string Login(string antiforgeryToken, string? returnUrl)
    {
        var builder = new StringBuilder(Prologue);
        builder.Append("<h1>Sign in</h1><form method=\"post\" action=\"/")
            .Append(Encoder.Encode(InteractiveAuthorizationContract.LoginPath))
            .Append("\">");
        AppendHidden(builder, InteractiveAuthorizationContract.AntiforgeryFieldName, antiforgeryToken);
        if (InteractiveAuthorizationContract.IsAuthorizationReturnUrl(returnUrl))
        {
            AppendHidden(builder, InteractiveAuthorizationContract.ReturnUrlParameter, returnUrl!);
        }

        builder.Append("<p><label for=\"u\">User</label><input id=\"u\" name=\"")
            .Append(Encoder.Encode(InteractiveAuthorizationContract.UsernameField))
            .Append("\" type=\"text\" autocomplete=\"username\" required></p>")
            .Append("<p><label for=\"c\">Credential</label><input id=\"c\" name=\"")
            .Append(Encoder.Encode(InteractiveAuthorizationContract.CredentialField))
            .Append("\" type=\"password\" autocomplete=\"current-password\" required></p>")
            .Append("<p><button type=\"submit\">Sign in</button></p></form>")
            .Append(Epilogue);
        return builder.ToString();
    }

    /// <summary>
    /// Përgjigje UNIFORME për çdo dështim login-i (subjekt i panjohur, kredencial
    /// i gabuar, llogari e bllokuar, faktor shtesë i kërkuar): i njëjti status,
    /// i njëjti trup, zero detaj për ekzistencën ose gjendjen e llogarisë.
    /// </summary>
    internal static string LoginRejected(string? returnUrl)
    {
        var builder = new StringBuilder(Prologue);
        builder.Append("<h1>Sign in</h1><p>Sign-in was not successful.</p><p><a href=\"/")
            .Append(Encoder.Encode(InteractiveAuthorizationContract.LoginPath));
        if (InteractiveAuthorizationContract.IsAuthorizationReturnUrl(returnUrl))
        {
            builder.Append('?')
                .Append(Encoder.Encode(InteractiveAuthorizationContract.ReturnUrlParameter))
                .Append('=')
                .Append(Encoder.Encode(Uri.EscapeDataString(returnUrl!)));
        }

        builder.Append("\">Try again</a></p>").Append(Epilogue);
        return builder.ToString();
    }

    internal static string SignedIn()
        => Prologue + "<h1>Signed in</h1><p>The interactive session is established.</p>" + Epilogue;

    internal static string Consent(
        AuthorizationRequestSnapshot snapshot,
        IEnumerable<KeyValuePair<string, string>> requestParameters,
        string antiforgeryToken)
    {
        var builder = new StringBuilder(Prologue);
        builder.Append("<h1>Authorization request</h1><p>Client <strong>")
            .Append(Encoder.Encode(snapshot.ClientDisplayName))
            .Append("</strong> (<code>")
            .Append(Encoder.Encode(snapshot.ClientId))
            .Append("</code>) is requesting access.</p>");

        if (snapshot.Scopes.Count > 0)
        {
            builder.Append("<ul>");
            foreach (var scope in snapshot.Scopes)
            {
                builder.Append("<li>").Append(Encoder.Encode(scope)).Append("</li>");
            }

            builder.Append("</ul>");
        }

        builder.Append("<form method=\"post\" action=\"/")
            .Append(Encoder.Encode(InteractiveAuthorizationContract.AuthorizationPath))
            .Append("\">");

        // Parametrat ORIGJINALË të kërkesës përcillen TË PANDRYSHUAR (të enkoduar):
        // serveri i rivalidon të gjithë në POST, prandaj manipulimi i tyre nuk
        // zgjeron kurrë autoritetin — thjesht bëhet kërkesë tjetër që refuzohet.
        foreach (var parameter in requestParameters)
        {
            AppendHidden(builder, parameter.Key, parameter.Value);
        }

        AppendHidden(builder, InteractiveAuthorizationContract.AntiforgeryFieldName, antiforgeryToken);
        builder.Append("<p><button type=\"submit\" name=\"")
            .Append(Encoder.Encode(InteractiveAuthorizationContract.DecisionField))
            .Append("\" value=\"")
            .Append(Encoder.Encode(InteractiveAuthorizationContract.ApproveValue))
            .Append("\">Approve</button> <button type=\"submit\" name=\"")
            .Append(Encoder.Encode(InteractiveAuthorizationContract.DecisionField))
            .Append("\" value=\"")
            .Append(Encoder.Encode(InteractiveAuthorizationContract.DenyValue))
            .Append("\">Deny</button></p></form>")
            .Append(Epilogue);
        return builder.ToString();
    }

    internal static string ProtocolContextMissing()
        => Prologue + "<h1>Authorization request</h1><p>The request is not available.</p>" + Epilogue;

    internal static string Unavailable()
        => Prologue + "<h1>Temporarily unavailable</h1><p>Please retry later.</p>" + Epilogue;

    private static void AppendHidden(StringBuilder builder, string name, string value)
        => builder.Append("<input type=\"hidden\" name=\"")
            .Append(Encoder.Encode(name))
            .Append("\" value=\"")
            .Append(Encoder.Encode(value))
            .Append("\">");
}
