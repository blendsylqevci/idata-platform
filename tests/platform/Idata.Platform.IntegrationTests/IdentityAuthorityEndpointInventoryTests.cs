using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// C2 (C1 correction) — inventari REAL i endpoint-eve të protokollit. Dy kontrata
/// TË NDARA: (a) route inventory i MVC/minimal API (`EndpointDataSource`, shih
/// `ApiSurfaceTests`) dhe (b) konfigurimi i endpoint-eve OpenIddict (middleware,
/// JO `RouteEndpoint`). Këtu provohet (b) ekzaktësisht mbi host-in PRODUCTION
/// (pa asnjë shtesë test-only): vetëm `connect/authorize` + `connect/token` +
/// `connect/userinfo` (B.2c) + metadata publike default e OpenIddict (discovery +
/// JWKS); vetëm Authorization Code + PKCE; zero introspection/revocation/logout/
/// device/PAR; matrica HTTP method/path: endpoint-et e synuara refuzojnë
/// fail-closed (kurrë 404, kurrë 200 për kërkesa invalide), të pasynuarat = 404.
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class IdentityAuthorityEndpointInventoryTests
{
    private const string PfxPassphrase = "synthetic-inventory-pfx-passphrase";

    private static (string, string) Pfx(X509Certificate2 certificate)
        => (SyntheticCertificates.ToPfxBase64(certificate, PfxPassphrase), PfxPassphrase);

    private static IdentityAuthorityHostSettings ProductionShape(X509Certificate2 signing, X509Certificate2 encryption)
        => new() { Environment = "Production", Signing = [Pfx(signing)], Encryption = [Pfx(encryption)] };

    private static string[] Paths(IEnumerable<Uri> uris) => uris.Select(uri => uri.OriginalString).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task Konfigurimi_OpenIddict_ka_vetem_authorize_token_dhe_metadata_publike_default_dhe_vetem_code_PKCE()
    {
        using var signing = SyntheticCertificates.Signing();
        using var encryption = SyntheticCertificates.Encryption();
        await using var factory = new IdentityAuthorityFactory(ProductionShape(signing, encryption));
        using var client = factory.CreateHttpsClient(); // ndez host-in real

        var server = factory.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().Get(Options.DefaultName);
        Assert.Equal(["connect/authorize"], Paths(server.AuthorizationEndpointUris));
        Assert.Equal(["connect/token"], Paths(server.TokenEndpointUris));
        // B.2c: endpoint-i i VETËM i ri — UserInfo (rruga nga kontrata neutrale e Application-it).
        Assert.Equal([Idata.Platform.Modules.Identity.Application.UserInfoContract.Path], Paths(server.UserInfoEndpointUris));

        // Metadata publike default e OpenIddict (dokumentuar, jo e shtuar nga ne).
        Assert.Equal([".well-known/oauth-authorization-server", ".well-known/openid-configuration"], Paths(server.ConfigurationEndpointUris));
        Assert.Equal([".well-known/jwks"], Paths(server.JsonWebKeySetEndpointUris));

        // ZERO endpoint tjetër.
        Assert.Empty(server.IntrospectionEndpointUris);
        Assert.Empty(server.RevocationEndpointUris);
        Assert.Empty(server.EndSessionEndpointUris);
        Assert.Empty(server.DeviceAuthorizationEndpointUris);
        Assert.Empty(server.EndUserVerificationEndpointUris);
        Assert.Empty(server.PushedAuthorizationEndpointUris);

        // Vetëm Authorization Code + PKCE (S256) — zero grant tjetër në host-in production.
        Assert.Equal([GrantTypes.AuthorizationCode], server.GrantTypes.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal([ResponseTypes.Code], server.ResponseTypes.Order(StringComparer.Ordinal).ToArray());
        Assert.True(server.RequireProofKeyForCodeExchange);
        // B.2b: `S256` është metoda E VETME — `plain` u hoq nga seti i serverit.
        Assert.Equal([CodeChallengeMethods.Sha256], server.CodeChallengeMethods.Order(StringComparer.Ordinal).ToArray());
        Assert.False(server.DisableTokenStorage);
        Assert.False(server.DisableAccessTokenEncryption);
        Assert.False(server.IgnoreEndpointPermissions);
        Assert.False(server.IgnoreGrantTypePermissions);
        Assert.False(server.AcceptAnonymousClients);
        Assert.False(server.EnableDegradedMode);

        var aspnet = factory.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerAspNetCoreOptions>>().Get(Options.DefaultName);
        // B.2b: passthrough VETËM për autorizimin (login/consent i owner-it të aprovuar);
        // B.2c: + UserInfo (thin controller pas validimit të bearer-it nga OpenIddict);
        // token endpoint-i mbetet tërësisht i OpenIddict-it (passthrough i NDALUAR kudo).
        Assert.True(aspnet.EnableAuthorizationEndpointPassthrough);
        Assert.True(aspnet.EnableUserInfoEndpointPassthrough);
        Assert.False(aspnet.EnableTokenEndpointPassthrough);
        Assert.False(aspnet.EnableEndSessionEndpointPassthrough);
        Assert.False(aspnet.EnableEndUserVerificationEndpointPassthrough);
        Assert.False(aspnet.DisableTransportSecurityRequirement);
    }

    [Theory]
    [InlineData("GET", "/connect/token")]
    [InlineData("POST", "/connect/token")]
    [InlineData("PUT", "/connect/token")]
    [InlineData("DELETE", "/connect/token")]
    [InlineData("GET", "/connect/authorize")]
    [InlineData("POST", "/connect/authorize")]
    [InlineData("PUT", "/connect/authorize")]
    [InlineData("DELETE", "/connect/authorize")]
    [InlineData("GET", "/connect/userinfo")]
    [InlineData("POST", "/connect/userinfo")]
    [InlineData("PUT", "/connect/userinfo")]
    [InlineData("DELETE", "/connect/userinfo")]
    public async Task Endpointet_e_synuara_ekzistojne_dhe_refuzojne_fail_closed_kerkesat_invalide(string method, string path)
    {
        using var signing = SyntheticCertificates.Signing();
        using var encryption = SyntheticCertificates.Encryption();
        await using var factory = new IdentityAuthorityFactory(ProductionShape(signing, encryption));
        using var client = factory.CreateHttpsClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (method is "POST" or "PUT")
        {
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["synthetic"] = "1" });
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        // B.2c: VETËM UserInfo lejon 401 (RFC 6750, bearer i munguar) dhe mbart `error` në
        // `WWW-Authenticate`; token/authorize mbeten EKZAKTËSISHT me kontratën e B.2b.
        var isUserInfo = path.Equals("/connect/userinfo", StringComparison.Ordinal);
        Assert.Contains(response.StatusCode, isUserInfo
            ? (HttpStatusCode[])[HttpStatusCode.BadRequest, HttpStatusCode.MethodNotAllowed, HttpStatusCode.Unauthorized]
            : (HttpStatusCode[])[HttpStatusCode.BadRequest, HttpStatusCode.MethodNotAllowed, HttpStatusCode.Redirect]);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            if (isUserInfo)
            {
                // Gabimi bearer mbartet në `WWW-Authenticate` (RFC 6750 §3), trupi mbetet bosh.
                Assert.Contains("error", response.Headers.WwwAuthenticate.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                // Token endpoint: JSON OAuth error; authorization endpoint pa redirect_uri të
                // validuar: përgjigje lokale gabimi (jo JSON) — të dyja mbartin `error` në TRUP.
                Assert.Contains("error", body, StringComparison.OrdinalIgnoreCase);
            }

            Assert.DoesNotContain("code=", body, StringComparison.Ordinal);
        }
        else if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // RFC 6750 §3: kërkesa PA asnjë kredencial merr challenge-in bazë `Bearer` PA kod
            // gabimi (kodi `invalid_token` shfaqet vetëm kur dërgohet token i pavlefshëm —
            // provuar te `IdentityOidcSignInTests`). Kurrë trup me subjekt.
            Assert.True(isUserInfo, "vetëm UserInfo lejon 401 në këtë matricë");
            Assert.NotEmpty(response.Headers.WwwAuthenticate);
            Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("\"sub\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("GET", "/connect/introspect")]
    [InlineData("POST", "/connect/introspect")]
    [InlineData("POST", "/connect/revoke")]
    [InlineData("GET", "/connect/logout")]
    [InlineData("GET", "/connect/endsession")]
    [InlineData("POST", "/connect/device")]
    [InlineData("POST", "/connect/par")]
    [InlineData("GET", "/connect/verify")]
    public async Task Endpointet_e_pasynuara_jane_404(string method, string path)
    {
        using var signing = SyntheticCertificates.Signing();
        using var encryption = SyntheticCertificates.Encryption();
        await using var factory = new IdentityAuthorityFactory(ProductionShape(signing, encryption));
        using var client = factory.CreateHttpsClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (method == "POST")
        {
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = "synthetic" });
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Discovery_dhe_JWKS_publike_reflektojne_inventarin_dhe_nuk_ekspozojne_material_privat()
    {
        using var signing = SyntheticCertificates.Signing();
        using var encryption = SyntheticCertificates.Encryption();
        await using var factory = new IdentityAuthorityFactory(ProductionShape(signing, encryption));
        using var client = factory.CreateHttpsClient();

        var discovery = await client.GetStringAsync(new Uri("/.well-known/openid-configuration", UriKind.Relative), TestContext.Current.CancellationToken);
        using (var document = JsonDocument.Parse(discovery))
        {
            var root = document.RootElement;
            Assert.EndsWith("/connect/token", root.GetProperty("token_endpoint").GetString(), StringComparison.Ordinal);
            Assert.EndsWith("/connect/authorize", root.GetProperty("authorization_endpoint").GetString(), StringComparison.Ordinal);
            Assert.Equal([GrantTypes.AuthorizationCode], root.GetProperty("grant_types_supported").EnumerateArray().Select(e => e.GetString()!).ToArray());
            Assert.Equal([ResponseTypes.Code], root.GetProperty("response_types_supported").EnumerateArray().Select(e => e.GetString()!).ToArray());
            Assert.Contains(CodeChallengeMethods.Sha256, root.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(e => e.GetString()!));
            // B.2c: `userinfo_endpoint` tani është REAL (i shpallur dhe i shërbyer); `openid` +
            // algoritmet e ID token-it shpallen — provohen te `IdentityOidcSignInTests`.
            Assert.EndsWith("/connect/userinfo", root.GetProperty("userinfo_endpoint").GetString(), StringComparison.Ordinal);
            Assert.Contains(Scopes.OpenId, root.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString()!));
            Assert.NotEmpty(root.GetProperty("id_token_signing_alg_values_supported").EnumerateArray());
            foreach (var absent in (string[])["introspection_endpoint", "revocation_endpoint", "end_session_endpoint", "device_authorization_endpoint", "pushed_authorization_request_endpoint"])
            {
                Assert.False(root.TryGetProperty(absent, out _), $"discovery ekspozon endpoint të pasynuar: {absent}");
            }
        }

        var jwks = await client.GetStringAsync(new Uri("/.well-known/jwks", UriKind.Relative), TestContext.Current.CancellationToken);
        using (var document = JsonDocument.Parse(jwks))
        {
            var keys = document.RootElement.GetProperty("keys").EnumerateArray().ToArray();
            Assert.NotEmpty(keys);
            foreach (var key in keys)
            {
                Assert.Equal("RSA", key.GetProperty("kty").GetString());
                foreach (var privateParameter in (string[])["d", "p", "q", "dp", "dq", "qi", "k"])
                {
                    Assert.False(key.TryGetProperty(privateParameter, out _), $"JWKS ekspozon parametër privat: {privateParameter}");
                }
            }
        }
    }
}
