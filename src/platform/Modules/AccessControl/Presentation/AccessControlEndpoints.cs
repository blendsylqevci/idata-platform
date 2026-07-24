using System.Data.Common;
using Idata.Platform.Modules.AccessControl.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Idata.Platform.Modules.AccessControl.Presentation;

/// <summary>Claim types PROVIDER-NEUTRAL (ClaimsPrincipal = primitivi i vetëm).
/// Production host-i NUK regjistron asnjë authentication scheme → principal
/// bosh → deny-by-default; override lejohet VETËM në integration-test
/// assembly (WebApplicationFactory me scheme-in e vet të testit).</summary>
public static class ActorClaimTypes
{
    public const string UserId = "idata:platform:user_id";
    public const string BusinessId = "idata:platform:business_id";
}

public sealed record AccessContextResponse(
    Guid UserId,
    Guid BusinessId,
    IReadOnlyList<string> Permissions,
    string IdentityStatus);

public sealed record UpdateMembershipRoleRequest(Guid RoleId, long ExpectedVersion);

public sealed record MembershipRoleResponse(Guid MembershipId, Guid RoleId, long Version, string Status);

/// <summary>
/// Endpoint-et e F1.1 — kontrata publike UNIFORME (ProblemDetails me
/// type/title/status/errorCode STABILË; vetëm trace id ndryshon):
/// 403 ACCESS_DENIED (para lookup-it; sjellje e PËRKOHSHME provider-neutral
/// e F1.1 — principal mungon/invalid, user/business/membership disabled ose
/// permission mungon → 403 uniform; ndarja 401/403 vendoset në ADR-028/F1
/// Identity, JO kontratë finale OIDC) · 404 RESOURCE_NOT_FOUND (identik për
/// cross-tenant/disabled/inekzistent — anti existence-oracle) · 409
/// IDEMPOTENCY_CONFLICT ose CONCURRENCY_CONFLICT · 400
/// INVALID_IDEMPOTENCY_KEY · 503 PLATFORM_UNAVAILABLE (DB e paarritshme).
/// </summary>
public static class AccessControlEndpoints
{
    private const string ProblemTypeBase = "https://docs.idata.plus/problems";

    public static IEndpointRouteBuilder MapAccessControl(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/platform/access-context", GetAccessContextAsync)
            .WithName("GetAccessContext")
            .WithSummary("Konteksti i qasjes i aktorit aktual (provider-neutral, redacted).")
            .Produces<AccessContextResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapPut("/api/platform/memberships/{membershipId:guid}/role", UpdateMembershipRoleAsync)
            .WithName("UpdateMembershipRole")
            .WithSummary("Ndryshim idempotent i rolit të një membership-i (Idempotency-Key i detyrueshëm).")
            .Produces<MembershipRoleResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> GetAccessContextAsync(
        HttpContext httpContext,
        GetAccessContextHandler handler,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler.HandleAsync(ResolveActor(httpContext), cancellationToken);
            return result is null
                ? AccessDenied()
                : Results.Ok(new AccessContextResponse(
                    result.UserId, result.BusinessId, result.PermissionKeys, result.IdentityStatus));
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            return PlatformUnavailable();
        }
    }

    private static async Task<IResult> UpdateMembershipRoleAsync(
        Guid membershipId,
        UpdateMembershipRoleRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        HttpContext httpContext,
        UpdateMembershipRoleHandler handler,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler.HandleAsync(
                new UpdateMembershipRoleCommand(
                    ResolveActor(httpContext), membershipId, request.RoleId, request.ExpectedVersion, idempotencyKey),
                cancellationToken);

            return result.Outcome switch
            {
                UpdateMembershipRoleOutcome.Updated => Results.Ok(new MembershipRoleResponse(
                    result.MembershipId!.Value, result.RoleId!.Value, result.Version!.Value, "updated")),
                UpdateMembershipRoleOutcome.InvalidIdempotencyKey => Problem(
                    StatusCodes.Status400BadRequest, "invalid-idempotency-key",
                    "Idempotency-Key i pavlefshem.", "INVALID_IDEMPOTENCY_KEY"),
                UpdateMembershipRoleOutcome.AccessDenied => AccessDenied(),
                UpdateMembershipRoleOutcome.TargetNotFound => Problem(
                    StatusCodes.Status404NotFound, "resource-not-found",
                    "Burimi nuk u gjet.", "RESOURCE_NOT_FOUND"),
                UpdateMembershipRoleOutcome.IdempotencyConflict => Problem(
                    StatusCodes.Status409Conflict, "idempotency-conflict",
                    "Idempotency-Key i riperdorur per kerkese tjeter.", "IDEMPOTENCY_CONFLICT"),
                UpdateMembershipRoleOutcome.ConcurrencyConflict => Problem(
                    StatusCodes.Status409Conflict, "concurrency-conflict",
                    "Version-i i pritur nuk perputhet me gjendjen aktuale.", "CONCURRENCY_CONFLICT"),
                _ => PlatformUnavailable(),
            };
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            return PlatformUnavailable();
        }
    }

    /// <summary>
    /// Aktori vjen VETËM nga principal-i i BESUAR dhe i AUTENTIKUAR — kurrë nga
    /// body/query. Claims të vlefshme në një ClaimsIdentity pa authenticationType
    /// (`IsAuthenticated == false`) NUK janë aktor i autentikuar → aktor anonim →
    /// deny-by-default.
    /// </summary>
    private static ActorContext ResolveActor(HttpContext httpContext)
    {
        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return ActorContext.Anonymous;
        }

        Guid? userId = Guid.TryParse(user.FindFirst(ActorClaimTypes.UserId)?.Value, out var parsedUser)
            ? parsedUser
            : null;
        Guid? businessId = Guid.TryParse(user.FindFirst(ActorClaimTypes.BusinessId)?.Value, out var parsedBusiness)
            ? parsedBusiness
            : null;
        return new ActorContext(userId, businessId, httpContext.TraceIdentifier);
    }

    /// <summary>DB e paarritshme/timeout → 503 i kontrolluar, zero detaje lidhjeje.</summary>
    private static bool IsInfrastructureFailure(Exception ex)
        => ex is DbException or TimeoutException
           || ex.InnerException is DbException
           || ex is InvalidOperationException { InnerException: DbException };

    private static IResult AccessDenied()
        => Problem(StatusCodes.Status403Forbidden, "access-denied", "Qasja refuzohet.", "ACCESS_DENIED");

    private static IResult PlatformUnavailable()
        => Problem(StatusCodes.Status503ServiceUnavailable, "platform-unavailable",
            "Platforma e padisponueshme perkohesisht.", "PLATFORM_UNAVAILABLE");

    private static IResult Problem(int status, string typeSlug, string title, string errorCode)
        => Results.Problem(
            statusCode: status,
            type: $"{ProblemTypeBase}/{typeSlug}",
            title: title,
            extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}
