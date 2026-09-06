using System.Collections.Immutable;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Idata.Platform.Modules.Identity.Application;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation;
using OpenIddict.Validation.AspNetCore;

namespace Idata.Platform.Modules.Identity.Infrastructure;

/// <summary>
/// Wiring i VETËM i authority-t OpenIddict co-located (ADR-028, Increment B.2a,
/// correction C1) — konsumohet nga composition root-i (API host) pa asnjë
/// referencë direkte OpenIddict aty:
///  - OpenIddict **Server** (issuer/endpoint-e/credentials/PKCE) me
///    Authorization Code + PKCE të DEKLARUAR si flow-i i vetëm production;
///    endpoint-i i autorizimit është FAIL-CLOSED (refuzon çdo kërkesë) derisa E2
///    të implementohet — zero implicit, zero password, zero client-credentials,
///    zero refresh në production;
///  - OpenIddict **Validation** lokale (`UseLocalServer`) + handler-i ASP.NET
///    Core: issuer, audiences, **tri politika JOSE të ndara** (firma e JWS-së së
///    brendshme · key-management i JWE-së së jashtme · content-encryption i JWE-së
///    së jashtme — header-i i jashtëm kontrollohet PARA çdo unwrap/dekriptimi),
///    lifetime/nbf/exp, clock skew i kufizuar, token-entry validation (revocation)
///    — unknown → 401;
///  - kufiri i startup-it FAIL-CLOSED **pa asnjë përjashtim runtime**: validim
///    `ValidateOnStart` (ekzekutohet nga host-i PARA çdo hosted service, pra para
///    çdo bind-i të server-it) mbi (1) konfigurimin + dështimet e regjistrimit
///    (snapshot i pandryshueshëm) dhe (2) credentials REALE të
///    `OpenIddictServerOptions` + koherencën issuance↔validation të algoritmeve;
///  - skema default e autentikimit (authenticate + challenge) = handler-i i
///    validimit OpenIddict — `[Authorize]` i zhveshur mjafton.
/// Product-neutral: zero client/audience/scope/rol specifik produkti.
/// </summary>
public static class IdentityAuthorityServiceCollectionExtensions
{
    public const string TokenEndpointPath = "connect/token";

    public const string AuthorizationEndpointPath = "connect/authorize";

