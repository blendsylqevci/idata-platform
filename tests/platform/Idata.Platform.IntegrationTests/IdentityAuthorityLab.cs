using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Idata.Platform.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation.AspNetCore;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Certifikata SINTETIKE të gjeneruara në runtime (RSA 2048, self-signed, key
/// usage korrekt) — kurrë të shkruara në repo; PFX-i kalon vetëm në memorie
/// si base64 te konfigurimi i host-it të testit.
/// </summary>
public static class SyntheticCertificates
{
    public static X509Certificate2 Signing(string subject = "CN=synthetic-signing",
        DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null)
        => Create(subject, X509KeyUsageFlags.DigitalSignature, notBefore, notAfter);

    public static X509Certificate2 Encryption(string subject = "CN=synthetic-encryption",
        DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null)
        => Create(subject, X509KeyUsageFlags.KeyEncipherment, notBefore, notAfter);

    private static X509Certificate2 Create(
        string subject, X509KeyUsageFlags usage, DateTimeOffset? notBefore, DateTimeOffset? notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));
        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(notBefore ?? now.AddMinutes(-5), notAfter ?? now.AddDays(30));
    }

    public static string ToPfxBase64(X509Certificate2 certificate, string passphrase)
        => Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12, passphrase));

    /// <summary>PFX VETËM me certifikatë publike (pa çelës privat).</summary>
    public static string ToPublicOnlyPfxBase64(X509Certificate2 certificate, string passphrase)
    {
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        return Convert.ToBase64String(publicOnly.Export(X509ContentType.Pkcs12, passphrase));
    }
}

/// <summary>
/// Konfigurim identity EKSPLICIT për çdo host testi mbi `Program` (B.2a e bën
/// startup-in fail-closed pa konfigurim): certifikata persistent SINTETIKE
/// të gjeneruara një herë për proces (të vlefshme në ÇDO environment —
/// Development/Staging/Production/i panjohur), product-neutral, zero material
/// në repo. Host settings → args → të dukshme në kohën e regjistrimit.
/// </summary>
public static class IdentityTestHost
{
    private const string PfxPassphrase = "synthetic-test-host-pfx-passphrase";

    private static readonly Lazy<(string SigningPfx, string EncryptionPfx)> Credentials = new(() =>
    {
        using var signing = SyntheticCertificates.Signing("CN=synthetic-test-host-signing");
        using var encryption = SyntheticCertificates.Encryption("CN=synthetic-test-host-encryption");
        return (SyntheticCertificates.ToPfxBase64(signing, PfxPassphrase), SyntheticCertificates.ToPfxBase64(encryption, PfxPassphrase));
    });

    public static void ApplyExplicitIdentity(IWebHostBuilder builder)
    {
        var settings = new IdentityAuthorityHostSettings
        {
            Signing = [(Credentials.Value.SigningPfx, PfxPassphrase)],
            Encryption = [(Credentials.Value.EncryptionPfx, PfxPassphrase)],
        };
        foreach (var (key, value) in settings.ToHostConfiguration())
        {
            builder.UseSetting(key, value);
        }
    }
}

/// <summary>
/// Zëvendësuesi i `new WebApplicationFactory&lt;Program&gt;()` për testet ekzistuese:
/// host-i REAL + konfigurim identity eksplicit sintetik (asnjë override tjetër).
/// </summary>
public class PlatformTestHostFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => IdentityTestHost.ApplyExplicitIdentity(builder);
}

/// <summary>Konfigurimi eksplicit i një host-i authority të testit (product-neutral, sintetik).</summary>
public sealed record IdentityAuthorityHostSettings
{
    public string Environment { get; init; } = "Production";

    public string? Issuer { get; init; } = "https://localhost/";

    public string[] Audiences { get; init; } = ["synthetic-api"];

    /// <summary>JOSE (1): allowlist i firmës së JWS-së së brendshme.</summary>
    public string[] SignatureAlgorithms { get; init; } = ["RS256"];

    /// <summary>JOSE (2): allowlist i key-management-it të JWE-së së jashtme.</summary>
    public string[] KeyManagementAlgorithms { get; init; } = ["RSA-OAEP"];

    /// <summary>JOSE (3): allowlist i content-encryption-it të JWE-së së jashtme.</summary>
    public string[] ContentEncryptionAlgorithms { get; init; } = ["A256CBC-HS512"];

    public int? ClockSkewSeconds { get; init; } = 0;

    public string? DevelopmentSigning { get; init; }

