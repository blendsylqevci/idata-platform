using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Tokens;

namespace Idata.Platform.Modules.Identity.Infrastructure;

/// <summary>Klasa e një credential-i sipas policy-së së B.2a (ADR-028 §5.2).</summary>
public enum IdentityCredentialKind
{
    /// <summary>Certifikatë X.509 me çelës privat, jo e gjeneruar nga OpenIddict për development.</summary>
    Persistent,

    /// <summary>Certifikata development e OpenIddict (`AddDevelopment*Certificate`), e njohur nga subjekti.</summary>
    Development,

    /// <summary>Çdo çelës jo-X.509 (RSA/ECDSA/simetrik i papërsistuar — `AddEphemeral*Key`, `Add*Key`).</summary>
    Ephemeral,
}

/// <summary>
/// Policy PURE, FAIL-CLOSED e credentials (ADR-028 §5.2, B.2a §2.4) —
/// vlerësohet mbi credentials REALE të regjistruara në `OpenIddictServerOptions`,
/// jo vetëm mbi konfigurim:
///  - Development (VETËM `IHostEnvironment.IsDevelopment()`): kërkohen ≥1 signing
///    + ≥1 encryption (dev/ephemeral ose persistent), të gjitha të vlefshme;
///  - çdo environment tjetër (Staging/Production/i panjohur): ÇFARËDO credential
///    development/ephemeral = FAIL (edhe mixed me persistent); kërkohen ≥1
///    signing PERSISTENT dhe ≥1 encryption PERSISTENT, të dyja të vlefshme;
///  - persistent = X.509 me çelës privat brenda [NotBefore, NotAfter]; i skaduar,
///    ende-jo-i-vlefshëm ose pa çelës privat = FAIL.
/// Mesazhet mbartin VETËM klasën e dështimit + identifikues jo-sekret
/// (lloji/indeksi) — kurrë çelës, certifikatë, thumbprint apo password.
/// </summary>
public static class IdentityCredentialsPolicy
{
    public const int MaxClockSkewSeconds = 300;

    /// <summary>Subjektet që OpenIddict 7.6.0 u vë certifikatave development (burimi zyrtar: OpenIddictServerBuilder).</summary>
    public const string DevelopmentSigningSubject = "CN=OpenIddict Server Signing Certificate";

    public const string DevelopmentEncryptionSubject = "CN=OpenIddict Server Encryption Certificate";

    public static IdentityCredentialKind Classify(SecurityKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key is not X509SecurityKey x509)
        {
            return IdentityCredentialKind.Ephemeral;
        }