    public static IServiceCollection AddIdentityAuthority(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var section = configuration.GetSection(IdentityAuthorityOptions.SectionName);
        var authority = section.Get<IdentityAuthorityOptions>() ?? new IdentityAuthorityOptions();
        var isDevelopment = environment.IsDevelopment();
        var jose = IdentityJosePolicy.From(authority.Jose);

        // Dështimet e regjistrimit (konfigurim i pavlefshëm, certifikatë e
        // pangarkueshme, credentials development jashtë Development, set i munguar)
        // mblidhen në një state TË BRENDSHËM dhe ngrihen në snapshot të
        // PANDRYSHUESHËM para se të dalin nga kjo metodë — kurrë të heshtura,
        // kurrë me material, kurrë të ndryshueshme nga konsumatorët.
        var state = new IdentityAuthorityStartupState();
        var configurationErrors = ValidateConfiguration(authority, isDevelopment);
        var configured = configurationErrors.Count == 0;
        foreach (var error in configurationErrors)
        {
            state.Fail($"configuration:{error}");
        }

        services.TryAddSingleton(TimeProvider.System);

        services.AddOpenIddict()
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris(AuthorizationEndpointPath)
                    .SetTokenEndpointUris(TokenEndpointPath)
                    // B.2c: endpoint-i i VETËM i ri — UserInfo (OIDC Core §5.3); rruga vjen
                    // nga kontrata neutrale e Application-it (e njëjta për controller-in).
                    .SetUserInfoEndpointUris(UserInfoContract.Path);

                // Flow-i target i ADR-028 §4: Authorization Code + PKCE i DETYRUESHËM.
                // Interaktiviteti (login/consent) = E2 OPEN → handler fail-closed më poshtë.
                options.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange();

                // B.2b: passthrough VETËM për endpoint-in e autorizimit — kërkesa e
                // VALIDUAR nga serveri i dorëzohet owner-it të aprovuar të interaksionit
                // (Identity.Presentation: login/consent). Token endpoint-i mbetet
                // TËRËSISHT i OpenIddict-it: zero passthrough, zero lëshim nga kodi ynë.
                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    // B.2c: passthrough i UserInfo VETËM te ky owner — OpenIddict validon
                    // access token-in (bearer) PARA se kërkesa të arrijë thin controller-in.
                    .EnableUserInfoEndpointPassthrough();

                // PKCE me `S256` i DETYRUESHËM: metoda `plain` hiqet nga seti i
                // serverit — kërkesat me `plain` (ose pa metodë) refuzohen.
                options.Configure(server => server.CodeChallengeMethods.Remove(OpenIddictConstants.CodeChallengeMethods.Plain));

                // B.2c: profili OIDC Authorization Code + PKCE (provider-side). Refuzimi i
                // qëllimshëm i B.2b për `openid` dhe pastrimi i metadata-s u HOQËN (zëvendësim
                // i kontrolluar me provë pozitive OIDC): `openid` → ID token i nënshkruar me
                // credentials REALE (aud = client_id; `nonce` i kërkesës i lidhur nga OpenIddict),
                // discovery/JWKS shpallin VETËM aftësitë reale (openid, algoritmet e ID token-it,
                // UserInfo). Grants/endpoint-e të tjera mbeten të paregjistruara (fail-closed).

                if (configured)
                {
                    options.SetIssuer(new Uri(authority.Issuer!, UriKind.Absolute));
                    RegisterCredentials(options, authority.Credentials, isDevelopment, state);

                    // JOSE mbi stack-un e SERVER-it (B.2b, mbyllje e follow-up-it të B.2a):
                    // `enc` i JWE-së së jashtme dhe `alg` i firmës së JWS-së së brendshme
                    // kalojnë nga i njëjti validator REAL që përdor validimi i aksesit.
                    options.Configure(server =>
                    {
                        server.TokenValidationParameters.ValidAlgorithms = null;
                        server.TokenValidationParameters.AlgorithmValidator = jose.ValidateAlgorithm;
                    });

                    // Header-i i JASHTËM (`alg`/`enc`) i ÇDO artefakti të mbrojtur që
                    // konsumon serveri (kodet e autorizimit te `/connect/token`; B.2c: access
                    // token-i bearer te `/connect/userinfo`) kontrollohet PARA çdo unwrap-i të
                    // CEK-ut — IdentityModel nuk e validon kurrë `alg`-un e jashtëm. Refuzim
                    // OAuth sipas endpoint-it (`invalid_grant` / `invalid_token`), kurrë 500.
                    options.AddEventHandler<OpenIddictServerEvents.ValidateTokenContext>(descriptor =>
                        descriptor.UseInlineHandler(context => jose.ValidateServerOuterHeader(context))
                            .SetOrder(OpenIddictServerHandlers.Protection.ValidateIdentityModelToken.Descriptor.Order - 500));
                }
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
                options.EnableTokenEntryValidation();

                if (configured)
                {
                    var clockSkew = TimeSpan.FromSeconds(authority.ClockSkewSeconds!.Value);
                    options.AddAudiences(authority.Audiences.ToArray());
                    options.Configure(validation =>
                    {
                        // Skew i KUFIZUAR (nga konfigurimi i validuar) — zbatohet nga
                        // OpenIddict `ValidateExpirationDate` (exp autoritativ nga token
                        // entry-i i store-it) DHE nga validimi i lifetime-it të IdentityModel.
                        validation.TokenValidationParameters.ClockSkew = clockSkew;

                        // D2 (KEEP): OpenIddict e çaktivizon default lifetime-in e IdentityModel
                        // (validon `exp` vetë, nga entry-i; nuk validon `nbf`). Aktivizimi =
                        // kontroll SHTESË i validator-it real mbi `exp`/`nbf` të vetë JWT-së.
                        validation.TokenValidationParameters.ValidateLifetime = true;

                        // JOSE (1) firma e JWS-së së brendshme + (3) `enc` i JWE-së së jashtme:
                        // delegate-i i validator-it REAL (IdentityModel e thërret për `enc` me
                        // token-in e jashtëm të enkriptuar dhe për `alg`-un e firmës me
                        // token-in e brendshëm). `ValidAlgorithms` (listë e vetme e përbashkët)
                        // NUK përdoret — nuk mbulon key-management-in e jashtëm.
                        validation.TokenValidationParameters.ValidAlgorithms = null;
                        validation.TokenValidationParameters.AlgorithmValidator = jose.ValidateAlgorithm;
                    });

                    // JOSE (2)+(3): header-i i JASHTËM (`alg`/`enc`) kontrollohet PARA
                    // `ValidateIdentityModelToken` të stack-ut të VALIDIMIT (access tokens),
                    // pra para çdo unwrap-i të CEK-ut me çelës privat — IdentityModel 8.19.2
                    // nuk e validon kurrë `alg`-un e jashtëm. Parse pa `JsonWebToken` (vetëm
                    // segmenti 0), catch-all: missing/unknown/jashtë allowlist-it/malformed
                    // (çdo segment) → refuzim (401), kurrë 500. Stack-u i SERVER-it
                    // (`/connect/token`: kode autorizimi/refresh) nuk lëshon token sot
                    // (autorizimi fail-closed) — gate i njëjtë = follow-up i E2.
                    options.AddEventHandler<OpenIddictValidationEvents.ValidateTokenContext>(descriptor =>
                        descriptor.UseInlineHandler(context => jose.ValidateOuterHeader(context))
                            .SetOrder(OpenIddictValidationHandlers.Protection.ValidateIdentityModelToken.Descriptor.Order - 500));
                }
            });

        // Snapshot-i ngrihet PAS çdo vlerësimi regjistrimi — konsumatorët marrin VETËM
        // `ImmutableArray<string>`; state-i dhe mutator-i nuk dalin nga ky assembly.
        var snapshot = state.Freeze();
        services.AddSingleton(snapshot);

        // Klasifikim DETERMINIST: dështimet e regjistrimit hidhen si `IConfigureOptions`
        // të `OpenIddictServerOptions` (regjistruar PAS AddServer) — configure-delegate-t
        // ekzekutohen para `PostConfigure`-it të OpenIddict (ID0085/ID0086), pavarësisht
        // se cili komponent i materializon opsionet i pari apo rendit të StartupValidator-it.
        services.Configure<OpenIddictServerOptions>(_ =>
        {
            if (!snapshot.Failures.IsDefaultOrEmpty)
            {
                throw new IdentityCredentialsPolicyException(snapshot.Failures);
            }
        });

        // Kufiri i startup-it (pika e VETME fail-closed, pa përjashtim runtime):
        // (1) konfigurimi + dështimet e shtyra; (2) credentials REALE + koherenca JOSE.
        // `ValidateOnStart` ekzekutohet nga host-i para çdo hosted service (para bind-it).
        services.AddOptions<IdentityAuthorityOptions>().Bind(section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentityAuthorityOptions>>(
            new IdentityAuthorityOptionsValidator(isDevelopment, snapshot));
        services.AddOptions<OpenIddictServerOptions>().ValidateOnStart();
        services.AddSingleton<IValidateOptions<OpenIddictServerOptions>>(provider =>
            new IdentityAuthorityCredentialsValidator(isDevelopment, jose, provider.GetRequiredService<TimeProvider>()));

        // Skema DEFAULT eksplicite (authenticate + challenge) = validimi OpenIddict.
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });
        services.AddAuthorization();

        return services;
    }

    private static void RegisterCredentials(
        OpenIddictServerBuilder options,
        IdentityAuthorityCredentialsOptions credentials,
        bool isDevelopment,
        IdentityAuthorityStartupState state)
    {
        var development = credentials.Development;
        var hasDevelopmentConfiguration = !string.IsNullOrWhiteSpace(development.Signing)
            || !string.IsNullOrWhiteSpace(development.Encryption);
        if (hasDevelopmentConfiguration && !isDevelopment)
        {
            state.Fail("development-credentials-outside-development");
        }

        var now = TimeProvider.System.GetUtcNow();
        var signing = 0;
        var encryption = 0;
        for (var index = 0; index < credentials.Signing.Length; index++)
        {
            if (TryLoadValidPersistent(credentials.Signing[index], "signing", index, now, state, out var certificate))
            {
                try
                {
                    options.AddSigningCertificate(certificate);
                    signing++;
                }
                catch (Exception exception)
                {
                    state.Fail(Describe(exception, "signing", index));
                }
            }
        }

        for (var index = 0; index < credentials.Encryption.Length; index++)
        {
            if (TryLoadValidPersistent(credentials.Encryption[index], "encryption", index, now, state, out var certificate))
            {
                try
                {
                    options.AddEncryptionCertificate(certificate);
                    encryption++;
                }
                catch (Exception exception)
                {
                    state.Fail(Describe(exception, "encryption", index));
                }
            }
        }

        if (isDevelopment)
        {
            // Development: zgjedhje EKSPLICITE, kurrë fallback implicit.
            switch (development.Signing)
            {
                case IdentityDevelopmentCredentialsOptions.EphemeralKey:
                    options.AddEphemeralSigningKey();
                    signing++;
                    break;
                case IdentityDevelopmentCredentialsOptions.DevelopmentCertificate:
                    options.AddDevelopmentSigningCertificate();
                    signing++;
                    break;
                case null or "":
                    break;
                default:
                    state.Fail("development-signing-unknown-choice");
                    break;
            }

            switch (development.Encryption)
            {
                case IdentityDevelopmentCredentialsOptions.EphemeralKey:
                    options.AddEphemeralEncryptionKey();
                    encryption++;
                    break;
                case IdentityDevelopmentCredentialsOptions.DevelopmentCertificate:
                    options.AddDevelopmentEncryptionCertificate();
                    encryption++;
                    break;
                case null or "":
                    break;
                default:
                    state.Fail("development-encryption-unknown-choice");
                    break;
            }
        }

        if (signing == 0)
        {
            state.Fail("signing-missing");
        }

        if (encryption == 0)
        {
            state.Fail("encryption-missing");
        }
    }

    /// <summary>
    /// Ngarkon dhe vlerëson PARA se t'ia dorëzojë OpenIddict-it: klasifikimi ynë
    /// (expired/not-yet-valid/pa çelës privat/i pangarkueshëm) është autoritativ
    /// dhe i regjistruar te startup state; asnjë credential i pavlefshëm nuk
    /// regjistrohet.
    /// </summary>
    private static bool TryLoadValidPersistent(
        IdentityPersistentCertificateOptions source,
        string kind,
        int index,
        DateTimeOffset now,
        IdentityAuthorityStartupState state,
        out System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        certificate = null!;
        try
        {
            certificate = IdentityCredentialsPolicy.LoadPersistentCertificate(source, kind, index);
        }
        catch (IdentityCredentialsPolicyException exception)
        {
            state.Fail(exception.Message);
            return false;
        }

        if (!IdentityCredentialsPolicy.IsValidPersistent(new X509SecurityKey(certificate), now, out var reason))
        {
            state.Fail($"{reason}:{kind}[{index}]");
            return false;
        }

        return true;
    }

    private static string Describe(Exception exception, string kind, int index)
        => exception is IdentityCredentialsPolicyException policy
            ? policy.Message
            : $"identity-credentials-policy: persistent-rejected({exception.GetType().Name}) ({kind}[{index}])";

    /// <summary>Validim PURE i konfigurimit (pa ngarkuar asnjë certifikatë).</summary>
    public static IReadOnlyList<string> ValidateConfiguration(IdentityAuthorityOptions options, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        if (!Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer))
        {
            errors.Add("issuer-missing-or-relative");
        }
        else if (!string.Equals(issuer.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !(isDevelopment && string.Equals(issuer.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("issuer-scheme-not-allowed");
        }

        if (options.Audiences.Length == 0 || options.Audiences.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("audiences-missing");
        }

        if (options.Jose.SignatureAlgorithms.Length == 0 || options.Jose.SignatureAlgorithms.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("jose-signature-algorithms-missing");
        }

        if (options.Jose.KeyManagementAlgorithms.Length == 0 || options.Jose.KeyManagementAlgorithms.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("jose-key-management-algorithms-missing");
        }

        if (options.Jose.ContentEncryptionAlgorithms.Length == 0 || options.Jose.ContentEncryptionAlgorithms.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("jose-content-encryption-algorithms-missing");
        }

        if (options.ClockSkewSeconds is null)
        {
            errors.Add("clock-skew-missing");
        }
        else if (options.ClockSkewSeconds < 0 || options.ClockSkewSeconds > IdentityCredentialsPolicy.MaxClockSkewSeconds)
        {
            errors.Add("clock-skew-out-of-bounds");
        }

        // B.2b: kufijtë e interaksionit kanë vlera të paracaktuara TË DEKLARUARA
        // (konstante), por çdo mbishkrim nga konfigurimi validohet fail-closed —
        // zero kufi i pakufizuar, zero çaktivizim i lockout-it/rate-limit-it.
        var interaction = options.Interaction;
        if (interaction.SessionMinutes < 1 || interaction.SessionMinutes > IdentityInteractionOptions.MaxSessionMinutes)
        {
            errors.Add("interaction-session-minutes-out-of-bounds");
        }

        if (interaction.MaxFailedAccessAttempts < 1
            || interaction.MaxFailedAccessAttempts > IdentityInteractionOptions.MaxLockoutAttempts)
        {
            errors.Add("interaction-lockout-attempts-out-of-bounds");
        }

        if (interaction.LockoutMinutes < 1 || interaction.LockoutMinutes > IdentityInteractionOptions.MaxLockoutMinutes)
        {
            errors.Add("interaction-lockout-minutes-out-of-bounds");
        }

        if (interaction.LoginPermitLimit < 1
            || interaction.LoginPermitLimit > IdentityInteractionOptions.MaxLoginPermitLimit)
        {
            errors.Add("interaction-login-permit-limit-out-of-bounds");
        }

        if (interaction.LoginWindowSeconds < 1
            || interaction.LoginWindowSeconds > IdentityInteractionOptions.MaxLoginWindowSeconds)
        {
            errors.Add("interaction-login-window-seconds-out-of-bounds");
        }

        return errors;
    }
}

/// <summary>
/// Tri politika JOSE të NDARA, ekzakte (ordinal), PURE: (1) firma e JWS-së së
/// brendshme; (2) key-management i JWE-së së jashtme; (3) content-encryption i
/// JWE-së së jashtme. Kriptografia mbetet te framework-u — këtu vetëm vendime
/// allowlist-i mbi emra algoritmesh (asnjë material tokeni/çelësi nuk mbahet).
/// </summary>
public sealed class IdentityJosePolicy
{
    private IdentityJosePolicy(
        ImmutableArray<string> signatureAlgorithms,
        ImmutableArray<string> keyManagementAlgorithms,
        ImmutableArray<string> contentEncryptionAlgorithms)
    {
        SignatureAlgorithms = signatureAlgorithms;
        KeyManagementAlgorithms = keyManagementAlgorithms;
        ContentEncryptionAlgorithms = contentEncryptionAlgorithms;
    }

    public ImmutableArray<string> SignatureAlgorithms { get; }

    public ImmutableArray<string> KeyManagementAlgorithms { get; }

    public ImmutableArray<string> ContentEncryptionAlgorithms { get; }

    public static IdentityJosePolicy From(IdentityAuthorityJoseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new IdentityJosePolicy(
            [.. options.SignatureAlgorithms.Where(algorithm => !string.IsNullOrWhiteSpace(algorithm)).Distinct(StringComparer.Ordinal)],
            [.. options.KeyManagementAlgorithms.Where(algorithm => !string.IsNullOrWhiteSpace(algorithm)).Distinct(StringComparer.Ordinal)],
            [.. options.ContentEncryptionAlgorithms.Where(algorithm => !string.IsNullOrWhiteSpace(algorithm)).Distinct(StringComparer.Ordinal)]);
    }

    public bool AllowsSignature(string? algorithm)
        => algorithm is not null && SignatureAlgorithms.Contains(algorithm, StringComparer.Ordinal);

    public bool AllowsKeyManagement(string? algorithm)
        => algorithm is not null && KeyManagementAlgorithms.Contains(algorithm, StringComparer.Ordinal);

    public bool AllowsContentEncryption(string? algorithm)
        => algorithm is not null && ContentEncryptionAlgorithms.Contains(algorithm, StringComparer.Ordinal);

    /// <summary>
    /// Delegate-i i validator-it REAL (IdentityModel `AlgorithmValidator`): për
    /// `enc` thirret me token-in e JASHTËM të enkriptuar (politika 3, dhe emri
    /// duhet të përputhet ekzakt me `enc` të header-it); për firmën thirret me
    /// token-in e BRENDSHËM (politika 1). Çdo rast tjetër → refuzim.
    /// </summary>
    public bool ValidateAlgorithm(
        string algorithm, SecurityKey securityKey, SecurityToken securityToken, TokenValidationParameters validationParameters)
        => securityToken is JsonWebToken { IsEncrypted: true } encrypted
            ? string.Equals(algorithm, encrypted.Enc, StringComparison.Ordinal) && AllowsContentEncryption(algorithm)
            : securityToken is JsonWebToken { IsEncrypted: false } && AllowsSignature(algorithm);

    /// <summary>
    /// Vendim PURE mbi header-in e JASHTËM të një tokeni compact — pa kriptografi
    /// dhe pa ndërtuar `JsonWebToken` (parser-i i tij dekodon EDHE segmentet e
    /// tjera dhe mund të hedhë `FormatException` për encrypted-key të keqformuar):
    /// kërkohen saktësisht 5 segmente jo-bosh (JWE compact), dekodohet VETËM
    /// segmenti 0 brenda catch-all, lexohen `alg`/`enc` si string. null = i pranuar;
    /// ndryshe klasa e refuzimit (asnjë përmbajtje header-i/tokeni).
    /// </summary>
    public string? RejectOuterHeader(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "jose-token-missing";
        }

        var segments = token.Split('.');
        if (segments.Length == 3)
        {
            return "jose-token-not-encrypted";
        }

        if (segments.Length != 5 || segments.Any(string.IsNullOrEmpty))
        {
            return "jose-token-malformed";
        }

        string? alg;
        string? enc;
        try
        {
            using var header = System.Text.Json.JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(segments[0]));
            if (header.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return "jose-token-malformed";
            }

            alg = header.RootElement.TryGetProperty("alg", out var algElement)
                && algElement.ValueKind == System.Text.Json.JsonValueKind.String ? algElement.GetString() : null;
            enc = header.RootElement.TryGetProperty("enc", out var encElement)
                && encElement.ValueKind == System.Text.Json.JsonValueKind.String ? encElement.GetString() : null;
        }
        catch (Exception)
        {
            // Çdo dështim dekodimi/parsimi (base64url, JSON, madhësi) = malformed, kurrë 500.
            return "jose-token-malformed";
        }

        if (string.IsNullOrEmpty(alg))
        {
            return "jose-key-management-algorithm-missing";
        }

        if (!AllowsKeyManagement(alg))
        {
            return "jose-key-management-algorithm-not-allowed";
        }

        if (string.IsNullOrEmpty(enc))
        {
            return "jose-content-encryption-algorithm-missing";
        }

        if (!AllowsContentEncryption(enc))
        {
            return "jose-content-encryption-algorithm-not-allowed";
        }

        return null;
    }

    /// <summary>Handler-i OpenIddict (para `ValidateIdentityModelToken`): refuzim gjenerik, kurrë 500.</summary>
    internal ValueTask ValidateOuterHeader(OpenIddictValidationEvents.ValidateTokenContext context)
    {
        if (context.IsRejected)
        {
            return default;
        }

        if (RejectOuterHeader(context.Token) is not null)
        {
            context.Reject(
                error: OpenIddictConstants.Errors.InvalidToken,
                description: "The specified token is invalid.");
        }

        return default;
    }

    /// <summary>
    /// I njëjti gate mbi stack-un e SERVER-it (çdo artefakt i mbrojtur që konsumon
    /// serveri): refuzim para çdo dekriptimi, kurrë 500, kurrë token — asnjë klasë
    /// refuzimi nuk del jashtë. B.2c: gabimi është i SAKTË sipas endpoint-it dhe
    /// artefaktit — `invalid_grant` te token endpoint-i (kodi i autorizimit),
    /// `invalid_token` te UserInfo (bearer access token); kontrollet JOSE janë identike.
    /// </summary>
    internal ValueTask ValidateServerOuterHeader(OpenIddictServerEvents.ValidateTokenContext context)
    {
        if (context.IsRejected)
        {
            return default;
        }

        if (RejectOuterHeader(context.Token) is not null)
        {
            if (context.EndpointType == OpenIddictServerEndpointType.UserInfo)
            {
                context.Reject(
                    error: OpenIddictConstants.Errors.InvalidToken,
                    description: "The specified token is invalid.");
            }
            else
            {
                context.Reject(
                    error: OpenIddictConstants.Errors.InvalidGrant,
                    description: "The specified grant is invalid.");
            }
        }

        return default;
    }

    /// <summary>
    /// Koherencë issuance↔validation (B2): algoritmet REALE që lëshuesi do të
    /// përdorë (firma e çdo signing credential; `alg`/`enc` i çdo encryption
    /// credential) duhet të pranohen nga policy-ja e validator-it — ndryshe host-i
    /// do të niste por do të refuzonte token-at e vet → startup FAIL.
    /// </summary>
    public IReadOnlyList<string> EvaluateCoherence(
        IReadOnlyList<string?> signingAlgorithms, IReadOnlyList<(string? Alg, string? Enc)> encryptionAlgorithms)
    {
        ArgumentNullException.ThrowIfNull(signingAlgorithms);
        ArgumentNullException.ThrowIfNull(encryptionAlgorithms);
        var violations = new List<string>();
        for (var index = 0; index < signingAlgorithms.Count; index++)
        {
            if (!AllowsSignature(signingAlgorithms[index]))
            {
                violations.Add($"jose-signature-algorithm-mismatch:signing[{index}]:{signingAlgorithms[index] ?? "none"}");
            }
        }

        for (var index = 0; index < encryptionAlgorithms.Count; index++)
        {
            if (!AllowsKeyManagement(encryptionAlgorithms[index].Alg))
            {
                violations.Add($"jose-key-management-algorithm-mismatch:encryption[{index}]:{encryptionAlgorithms[index].Alg ?? "none"}");
            }

            if (!AllowsContentEncryption(encryptionAlgorithms[index].Enc))
            {
                violations.Add($"jose-content-encryption-algorithm-mismatch:encryption[{index}]:{encryptionAlgorithms[index].Enc ?? "none"}");
            }
        }

        return violations;
    }
}

