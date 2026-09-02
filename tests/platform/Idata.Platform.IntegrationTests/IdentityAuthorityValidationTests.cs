using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Idata.Platform.Modules.Identity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Validimi REAL i tokenëve nga middleware-i i validimit OpenIddict
/// (`UseLocalServer` + ASP.NET Core) mbi PostgreSQL sintetik: issuance
/// përmes pipeline-it REAL (`/connect/accessToken`, klient sintetik test-only) dhe
/// çdo rast negativ = 401 (pa accessToken, garbage, tampered, audience/issuer i
/// gabuar, çelës i panjohur, algoritëm i palejuar, i skaduar, nbf e ardhme,
/// typ i gabuar, i revokuar), clock skew i kufizuar (brenda → 200, jashtë →
/// 401), restart me credentials të njëjta dhe rotation me overlap.
/// Rastet kohore/typ/iss përdorin crafting VETËM me çelësat sintetikë të
/// testit, me kontroll "i pandryshuar → 200".
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class IdentityAuthorityValidationTests
{
    private const string PfxPassphrase = "synthetic-pfx-passphrase-not-confidential";

    private static (string, string) Pfx(X509Certificate2 certificate)
        => (SyntheticCertificates.ToPfxBase64(certificate, PfxPassphrase), PfxPassphrase);

    /// <summary>DB sintetike e migruar + certifikata + host authority REAL me issuance test-only.</summary>
    private sealed class Lab : IAsyncDisposable
    {
        public PlatformTestDatabase Database { get; private init; } = null!;

        public X509Certificate2 Signing { get; } = SyntheticCertificates.Signing();

        public X509Certificate2 Encryption { get; } = SyntheticCertificates.Encryption();

        private string? previousDatabase;

        public IdentityAuthorityHostSettings Settings => new()
        {
            Environment = "Production",
            Signing = [Pfx(Signing)],
            Encryption = [Pfx(Encryption)],
            EnableTestIssuance = true,
        };

        public static async Task<Lab> CreateAsync(string label)
        {
            var database = await PlatformTestDatabase.CreateAsync(label);
            var lab = new Lab { Database = database };
            lab.previousDatabase = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", database.DatabaseName);
            Assert.Equal(0, await Idata.Platform.DbMigrator.MigratorApp.RunAsync(["migrate"]));
            return lab;
        }

        public IdentityAuthorityFactory Host(IdentityAuthorityHostSettings? settings = null)
            => new(settings ?? Settings);

        public async ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previousDatabase);
            Signing.Dispose();
            Encryption.Dispose();
            await Database.DisposeAsync();
        }
    }

    private static async Task<HttpStatusCode> ProbeStatusAsync(HttpClient client, string? accessToken)
    {
        using var response = await IdentityAuthorityFactory.ProbeAsync(client, accessToken);
        return response.StatusCode;
    }

    [Fact]
    public async Task Token_real_hap_probe_e_mbrojtur_ndersa_pa_token_garbage_tampered_dhe_audience_gabim_401()
    {
        await using var lab = await Lab.CreateAsync("b2avalid");
        await using var factory = lab.Host();
        using var client = factory.CreateHttpsClient();
        var credentials = await factory.RegisterSyntheticClientAsync();
        var accessToken = await factory.IssueAccessTokenAsync(client, credentials);

        using (var response = await IdentityAuthorityFactory.ProbeAsync(client, accessToken))
        {
            Assert.True(HttpStatusCode.OK == response.StatusCode, $"status={response.StatusCode}{Environment.NewLine}{factory.DumpLogs()}");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(credentials.ClientId, body.RootElement.GetProperty("subject").GetString());
        }

        using (var response = await IdentityAuthorityFactory.ProbeAsync(client, null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(client, "garbage.opaque.value"));

        // Tampered: një karakter i ndryshuar në trupin e JWE-së.
        var middle = accessToken.Length / 2;
        var tampered = accessToken[..middle] + (accessToken[middle] == 'A' ? 'B' : 'A') + accessToken[(middle + 1)..];
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(client, tampered));

        // Audience i gabuar (accessToken real, i lëshuar për resource tjetër).
        var wrongAudience = await factory.IssueAccessTokenAsync(client, credentials, audience: "other-synthetic-api");
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(client, wrongAudience));

        // Token-i i mirë vazhdon të punojë (rastet negative nuk ndotën gjendjen).
        Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(client, accessToken));
    }

    [Fact]
    public async Task Issuer_i_gabuar_dhe_celes_i_panjohur_401_me_tokene_reale_nga_hoste_te_tjere()
    {
        await using var lab = await Lab.CreateAsync("b2aissuer");
        await using var factory = lab.Host();
        using var client = factory.CreateHttpsClient();
        var credentials = await factory.RegisterSyntheticClientAsync();
        Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(client, await factory.IssueAccessTokenAsync(client, credentials)));

        // Host B: TË NJËJTAT çelësa, issuer tjetër → accessToken real → 401 te host-i A.
        await using (var other = lab.Host(lab.Settings with { Issuer = "https://other.synthetic.local/" }))
        {
            using var otherClient = other.CreateHttpsClient();
            var foreignIssuer = await other.IssueAccessTokenAsync(otherClient, credentials);
            Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(otherClient, foreignIssuer)); // valid te B
            Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(client, foreignIssuer));
        }

        // Host C: i njëjti issuer + enkriptim, çelës FIRME i panjohur → 401.
        using var unknownSigning = SyntheticCertificates.Signing("CN=synthetic-unknown");
        await using (var other = lab.Host(lab.Settings with { Signing = [Pfx(unknownSigning)] }))
        {
            using var otherClient = other.CreateHttpsClient();
            var unknownKey = await other.IssueAccessTokenAsync(otherClient, credentials);
            Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(otherClient, unknownKey));
            Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(client, unknownKey));
        }
    }

    [Fact]
    public async Task Algoritmi_jashte_allowlist_401_dhe_allowlist_me_RS256_200()
    {
        await using var lab = await Lab.CreateAsync("b2aalg");
        await using var factory = lab.Host();
        using var client = factory.CreateHttpsClient();
        var credentials = await factory.RegisterSyntheticClientAsync();
        var accessToken = await factory.IssueAccessTokenAsync(client, credentials);

        await using (var strict = lab.Host(lab.Settings with { AllowedAlgorithms = ["PS256"] }))
        {
            using var strictClient = strict.CreateHttpsClient();
            Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(strictClient, accessToken));
        }

        await using (var permissive = lab.Host(lab.Settings with { AllowedAlgorithms = ["PS256", "RS256"] }))
        {
            using var permissiveClient = permissive.CreateHttpsClient();
            Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(permissiveClient, accessToken));
        }
    }

    [Fact]
    public async Task Crafting_kontroll_typ_iss_exp_nbf_dhe_clock_skew_i_kufizuar()
    {
        await using var lab = await Lab.CreateAsync("b2acraft");
        await using var factory = lab.Host();
        using var client = factory.CreateHttpsClient();
        var credentials = await factory.RegisterSyntheticClientAsync();
        var accessToken = await factory.IssueAccessTokenAsync(client, credentials);
        var claims = SyntheticTokenCrafter.DecryptClaims(lab.Signing, lab.Encryption, accessToken);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // KONTROLL: ri-nënshkruar/ri-enkriptuar PA ndryshim → 200 (crafting-u është besnik).
        Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(client,
            SyntheticTokenCrafter.Create(claims, lab.Signing, lab.Encryption)));

        // typ i gabuar (JWT në vend të at+jwt).
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(client,
            SyntheticTokenCrafter.Create(claims, lab.Signing, lab.Encryption, tokenType: "JWT")));

        // iss i ndryshuar me çelësa REALË (izolon issuer-in nga çelësi).
        var wrongIssuer = new Dictionary<string, object>(claims) { ["iss"] = "https://other.synthetic.local/" };
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(client,
            SyntheticTokenCrafter.Create(wrongIssuer, lab.Signing, lab.Encryption)));

        // Çelës firme i panjohur (enkriptim real).
        using var unknown = SyntheticCertificates.Signing("CN=synthetic-unknown");
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(client,
            SyntheticTokenCrafter.Create(claims, unknown, lab.Encryption)));

        // Algoritëm jashtë allowlist-it (PS256 me çelësin real) → 401.
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(client,
            SyntheticTokenCrafter.Create(claims, lab.Signing, lab.Encryption, algorithm: SecurityAlgorithms.RsaSsaPssSha256)));

        // JWT-level (validimi i lifetime-it të IdentityModel, i aktivizuar në wiring):
        // exp në të kaluarën (skew = 0) → 401; nbf në të ardhmen (skew = 0) → 401 —
        // edhe pse entry-i i store-it është VALID (kontroll shtesë, jo zëvendësim).
        var expired = new Dictionary<string, object>(claims) { ["exp"] = now - 30 };
        var notYetValid = new Dictionary<string, object>(claims) { ["nbf"] = now + 30 };
        var expiredStatus = await ProbeStatusAsync(client, SyntheticTokenCrafter.Create(expired, lab.Signing, lab.Encryption));
        Assert.True(HttpStatusCode.Unauthorized == expiredStatus,
            $"expired-30s status={expiredStatus} | {factory.DumpValidationOptions()}{Environment.NewLine}{factory.DumpLogs()}");
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(client,
            SyntheticTokenCrafter.Create(notYetValid, lab.Signing, lab.Encryption)));

        // Clock skew i KUFIZUAR (60s): brenda skew → 200; jashtë skew → 401.
        await using var skewed = lab.Host(lab.Settings with { ClockSkewSeconds = 60 });
        using var skewedClient = skewed.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(skewedClient,
            SyntheticTokenCrafter.Create(expired, lab.Signing, lab.Encryption)));
        Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(skewedClient,
            SyntheticTokenCrafter.Create(notYetValid, lab.Signing, lab.Encryption)));
        var farExpired = new Dictionary<string, object>(claims) { ["exp"] = now - 120 };
        var farFuture = new Dictionary<string, object>(claims) { ["nbf"] = now + 120 };
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(skewedClient,
            SyntheticTokenCrafter.Create(farExpired, lab.Signing, lab.Encryption)));
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(skewedClient,
            SyntheticTokenCrafter.Create(farFuture, lab.Signing, lab.Encryption)));
    }

    /// <summary>
    /// OpenIddict (burim zyrtar 7.6.0, `OpenIddictValidationHandlers.Protection`):
    /// `RestoreTokenEntryProperties` rikthen `exp` nga accessToken entry-i i store-it
    /// (AUTORITATIV mbi claim-in e JWT-së) dhe `ValidateExpirationDate` refuzon
    /// kur `exp + ClockSkew &lt; now`. Provohet me accessToken REAL (JWT i vlefshëm) dhe
    /// entry me skadim të kaluar: skew 0 → 401; skew 60 brenda → 200; jashtë → 401.
    /// </summary>
    [Fact]
    public async Task Skadimi_i_entry_it_te_store_it_eshte_autoritativ_dhe_respekton_skew_in_e_kufizuar()
    {
        await using var lab = await Lab.CreateAsync("b2aentryexp");
        await using var factory = lab.Host();
        using var client = factory.CreateHttpsClient();
        var credentials = await factory.RegisterSyntheticClientAsync();
        var accessToken = await factory.IssueAccessTokenAsync(client, credentials);
        Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(client, accessToken));

        async Task SetEntryExpirationAsync(TimeSpan offsetFromNow)
        {
            using var scope = factory.Services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            var entries = new List<object>();
            await foreach (var entry in manager.FindBySubjectAsync(credentials.ClientId, TestContext.Current.CancellationToken))
            {
                entries.Add(entry);
            }

            var single = Assert.Single(entries);
            var descriptor = new OpenIddictTokenDescriptor();
            await manager.PopulateAsync(descriptor, single, TestContext.Current.CancellationToken);
            descriptor.ExpirationDate = DateTimeOffset.UtcNow.Add(offsetFromNow);
            await manager.UpdateAsync(single, descriptor, TestContext.Current.CancellationToken);
        }

        await SetEntryExpirationAsync(TimeSpan.FromSeconds(-30));
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(client, accessToken));

        await using var skewed = lab.Host(lab.Settings with { ClockSkewSeconds = 60 });
        using var skewedClient = skewed.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(skewedClient, accessToken));

        await SetEntryExpirationAsync(TimeSpan.FromSeconds(-120));
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(skewedClient, accessToken));
    }

    /// <summary>
    /// Reliability Gate skenari 7 (DB unavailable): accessToken-entry validation kërkon
    /// store-in REAL — me DB të paarritshme middleware-i NUK kthen kurrë 200
    /// (fail-closed: 401 ose 500), edhe për një accessToken të lëshuar më parë si valid.
    /// </summary>
    [Fact]
    public async Task Db_e_paarritshme_per_token_entry_validation_nuk_kthen_kurre_200_fail_closed()
    {
        await using var lab = await Lab.CreateAsync("b2adbdown");
        string accessToken;
        await using (var live = lab.Host())
        {
            using var client = live.CreateHttpsClient();
            var credentials = await live.RegisterSyntheticClientAsync();
            accessToken = await live.IssueAccessTokenAsync(client, credentials);
            Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(client, accessToken));
        }

        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", "idata_platform_test_unreachable_synthetic");
        try
        {
            await using var down = lab.Host();
            using var downClient = down.CreateHttpsClient();
            // TestServer e ri-hedh exception-in e pipeline-it te klienti (Kestrel → 500);
            // në të dyja rastet ZERO principal dhe KURRË 200.
            HttpStatusCode? status = null;
            Exception? failure = null;
            try
            {
                status = await ProbeStatusAsync(downClient, accessToken);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.NotEqual(HttpStatusCode.OK, status);
            Assert.True(
                status is HttpStatusCode.Unauthorized or HttpStatusCode.InternalServerError || failure is Npgsql.PostgresException,
                $"status={status} failure={failure?.GetType().FullName}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    [Fact]
    public async Task Revokimi_ne_store_refuzon_tokenin_real_401()
    {
        await using var lab = await Lab.CreateAsync("b2arevoke");
        await using var factory = lab.Host();
        using var client = factory.CreateHttpsClient();
        var credentials = await factory.RegisterSyntheticClientAsync();
        var accessToken = await factory.IssueAccessTokenAsync(client, credentials);
        Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(client, accessToken));

        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            var revoked = 0;
            await foreach (var entry in manager.FindBySubjectAsync(credentials.ClientId, TestContext.Current.CancellationToken))
            {
                Assert.True(await manager.TryRevokeAsync(entry, TestContext.Current.CancellationToken));
                revoked++;
            }

            Assert.Equal(1, revoked);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(client, accessToken));
    }

    [Fact]
    public async Task Restart_me_credentials_te_njejta_pranon_tokenin_e_leshuar_para_restartit()
    {
        await using var lab = await Lab.CreateAsync("b2arestart");
        string accessToken;
        (string, string) credentials;
        await using (var first = lab.Host())
        {
            using var client = first.CreateHttpsClient();
            credentials = await first.RegisterSyntheticClientAsync();
            accessToken = await first.IssueAccessTokenAsync(client, credentials);
            Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(client, accessToken));
        }

        await using var second = lab.Host();
        using var secondClient = second.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(secondClient, accessToken));
        Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(secondClient, await second.IssueAccessTokenAsync(secondClient, credentials)));
    }

    [Fact]
    public async Task Rotation_me_overlap_pranon_te_vjetrin_gjate_overlap_dhe_e_refuzon_pas_heqjes()
    {
        await using var lab = await Lab.CreateAsync("b2arotate");
        // Çelësat e rinj me NotAfter më të vonë → OpenIddict i preferon për issuance.
        using var newSigning = SyntheticCertificates.Signing("CN=synthetic-signing-2", notAfter: DateTimeOffset.UtcNow.AddDays(60));
        using var newEncryption = SyntheticCertificates.Encryption("CN=synthetic-encryption-2", notAfter: DateTimeOffset.UtcNow.AddDays(60));

        string oldToken;
        (string, string) credentials;
        await using (var phase1 = lab.Host())
        {
            using var client = phase1.CreateHttpsClient();
            credentials = await phase1.RegisterSyntheticClientAsync();
            oldToken = await phase1.IssueAccessTokenAsync(client, credentials);
        }

        string newToken;
        await using (var phase2 = lab.Host(lab.Settings with
        {
            Signing = [Pfx(lab.Signing), Pfx(newSigning)],
            Encryption = [Pfx(lab.Encryption), Pfx(newEncryption)],
        }))
        {
            using var client = phase2.CreateHttpsClient();
            Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(client, oldToken));
            newToken = await phase2.IssueAccessTokenAsync(client, credentials);
            Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(client, newToken));
            // Token-i i ri është vërtet me çelësat e RINJ (dekriptohet vetëm me ta).
            Assert.ThrowsAny<Exception>(() => SyntheticTokenCrafter.DecryptClaims(lab.Signing, lab.Encryption, newToken));
            Assert.NotEmpty(SyntheticTokenCrafter.DecryptClaims(newSigning, newEncryption, newToken));
        }

        await using var phase3 = lab.Host(lab.Settings with { Signing = [Pfx(newSigning)], Encryption = [Pfx(newEncryption)] });
        using var phase3Client = phase3.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatusAsync(phase3Client, oldToken));
        Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(phase3Client, newToken));
    }

    [Fact]
    public async Task Endpoint_i_autorizimit_fail_closed_dhe_grant_et_e_ndaluara_refuzohen_pa_rrjedhje_sekreti()
    {
        await using var lab = await Lab.CreateAsync("b2agrants");
        await using var factory = lab.Host();
        using var client = factory.CreateHttpsClient();
        var credentials = await factory.RegisterSyntheticClientAsync();

        // Klient sintetik i PLOTË për code flow (redirect URI + permissions + PKCE) —
        // kërkesa arrin te handler-i fail-closed (jo te validimi i klientit).
        const string redirectUri = "https://client.synthetic.local/callback";
        var codeClientId = $"synthetic-code-{Guid.NewGuid():N}";
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = codeClientId,
                ClientType = ClientTypes.Public,
                RedirectUris = { new Uri(redirectUri) },
                Permissions =
                {
                    Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
                    Permissions.GrantTypes.AuthorizationCode, Permissions.ResponseTypes.Code,
                },
                Requirements = { Requirements.Features.ProofKeyForCodeExchange },
            }, TestContext.Current.CancellationToken);
        }

        var authorize = "/" + IdentityAuthorityServiceCollectionExtensions.AuthorizationEndpointPath
            + $"?client_id={codeClientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code"
            + "&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256";
        using (var response = await client.GetAsync(new Uri(authorize, UriKind.Relative), TestContext.Current.CancellationToken))
        {
            // Refuzim OAuth i kontrolluar: redirect me error, KURRË `code=`.
            Assert.True(HttpStatusCode.Redirect == response.StatusCode,
                $"status={response.StatusCode} body={await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}{Environment.NewLine}{factory.DumpLogs()}");
            var location = response.Headers.Location?.ToString() ?? string.Empty;
            Assert.StartsWith(redirectUri, location, StringComparison.Ordinal);
            Assert.Contains("error=unsupported_response_type", location, StringComparison.Ordinal);
            Assert.DoesNotContain("code=", location, StringComparison.Ordinal);
        }

        // Grant-et e ndaluara: password/implicit/refresh → unsupported_grant_type.
        foreach (var grant in (string[])[GrantTypes.Password, GrantTypes.RefreshToken, GrantTypes.Implicit])
        {
            using var response = await client.PostAsync(
                new Uri("/" + IdentityAuthorityServiceCollectionExtensions.TokenEndpointPath, UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = grant,
                    ["client_id"] = credentials.ClientId,
                    ["client_secret"] = credentials.ClientSecret,
                    ["username"] = "synthetic",
                    ["password"] = "synthetic",
                    ["refresh_token"] = "synthetic",
                }),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains(Errors.UnsupportedGrantType, body, StringComparison.Ordinal);
        }

        // Secret i gabuar → invalid_client; përgjigjja nuk e jehon secret-in.
        using (var response = await IdentityAuthorityFactory.RequestTokenAsync(client, (credentials.ClientId, "wrong-" + credentials.ClientSecret)))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains(Errors.InvalidClient, body, StringComparison.Ordinal);
            Assert.DoesNotContain(credentials.ClientSecret, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Development_ephemeral_leshon_dhe_validon_ndersa_OpenAPI_nuk_permban_endpointe_protokolli_as_konfigurim()
    {
        await using var lab = await Lab.CreateAsync("b2adev");
        await using var factory = lab.Host(new IdentityAuthorityHostSettings
        {
            Environment = "Development",
            DevelopmentSigning = IdentityDevelopmentCredentialsOptions.EphemeralKey,
            DevelopmentEncryption = IdentityDevelopmentCredentialsOptions.EphemeralKey,
            EnableTestIssuance = true,
        });
        using var client = factory.CreateHttpsClient();
        var credentials = await factory.RegisterSyntheticClientAsync();
        Assert.Equal(HttpStatusCode.OK, await ProbeStatusAsync(client, await factory.IssueAccessTokenAsync(client, credentials)));

        // Dokumenti runtime i këtij host-i përmban probe-in test-only (application part
        // i test assembly-t) — kontrata production (build-time, contracts/openapi) nuk e
        // përmban (CI drift-check). Këtu provohet: ZERO endpoint protokolli OpenIddict
        // dhe ZERO vlerë konfigurimi identity në dokument.
        var openApi = await client.GetStringAsync(new Uri("/openapi/platform-v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("connect/", openApi, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Identity:Authority", openApi, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-api", openApi, StringComparison.Ordinal);
        Assert.DoesNotContain("EphemeralKey", openApi, StringComparison.Ordinal);
    }
}
