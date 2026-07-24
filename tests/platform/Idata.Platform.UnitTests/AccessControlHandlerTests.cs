using System.Text.Json;
using Idata.Platform.Modules.AccessControl.Application;
using Idata.Platform.Modules.AccessControl.Domain;
using Xunit;

namespace Idata.Platform.UnitTests;

/// <summary>Fake PURE i store-it — grants të paracaktuara për (business, user).</summary>
internal sealed class FakeAccessControlStore(ActorGrants? grants) : IAccessControlStore
{
    public int Calls { get; private set; }

    public Task<ActorGrants?> GetGrantsAsync(Guid businessId, Guid userId, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(grants);
    }
}

/// <summary>
/// Fake PURE i unit-of-work — regjistron ÇDO thirrje: provon që deny-paths
/// ndodhin PARA çdo lookup-i dhe që replay/konflikt nuk ekzekuton mutation.
/// </summary>
internal sealed class FakeMembershipRoleUow : IMembershipRoleUnitOfWork
{
    public CommandReceipt? Receipt;
    public CommandReceipt? ReceiptAfterConflict;
    public BusinessMembership? Membership;
    public Role? Role;
    public CommandSaveOutcome SaveOutcome = CommandSaveOutcome.Saved;

    public int ReceiptLookups;
    public int MembershipLookups;
    public int RoleLookups;
    public int Saves;
    public Guid? AppliedRoleId;
    public long? AppliedVersion;
    public AuditLogEntry? AddedAudit;
    public CommandReceipt? AddedReceipt;

    public Task<CommandReceipt?> FindReceiptAsync(
        Guid businessId, string operationKey, string idempotencyKey, CancellationToken cancellationToken)
    {
        ReceiptLookups++;
        return Task.FromResult(ReceiptLookups == 1 ? Receipt : ReceiptAfterConflict);
    }

    public Task<BusinessMembership?> FindActiveMembershipAsync(
        Guid businessId, Guid membershipId, CancellationToken cancellationToken)
    {
        MembershipLookups++;
        return Task.FromResult(Membership is { } found && found.BusinessId == businessId && found.Id == membershipId
            ? found
            : null);
    }

    public Task<Role?> FindRoleAsync(Guid businessId, Guid roleId, CancellationToken cancellationToken)
    {
        RoleLookups++;
        return Task.FromResult(Role is { } found && found.BusinessId == businessId && found.Id == roleId
            ? found
            : null);
    }

    public void ApplyRoleChange(Guid newRoleId, long newVersion)
    {
        AppliedRoleId = newRoleId;
        AppliedVersion = newVersion;
    }

    public void AddAudit(AuditLogEntry entry) => AddedAudit = entry;

    public void AddReceipt(CommandReceipt receipt) => AddedReceipt = receipt;

    public Task<CommandSaveOutcome> SaveAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.FromResult(SaveOutcome);
    }
}

