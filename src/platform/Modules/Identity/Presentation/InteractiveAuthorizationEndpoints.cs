using System.Security.Claims;
using System.Text;
using Idata.Platform.Modules.Identity.Application;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Idata.Platform.Modules.Identity.Presentation;

/// <summary>
/// Endpoint-et e interaksionit të authority-t (B.2b): login (GET/POST) dhe
/// consent (GET/POST mbi endpoint-in e autorizimit, i arritur me passthrough).
/// Kufijtë e mbajtur këtu janë VETËM transport: antiforgery (i imponuar nga
/// middleware-i për form binding), rate limiting i deklaruar, session cookie e
/// veçantë dhe rikthim VETËM te një return URL e validuar. Vendimet e
/// protokollit dhe verifikimi i kredencialeve jetojnë te Application/
/// Infrastructure. Asnjë nga këto rrugë nuk hyn në kontratën publike OpenAPI.
/// </summary>
public static class InteractiveAuthorizationEndpoints
{
    private const string HtmlContentType = "text/html";

    public static IEndpointRouteBuilder MapIdentityInteractiveAuthorization(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/" + InteractiveAuthorizationContract.LoginPath, GetLogin)
            .ExcludeFromDescription();

        endpoints.MapPost("/" + InteractiveAuthorizationContract.LoginPath, PostLoginAsync)
            .RequireRateLimiting(InteractiveAuthorizationContract.RateLimitPolicy)
            .ExcludeFromDescription();

        endpoints.MapGet("/" + InteractiveAuthorizationContract.AuthorizationPath, GetAuthorizeAsync)
            .ExcludeFromDescription();

        endpoints.MapPost("/" + InteractiveAuthorizationContract.AuthorizationPath, PostAuthorizeAsync)
            .ExcludeFromDescription();

        return endpoints;
    }

    private static IResult GetLogin(HttpContext httpContext, IAntiforgery antiforgery)
    {
        var returnUrl = httpContext.Request.Query[InteractiveAuthorizationContract.ReturnUrlParameter].ToString();
        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        return Html(InteractiveAuthorizationPages.Login(tokens.RequestToken ?? string.Empty, returnUrl));
    }

    private static async Task<IResult> PostLoginAsync(
        HttpContext httpContext,
        IFormCollection form,
        InteractiveSignInHandler handler,
        CancellationToken cancellationToken)
    {
        var returnUrl = form[InteractiveAuthorizationContract.ReturnUrlParameter].ToString();
        var result = await handler.HandleAsync(
            new InteractiveSignInCommand(
                form[InteractiveAuthorizationContract.UsernameField].ToString(),
                form[InteractiveAuthorizationContract.CredentialField].ToString()),
            cancellationToken);

        if (result.Rejection == InteractiveSignInRejection.Unavailable)
        {
            return Html(InteractiveAuthorizationPages.Unavailable(), StatusCodes.Status503ServiceUnavailable);
        }

        if (!result.Succeeded)
        {
            // Përgjigje UNIFORME: i njëjti status dhe i njëjti trup për çdo klasë refuzimi.
            return Html(
                InteractiveAuthorizationPages.LoginRejected(returnUrl), StatusCodes.Status401Unauthorized);
        }

        var identity = new ClaimsIdentity(InteractiveAuthorizationContract.CookieAuthenticationScheme);
        identity.AddClaim(new Claim(InteractiveAuthorizationContract.SubjectClaimType, result.SubjectId!));
        identity.AddClaim(new Claim(InteractiveAuthorizationContract.SessionStampClaimType, result.SessionStamp!));

        // Sesioni interaktiv lëshohet VETËM në skemën e vet — kurrë si bearer credential.
        await httpContext.SignInAsync(
            InteractiveAuthorizationContract.CookieAuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false });

        return InteractiveAuthorizationContract.IsAuthorizationReturnUrl(returnUrl)
            ? Results.Redirect(returnUrl)
            : Html(InteractiveAuthorizationPages.SignedIn());
    }

    private static async Task<IResult> GetAuthorizeAsync(
        HttpContext httpContext,
        InteractiveConsentHandler handler,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var authentication = await httpContext.AuthenticateAsync(
            InteractiveAuthorizationContract.CookieAuthenticationScheme);
        if (!authentication.Succeeded)
        {
            return ChallengeInteractiveSession(httpContext);
        }

        var snapshot = await handler.DescribeAsync(cancellationToken);
        if (snapshot is null)
        {
            return Html(InteractiveAuthorizationPages.ProtocolContextMissing(), StatusCodes.Status400BadRequest);
        }

        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        return Html(InteractiveAuthorizationPages.Consent(
            snapshot, RequestParameters(httpContext), tokens.RequestToken ?? string.Empty));
    }

    private static async Task<IResult> PostAuthorizeAsync(
        HttpContext httpContext,
        IFormCollection form,
        InteractiveConsentHandler handler,
        CancellationToken cancellationToken)
    {
        var authentication = await httpContext.AuthenticateAsync(
            InteractiveAuthorizationContract.CookieAuthenticationScheme);
        var subject = authentication.Succeeded
            ? authentication.Principal?.FindFirst(InteractiveAuthorizationContract.SubjectClaimType)?.Value
            : null;
        if (string.IsNullOrEmpty(subject))
        {
            return ChallengeInteractiveSession(httpContext);
        }

        var outcome = await handler.DecideAsync(
            InteractiveConsentHandler.ParseDecision(form[InteractiveAuthorizationContract.DecisionField].ToString()),
            subject,
            cancellationToken);

        // Përgjigjen e protokollit (redirect me `code` ose me `error`) e shkruan
        // vetë kufiri i protokollit; transporti nuk e mbishkruan kurrë.
        return outcome == AuthorizationGrantOutcome.Unavailable
            ? Html(InteractiveAuthorizationPages.Unavailable(), StatusCodes.Status503ServiceUnavailable)
            : Results.Empty;
    }

    /// <summary>Rikthimi bëhet VETËM te kërkesa aktuale e autorizimit (zero open redirect).</summary>
    private static IResult ChallengeInteractiveSession(HttpContext httpContext)
    {
        var target = httpContext.Request.Path + httpContext.Request.QueryString;
        var properties = new AuthenticationProperties();
        if (InteractiveAuthorizationContract.IsAuthorizationReturnUrl(target))
        {
            properties.RedirectUri = target;
        }

        return Results.Challenge(properties, [InteractiveAuthorizationContract.CookieAuthenticationScheme]);
    }

    /// <summary>Parametrat e kërkesës aktuale, pa fushat e transportit të formularit.</summary>
    private static IEnumerable<KeyValuePair<string, string>> RequestParameters(HttpContext httpContext)
    {
        foreach (var parameter in httpContext.Request.Query)
        {
            if (string.Equals(parameter.Key, InteractiveAuthorizationContract.AntiforgeryFieldName, StringComparison.Ordinal)
                || string.Equals(parameter.Key, InteractiveAuthorizationContract.DecisionField, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var value in parameter.Value)
            {
                yield return new KeyValuePair<string, string>(parameter.Key, value ?? string.Empty);
            }
        }
    }

    private static IResult Html(string content, int statusCode = StatusCodes.Status200OK)
        => Results.Text(content, HtmlContentType, Encoding.UTF8, statusCode);
}