    public string? DevelopmentEncryption { get; init; }

    public (string PfxBase64, string Passphrase)[] Signing { get; init; } = [];

    public (string PfxBase64, string Passphrase)[] Encryption { get; init; } = [];

    /// <summary>Fut TË GJITHË seksionin `Identity:Authority` (false = seksion i munguar).</summary>
    public bool IncludeSection { get; init; } = true;

    /// <summary>Test-only: klient sintetik client-credentials + probe i mbrojtur (kurrë në production host).</summary>
    public bool EnableTestIssuance { get; init; }

    /// <summary>
    /// Test-only (B.2b): VETËM probe-i `[Authorize]` — pa asnjë grant shtesë, pra
    /// host-i mban saktësisht grant-et production (authorization_code + PKCE).
    /// </summary>
    public bool EnableProtectedProbe { get; init; }

    /// <summary>
    /// Test-only (B.2b C1, restart-mid-flow): key ring Data Protection i qëndrueshëm në
    /// një direktori TË PËRKOHSHME të kontrolluar nga testi — që dy host-e të njëpasnjëshëm
    /// të ndajnë të njëjtat çelësa (cookie/antiforgery). NUK është vendim production
    /// për key custody (ADR-025/026 mbeten OPEN); null = key ring efemer i host-it.
    /// </summary>
    public string? DataProtectionKeyDirectory { get; init; }

    /// <summary>Mbishkrime opsionale të kufijve të interaksionit (B.2b) — null = vlera e paracaktuar e kodit.</summary>
    public int? SessionMinutes { get; init; }

    public int? MaxFailedAccessAttempts { get; init; }

    public int? LockoutMinutes { get; init; }

    public int? LoginPermitLimit { get; init; }

    public int? LoginWindowSeconds { get; init; }

    /// <summary>
    /// Test-only: shton një çelës EPHEMERAL firmë JASHTË konfigurimit (si një
    /// regjistrim i tretë) — provon që policy-ja vlerësohet mbi credentials
    /// REALE të OpenIddict, jo vetëm mbi konfigurim.
    /// </summary>
    public bool ExtraEphemeralSigningKeyOutsideConfiguration { get; init; }

    /// <summary>Test-only: log Debug për OpenIddict/Authentication (diagnostikë lokale).</summary>
    public bool DiagnosticLogging { get; init; }

