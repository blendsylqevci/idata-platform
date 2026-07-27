using Idata.Platform.Modules.AccessControl.Domain;

namespace Idata.Platform.Modules.AccessControl.Application;

/// <summary>Kontrata minimale/redacted e query-t — pa PII, pa email/emër real.</summary>
public sealed record AccessContextResult(
    Guid UserId,
    Guid BusinessId,
    IReadOnlyList<string> PermissionKeys,
    string IdentityStatus);

/// <summary>
/// QUERY `GET /api/platform/access-context` — DENY-BY-DEFAULT: pa aktor të
/// autentikuar, pa tenant aktiv ose pa membership → `null` (403 uniform).
/// Autorizimi i leximit të kontekstit VETJAK = ekzistenca e membership-it
/// aktiv (vendim i dokumentuar në F1.1 Record); permission keys vijnë nga
/// DB-ja (rol → role_permission → permission), kurrë nga kërkesa.
/// </summary>
public sealed class GetAccessContextHandler(IAccessControlStore store)
{
    /// <summary>Statusi provider-neutral — ADR-028 OPEN; asnjë pretendim Identity READY.</summary>
    public const string IdentityStatusProviderNeutral = "PROVIDER_NEUTRAL_ADR028_OPEN";

    public async Task<AccessContextResult?> HandleAsync(ActorContext actor, CancellationToken cancellationToken)
    {
        if (actor.UserId is not { } userId || actor.ActiveBusinessId is not { } businessId)
        {
            return null; // deny-by-default: pa identitet ose pa tenant aktiv
        }

        var grants = await store.GetGrantsAsync(businessId, userId, cancellationToken).ConfigureAwait(false);
        if (grants is null)
        {
            return null; // pa membership aktiv në tenant-in e kërkuar
        }

        return new AccessContextResult(
            userId,
            businessId,
            [.. grants.PermissionKeys.Order(StringComparer.Ordinal)],
            IdentityStatusProviderNeutral);
    }
}
