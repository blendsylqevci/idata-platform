using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Idata.Platform.Modules.Identity.Application;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// F1 Increment B.2c — profili OIDC Authorization Code + PKCE (provider-side) mbi
/// login/consent + PKCE ekzistues të B.2b: `openid` → ID token i nënshkruar, `nonce`
/// i lidhur me flow-in, UserInfo minimal (VETËM `sub`). Konsumatori SINTETIK i testit
/// validon ID token-in KRIPTOGRAFIKISHT me JWKS-in publik të authority-t (bibliotekat
/// ekzistuese Microsoft.IdentityModel) — leximi i claims pa validim nuk mjafton.
/// Gjithçka kalon pipeline-in REAL (HTTP → middleware → OpenIddict → PostgreSQL sintetik);
/// zero auth override, zero mock principal. Nuk pretendohet full OIDC conformance.
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class IdentityOidcSignInTests
{
    private const string State = "synthetic-oidc-state";

    /// <summary>Claims të lejuara në ID token — protokoll + identifikuesit e brendshëm të OpenIddict; zero profile/email/stamp.</summary>
    private static readonly string[] AllowedIdTokenClaims =
        ["iss", "sub", "aud", "exp", "iat", "nbf", "auth_time", "nonce", "at_hash", "azp", "jti",
         // Identifikues INTERNAL të OpenIddict 7.6 (entry/authorization/token id, krijim/skadim,
         // prezantues, lloj) — të toleruar SHPREHIMISHT: të gjeneruar nga biblioteka, zero të dhëna
         // përdoruesi, zero security stamp. Çdo emër tjetër e skuq testin.
         "oi_au_id", "oi_tkn_id", "oi_prst", "oi_crt_dt", "oi_exp_dt", "oi_tkn_typ"];

    // ═══════════════ 1 · OAuth-only i pandryshuar ═══════════════

    [Fact]
    public async Task OAuth_only_pa_openid_ruan_sjelljen_ekzistuese_dhe_nuk_merr_ID_token_as_UserInfo()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2coauth");
        await using var factory = lab.Host(lab.Settings with { EnableProtectedProbe = true });
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);

        var (code, verifier) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State);
        string accessToken;
        using (var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
        {
            Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
            accessToken = (await InteractiveClient.AccessTokenAsync(redeemed))!;
            Assert.False(string.IsNullOrEmpty(accessToken));
            Assert.Null(await InteractiveClient.IdTokenAsync(redeemed)); // pa `openid` → zero ID token
        }

        using (var authorized = await InteractiveClient.ProtectedAsync(client, accessToken))
        {
            Assert.Equal(HttpStatusCode.OK, authorized.StatusCode); // API bearer i pandryshuar
        }

        // UserInfo kërkon scope-in `openid` — access token i vlefshëm pa të → 403 insufficient_scope.
        using var userInfo = await InteractiveClient.UserInfoAsync(client, accessToken);
        Assert.Equal(HttpStatusCode.Forbidden, userInfo.StatusCode);
        var body = await InteractiveClient.BodyAsync(userInfo);
        Assert.Contains(UserInfoContract.InsufficientScopeError, body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"sub\"", body, StringComparison.Ordinal);
        Assert.Contains(UserInfoContract.InsufficientScopeError, userInfo.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        Assert.Equal("no-store", userInfo.Headers.CacheControl?.ToString());
    }

    // ═══════════════ 2 · OIDC code flow me openid + nonce ═══════════════

    [Fact]
    public async Task OIDC_code_flow_me_openid_dhe_nonce_prodhon_access_dhe_ID_token_te_validuar_kriptografikisht()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2coidc");
        await using var factory = lab.Host(lab.Settings with { EnableProtectedProbe = true });
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);
        var nonce = $"synthetic-nonce-{Guid.NewGuid():N}";

        var (code, verifier) = await InteractiveClient.IssueCodeAsync(
            client, clientId, userName, State, scope: Scopes.OpenId, nonce: nonce);

        string accessToken;
        string idToken;
        using (var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
        {
            Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
            accessToken = (await InteractiveClient.AccessTokenAsync(redeemed))!;
            idToken = (await InteractiveClient.IdTokenAsync(redeemed))!;
            Assert.False(string.IsNullOrEmpty(accessToken));
            Assert.False(string.IsNullOrEmpty(idToken));
            using var document = JsonDocument.Parse(await InteractiveClient.BodyAsync(redeemed));
            Assert.Equal("Bearer", document.RootElement.GetProperty("token_type").GetString());
            Assert.False(document.RootElement.TryGetProperty("refresh_token", out _));
        }

        // Konsumatori sintetik: validim KRIPTOGRAFIK (JWKS publik, issuer, aud = client_id,
        // lifetime, algoritëm i shpallur, subjekt real, nonce ekzakt).
        var validated = await ValidateIdTokenAsync(client, idToken, clientId, userId.ToString(), nonce);
        AssertOnlyAllowedClaims(validated);

        // Access token-i real kalon te `[Authorize]`; ID token-i NUK e zëvendëson (cookie–bearer–ID i ndarë).
        using (var authorized = await InteractiveClient.ProtectedAsync(client, accessToken))
        {
            Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        }

        using (var idAsBearer = await InteractiveClient.ProtectedAsync(client, idToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, idAsBearer.StatusCode);
        }

        // UserInfo GET dhe POST: VETËM `sub`, i njëjti me ID token-in e këtij flow-i, pa cache.
        foreach (var method in (HttpMethod[])[HttpMethod.Get, HttpMethod.Post])
        {
            using var userInfo = await InteractiveClient.UserInfoAsync(client, accessToken, method);
            Assert.Equal(HttpStatusCode.OK, userInfo.StatusCode);
            Assert.Equal("application/json", userInfo.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-store", userInfo.Headers.CacheControl?.ToString());
            using var document = JsonDocument.Parse(await InteractiveClient.BodyAsync(userInfo));
            var members = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
            Assert.Equal([UserInfoContract.SubjectMember], members);
            Assert.Equal(validated.Subject, document.RootElement.GetProperty("sub").GetString());
            Assert.Equal(userId.ToString(), document.RootElement.GetProperty("sub").GetString());
        }

        // Asnjë material tokeni në log-un e host-it.
        var logs = factory.DumpLogs();
        Assert.DoesNotContain(accessToken, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(idToken, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nonce_i_munguar_lejohet_per_code_flow_dhe_ID_token_i_nuk_shpik_nonce()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2cnonce");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);

        var (code, verifier) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State, scope: Scopes.OpenId);
        using var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier);
        Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        var idToken = (await InteractiveClient.IdTokenAsync(redeemed))!;
        var validated = await ValidateIdTokenAsync(client, idToken, clientId, userId.ToString(), expectedNonce: null);
        AssertOnlyAllowedClaims(validated);
    }

    // ═══════════════ 3 · Dy flow të pavarura — lidhjet e veta ═══════════════

    [Fact]
    public async Task Dy_flow_te_pavarura_ruajne_sub_client_state_nonce_e_veta_dhe_konsumatori_refuzon_mosperputhjet()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2ctwo");
        await using var factory = lab.Host();
        var flows = new List<(string ClientId, Guid UserId, string State, string Nonce, string AccessToken, string IdToken)>();

        for (var index = 0; index < 2; index++)
        {
            using var client = InteractiveClient.CreateCookieClient(factory);
            var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
            var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);
            var state = $"synthetic-state-{index}-{Guid.NewGuid():N}";
            var nonce = $"synthetic-nonce-{index}-{Guid.NewGuid():N}";
            var (code, verifier) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, state, Scopes.OpenId, nonce);
            using var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier);
            Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
            flows.Add((clientId, userId, state, nonce,
                (await InteractiveClient.AccessTokenAsync(redeemed))!, (await InteractiveClient.IdTokenAsync(redeemed))!));
        }

        var (a, b) = (flows[0], flows[1]);
        Assert.NotEqual(a.UserId, b.UserId);
        Assert.NotEqual(a.ClientId, b.ClientId);
        Assert.NotEqual(a.IdToken, b.IdToken);

        using var consumer = InteractiveClient.CreateCookieClient(factory);
        // Secili ID token validohet VETËM me lidhjet e veta (aud/sub/nonce).
        var validatedA = await ValidateIdTokenAsync(consumer, a.IdToken, a.ClientId, a.UserId.ToString(), a.Nonce);
        var validatedB = await ValidateIdTokenAsync(consumer, b.IdToken, b.ClientId, b.UserId.ToString(), b.Nonce);
        Assert.NotEqual(validatedA.GetClaim("nonce").Value, validatedB.GetClaim("nonce").Value);

        // Kryqëzimi refuzohet nga VALIDATORI i konsumatorit: audience e klientit tjetër → i pavlefshëm.
        var cross = await RawValidateAsync(consumer, a.IdToken, audience: b.ClientId);
        Assert.False(cross.IsValid);
        Assert.IsAssignableFrom<SecurityTokenInvalidAudienceException>(cross.Exception);

        // Nonce-i i flow-it tjetër nuk përputhet (kontrolli i konsumatorit, jo pohim tekstual).
        Assert.NotEqual(b.Nonce, validatedA.GetClaim("nonce").Value);

        // UserInfo me token-in A kthen VETËM subjektin A; me token-in B subjektin B.
        foreach (var flow in flows)
        {
            using var userInfo = await InteractiveClient.UserInfoAsync(consumer, flow.AccessToken);
            Assert.Equal(HttpStatusCode.OK, userInfo.StatusCode);
            using var document = JsonDocument.Parse(await InteractiveClient.BodyAsync(userInfo));
            Assert.Equal(flow.UserId.ToString(), document.RootElement.GetProperty("sub").GetString());
        }
    }

    // ═══════════════ 4 · UserInfo — refuzimet fail-closed ═══════════════

    [Fact]
    public async Task UserInfo_refuzon_anonim_cookie_ID_token_token_te_manipuluar_dhe_te_revokuar_pa_500()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2cneg");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (code, verifier) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce");
        string accessToken;
        string idToken;
        using (var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
        {
            Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
            accessToken = (await InteractiveClient.AccessTokenAsync(redeemed))!;
            idToken = (await InteractiveClient.IdTokenAsync(redeemed))!;
        }

        // Parakusht: token-i real jep 200 (refuzimet më poshtë nuk vijnë nga artefakt i konsumuar).
        using (var precondition = await InteractiveClient.UserInfoAsync(client, accessToken))
        {
            Assert.Equal(HttpStatusCode.OK, precondition.StatusCode);
        }

        // (a) anonim — pa bearer; (b) vetëm cookie-ja e sesionit interaktiv (klienti ka cookie aktive);
        // (c) ID token-i si bearer; (d) access token i manipuluar; (e) access token pas revokimit.
        foreach (var (label, token) in (IEnumerable<(string, string?)>)[
            ("anonim", null),
            ("id-token-si-bearer", idToken),
            ("i-manipuluar", accessToken[..^6] + "AAAAAA"),
            ("i-shkurtuar", accessToken[..(accessToken.Length / 2)]),
            ("jo-jwt", "synthetic-opaque-bearer")])
        {
            using var response = await InteractiveClient.UserInfoAsync(client, token);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"rasti `{label}`: pritej 401, u mor {(int)response.StatusCode}");
            Assert.NotEmpty(response.Headers.WwwAuthenticate);
            var body = await InteractiveClient.BodyAsync(response);
            Assert.DoesNotContain("\"sub\"", body, StringComparison.Ordinal);
            Assert.DoesNotContain(userId.ToString(), body, StringComparison.Ordinal);
            Assert.DoesNotContain(accessToken, body, StringComparison.Ordinal);
        }

        // (b) cookie-only: i njëjti klient mban cookie-n AKTIVE të sesionit interaktiv (prova:
        // faqja e consent-it hapet pa login të ri) — por pa bearer UserInfo mbetet 401 (kufiri cookie–bearer).
        var (consentPage, _) = await InteractiveClient.ConsentPageAsync(
            client, InteractiveClient.AuthorizationUrl(clientId, InteractiveClient.CreatePkcePair().Challenge, State, Scopes.OpenId));
        Assert.Equal(HttpStatusCode.OK, consentPage.StatusCode);
        consentPage.Dispose();
        using (var cookieOnly = await InteractiveClient.UserInfoAsync(client, accessToken: null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, cookieOnly.StatusCode);
        }

        // (e) revokim real i access token-it → 401 (token entry i revokuar; storage aktiv).
        var (revokedCount, observedTypes) = await SyntheticPrincipals.RevokeAccessTokensAsync(factory, userId.ToString());
        Assert.True(revokedCount >= 1, $"asnjë access token i revokuar; llojet e vëzhguara: [{observedTypes}]");
        using (var revoked = await InteractiveClient.UserInfoAsync(client, accessToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        }
    }

    [Fact]
    public async Task UserInfo_nuk_zgjedh_subjekt_tjeter_me_parametra_query_ose_body()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2csub");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (_, otherId) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (code, verifier) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce");
        using var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier);
        Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        var accessToken = (await InteractiveClient.AccessTokenAsync(redeemed))!;
        Assert.False(string.IsNullOrEmpty(accessToken));

        // PARAKUSHT: i njëjti token jep 200 me subjektin e vet — refuzimet/injorimet më poshtë
        // nuk mund të vijnë nga një token i pavlefshëm.
        using (var precondition = await InteractiveClient.UserInfoAsync(client, accessToken))
        {
            Assert.Equal(HttpStatusCode.OK, precondition.StatusCode);
            using var document = JsonDocument.Parse(await InteractiveClient.BodyAsync(precondition));
            Assert.Equal(userId.ToString(), document.RootElement.GetProperty("sub").GetString());
        }

        // Parametrat INJOROHEN plotësisht: kërkesa mbetet 200 me subjektin e bearer-it.
        var ignored = new (HttpMethod Method, string? Query, IReadOnlyDictionary<string, string>? Form, string Label)[]
        {
            (HttpMethod.Get, $"sub={otherId}&subject={otherId}&user_id={otherId}", null, "GET query"),
            (HttpMethod.Post, $"sub={otherId}", new Dictionary<string, string> { ["subject"] = otherId.ToString() }, "POST query+form"),
        };
        foreach (var (method, query, form, label) in ignored)
        {
            using var response = await InteractiveClient.UserInfoAsync(client, accessToken, method, query, form);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"rasti `{label}`: pritej 200, u mor {(int)response.StatusCode}");
            var body = await InteractiveClient.BodyAsync(response);
            using var document = JsonDocument.Parse(body);
            Assert.Equal(userId.ToString(), document.RootElement.GetProperty("sub").GetString());
            Assert.DoesNotContain(otherId.ToString(), body, StringComparison.Ordinal);
        }

        // `access_token` i rremë NË TRUP përkrah bearer-it të vlefshëm në header: sjellja REALE
        // e vëzhguar është që header-i i validuar mbetet autoritet dhe trupi injorohet — kurrë
        // subjekt tjetër, kurrë 500. (Refuzimi RFC 6750 §2 për dy metoda transmetimi NUK
        // pretendohet si i implementuar — vëzhgim i regjistruar në Record §9.)
        using (var multiple = await InteractiveClient.UserInfoAsync(
            client, accessToken, HttpMethod.Post, null,
            new Dictionary<string, string> { ["sub"] = otherId.ToString(), ["access_token"] = "synthetic" }))
        {
            Assert.NotEqual(HttpStatusCode.InternalServerError, multiple.StatusCode);
            var body = await InteractiveClient.BodyAsync(multiple);
            Assert.DoesNotContain(otherId.ToString(), body, StringComparison.Ordinal);
            if (multiple.StatusCode == HttpStatusCode.OK)
            {
                using var document = JsonDocument.Parse(body);
                Assert.Equal(userId.ToString(), document.RootElement.GetProperty("sub").GetString());
            }
        }
    }

    [Fact]
    public async Task Access_token_i_skaduar_refuzohet_te_UserInfo_pa_subjekt()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2cexp");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (code, verifier) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce");
        using var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier);
        Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        var accessToken = (await InteractiveClient.AccessTokenAsync(redeemed))!;

        // PARAKUSHT: token i vlefshëm → 200.
        using (var before = await InteractiveClient.UserInfoAsync(client, accessToken))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        // Skadim REAL i token entry-t (vetëm DB sintetike) — lifetime-i imponohet edhe në rrugën e serverit.
        Assert.True(await SyntheticPrincipals.ExpireAccessTokensAsync(factory, userId.ToString()) >= 1);
        using var expired = await InteractiveClient.UserInfoAsync(client, accessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, expired.StatusCode);
        var expiredBody = await InteractiveClient.BodyAsync(expired);
        Assert.DoesNotContain("\"sub\"", expiredBody, StringComparison.Ordinal);
        Assert.DoesNotContain(userId.ToString(), expiredBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task UserInfo_metodat_jashte_GET_POST_refuzohen_pa_200_dhe_pa_500(string method)
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2cmeth");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);

        using var response = await InteractiveClient.UserInfoAsync(client, "synthetic-bearer", new HttpMethod(method));
        // OIDC Core §5.3.1: vetëm GET/POST — metoda tjetër refuzohet nga protokolli me 400
        // dhe `error` në `WWW-Authenticate` (RFC 6750 §3); kurrë 200, kurrë 404, kurrë 500.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("error", response.Headers.WwwAuthenticate.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"sub\"", await InteractiveClient.BodyAsync(response), StringComparison.Ordinal);
    }

    // ═══════════════ 5 · JOSE te UserInfo — invalid_token, kurrë 500 ═══════════════

    [Fact]
    public async Task Access_token_me_JOSE_jashte_allowlist_it_refuzohet_te_UserInfo_si_invalid_token_kurre_500()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2cjose");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (code, verifier) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce");
        using var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier);
        var accessToken = (await InteractiveClient.AccessTokenAsync(redeemed))!;
        Assert.Equal(5, accessToken.Split('.').Length); // JWE real

        var inner = SyntheticTokenCrafter.ExtractInnerJws(lab.Signing, lab.Encryption, accessToken);
        var header = SyntheticTokenCrafter.OuterHeader(accessToken);

        // Kontroll: i njëjti inner JWS i ri-enkriptuar me algoritmet E LEJUARA → 200.
        using (var accepted = await InteractiveClient.UserInfoAsync(
            client, SyntheticTokenCrafter.ReEncrypt(inner, lab.Encryption, "RSA-OAEP", "A256CBC-HS512", header)))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        foreach (var tampered in (string[])
        [
            SyntheticTokenCrafter.ReEncrypt(inner, lab.Encryption, "RSA1_5", "A256CBC-HS512", header),
            SyntheticTokenCrafter.ReEncrypt(inner, lab.Encryption, "RSA-OAEP", "A128CBC-HS256", header),
            SyntheticTokenCrafter.WithOuterHeader(accessToken, "{\"alg\":\"RSA-OAEP\"}"),
            SyntheticTokenCrafter.WithOuterHeader(accessToken, "synthetic-not-json"),
        ])
        {
            using var rejected = await InteractiveClient.UserInfoAsync(client, tampered);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.NotEqual(HttpStatusCode.InternalServerError, rejected.StatusCode);
            var challenge = rejected.Headers.WwwAuthenticate.ToString();
            Assert.Contains(Errors.InvalidToken, challenge, StringComparison.Ordinal);
            Assert.DoesNotContain(Errors.InvalidGrant, challenge, StringComparison.Ordinal);
        }
    }

    // ═══════════════ 6 · Discovery / JWKS — vetëm aftësi reale ═══════════════

    [Fact]
    public async Task Discovery_dhe_JWKS_shpallin_vetem_aftesite_reale_OIDC_dhe_perputhen_me_ID_token_in()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2cdisc");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (code, verifier) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce");
        using var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier);
        var idToken = new JsonWebToken((await InteractiveClient.IdTokenAsync(redeemed))!);

        using var discovery = JsonDocument.Parse(await client.GetStringAsync(
            new Uri("/.well-known/openid-configuration", UriKind.Relative), TestContext.Current.CancellationToken));
        var root = discovery.RootElement;
        Assert.EndsWith("/" + UserInfoContract.Path, root.GetProperty("userinfo_endpoint").GetString(), StringComparison.Ordinal);
        Assert.Contains(Scopes.OpenId, root.GetProperty("scopes_supported").EnumerateArray().Select(item => item.GetString()!));
        var algorithms = root.GetProperty("id_token_signing_alg_values_supported").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.NotEmpty(algorithms);
        Assert.Contains(idToken.Alg, algorithms); // e shpallura = e përdorura realisht
        Assert.Equal([GrantTypes.AuthorizationCode], root.GetProperty("grant_types_supported").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Equal([ResponseTypes.Code], root.GetProperty("response_types_supported").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Equal([CodeChallengeMethods.Sha256], root.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Contains("public", root.GetProperty("subject_types_supported").EnumerateArray().Select(item => item.GetString()!));
        foreach (var absent in (string[])["end_session_endpoint", "introspection_endpoint", "revocation_endpoint",
            "device_authorization_endpoint", "pushed_authorization_request_endpoint"])
        {
            Assert.False(root.TryGetProperty(absent, out _), $"discovery premton `{absent}`");
        }

        if (root.TryGetProperty("claims_supported", out var claims))
        {
            // Zero claims të paimplementuara (profile/email/phone/address/rol).
            var declared = claims.EnumerateArray().Select(item => item.GetString()!).ToArray();
            Assert.All(declared, claim => Assert.Contains(claim, AllowedIdTokenClaims));
        }

        Assert.DoesNotContain(GrantTypes.RefreshToken, root.GetProperty("grant_types_supported").EnumerateArray().Select(item => item.GetString()!));
        Assert.DoesNotContain(Scopes.OfflineAccess, root.GetProperty("scopes_supported").EnumerateArray().Select(item => item.GetString()!));

        using var jwks = JsonDocument.Parse(await client.GetStringAsync(new Uri("/.well-known/jwks", UriKind.Relative), TestContext.Current.CancellationToken));
        var keys = jwks.RootElement.GetProperty("keys").EnumerateArray().ToArray();
        Assert.Contains(keys, key => key.GetProperty("kid").GetString() == idToken.Kid); // çelësi publik REAL i firmës
        foreach (var key in keys)
        {
            foreach (var privateParameter in (string[])["d", "p", "q", "dp", "dq", "qi", "k"])
            {
                Assert.False(key.TryGetProperty(privateParameter, out _), $"JWKS ekspozon parametër privat: {privateParameter}");
            }
        }

        // ID token-i validohet me JWKS-in e shpallur (jo me çelësin e brendshëm të testit).
        await ValidateIdTokenAsync(client, idToken.EncodedToken, clientId, userId.ToString(), "synthetic-nonce");
    }

    // ═══════════════ 7 · Reliability — DB e paarritshme, restart real, rikthim ═══════════════

    [Fact]
    public async Task DB_e_paarritshme_gjate_UserInfo_kthen_503_JSON_no_store_dhe_rikthimi_i_se_njejtes_DB_lejon_200()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2cdb");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (code, verifier) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce");
        using var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier);
        var accessToken = (await InteractiveClient.AccessTokenAsync(redeemed))!;

        using (var before = await InteractiveClient.UserInfoAsync(client, accessToken))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode); // parakusht
        }

        await SyntheticOutage.SuspendAsync(lab.Database.DatabaseName);
        try
        {
            using var during = await InteractiveClient.UserInfoAsync(client, accessToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, during.StatusCode);
            Assert.Equal("application/json", during.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-store", during.Headers.CacheControl?.ToString());
            using var document = JsonDocument.Parse(await InteractiveClient.BodyAsync(during));
            Assert.Equal(Errors.TemporarilyUnavailable, document.RootElement.GetProperty("error").GetString());
            Assert.False(document.RootElement.TryGetProperty("sub", out _));
            var body = document.RootElement.GetRawText();
            Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(userId.ToString(), body, StringComparison.Ordinal);
        }
        finally
        {
            await SyntheticOutage.ResumeAsync(lab.Database.DatabaseName);
        }

        using var after = await InteractiveClient.UserInfoAsync(client, accessToken);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using var recovered = JsonDocument.Parse(await InteractiveClient.BodyAsync(after));
        Assert.Equal(userId.ToString(), recovered.RootElement.GetProperty("sub").GetString());
    }

    [Fact]
    public async Task Restart_real_i_host_it_ruan_vlefshmerine_e_access_dhe_ID_token_it_me_te_njejtat_credentials()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2crestart");
        string clientId;
        Guid userId;
        string accessToken;
        string idToken;
        await using (var hostA = lab.Host())
        {
            using var client = InteractiveClient.CreateCookieClient(hostA);
            clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(hostA, withOpenId: true);
            var (userName, id) = await SyntheticPrincipals.CreateUserAsync(hostA);
            userId = id;
            var (code, verifier) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce");
            using var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier);
            Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
            accessToken = (await InteractiveClient.AccessTokenAsync(redeemed))!;
            idToken = (await InteractiveClient.IdTokenAsync(redeemed))!;
        }

        // Host i RI (proces i ri i aplikacionit) mbi të njëjtën DB dhe credentials.
        await using var hostB = lab.Host();
        using var consumer = InteractiveClient.CreateCookieClient(hostB);
        using (var userInfo = await InteractiveClient.UserInfoAsync(consumer, accessToken))
        {
            Assert.Equal(HttpStatusCode.OK, userInfo.StatusCode);
            using var document = JsonDocument.Parse(await InteractiveClient.BodyAsync(userInfo));
            Assert.Equal(userId.ToString(), document.RootElement.GetProperty("sub").GetString());
        }

        await ValidateIdTokenAsync(consumer, idToken, clientId, userId.ToString(), "synthetic-nonce");
    }

    // ═══════════════ Konsumatori sintetik (validim REAL, bibliotekat ekzistuese) ═══════════════

    private static async Task<JsonWebToken> ValidateIdTokenAsync(
        HttpClient client, string idToken, string clientId, string expectedSubject, string? expectedNonce)
    {
        var result = await RawValidateAsync(client, idToken, clientId);
        Assert.True(result.IsValid, result.Exception?.ToString());
        var validated = (JsonWebToken)result.SecurityToken;
        Assert.Equal(expectedSubject, validated.Subject);
        Assert.Equal([clientId], validated.Audiences.ToArray());
        Assert.True(validated.ValidTo > DateTime.UtcNow, "ID token i skaduar");
        Assert.True(validated.ValidTo - validated.IssuedAt <= TimeSpan.FromHours(1), "lifetime i ID token-it jashtë kufirit të pritur");
        if (expectedNonce is null)
        {
            Assert.False(validated.TryGetClaim("nonce", out _), "ID token-i shpik `nonce` pa kërkesë");
        }
        else
        {
            Assert.Equal(expectedNonce, validated.GetClaim("nonce").Value);
        }

        return validated;
    }

    private static async Task<TokenValidationResult> RawValidateAsync(HttpClient client, string idToken, string audience)
    {
        using var discovery = JsonDocument.Parse(await client.GetStringAsync(
            new Uri("/.well-known/openid-configuration", UriKind.Relative), TestContext.Current.CancellationToken));
        var issuer = discovery.RootElement.GetProperty("issuer").GetString()!;
        var algorithms = discovery.RootElement.GetProperty("id_token_signing_alg_values_supported")
            .EnumerateArray().Select(item => item.GetString()!).ToArray();
        var jwks = await client.GetStringAsync(new Uri("/.well-known/jwks", UriKind.Relative), TestContext.Current.CancellationToken);
        var keys = new JsonWebKeySet(jwks).GetSigningKeys();

        var handler = new JsonWebTokenHandler();
        return await handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKeys = keys,
            ValidAlgorithms = algorithms,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
        });
    }

    private static void AssertOnlyAllowedClaims(JsonWebToken token)
    {
        var names = token.Claims.Select(claim => claim.Type).Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.All(names, name => Assert.Contains(name, AllowedIdTokenClaims));
        Assert.Contains("sub", names);
        Assert.Contains("iss", names);
        Assert.Contains("aud", names);
        Assert.Contains("exp", names);
        Assert.Contains("iat", names);
        foreach (var forbidden in (string[])["name", "email", "preferred_username", "role", "roles", "permissions",
            "business_id", "tenant", InteractiveAuthorizationContract.SessionStampClaimType, "AspNet.Identity.SecurityStamp"])
        {
            Assert.DoesNotContain(forbidden, names);
        }
    }
}
