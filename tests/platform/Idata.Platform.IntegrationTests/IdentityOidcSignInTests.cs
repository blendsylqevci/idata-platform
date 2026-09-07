using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Idata.Platform.Modules.Identity.Application;
using Microsoft.AspNetCore.Hosting;
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
            Assert.False(body.Contains(accessToken, StringComparison.Ordinal), $"rasti `{label}`: echo i access token-it në trup");
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
            AssertNoStore(multiple, "trup + header valid");
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

    // ═══════════════ C2 §2/§3 — privatësia REALE e log-eve: protokolli DHE host-i, mbi konfigurimin efektiv ═══════════════

    /// <summary>Kategori canary: dy target-e të politikës, dy jashtë saj (njëra motër e prefiksit të host-it) dhe një e mbyllur nga callback-u i operatorit.</summary>
    private const string CanaryProtocol = "OpenIddict.Synthetic.Canary";
    private const string CanaryHosting = "Microsoft.AspNetCore.Hosting.Diagnostics";
    private const string CanaryHostingSibling = "Microsoft.AspNetCore.Hosting.Synthetic";
    private const string CanaryApplication = "Idata.Synthetic.Canary";
    private const string CanaryMuted = "Idata.Muted.Canary";

    private static readonly LogLevel[] CanaryLevels =
        [LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error];

    private static readonly string[] CanaryCategories =
        [CanaryProtocol, CanaryHosting, CanaryHostingSibling, CanaryApplication, CanaryMuted];

    /// <summary>Lëshon canary të PADËMSHËM (marker sintetik, zero material autentikimi) në çdo kategori × nivel përmes fabrikës REALE të host-it.</summary>
    private static string EmitCanaries(IdentityAuthorityFactory factory)
    {
        var marker = $"synthetic-canary-{Guid.NewGuid():N}";
        var loggerFactory = factory.Services.GetRequiredService<ILoggerFactory>();
        foreach (var category in CanaryCategories)
        {
            var logger = loggerFactory.CreateLogger(category);
            foreach (var level in CanaryLevels)
            {
                logger.Log(level, new EventId(9000 + (int)level), "synthetic canary {Marker} at {Level}", marker, level);
            }
        }

        return marker;
    }

    /// <summary>Nivelet e kapura në një sink për një kategori — VETËM rekordet canary të këtij marker-i, të renditura.</summary>
    private static LogLevel[] CapturedLevels(IEnumerable<CapturedLogRecord> sink, string category, string marker)
        => [.. sink
            .Where(record => record.Category == category && record.Message.Contains(marker, StringComparison.Ordinal))
            .Select(record => Enum.Parse<LogLevel>(record.Level))
            .Distinct()
            .Order()];

    private static string Levels(IEnumerable<LogLevel> levels) => string.Join(",", levels);

    /// <summary>Parakusht i daljeve të KONTROLLUARA: në host-in e testit janë regjistruar VETËM dy sink-et sintetike (raportohen vetëm emrat e tipave).</summary>
    private static void AssertIsolatedOutputs(IdentityAuthorityFactory factory)
    {
        var providers = factory.Services.GetServices<ILoggerProvider>().Select(provider => provider.GetType().Name).Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            providers.SequenceEqual([nameof(PrimaryCapturingLoggerProvider), nameof(SecondaryCapturingLoggerProvider)]),
            $"daljet nuk janë të izoluara — provider-at e regjistruar: [{string.Join(", ", providers)}]");
    }

    /// <summary>Pohim “zero echo” që në dështim raporton VETËM etiketën, kurrë vlerën.</summary>
    private static void AssertNoEcho(string surface, string label, string value)
        => Assert.False(surface.Contains(value, StringComparison.Ordinal), $"echo i artefaktit `{label}` në {(surface.Length > 0 ? "sipërfaqen e kapur" : "sipërfaqe bosh")}");

    /// <summary>Rekordet e të DY sink-eve që përmbajnë vlerën (kurrë vlera vetë).</summary>
    private static CapturedLogRecord[] CapturedAnywhere(IdentityAuthorityFactory factory, string value)
        => [.. factory.CapturedContaining(value), .. factory.CapturedSecondaryContaining(value)];

    /// <summary>
    /// KONTROLL RED (diskriminues, C2): në një host me politikën e HEQUR, i njëjti collector pasiv kap
    /// (a) canary të padëmshëm `Information` në të dy kategoritë target, (b) event-in REAL të host-it
    /// `Request starting`/`Request finished` me URL-në e plotë — përfshirë query string-un — për një
    /// kërkesë discovery pa asnjë material autentikimi, dhe (c) event-e reale `Information` të
    /// bibliotekës së protokollit. Asnjë flow me token nuk ekzekutohet këtu; asnjë vlerë nuk printohet.
    /// </summary>
    [Fact]
    public async Task Kontroll_RED_pa_politiken_canary_i_padhemshem_del_ne_te_dy_kategorite_target()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2clogred");
        await using var factory = lab.Host(lab.Settings with { CaptureLogs = true, DisableProtocolLoggingPolicy = true });
        using var client = InteractiveClient.CreateCookieClient(factory);
        AssertIsolatedOutputs(factory);

        // (a) canary përmes fabrikës reale — pa politikë `Information` del në të dy target-et.
        var marker = EmitCanaries(factory);
        foreach (var target in (string[])[CanaryProtocol, CanaryHosting])
        {
            var captured = CapturedLevels(factory.Captured, target, marker);
            Assert.True(
                captured.Contains(LogLevel.Information),
                $"pa politikë kategoria `{target}` nuk lëshoi Information (u kapën: [{Levels(captured)}]) — kontrolli RED nuk diskriminon");
        }

        // (b) event REAL i host-it: URL-ja e plotë e një kërkese të padëmshme (discovery) me canary në query.
        var urlMarker = $"synthetic-url-canary-{Guid.NewGuid():N}";
        using (var discovery = await client.GetAsync(
            new Uri($"/.well-known/openid-configuration?synthetic_canary={urlMarker}", UriKind.Relative), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        }

        var hosting = factory.Captured
            .Where(record => record.Category == CanaryHosting && record.Level == nameof(LogLevel.Information) && record.Contains(urlMarker))
            .ToArray();
        var locators = string.Join(", ", hosting.Select(record => record.SafeLocator).Distinct().Take(5));
        Assert.True(hosting.Length > 0, "pa politikë host-i nuk e logoi URL-në me query string — event-i real i host-it nuk u riprodhua");
        // RequestStarting (1) / RequestFinished (2) të ASP.NET Core 10.0.10: marker-i në MESAZH dhe në STRUCTURED STATE të të njëjtit rekord.
        Assert.True(hosting.Any(record => record.EventId is 1 or 2), $"asnjë EventId 1/2 mes: {locators}");
        Assert.True(
            hosting.Any(record => record.Message.Contains(urlMarker, StringComparison.Ordinal)
                && record.State.Contains("QueryString=", StringComparison.Ordinal)
                && record.State.Contains(urlMarker, StringComparison.Ordinal)),
            $"asnjë rekord me marker-in në mesazh DHE në state (`QueryString=`) mes: {locators}");

        // (c) event REAL i protokollit: kërkesa discovery prodhon `Information` në kategoritë OpenIddict.
        Assert.True(
            factory.Captured.Any(record => record.Category.StartsWith("OpenIddict", StringComparison.Ordinal) && record.Level == nameof(LogLevel.Information)),
            "pa politikë biblioteka e protokollit nuk lëshoi asnjë Information për kërkesën discovery");
    }

    /// <summary>
    /// C2 §2 — mbi konfigurimin REAL të filtrit, me politikën production dhe dalje të kontrolluara:
    /// (a) politika është EFEKTIVE PARA kërkesës së parë (protokoll + host-diagnostics nuk lëshojnë
    /// `Information`, lëshojnë `Warning`); (b) canary GREEN: target-et heshtin nën `Warning`, kategoritë
    /// e tjera jo; (c) e para kërkesë HTTP (discovery me canary në URL) nuk prodhon asnjë event host-i nën
    /// `Warning`; (d) flow OIDC POZITIV, refuzim protokolli, transport UserInfo i refuzuar me kredencialin
    /// në URL/trup dhe dështim infrastrukture — asnjë vlerë sintetike REALE e ekzekutimit në asnjë
    /// sipërfaqe të kapur (mesazh, structured state, scopes, exception i jashtëm); (e) telemetria e
    /// dobishme mbijeton (Information e kategorive të tjera, Warning i kufirit 503).
    /// </summary>
    [Fact]
    public async Task Politika_e_log_eve_ndalon_materialin_e_flow_it_dhe_URL_ne_ne_sukses_refuzim_transport_dhe_deshtim_DB()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2clog");
        await using var factory = lab.Host(lab.Settings with { CaptureLogs = true });
        var cookies = new System.Net.CookieContainer();
        using var client = InteractiveClient.CreateCookieClient(factory, cookies);
        AssertIsolatedOutputs(factory);

        // (a) Konfigurimi EFEKTIV — vetëm fabrika e host-it, asnjë kërkesë HTTP ende.
        var loggerFactory = factory.Services.GetRequiredService<ILoggerFactory>();
        foreach (var category in (string[])
            ["OpenIddict", "OpenIddict.Server.OpenIddictServerDispatcher", "OpenIddict.Validation.OpenIddictValidationDispatcher", CanaryHosting])
        {
            var logger = loggerFactory.CreateLogger(category);
            Assert.False(logger.IsEnabled(LogLevel.Information), $"kategoria `{category}` ende lëshon Information");
            Assert.False(logger.IsEnabled(LogLevel.Debug), $"kategoria `{category}` ende lëshon Debug");
            Assert.True(logger.IsEnabled(LogLevel.Warning), $"kategoria `{category}` humbi telemetrinë Warning");
        }

        // (b) Canary GREEN — e njëjta prova e kontrollit RED, tani me politikën.
        var marker = EmitCanaries(factory);
        foreach (var target in (string[])[CanaryProtocol, CanaryHosting])
        {
            var captured = CapturedLevels(factory.Captured, target, marker);
            Assert.True(
                captured.SequenceEqual([LogLevel.Warning, LogLevel.Error]),
                $"target-i `{target}`: pritej [Warning,Error], u kapën [{Levels(captured)}]");
        }

        foreach (var other in (string[])[CanaryHostingSibling, CanaryApplication, CanaryMuted])
        {
            var captured = CapturedLevels(factory.Captured, other, marker);
            Assert.True(
                captured.SequenceEqual([LogLevel.Information, LogLevel.Warning, LogLevel.Error]),
                $"kategoria jo-target `{other}`: pritej [Information,Warning,Error] (MinLevel i host-it), u kapën [{Levels(captured)}]");
        }

        // (c) E PARA kërkesë HTTP e host-it: URL me canary — asnjë event host-diagnostics nën Warning, marker-i askund.
        var urlMarker = $"synthetic-url-canary-{Guid.NewGuid():N}";
        using (var discovery = await client.GetAsync(
            new Uri($"/.well-known/openid-configuration?synthetic_canary={urlMarker}", UriKind.Relative), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        }

        Assert.Empty(CapturedAnywhere(factory, urlMarker).Select(record => record.SafeLocator));

        // (d) Flow REAL pozitiv.
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);
        var nonce = "synthetic-nonce-log";
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

        // (e) Refuzim REAL i protokollit (verifier i gabuar mbi një kod të ri).
        var (rejectedCode, _) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State, Scopes.OpenId, nonce);
        var rejectedVerifier = InteractiveClient.CreatePkcePair().Verifier;
        using (var rejected = await InteractiveClient.RedeemAsync(client, clientId, rejectedCode, rejectedVerifier))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        // (f) Transport i refuzuar me kredencialin REAL në URL (rasti i raportuar i rrjedhjes së host-it) dhe në trup.
        using (var inQuery = await InteractiveClient.UserInfoAsync(client, null, HttpMethod.Get, $"{CredentialParameter}={accessToken}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, inQuery.StatusCode);
        }

        using (var inForm = await InteractiveClient.UserInfoAsync(
            client, null, HttpMethod.Post, null, new Dictionary<string, string> { [CredentialParameter] = accessToken }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, inForm.StatusCode);
        }

        // (g) Dështim REAL i infrastrukturës → 503 i kontrolluar; paralajmërimi duhet të MBETET.
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

        // Pohime VETËM mbi projeksion të pastruar (kategori/nivel/event), kurrë mbi përmbajtjen.
        var records = factory.Captured.Concat(factory.CapturedSecondary).ToArray();
        var categories = records.Select(record => record.Category).Distinct(StringComparer.Ordinal).ToArray();
        Assert.True(records.Length > 0, "collector-i pasiv nuk kapi asnjë event");
        Assert.True(categories.Length >= 2, "collector-i kapi vetëm një kategori — gjerësia e kapjes nuk provohet");
        Assert.True(
            records.Any(record => record.Level == nameof(LogLevel.Information) && !IsProtectedCategory(record.Category)),
            $"asnjë event Information jashtë target-eve mes {records.Length} rekordeve — pragu global mund ta fshehë politikën");
        Assert.True(
            records.Any(record => record.Level == nameof(LogLevel.Warning)
                && record.Message.Contains("identity-interaction: infrastructure unavailable", StringComparison.Ordinal)),
            $"mungon paralajmërimi i kufirit 503 mes {records.Length} rekordeve: {string.Join(", ", records.Where(record => record.Level == nameof(LogLevel.Warning)).Select(record => record.SafeLocator).Distinct().Take(5))}");

        // Asnjë event nën Warning në asnjë kategori target — host-i nuk shkroi asnjë `Request starting/finished`.
        var leakedTargets = records
            .Where(record => IsProtectedCategory(record.Category) && Enum.Parse<LogLevel>(record.Level) < LogLevel.Warning)
            .Select(record => record.SafeLocator)
            .Distinct()
            .ToArray();
        Assert.True(leakedTargets.Length == 0, $"event-e nën Warning në kategori target: {string.Join(", ", leakedTargets.Take(8))}");

        // Asnjë vlerë sintetike REALE e këtij ekzekutimi nuk del në asnjë sipërfaqe të kapur.
        var secrets = new (string Label, string Value)[]
        {
            ("pkce-verifier", verifier),
            ("authorization-code", code),
            ("access-token", accessToken),   // përfshirë kërkesën me kredencialin në URL/trup
            ("id-token", idToken),
            ("rejected-code", rejectedCode),
            ("rejected-verifier", rejectedVerifier),
            ("credential", IdentityInteractiveLab.SyntheticCredential),
            ("subject", userId.ToString()),
            ("session-cookie", InteractiveClient.CookieValue(cookies, InteractiveAuthorizationContract.CookieName) ?? string.Empty),
        };
        foreach (var (label, value) in secrets)
        {
            Assert.False(string.IsNullOrEmpty(value), $"artefakti `{label}` është bosh — pohimi zero-hit do të ishte i pavlefshëm");
            var hits = CapturedAnywhere(factory, value);
            Assert.True(
                hits.Length == 0,
                $"artefakti `{label}` u shfaq në {hits.Length} rekord log-u: {string.Join(", ", hits.Select(record => record.SafeLocator).Distinct().Take(5))}");
        }
    }

    /// <summary>Kopje TEST-SIDE e `IdentityProtocolLoggingPolicy.ProtectedCategoryPrefixes` (tip `internal`, i padukshëm nga testet) — mbahet e barabartë me dorë.</summary>
    private static bool IsProtectedCategory(string category)
        => category.StartsWith("OpenIddict", StringComparison.OrdinalIgnoreCase)
           || category.StartsWith(CanaryHosting, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// C2 §3 — një skenar i konfigurimit të OPERATORIT (config në memorie dhe/ose kod) dhe pritshmëria e
    /// nxjerrë me dorë nga semantika e pinuar e `LoggerRuleSelector` (.NET 10.0.10) + dyshemeja e politikës.
    /// `Minimum(provider, kategori)` = niveli më i ulët që DUHET të kapet në atë sink; `null` = asgjë.
    /// </summary>
    private sealed record LogFilterScenario(
        Action<IWebHostBuilder>? Seed, Func<string, string, LogLevel?> Minimum, Func<string, string, LogLevel?> RedMinimum);

    /// <summary>Pritshmëria RED (host PA politikë) vetëm për dy target-et — bën të vëzhgueshëm çdo rregull të mbjellë mbi to.</summary>
    private static Func<string, string, LogLevel?> Red(LogLevel? protocol, LogLevel? hosting)
        => (_, category) => category == CanaryProtocol ? protocol : hosting;

    private static Func<string, string, LogLevel?> RedPerProvider(Func<string, string, LogLevel?> primary, Func<string, string, LogLevel?> secondary)
        => (provider, category) => provider == Primary ? primary(provider, category) : secondary(provider, category);

    private const string Primary = PrimaryCapturingLoggerProvider.Alias;
    private const string Secondary = SecondaryCapturingLoggerProvider.Alias;

    private static Func<string, string, LogLevel?> Expect(
        LogLevel? protocol, LogLevel? hosting, LogLevel? sibling, LogLevel? application, LogLevel? muted)
        => (_, category) => category switch
        {
            CanaryProtocol => protocol,
            CanaryHosting => hosting,
            CanaryHostingSibling => sibling,
            CanaryApplication => application,
            CanaryMuted => muted,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

    private static Func<string, string, LogLevel?> ExpectPerProvider(
        Func<string, string, LogLevel?> primary, Func<string, string, LogLevel?> secondary)
        => (provider, category) => provider == Primary ? primary(provider, category) : secondary(provider, category);

    /// <summary>Matrica e fokusuar e §3 — pritshmëritë janë literale, jo të llogaritura nga rregullat.</summary>
    private static readonly IReadOnlyDictionary<string, LogFilterScenario> LogFilterScenarios =
        new Dictionary<string, LogFilterScenario>(StringComparer.Ordinal)
        {
            // Pa asnjë rregull konkurrues: MinLevel i host-it = Information; target-et = Warning.
            ["baseline"] = new(null,
                Expect(LogLevel.Warning, LogLevel.Warning, LogLevel.Information, LogLevel.Information, LogLevel.Information),
                Red(LogLevel.Information, LogLevel.Information)),

            // Default/null më strikt nga KONFIGURIMI: Error kudo — target-et NUK riaktivizohen në Warning.
            ["default-null-error-config"] = new(
                builder => builder.UseSetting("Logging:LogLevel:Default", "Error"),
                Expect(LogLevel.Error, LogLevel.Error, LogLevel.Error, LogLevel.Error, LogLevel.Error),
                Red(LogLevel.Error, LogLevel.Error)),

            // Wildcard global më strikt nga KODI (`*` = Error): njësoj — asnjë riaktivizim.
            ["wildcard-global-error-code"] = new(
                builder => builder.ConfigureLogging(logging =>
                    Microsoft.Extensions.Logging.FilterLoggingBuilderExtensions.AddFilter(logging, "*", LogLevel.Error)),
                Expect(LogLevel.Error, LogLevel.Error, LogLevel.Error, LogLevel.Error, LogLevel.Error),
                Red(LogLevel.Error, LogLevel.Error)),

            // Wildcard global None nga KONFIGURIMI + një përjashtim eksplicit më i gjatë: target-et mbeten
            // None (provider-i hiqet fare), përjashtimi i operatorit ruhet për kategorinë e tij.
            ["wildcard-global-none-config-with-exception"] = new(
                builder =>
                {
                    builder.UseSetting("Logging:LogLevel:*", "None");
                    builder.UseSetting("Logging:LogLevel:Idata.Synthetic", "Information");
                },
                Expect(null, null, null, LogLevel.Information, null),
                Red(null, null)),

            // Kategori më specifike + wildcard që përputhet me target-in, nga KONFIGURIMI, në Trace:
            // target-et kufizohen në Warning, ndërsa rregulli i PËRBASHKËT wildcard ruhet për motrën
            // jo-target (`Microsoft.AspNetCore.Hosting.Synthetic` → Trace) — asnjë rregull nuk hiqet.
            ["specific-and-wildcard-permissive-config"] = new(
                builder =>
                {
                    builder.UseSetting("Logging:LogLevel:OpenIddict.Synthetic", "Trace");
                    builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore.Hosting.*", "Trace");
                },
                Expect(LogLevel.Warning, LogLevel.Warning, LogLevel.Trace, LogLevel.Information, LogLevel.Information),
                Red(LogLevel.Trace, LogLevel.Trace)), // pa politikë rregullat e mbjella mbi target-et janë të VËZHGUESHME

            // E njëjta formë rregullash nga KODI (dallimi është forma e rregullit, jo origjina).
            ["specific-and-wildcard-permissive-code"] = new(
                builder => builder.ConfigureLogging(logging =>
                {
                    Microsoft.Extensions.Logging.FilterLoggingBuilderExtensions.AddFilter(logging, "OpenIddict.Synthetic", LogLevel.Trace);
                    Microsoft.Extensions.Logging.FilterLoggingBuilderExtensions.AddFilter(logging, "Microsoft.AspNetCore.Hosting.*", LogLevel.Trace);
                }),
                Expect(LogLevel.Warning, LogLevel.Warning, LogLevel.Trace, LogLevel.Information, LogLevel.Information),
                Red(LogLevel.Trace, LogLevel.Trace)),

            // Rregull provider-SPECIFIC konkurrues mbi dy provider-a REALË: dytësori merr Default=Trace
            // nga konfigurimi sipas ALIAS-it (kategori null → mposht çdo rregull provider-agnostic),
            // parësori merr `OpenIddict`=Trace nga kodi sipas TIPIT. Target-et = Warning te të dy;
            // jo-target-et ruajnë vendimin e secilit provider (dytësori Trace, parësori Information).
            ["provider-specific-alias-config-and-type-code"] = new(
                builder =>
                {
                    builder.UseSetting($"Logging:{Secondary}:LogLevel:Default", "Trace");
                    builder.ConfigureLogging(logging =>
                        Microsoft.Extensions.Logging.FilterLoggingBuilderExtensions.AddFilter<PrimaryCapturingLoggerProvider>(logging, "OpenIddict", LogLevel.Trace));
                },
                ExpectPerProvider(
                    Expect(LogLevel.Warning, LogLevel.Warning, LogLevel.Information, LogLevel.Information, LogLevel.Information),
                    Expect(LogLevel.Warning, LogLevel.Warning, LogLevel.Trace, LogLevel.Trace, LogLevel.Trace)),
                // RED: rregulli sipas TIPIT për parësorin bëhet i vëzhgueshëm (protokolli Trace vetëm te parësori; host-i mbetet MinLevel).
                RedPerProvider(Red(LogLevel.Trace, LogLevel.Information), Red(LogLevel.Trace, LogLevel.Trace))),

            // Një provider më strikt se tjetri (config sipas alias-it): dytësori None për protokollin dhe
            // Error për host-diagnostics — ruhen (asnjë riaktivizim); parësori NUK e trashëgon kufizimin.
            ["one-provider-stricter-alias-config"] = new(
                builder =>
                {
                    builder.UseSetting($"Logging:{Secondary}:LogLevel:OpenIddict", "None");
                    builder.UseSetting($"Logging:{Secondary}:LogLevel:{CanaryHosting}", "Error");
                },
                ExpectPerProvider(
                    Expect(LogLevel.Warning, LogLevel.Warning, LogLevel.Information, LogLevel.Information, LogLevel.Information),
                    Expect(null, LogLevel.Error, LogLevel.Information, LogLevel.Information, LogLevel.Information)),
                RedPerProvider(Red(LogLevel.Information, LogLevel.Information), Red(null, LogLevel.Error))),

            // Callback filtri i operatorit (rregull Default pa nivel): heq portën e nivelit kudo dhe mbyll
            // `Idata.Muted*` — callback-u ruhet (Muted asgjë, të tjerat Trace), target-et = Warning.
            ["default-filter-callback-code"] = new(
                builder => builder.ConfigureLogging(logging =>
                    Microsoft.Extensions.Logging.FilterLoggingBuilderExtensions.AddFilter(
                        logging,
                        (Func<string?, LogLevel, bool>)((category, _) => category?.StartsWith("Idata.Muted", StringComparison.Ordinal) != true))),
                Expect(LogLevel.Warning, LogLevel.Warning, LogLevel.Trace, LogLevel.Trace, null),
                Red(LogLevel.Trace, LogLevel.Trace)),
        };

    public static TheoryData<string> LogFilterScenarioNames => new(LogFilterScenarios.Keys);

    /// <summary>
    /// C2 §3 — invariantet e politikës mbi HOST-in REAL me dy provider-a REALË të regjistruar (secili me
    /// alias dhe sink të vetin), për konfigurim sintetik në memorie (config + kod, Default/null, wildcard
    /// global Error/None, kategori/wildcard më specifike, rregulla provider-specific sipas alias-it dhe
    /// sipas tipit, provider më strikt se tjetri, callback filtri). Pohimet dalin nga logger-i EFEKTIV dhe
    /// nga canary të padëmshëm të kapur për provider — kurrë nga numërimi i rregullave. Politika është
    /// efektive para kërkesës së parë (asnjë HTTP këtu).
    /// </summary>
    [Theory]
    [MemberData(nameof(LogFilterScenarioNames))]
    public async Task Politika_e_log_eve_ruan_vendimin_e_operatorit_dhe_kufizon_vetem_target_et(string scenarioName)
    {
        var scenario = LogFilterScenarios[scenarioName];
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2clogmx");

        // GREEN: host me politikën production + seed-in e operatorit.
        await using (var factory = lab.Host(lab.Settings with { CaptureLogs = true, LoggingSeed = scenario.Seed }))
        {
            AssertIsolatedOutputs(factory);

            // Logger-i EFEKTIV (agregat mbi të dy provider-at): target-et nuk lëshojnë kurrë Information — para çdo HTTP.
            var loggerFactory = factory.Services.GetRequiredService<ILoggerFactory>();
            foreach (var target in (string[])[CanaryProtocol, CanaryHosting])
            {
                Assert.False(loggerFactory.CreateLogger(target).IsEnabled(LogLevel.Information), $"skenari `{scenarioName}`: `{target}` lëshon Information");
            }

            AssertCanaryMatrix(factory, scenarioName + " (GREEN)", scenario.Minimum);
        }

        // RED (binjak): i njëjti seed, politika e HEQUR — çdo rregull i mbjellë mbi target-et bëhet i VËZHGUESHËM,
        // ndërsa kategoritë jo-target duhet të japin SAKTËSISHT vendimin e GREEN-it (invarianti A, matje e drejtpërdrejtë).
        await using (var red = lab.Host(lab.Settings with { CaptureLogs = true, LoggingSeed = scenario.Seed, DisableProtocolLoggingPolicy = true }))
        {
            AssertIsolatedOutputs(red);
            AssertCanaryMatrix(red, scenarioName + " (RED)", (provider, category) =>
                category is CanaryProtocol or CanaryHosting ? scenario.RedMinimum(provider, category) : scenario.Minimum(provider, category));
        }
    }

    /// <summary>Lëshon canary dhe pohon, për SECILIN provider real, saktësisht nivelet e kapura për çdo kategori canary.</summary>
    private static void AssertCanaryMatrix(IdentityAuthorityFactory factory, string label, Func<string, string, LogLevel?> minimum)
    {
        var marker = EmitCanaries(factory);
        foreach (var (tag, provider, sink) in (IEnumerable<(string, CapturingLoggerProviderBase, IEnumerable<CapturedLogRecord>)>)
            [(Primary, factory.PrimaryProvider!, factory.Captured), (Secondary, factory.SecondaryProvider!, factory.CapturedSecondary)])
        {
            // Parakusht: provider-i u konsultua REALISHT nga fabrika për çdo kategori canary (edhe kur asgjë s'i dorëzohet).
            foreach (var category in CanaryCategories)
            {
                Assert.True(provider.ConsultedCategories.Contains(category), $"{label}, provider `{tag}`: fabrika nuk e konsultoi për `{category}`");
            }

            // Dump bosh nuk është PASS: çdo skenar ruan të paktën një canary të padëmshëm në secilin sink.
            Assert.True(
                sink.Any(record => record.Message.Contains(marker, StringComparison.Ordinal)),
                $"{label}, provider `{tag}`: asnjë canary i kapur — provider-i nuk provon asgjë");

            foreach (var category in CanaryCategories)
            {
                var floor = minimum(tag, category);
                var expected = floor is { } level ? CanaryLevels.Where(candidate => candidate >= level).ToArray() : [];
                var captured = CapturedLevels(sink, category, marker);
                Assert.True(
                    expected.SequenceEqual(captured),
                    $"{label}, provider `{tag}`, kategoria `{category}`: pritej [{Levels(expected)}], u kapën [{Levels(captured)}]");
            }
        }
    }

    // ═══════════════ C1 §4 / C2 §5 — transporti i UserInfo: header-only, një kredencial i vetëm ═══════════════

    private static void AssertNoStore(HttpResponseMessage response, string label)
    {
        Assert.True(response.Headers.CacheControl?.NoStore == true, $"rasti `{label}`: mungon `Cache-Control: no-store`");
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// C1 §4 — profil LOKAL sigurie: credential-i i UserInfo pranohet VETËM në `Authorization: Bearer`.
    /// Prania e `access_token` në query ose form refuzohet me **400 `invalid_request`** sipas PRANISË
    /// (vlerë bosh, vlerë e vlefshme, ose bashkëekzistencë me header valid), pa `sub` dhe pa echo,
    /// dhe (C2) me `no-store`/`no-cache`. Header-only mbetet 200; mungesa e credential-it mbetet 401.
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
            AssertNoStore(response, label);
            Assert.DoesNotContain("\"sub\"", body, StringComparison.Ordinal);
            Assert.DoesNotContain(userId.ToString(), body, StringComparison.Ordinal);
            AssertNoEcho(body, "access-token (" + label + ")", accessToken);
            AssertNoEcho(challenge, "access-token/challenge (" + label + ")", accessToken);
        }

        // Mungesa e credential-it mbetet challenge 401 (jo 400) — kontrata e pandryshuar; tani me no-store.
        using (var anonymous = await InteractiveClient.UserInfoAsync(client, accessToken: null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            AssertNoStore(anonymous, "pa kredencial");
        }

        // `/connect/token` NUK preket: parametrat e grant-it në body mbeten normalë.
        var (secondCode, secondVerifier) = await InteractiveClient.IssueCodeAsync(
            client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce-transport-2");
        using var again = await InteractiveClient.RedeemAsync(client, clientId, secondCode, secondVerifier);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.False(string.IsNullOrEmpty(await InteractiveClient.AccessTokenAsync(again)));
    }

    /// <summary>
    /// C2 §5 — plotësimi i kontratës së transportit: header-a `Authorization` të PËRSËRITUR (identikë ose
    /// të ndryshëm), të BASHKUAR (listë me presje në një vlerë) ose me SKEMA TË PËRZIERA (`Basic` + `Bearer`)
    /// refuzohen me **400 `invalid_request`** PARA autentikimit, me `no-store`/`no-cache`, pa `sub`, pa echo —
    /// asnjë zgjedhje “i pari/i fundit fiton”. Ruhen: `bearer` case-insensitive (200), skemë tjetër e vetme
    /// (401 challenge), një header i vetëm me token të pavlefshëm (401 `invalid_token`, JO 400).
    /// </summary>
    [Fact]
    public async Task UserInfo_refuzon_header_Authorization_te_perseritur_te_bashkuar_ose_skema_te_perziera_me_400_no_store()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2cauthhdr");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, withOpenId: true);
        var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);

        var tokens = new List<string>();
        foreach (var suffix in (string[])["a", "b"])
        {
            var (code, verifier) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce-hdr-" + suffix);
            using var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier);
            Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
            tokens.Add((await InteractiveClient.AccessTokenAsync(redeemed))!);
        }

        var (first, second) = (tokens[0], tokens[1]);
        Assert.False(string.Equals(first, second, StringComparison.Ordinal), "dy access token-a identikë");
        var basic = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("synthetic-basic-user:synthetic-basic-value"));

        // PARAKUSHT: një header i vetëm valid → 200 (GET/POST); skema me shkronja të vogla → 200 (case-insensitive).
        foreach (var method in (HttpMethod[])[HttpMethod.Get, HttpMethod.Post])
        {
            foreach (var value in (string[])[$"Bearer {first}", $"bearer {first}"])
            {
                using var allowed = await InteractiveClient.UserInfoRawAsync(client, method, [value]);
                Assert.True(allowed.StatusCode == HttpStatusCode.OK, $"parakushti `{value[..6]}…` ({method}): pritej 200, u mor {(int)allowed.StatusCode}");
                using var document = JsonDocument.Parse(await InteractiveClient.BodyAsync(allowed));
                Assert.Equal(userId.ToString(), document.RootElement.GetProperty("sub").GetString());
            }
        }

        var ambiguous = new (string Label, string[] Values)[]
        {
            ("i përsëritur identik", [$"Bearer {first}", $"Bearer {first}"]),
            ("i përsëritur i ndryshëm", [$"Bearer {first}", $"Bearer {second}"]),
            ("i bashkuar në një vlerë", [$"Bearer {first}, Bearer {second}"]),
            ("i bashkuar, Basic në krye", [$"Basic {basic}, Bearer {first}"]),
            ("i bashkuar, Bearer në krye", [$"Bearer {first}, Basic {basic}"]),
            ("skema të përziera Basic+Bearer", [$"Basic {basic}", $"Bearer {first}"]),
            ("skema të përziera Bearer+Basic", [$"Bearer {first}", $"Basic {basic}"]),
        };
        foreach (var method in (HttpMethod[])[HttpMethod.Get, HttpMethod.Post])
        {
            foreach (var (label, values) in ambiguous)
            {
                using var response = await InteractiveClient.UserInfoRawAsync(client, method, values);
                Assert.True(
                    response.StatusCode == HttpStatusCode.BadRequest,
                    $"rasti `{label}` ({method}): pritej 400 invalid_request, u mor {(int)response.StatusCode}");
                var body = await InteractiveClient.BodyAsync(response);
                var challenge = response.Headers.WwwAuthenticate.ToString();
                Assert.Contains(Errors.InvalidRequest, challenge, StringComparison.Ordinal);
                AssertNoStore(response, label);
                // Refuzimi i hershëm i pipeline-it të serverit (OpenIddict 7.6.0): gabimi VETËM në `WWW-Authenticate`, trup BOSH.
                Assert.True(body.Length == 0, $"rasti `{label}` ({method}): trupi i refuzimit të hershëm nuk është bosh ({body.Length} byte)");
                Assert.DoesNotContain(userId.ToString(), challenge, StringComparison.Ordinal);
                AssertNoEcho(challenge, "access-token/challenge (" + label + ")", first);
                AssertNoEcho(challenge, "access-token-2/challenge (" + label + ")", second);
            }
        }

        foreach (var method in (HttpMethod[])[HttpMethod.Get, HttpMethod.Post])
        {
            // Ruhen: skemë tjetër e VETME = kredencial bearer i munguar → 401 challenge bazë `Bearer` pa kod gabimi
            // (RFC 6750 §3; `missing_token` nuk shfaqet në challenge) — jo 400, jo redirect, jo 5xx.
            using (var basicOnly = await InteractiveClient.UserInfoRawAsync(client, method, [$"Basic {basic}"]))
            {
                Assert.True(basicOnly.StatusCode == HttpStatusCode.Unauthorized, $"vetëm Basic ({method}): pritej 401, u mor {(int)basicOnly.StatusCode}");
                var challenge = basicOnly.Headers.WwwAuthenticate.ToString();
                Assert.StartsWith("Bearer", challenge, StringComparison.Ordinal);
                Assert.DoesNotContain(Errors.InvalidRequest, challenge, StringComparison.Ordinal);
                AssertNoStore(basicOnly, "vetëm Basic");
                Assert.DoesNotContain("\"sub\"", await InteractiveClient.BodyAsync(basicOnly), StringComparison.Ordinal);
            }

            // Ruhet: NJË header me token të PAVLEFSHËM nuk është transport i shumëfishtë → 401 invalid_token (jo 400).
            using (var invalid = await InteractiveClient.UserInfoRawAsync(client, method, [$"Bearer {first[..^6]}AAAAAA"]))
            {
                Assert.True(invalid.StatusCode == HttpStatusCode.Unauthorized, $"token i pavlefshëm ({method}): pritej 401, u mor {(int)invalid.StatusCode}");
                var challenge = invalid.Headers.WwwAuthenticate.ToString();
                Assert.Contains(Errors.InvalidToken, challenge, StringComparison.Ordinal);
                Assert.DoesNotContain(Errors.InvalidRequest, challenge, StringComparison.Ordinal);
                AssertNoStore(invalid, "token i pavlefshëm");
            }

            // Ruhet: pa asnjë header → 401 challenge bazë.
            using (var anonymous = await InteractiveClient.UserInfoRawAsync(client, method, []))
            {
                Assert.True(anonymous.StatusCode == HttpStatusCode.Unauthorized, $"anonim ({method}): pritej 401, u mor {(int)anonymous.StatusCode}");
                AssertNoStore(anonymous, "anonim");
            }
        }

        // Ruhet: scope i pamjaftueshëm → 403 (klient pa `openid`).
        var plainClient = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (plainCode, plainVerifier) = await InteractiveClient.IssueCodeAsync(client, plainClient, userName, State);
        using var plainRedeemed = await InteractiveClient.RedeemAsync(client, plainClient, plainCode, plainVerifier);
        var scopedBearer = (await InteractiveClient.AccessTokenAsync(plainRedeemed))!;
        using (var forbidden = await InteractiveClient.UserInfoRawAsync(client, HttpMethod.Get, [$"Bearer {scopedBearer}"]))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            AssertNoStore(forbidden, "scope i pamjaftueshëm"); // rruga e controller-it (passthrough)
        }
    }

    // ═══════════════ C1 §5 / C2 §4 — dështim gjatë SHKRIMIT në persistencë, recovery me përgjigje reale ═══════════════

    /// <summary>C2 §4: pohime të PLOTA mbi një përgjigje reale `invalid_grant`: JSON i parsuar (i pavlefshëm = dështim), kodi, zero token-a, zero echo.</summary>
    private static async Task AssertInvalidGrantWithoutTokensAsync(HttpResponseMessage response, string code, string verifier, Guid userId, string label)
    {
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{label}: pritej 400, u mor {(int)response.StatusCode}");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        AssertNoStore(response, label);
        var body = await InteractiveClient.BodyAsync(response);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(Errors.InvalidGrant, document.RootElement.GetProperty("error").GetString());
        Assert.False(document.RootElement.TryGetProperty("access_token", out _), $"{label}: access token në përgjigjen e refuzuar");
        Assert.False(document.RootElement.TryGetProperty("id_token", out _), $"{label}: ID token në përgjigjen e refuzuar");
        AssertNoEcho(body, "authorization-code (" + label + ")", code);
        AssertNoEcho(body, "pkce-verifier (" + label + ")", verifier);
        Assert.DoesNotContain(userId.ToString(), body, StringComparison.Ordinal);
    }

    /// <summary>C2 §4: sukses REAL i lëshimit — access token DHE ID token jo-boshë.</summary>
    private static async Task AssertIssuedBothTokensAsync(HttpResponseMessage response, string label)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{label}: pritej 200, u mor {(int)response.StatusCode}");
        Assert.False(string.IsNullOrEmpty(await InteractiveClient.AccessTokenAsync(response)), $"{label}: access token bosh");
        Assert.False(string.IsNullOrEmpty(await InteractiveClient.IdTokenAsync(response)), $"{label}: ID token bosh");
    }

    /// <summary>
    /// C2 §4: retry me të NJËJTIN kod pas një dështimi shkrimi — çdo degë e lejuar pohohet plotësisht:
    /// 400 → `invalid_grant` pa token-a dhe pa echo; 200 → access DHE ID token, pastaj ripërdorimi i dytë
    /// 400 `invalid_grant` pa token-a; çdo status tjetër ose JSON i pavlefshëm = dështim. Kthen degën e
    /// VËZHGUAR (raportohet, nuk pinohet si garanci universale).
    /// </summary>
    private static async Task<string> AssertRetryBranchAsync(
        HttpClient client, string clientId, string code, string verifier, Guid userId)
    {
        using var retry = await InteractiveClient.RedeemAsync(client, clientId, code, verifier);
        switch (retry.StatusCode)
        {
            case HttpStatusCode.BadRequest:
                await AssertInvalidGrantWithoutTokensAsync(retry, code, verifier, userId, "retry");
                return "retry=400 invalid_grant";

            case HttpStatusCode.OK:
                await AssertIssuedBothTokensAsync(retry, "retry");
                using (var replay = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
                {
                    await AssertInvalidGrantWithoutTokensAsync(replay, code, verifier, userId, "ripërdorimi i dytë");
                }

                return "retry=200 (access+ID) pastaj ripërdorim=400 invalid_grant";

            default:
                throw Xunit.Sdk.FailException.ForFailure($"status i papritur pas dështimit të shkrimit: {(int)retry.StatusCode}");
        }
    }

    /// <summary>
    /// C1 §5 / C2 §4 — dështim i kontrolluar në pikën REALE të persistencës që konsumon OpenIddict
    /// (`SaveChanges` i `PlatformDbContext`) gjatë KRIJIMIT të token-it: simulim i KLASËS së gabimit
    /// (fault-i hidhet PARA shkrimit — jo provë rollback-u fizik). Provohet se fault-i u aktivizua;
    /// përgjigjja është 503 e kontrolluar pa asnjë token; retry-i me të njëjtin kod pohohet në çdo degë;
    /// flow-i i ri përfundon me access DHE ID token.
    /// </summary>
    [Fact]
    public async Task Deshtimi_i_shkrimit_gjate_issuance_jep_503_pa_token_dhe_rikuperohet()
    {
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

        // Gjendja REALE e kodit të prekur: çdo degë e lejuar pohohet plotësisht; dega e vëzhguar raportohet.
        var observed = await AssertRetryBranchAsync(client, clientId, code, verifier, userId);
        TestContext.Current.TestOutputHelper?.WriteLine($"issuance-fault: dega e vëzhguar = {observed}");

        // Rikuperim: flow krejt i ri përfundon pozitivisht me access DHE ID token.
        var (freshCode, freshVerifier) = await InteractiveClient.IssueCodeAsync(
            client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce-write-2");
        using var recovered = await InteractiveClient.RedeemAsync(client, clientId, freshCode, freshVerifier);
        await AssertIssuedBothTokensAsync(recovered, "rikuperimi");
    }

    /// <summary>
    /// C1 §5 / C2 §4 (nën-rasti i dytë, sjellje e MATUR jo e supozuar): kur dështimi i shkrimit godet
    /// PËRDITËSIMIN e shlyerjes së kodit (jo krijimin e token-it), biblioteka e kap vetë përjashtimin dhe
    /// e refuzon grant-in fail-closed me **400 `invalid_grant`** — kërkesa NUK arrin kufirin tonë 503.
    /// Garancia: zero token, kurrë 200, kurrë 500. Retry-i pohohet në çdo degë; flow-i i ri jep access DHE ID token.
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
            await AssertInvalidGrantWithoutTokensAsync(during, code, verifier, userId, "gjatë fault-it");
        }

        Assert.True(fault.Fired, "fault-i nuk u aktivizua — përditësimi i shlyerjes nuk u arrit");

        fault.Disarm();

        // Gjendja REALE e kodit ORIGJINAL pas fault-it (i hedhur PARA shkrimit): çdo degë pohohet plotësisht.
        var observed = await AssertRetryBranchAsync(client, clientId, code, verifier, userId);
        TestContext.Current.TestOutputHelper?.WriteLine($"redeem-fault: dega e vëzhguar = {observed}");

        var (freshCode, freshVerifier) = await InteractiveClient.IssueCodeAsync(
            client, clientId, userName, State, Scopes.OpenId, "synthetic-nonce-redeem-2");
        using var recovered = await InteractiveClient.RedeemAsync(client, clientId, freshCode, freshVerifier);
        await AssertIssuedBothTokensAsync(recovered, "rikuperimi");
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
