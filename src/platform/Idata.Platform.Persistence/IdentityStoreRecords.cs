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
