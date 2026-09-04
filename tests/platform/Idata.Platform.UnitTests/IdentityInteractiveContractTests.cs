using Idata.Platform.Modules.Identity.Application;
using Idata.Platform.Modules.Identity.Infrastructure;
using Xunit;

namespace Idata.Platform.UnitTests;

/// <summary>
/// Vendimet PURE të kontratës së interaksionit (B.2b): validimi i return URL-së
/// (zero open redirect), parse-i fail-closed i vendimit të consent-it, kufiri i
/// hyrjes së login-it dhe validimi i kufijve të konfigurueshëm. Zero HTTP, zero
/// DB, zero protokoll — provë e izoluar e vetë rregullave.
/// </summary>
public sealed class IdentityInteractiveContractTests
{
    [Theory]
    [InlineData("/connect/authorize")]
    [InlineData("/connect/authorize?client_id=synthetic&state=a+b")]
    public void Return_url_lokale_drejt_endpointit_te_autorizimit_pranohet(string value)
        => Assert.True(InteractiveAuthorizationContract.IsAuthorizationReturnUrl(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://synthetic-attacker.localhost/connect/authorize")]
    [InlineData("//synthetic-attacker.localhost/connect/authorize")]
    [InlineData("/\\synthetic-attacker.localhost/connect/authorize")]
    [InlineData("\\/connect/authorize")]
    [InlineData("/api/platform/status")]
    [InlineData("/connect/token")]
    [InlineData("/connect/authorize#fragment")]
    [InlineData("/connect/authorize\r\nLocation: https://synthetic-attacker.localhost")]
    [InlineData("connect/authorize")]
    public void Return_url_jashte_kontrates_refuzohet(string? value)
        => Assert.False(InteractiveAuthorizationContract.IsAuthorizationReturnUrl(value));

    [Fact]
    public void Return_url_me_gjatesi_te_tepruar_refuzohet()
        => Assert.False(InteractiveAuthorizationContract.IsAuthorizationReturnUrl(
            "/connect/authorize?state=" + new string('a', InteractiveAuthorizationContract.MaxReturnUrlLength)));

    [Theory]
    [InlineData("approve", ConsentDecision.Approve)]
    [InlineData("Approve", ConsentDecision.Deny)]
    [InlineData("APPROVE", ConsentDecision.Deny)]
    [InlineData("approve ", ConsentDecision.Deny)]
    [InlineData("deny", ConsentDecision.Deny)]
    [InlineData("", ConsentDecision.Deny)]
    [InlineData(null, ConsentDecision.Deny)]
    [InlineData("true", ConsentDecision.Deny)]
    public void Vendimi_i_consent_it_eshte_fail_closed(string? value, ConsentDecision expected)
        => Assert.Equal(expected, InteractiveConsentHandler.ParseDecision(value));

    [Theory]
    [InlineData(null, "Synthetic-1")]
    [InlineData("", "Synthetic-1")]
    [InlineData("   ", "Synthetic-1")]
    [InlineData("synthetic-user", null)]
    [InlineData("synthetic-user", "")]
    public async Task Hyrja_bosh_refuzohet_pa_prekur_store_in(string? username, string? credential)
    {
        var verifier = new RecordingVerifier();
        var handler = new InteractiveSignInHandler(verifier);

        var result = await handler.HandleAsync(new InteractiveSignInCommand(username, credential), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(InteractiveSignInRejection.InvalidCredential, result.Rejection);
        Assert.Equal(0, verifier.Invocations);
    }

    [Fact]
    public async Task Hyrja_e_plote_delegohet_te_porta_e_verifikimit()
    {
        var verifier = new RecordingVerifier();
        var handler = new InteractiveSignInHandler(verifier);

        var result = await handler.HandleAsync(
            new InteractiveSignInCommand("synthetic-user", "Synthetic-1"), TestContext.Current.CancellationToken);

        Assert.Equal(1, verifier.Invocations);
        Assert.True(result.Succeeded);
        Assert.Equal("synthetic-subject", result.SubjectId);
    }

    [Fact]
    public void Rezultati_i_refuzimit_nuk_mbart_kurre_subjekt_ose_stamp()
    {
        var rejected = InteractiveSignInResult.Rejected(InteractiveSignInRejection.LockedOut);

        Assert.False(rejected.Succeeded);
        Assert.Null(rejected.SubjectId);
        Assert.Null(rejected.SessionStamp);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => InteractiveSignInResult.Rejected(InteractiveSignInRejection.None));
    }

    [Fact]
    public async Task Vendimi_i_consent_it_kerkon_subjekt_dhe_ndan_grant_nga_deny()
    {
        var gateway = new RecordingGateway();
        var handler = new InteractiveConsentHandler(gateway);

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.DecideAsync(ConsentDecision.Approve, "  ", TestContext.Current.CancellationToken));

        Assert.Equal(
            AuthorizationGrantOutcome.Granted,
            await handler.DecideAsync(ConsentDecision.Approve, "synthetic-subject", TestContext.Current.CancellationToken));
        Assert.Equal(
            AuthorizationGrantOutcome.Denied,
            await handler.DecideAsync(ConsentDecision.Deny, "synthetic-subject", TestContext.Current.CancellationToken));
        Assert.Equal(1, gateway.Grants);
        Assert.Equal(1, gateway.Denials);
    }

    // ── Kufijtë e konfigurueshëm të interaksionit (fail-closed) ──

    private static IdentityAuthorityOptions ValidOptions() => new()
    {
        Issuer = "https://localhost/",
        Audiences = ["synthetic-api"],
        Jose = new IdentityAuthorityJoseOptions
        {
            SignatureAlgorithms = ["RS256"],
            KeyManagementAlgorithms = ["RSA-OAEP"],
            ContentEncryptionAlgorithms = ["A256CBC-HS512"],
        },
        ClockSkewSeconds = 0,
    };

    [Fact]
    public void Kufijte_e_paracaktuar_te_interaksionit_jane_te_vlefshem()
    {
        Assert.Empty(IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(ValidOptions(), isDevelopment: false));

        var defaults = new IdentityInteractionOptions();
        Assert.Equal(IdentityInteractionOptions.DefaultSessionMinutes, defaults.SessionMinutes);
        Assert.Equal(IdentityInteractionOptions.DefaultMaxFailedAccessAttempts, defaults.MaxFailedAccessAttempts);
        Assert.Equal(IdentityInteractionOptions.DefaultLockoutMinutes, defaults.LockoutMinutes);
        Assert.Equal(IdentityInteractionOptions.DefaultLoginPermitLimit, defaults.LoginPermitLimit);
        Assert.Equal(IdentityInteractionOptions.DefaultLoginWindowSeconds, defaults.LoginWindowSeconds);
    }

    [Theory]
    [InlineData("SessionMinutes", 0, "interaction-session-minutes-out-of-bounds")]
    [InlineData("SessionMinutes", 1441, "interaction-session-minutes-out-of-bounds")]
    [InlineData("MaxFailedAccessAttempts", 0, "interaction-lockout-attempts-out-of-bounds")]
    [InlineData("MaxFailedAccessAttempts", 21, "interaction-lockout-attempts-out-of-bounds")]
    [InlineData("LockoutMinutes", 0, "interaction-lockout-minutes-out-of-bounds")]
    [InlineData("LockoutMinutes", 1441, "interaction-lockout-minutes-out-of-bounds")]
    [InlineData("LoginPermitLimit", 0, "interaction-login-permit-limit-out-of-bounds")]
    [InlineData("LoginPermitLimit", 1001, "interaction-login-permit-limit-out-of-bounds")]
    [InlineData("LoginWindowSeconds", 0, "interaction-login-window-seconds-out-of-bounds")]
    [InlineData("LoginWindowSeconds", 3601, "interaction-login-window-seconds-out-of-bounds")]
    public void Kufijte_jashte_intervalit_te_lejuar_e_bejne_startup_in_te_deshtoje(
        string property, int value, string expected)
    {
        var options = ValidOptions();
        switch (property)
        {
            case "SessionMinutes": options.Interaction.SessionMinutes = value; break;
            case "MaxFailedAccessAttempts": options.Interaction.MaxFailedAccessAttempts = value; break;
            case "LockoutMinutes": options.Interaction.LockoutMinutes = value; break;
            case "LoginPermitLimit": options.Interaction.LoginPermitLimit = value; break;
            default: options.Interaction.LoginWindowSeconds = value; break;
        }

        Assert.Contains(
            expected,
            IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(options, isDevelopment: false));
    }

    private sealed class RecordingVerifier : IInteractiveCredentialsVerifier
    {
        internal int Invocations { get; private set; }

        public Task<InteractiveSignInResult> VerifyAsync(
            InteractiveSignInCommand command, CancellationToken cancellationToken)
        {
            Invocations++;
            return Task.FromResult(InteractiveSignInResult.Success("synthetic-subject", "synthetic-stamp"));
        }
    }

    private sealed class RecordingGateway : IAuthorizationProtocolGateway
    {
        internal int Grants { get; private set; }

        internal int Denials { get; private set; }

        public Task<AuthorizationRequestSnapshot?> DescribeCurrentRequestAsync(CancellationToken cancellationToken)
            => Task.FromResult<AuthorizationRequestSnapshot?>(null);

        public Task<AuthorizationGrantOutcome> GrantAsync(string subjectId, CancellationToken cancellationToken)
        {
            Grants++;
            return Task.FromResult(AuthorizationGrantOutcome.Granted);
        }

        public Task<AuthorizationGrantOutcome> DenyAsync(CancellationToken cancellationToken)
        {
            Denials++;
            return Task.FromResult(AuthorizationGrantOutcome.Denied);
        }
    }
}
