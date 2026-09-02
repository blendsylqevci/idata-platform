using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation;
using OpenIddict.Validation.AspNetCore;

namespace Idata.Platform.Modules.Identity.Infrastructure;

/// <summary>
/// Wiring i VETËM i authority-t OpenIddict co-located (ADR-028, Increment B.2a)
/// — konsumohet nga composition root-i (API host) pa asnjë referencë direkte
/// OpenIddict aty:
///  - OpenIddict **Server** (issuer/endpoint-e/credentials/PKCE) me
///    Authorization Code + PKCE të DEKLARUAR si flow-i target; endpoint-i i
///    autorizimit është FAIL-CLOSED (refuzon çdo kërkesë) derisa E2 të
///    implementohet — zero implicit, zero password, zero client-credentials,
///    zero refresh në production;
///  - OpenIddict **Validation** lokale (`UseLocalServer`) + handler-i ASP.NET
///    Core: issuer, audiences, algorithm allowlist, lifetime/nbf/exp, clock
///    skew i kufizuar, token-entry validation (revocation) — unknown → 401;
///  - policy FAIL-CLOSED e credentials (mbi credentials REALE) në STARTUP
///    (<see cref="IdentityCredentialsStartupGuard"/>): `StartAsync` dështon për
///    konfigurim/credentials të munguar, të pavlefshëm ose të ndaluar për
///    environment-in. Përjashtimi i VETËM, eksplicit dhe i testuar:
///    <see cref="IsDesignTimeDocumentGenerationHost"/> (gjenerimi build-time i
///    OpenAPI starton host-in brenda procesit të tool-it pa konfigurim
///    deployment-i dhe pa shërbyer trafik);
///  - skema default e autentikimit = handler-i i validimit OpenIddict.
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

        // Validimi i konfigurimit: IValidateOptions për çdo konsumator + guard-i i
        // startup-it si pika e VETME fail-closed (jo ValidateOnStart, që të mos
        // ekzistojnë dy pika me përjashtime të ndryshme).
        services.AddOptions<IdentityAuthorityOptions>().Bind(section);
        services.AddSingleton<IValidateOptions<IdentityAuthorityOptions>>(
            new IdentityAuthorityOptionsValidator(isDevelopment));
        services.TryAddSingleton(TimeProvider.System);

        // Dështimet e regjistrimit (konfigurim i pavlefshëm, certifikatë e
        // pangarkueshme, credentials development jashtë Development) shtyhen
        // FAIL-CLOSED te startup guard — kurrë të heshtura, kurrë me material.
        var startup = new IdentityAuthorityStartupState();
        services.AddSingleton(startup);

        var configurationErrors = ValidateConfiguration(authority, isDevelopment);
        var configured = configurationErrors.Count == 0;
        foreach (var error in configurationErrors)
        {
            startup.Fail($"configuration:{error}");
        }

        services.AddOpenIddict()
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris(AuthorizationEndpointPath)
                    .SetTokenEndpointUris(TokenEndpointPath);

                // Flow-i target i ADR-028 §4: Authorization Code + PKCE i DETYRUESHËM.
                // Interaktiviteti (login/consent) = E2 OPEN → handler fail-closed më poshtë.
                options.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange();

                // Zero passthrough: asnjë endpoint OpenIddict nuk i delegohet kodit tonë.
                options.UseAspNetCore();

                // E2 OPEN — çdo kërkesë autorizimi refuzohet me përgjigje OAuth 2.0
                // të kontrolluar (jo 500, kurrë kod autorizimi).
                options.AddEventHandler<OpenIddictServerEvents.HandleAuthorizationRequestContext>(descriptor =>
                    descriptor.UseInlineHandler(context =>
                    {
                        context.Reject(
                            error: OpenIddictConstants.Errors.UnsupportedResponseType,
                            description: "identity-authority: interactive authorization is not available in this increment.");
                        return default;
                    }));

                if (configured)
                {
                    options.SetIssuer(new Uri(authority.Issuer!, UriKind.Absolute));
                    RegisterCredentials(options, authority.Credentials, isDevelopment, startup);
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
                        // entry-i i store-it kur token-entry validation është aktiv) DHE
                        // nga validimi i lifetime-it të IdentityModel më poshtë.
                        validation.TokenValidationParameters.ClockSkew = clockSkew;

                        // OpenIddict e çaktivizon default lifetime-in e IdentityModel (e
                        // validon exp vetë, nga entry-i). Aktivizimi = kontroll SHTESË i
                        // validator-it real mbi `exp`/`nbf` të vetë JWT-së (jo validator
                        // paralel): një JWT me exp të skaduar ose nbf të ardhshëm jashtë
                        // skew-it refuzohet edhe nëse entry-i i store-it është valid.
                        validation.TokenValidationParameters.ValidateLifetime = true;
                    });
                }
            });

        if (configured)
        {
            // Allowlist-i i algoritmeve: firma = VETËM ato të konfiguruara; JWE-ja
            // (alg/enc) = VETËM ato që prodhojnë credentials REALE të enkriptimit
            // (të importuara nga server-i përmes UseLocalServer) — të derivuara,
            // jo hardcode; çdo algoritëm tjetër → refuzim. PostConfigure pas
            // integrimit të server-it, që lista të pasqyrojë credentials reale.
            var signatureAlgorithms = authority.AllowedAlgorithms.ToArray();
            services.AddOptions<OpenIddictValidationOptions>().PostConfigure(validation =>
                validation.TokenValidationParameters.ValidAlgorithms = signatureAlgorithms
                    .Concat(validation.EncryptionCredentials.SelectMany(credential => new[] { credential.Alg, credential.Enc }))
                    .Where(algorithm => !string.IsNullOrEmpty(algorithm))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray());
        }

        services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        services.AddAuthorization();

        // Guard-i i startup-it: dështimet e shtyra + policy mbi credentials REALE
        // (edhe nëse dikush shton credentials jashtë këtij wiring-u).
        services.AddHostedService<IdentityCredentialsStartupGuard>();

        return services;
    }

    /// <summary>
    /// Emrat e entry assembly-t të tool-it zyrtar të gjenerimit build-time të
    /// OpenAPI (`Microsoft.Extensions.ApiDescription.Server` → `dotnet-getdocument`
    /// → `GetDocument.Insider`). Tool-i e ekzekuton `Program` deri te
    /// `ApplicationStarted` brenda procesit të VET, pa konfigurim deployment-i.
    /// </summary>
    public static IReadOnlyList<string> DesignTimeDocumentGenerationEntryAssemblies { get; } =
        ["GetDocument.Insider", "dotnet-getdocument"];

    /// <summary>
    /// TRUE vetëm kur procesi aktual është tool-i i gjenerimit build-time të
    /// OpenAPI. Sinjali është identiteti i PROCESIT (assembly-i i entry point-it
    /// nga command line-i i procesit: `dotnet <entry>.dll …`) — jo environment,
    /// konfigurim apo argument i aplikacionit (të kontrollueshëm nga operatori/
    /// deployment-i). Lexohet pa reflection (ENG-006: manifesti i aksesit dinamik
    /// në production mbetet BOSH). Host-i real (`Idata.Platform.Api`) dhe host-et
    /// e testeve japin FALSE → guard-i fail-closed vlerësohet gjithmonë.
    /// </summary>
    public static bool IsDesignTimeDocumentGenerationHost
        => IsDesignTimeDocumentGenerationEntry(Environment.GetCommandLineArgs());

    /// <summary>Forma PURE e predikatit (e testueshme): argumenti i parë = entry-i i procesit.</summary>
    public static bool IsDesignTimeDocumentGenerationEntry(IReadOnlyList<string> processCommandLine)
    {
        ArgumentNullException.ThrowIfNull(processCommandLine);
        if (processCommandLine.Count == 0 || string.IsNullOrWhiteSpace(processCommandLine[0]))
        {
            return false;
        }

        var entry = Path.GetFileNameWithoutExtension(processCommandLine[0]);
        return DesignTimeDocumentGenerationEntryAssemblies.Contains(entry, StringComparer.Ordinal);
    }

    private static void RegisterCredentials(
        OpenIddictServerBuilder options,
        IdentityAuthorityCredentialsOptions credentials,
        bool isDevelopment,
        IdentityAuthorityStartupState startup)
    {
        var development = credentials.Development;
        var hasDevelopmentConfiguration = !string.IsNullOrWhiteSpace(development.Signing)
            || !string.IsNullOrWhiteSpace(development.Encryption);
        if (hasDevelopmentConfiguration && !isDevelopment)
        {
            startup.Fail("development-credentials-outside-development");
        }

        var now = TimeProvider.System.GetUtcNow();
        var signing = 0;
        var encryption = 0;
        for (var index = 0; index < credentials.Signing.Length; index++)
        {
            if (TryLoadValidPersistent(credentials.Signing[index], "signing", index, now, startup, out var certificate))
            {
                try
                {
                    options.AddSigningCertificate(certificate);
                    signing++;
                }
                catch (Exception exception)
                {
                    startup.Fail(Describe(exception, "signing", index));
                }
            }
        }

        for (var index = 0; index < credentials.Encryption.Length; index++)
        {
            if (TryLoadValidPersistent(credentials.Encryption[index], "encryption", index, now, startup, out var certificate))
            {
                try
                {
                    options.AddEncryptionCertificate(certificate);
                    encryption++;
                }
                catch (Exception exception)
                {
                    startup.Fail(Describe(exception, "encryption", index));
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
                    startup.Fail("development-signing-unknown-choice");
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
                    startup.Fail("development-encryption-unknown-choice");
                    break;
            }
        }

        if (signing == 0)
        {
            startup.Fail("signing-missing");
        }

        if (encryption == 0)
        {
            startup.Fail("encryption-missing");
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
        IdentityAuthorityStartupState startup,
        out System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        certificate = null!;
        try
        {
            certificate = IdentityCredentialsPolicy.LoadPersistentCertificate(source, kind, index);
        }
        catch (IdentityCredentialsPolicyException exception)
        {
            startup.Fail(exception.Message);
            return false;
        }

        if (!IdentityCredentialsPolicy.IsValidPersistent(
                new Microsoft.IdentityModel.Tokens.X509SecurityKey(certificate), now, out var reason))
        {
            startup.Fail($"{reason}:{kind}[{index}]");
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

        if (options.AllowedAlgorithms.Length == 0 || options.AllowedAlgorithms.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("allowed-algorithms-missing");
        }

        if (options.ClockSkewSeconds is null)
        {
            errors.Add("clock-skew-missing");
        }
        else if (options.ClockSkewSeconds < 0 || options.ClockSkewSeconds > IdentityCredentialsPolicy.MaxClockSkewSeconds)
        {
            errors.Add("clock-skew-out-of-bounds");
        }

        return errors;
    }
}

/// <summary>Dështimet e regjistrimit të shtyra te startup (klasa + identifikues jo-sekret vetëm).</summary>
public sealed class IdentityAuthorityStartupState
{
    private readonly List<string> _failures = [];

    public IReadOnlyList<string> Failures => _failures;

    public void Fail(string failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);
        _failures.Add(failure);
    }
}

internal sealed class IdentityAuthorityOptionsValidator(bool isDevelopment) : IValidateOptions<IdentityAuthorityOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityAuthorityOptions options)
    {
        var errors = IdentityAuthorityServiceCollectionExtensions.ValidateConfiguration(options, isDevelopment);
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors.Select(error => $"identity-authority-configuration: {error}"));
    }
}

