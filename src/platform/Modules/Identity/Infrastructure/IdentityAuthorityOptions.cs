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

    /// <summary>
    /// Tri politikat JOSE të NDARA dhe të DETYRUESHME (`Jose:SignatureAlgorithms`,
    /// `Jose:KeyManagementAlgorithms`, `Jose:ContentEncryptionAlgorithms`). Emri i
    /// vjetër `AllowedAlgorithms` NUK ekziston më (zero konfigurim dual, zero fallback).
    /// </summary>
    public IdentityAuthorityJoseOptions Jose { get; set; } = new();

    /// <summary>Clock skew eksplicit dhe i KUFIZUAR (0..<see cref="IdentityCredentialsPolicy.MaxClockSkewSeconds"/>).</summary>
    public int? ClockSkewSeconds { get; set; }

    public IdentityAuthorityCredentialsOptions Credentials { get; set; } = new();

    /// <summary>Kufijtë e sipërfaqes interaktive (B.2b) — të deklaruar dhe të validuar fail-closed.</summary>
    public IdentityInteractionOptions Interaction { get; set; } = new();
}

/// <summary>
/// Kufijtë EKSPLICITË të interaksionit (login/consent) — vlerat e paracaktuara
/// janë konstante të deklaruara në kod (jo magji e fshehur) dhe mund të
/// mbishkruhen nga konfigurimi i jashtëm; çdo mbishkrim jashtë kufijve = startup
/// FAIL. Nuk ka fallback të heshtur dhe nuk ka kufi të pakufizuar.
/// </summary>
public sealed class IdentityInteractionOptions
{
    public const int DefaultSessionMinutes = 30;
    public const int DefaultMaxFailedAccessAttempts = 5;
    public const int DefaultLockoutMinutes = 15;
    public const int DefaultLoginPermitLimit = 20;
    public const int DefaultLoginWindowSeconds = 60;

    public const int MaxSessionMinutes = 1440;
    public const int MaxLockoutAttempts = 20;
    public const int MaxLockoutMinutes = 1440;
    public const int MaxLoginPermitLimit = 1000;
    public const int MaxLoginWindowSeconds = 3600;

    /// <summary>Jetëgjatësia e session cookie-t interaktive (sliding = OFF).</summary>
    public int SessionMinutes { get; set; } = DefaultSessionMinutes;

    /// <summary>Tentativa të dështuara para bllokimit të llogarisë.</summary>
    public int MaxFailedAccessAttempts { get; set; } = DefaultMaxFailedAccessAttempts;

    /// <summary>Kohëzgjatja e bllokimit pas kalimit të pragut.</summary>
    public int LockoutMinutes { get; set; } = DefaultLockoutMinutes;

    /// <summary>Kërkesa POST login të lejuara për dritare, per burim.</summary>
    public int LoginPermitLimit { get; set; } = DefaultLoginPermitLimit;

    /// <summary>Gjatësia e dritares fikse të rate-limit-it, në sekonda.</summary>
    public int LoginWindowSeconds { get; set; } = DefaultLoginWindowSeconds;
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

/// <summary>
/// Tri allowlist-e EKZAKTE (ordinal) dhe të NDARA për tokenët e aksesit (JWE me JWS
/// të brendshëm): (1) `alg` i firmës së JWS-së së brendshme; (2) `alg` i
/// key-management-it të JWE-së së jashtme; (3) `enc` i content-encryption-it të
/// JWE-së së jashtme. Secila kërkohet eksplicitisht — zero default, zero fallback.
/// </summary>
public sealed class IdentityAuthorityJoseOptions
{
    public string[] SignatureAlgorithms { get; set; } = [];

    public string[] KeyManagementAlgorithms { get; set; } = [];

    public string[] ContentEncryptionAlgorithms { get; set; } = [];
}
