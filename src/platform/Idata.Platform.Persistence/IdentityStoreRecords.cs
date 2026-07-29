using Microsoft.AspNetCore.Identity;

namespace Idata.Platform.Persistence;

/// <summary>
/// Storage record i llogarisë identity (ADR-028, Increment B.1) — tipi i
/// vetëm account i store-it roleless, me çelës Guid, i mapuar te
/// `identity.user_account`. Rregullat e B.1:
///  - deriviohet nga <see cref="IdentityUser{TKey}"/> me TKey = Guid;
///  - ZERO property shtesë të shpikura — kolonat dalin nga modeli REAL i
///    ASP.NET Core Identity (asnjë lifecycle/projection field para B.3);
///  - ZERO role: konteksti mbetet IdentityUserContext (roleless) —
///    autorizimi tenant mbetet EKSKLUZIVISHT te platform.role/permission/
///    role_permission/business_membership (F1.1, të paprekura);
///  - `platform.app_user` NUK preket — projection identity→platform = B.3.
/// </summary>
public sealed class UserAccountRecord : IdentityUser<Guid>;

/// <summary>
/// Storage record i claim-it të llogarisë (ADR-028, correction i B.1) —
/// zbaton rregullin ABSOLUT "zero persisted int IDs":
///  - `IdentityUserClaim&lt;TKey&gt;.Id` (int) i framework-ut NUK ritipizohet
///    dot në mënyrë të sigurt → mbetet anëtar CLR i TRASHËGUAR, INJOROHET
///    nga EF (mapping string-based — zero kolonë int identifikuese në DB)
///    dhe çdo përdorim EKZEKUTUES i tij ndalohet nga IL architecture guard
///    në CI (nuk pretendohet compiler-level inaccessibility për kod jashtë
///    repo-s);
///  - çelësi i VETËM i persistuar = <see cref="ClaimId"/> (Guid) → kolona
///    `id` uuid — INVARIANT dy-shtresor: setter-i refuzon
///    <see cref="Guid.Empty"/> në aplikacion; CHECK constraint
///    `ck_user_claim_id_non_empty` e refuzon në PostgreSQL.
/// </summary>
public sealed class UserClaimRecord : IdentityUserClaim<Guid>
{
    private Guid claimId = Guid.NewGuid();

    public Guid ClaimId
    {
        get => claimId;
        set => claimId = value != Guid.Empty
            ? value
            : throw new ArgumentException(
                "ClaimId nuk lejohet te jete Guid.Empty — celesi UUID gjenerohet ne aplikacion dhe duhet te jete non-empty.",
                nameof(value));
    }
}
