using System.Text.Json.Serialization;
using Idata.Platform.Modules.Identity.Application;
using Idata.Platform.Modules.Identity.Application.Features.GetUserInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Idata.Platform.Modules.Identity.Presentation.Controllers;

/// <summary>
/// Endpoint-i UserInfo (OIDC Core §5.3) — F1 Increment B.2c. Thin controller
/// (ADR-029): <c>Mediator.Send</c> → mapim në kontratën HTTP. GET dhe POST
/// (§5.3.1); përgjigjja e suksesit përmban VETËM <c>sub</c>, pa cache.
/// Autentikimi vjen nga pipeline-i REAL i protokollit (passthrough i owner-it
/// Infrastructure): kërkesa arrin këtu VETËM pas validimit të access token-it;
/// çdo rezultat tjetër i portit refuzohet fail-closed (401/403, kurrë 200).
/// Endpoint PROTOKOLLI: jashtë dokumentit të business API `platform-v1`
/// (<see cref="ApiExplorerSettingsAttribute"/>), i pranishëm në discovery dhe
/// në inventarin real të endpoint-eve.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class UserInfoController(Mediator.IMediator mediator) : BaseApiController(mediator)
{
    [HttpGet("/" + UserInfoContract.Path)]
    [HttpPost("/" + UserInfoContract.Path)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var response = await Mediator.Send(new GetUserInfoQuery(), cancellationToken).ConfigureAwait(false);

        // Të dhëna përdoruesi: kurrë të cache-uara (RFC 6750 §5.3 / OIDC Core §5.3).
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        switch (response.Outcome)
        {
            case UserInfoAuthenticationOutcome.Authenticated when !string.IsNullOrWhiteSpace(response.Subject):
                return new JsonResult(new UserInfoResponse(response.Subject));

            case UserInfoAuthenticationOutcome.InsufficientScope:
                Response.Headers.WWWAuthenticate =
                    $"Bearer error=\"{UserInfoContract.InsufficientScopeError}\", scope=\"{UserInfoContract.RequiredScope}\"";
                return new JsonResult(new UserInfoError(UserInfoContract.InsufficientScopeError))
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                };

            default:
                Response.Headers.WWWAuthenticate = $"Bearer error=\"{UserInfoContract.InvalidTokenError}\"";
                return new JsonResult(new UserInfoError(UserInfoContract.InvalidTokenError))
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                };
        }
    }

    /// <summary>Përgjigjja e suksesit: VETËM subjekti (OIDC Core §5.3.2 — `sub` i detyrueshëm).</summary>
    public sealed record UserInfoResponse([property: JsonPropertyName(UserInfoContract.SubjectMember)] string Sub);

    /// <summary>Gabim RFC 6750: vetëm kodi — zero detaj, zero material tokeni.</summary>
    public sealed record UserInfoError([property: JsonPropertyName("error")] string Error);
}
