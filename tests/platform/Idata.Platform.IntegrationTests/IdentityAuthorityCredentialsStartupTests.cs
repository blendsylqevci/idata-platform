using System.Security.Cryptography.X509Certificates;
using Idata.Platform.Modules.Identity.Infrastructure;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Matrica e credentials në STARTUP (ADR-028 §5.2, B.2a §2.4/§5.1) mbi host-in
/// REAL (`Program`) me certifikata SINTETIKE runtime: Development eksplicit
/// vetëm; jashtë Development çdo dev/ephemeral/mixed/missing/expired/
/// not-yet-valid/pa çelës privat/PFX i pavlefshëm/konfigurim i munguar =
/// startup FAIL; mesazhet pa material (PFX/password). Zero DB e nevojshme.
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class IdentityAuthorityCredentialsStartupTests
{
    private const string PfxPassphrase = "synthetic-pfx-passphrase-not-confidential";

    private static (string, string) Pfx(X509Certificate2 certificate)
        => (SyntheticCertificates.ToPfxBase64(certificate, PfxPassphrase), PfxPassphrase);

    private static IdentityAuthorityHostSettings Production() => new() { Environment = "Production" };

    private static IdentityAuthorityHostSettings Development() => new() { Environment = "Development" };

    private static async Task AssertStartsAsync(IdentityAuthorityHostSettings settings)
    {
        await using var factory = new IdentityAuthorityFactory(settings);
        using var client = factory.CreateHttpsClient();
        // Host-i u startua (guard-i kaloi); endpoint-i i tokenit ekziston (GET → refuzim OAuth, jo 404).
        var response = await client.GetAsync(
            new Uri("/" + IdentityAuthorityServiceCollectionExtensions.TokenEndpointPath, UriKind.Relative),
            TestContext.Current.CancellationToken);
        Assert.NotEqual(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private static IdentityCredentialsPolicyException AssertStartupFails(
        IdentityAuthorityHostSettings settings, params string[] expectedFragments)
    {
        using var factory = new IdentityAuthorityFactory(settings);
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateHttpsClient());
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is IdentityCredentialsPolicyException policy)
            {
                foreach (var fragment in expectedFragments)
                {
                    Assert.Contains(fragment, policy.Message, StringComparison.Ordinal);
                }

                Assert.DoesNotContain(PfxPassphrase, policy.Message, StringComparison.Ordinal);
                return policy;
            }
        }

        throw new Xunit.Sdk.XunitException($"Startup dështoi me përjashtim tjetër (jo policy): {exception}");
    }

    // ══════════ Development (VETËM zgjedhje eksplicite) ══════════

    [Fact]
    public Task Development_ephemeral_eksplicit_per_te_dy_setet_starton()
        => AssertStartsAsync(Development() with
        {
            DevelopmentSigning = IdentityDevelopmentCredentialsOptions.EphemeralKey,
            DevelopmentEncryption = IdentityDevelopmentCredentialsOptions.EphemeralKey,
        });

    [Fact]
    public Task Development_certifikate_development_eksplicite_starton()
        => AssertStartsAsync(Development() with
        {
            DevelopmentSigning = IdentityDevelopmentCredentialsOptions.DevelopmentCertificate,
            DevelopmentEncryption = IdentityDevelopmentCredentialsOptions.DevelopmentCertificate,
        });

    [Fact]
    public Task Development_mixed_persistent_signing_dhe_ephemeral_encryption_starton()
    {
        using var signing = SyntheticCertificates.Signing();
        return AssertStartsAsync(Development() with
        {
            Signing = [Pfx(signing)],
            DevelopmentEncryption = IdentityDevelopmentCredentialsOptions.EphemeralKey,
        });
    }

    [Fact]
    public void Development_pa_encryption_deshton_fail_closed()
        => AssertStartupFails(
            Development() with { DevelopmentSigning = IdentityDevelopmentCredentialsOptions.EphemeralKey },
            "encryption-missing");

    [Fact]
    public void Development_pa_asnje_zgjedhje_deshton_asnje_fallback_implicit()
        => AssertStartupFails(Development(), "signing-missing", "encryption-missing");

    [Fact]
    public void Development_zgjedhje_e_panjohur_deshton()
        => AssertStartupFails(
            Development() with { DevelopmentSigning = "Whatever", DevelopmentEncryption = IdentityDevelopmentCredentialsOptions.EphemeralKey },
            "development-signing-unknown-choice");

    // ══════════ Jashtë Development — FAIL-CLOSED ══════════

    [Fact]
    public void Production_ephemeral_deshton()
        => AssertStartupFails(Production() with
        {
            DevelopmentSigning = IdentityDevelopmentCredentialsOptions.EphemeralKey,
            DevelopmentEncryption = IdentityDevelopmentCredentialsOptions.EphemeralKey,
        }, "development-credentials-outside-development");

    [Fact]
    public void Production_certifikate_development_deshton()
        => AssertStartupFails(Production() with
        {
            DevelopmentSigning = IdentityDevelopmentCredentialsOptions.DevelopmentCertificate,
            DevelopmentEncryption = IdentityDevelopmentCredentialsOptions.DevelopmentCertificate,
        }, "development-credentials-outside-development");

    [Fact]
    public void Staging_ephemeral_deshton_environment_jo_Development_trajtohet_si_production()
        => AssertStartupFails(new IdentityAuthorityHostSettings
        {
            Environment = "Staging",
            DevelopmentSigning = IdentityDevelopmentCredentialsOptions.EphemeralKey,
            DevelopmentEncryption = IdentityDevelopmentCredentialsOptions.EphemeralKey,
        }, "development-credentials-outside-development");

    [Fact]
    public async Task Production_persistent_te_vlefshme_starton_dhe_ristarton_me_te_njejtat()
    {
        using var signing = SyntheticCertificates.Signing();
        using var encryption = SyntheticCertificates.Encryption();
        var settings = Production() with { Signing = [Pfx(signing)], Encryption = [Pfx(encryption)] };
        await AssertStartsAsync(settings);
        await AssertStartsAsync(settings);
    }

    [Fact]
    public void Production_mixed_persistent_konfigurim_plus_ephemeral_konfigurim_deshton()
    {
        using var signing = SyntheticCertificates.Signing();
        using var encryption = SyntheticCertificates.Encryption();
        AssertStartupFails(Production() with
        {
            Signing = [Pfx(signing)],
            Encryption = [Pfx(encryption)],
            DevelopmentEncryption = IdentityDevelopmentCredentialsOptions.EphemeralKey,
        }, "development-credentials-outside-development");
    }

    [Fact]
    public void Production_mixed_me_celes_ephemeral_te_regjistruar_JASHTE_konfigurimit_deshton_nga_policy_mbi_credentials_reale()
    {
        using var signing = SyntheticCertificates.Signing();
        using var encryption = SyntheticCertificates.Encryption();
        // Konfigurimi është i pastër (persistent të vlefshme) — vetëm një regjistrim
        // i tretë shton ephemeral: faza 2 e guard-it (Evaluate mbi OpenIddictServerOptions).
        AssertStartupFails(Production() with
        {
            Signing = [Pfx(signing)],
            Encryption = [Pfx(encryption)],
            ExtraEphemeralSigningKeyOutsideConfiguration = true,
        }, "non-development-ephemeral-credential:signing[1]");
    }

    [Fact]
    public void Production_persistent_i_skaduar_deshton()
    {
        using var signing = SyntheticCertificates.Signing(
            notBefore: DateTimeOffset.UtcNow.AddDays(-30), notAfter: DateTimeOffset.UtcNow.AddMinutes(-1));
        using var encryption = SyntheticCertificates.Encryption();
        AssertStartupFails(Production() with { Signing = [Pfx(signing)], Encryption = [Pfx(encryption)] },
            "persistent-expired:signing[0]");
    }

    [Fact]
    public void Production_persistent_ende_jo_i_vlefshem_deshton()
    {
        using var signing = SyntheticCertificates.Signing();
        using var encryption = SyntheticCertificates.Encryption(
            notBefore: DateTimeOffset.UtcNow.AddMinutes(10), notAfter: DateTimeOffset.UtcNow.AddDays(30));
        AssertStartupFails(Production() with { Signing = [Pfx(signing)], Encryption = [Pfx(encryption)] },
            "persistent-not-yet-valid:encryption[0]");
    }

    [Fact]
    public void Production_persistent_pa_celes_privat_deshton()
    {
        using var signing = SyntheticCertificates.Signing();
        using var encryption = SyntheticCertificates.Encryption();
        AssertStartupFails(Production() with
        {
            Signing = [(SyntheticCertificates.ToPublicOnlyPfxBase64(signing, PfxPassphrase), PfxPassphrase)],
            Encryption = [Pfx(encryption)],
        }, "persistent-without-private-key", "(signing[0])");
    }

    [Fact]
    public void Production_pfx_i_pavlefshem_ose_password_i_gabuar_deshton_pa_rrjedhje_materiali()
    {
        using var signing = SyntheticCertificates.Signing();
        using var encryption = SyntheticCertificates.Encryption();
        var pfx = SyntheticCertificates.ToPfxBase64(signing, PfxPassphrase);

        var badBase64 = AssertStartupFails(Production() with
        {
            Signing = [("this-is-not-base64!!", PfxPassphrase)],
            Encryption = [Pfx(encryption)],
        }, "persistent-unloadable(", "(signing[0])");
        Assert.DoesNotContain("this-is-not-base64", badBase64.Message, StringComparison.Ordinal);

        var wrongPassword = AssertStartupFails(Production() with
        {
            Signing = [(pfx, "wrong-password")],
            Encryption = [Pfx(encryption)],
        }, "persistent-unloadable(", "(signing[0])");
        Assert.DoesNotContain(pfx[..32], wrongPassword.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong-password", wrongPassword.Message, StringComparison.Ordinal);
    }

    /// <summary>A2 (C1): `signing[0]` valid + `signing[1]` i pangarkueshëm → startup FAIL (state i shtyrë i pandryshueshëm).</summary>
    [Fact]
    public void Production_signing0_valid_dhe_signing1_i_pangarkueshem_deshton()
    {
        using var signing = SyntheticCertificates.Signing();
        using var encryption = SyntheticCertificates.Encryption();
        var failure = AssertStartupFails(Production() with
        {
            Signing = [Pfx(signing), ("this-is-not-base64!!", PfxPassphrase)],
            Encryption = [Pfx(encryption)],
        }, "persistent-unloadable(", "(signing[1])");
        Assert.DoesNotContain("signing[0]", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_konfigurim_i_munguar_deshton()
        => AssertStartupFails(Production() with { IncludeSection = false },
            "configuration:issuer-missing-or-relative",
            "configuration:audiences-missing",
            "configuration:jose-signature-algorithms-missing",
            "configuration:jose-key-management-algorithms-missing",
            "configuration:jose-content-encryption-algorithms-missing",
            "configuration:clock-skew-missing");

    [Fact]
    public void Production_issuer_http_deshton_ndersa_Development_e_lejon()
    {
        using var signing = SyntheticCertificates.Signing();
        using var encryption = SyntheticCertificates.Encryption();
        AssertStartupFails(Production() with
        {
            Issuer = "http://localhost/",
            Signing = [Pfx(signing)],
            Encryption = [Pfx(encryption)],
        }, "configuration:issuer-scheme-not-allowed");
    }

    [Fact]
    public void Production_clock_skew_jashte_kufijve_deshton()
    {
        using var signing = SyntheticCertificates.Signing();
        using var encryption = SyntheticCertificates.Encryption();
        AssertStartupFails(Production() with
        {
            ClockSkewSeconds = IdentityCredentialsPolicy.MaxClockSkewSeconds + 1,
            Signing = [Pfx(signing)],
            Encryption = [Pfx(encryption)],
        }, "configuration:clock-skew-out-of-bounds");
    }
}
