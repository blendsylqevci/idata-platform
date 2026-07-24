using Idata.Platform.Modules.AccessControl.Application;
using Idata.Platform.Modules.AccessControl.Domain;
using Idata.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Idata.Platform.Modules.AccessControl.Infrastructure;

/// <summary>
/// Adapter TRANSAKSIONAL i komandës — një `SaveChangesAsync` = një
/// transaction DB për (mutation + audit + receipt): dështimi i audit-it ose
/// i receipt-it rrëzon edhe mutation-in. Mapon storage records ↔ tipet e
/// AccessControl. Dallon dy garat:
///  - unique violation në `ux_command_receipt_idempotency` → ReceiptConflict;
///  - <see cref="DbUpdateConcurrencyException"/> nga concurrency token i
///    membership-it (CAS) → ConcurrencyConflict.
/// Humbësi nuk lë ASNJË efekt (EF nuk commit-oi asgjë).
/// </summary>
public sealed class EfMembershipRoleUnitOfWork(PlatformDbContext context) : IMembershipRoleUnitOfWork
{
    private BusinessMembershipRecord? tracked;

    public async Task<CommandReceipt?> FindReceiptAsync(
        Guid businessId, string operationKey, string idempotencyKey, CancellationToken cancellationToken)
    {
        var record = await context.CommandReceipts.AsNoTracking()
            .SingleOrDefaultAsync(
                receipt => receipt.BusinessId == businessId
                    && receipt.OperationKey == operationKey
                    && receipt.IdempotencyKey == idempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? null : ToDomain(record);
    }

    public async Task<BusinessMembership?> FindActiveMembershipAsync(
        Guid businessId, Guid membershipId, CancellationToken cancellationToken)
    {
        // Membership AKTIV + business AKTIV + user target AKTIV; TRACKED për mutation.
        var record = await context.BusinessMemberships
            .Where(membership => membership.BusinessId == businessId
                && membership.Id == membershipId
                && membership.Status == RecordStatus.Active)
            .Where(membership => context.Businesses.Any(business =>
                business.Id == businessId && business.Status == RecordStatus.Active))
            .Where(membership => context.AppUsers.Any(user =>
                user.Id == membership.UserId && user.Status == RecordStatus.Active))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        tracked = record;
        return new BusinessMembership
        {
            Id = record.Id,
            BusinessId = record.BusinessId,
            UserId = record.UserId,
            RoleId = record.RoleId,
            Version = record.Version,
            Status = record.Status,
            CreatedAt = record.CreatedAt,
        };
    }

    public async Task<Role?> FindRoleAsync(Guid businessId, Guid roleId, CancellationToken cancellationToken)
    {
        var record = await context.Roles.AsNoTracking()
            .SingleOrDefaultAsync(
                role => role.BusinessId == businessId && role.Id == roleId,
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? null
            : new Role
            {
                Id = record.Id,
                BusinessId = record.BusinessId,
                Name = record.Name,
                IsSystem = record.IsSystem,
                CreatedAt = record.CreatedAt,
            };
    }

    public void ApplyRoleChange(Guid newRoleId, long newVersion)
    {
        var record = tracked ?? throw new InvalidOperationException(
            "ApplyRoleChange u thirr para FindActiveMembershipAsync.");
        record.RoleId = newRoleId;
        record.Version = newVersion;
    }

    public void AddAudit(AuditLogEntry entry) => context.AuditLog.Add(new AuditLogRecord
    {
        Id = entry.Id,
        BusinessId = entry.BusinessId,
        UserId = entry.UserId,
        Entity = entry.Entity,
        EntityId = entry.EntityId,
        Action = entry.Action,
        BeforeState = entry.BeforeState,
        AfterState = entry.AfterState,
        CommandReceiptId = entry.CommandReceiptId,
        CorrelationId = entry.CorrelationId,
        EventAt = entry.EventAt,
    });

    public void AddReceipt(CommandReceipt receipt) => context.CommandReceipts.Add(new CommandReceiptRecord
    {
        Id = receipt.Id,
        BusinessId = receipt.BusinessId,
        OperationKey = receipt.OperationKey,
        IdempotencyKey = receipt.IdempotencyKey,
        RequestFingerprint = receipt.RequestFingerprint,
        ResultStatus = receipt.ResultStatus,
        ResultPayload = receipt.ResultPayload,
        CreatedAt = receipt.CreatedAt,
        CompletedAt = receipt.CompletedAt,
    });

    public async Task<CommandSaveOutcome> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return CommandSaveOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            // CAS race: concurrency token i membership-it lëvizi — zero commit.
            context.ChangeTracker.Clear();
            return CommandSaveOutcome.ConcurrencyConflict;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation
            && string.Equals(violation.ConstraintName, "ux_command_receipt_idempotency", StringComparison.Ordinal))
        {
            // Gara e idempotencës — hiq entitetet e parikuperueshme nga tracking-u;
            // context-i mbetet i përdorshëm vetëm për leximin e receipt-it të fituesit.
            context.ChangeTracker.Clear();
            return CommandSaveOutcome.ReceiptConflict;
        }
    }

    private static CommandReceipt ToDomain(CommandReceiptRecord record) => new()
    {
        Id = record.Id,
        BusinessId = record.BusinessId,
        OperationKey = record.OperationKey,
        IdempotencyKey = record.IdempotencyKey,
        RequestFingerprint = record.RequestFingerprint,
        ResultStatus = record.ResultStatus,
        ResultPayload = record.ResultPayload,
        CreatedAt = record.CreatedAt,
        CompletedAt = record.CompletedAt,
    };
}