    public IReadOnlyDictionary<string, string?> ToHostConfiguration()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!IncludeSection)
        {
            return values;
        }

        const string prefix = "Identity:Authority:";
        values[prefix + "Issuer"] = Issuer;
        for (var index = 0; index < Audiences.Length; index++)
        {
            values[$"{prefix}Audiences:{index}"] = Audiences[index];
        }

        for (var index = 0; index < SignatureAlgorithms.Length; index++)
        {
            values[$"{prefix}Jose:SignatureAlgorithms:{index}"] = SignatureAlgorithms[index];
        }

        for (var index = 0; index < KeyManagementAlgorithms.Length; index++)
        {
            values[$"{prefix}Jose:KeyManagementAlgorithms:{index}"] = KeyManagementAlgorithms[index];
        }

        for (var index = 0; index < ContentEncryptionAlgorithms.Length; index++)
        {
            values[$"{prefix}Jose:ContentEncryptionAlgorithms:{index}"] = ContentEncryptionAlgorithms[index];
        }

        if (ClockSkewSeconds is not null)
        {
            values[prefix + "ClockSkewSeconds"] = ClockSkewSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (var (name, value) in ((string Name, int? Value)[])
            [("SessionMinutes", SessionMinutes), ("MaxFailedAccessAttempts", MaxFailedAccessAttempts),
             ("LockoutMinutes", LockoutMinutes), ("LoginPermitLimit", LoginPermitLimit),
             ("LoginWindowSeconds", LoginWindowSeconds)])
        {
            if (value is not null)
            {
                values[$"{prefix}Interaction:{name}"] = value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        if (DevelopmentSigning is not null)
        {
            values[prefix + "Credentials:Development:Signing"] = DevelopmentSigning;
        }

        if (DevelopmentEncryption is not null)
        {
            values[prefix + "Credentials:Development:Encryption"] = DevelopmentEncryption;
        }

        for (var index = 0; index < Signing.Length; index++)
        {
            values[$"{prefix}Credentials:Signing:{index}:Pfx"] = Signing[index].PfxBase64;
            values[$"{prefix}Credentials:Signing:{index}:PfxPassword"] = Signing[index].Passphrase;
        }

        for (var index = 0; index < Encryption.Length; index++)
        {
            values[$"{prefix}Credentials:Encryption:{index}:Pfx"] = Encryption[index].PfxBase64;
            values[$"{prefix}Credentials:Encryption:{index}:PfxPassword"] = Encryption[index].Passphrase;
        }

        return values;
    }
}

/// <summary>
/// Host i testit mbi `Program`-in REAL: konfigurimi identity jepet si host
/// settings (WebApplicationFactory i kalon si args → të dukshme te
/// `builder.Configuration` në kohën e regjistrimit), environment-i eksplicit.
/// Shtesa TEST-ONLY (vetëm kur `EnableTestIssuance`): grant client_credentials
/// + handler që nënshkruan një principal sintetik + probe controller i mbrojtur.
/// </summary>
public sealed class IdentityAuthorityFactory(IdentityAuthorityHostSettings settings) : WebApplicationFactory<Program>
{
    public IdentityAuthorityHostSettings Settings { get; } = settings;

    /// <summary>Log-et e kapura in-process (vetëm me DiagnosticLogging) — për diagnostikë të dështimeve.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<string> Logs { get; } = new();

    public string DumpLogs() => string.Join(Environment.NewLine, Logs);

    /// <summary>Parametrat EFEKTIVË të validimit (diagnostikë): skew, lifetime, algoritme, issuers, types.</summary>
    public string DumpValidationOptions()
    {
        var options = Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<OpenIddict.Validation.OpenIddictValidationOptions>>()
            .Get(Microsoft.Extensions.Options.Options.DefaultName);
        var parameters = options.TokenValidationParameters;
        return $"ClockSkew={parameters.ClockSkew} ValidateLifetime={parameters.ValidateLifetime} "
            + $"ValidAlgorithms=[{string.Join(",", parameters.ValidAlgorithms ?? [])}] "
            + $"ValidIssuers=[{string.Join(",", parameters.ValidIssuers ?? [])}] ValidIssuer={parameters.ValidIssuer} "
            + $"ValidTypes=[{string.Join(",", parameters.ValidTypes ?? [])}] Issuer={options.Issuer} "
            + $"Audiences=[{string.Join(",", options.Audiences)}] Signing={options.SigningCredentials.Count} Encryption={options.EncryptionCredentials.Count}";
    }

    private sealed class CollectingLoggerProvider(System.Collections.Concurrent.ConcurrentQueue<string> sink)
        : Microsoft.Extensions.Logging.ILoggerProvider
    {
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, sink);

        public void Dispose()
        {
        }

        private sealed class CollectingLogger(string category, System.Collections.Concurrent.ConcurrentQueue<string> sink)
            : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
                => logLevel >= Microsoft.Extensions.Logging.LogLevel.Debug;

            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
                TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (category.StartsWith("OpenIddict", StringComparison.Ordinal)
                    || category.StartsWith("Microsoft.AspNetCore.Authentication", StringComparison.Ordinal)
                    || category.StartsWith("Microsoft.AspNetCore.Authorization", StringComparison.Ordinal))
                {
                    sink.Enqueue($"[{logLevel}] {category}: {formatter(state, exception)}{(exception is null ? string.Empty : " | " + exception.GetType().Name + ": " + exception.Message)}");
                }
            }
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Settings.Environment);
        foreach (var (key, value) in Settings.ToHostConfiguration())
        {
            builder.UseSetting(key, value);
        }

        if (Settings.DiagnosticLogging)
        {
            builder.UseSetting("Logging:LogLevel:OpenIddict", "Debug");
            builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore.Authentication", "Debug");
            builder.ConfigureServices(services =>
                services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(new CollectingLoggerProvider(Logs)));
        }

        if (Settings.ExtraEphemeralSigningKeyOutsideConfiguration)
        {
            builder.ConfigureServices(services =>
                services.AddOpenIddict().AddServer(options => options.AddEphemeralSigningKey()));
        }

        if (Settings.DataProtectionKeyDirectory is { } keyDirectory)
        {
            builder.ConfigureServices(services =>
                services.AddDataProtection()
                    .SetApplicationName("idata-synthetic-interactive-restart")
                    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory)));
        }

        if (Settings.EnableProtectedProbe)
        {
            builder.ConfigureServices(services =>
                services.AddControllers().AddApplicationPart(typeof(IdentityAuthorityProbeController).Assembly));
        }

        if (!Settings.EnableTestIssuance)
        {
            return;
        }

        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(IdentityAuthorityProbeController).Assembly);
            services.AddOpenIddict().AddServer(options =>
            {
                // TEST-ONLY: grant i vetëm i lejuar për evidencë; production host-i
                // nuk e regjistron kurrë (guard arkitekturor mbi MemberRefs).
                options.AllowClientCredentialsFlow();

                options.AddEventHandler<OpenIddictServerEvents.HandleTokenRequestContext>(descriptor =>
                    descriptor.UseInlineHandler(context =>
                    {
                        if (!context.Request.IsClientCredentialsGrantType())
                        {
                            return default;
                        }

                        var identity = new ClaimsIdentity(
                            TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
                        identity.SetClaim(Claims.Subject, context.ClientId!);
                        identity.SetDestinations(static _ => [Destinations.AccessToken]);
                        var principal = new ClaimsPrincipal(identity);
                        // Parametër TEST-ONLY jo-standard (OpenIddict e injoron; `audience`
                        // standard do të validohej kundër permissions të klientit).
                        var audience = (string?)context.Request["synthetic_audience"];
                        principal.SetResources(audience ?? Settings.Audiences[0]);
                        context.SignIn(principal);
                        return default;
                    }));
            });
        });
    }

    /// <summary>
    /// Klient HTTPS (TestServer: IsHttps=true) — kërkesa e TLS-së e OpenIddict
    /// mbetet AKTIVE; pa auto-redirect (përgjigjet 302 të endpoint-it të
    /// autorizimit vëzhgohen, jo ndiqen).
    /// </summary>
    public HttpClient CreateHttpsClient()
        => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost/"),
            AllowAutoRedirect = false,
        });

    /// <summary>Regjistron një klient SINTETIK (secret i gjeneruar në runtime) në store-in real.</summary>
    public async Task<(string ClientId, string ClientSecret)> RegisterSyntheticClientAsync()
    {
        var clientId = $"synthetic-{Guid.NewGuid():N}";
        var clientSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var scope = Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            ClientType = ClientTypes.Confidential,
            Permissions = { Permissions.Endpoints.Token, Permissions.GrantTypes.ClientCredentials },
        });
        return (clientId, clientSecret);
    }

    /// <summary>Token REAL nga `/connect/token` (pipeline-i i plotë OpenIddict).</summary>
    public async Task<string> IssueAccessTokenAsync(
        HttpClient client, (string ClientId, string ClientSecret) credentials, string? audience = null)
    {
        var response = await RequestTokenAsync(client, credentials, audience);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("access_token").GetString()!;
    }

    public static Task<HttpResponseMessage> RequestTokenAsync(
        HttpClient client, (string ClientId, string ClientSecret) credentials, string? audience = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = GrantTypes.ClientCredentials,
            ["client_id"] = credentials.ClientId,
            ["client_secret"] = credentials.ClientSecret,
        };
        if (audience is not null)
        {
            form["synthetic_audience"] = audience;
        }

        return client.PostAsync(
            new Uri("/" + IdentityAuthorityServiceCollectionExtensions.TokenEndpointPath, UriKind.Relative),
            new FormUrlEncodedContent(form),
            TestContext.Current.CancellationToken);
    }

    public static Task<HttpResponseMessage> ProbeAsync(HttpClient client, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/__b2a/protected", UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}

/// <summary>Probe TEST-ONLY i mbrojtur nga skema e validimit OpenIddict (pipeline-i real i Program-it).</summary>
[ApiController]
[Route("__b2a")]
public sealed class IdentityAuthorityProbeController : ControllerBase
{
    /// <summary>`[Authorize]` i ZHVESHUR — mbrojtja varet VETËM nga skema default e host-it real (C1).</summary>
    [HttpGet("protected")]
    [Authorize]
    public IActionResult Protected() => Ok(new { subject = User.GetClaim(Claims.Subject) });
}

/// <summary>
/// Crafting i tokenëve VETËM me çelësat sintetikë të testit (të cilët testi i
/// zotëron): dekripton tokenin REAL, kopjon claims-at dhe ri-nënshkruan/
/// ri-enkripton me ndryshime të izoluara (iss/typ/nbf/exp/alg/çelës).
/// Kontrolli "i pandryshuar → 200" provon besnikërinë e crafting-ut.
/// </summary>
public static class SyntheticTokenCrafter
{
    public static Dictionary<string, object> DecryptClaims(X509Certificate2 signing, X509Certificate2 encryption, string encodedToken)
    {
        var handler = new JsonWebTokenHandler();
        var result = handler.ValidateTokenAsync(encodedToken, new TokenValidationParameters
        {
            TokenDecryptionKey = new X509SecurityKey(encryption),
            IssuerSigningKey = new X509SecurityKey(signing),
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateLifetime = false,
            ValidateTokenReplay = false,
            ValidTypes = null,
        }).GetAwaiter().GetResult();
        if (!result.IsValid)
        {
            throw new InvalidOperationException("Dekriptimi i tokenit sintetik dështoi.", result.Exception);
        }

        var inner = ((JsonWebToken)result.SecurityToken).InnerToken ?? (JsonWebToken)result.SecurityToken;
        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(inner.EncodedPayload));
        var claims = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in payload.RootElement.EnumerateObject())
        {
            claims[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.Number => property.Value.GetInt64(),
                JsonValueKind.String => property.Value.GetString()!,
                JsonValueKind.Array => property.Value.EnumerateArray().Select(item => item.GetString()!).ToArray(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => property.Value.GetRawText(),
            };
        }

        return claims;
    }

    /// <summary>Dekripton JWE-në reale dhe kthen JWS-në e BRENDSHME compact TË PANDRYSHUAR.</summary>
    public static string ExtractInnerJws(X509Certificate2 signing, X509Certificate2 encryption, string encodedToken)
    {
        var handler = new JsonWebTokenHandler();
        var result = handler.ValidateTokenAsync(encodedToken, new TokenValidationParameters
        {
            TokenDecryptionKey = new X509SecurityKey(encryption),
            IssuerSigningKey = new X509SecurityKey(signing),
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateLifetime = false,
            ValidateTokenReplay = false,
            ValidTypes = null,
        }).GetAwaiter().GetResult();
        if (!result.IsValid)
        {
            throw new InvalidOperationException("Dekriptimi i tokenit sintetik dështoi.", result.Exception);
        }

        var outer = (JsonWebToken)result.SecurityToken;
        return (outer.InnerToken ?? throw new InvalidOperationException("Token-i nuk është JWE me JWS të brendshëm.")).EncodedToken;
    }

    /// <summary>Header-i i jashtëm (JOSE) i JWE-së, pa asnjë kriptografi.</summary>
    public static IReadOnlyDictionary<string, object> OuterHeader(string encodedToken)
    {
        var segment = encodedToken.Split('.')[0];
        using var document = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(segment));
        return document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => (object)(property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText()),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Ri-enkripton të NJËJTIN inner JWS (firmë, claims, entry id, typ, iss, aud,
    /// lifetime të pandryshuara) me `alg`/`enc` të jashtëm të zgjedhur — vetëm
    /// wrapping-u i CEK-ut, `alg`/`enc` dhe tag/AAD ndryshojnë. Header-i i jashtëm
    /// riprodhon `typ`/`cty` të tokenit real.
    /// </summary>
    public static string ReEncrypt(
        string innerJws, X509Certificate2 encryption, string keyManagementAlgorithm, string contentEncryptionAlgorithm,
        IReadOnlyDictionary<string, object> outerHeader)
    {
        var handler = new JsonWebTokenHandler();
        var additional = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var name in (string[])["typ", "cty"])
        {
            if (outerHeader.TryGetValue(name, out var value))
            {
                additional[name] = value;
            }
        }

        return handler.EncryptToken(
            innerJws,
            new EncryptingCredentials(new X509SecurityKey(encryption), keyManagementAlgorithm, contentEncryptionAlgorithm),
            additional);
    }

    /// <summary>Zëvendëson header-in e jashtëm me JSON arbitrar (malformed/missing alg-enc) — pa kriptografi.</summary>
    public static string WithOuterHeader(string encodedToken, string headerJson)
    {
        var parts = encodedToken.Split('.');
        parts[0] = Base64UrlEncoder.Encode(headerJson);
        return string.Join('.', parts);
    }

    public static string Create(
        IDictionary<string, object> claims,
        X509Certificate2 signing,
        X509Certificate2 encryption,
        string algorithm = SecurityAlgorithms.RsaSha256,
        string tokenType = JsonWebTokenTypes.AccessToken)
    {
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            SigningCredentials = new SigningCredentials(new X509SecurityKey(signing), algorithm),
            EncryptingCredentials = new EncryptingCredentials(
                new X509SecurityKey(encryption), SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512),
            TokenType = tokenType,
        });
    }
}