/// <summary>State i BRENDSHËM i dështimeve të regjistrimit — jeton vetëm brenda `AddIdentityAuthority`.</summary>
internal sealed class IdentityAuthorityStartupState
{
    private readonly List<string> _failures = [];

    internal void Fail(string failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);
        _failures.Add(failure);
    }

    /// <summary>Snapshot i PANDRYSHUESHËM (kopje) — e vetmja formë që del nga regjistrimi.</summary>
    internal IdentityAuthorityStartupSnapshot Freeze() => new([.. _failures]);
}

/// <summary>Snapshot i ngrirë i dështimeve të regjistrimit: vetëm `ImmutableArray&lt;string&gt;`, pa mutator.</summary>
internal sealed class IdentityAuthorityStartupSnapshot
{
    internal IdentityAuthorityStartupSnapshot(ImmutableArray<string> failures)
    {
        Failures = failures;
    }

    public ImmutableArray<string> Failures { get; }
}

/// <summary>
/// Kufiri (1): konfigurimi + dështimet e shtyra të regjistrimit. Ekzekutohet nga
/// `ValidateOnStart` para çdo hosted service; dështimi = `IdentityCredentialsPolicyException`
/// me klasa pa material.
/// </summary>
internal sealed class IdentityAuthorityOptionsValidator(bool isDevelopment, IdentityAuthorityStartupSnapshot snapshot)
    : IValidateOptions<IdentityAuthorityOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityAuthorityOptions options)
    {
        if (!snapshot.Failures.IsDefaultOrEmpty)
        {
            throw new IdentityCredentialsPolicyException(snapshot.Failures);
        }

        var errors = IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(options, isDevelopment);
        if (errors.Count > 0)
        {
            throw new IdentityCredentialsPolicyException([.. errors.Select(error => $"configuration:{error}")]);
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Kufiri (2): policy-ja mbi credentials REALE të `OpenIddictServerOptions` (pas
/// konfigurimit të plotë të OpenIddict) + koherenca JOSE issuance↔validation.
/// Ekzekutohet nga `ValidateOnStart` para çdo hosted service.
/// </summary>
internal sealed class IdentityAuthorityCredentialsValidator(bool isDevelopment, IdentityJosePolicy jose, TimeProvider timeProvider)
    : IValidateOptions<OpenIddictServerOptions>
{
    public ValidateOptionsResult Validate(string? name, OpenIddictServerOptions options)
    {
        var violations = new List<string>(IdentityCredentialsPolicy.Evaluate(
            isDevelopment,
            [.. options.SigningCredentials.Select(credential => credential.Key)],
            [.. options.EncryptionCredentials.Select(credential => credential.Key)],
            timeProvider.GetUtcNow()));
        violations.AddRange(jose.EvaluateCoherence(
            [.. options.SigningCredentials.Select(credential => credential.Algorithm)],
            [.. options.EncryptionCredentials.Select(credential => (credential.Alg, credential.Enc))]));
        if (violations.Count > 0)
        {
            throw new IdentityCredentialsPolicyException(violations);
        }

        return ValidateOptionsResult.Success;
    }
}
