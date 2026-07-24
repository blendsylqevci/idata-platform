using System.Text.RegularExpressions;

namespace Idata.Platform.Modules.AccessControl.Domain;

/// <summary>
/// Vokabulari canonical i veprimeve (RBAC Matrix): R Lexim · C Krijim ·
/// U Modifikim · X Anulim/Korrigjim · P Shtypje · A Aprovim; E/Cl/Ro vetëm
/// kur kërkohen. Çelësi i permission-it = "feature_key:action".
/// </summary>
public static class PermissionActions
{
    public const string Read = "R";
    public const string Create = "C";
    public const string Update = "U";
    public const string Cancel = "X";
    public const string Print = "P";
    public const string Approve = "A";
    public const string Export = "E";
    public const string Close = "Cl";
    public const string Reopen = "Ro";

    public static readonly IReadOnlyList<string> All =
        [Read, Create, Update, Cancel, Print, Approve, Export, Close, Reopen];
}

/// <summary>Feature keys e F1.1 — rreshti "Përdorues & Role (RBAC)" i matricës.</summary>
public static class PlatformFeatures
{
    public const string UsersRoles = "platform.users_roles";
}

public static class PermissionKey
{
    /// <summary>Format i qëndrueshëm "feature:action" (p.sh. platform.users_roles:U).</summary>
    public static string Of(string featureKey, string action) => $"{featureKey}:{action}";
}

/// <summary>Operation keys e receipt-ave — një për komandë të shkruar.</summary>
public static class CommandOperations
{
    public const string MembershipRoleUpdate = "membership.role.update";
}

public static class AuditEntities
{
    public const string BusinessMembership = "business_membership";
}

public static class CommandReceiptStatus
{
    public const string Completed = "completed";
}

/// <summary>
/// Rregullat PURE të Idempotency-Key (header i detyrueshëm i komandave):
/// 1–128 karaktere, VETËM [A-Za-z0-9_-]; ankorim \z (jo `$` — në .NET `$`
/// lejon newline fundor). Çdo devijim → refuzim fail-closed para çdo I/O.
/// </summary>
public static class IdempotencyKeyRules
{
    public const int MaxLength = 128;

    public static bool IsValid(string? key)
        => key is not null
           && key.Length is >= 1 and <= MaxLength
           && Regex.IsMatch(key, @"^[A-Za-z0-9_-]+\z");
}
