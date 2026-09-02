using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Idata.Platform.Modules.Identity.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Idata.Platform.UnitTests;

/// <summary>
/// Matrica PURE e policy-së së credentials (ADR-028 §5.2, B.2a §2.4) mbi
/// çelësa/certifikata SINTETIKE të gjeneruara në runtime (zero material në repo):
/// klasifikimi (persistent/development/ephemeral), Development eksplicit,
/// jashtë-Development fail-closed (dev/ephemeral/mixed/missing/expired/
/// not-yet-valid/pa çelës privat), mesazhe pa material sekret, validimi PURE
/// i konfigurimit dhe predikati design-time (FALSE në procesin e testit).
/// </summary>
public sealed class IdentityCredentialsPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private static X509SecurityKey Persistent(string subject = "CN=synthetic-persistent",
        DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null, bool withPrivateKey = true)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(notBefore ?? Now.AddDays(-1), notAfter ?? Now.AddDays(30));
        if (!withPrivateKey)
        {
            certificate = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        }

        return new X509SecurityKey(certificate);
    }

    private static X509SecurityKey DevelopmentSigning() => Persistent(IdentityCredentialsPolicy.DevelopmentSigningSubject);

    private static X509SecurityKey DevelopmentEncryption() => Persistent(IdentityCredentialsPolicy.DevelopmentEncryptionSubject);

    private static RsaSecurityKey Ephemeral() => new(RSA.Create(2048));

    // ══════════ Klasifikimi ══════════

    [Fact]
    public void Klasifikimi_persistent_development_ephemeral()
    {
        Assert.Equal(IdentityCredentialKind.Persistent, IdentityCredentialsPolicy.Classify(Persistent()));
        Assert.Equal(IdentityCredentialKind.Development, IdentityCredentialsPolicy.Classify(DevelopmentSigning()));
        Assert.Equal(IdentityCredentialKind.Development, IdentityCredentialsPolicy.Classify(DevelopmentEncryption()));
        Assert.Equal(IdentityCredentialKind.Ephemeral, IdentityCredentialsPolicy.Classify(Ephemeral()));
        Assert.Equal(IdentityCredentialKind.Ephemeral,
            IdentityCredentialsPolicy.Classify(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32))));
        // Subjekt i ngjashëm por jo identik = PERSISTENT (jo prefix/contains matching).
        Assert.Equal(IdentityCredentialKind.Persistent,
            IdentityCredentialsPolicy.Classify(Persistent("CN=OpenIddict Server Signing Certificate 2")));
    }

    // ══════════ Development (VETËM IsDevelopment) ══════════

    [Fact]
    public void Development_pranon_ephemeral_dev_ose_persistent_por_kerkon_te_dy_setet()
    {
        Assert.Empty(IdentityCredentialsPolicy.Evaluate(true, [Ephemeral()], [Ephemeral()], Now));
        Assert.Empty(IdentityCredentialsPolicy.Evaluate(true, [DevelopmentSigning()], [DevelopmentEncryption()], Now));
        Assert.Empty(IdentityCredentialsPolicy.Evaluate(true, [Persistent()], [Persistent()], Now));
        Assert.Empty(IdentityCredentialsPolicy.Evaluate(true, [Persistent(), Ephemeral()], [DevelopmentEncryption()], Now));

        Assert.Equal(["encryption-missing"], IdentityCredentialsPolicy.Evaluate(true, [Ephemeral()], [], Now));
        Assert.Equal(["signing-missing"], IdentityCredentialsPolicy.Evaluate(true, [], [Ephemeral()], Now));
        Assert.Equal(["signing-missing", "encryption-missing"], IdentityCredentialsPolicy.Evaluate(true, [], [], Now));
    }

    [Fact]
    public void Development_refuzon_persistent_te_skaduar_ose_pa_celes_privat()
    {
        var expired = Persistent(notBefore: Now.AddDays(-30), notAfter: Now.AddDays(-1));
        Assert.Equal(["persistent-expired:signing[0]"],
            IdentityCredentialsPolicy.Evaluate(true, [expired], [Ephemeral()], Now));
        Assert.Equal(["persistent-without-private-key:encryption[0]"],
            IdentityCredentialsPolicy.Evaluate(true, [Ephemeral()], [Persistent(withPrivateKey: false)], Now));
    }

    // ══════════ Jashtë Development — FAIL-CLOSED ══════════

    [Fact]
    public void Jashte_Development_pranon_vetem_persistent_te_vlefshem_ne_te_dy_setet()
    {
        Assert.Empty(IdentityCredentialsPolicy.Evaluate(false, [Persistent()], [Persistent()], Now));
        // Rotation me overlap: disa persistent të vlefshëm.
        Assert.Empty(IdentityCredentialsPolicy.Evaluate(false, [Persistent(), Persistent()], [Persistent(), Persistent()], Now));
    }

    [Fact]
    public void Jashte_Development_ephemeral_dev_ose_mixed_deshton()
    {
        // Të dyja arsyet raportohen: credential i ndaluar DHE mungesa e persistent-it.
        Assert.Equal(
            ["non-development-ephemeral-credential:signing[0]", "non-development-ephemeral-credential:encryption[0]",
             "persistent-signing-missing", "persistent-encryption-missing"],
            IdentityCredentialsPolicy.Evaluate(false, [Ephemeral()], [Ephemeral()], Now));
        Assert.Equal(
            ["non-development-development-credential:signing[0]", "non-development-development-credential:encryption[0]",
             "persistent-signing-missing", "persistent-encryption-missing"],
            IdentityCredentialsPolicy.Evaluate(false, [DevelopmentSigning()], [DevelopmentEncryption()], Now));
        // MIXED: persistent i vlefshëm + një ephemeral = FAIL (jo "mjafton një persistent").
        Assert.Equal(["non-development-ephemeral-credential:signing[1]"],
            IdentityCredentialsPolicy.Evaluate(false, [Persistent(), Ephemeral()], [Persistent()], Now));
        Assert.Equal(["non-development-development-credential:encryption[0]"],
            IdentityCredentialsPolicy.Evaluate(false, [Persistent()], [DevelopmentEncryption(), Persistent()], Now));
    }

    [Fact]
    public void Jashte_Development_missing_expired_not_yet_valid_pa_celes_privat_deshton()
    {
        Assert.Equal(["signing-missing", "encryption-missing", "persistent-signing-missing", "persistent-encryption-missing"],
            IdentityCredentialsPolicy.Evaluate(false, [], [], Now));
        Assert.Equal(["encryption-missing", "persistent-encryption-missing"],
            IdentityCredentialsPolicy.Evaluate(false, [Persistent()], [], Now));

        var expired = Persistent(notBefore: Now.AddDays(-30), notAfter: Now.AddMinutes(-1));
        Assert.Equal(["persistent-expired:signing[0]", "persistent-signing-missing"],
            IdentityCredentialsPolicy.Evaluate(false, [expired], [Persistent()], Now));

        var future = Persistent(notBefore: Now.AddMinutes(1), notAfter: Now.AddDays(30));
        Assert.Equal(["persistent-not-yet-valid:encryption[0]", "persistent-encryption-missing"],
            IdentityCredentialsPolicy.Evaluate(false, [Persistent()], [future], Now));

        Assert.Equal(["persistent-without-private-key:signing[0]", "persistent-signing-missing"],
            IdentityCredentialsPolicy.Evaluate(false, [Persistent(withPrivateKey: false)], [Persistent()], Now));

        // Kufiri i kohës: NotAfter ekzakt = ende i vlefshëm; +1s = i skaduar.
        var edge = Persistent(notBefore: Now.AddDays(-1), notAfter: Now);
        Assert.Empty(IdentityCredentialsPolicy.Evaluate(false, [edge], [Persistent()], Now));
        Assert.Contains("persistent-expired:signing[0]",
            IdentityCredentialsPolicy.Evaluate(false, [edge], [Persistent()], Now.AddSeconds(1)));
    }

    // ══════════ Mesazhe pa material sekret ══════════

    [Fact]
    public void Ngarkimi_persistent_deshton_me_klase_pa_rrjedhje_materiali()
    {
        const string passphrase = "synthetic-pass-not-confidential";
        var missing = Assert.Throws<IdentityCredentialsPolicyException>(() =>
            IdentityCredentialsPolicy.LoadPersistentCertificate(new IdentityPersistentCertificateOptions(), "signing", 0));
        Assert.Equal("persistent-source-missing", missing.FailureClass);

        var ambiguous = Assert.Throws<IdentityCredentialsPolicyException>(() =>
            IdentityCredentialsPolicy.LoadPersistentCertificate(
                new IdentityPersistentCertificateOptions { Pfx = "QUJD", PfxPath = "/synthetic/path.pfx", PfxPassword = passphrase }, "signing", 1));
        Assert.Equal("persistent-source-ambiguous", ambiguous.FailureClass);
        Assert.DoesNotContain(passphrase, ambiguous.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/synthetic/path.pfx", ambiguous.Message, StringComparison.Ordinal);

        var badBase64 = Assert.Throws<IdentityCredentialsPolicyException>(() =>
            IdentityCredentialsPolicy.LoadPersistentCertificate(
                new IdentityPersistentCertificateOptions { Pfx = "not-base64!!", PfxPassword = passphrase }, "encryption", 2));
        Assert.StartsWith("persistent-unloadable(", badBase64.FailureClass, StringComparison.Ordinal);
        Assert.DoesNotContain(passphrase, badBase64.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not-base64", badBase64.Message, StringComparison.Ordinal);
        Assert.Contains("(encryption[2])", badBase64.Message, StringComparison.Ordinal);

        var missingFile = Assert.Throws<IdentityCredentialsPolicyException>(() =>
            IdentityCredentialsPolicy.LoadPersistentCertificate(
                new IdentityPersistentCertificateOptions { PfxPath = "/synthetic/owner-private/missing.pfx", PfxPassword = passphrase }, "signing", 0));
        Assert.StartsWith("persistent-unloadable(", missingFile.FailureClass, StringComparison.Ordinal);
        Assert.DoesNotContain("owner-private", missingFile.Message, StringComparison.Ordinal);

        // Password i gabuar → klasë, jo material.
        using var rsa = RSA.Create(2048);
        var certificate = new CertificateRequest("CN=synthetic", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(Now.AddDays(-1), Now.AddDays(1));
        var pfx = Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12, "right"));
        var wrongPassword = Assert.Throws<IdentityCredentialsPolicyException>(() =>
            IdentityCredentialsPolicy.LoadPersistentCertificate(
                new IdentityPersistentCertificateOptions { Pfx = pfx, PfxPassword = "wrong" }, "signing", 0));
        Assert.StartsWith("persistent-unloadable(", wrongPassword.FailureClass, StringComparison.Ordinal);
        Assert.DoesNotContain(pfx[..24], wrongPassword.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong", wrongPassword.Message, StringComparison.Ordinal);

        // Ngarkim i suksesshëm nga base64 me passphrase.
        var loaded = IdentityCredentialsPolicy.LoadPersistentCertificate(
            new IdentityPersistentCertificateOptions { Pfx = pfx, PfxPassword = "right" }, "signing", 0);
        Assert.True(loaded.HasPrivateKey);
        Assert.Equal(certificate.Thumbprint, loaded.Thumbprint);
    }

    [Fact]
    public void Perjashtimi_i_policy_se_mbart_vetem_klasat_e_shkeljeve()
    {
        var exception = new IdentityCredentialsPolicyException(["persistent-expired:signing[0]", "persistent-signing-missing"]);
        Assert.Equal("persistent-expired:signing[0]", exception.FailureClass);
        Assert.Equal("identity-credentials-policy: persistent-expired:signing[0], persistent-signing-missing", exception.Message);
    }

    // ══════════ Validimi PURE i konfigurimit ══════════

    private static IdentityAuthorityOptions Valid() => new()
    {
        Issuer = "https://authority.synthetic.local/",
        Audiences = ["synthetic-api"],
        AllowedAlgorithms = ["RS256"],
        ClockSkewSeconds = 30,
    };

    [Fact]
    public void Konfigurimi_valid_kalon_dhe_cdo_fushe_e_munguar_ose_jashte_kufijve_deshton()
    {
        Assert.Empty(IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(Valid(), false));
        Assert.Empty(IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(Valid(), true));

        Assert.Equal(["issuer-missing-or-relative", "audiences-missing", "allowed-algorithms-missing", "clock-skew-missing"],
            IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(new IdentityAuthorityOptions(), false));

        var relative = Valid(); relative.Issuer = "authority/relative";
        Assert.Equal(["issuer-missing-or-relative"], IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(relative, false));

        // http: lejohet VETËM në Development.
        var http = Valid(); http.Issuer = "http://localhost/";
        Assert.Equal(["issuer-scheme-not-allowed"], IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(http, false));
        Assert.Empty(IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(http, true));

        var blankAudience = Valid(); blankAudience.Audiences = ["synthetic-api", " "];
        Assert.Equal(["audiences-missing"], IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(blankAudience, false));

        var noAlgorithms = Valid(); noAlgorithms.AllowedAlgorithms = [];
        Assert.Equal(["allowed-algorithms-missing"], IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(noAlgorithms, false));

        var negativeSkew = Valid(); negativeSkew.ClockSkewSeconds = -1;
        Assert.Equal(["clock-skew-out-of-bounds"], IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(negativeSkew, false));
        var hugeSkew = Valid(); hugeSkew.ClockSkewSeconds = IdentityCredentialsPolicy.MaxClockSkewSeconds + 1;
        Assert.Equal(["clock-skew-out-of-bounds"], IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(hugeSkew, false));
        var maxSkew = Valid(); maxSkew.ClockSkewSeconds = IdentityCredentialsPolicy.MaxClockSkewSeconds;
        Assert.Empty(IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(maxSkew, false));
        var zeroSkew = Valid(); zeroSkew.ClockSkewSeconds = 0;
        Assert.Empty(IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(zeroSkew, false));
    }

    // ══════════ Neutraliteti i konfigurimit ══════════

    [Fact]
    public void Konfigurimi_nuk_ka_asnje_default_production_issuer_audience_algoritem_skew_apo_credential()
    {
        var options = new IdentityAuthorityOptions();
        Assert.Null(options.Issuer);
        Assert.Empty(options.Audiences);
        Assert.Empty(options.AllowedAlgorithms);
        Assert.Null(options.ClockSkewSeconds);
        Assert.Null(options.Credentials.Development.Signing);
        Assert.Null(options.Credentials.Development.Encryption);
        Assert.Empty(options.Credentials.Signing);
        Assert.Empty(options.Credentials.Encryption);
        Assert.Equal("Identity:Authority", IdentityAuthorityOptions.SectionName);
        // Endpoint-et janë path-e RELATIVE (issuer-i vjen vetëm nga konfigurimi).
        Assert.False(Uri.TryCreate(IdentityAuthorityServiceCollectionExtensions.TokenEndpointPath, UriKind.Absolute, out _));
        Assert.False(Uri.TryCreate(IdentityAuthorityServiceCollectionExtensions.AuthorizationEndpointPath, UriKind.Absolute, out _));
    }

    // ══════════ Predikati design-time ══════════

    [Fact]
    public void Predikati_design_time_eshte_FALSE_ne_procesin_e_testit_dhe_lidhet_vetem_me_toolin_zyrtar()
    {
        Assert.False(IdentityAuthorityServiceCollectionExtensions.IsDesignTimeDocumentGenerationHost);
        Assert.Equal<string>(["GetDocument.Insider", "dotnet-getdocument"],
            IdentityAuthorityServiceCollectionExtensions.DesignTimeDocumentGenerationEntryAssemblies);

        // Forma PURE: vetëm entry-i i tool-it zyrtar → TRUE; host real/test/bosh → FALSE.
        Assert.True(IdentityAuthorityServiceCollectionExtensions.IsDesignTimeDocumentGenerationEntry(
            ["/synthetic/tools/net10.0/GetDocument.Insider.dll", "--assembly", "x.dll"]));
        Assert.True(IdentityAuthorityServiceCollectionExtensions.IsDesignTimeDocumentGenerationEntry(
            ["/synthetic/dotnet-getdocument.dll"]));
        Assert.False(IdentityAuthorityServiceCollectionExtensions.IsDesignTimeDocumentGenerationEntry(
            ["/synthetic/app/Idata.Platform.Api.dll"]));
        Assert.False(IdentityAuthorityServiceCollectionExtensions.IsDesignTimeDocumentGenerationEntry(
            ["/synthetic/app/Idata.Platform.Api"]));
        Assert.False(IdentityAuthorityServiceCollectionExtensions.IsDesignTimeDocumentGenerationEntry(
            ["/synthetic/tests/Idata.Platform.IntegrationTests.dll"]));
        Assert.False(IdentityAuthorityServiceCollectionExtensions.IsDesignTimeDocumentGenerationEntry(
            ["/synthetic/GetDocument.Insider.Fake.dll"])); // pa prefix/contains matching
        Assert.False(IdentityAuthorityServiceCollectionExtensions.IsDesignTimeDocumentGenerationEntry([]));
        Assert.False(IdentityAuthorityServiceCollectionExtensions.IsDesignTimeDocumentGenerationEntry([""]));
    }
}
