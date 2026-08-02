using Idata.Platform.Modules.AccessControl.Application;
using Idata.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Idata.Platform.Modules.AccessControl.Infrastructure;

/// <summary>
/// READ adapter i grants — burimi i vetëm: DB. Grants jepen VETËM kur, në
/// të njëjtin business scope: app_user AKTIV, business AKTIV dhe
/// business_membership AKTIV (lifecycle nga DB, jo nga claims). Query të
/// kufizuara GJITHMONË me `business_id` direkt; asnjë lookup global. Mapon
/// storage records → tipet e Application-it.
/// </summary>
public sealed class EfAccessControlStore(PlatformDbContext context) : IAccessControlStore
{
    public async Task<ActorGrants?> GetGrantsAsync(
        Guid businessId, Guid userId, CancellationToken cancellationToken)
    {
        var membership = await context.BusinessMemberships.AsNoTracking()
            .Where(candidate => candidate.BusinessId == businessId
                && candidate.UserId == userId
                && candidate.Status == RecordStatus.Active)
            .Where(candidate => context.Businesses.Any(business =>
                business.Id == businessId && business.Status == RecordStatus.Active))
            .Where(candidate => context.PlatformAppUsers.Any(user =>
                user.Id == userId && user.Status == RecordStatus.Active))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (membership is null)
        {
            return null;
        }

        var permissionKeys = await context.RolePermissions.AsNoTracking()
            .Where(link => link.BusinessId == businessId && link.RoleId == membership.RoleId)
            .Join(
                context.Permissions.AsNoTracking(),
                link => link.PermissionId,
                permission => permission.Id,
                (link, permission) => permission.FeatureKey + ":" + permission.Action)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ActorGrants(membership.Id, membership.RoleId, permissionKeys);
    }
}
