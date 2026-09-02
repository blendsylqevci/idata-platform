namespace Idata.Platform.Modules.Identity.Infrastructure;

/// <summary>
/// Konfigurimi PRODUCT-NEUTRAL i authority-t OpenIddict co-located (ADR-028,
/// Increment B.2a). Çdo vlerë vjen nga kufiri i jashtëm i konfigurimit/
/// deployment-it (environment, secrets provider) dhe validohet FAIL-CLOSED në
/// startup: asnjë host, audience, algoritëm apo skew nuk sajohet si default
/// production. Zero client/audience/scope/rol specifik produkti; zero material
/// sekret në repo.
/// </summary>
public sealed class IdentityAuthorityOptions
{
    public const string SectionName = "Identity:Authority";

    /// <summary>Issuer absolut (URI) — përdoret si `iss` në issuance dhe validim.</summary>
    public string? Issuer { get; set; }

    /// <summary>Audiences/resources të pranuara nga API-ja (validation `aud`).</summary>
    public string[] Audiences { get; set; } = [];

    /// <summary>Allowlist eksplicit i algoritmeve të firmës (p.sh. RS256).</summary>
    public string[] AllowedAlgorithms { get; set; } = [];

    /// <summary>Clock skew eksplicit dhe i KUFIZUAR (0..<see cref="IdentityCredentialsPolicy.MaxClockSkewSeconds"/>).</summary>
    public int? ClockSkewSeconds { get; set; }

    public IdentityAuthorityCredentialsOptions Credentials { get; set; } = new();
}

/// <summary>Burimet e credentials — persistent (PFX) dhe/ose eksplicite development.</summary>
public sealed class IdentityAuthorityCredentialsOptions
{
    /// <summary>Credentials development/ephemeral — nderohen VETËM nën <c>IHostEnvironment.IsDevelopment()</c>.</summary>
    public IdentityDevelopmentCredentialsOptions Development { get; set; } = new();

    /// <summary>Certifikata PERSISTENT për firmë (RSA, DigitalSignature) — mund të jenë disa (rotation me overlap).</summary>
    public IdentityPersistentCertificateOptions[] Signing { get; set; } = [];

    /// <summary>Certifikata PERSISTENT për enkriptim (RSA, KeyEncipherment) — mund të jenë disa (rotation me overlap).</summary>
    public IdentityPersistentCertificateOptions[] Encryption { get; set; } = [];
}

/// <summary>
/// Zgjedhja EKSPLICITE e credentials development: `EphemeralKey` ose
/// `DevelopmentCertificate` për secilin set. Asnjë fallback implicit: mungesa
/// e njërit në Development = startup FAIL; prania e cilitdo jashtë Development
/// = startup FAIL.
/// </summary>
public sealed class IdentityDevelopmentCredentialsOptions
{
    public const string EphemeralKey = "EphemeralKey";
    public const string DevelopmentCertificate = "DevelopmentCertificate";

    public string? Signing { get; set; }

    public string? Encryption { get; set; }
}

/// <summary>
/// Një certifikatë persistent nga kufiri i jashtëm: PKCS#12 si base64 (nga
/// secrets provider/environment) OSE si path file-i — saktësisht njëra.
/// Password-i (opsional) vjen nga i njëjti kufi. Asnjë material nuk logohet.
/// </summary>
public sealed class IdentityPersistentCertificateOptions
{
    public string? Pfx { get; set; }

    public string? PfxPath { get; set; }

    public string? PfxPassword { get; set; }
}
