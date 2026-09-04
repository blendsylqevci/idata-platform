using System.Data.Common;
using System.Text;
using Idata.Platform.Modules.Identity.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Idata.Platform.Modules.Identity.Presentation;

/// <summary>
/// Dy kufij TË NGUSHTË të pipeline-it, vetëm për sipërfaqen e authority-t
/// (`/connect/*` dhe `/identity/*`) — B.2b correction C1:
///  1. **Kufiri i dështimit infrastrukturor** (para middleware-it të autentikimit,
///     që mbulon edhe endpoint-et e protokollit që i shërben vetë OpenIddict-i):
///     dështim DB/timeout → HTTP 503 i kontrolluar, trup gjenerik (JSON për
///     protokollin, HTML për interaksionin), zero detaj i brendshëm, zero material
///     kredenciali/tokeni; asnjë exception tjetër nuk maskohet.
///  2. **Sesioni interaktiv në `HttpContext.User`** (pas autentikimit, para
///     antiforgery-t), VETËM për login/consent: principali i validuar i cookie-t
///     bëhet efektiv para krijimit dhe validimit të antiforgery token-it, prandaj
///     formulari lidhet me identitetin e sesionit. Skema default (bearer OpenIddict)
///     nuk ndryshon; `/connect/token` dhe API-të nuk e shohin kurrë cookie-n.
/// </summary>
public static class InteractiveAuthorizationPipeline
{
    private static readonly PathString ProtocolPrefix =
        new("/" + InteractiveAuthorizationContract.AuthorizationPath.Split('/')[0]);

    private static readonly PathString InteractionPrefix =
        new("/" + InteractiveAuthorizationContract.LoginPath.Split('/')[0]);

    private static readonly PathString AuthorizationEndpoint =
        new("/" + InteractiveAuthorizationContract.AuthorizationPath);

    private const string ProtocolUnavailableBody =
        "{\"error\":\"temporarily_unavailable\",\"error_description\":\"The authority is temporarily unavailable.\"}";

    /// <summary>Regjistrohet PARA `UseAuthentication()`: mbështjell edhe përpunimin e protokollit nga OpenIddict.</summary>
    public static IApplicationBuilder UseIdentityInteractionFailureBoundary(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseWhen(
            context => IsProtocolOrInteractionPath(context.Request.Path),
            branch => branch.UseMiddleware<InteractionFailureBoundaryMiddleware>());
    }

    /// <summary>Regjistrohet PAS `UseAuthentication()`/`UseAuthorization()` dhe PARA `UseAntiforgery()`.</summary>
    public static IApplicationBuilder UseIdentityInteractiveSession(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseWhen(
            context => IsInteractionPath(context.Request.Path),
            branch => branch.Use(static async (context, next) =>
            {
                var authentication = await context.AuthenticateAsync(
                    InteractiveAuthorizationContract.CookieAuthenticationScheme);
                if (authentication.Succeeded && authentication.Principal is { } principal)
                {
                    context.User = principal;
                }

                await next(context);
            }));
    }

    internal static bool IsProtocolOrInteractionPath(PathString path)
        => path.StartsWithSegments(ProtocolPrefix) || path.StartsWithSegments(InteractionPrefix);

    /// <summary>Vetëm login + authorize — kurrë token endpoint-i, kurrë metadata publike.</summary>
    internal static bool IsInteractionPath(PathString path)
        => path.StartsWithSegments(InteractionPrefix)
           || path.Equals(AuthorizationEndpoint, StringComparison.OrdinalIgnoreCase);

    /// <summary>Klasifikim i vetëm dhe i NGUSHTË: vetëm dështime infrastrukture, kurrë catch-all.</summary>
    internal static bool IsInfrastructureFailure(Exception exception)
        => exception is DbException or TimeoutException
           || exception.InnerException is DbException
           || exception is InvalidOperationException { InnerException: DbException };

    private sealed class InteractionFailureBoundaryMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context, ILogger<InteractionFailureBoundaryMiddleware> logger)
        {
            try
            {
                await next(context);
            }
            catch (Exception exception) when (IsInfrastructureFailure(exception) && !context.Response.HasStarted)
            {
                // Vetëm klasa e përjashtimit dhe trace id — zero mesazh, zero connection string,
                // zero kredencial/token/kod.
                logger.LogWarning(
                    "identity-interaction: infrastructure unavailable ({ExceptionType}, trace {TraceIdentifier})",
                    exception.GetType().Name,
                    context.TraceIdentifier);

                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.CacheControl = "no-store";
                if (IsInteractionPath(context.Request.Path))
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.WriteAsync(InteractiveAuthorizationPages.Unavailable(), Encoding.UTF8);
                }
                else
                {
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync(ProtocolUnavailableBody, Encoding.UTF8);
                }
            }
        }
    }
}
