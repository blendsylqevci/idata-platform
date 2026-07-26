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
    /// <summary>Rezultati i simuluar i fazës authority-lock/validation/mutation.</summary>
    public AtomicRoleMutationResult MutationResult =
        new(Applied: true, NewVersion: 1, PreviousRoleId: Guid.Empty, Denial: RoleMutationDenial.None);
    public CommandSaveOutcome SaveOutcome = CommandSaveOutcome.Saved;

    public int ReceiptLookups;
    public int MutationAttempts;
    public int Saves;
    public int Discards;
    public AtomicRoleMutation? LastMutation;
    public AuditLogEntry? AddedAudit;
    public CommandReceipt? AddedReceipt;

    public Task<CommandReceipt?> FindReceiptAsync(
        Guid businessId, string operationKey, string idempotencyKey, CancellationToken cancellationToken)
    {
        ReceiptLookups++;
        return Task.FromResult(ReceiptLookups == 1 ? Receipt : ReceiptAfterConflict);
    }

    public Task<AtomicRoleMutationResult> TryMutateRoleAsync(
        AtomicRoleMutation mutation, CancellationToken cancellationToken)
    {
        MutationAttempts++;
        LastMutation = mutation;
        return Task.FromResult(MutationResult);
    }

    public void AddAudit(AuditLogEntry entry) => AddedAudit = entry;

    public void AddReceipt(CommandReceipt receipt) => AddedReceipt = receipt;

    public Task<CommandSaveOutcome> SaveAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.FromResult(SaveOutcome);
    }

    public Task DiscardAsync(CancellationToken cancellationToken)
    {
        Discards++;
        return Task.CompletedTask;
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

    /// <summary>UoW me faze mutacioni qe LEJON mutacionin (version 0→1).</summary>
    private static FakeMembershipRoleUow UowApplying(long newVersion = 1) => new()
    {
        MutationResult = new(Applied: true, NewVersion: newVersion, PreviousRoleId: OldRoleId,
            Denial: RoleMutationDenial.None),
    };

    /// <summary>UoW me faze mutacioni qe REFUZON me arsyen e dhene.</summary>
    private static FakeMembershipRoleUow UowDenying(RoleMutationDenial denial) => new()
    {
        MutationResult = new(Applied: false, NewVersion: 0, PreviousRoleId: Guid.Empty, Denial: denial),
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
        var uow = UowApplying();
        var result = await Handler(store, uow).HandleAsync(
            Command(key: "me;injektim"), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateMembershipRoleOutcome.InvalidIdempotencyKey, result.Outcome);
        Assert.Equal(0, store.Calls);
        Assert.Equal(0, uow.ReceiptLookups + uow.MutationAttempts + uow.Saves);
    }

    [Fact]
    public async Task Pa_aktor_pa_grants_ose_pa_permission_deny_uniform_para_lookup_it()
    {
        foreach (var actor in (ActorContext[])[ActorContext.Anonymous, new(UserId, null, null), new(null, BusinessId, null)])
        {
            var uow = UowApplying();
            var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
                Command(actor: actor), TestContext.Current.CancellationToken);
            Assert.Equal(UpdateMembershipRoleOutcome.AccessDenied, result.Outcome);
            Assert.Equal(0, uow.MutationAttempts);
        }

        var noGrantsUow = UowApplying();
        var noGrants = await Handler(new FakeAccessControlStore(null), noGrantsUow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateMembershipRoleOutcome.AccessDenied, noGrants.Outcome);
        Assert.Equal(0, noGrantsUow.MutationAttempts);

        var readOnly = new ActorGrants(MembershipId, OldRoleId, ["platform.users_roles:R"]);
        var readOnlyUow = UowApplying();
        var denied = await Handler(new FakeAccessControlStore(readOnly), readOnlyUow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateMembershipRoleOutcome.AccessDenied, denied.Outcome);
        Assert.Equal(0, readOnlyUow.MutationAttempts);
    }

    // ── CAS: expectedVersion ──

    [Fact]
    public async Task Denial_VersionConflict_kthen_ConcurrencyConflict_pa_save()
    {
        var uow = UowDenying(RoleMutationDenial.VersionConflict);
        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(expectedVersion: 2), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateMembershipRoleOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Equal(0, uow.Saves);
        Assert.Equal(1, uow.Discards); // transaction-i rrezohet
        Assert.Null(uow.AddedAudit);
        Assert.Null(uow.AddedReceipt);
    }

    /// <summary>Mapping-u i ÇDO arsyeje refuzimi te kontrata publike (parity).</summary>
    [Theory]
    [InlineData(RoleMutationDenial.ActorNotAuthorized, UpdateMembershipRoleOutcome.AccessDenied)]
    [InlineData(RoleMutationDenial.SelfRoleChange, UpdateMembershipRoleOutcome.AccessDenied)]
    [InlineData(RoleMutationDenial.TargetNotAssignable, UpdateMembershipRoleOutcome.TargetNotFound)]
    [InlineData(RoleMutationDenial.VersionConflict, UpdateMembershipRoleOutcome.ConcurrencyConflict)]
    public async Task Cdo_denial_i_fazes_se_mutacionit_jep_zero_efekt(
        RoleMutationDenial denial, UpdateMembershipRoleOutcome expected)
    {
        var uow = UowDenying(denial);
        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(1, uow.MutationAttempts);
        Assert.Equal(0, uow.Saves);
        Assert.Equal(1, uow.Discards);
        Assert.Null(uow.AddedAudit);
        Assert.Null(uow.AddedReceipt);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task Mutacioni_dergohet_me_ACTOR_USER_ID_dhe_expectedVersion_te_kerkesa()
    {
        var uow = UowApplying();
        _ = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow)
            .HandleAsync(Command(expectedVersion: 7), TestContext.Current.CancellationToken);
        var sent = uow.LastMutation!;
        Assert.Equal(BusinessId, sent.BusinessId);
        Assert.Equal(UserId, sent.ActorUserId); // USER ID, jo membership id
        Assert.Equal(MembershipId, sent.MembershipId);
        Assert.Equal(NewRoleId, sent.RoleId);
        Assert.Equal(7L, sent.ExpectedVersion);
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
        var uow = UowApplying();
        uow.Receipt = StoredReceipt(fingerprint);

        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateMembershipRoleOutcome.Updated, result.Outcome);
        Assert.Equal(MembershipId, result.MembershipId);
        Assert.Equal(NewRoleId, result.RoleId);
        Assert.Equal(1L, result.Version);
        Assert.Equal(0, uow.MutationAttempts + uow.Saves);
    }

    [Fact]
    public async Task I_njejti_key_me_kerkese_tjeter_konflikt_pa_mutation()
    {
        var otherFingerprint = RequestFingerprint.ForMembershipRoleUpdate(BusinessId, UserId, MembershipId, OldRoleId, 0);
        var uow = UowApplying();
        uow.Receipt = StoredReceipt(otherFingerprint);

        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateMembershipRoleOutcome.IdempotencyConflict, result.Outcome);
        Assert.Equal(0, uow.MutationAttempts + uow.Saves);
    }

    // ── Sukses: mutation + audit i redaktuar/i lidhur + receipt ──

    [Fact]
    public async Task Sukses_mutation_audit_i_lidhur_dhe_receipt_ne_nje_save()
    {
        var uow = UowApplying();
        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateMembershipRoleOutcome.Updated, result.Outcome);
        Assert.Equal(NewRoleId, result.RoleId);
        Assert.Equal(1L, result.Version);
        Assert.Equal(1, uow.MutationAttempts);
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
    public async Task Denial_TargetNotAssignable_nuk_prodhon_kurre_efekt()
    {
        // Adapteri e kthen NJË denial të vetëm `TargetNotAssignable` për
        // cross-tenant, inekzistent, disabled DHE IsSystem — pra kontrata
        // publike nuk mund t'i dallojë. Parity-n REALE mbi DB e provon
        // `AtomicAuthorizationTests` (integration); këtu provohet vetëm se
        // ky denial nuk prodhon kurrë efekt.
        var uow = UowDenying(RoleMutationDenial.TargetNotAssignable);
        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateMembershipRoleOutcome.TargetNotFound, result.Outcome);
        Assert.Equal(0, uow.Saves);
        Assert.Null(uow.AddedAudit);
        Assert.Null(uow.AddedReceipt);
    }

    [Fact]
    public async Task Humbesi_i_gares_se_idempotences_merr_rezultatin_e_fituesit()
    {
        var fingerprint = RequestFingerprint.ForMembershipRoleUpdate(BusinessId, UserId, MembershipId, NewRoleId, 0);
        var uow = UowApplying();
        uow.SaveOutcome = CommandSaveOutcome.ReceiptConflict;
        uow.ReceiptAfterConflict = StoredReceipt(fingerprint);

        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateMembershipRoleOutcome.Updated, result.Outcome);
        Assert.Equal(NewRoleId, result.RoleId);
        Assert.Equal(2, uow.ReceiptLookups); // para + pas garës
    }

    // ── Recovery pas humbjes së garës same-key NË FAZËN E MUTACIONIT ──
    // Humbësi që pret target row-lock merr VersionConflict pasi fituesi
    // commit-oi; kontrata kërkon NJË rilexim deterministik të receipt-it.

    [Fact]
    public async Task VersionConflict_me_receipt_te_fituesit_kthen_rezultatin_e_ruajtur()
    {
        var fingerprint = RequestFingerprint.ForMembershipRoleUpdate(BusinessId, UserId, MembershipId, NewRoleId, 0);
        var uow = UowDenying(RoleMutationDenial.VersionConflict);
        uow.ReceiptAfterConflict = StoredReceipt(fingerprint); // i dukshëm VETËM pas garës

        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateMembershipRoleOutcome.Updated, result.Outcome);
        Assert.Equal(NewRoleId, result.RoleId);
        Assert.Equal(1L, result.Version);
        Assert.Equal(2, uow.ReceiptLookups); // fillestar + rileximi i recovery-t
        Assert.Equal(1, uow.Discards);       // rollback PARA rileximit
        Assert.Equal(0, uow.Saves);          // zero efekt nga humbësi
    }

    [Fact]
    public async Task VersionConflict_me_fingerprint_tjeter_eshte_konflikt_idempotence_jo_CAS()
    {
        var otherFingerprint = RequestFingerprint.ForMembershipRoleUpdate(BusinessId, UserId, MembershipId, OldRoleId, 0);
        var uow = UowDenying(RoleMutationDenial.VersionConflict);
        uow.ReceiptAfterConflict = StoredReceipt(otherFingerprint);

        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);

        // KURRË ConcurrencyConflict për same-key me kërkesë tjetër.
        Assert.Equal(UpdateMembershipRoleOutcome.IdempotencyConflict, result.Outcome);
        Assert.Equal(0, uow.Saves);
    }

    [Fact]
    public async Task VersionConflict_pa_receipt_mbetet_CAS_i_vertete()
    {
        var uow = UowDenying(RoleMutationDenial.VersionConflict); // ReceiptAfterConflict = null

        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateMembershipRoleOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Equal(2, uow.ReceiptLookups); // rileximi u bë dhe s'gjeti asgjë
        Assert.Equal(0, uow.Saves);
    }

    // ── Contention/transient ≠ CAS ──

    [Fact]
    public async Task TransientFailure_kthen_PlatformUnavailable_pa_rilexim_dhe_pa_efekt()
    {
        var uow = UowDenying(RoleMutationDenial.TransientFailure);

        var result = await Handler(new FakeAccessControlStore(GrantsWithUpdate), uow).HandleAsync(
            Command(), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateMembershipRoleOutcome.PlatformUnavailable, result.Outcome);
        Assert.Equal(1, uow.ReceiptLookups); // VETËM lookup-i fillestar — zero polling
        Assert.Equal(1, uow.Discards);
        Assert.Equal(0, uow.Saves);
        Assert.Null(uow.AddedAudit);
        Assert.Null(uow.AddedReceipt);
    }
}