/// <summary>Matricat PURE të query/command handler-ëve (§10.B) — deny-by-default,
/// validim i key-t, fingerprint, CAS, replay/konflikt, redaktim+correlation auditi.</summary>
public sealed class AccessControlHandlerTests
{
    private static readonly Guid BusinessId = Guid.Parse("aaaaaaaa-face-4ace-8ace-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("caaaaaaa-cafe-4afe-8afe-caaaaaaaaaaa");
    private static readonly Guid MembershipId = Guid.Parse("eaeaeaea-bead-4ead-8ead-eaeaeaeaeaea");
    private static readonly Guid OldRoleId = Guid.Parse("daaaaaaa-fade-4ade-8ade-daaaaaaaaaaa");
    private static readonly Guid NewRoleId = Guid.Parse("dddddaaa-fade-4ade-8ade-dddddaaaaaaa");

    private static ActorContext Actor => new(UserId, BusinessId, "korrelim-test");

    private static ActorGrants GrantsWithUpdate => new(
        MembershipId, OldRoleId, ["platform.users_roles:R", "platform.users_roles:U"]);

    private static FakeMembershipRoleUow UowWithTargets(long version = 0) => new()
    {
        Membership = new BusinessMembership
        {
            Id = MembershipId, BusinessId = BusinessId, UserId = UserId, RoleId = OldRoleId,
            Version = version, Status = EntityStatus.Active, CreatedAt = DateTimeOffset.UnixEpoch,
        },
        Role = new Role
        {
            Id = NewRoleId, BusinessId = BusinessId, Name = "rol-i-ri", IsSystem = false,
            CreatedAt = DateTimeOffset.UnixEpoch,
        },
    };

    private static UpdateMembershipRoleHandler Handler(IAccessControlStore store, IMembershipRoleUnitOfWork uow)
        => new(store, uow, TimeProvider.System);

    private static UpdateMembershipRoleCommand Command(
        long expectedVersion = 0, string? key = "key-ok", Guid? role = null, Guid? membership = null, ActorContext? actor = null)
        => new(actor ?? Actor, membership ?? MembershipId, role ?? NewRoleId, expectedVersion, key);

    // ── Idempotency-Key: matrica e validimit PURE ──

    [Theory]
    [InlineData("a")]
    [InlineData("kli-order_seven")]
    [InlineData("A_Z-xy")]
    public void Key_te_vlefshem_kalojne(string key) => Assert.True(IdempotencyKeyRules.IsValid(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("me hapesire")]
    [InlineData("me;pikepresje")]
    [InlineData("me=barazim")]
    [InlineData("me\"thonjza")]
    [InlineData("me\nrresht")]
    public void Key_te_pavlefshem_refuzohen(string? key) => Assert.False(IdempotencyKeyRules.IsValid(key));

    [Fact]
    public void Key_129_karaktere_refuzohet_128_kalon()
    {
        Assert.True(IdempotencyKeyRules.IsValid(new string('k', 128)));
        Assert.False(IdempotencyKeyRules.IsValid(new string('k', 129)));
    }

    // ── Fingerprint deterministik (incl. expectedVersion) ──

    [Fact]
    public void Fingerprint_eshte_deterministik_dhe_ndryshon_per_cdo_fushe()
    {
        var baseline = RequestFingerprint.ForMembershipRoleUpdate(BusinessId, UserId, MembershipId, NewRoleId, 0);
        Assert.Equal(baseline, RequestFingerprint.ForMembershipRoleUpdate(BusinessId, UserId, MembershipId, NewRoleId, 0));
        Assert.Equal(64, baseline.Length); // SHA-256 hex

        var other = Guid.Parse("deadbeef-dead-4bee-8eef-deadbeefaaaa");
        Assert.NotEqual(baseline, RequestFingerprint.ForMembershipRoleUpdate(other, UserId, MembershipId, NewRoleId, 0));
        Assert.NotEqual(baseline, RequestFingerprint.ForMembershipRoleUpdate(BusinessId, other, MembershipId, NewRoleId, 0));
        Assert.NotEqual(baseline, RequestFingerprint.ForMembershipRoleUpdate(BusinessId, UserId, other, NewRoleId, 0));
        Assert.NotEqual(baseline, RequestFingerprint.ForMembershipRoleUpdate(BusinessId, UserId, MembershipId, other, 0));
        // expectedVersion i ndryshëm → fingerprint i ndryshëm.
        Assert.NotEqual(baseline, RequestFingerprint.ForMembershipRoleUpdate(BusinessId, UserId, MembershipId, NewRoleId, 1));
    }

    // ── GetAccessContextHandler: deny-by-default ──

    [Fact]
    public async Task Query_pa_aktor_ose_pa_tenant_mohohet_pa_prekur_store()
    {
        var store = new FakeAccessControlStore(GrantsWithUpdate);
        var handler = new GetAccessContextHandler(store);

        Assert.Null(await handler.HandleAsync(ActorContext.Anonymous, TestContext.Current.CancellationToken));
        Assert.Null(await handler.HandleAsync(new ActorContext(UserId, null, null), TestContext.Current.CancellationToken));
        Assert.Null(await handler.HandleAsync(new ActorContext(null, BusinessId, null), TestContext.Current.CancellationToken));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Query_pa_membership_mohohet_me_grants_kthen_celesat_e_renditur()
    {
        Assert.Null(await new GetAccessContextHandler(new FakeAccessControlStore(null))
            .HandleAsync(Actor, TestContext.Current.CancellationToken));

        var granted = new ActorGrants(MembershipId, OldRoleId, ["b:U", "a:R"]);
        var result = await new GetAccessContextHandler(new FakeAccessControlStore(granted))
            .HandleAsync(Actor, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(UserId, result.UserId);
        Assert.Equal(BusinessId, result.BusinessId);
        Assert.Equal(["a:R", "b:U"], result.PermissionKeys);
        Assert.Equal(GetAccessContextHandler.IdentityStatusProviderNeutral, result.IdentityStatus);
    }

    // ── UpdateMembershipRoleHandler: deny/validim PARA lookup-it ──

    [Fact]
    public async Task Key_i_pavlefshem_kthehet_para_cdo_pune()
    {
        var store = new FakeAccessControlStore(GrantsWithUpdate);
        var uow = UowWithTargets();
        var result = await Handler(store, uow).HandleAsync(
            Command(key: "me;injektim"), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateMembershipRoleOutcome.InvalidIdempotencyKey, result.Outcome);
        Assert.Equal(0, store.Calls);
        Assert.Equal(0, uow.ReceiptLookups + uow.MembershipLookups + uow.RoleLookups + uow.Saves);
    }

    [Fact]
    public async Task Pa_aktor_pa_grants_ose_pa_permission_deny_uniform_para_lookup_it()
    {
        foreach (var actor in (ActorContext[])[ActorContext.Anonymous, new(UserId, null, null), new(null, BusinessId, null)])
        {
            var uow = UowWithTargets();
            var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
                Command(actor: actor), TestContext.Current.CancellationToken);
            Assert.Equal(UpdateMembershipRoleOutcome.AccessDenied, result.Outcome);
            Assert.Equal(0, uow.MembershipLookups + uow.RoleLookups);
        }

        var noGrantsUow = UowWithTargets();
        var noGrants = await Handler(new FakeAccessControlStore(null), noGrantsUow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateMembershipRoleOutcome.AccessDenied, noGrants.Outcome);
        Assert.Equal(0, noGrantsUow.MembershipLookups + noGrantsUow.RoleLookups);

        var readOnly = new ActorGrants(MembershipId, OldRoleId, ["platform.users_roles:R"]);
        var readOnlyUow = UowWithTargets();
        var denied = await Handler(new FakeAccessControlStore(readOnly), readOnlyUow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateMembershipRoleOutcome.AccessDenied, denied.Outcome);
        Assert.Equal(0, readOnlyUow.MembershipLookups + readOnlyUow.RoleLookups);
    }

    // ── CAS: expectedVersion ──

    [Fact]
    public async Task ExpectedVersion_i_gabuar_kthen_ConcurrencyConflict_pa_mutation()
    {
        var uow = UowWithTargets(version: 3);
        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(expectedVersion: 2), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateMembershipRoleOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Null(uow.AppliedRoleId);
        Assert.Equal(0, uow.Saves);
    }

    [Fact]
    public async Task Save_ConcurrencyConflict_perkthehet_ne_ConcurrencyConflict_pa_efekt()
    {
        var uow = UowWithTargets();
        uow.SaveOutcome = CommandSaveOutcome.ConcurrencyConflict;
        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateMembershipRoleOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Equal(1, uow.Saves);
        Assert.Equal(1, uow.ReceiptLookups); // pa lexim të dytë të receipt-it
    }

    // ── Replay / konflikt idempotence ──

    private static CommandReceipt StoredReceipt(string fingerprint) => new()
    {
        Id = Guid.CreateVersion7(),
        BusinessId = BusinessId,
        OperationKey = CommandOperations.MembershipRoleUpdate,
        IdempotencyKey = "key-ok",
        RequestFingerprint = fingerprint,
        ResultStatus = CommandReceiptStatus.Completed,
        ResultPayload = JsonSerializer.Serialize(new { MembershipId, RoleId = NewRoleId, Version = 1L }),
        CreatedAt = DateTimeOffset.UnixEpoch,
        CompletedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task Replay_i_njejte_kthen_rezultatin_e_ruajtur_pa_mutation_te_dyte()
    {
        var fingerprint = RequestFingerprint.ForMembershipRoleUpdate(BusinessId, UserId, MembershipId, NewRoleId, 0);
        var uow = UowWithTargets();
        uow.Receipt = StoredReceipt(fingerprint);

        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateMembershipRoleOutcome.Updated, result.Outcome);
        Assert.Equal(MembershipId, result.MembershipId);
        Assert.Equal(NewRoleId, result.RoleId);
        Assert.Equal(1L, result.Version);
        Assert.Equal(0, uow.MembershipLookups + uow.RoleLookups + uow.Saves);
    }

    [Fact]
    public async Task I_njejti_key_me_kerkese_tjeter_konflikt_pa_mutation()
    {
        var otherFingerprint = RequestFingerprint.ForMembershipRoleUpdate(BusinessId, UserId, MembershipId, OldRoleId, 0);
        var uow = UowWithTargets();
        uow.Receipt = StoredReceipt(otherFingerprint);

        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateMembershipRoleOutcome.IdempotencyConflict, result.Outcome);
        Assert.Equal(0, uow.MembershipLookups + uow.RoleLookups + uow.Saves);
    }

    // ── Sukses: mutation + audit i redaktuar/i lidhur + receipt ──

    [Fact]
    public async Task Sukses_mutation_audit_i_lidhur_dhe_receipt_ne_nje_save()
    {
        var uow = UowWithTargets();
        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateMembershipRoleOutcome.Updated, result.Outcome);
        Assert.Equal(NewRoleId, uow.AppliedRoleId);
        Assert.Equal(1L, uow.AppliedVersion);
        Assert.Equal(1L, result.Version);
        Assert.Equal(1, uow.Saves);

        var audit = uow.AddedAudit!;
        Assert.Equal(BusinessId, audit.BusinessId);
        Assert.Equal(UserId, audit.UserId);
        Assert.Equal(AuditEntities.BusinessMembership, audit.Entity);
        Assert.Equal(MembershipId, audit.EntityId);
        Assert.Equal(PermissionActions.Update, audit.Action);
        // Lidhja audit → receipt DHE correlation server-generated (jo default).
        Assert.Equal(uow.AddedReceipt!.Id, audit.CommandReceiptId);
        Assert.NotEqual(Guid.Empty, audit.CorrelationId);
        using (var before = JsonDocument.Parse(audit.BeforeState!))
        using (var after = JsonDocument.Parse(audit.AfterState!))
        {
            Assert.Equal(["RoleId"], before.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal(OldRoleId, before.RootElement.GetProperty("RoleId").GetGuid());
            Assert.Equal(NewRoleId, after.RootElement.GetProperty("RoleId").GetGuid());
        }

        var receipt = uow.AddedReceipt!;
        Assert.Equal(CommandOperations.MembershipRoleUpdate, receipt.OperationKey);
        Assert.Equal("key-ok", receipt.IdempotencyKey);
        Assert.Equal(CommandReceiptStatus.Completed, receipt.ResultStatus);
        Assert.NotNull(receipt.CompletedAt);
    }

    [Fact]
    public async Task Target_inekzistent_ose_cross_tenant_kthen_te_njejtin_TargetNotFound()
    {
        var crossTenantUow = UowWithTargets();
        crossTenantUow.Membership!.BusinessId = Guid.Parse("bbbbbbbb-face-4ace-8ace-bbbbbbbbbbbb");
        var crossTenant = await Handler(new FakeAccessControlStore(GrantsWithUpdate), crossTenantUow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateMembershipRoleOutcome.TargetNotFound, crossTenant.Outcome);

        var missingRoleUow = UowWithTargets();
        missingRoleUow.Role = null;
        var missingRole = await Handler(new FakeAccessControlStore(GrantsWithUpdate), missingRoleUow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateMembershipRoleOutcome.TargetNotFound, missingRole.Outcome);

        Assert.Equal(0, crossTenantUow.Saves + missingRoleUow.Saves);
    }

    [Fact]
    public async Task Humbesi_i_gares_se_idempotences_merr_rezultatin_e_fituesit()
    {
        var fingerprint = RequestFingerprint.ForMembershipRoleUpdate(BusinessId, UserId, MembershipId, NewRoleId, 0);
        var uow = UowWithTargets();
        uow.SaveOutcome = CommandSaveOutcome.ReceiptConflict;
        uow.ReceiptAfterConflict = StoredReceipt(fingerprint);

        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateMembershipRoleOutcome.Updated, result.Outcome);
        Assert.Equal(NewRoleId, result.RoleId);
        Assert.Equal(2, uow.ReceiptLookups); // para + pas garës
    }
}