/// <summary>
/// Startup guard (pika e VETME fail-closed): (1) dështimet e shtyra të
/// regjistrimit (konfigurim, ngarkim certifikate, development jashtë
/// Development); (2) policy-ja mbi credentials REALE të
/// `OpenIddictServerOptions` (pas konfigurimit të plotë të OpenIddict). Çdo
/// dështim ndal host-in. Përjashtim i vetëm: host-i design-time i gjenerimit
/// të dokumentit (i logohet si warning, s'shërben trafik).
/// </summary>
internal sealed class IdentityCredentialsStartupGuard(
    IdentityAuthorityStartupState startup,
    IOptionsMonitor<OpenIddictServerOptions> serverOptions,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<IdentityCredentialsStartupGuard> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (IdentityAuthorityServiceCollectionExtensions.IsDesignTimeDocumentGenerationHost)
        {
            logger.LogWarning(
                "identity-authority: design-time document generation host — credentials policy not evaluated (no traffic is served)");
            return Task.CompletedTask;
        }

        if (startup.Failures.Count > 0)
        {
            throw new IdentityCredentialsPolicyException(startup.Failures);
        }

        var options = serverOptions.Get(Options.DefaultName);
        var violations = IdentityCredentialsPolicy.Evaluate(
            environment.IsDevelopment(),
            [.. options.SigningCredentials.Select(credential => credential.Key)],
            [.. options.EncryptionCredentials.Select(credential => credential.Key)],
            timeProvider.GetUtcNow());
        if (violations.Count > 0)
        {
            throw new IdentityCredentialsPolicyException(violations);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
