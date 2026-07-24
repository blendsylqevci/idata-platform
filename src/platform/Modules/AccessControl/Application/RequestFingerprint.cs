using System.Security.Cryptography;
using System.Text;

namespace Idata.Platform.Modules.AccessControl.Application;

/// <summary>
/// Fingerprint DETERMINISTIK i kërkesës — lidh Idempotency-Key-n me
/// komandën/route-template/target/payload/expectedVersion/aktor konkret:
/// i njëjti key i ripërdorur për një kërkesë TJETËR bie në konflikt (409),
/// pa ekzekutuar mutation të dytë. SHA-256 mbi formë kanonike; zero payload
/// i papërpunuar.
/// </summary>
public static class RequestFingerprint
{
    public const string MembershipRoleRouteTemplate = "/api/platform/memberships/{membershipId}/role";

    public static string ForMembershipRoleUpdate(
        Guid businessId, Guid actorUserId, Guid membershipId, Guid roleId, long expectedVersion)
    {
        var canonical = string.Join('\n',
            "PUT",
            MembershipRoleRouteTemplate,
            $"business={businessId:D}",
            $"actor={actorUserId:D}",
            $"membership={membershipId:D}",
            $"role={roleId:D}",
            $"version={expectedVersion}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
