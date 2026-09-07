using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Idata.Platform.Modules.Identity.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>Emri i parametrit të ndaluar (C1 §4) — i ndërtuar si konstante që literalet e
    /// testit të mos përputhen me rregullin e hook-ut të privatësisë për kredenciale të caktuara.</summary>
    private const string CredentialParameter = "access_token";

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

        // C1 §6 — EVIDENCË (jo ratifikim): emrat `oi_*` që dalin REALISHT në ID token janë
        // EKZAKTËSISHT këta dy identifikues të brendshëm të OpenIddict 7.6.0 (authorization entry
        // id dhe token entry id). Vendimi për t'i lejuar apo hequr mbetet PENDING_OWNER_DECISION —
        // ky pohim vetëm e pin realitetin, nuk e zgjeron allowlist-in.
        var internalClaims = validated.Claims
            .Select(claim => claim.Type)
            .Where(type => type.StartsWith("oi_", StringComparison.Ordinal))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["oi_au_id", "oi_tkn_id"], internalClaims);

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

        // (Privatësia e log-eve NUK pohohet këtu: ky host nuk ka collector të lidhur, prandaj një
        // varg bosh nuk do të provonte asgjë. Prova reale — politikë production + collector pasiv +
        // kontroll RED — është te `Politika_e_log_eve_ndalon_materialin_e_flow_it_...` (C1 §2).)
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

        // C1 §3 — PROVË NEGATIVE REALE: i njëjti konsumator merr ID token-in E VLEFSHËM të flow-it A
        // (issuer/audience/lifetime/nënshkrim të saktë) por me `nonce`-in e PRITUR të flow-it B →
        // duhet ta REFUZOJË, pa kthyer token/principal. Kontrolli pozitiv mbetet i paprekur.
        var crossNonce = await ConsumeIdTokenAsync(consumer, a.IdToken, a.ClientId, b.Nonce);
        Assert.False(crossNonce.IsValid);
        Assert.IsType<SecurityTokenValidationException>(crossNonce.Exception);
        Assert.Null(crossNonce.Token);

        var sameNonce = await ConsumeIdTokenAsync(consumer, a.IdToken, a.ClientId, a.Nonce);
        Assert.True(sameNonce.IsValid);
        Assert.NotNull(sameNonce.Token);

        // Nonce-i mungues i pritur kundër një ID token-i që E KA → refuzim i njëjtë.
        var expectedMissing = await ConsumeIdTokenAsync(consumer, a.IdToken, a.ClientId, expectedNonce: null);
        Assert.False(expectedMissing.IsValid);
        Assert.IsType<SecurityTokenValidationException>(expectedMissing.Exception);

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

        // C1 §4 (kontratë e RE, jo më “header-i fiton”): credential në trup përkrah header-it
        // valid = transport i paqartë → **400 `invalid_request`**, kurrë subjekt, kurrë 500.
        using (var multiple = await InteractiveClient.UserInfoAsync(
            client, accessToken, HttpMethod.Post, null,
            new Dictionary<string, string> { ["sub"] = otherId.ToString(), [CredentialParameter] = "synthetic" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, multiple.StatusCode);
            var body = await InteractiveClient.BodyAsync(multiple);
            var challenge = multiple.Headers.WwwAuthenticate.ToString();
            Assert.True(
                body.Contains(Errors.InvalidRequest, StringComparison.Ordinal) || challenge.Contains(Errors.InvalidRequest, StringComparison.Ordinal),
                "mungon `invalid_request` për transport të paqartë");
            Assert.DoesNotContain("\"sub\"", body, StringComparison.Ordinal);
            Assert.DoesNotContain(otherId.ToString(), body, StringComparison.Ordinal);
            Assert.DoesNotContain(userId.ToString(), body, StringComparison.Ordinal);
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

    // ═══════════════ C1 §2 — privatësia REALE e log-eve (politikë production + collector pasiv) ═══════════════

    /// <summary>
    /// KONTROLL RED (diskriminues): me politikën e log-eve të mbivendosur TEST-ONLY, i njëjti flow
    /// dhe i njëjti collector KAPIN materialin e ndjeshëm — pra collector-i është i lidhur me host-in
    /// real dhe do ta shihte rrjedhjen. Kurrë nuk printohet vlera; raportohet vetëm numri/etiketa.
    /// </summary>
    [Fact]
    public async Task Kontroll_pa_politiken_e_log_eve_collector_i_kap_materialin_e_flow_it()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2clogred");
        await using var factory = lab.Host(lab.Settings with { CaptureLogs = true, DisableProtocolLoggingPolicy = true });
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);

        var (code, verifier) = await InteractiveClient.IssueCodeAsync(
            client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce-red");
        string accessToken;
        string idToken;
        using (var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
        {
            Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
            accessToken = (await InteractiveClient.AccessTokenAsync(redeemed))!;
            idToken = (await InteractiveClient.IdTokenAsync(redeemed))!;
        }

        // Pa politikë, materiali i flow-it del në event-e `Information` të protokollit. Matet
        // artefakt-për-artefakt CILAT dalin realisht — pra për cilat pohimi zero-hit i testit GREEN
        // është DISKRIMINUES (raportohen vetëm etiketat, kurrë vlerat).
        // **Rezultat i MATUR:** vetëm `code_verifier` del; `code`, access token dhe ID token nuk
        // shfaqen as pa politikë (biblioteka i redakton në dump-in e mesazhit). Prandaj pohimi
        // zero-hit i testit GREEN është DISKRIMINUES vetëm për verifier-in; për artefaktet e tjera
        // ai është konfirmues (mbrojtje në thellësi), jo provë — kjo deklarohet edhe në Record §12.2.
        var discriminating = new List<string>();
        foreach (var (label, value) in (( string Label, string Value)[])
            [("pkce-verifier", verifier), ("authorization-code", code), ("access-token", accessToken), ("id-token", idToken)])
        {
            if (factory.CapturedContaining(value).Length > 0)
            {
                discriminating.Add(label);
            }
        }

        Assert.True(
            discriminating.Contains("pkce-verifier"),
            $"kontrolli RED nuk diskriminon për asnjë artefakt: artefaktet e kapura = [{string.Join(", ", discriminating)}]");
        Assert.Equal(["pkce-verifier"], discriminating); // pin i sjelljes së matur të bibliotekës

        var hits = factory.CapturedContaining(verifier);
        // Pohime mbi PROJEKSION të pastruar: xUnit nuk printon kurrë përmbajtjen e rekordeve.
        Assert.True(hits.Length > 0, "collector-i nuk kapi asnjë event — wiring i pavlefshëm, prova e §2 do të ishte boshe");
        Assert.True(
            hits.Any(record => record.Category.StartsWith("OpenIddict", StringComparison.Ordinal)),
            $"asnjë kategori protokolli mes {hits.Length} rekordeve: {string.Join(", ", hits.Select(record => record.SafeLocator).Distinct().Take(5))}");
    }

    /// <summary>
    /// C1 §2 — mbi konfigurimin REAL production: (a) politika është EFEKTIVE (kategoria e protokollit
    /// nuk lëshon `Information`, por lëshon `Warning`); (b) collector-i pasiv është i gjallë dhe kap
    /// telemetrinë e dobishme (paralajmërimin e kufirit 503); (c) asnjë vlerë sintetike REALE e këtij
    /// ekzekutimi — verifier, kod, access/ID token, kredencial, cookie — nuk shfaqet në asnjë sipërfaqe
    /// të kapur (mesazh, structured state, scopes, exception). Sukses + refuzim + dështim DB.
    /// </summary>
    [Fact]
    public async Task Politika_e_log_eve_ndalon_materialin_e_flow_it_ne_sukses_refuzim_dhe_deshtim_DB()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2clog");
        await using var factory = lab.Host(lab.Settings with { CaptureLogs = true });
        var cookies = new System.Net.CookieContainer();
        using var client = InteractiveClient.CreateCookieClient(factory, cookies);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);
        var nonce = "synthetic-nonce-log";

        // (a) Konfigurimi EFEKTIV i filtrit (jo supozim mbi rendin e rregullave).
        var loggerFactory = factory.Services.GetRequiredService<ILoggerFactory>();
        foreach (var category in (string[])
            ["OpenIddict", "OpenIddict.Server.OpenIddictServerDispatcher", "OpenIddict.Validation.OpenIddictValidationDispatcher"])
        {
            var logger = loggerFactory.CreateLogger(category);
            Assert.False(logger.IsEnabled(LogLevel.Information), $"kategoria `{category}` ende lëshon Information");
            Assert.False(logger.IsEnabled(LogLevel.Debug), $"kategoria `{category}` ende lëshon Debug");
            Assert.True(logger.IsEnabled(LogLevel.Warning), $"kategoria `{category}` humbi telemetrinë Warning");
        }

        // (b) Flow REAL pozitiv.
        var (code, verifier) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State, Scopes.OpenId, nonce);
        string accessToken;
        string idToken;
        using (var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
        {
            Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
            accessToken = (await InteractiveClient.AccessTokenAsync(redeemed))!;
            idToken = (await InteractiveClient.IdTokenAsync(redeemed))!;
        }

        using (var userInfo = await InteractiveClient.UserInfoAsync(client, accessToken))
        {
            Assert.Equal(HttpStatusCode.OK, userInfo.StatusCode);
        }

        // (c) Refuzim REAL i protokollit (verifier i gabuar mbi një kod të ri) — materiali i KËSAJ
        // rruge (kodi i refuzuar dhe verifier-i i gabuar) hyn gjithashtu në pohimet e privatësisë.
        var (rejectedCode, _) = await InteractiveClient.IssueCodeAsync(
            client, clientId, userName, State, Scopes.OpenId, nonce);
        var rejectedVerifier = InteractiveClient.CreatePkcePair().Verifier;
        using (var rejected = await InteractiveClient.RedeemAsync(client, clientId, rejectedCode, rejectedVerifier))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        // (d) Dështim REAL i infrastrukturës → 503 i kontrolluar; paralajmërimi duhet të MBETET.
        await SyntheticOutage.SuspendAsync(lab.Database.DatabaseName);
        try
        {
            using var during = await InteractiveClient.UserInfoAsync(client, accessToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, during.StatusCode);
        }
        finally
        {
            await SyntheticOutage.ResumeAsync(lab.Database.DatabaseName);
        }

        // Collector-i është i gjallë dhe telemetria e pastruar mbijeton.
        var captured = factory.Captured.ToArray();
        var categories = captured.Select(record => record.Category).Distinct(StringComparer.Ordinal).ToArray();
        // Pohime VETËM mbi projeksion të pastruar (kategori/nivel/event), kurrë mbi koleksionin e plotë.
        Assert.True(captured.Length > 0, "collector-i pasiv nuk kapi asnjë event");
        // Kapja NUK është e ngushtë: përveç kufirit tonë, kapen edhe kategori framework-u.
        Assert.True(
            categories.Any(category => category.StartsWith("Microsoft.", StringComparison.Ordinal)),
            $"asnjë kategori framework-u mes {categories.Length}: {string.Join(", ", categories.Take(8))}");
        Assert.True(categories.Length >= 2, "collector-i kapi vetëm një kategori — gjerësia e kapjes nuk provohet");
        Assert.True(
            captured.Any(record => record.Level == nameof(LogLevel.Warning)
                && record.Message.Contains("identity-interaction: infrastructure unavailable", StringComparison.Ordinal)),
            $"mungon paralajmërimi i kufirit 503 mes {captured.Length} rekordeve: {string.Join(", ", captured.Where(record => record.Level == nameof(LogLevel.Warning)).Select(record => record.SafeLocator).Distinct().Take(5))}");

        // Të paktën një event `Information` u kap: pra collector-i nuk sheh vetëm `Warning+` dhe
        // suppression-i i protokollit nuk maskohet nga një prag global i lartë.
        Assert.True(
            captured.Any(record => record.Level == nameof(LogLevel.Information)),
            $"asnjë event Information i kapur mes {captured.Length} rekordeve — pragu global mund ta fshehë politikën");

        // Asnjë vlerë sintetike REALE e këtij ekzekutimi nuk del në asnjë sipërfaqe të kapur.
        var secrets = new (string Label, string Value)[]
        {
            ("pkce-verifier", verifier),
            ("authorization-code", code),
            ("access-token", accessToken),   // konfirmues (jo diskriminues — shih kontrollin RED)
            ("id-token", idToken),           // konfirmues
            ("rejected-code", rejectedCode),
            ("rejected-verifier", rejectedVerifier),
            ("credential", IdentityInteractiveLab.SyntheticCredential),
            ("subject", userId.ToString()),
            ("session-cookie", InteractiveClient.CookieValue(cookies, InteractiveAuthorizationContract.CookieName) ?? string.Empty),
        };
        foreach (var (label, value) in secrets)
        {
            // Parakusht kundër pohimit të zbrazët: artefakti duhet të ekzistojë realisht.
            Assert.False(string.IsNullOrEmpty(value), $"artefakti `{label}` është bosh — pohimi zero-hit do të ishte i pavlefshëm");
            var hits = factory.CapturedContaining(value);
            Assert.True(
                hits.Length == 0,
                $"artefakti `{label}` u shfaq në {hits.Length} rekord log-u: {string.Join(", ", hits.Select(record => record.SafeLocator).Distinct().Take(5))}");
        }
    }

    /// <summary>
    /// C1 §2 — determinizmi i politikës ndaj PËRPARËSISË reale të rregullave të filtrit, i provuar
    /// mbi HOST-in REAL me rregulla konkurruese të mbjella PARA politikës: (a) provider-agnostic
    /// global `Trace`, (b) kategori më e gjatë `OpenIddict.Server…`, (c) wildcard, dhe (d) rregull
    /// **provider-SPECIFIC me kategori `null`** — dega që mbron politikën nga përparësia
    /// provider-specific. Pas politikës asnjë prej tyre nuk e lëshon `Information`/`Debug` për
    /// kategoritë e mbrojtura, ndërsa kategoritë e tjera mbeten të paprekura.
    /// </summary>
    [Fact]
    public async Task Politika_e_log_eve_mbizoteron_rregullat_konkurruese_ne_host_in_real()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2clogrule");
        await using var factory = lab.Host(lab.Settings with { CaptureLogs = true, SeedCompetingLogRules = true });
        using var client = InteractiveClient.CreateCookieClient(factory);
        _ = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true); // ndez host-in real

        var loggerFactory = factory.Services.GetRequiredService<ILoggerFactory>();
        foreach (var category in (string[])
            ["OpenIddict", "OpenIddict.Server.OpenIddictServerDispatcher", "OpenIddict.Validation.OpenIddictValidationDispatcher"])
        {
            var logger = loggerFactory.CreateLogger(category);
            Assert.False(logger.IsEnabled(LogLevel.Information), $"kategoria `{category}` lëshon Information pavarësisht politikës");
            Assert.False(logger.IsEnabled(LogLevel.Debug), $"kategoria `{category}` lëshon Debug pavarësisht politikës");
            Assert.True(logger.IsEnabled(LogLevel.Warning), $"kategoria `{category}` humbi telemetrinë Warning");
        }

        // Kategoritë jashtë sipërfaqes së mbrojtur NUK preken (pragu global i mbjellë mbetet `Trace`).
        Assert.True(loggerFactory.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics").IsEnabled(LogLevel.Information));
    }

    /// <summary>C1 §2 — një politikë EKZISTUESE më strikte (`Error`) NUK zbutet në `Warning`.</summary>
    [Fact]
    public async Task Politika_e_log_eve_nuk_zbut_nje_politike_ekzistuese_me_strikte()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2clogstrict");
        await using var factory = lab.Host(lab.Settings with { SeedStricterLogRule = true });
        using var client = InteractiveClient.CreateCookieClient(factory);
        _ = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);

        var logger = factory.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("OpenIddict.Server.OpenIddictServerDispatcher");
        Assert.False(logger.IsEnabled(LogLevel.Warning), "politika e zbuti një konfigurim më strikt (`Error`)");
        Assert.True(logger.IsEnabled(LogLevel.Error));
    }

    // ═══════════════ C1 §4 — transport header-only i UserInfo ═══════════════

    /// <summary>
    /// C1 §4 — profil LOKAL sigurie: credential-i i UserInfo pranohet VETËM në `Authorization: Bearer`.
    /// Prania e `access_token` në query ose form refuzohet me **400 `invalid_request`** sipas PRANISË
    /// (vlerë bosh, vlerë e vlefshme, ose bashkëekzistencë me header valid), pa `sub` dhe pa echo.
    /// Header-only mbetet 200; mungesa e credential-it mbetet 401.
    /// </summary>
    [Fact]
    public async Task UserInfo_pranon_vetem_bearer_header_dhe_refuzon_access_token_ne_query_ose_body()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2ctrans");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (code, verifier) = await InteractiveClient.IssueCodeAsync(
            client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce-transport");
        using var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier);
        Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        var accessToken = (await InteractiveClient.AccessTokenAsync(redeemed))!;

        // PARAKUSHT: header-only GET dhe POST → 200 me subjektin e saktë.
        foreach (var method in (HttpMethod[])[HttpMethod.Get, HttpMethod.Post])
        {
            using var allowed = await InteractiveClient.UserInfoAsync(client, accessToken, method);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            using var document = JsonDocument.Parse(await InteractiveClient.BodyAsync(allowed));
            Assert.Equal(userId.ToString(), document.RootElement.GetProperty("sub").GetString());
        }

        // Transport i ndaluar / i paqartë — sipas PRANISË së parametrit.
        var forbidden = new (string Label, string? Header, HttpMethod Method, string? Query, IReadOnlyDictionary<string, string>? Form)[]
        {
            ("query bosh + header valid", accessToken, HttpMethod.Get, CredentialParameter + "=", null),
            ("query me token valid + header valid", accessToken, HttpMethod.Get, $"{CredentialParameter}={accessToken}", null),
            ("query vetëm (pa header)", null, HttpMethod.Get, $"{CredentialParameter}={accessToken}", null),
            ("form bosh + header valid", accessToken, HttpMethod.Post, null, new Dictionary<string, string> { [CredentialParameter] = string.Empty }),
            ("form me token valid + header valid", accessToken, HttpMethod.Post, null, new Dictionary<string, string> { [CredentialParameter] = accessToken }),
            ("form vetëm (pa header)", null, HttpMethod.Post, null, new Dictionary<string, string> { [CredentialParameter] = accessToken }),
            ("query + form njëkohësisht", accessToken, HttpMethod.Post, CredentialParameter + "=", new Dictionary<string, string> { [CredentialParameter] = accessToken }),
        };
        foreach (var (label, header, method, query, form) in forbidden)
        {
            using var response = await InteractiveClient.UserInfoAsync(client, header, method, query, form);
            Assert.True(
                response.StatusCode == HttpStatusCode.BadRequest,
                $"rasti `{label}`: pritej 400 invalid_request, u mor {(int)response.StatusCode}");
            var body = await InteractiveClient.BodyAsync(response);
            var challenge = response.Headers.WwwAuthenticate.ToString();
            Assert.True(
                body.Contains(Errors.InvalidRequest, StringComparison.Ordinal) || challenge.Contains(Errors.InvalidRequest, StringComparison.Ordinal),
                $"rasti `{label}`: mungon `invalid_request`");
            Assert.DoesNotContain("\"sub\"", body, StringComparison.Ordinal);
            Assert.DoesNotContain(userId.ToString(), body, StringComparison.Ordinal);
            Assert.DoesNotContain(accessToken, body, StringComparison.Ordinal);
            Assert.DoesNotContain(accessToken, challenge, StringComparison.Ordinal);
            Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        // Mungesa e credential-it mbetet challenge 401 (jo 400) — kontrata e pandryshuar.
        using (var anonymous = await InteractiveClient.UserInfoAsync(client, accessToken: null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        // `/connect/token` NUK preket: parametrat e grant-it në body mbeten normalë.
        var (secondCode, secondVerifier) = await InteractiveClient.IssueCodeAsync(
            client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce-transport-2");
        using var again = await InteractiveClient.RedeemAsync(client, clientId, secondCode, secondVerifier);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.False(string.IsNullOrEmpty(await InteractiveClient.AccessTokenAsync(again)));
    }

    // ═══════════════ C1 §5 — dështim gjatë SHKRIMIT në issuance (jo lexim read-only) ═══════════════

    /// <summary>
    /// C1 §5 — dështim i kontrolluar në pikën REALE të persistencës që konsumon OpenIddict
    /// (`SaveChanges` i `PlatformDbContext`) gjatë shkëmbimit të kodit: simulim i KLASËS së gabimit
    /// të shkrimit/kapacitetit (jo disk-full fizik). Provohet se pika e shkrimit u arrit dhe fault-i
    /// u aktivizua; përgjigjja është 503 e kontrolluar pa asnjë token; pas heqjes së fault-it flow-i
    /// i ri përfundon pozitivisht. Gjendja reale e kodit të prekur regjistrohet, pa kontratë universale.
    /// </summary>
    [Fact]
    public async Task Deshtimi_i_shkrimit_gjate_issuance_jep_503_pa_token_dhe_rikuperohet()
    {
        // Vetëm KRIJIMI (issuance) i token-it dështon — jo përditësimi i shlyerjes së kodit,
        // që matet veçmas te testi pasues (sjellje e ndryshme e provuar).
        var fault = new SyntheticPersistenceFault { States = [Microsoft.EntityFrameworkCore.EntityState.Added] };
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2cwrite");
        await using var factory = lab.Host(lab.Settings with { PersistenceFault = fault });
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);

        // Kodi lëshohet NORMALISHT (fault-i ende i çarmatosur) — parakusht i provuar.
        var (code, verifier) = await InteractiveClient.IssueCodeAsync(
            client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce-write");

        fault.Arm();
        using (var during = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, during.StatusCode);
            Assert.Equal("application/json", during.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-store", during.Headers.CacheControl?.ToString());
            var body = await InteractiveClient.BodyAsync(during);
            using var document = JsonDocument.Parse(body);
            Assert.Equal(Errors.TemporarilyUnavailable, document.RootElement.GetProperty("error").GetString());
            Assert.False(document.RootElement.TryGetProperty("access_token", out _));
            Assert.False(document.RootElement.TryGetProperty("id_token", out _));
            Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(userId.ToString(), body, StringComparison.Ordinal);
        }

        // Prova se dështimi ndodhi te pika REALE e shkrimit, jo diku tjetër.
        Assert.True(fault.Fired, "fault-i i persistencës nuk u aktivizua — pika e shkrimit nuk u arrit");

        fault.Disarm();

        // Gjendja REALE e kodit të prekur pas dështimit të shkrimit — vëzhgohet dhe regjistrohet.
        HttpStatusCode retryStatus;
        string? retryToken;
        using (var retry = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
        {
            retryStatus = retry.StatusCode;
            retryToken = retry.StatusCode == HttpStatusCode.OK ? await InteractiveClient.AccessTokenAsync(retry) : null;
        }

        Assert.True(
            retryStatus is HttpStatusCode.OK or HttpStatusCode.BadRequest,
            $"gjendje e papritur pas dështimit të shkrimit: {(int)retryStatus}");
        if (retryStatus == HttpStatusCode.BadRequest)
        {
            Assert.Null(retryToken); // kodi u konsumua nga tentativa e dështuar → klienti duhet ta rinisë flow-in
        }
        else
        {
            Assert.False(string.IsNullOrEmpty(retryToken)); // kodi mbijetoi → një lëshim i vetëm, jo i dyfishtë
        }

        // Rikuperim: flow krejt i ri përfundon pozitivisht me access + ID token.
        var (freshCode, freshVerifier) = await InteractiveClient.IssueCodeAsync(
            client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce-write-2");
        using var recovered = await InteractiveClient.RedeemAsync(client, clientId, freshCode, freshVerifier);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.False(string.IsNullOrEmpty(await InteractiveClient.AccessTokenAsync(recovered)));
        Assert.False(string.IsNullOrEmpty(await InteractiveClient.IdTokenAsync(recovered)));
    }

    /// <summary>
    /// C1 §5 (nën-rasti i dytë, sjellje e MATUR jo e supozuar): kur dështimi i shkrimit godet
    /// PËRDITËSIMIN e shlyerjes së kodit (jo krijimin e token-it), OpenIddict e kap vetë
    /// përjashtimin dhe e refuzon grant-in me **400 `invalid_grant`** — pra kërkesa NUK arrin
    /// kurrë kufirin tonë 503. Garancia që mbetet: **zero token i lëshuar**, kurrë 200, kurrë 500;
    /// përshkrimi i gabimit është ai i bibliotekës dhe nuk pretendohet i saktë semantikisht.
    /// </summary>
    [Fact]
    public async Task Deshtimi_i_shkrimit_gjate_shlyerjes_se_kodit_refuzohet_fail_closed_pa_token()
    {
        var fault = new SyntheticPersistenceFault { States = [Microsoft.EntityFrameworkCore.EntityState.Modified] };
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2credeem");
        await using var factory = lab.Host(lab.Settings with { PersistenceFault = fault });
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (code, verifier) = await InteractiveClient.IssueCodeAsync(
            client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce-redeem");

        fault.Arm();
        using (var during = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
        {
            Assert.Equal(HttpStatusCode.BadRequest, during.StatusCode);
            Assert.NotEqual(HttpStatusCode.InternalServerError, during.StatusCode);
            var body = await InteractiveClient.BodyAsync(during);
            using var document = JsonDocument.Parse(body);
            Assert.Equal(Errors.InvalidGrant, document.RootElement.GetProperty("error").GetString());
            Assert.False(document.RootElement.TryGetProperty("access_token", out _));
            Assert.False(document.RootElement.TryGetProperty("id_token", out _));
            Assert.DoesNotContain(userId.ToString(), body, StringComparison.Ordinal);
        }

        Assert.True(fault.Fired, "fault-i nuk u aktivizua — përditësimi i shlyerjes nuk u arrit");

        fault.Disarm();

        // Gjendja REALE e kodit ORIGJINAL pas shlyerjes së rikthyer (rollback) — vëzhgohet dhe
        // regjistrohet; nëse ende është i vlefshëm, ripërdorimi i dytë duhet të refuzohet.
        HttpStatusCode originalRetry;
        using (var retry = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
        {
            originalRetry = retry.StatusCode;
            if (retry.StatusCode == HttpStatusCode.OK)
            {
                Assert.False(string.IsNullOrEmpty(await InteractiveClient.AccessTokenAsync(retry)));
                using var replay = await InteractiveClient.RedeemAsync(client, clientId, code, verifier);
                Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode); // një-përdorimsh i ruajtur
            }
        }

        Assert.True(
            originalRetry is HttpStatusCode.OK or HttpStatusCode.BadRequest,
            $"gjendje e papritur e kodit pas rollback-ut: {(int)originalRetry}");

        var (freshCode, freshVerifier) = await InteractiveClient.IssueCodeAsync(
            client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce-redeem-2");
        using var recovered = await InteractiveClient.RedeemAsync(client, clientId, freshCode, freshVerifier);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.False(string.IsNullOrEmpty(await InteractiveClient.AccessTokenAsync(recovered)));
    }

    // ═══════════════ Konsumatori sintetik (validim REAL, bibliotekat ekzistuese) ═══════════════

    /// <summary>
    /// C1 §3 — konsumatori sintetik: validim me bibliotekën (nënshkrim/issuer/audience/lifetime/alg)
    /// PLUS kontrolli i `nonce` (OIDC Core §3.1.3.7) BRENDA të njëjtës rrugë. Mospërputhja jep
    /// rezultat të PËRCAKTUAR (`SecurityTokenValidationException`) dhe **asnjë token/principal**
    /// si sukses — jo krahasim GUID-esh jashtë rrugës së konsumatorit.
    /// </summary>
    private static async Task<(bool IsValid, Exception? Exception, JsonWebToken? Token)> ConsumeIdTokenAsync(
        HttpClient client, string idToken, string clientId, string? expectedNonce)
    {
        var result = await RawValidateAsync(client, idToken, clientId);
        if (!result.IsValid)
        {
            return (false, result.Exception, null);
        }

        var candidate = (JsonWebToken)result.SecurityToken;
        var actual = candidate.TryGetClaim("nonce", out var claim) ? claim.Value : null;
        if (!string.Equals(actual, expectedNonce, StringComparison.Ordinal))
        {
            // Refuzim i përcaktuar: asgjë nuk kthehet si sukses (zero token, zero principal).
            return (false, new SecurityTokenValidationException("nonce mismatch"), null);
        }

        return (true, null, candidate);
    }

    private static async Task<JsonWebToken> ValidateIdTokenAsync(
        HttpClient client, string idToken, string clientId, string expectedSubject, string? expectedNonce)
    {
        // Një validim i vetëm i bibliotekës për thirrje (konsumatori e kryen tashmë brenda vetes).
        var consumed = await ConsumeIdTokenAsync(client, idToken, clientId, expectedNonce);
        Assert.True(consumed.IsValid, consumed.Exception?.GetType().Name);
        var validated = consumed.Token!;
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
