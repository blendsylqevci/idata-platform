using System.Net;
using System.Text.Json;
using Idata.Platform.Modules.Identity.Application;
using Idata.Platform.Modules.Identity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// F1 Increment B.2b — kontrata REALE OAuth 2.0 Authorization Code + PKCE mbi
/// pipeline-in e plotë `HTTP → middleware → Identity/OpenIddict → PostgreSQL
/// sintetik`: login me ASP.NET Core Identity, session cookie e ndarë nga bearer,
/// consent eksplicit, lëshim dhe shkëmbim kodi, JOSE server-stack, dhe kufijtë
/// fail-closed (antiforgery, rate limit, lockout, open redirect, replay).
/// Zero helper/seam: çdo pohim kalon nga middleware-i real.
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class IdentityInteractiveAuthorizationTests
{
    private const string SyntheticState = "synthetic state+/=&";

    private static IdentityAuthorityHostSettings Probe(IdentityInteractiveLab lab)
        => lab.Settings with { EnableProtectedProbe = true };

    // ═══════════════ 1 · 5 · 7 · 8 · 12 · 13 · 14 — fluksi i plotë ═══════════════

    [Fact]
    public async Task Fluksi_i_plote_login_consent_kod_dhe_token_kalon_pipeline_in_real()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2bflow");
        await using var factory = lab.Host(Probe(lab));
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (userName, userId) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (verifier, challenge) = InteractiveClient.CreatePkcePair();
        var authorize = InteractiveClient.AuthorizationUrl(
            clientId, challenge, SyntheticState, IdentityInteractiveLab.SyntheticScope);

        // (1) Anonim → ridrejtim te login-i, kurrë kod.
        using (var anonymous = await client.GetAsync(new Uri(authorize, UriKind.Relative), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
            var location = anonymous.Headers.Location!.ToString();
            Assert.Contains("/" + InteractiveAuthorizationContract.LoginPath, location, StringComparison.Ordinal);
            Assert.Contains(Uri.EscapeDataString("/" + InteractiveAuthorizationContract.AuthorizationPath), location, StringComparison.Ordinal);
            Assert.DoesNotContain("code=", location, StringComparison.Ordinal);
        }

        // (5) Login valid → VETËM session cookie interaktive dhe kthim te kërkesa e ruajtur.
        using (var login = await InteractiveClient.SignInAsync(
            client, userName, IdentityInteractiveLab.SyntheticCredential, authorize))
        {
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            Assert.Equal(authorize, login.Headers.Location!.ToString());
            var cookies = string.Join(" ", login.Headers.GetValues("Set-Cookie"));
            Assert.Contains(InteractiveAuthorizationContract.CookieName, cookies, StringComparison.Ordinal);
            Assert.Contains("httponly", cookies, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", cookies, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("access_token", cookies, StringComparison.OrdinalIgnoreCase);
        }

        // (7) Consent eksplicit → kod autorizimi me `state` byte-identik.
        var (consent, fields) = await InteractiveClient.ConsentPageAsync(client, authorize);
        using (consent)
        {
            Assert.Equal(HttpStatusCode.OK, consent.StatusCode);
            var page = await InteractiveClient.BodyAsync(consent);
            Assert.Contains("Synthetic interactive client", page, StringComparison.Ordinal);
            Assert.Contains(IdentityInteractiveLab.SyntheticScope, page, StringComparison.Ordinal);
            Assert.Contains(InteractiveAuthorizationContract.AntiforgeryFieldName, fields.Keys);
        }

        string code;
        using (var granted = await InteractiveClient.DecideAsync(
            client, fields, InteractiveAuthorizationContract.ApproveValue))
        {
            Assert.Equal(HttpStatusCode.Redirect, granted.StatusCode);
            Assert.StartsWith(IdentityInteractiveLab.SyntheticRedirectUri, granted.Headers.Location!.ToString(), StringComparison.Ordinal);
            code = InteractiveClient.RedirectParameter(granted, "code")!;
            Assert.False(string.IsNullOrEmpty(code));
            Assert.Equal(SyntheticState, InteractiveClient.RedirectParameter(granted, "state"));
            Assert.Null(InteractiveClient.RedirectParameter(granted, "error"));
        }

        // (8) Shkëmbimi me `code_verifier` korrekt → access token REAL.
        string accessToken;
        using (var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
        {
            Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
            accessToken = (await InteractiveClient.AccessTokenAsync(redeemed))!;
            Assert.False(string.IsNullOrEmpty(accessToken));
            using var document = JsonDocument.Parse(await InteractiveClient.BodyAsync(redeemed));
            Assert.False(document.RootElement.TryGetProperty("id_token", out _));
            Assert.False(document.RootElement.TryGetProperty("refresh_token", out _));
        }

        // (13) Token-i real kalon te `[Authorize]` me subjektin e sesionit interaktiv.
        using (var authorized = await InteractiveClient.ProtectedAsync(client, accessToken))
        {
            Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
            Assert.Contains(userId.ToString(), await InteractiveClient.BodyAsync(authorized), StringComparison.Ordinal);
        }

        // (14) Pa token / me token të manipuluar → 401.
        using (var anonymous = await InteractiveClient.ProtectedAsync(client, null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        using (var tampered = await InteractiveClient.ProtectedAsync(client, accessToken + "x"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
        }
    }

    // ═══════════════ 6 — refuzimi i shprehur i consent-it ═══════════════

    [Fact]
    public async Task Consent_i_refuzuar_kthen_access_denied_dhe_zero_kod()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2bdeny");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (_, challenge) = InteractiveClient.CreatePkcePair();
        var authorize = InteractiveClient.AuthorizationUrl(clientId, challenge, SyntheticState);

        using (var login = await InteractiveClient.SignInAsync(
            client, userName, IdentityInteractiveLab.SyntheticCredential, authorize))
        {
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        }

        var (consent, fields) = await InteractiveClient.ConsentPageAsync(client, authorize);
        consent.Dispose();

        using var denied = await InteractiveClient.DecideAsync(
            client, fields, InteractiveAuthorizationContract.DenyValue);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        var location = denied.Headers.Location!.ToString();
        Assert.StartsWith(IdentityInteractiveLab.SyntheticRedirectUri, location, StringComparison.Ordinal);
        Assert.Equal(Errors.AccessDenied, InteractiveClient.RedirectParameter(denied, "error"));
        Assert.Equal(SyntheticState, InteractiveClient.RedirectParameter(denied, "state"));
        Assert.DoesNotContain("code=", location, StringComparison.Ordinal);
    }

    // ═══════════════ 9 · 10 · 11 — PKCE, replay dhe lidhja e kodit ═══════════════

    [Fact]
    public async Task Kodi_lidhet_me_verifier_klient_dhe_redirect_dhe_eshte_nje_perdorimsh()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2bpkce");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var otherClientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);

        async Task<(string Code, string Verifier)> IssueCodeAsync()
        {
            var (verifier, challenge) = InteractiveClient.CreatePkcePair();
            var authorize = InteractiveClient.AuthorizationUrl(clientId, challenge, SyntheticState);
            using (var login = await InteractiveClient.SignInAsync(
                client, userName, IdentityInteractiveLab.SyntheticCredential, authorize))
            {
                Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            }

            var (page, fields) = await InteractiveClient.ConsentPageAsync(client, authorize);
            page.Dispose();
            using var granted = await InteractiveClient.DecideAsync(
                client, fields, InteractiveAuthorizationContract.ApproveValue);
            return (InteractiveClient.RedirectParameter(granted, "code")!, verifier);
        }

        // (9a) Verifier që mungon → refuzim, zero token.
        var missing = await IssueCodeAsync();
        using (var response = await InteractiveClient.RedeemAsync(client, clientId, missing.Code, verifier: null))
        {
            await AssertOauthRejectionAsync(response);
        }

        // (9b) Verifier i gabuar → refuzim.
        var wrong = await IssueCodeAsync();
        using (var response = await InteractiveClient.RedeemAsync(
            client, clientId, wrong.Code, InteractiveClient.CreatePkcePair().Verifier))
        {
            await AssertOauthRejectionAsync(response);
        }

        // (11a) Klient tjetër → refuzim.
        var crossClient = await IssueCodeAsync();
        using (var response = await InteractiveClient.RedeemAsync(
            client, otherClientId, crossClient.Code, crossClient.Verifier))
        {
            await AssertOauthRejectionAsync(response);
        }

        // (11b) Redirect URI tjetër → refuzim.
        var crossRedirect = await IssueCodeAsync();
        using (var response = await InteractiveClient.RedeemAsync(
            client, clientId, crossRedirect.Code, crossRedirect.Verifier,
            redirectUri: "https://synthetic-client.localhost/other"))
        {
            await AssertOauthRejectionAsync(response);
        }

        // (9c) PKCE-ja imponohet nga SERVERI edhe kur klienti nuk e kërkon vetë:
        // kërkesa pa `code_challenge` refuzohet para çdo kodi (provë diskriminuese e
        // `RequireProofKeyForCodeExchange`, jo e kërkesës per-klient).
        var clientWithoutPkce = await SyntheticPrincipals.RegisterPublicCodeClientAsync(
            factory, requirePkceAtClient: false);
        using (var response = await client.GetAsync(
            new Uri(InteractiveClient.AuthorizationUrl(clientWithoutPkce, challenge: null, state: SyntheticState), UriKind.Relative),
            TestContext.Current.CancellationToken))
        {
            Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            var location = response.Headers.Location?.ToString() ?? string.Empty;
            var body = await InteractiveClient.BodyAsync(response);
            Assert.DoesNotContain("code=", location, StringComparison.Ordinal);
            // Kërkesa nuk arrin as te login-i: refuzohet në kufirin e protokollit.
            Assert.DoesNotContain("/" + InteractiveAuthorizationContract.LoginPath, location, StringComparison.Ordinal);
            Assert.True(
                location.Contains("error=", StringComparison.Ordinal)
                || body.Contains("error", StringComparison.OrdinalIgnoreCase),
                $"status={response.StatusCode} location={location}");
        }

        // (10) Replay: përdorimi i parë kalon, i dyti refuzohet.
        var replay = await IssueCodeAsync();
        using (var first = await InteractiveClient.RedeemAsync(client, clientId, replay.Code, replay.Verifier))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using (var second = await InteractiveClient.RedeemAsync(client, clientId, replay.Code, replay.Verifier))
        {
            await AssertOauthRejectionAsync(second);
        }
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("S512")]
    [InlineData("")]
    public async Task Metoda_e_challenge_it_jashte_S256_refuzohet_pa_kod(string method)
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2bchal");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (verifier, _) = InteractiveClient.CreatePkcePair();

        var authorize = InteractiveClient.AuthorizationUrl(
            clientId, challenge: verifier, state: SyntheticState, challengeMethod: method);
        using var response = await client.GetAsync(new Uri(authorize, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? string.Empty;
        Assert.DoesNotContain("code=", location, StringComparison.Ordinal);
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            Assert.StartsWith(IdentityInteractiveLab.SyntheticRedirectUri, location, StringComparison.Ordinal);
            Assert.Contains("error=", location, StringComparison.Ordinal);
        }
    }

    // ═══════════════ 2 · 3 · 4 — login fail-closed pa enumeration ═══════════════

    [Fact]
    public async Task Deshtimet_e_login_it_jane_uniforme_dhe_aktivizojne_lockout()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2block");
        await using var factory = lab.Host(lab.Settings with { MaxFailedAccessAttempts = 2, LoginPermitLimit = 100 });
        using var client = InteractiveClient.CreateCookieClient(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (twoFactorUser, _) = await SyntheticPrincipals.CreateUserAsync(factory, twoFactor: true);

        // (2) Kredencial i gabuar → zero cookie sesioni.
        using (var wrong = await InteractiveClient.SignInAsync(client, userName, "Synthetic-wrong-9"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.DoesNotContain(
                InteractiveAuthorizationContract.CookieName,
                string.Join(" ", wrong.Headers.TryGetValues("Set-Cookie", out var values) ? values : []),
                StringComparison.Ordinal);
        }

        var unknownBody = await BodyOfAsync(client, "synthetic-unknown-user", "Synthetic-wrong-9");
        var invalidBody = await BodyOfAsync(client, userName, "Synthetic-wrong-9");

        // (3) Pragu i lockout-it u kalua → llogaria bllokohet realisht.
        Assert.True(await SyntheticPrincipals.IsLockedOutAsync(factory, userName));
        var lockedBody = await BodyOfAsync(client, userName, IdentityInteractiveLab.SyntheticCredential);

        // (4) Llogaria me faktor të dytë nuk merr kurrë sesion.
        var twoFactorBody = await BodyOfAsync(client, twoFactorUser, IdentityInteractiveLab.SyntheticCredential);

        // Zero enumeration: katër klasat janë të padallueshme nga jashtë.
        Assert.Equal(unknownBody, invalidBody);
        Assert.Equal(unknownBody, lockedBody);
        Assert.Equal(unknownBody, twoFactorBody);

        // Sesioni mbetet i palëshuar → kërkesa e autorizimit kthehet te login-i.
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (_, challenge) = InteractiveClient.CreatePkcePair();
        using var authorize = await client.GetAsync(
            new Uri(InteractiveClient.AuthorizationUrl(clientId, challenge, SyntheticState), UriKind.Relative),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.Contains(
            "/" + InteractiveAuthorizationContract.LoginPath,
            authorize.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rate_limit_i_login_it_refuzon_fail_closed_pas_pragut()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2brate");
        await using var factory = lab.Host(lab.Settings with { LoginPermitLimit = 2, LoginWindowSeconds = 600 });
        using var client = InteractiveClient.CreateCookieClient(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var allowed = await InteractiveClient.SignInAsync(client, userName, "Synthetic-wrong-9");
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var throttled = await InteractiveClient.SignInAsync(
            client, userName, IdentityInteractiveLab.SyntheticCredential);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.DoesNotContain(
            InteractiveAuthorizationContract.CookieName,
            string.Join(" ", throttled.Headers.TryGetValues("Set-Cookie", out var values) ? values : []),
            StringComparison.Ordinal);
    }

    // ═══════════════ 15 — ndarja cookie ↔ bearer ═══════════════

    [Fact]
    public async Task Cookie_ja_interaktive_nuk_autentikon_asnje_API_bearer()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2bscheme");
        await using var factory = lab.Host(Probe(lab));
        using var client = InteractiveClient.CreateCookieClient(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);

        using (var login = await InteractiveClient.SignInAsync(
            client, userName, IdentityInteractiveLab.SyntheticCredential))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        // Skema DEFAULT (authenticate + challenge) mbetet validimi OpenIddict — jo cookie-ja.
        var authentication = factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Authentication.AuthenticationOptions>>()
            .Value;
        Assert.Equal(OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, authentication.DefaultAuthenticateScheme);
        Assert.Equal(OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, authentication.DefaultChallengeScheme);

        // Cookie-ja dërgohet nga container-i; skema default mbetet validimi OpenIddict.
        using var probe = await InteractiveClient.ProtectedAsync(client, accessToken: null);
        Assert.Equal(HttpStatusCode.Unauthorized, probe.StatusCode);

        using var accessContext = await client.GetAsync(
            new Uri("/api/platform/access-context", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, accessContext.StatusCode);
    }

    // ═══════════════ 17 — antiforgery ═══════════════

    [Fact]
    public async Task POST_pa_antiforgery_refuzohet_ne_login_dhe_ne_consent()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2bxsrf");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);

        using (var withoutToken = await client.PostAsync(
            new Uri("/" + InteractiveAuthorizationContract.LoginPath, UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [InteractiveAuthorizationContract.UsernameField] = userName,
                [InteractiveAuthorizationContract.CredentialField] = IdentityInteractiveLab.SyntheticCredential,
            }),
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
            Assert.DoesNotContain(
                InteractiveAuthorizationContract.CookieName,
                string.Join(" ", withoutToken.Headers.TryGetValues("Set-Cookie", out var values) ? values : []),
                StringComparison.Ordinal);
        }

        var (_, challenge) = InteractiveClient.CreatePkcePair();
        var authorize = InteractiveClient.AuthorizationUrl(clientId, challenge, SyntheticState);
        using (var login = await InteractiveClient.SignInAsync(
            client, userName, IdentityInteractiveLab.SyntheticCredential, authorize))
        {
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        }

        var (page, fields) = await InteractiveClient.ConsentPageAsync(client, authorize);
        page.Dispose();
        var withoutAntiforgery = fields
            .Where(field => !string.Equals(field.Key, InteractiveAuthorizationContract.AntiforgeryFieldName, StringComparison.Ordinal))
            .ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);

        using var forged = await InteractiveClient.DecideAsync(
            client, withoutAntiforgery, InteractiveAuthorizationContract.ApproveValue);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.DoesNotContain("code=", forged.Headers.Location?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    // ═══════════════ 18 — zero open redirect ═══════════════

    [Theory]
    [InlineData("https://synthetic-attacker.localhost/steal")]
    [InlineData("//synthetic-attacker.localhost/steal")]
    [InlineData("/api/platform/status")]
    [InlineData("/connect/authorize/../../etc")]
    public async Task Return_url_jashte_endpointit_te_autorizimit_nuk_ndiqet_kurre(string returnUrl)
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2bredir");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);

        using var login = await InteractiveClient.SignInAsync(
            client, userName, IdentityInteractiveLab.SyntheticCredential, returnUrl);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Null(login.Headers.Location);
        Assert.DoesNotContain(
            "synthetic-attacker", await InteractiveClient.BodyAsync(login), StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════ 19 — JOSE mbi stack-un e SERVER-it ═══════════════

    [Fact]
    public async Task Artefakti_i_mbrojtur_me_JOSE_jashte_allowlist_it_refuzohet_para_dekriptimit()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2bjose");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);

        async Task<(string Code, string Verifier)> IssueCodeAsync()
        {
            var (verifier, challenge) = InteractiveClient.CreatePkcePair();
            var authorize = InteractiveClient.AuthorizationUrl(clientId, challenge, SyntheticState);
            using (var login = await InteractiveClient.SignInAsync(
                client, userName, IdentityInteractiveLab.SyntheticCredential, authorize))
            {
                Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            }

            var (page, fields) = await InteractiveClient.ConsentPageAsync(client, authorize);
            page.Dispose();
            using var granted = await InteractiveClient.DecideAsync(
                client, fields, InteractiveAuthorizationContract.ApproveValue);
            return (InteractiveClient.RedirectParameter(granted, "code")!, verifier);
        }

        // Artefakti që `/connect/token` konsumon realisht është PAYLOAD-i i ruajtur i
        // kodit reference (JWE me JWS të brendshëm) — jo identifikuesi opak i klientit.
        var control = await IssueCodeAsync();
        var payload = await SyntheticPrincipals.ReadCodePayloadAsync(factory, control.Code);
        Assert.Equal(5, payload.Split('.').Length);

        var inner = SyntheticTokenCrafter.ExtractInnerJws(lab.Signing, lab.Encryption, payload);
        var header = SyntheticTokenCrafter.OuterHeader(payload);

        // Kontroll: i njëjti inner JWS, i ri-enkriptuar me algoritmet E LEJUARA → pranohet.
        await SyntheticPrincipals.ReplaceCodePayloadAsync(
            factory, control.Code,
            SyntheticTokenCrafter.ReEncrypt(inner, lab.Encryption, "RSA-OAEP", "A256CBC-HS512", header));
        using (var accepted = await InteractiveClient.RedeemAsync(client, clientId, control.Code, control.Verifier))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        // Variantet jashtë allowlist-it: key-management `RSA1_5` (IdentityModel NUK e
        // validon kurrë vetë `alg`-un e jashtëm), `enc` i dobët, header pa `enc` dhe
        // header i keqformuar — secili mbi një kod TË RI, të vlefshëm.
        foreach (var tamper in (Func<string, string>[])
        [
            source => SyntheticTokenCrafter.ReEncrypt(
                SyntheticTokenCrafter.ExtractInnerJws(lab.Signing, lab.Encryption, source),
                lab.Encryption, "RSA1_5", "A256CBC-HS512", SyntheticTokenCrafter.OuterHeader(source)),
            source => SyntheticTokenCrafter.ReEncrypt(
                SyntheticTokenCrafter.ExtractInnerJws(lab.Signing, lab.Encryption, source),
                lab.Encryption, "RSA-OAEP", "A128CBC-HS256", SyntheticTokenCrafter.OuterHeader(source)),
            source => SyntheticTokenCrafter.WithOuterHeader(source, "{\"alg\":\"RSA-OAEP\"}"),
            source => SyntheticTokenCrafter.WithOuterHeader(source, "synthetic-not-json"),
        ])
        {
            var issued = await IssueCodeAsync();
            var original = await SyntheticPrincipals.ReadCodePayloadAsync(factory, issued.Code);
            await SyntheticPrincipals.ReplaceCodePayloadAsync(factory, issued.Code, tamper(original));

            using var rejected = await InteractiveClient.RedeemAsync(client, clientId, issued.Code, issued.Verifier);
            await AssertOauthRejectionAsync(rejected);
        }
    }

    [Fact]
    public async Task Faqja_e_consent_it_enkodon_cdo_vlere_te_pabesuar()
    {
        const string hostile = "<script>alert('synthetic')</script>";
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2bhtml");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory, displayName: hostile);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (_, challenge) = InteractiveClient.CreatePkcePair();
        var authorize = InteractiveClient.AuthorizationUrl(clientId, challenge, "state\"><b>x</b>");

        using (var login = await InteractiveClient.SignInAsync(
            client, userName, IdentityInteractiveLab.SyntheticCredential, authorize))
        {
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        }

        var (consent, fields) = await InteractiveClient.ConsentPageAsync(client, authorize);
        using (consent)
        {
            Assert.Equal(HttpStatusCode.OK, consent.StatusCode);
            var page = await InteractiveClient.BodyAsync(consent);
            Assert.DoesNotContain("<script>", page, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<b>x</b>", page, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;", page, StringComparison.Ordinal);
        }

        // Parametrat e pabesuar përcillen TË PANDRYSHUAR si vlera (jo si markup).
        Assert.Equal("state\"><b>x</b>", fields["state"]);
    }

    // ═══════════════ 16 · 20 · 21 · 22 — inventari, `openid` dhe kërkesat e keqformuara ═══════════════

    /// <summary>
    /// B.2c — ZËVENDËSIM I KONTROLLUAR (jo heqje) i testit të B.2b “Openid_refuzohet…”:
    /// refuzimi i qëllimshëm i `openid` u zëvendësua me profilin OIDC (prova pozitive te
    /// `IdentityOidcSignInTests`). Këtu mbetet inventari i SINQERTË: OAuth-only pa `openid`
    /// i pandryshuar (zero ID token); metadata shpall VETËM aftësitë e implementuara
    /// (openid, ID token, UserInfo) dhe asnjë grant/endpoint tjetër.
    /// </summary>
    [Fact]
    public async Task Inventari_publik_premton_vetem_aftesite_e_implementuara_dhe_OAuth_only_mbetet_pa_ID_token()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2boidc");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (_, challenge) = InteractiveClient.CreatePkcePair();

        using (var login = await InteractiveClient.SignInAsync(
            client, userName, IdentityInteractiveLab.SyntheticCredential))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        // (21) OAuth-only (pa `openid`) mbetet i pandryshuar: flow-i i plotë deri te redirect-i
        // real jep KOD, asnjë `id_token` dhe asnjë `access_token` në fragment (response_type=code).
        var authorizeUrl = InteractiveClient.AuthorizationUrl(clientId, challenge, SyntheticState);
        var (consentPage, consentFields) = await InteractiveClient.ConsentPageAsync(client, authorizeUrl);
        Assert.Equal(HttpStatusCode.OK, consentPage.StatusCode);
        consentPage.Dispose();
        using (var granted = await InteractiveClient.DecideAsync(
            client, consentFields, InteractiveAuthorizationContract.ApproveValue))
        {
            Assert.Equal(HttpStatusCode.Redirect, granted.StatusCode);
            var location = granted.Headers.Location?.ToString() ?? string.Empty;
            Assert.Contains("code=", location, StringComparison.Ordinal);
            Assert.DoesNotContain("id_token", location, StringComparison.Ordinal);
            Assert.DoesNotContain("access_token", location, StringComparison.Ordinal);
            Assert.Equal(SyntheticState, InteractiveClient.RedirectParameter(granted, "state"));
        }

        // (22) Metadata publike premton VETËM aftësitë e implementuara (B.2c: openid, ID token,
        // UserInfo) — asnjë refresh/logout/introspection/revocation/device.
        var discovery = await client.GetStringAsync(
            new Uri("/.well-known/openid-configuration", UriKind.Relative), TestContext.Current.CancellationToken);
        using (var document = JsonDocument.Parse(discovery))
        {
            var root = document.RootElement;
            Assert.Equal(
                [GrantTypes.AuthorizationCode],
                root.GetProperty("grant_types_supported").EnumerateArray().Select(item => item.GetString()!).ToArray());
            Assert.Equal(
                [ResponseTypes.Code],
                root.GetProperty("response_types_supported").EnumerateArray().Select(item => item.GetString()!).ToArray());
            foreach (var absent in (string[])
                ["end_session_endpoint", "introspection_endpoint", "revocation_endpoint",
                 "device_authorization_endpoint", "pushed_authorization_request_endpoint"])
            {
                Assert.False(root.TryGetProperty(absent, out _), $"discovery premton `{absent}`");
            }

            Assert.EndsWith("/connect/userinfo", root.GetProperty("userinfo_endpoint").GetString(), StringComparison.Ordinal);
            Assert.NotEmpty(root.GetProperty("id_token_signing_alg_values_supported").EnumerateArray());
            Assert.Contains(Scopes.OpenId, root.GetProperty("scopes_supported").EnumerateArray().Select(item => item.GetString()!));
            Assert.DoesNotContain(Scopes.OfflineAccess, root.GetProperty("scopes_supported").EnumerateArray().Select(item => item.GetString()!));
        }

        // (16) Grant-et e ndaluara mbeten të refuzuara edhe pas aktivizimit të flow-it interaktiv.
        foreach (var grant in (string[])[GrantTypes.Password, GrantTypes.RefreshToken, GrantTypes.ClientCredentials])
        {
            using var response = await InteractiveClient.RedeemAsync(
                client, clientId, "synthetic-code", "synthetic-verifier", grantType: grant);
            await AssertOauthRejectionAsync(response);
        }
    }

    [Theory]
    [InlineData("/connect/authorize")]
    [InlineData("/connect/authorize?client_id=")]
    [InlineData("/connect/authorize?client_id=synthetic-missing&response_type=code")]
    [InlineData("/identity/login?returnUrl=%00")]
    public async Task Kerkesat_e_keqformuara_nuk_prodhojne_kurre_500(string path)
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2bmalf");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("code=", response.Headers.Location?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    // ═══════════════ Reliability Gate — revokim sesioni dhe DB e paarritshme ═══════════════

    [Fact]
    public async Task Rrotullimi_i_stamp_it_e_zhvlereson_sesionin_menjehere()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2bstamp");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (_, challenge) = InteractiveClient.CreatePkcePair();
        var authorize = InteractiveClient.AuthorizationUrl(clientId, challenge, SyntheticState);

        using (var login = await InteractiveClient.SignInAsync(
            client, userName, IdentityInteractiveLab.SyntheticCredential, authorize))
        {
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        }

        using (var before = await client.GetAsync(new Uri(authorize, UriKind.Relative), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        await SyntheticPrincipals.RotateSecurityStampAsync(factory, userName);

        using var after = await client.GetAsync(new Uri(authorize, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.Contains(
            "/" + InteractiveAuthorizationContract.LoginPath,
            after.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DB_e_paarritshme_nuk_leshon_kurre_sesion_ose_kod()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2bdown");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (_, challenge) = InteractiveClient.CreatePkcePair();
        var authorize = InteractiveClient.AuthorizationUrl(clientId, challenge, SyntheticState);

        using (var login = await InteractiveClient.SignInAsync(
            client, userName, IdentityInteractiveLab.SyntheticCredential, authorize))
        {
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        }

        await SyntheticOutage.SuspendAsync(lab.Database.DatabaseName);
        try
        {
            // (Reliability #2) Consent me DB të paarritshme → 503 i KONTROLLUAR (HTML gjenerik),
            // zero kod, zero detaj i brendshëm — kufiri i dështimit mbështjell edhe
            // validimin e klientit nga OpenIddict (jo exception i propaguar te thirrësi).
            using (var consent = await client.GetAsync(new Uri(authorize, UriKind.Relative), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, consent.StatusCode);
                var body = await InteractiveClient.BodyAsync(consent);
                Assert.Contains("Temporarily unavailable", body, StringComparison.Ordinal);
                Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(lab.Database.DatabaseName, body, StringComparison.Ordinal);
                Assert.Null(InteractiveClient.RedirectParameter(consent, "code"));
            }

            // (Reliability #1) Login me DB të paarritshme → 503 i kontrolluar, zero cookie.
            using var login503 = await InteractiveClient.SignInAsync(
                client, userName, IdentityInteractiveLab.SyntheticCredential);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, login503.StatusCode);
            Assert.DoesNotContain(
                InteractiveAuthorizationContract.CookieName,
                string.Join(" ", login503.Headers.TryGetValues("Set-Cookie", out var values) ? values : []),
                StringComparison.Ordinal);
        }
        finally
        {
            await SyntheticOutage.ResumeAsync(lab.Database.DatabaseName);
        }

        // Pas rikthimit të së njëjtës DB sesioni ekzistues vlen sërish (zero gjendje gjysmake).
        using var recovered = await client.GetAsync(new Uri(authorize, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    // ═══════════════ C1 — user-binding i antiforgery-t me sesionin interaktiv ═══════════════

    [Fact]
    public async Task Formulari_i_consent_it_lidhet_me_identitetin_e_sesionit_dhe_refuzohet_kur_sesioni_ndryshon()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2bbind");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (userA, _) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (userB, _) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (_, challenge) = InteractiveClient.CreatePkcePair();
        var authorize = InteractiveClient.AuthorizationUrl(clientId, challenge, SyntheticState);

        // Formulari i consent-it i marrë nga sesioni i A-së.
        using (var login = await InteractiveClient.SignInAsync(client, userA, IdentityInteractiveLab.SyntheticCredential, authorize))
        {
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        }

        var (page, formOfA) = await InteractiveClient.ConsentPageAsync(client, authorize);
        page.Dispose();

        // (a) Sesioni i A-së revokohet (stamp) → formulari i A-së nuk përputhet më me
        //     identitetin aktual (anonim): refuzim, zero kod, zero challenge-redirect me kod.
        await SyntheticPrincipals.RotateSecurityStampAsync(factory, userA);
        using (var stale = await InteractiveClient.DecideAsync(client, formOfA, InteractiveAuthorizationContract.ApproveValue))
        {
            Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
            Assert.Null(InteractiveClient.RedirectParameter(stale, "code"));
        }

        // (b) Sesion i RI i B-së në të njëjtin browser → formulari i A-së refuzohet
        //     (identitet tjetër), ndërsa formulari i vetë B-së kalon.
        using (var login = await InteractiveClient.SignInAsync(client, userB, IdentityInteractiveLab.SyntheticCredential, authorize))
        {
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        }

        using (var crossIdentity = await InteractiveClient.DecideAsync(client, formOfA, InteractiveAuthorizationContract.ApproveValue))
        {
            Assert.Equal(HttpStatusCode.BadRequest, crossIdentity.StatusCode);
            Assert.Null(InteractiveClient.RedirectParameter(crossIdentity, "code"));
        }

        var (pageB, formOfB) = await InteractiveClient.ConsentPageAsync(client, authorize);
        pageB.Dispose();
        using var granted = await InteractiveClient.DecideAsync(client, formOfB, InteractiveAuthorizationContract.ApproveValue);
        Assert.Equal(HttpStatusCode.Redirect, granted.StatusCode);
        Assert.False(string.IsNullOrEmpty(InteractiveClient.RedirectParameter(granted, "code")));
    }

    private static async Task<string> BodyOfAsync(HttpClient client, string userName, string credential)
    {
        using var response = await InteractiveClient.SignInAsync(client, userName, credential);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        return await InteractiveClient.BodyAsync(response);
    }

    /// <summary>Refuzim OAuth i kontrolluar: kurrë 200, kurrë 500, kurrë token.</summary>
    private static async Task AssertOauthRejectionAsync(HttpResponseMessage response)
    {
        var body = await InteractiveClient.BodyAsync(response);
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized,
            $"status={response.StatusCode} body={body}");
        Assert.DoesNotContain("access_token", body, StringComparison.Ordinal);
        Assert.Contains("error", body, StringComparison.OrdinalIgnoreCase);
    }
}