        var subject = x509.Certificate.SubjectName.Name;
        return string.Equals(subject, DevelopmentSigningSubject, StringComparison.Ordinal)
            || string.Equals(subject, DevelopmentEncryptionSubject, StringComparison.Ordinal)
            ? IdentityCredentialKind.Development
            : IdentityCredentialKind.Persistent;
    }

    /// <summary>
    /// Vlerëson credentials REALE. Kthen listën e shkeljeve (bosh = policy e
    /// përmbushur). Çdo shkelje = `kod:lloj[indeks]` pa material sekret.
    /// </summary>
    public static IReadOnlyList<string> Evaluate(
        bool isDevelopment,
        IReadOnlyList<SecurityKey> signingKeys,
        IReadOnlyList<SecurityKey> encryptionKeys,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(signingKeys);
        ArgumentNullException.ThrowIfNull(encryptionKeys);

        var violations = new List<string>();
        var validPersistentSigning = EvaluateSet("signing", signingKeys, isDevelopment, now, violations);
        var validPersistentEncryption = EvaluateSet("encryption", encryptionKeys, isDevelopment, now, violations);

        if (signingKeys.Count == 0)
        {
            violations.Add("signing-missing");
        }

        if (encryptionKeys.Count == 0)
        {
            violations.Add("encryption-missing");
        }

        if (!isDevelopment)
        {
            if (validPersistentSigning == 0)
            {
                violations.Add("persistent-signing-missing");
            }

            if (validPersistentEncryption == 0)
            {
                violations.Add("persistent-encryption-missing");
            }
        }

        return violations;
    }

    private static int EvaluateSet(
        string kind, IReadOnlyList<SecurityKey> keys, bool isDevelopment, DateTimeOffset now, List<string> violations)
    {
        var validPersistent = 0;
        for (var index = 0; index < keys.Count; index++)
        {
            var classification = Classify(keys[index]);
            switch (classification)
            {
                case IdentityCredentialKind.Persistent:
                    if (IsValidPersistent((X509SecurityKey)keys[index], now, out var reason))
                    {
                        validPersistent++;
                    }
                    else
                    {
                        violations.Add($"{reason}:{kind}[{index}]");
                    }

                    break;
                case IdentityCredentialKind.Development:
                case IdentityCredentialKind.Ephemeral:
                    if (!isDevelopment)
                    {
                        violations.Add($"non-development-{classification.ToString().ToLowerInvariant()}-credential:{kind}[{index}]");
                    }

                    break;
            }
        }

        return validPersistent;
    }

    /// <summary>
    /// Vlefshmëria e një credential-i PERSISTENT në çastin `now`: çelës privat
    /// prezent dhe `now` brenda [NotBefore, NotAfter]. Arsyeja = klasë pa material.
    /// </summary>
    public static bool IsValidPersistent(X509SecurityKey key, DateTimeOffset now, out string reason)
    {
        ArgumentNullException.ThrowIfNull(key);
        var certificate = key.Certificate;
        if (!certificate.HasPrivateKey)
        {
            reason = "persistent-without-private-key";
            return false;
        }

        var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero);
        var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        if (now < notBefore)
        {
            reason = "persistent-not-yet-valid";
            return false;
        }

        if (now > notAfter)
        {
            reason = "persistent-expired";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Ngarkim FAIL-CLOSED i një certifikate persistent nga kufiri i jashtëm (pa rrjedhje materiali).</summary>
    public static X509Certificate2 LoadPersistentCertificate(
        IdentityPersistentCertificateOptions options, string kind, int index)
    {
        ArgumentNullException.ThrowIfNull(options);
        var hasInline = !string.IsNullOrWhiteSpace(options.Pfx);
        var hasPath = !string.IsNullOrWhiteSpace(options.PfxPath);
        if (hasInline == hasPath)
        {
            throw new IdentityCredentialsPolicyException(
                hasInline ? "persistent-source-ambiguous" : "persistent-source-missing", kind, index);
        }

        try
        {
            var certificate = hasInline
                ? X509CertificateLoader.LoadPkcs12(
                    Convert.FromBase64String(options.Pfx!), options.PfxPassword, X509KeyStorageFlags.DefaultKeySet)
                : X509CertificateLoader.LoadPkcs12FromFile(
                    options.PfxPath!, options.PfxPassword, X509KeyStorageFlags.DefaultKeySet);
            if (!certificate.HasPrivateKey)
            {
                throw new IdentityCredentialsPolicyException("persistent-without-private-key", kind, index);
            }

            return certificate;
        }
        catch (IdentityCredentialsPolicyException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Klasa e dështimit + tipi i përjashtimit — asnjë path, password, bajt.
            throw new IdentityCredentialsPolicyException(
                $"persistent-unloadable({exception.GetType().Name})", kind, index);
        }
    }
}

/// <summary>Dështim i policy-së së credentials — mesazh me klasë + identifikues jo-sekret vetëm.</summary>
public sealed class IdentityCredentialsPolicyException : InvalidOperationException
{
    public IdentityCredentialsPolicyException(string failureClass, string kind, int index)
        : base($"identity-credentials-policy: {failureClass} ({kind}[{index}])")
    {
        FailureClass = failureClass;
    }

    public IdentityCredentialsPolicyException(IReadOnlyList<string> violations)
        : base($"identity-credentials-policy: {string.Join(", ", violations)}")
    {
        FailureClass = violations.Count > 0 ? violations[0] : "unknown";
    }

    public string FailureClass { get; }
}
