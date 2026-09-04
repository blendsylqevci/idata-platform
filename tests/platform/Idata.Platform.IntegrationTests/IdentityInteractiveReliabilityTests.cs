using System.Net;
using System.Text.Json;
using Idata.Platform.Modules.Identity.Application;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// B.2b correction C1 — Reliability Gate mbi pipeline-in REAL dhe PostgreSQL
/// sintetik të izoluar: ndërprerje e PËRKOHSHME e së njëjtës DB (R3/R8), dorëzim i
/// dyfishtë REAL i consent-it (R4), restart REAL i host-it mid-flow (R6, me dhe pa
/// key ring të ndarë), dhe kërkesa KONKURRUESE me mbivendosje të matur (R7).
/// Asnjë seam: çdo pohim kalon nga middleware-i dhe storage-u real.
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class IdentityInteractiveReliabilityTests
{
    private const string State = "synthetic-reliability-state";

    // ═══════════════ R3 — DB e paarritshme GJATË shkëmbimit të kodit ═══════════════

    [Fact]
    public async Task R3_DB_e_paarritshme_gjate_shkembimit_te_kodit_kthen_503_pa_token_dhe_kodi_mbetet_i_perdorshem_pas_rikthimit()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2br3");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (code, verifier) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State);

        await SyntheticOutage.SuspendAsync(lab.Database.DatabaseName);
        try
        {
            // Faza e redemption-it ushtrohet REALISHT me DB të paarritshme: 503 i
            // kontrolluar (JSON gjenerik), zero token, zero detaj i brendshëm.
            using var during = await InteractiveClient.RedeemAsync(client, clientId, code, verifier);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, during.StatusCode);
            var body = await InteractiveClient.BodyAsync(during);
            Assert.Contains("temporarily_unavailable", body, StringComparison.Ordinal);
            Assert.DoesNotContain("access_token", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(lab.Database.DatabaseName, body, StringComparison.Ordinal);
            Assert.Equal("no-store", during.Headers.CacheControl?.ToString());
        }
        finally
        {
            await SyntheticOutage.ResumeAsync(lab.Database.DatabaseName);
        }

        // Gjendja e RUAJTUR e kodit pas rikthimit: kërkesa që dështoi nuk e konsumoi —
        // i njëjti kod shlyhet një herë të vetme, pastaj refuzohet.
        using (var after = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
        {
            Assert.Equal(HttpStatusCode.OK, after.StatusCode);
            Assert.False(string.IsNullOrEmpty(await InteractiveClient.AccessTokenAsync(after)));
        }

        using (var replay = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
        {
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
            Assert.DoesNotContain("access_token", await InteractiveClient.BodyAsync(replay), StringComparison.Ordinal);
        }
    }

    // ═══════════════ R4 — dorëzim i DYFISHTË i të njëjtit consent ═══════════════

    [Fact]
    public async Task R4_dorezimi_i_dyfishte_i_consent_it_prodhon_dy_autorizime_te_ndara_nje_perdorimshe()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2br4");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);
        var (verifier, challenge) = InteractiveClient.CreatePkcePair();
        var authorize = InteractiveClient.AuthorizationUrl(clientId, challenge, State);

        using (var login = await InteractiveClient.SignInAsync(
            client, userName, IdentityInteractiveLab.SyntheticCredential, authorize))
        {
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        }

        var (page, fields) = await InteractiveClient.ConsentPageAsync(client, authorize);
        page.Dispose();

        // I NJËJTI formular (të njëjtat fusha, i njëjti antiforgery token) dërgohet DY herë.
        string first;
        string second;
        using (var response = await InteractiveClient.DecideAsync(client, fields, InteractiveAuthorizationContract.ApproveValue))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            first = InteractiveClient.RedirectParameter(response, "code")!;
            Assert.Equal(State, InteractiveClient.RedirectParameter(response, "state"));
        }

        using (var response = await InteractiveClient.DecideAsync(client, fields, InteractiveAuthorizationContract.ApproveValue))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            second = InteractiveClient.RedirectParameter(response, "code")!;
            Assert.Equal(State, InteractiveClient.RedirectParameter(response, "state"));
        }

        // Semantika e deklaruar: çdo dorëzim rivalidohet si kërkesë autorizimi → dy kode
        // TË NDARA, secili me lidhjet e veta (i njëjti klient/redirect/challenge) dhe
        // përdorim NJËHERËSH — replay i secilit refuzohet.
        Assert.NotEqual(first, second);
        foreach (var code in (string[])[first, second])
        {
            using (var redeemed = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
            {
                Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
            }

            using (var replay = await InteractiveClient.RedeemAsync(client, clientId, code, verifier))
            {
                Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
            }
        }

        // Lidhja mbetet e saktë: verifier tjetër nuk e shlyen kodin e këtij challenge-i.
        var (third, _) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State);
        using var wrongVerifier = await InteractiveClient.RedeemAsync(
            client, clientId, third, InteractiveClient.CreatePkcePair().Verifier);
        Assert.Equal(HttpStatusCode.BadRequest, wrongVerifier.StatusCode);
    }

    // ═══════════════ R6 — RESTART REAL i host-it mid-flow ═══════════════

    [Fact]
    public async Task R6_restart_mid_flow_me_key_ring_te_ndare_ruan_sesionin_dhe_kodin_por_jo_gjendjen_e_konsumuar()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2br6a");
        var keyDirectory = Directory.CreateTempSubdirectory("idata-synthetic-keys-").FullName;
        try
        {
            var settings = lab.Settings with { DataProtectionKeyDirectory = keyDirectory };
            var cookies = new System.Net.CookieContainer();
            string clientId;
            string userName;
            string pendingAuthorize;
            Dictionary<string, string> pendingConsent;
            (string Code, string Verifier) unredeemed;
            (string Code, string Verifier) consumed;

            // Host A: sesion aktiv, një consent NË PROCES (formular i marrë, i padërguar),
            // një kod i lëshuar e i PASHLYER dhe një kod i KONSUMUAR — pastaj mbyllet.
            await using (var hostA = lab.Host(settings))
            {
                using var clientA = InteractiveClient.CreateCookieClient(hostA, cookies);
                clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(hostA);
                (userName, _) = await SyntheticPrincipals.CreateUserAsync(hostA);

                unredeemed = await InteractiveClient.IssueCodeAsync(clientA, clientId, userName, State);
                consumed = await InteractiveClient.IssueCodeAsync(clientA, clientId, userName, State);
                using (var redeemed = await InteractiveClient.RedeemAsync(clientA, clientId, consumed.Code, consumed.Verifier))
                {
                    Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
                }

                var (_, challenge) = InteractiveClient.CreatePkcePair();
                pendingAuthorize = InteractiveClient.AuthorizationUrl(clientId, challenge, State);
                var (page, fields) = await InteractiveClient.ConsentPageAsync(clientA, pendingAuthorize);
                using (page)
                {
                    Assert.Equal(HttpStatusCode.OK, page.StatusCode);
                }

                pendingConsent = fields;
            }

            // Host B (i ri, pas mbylljes së A): e njëjta DB, klient, përdorues, issuer,
            // credentials dhe key ring i ndarë — kushtet e PROVUARA të vazhdimësisë.
            await using var hostB = lab.Host(settings);
            using var clientB = InteractiveClient.CreateCookieClient(hostB, cookies);

            // (1) Sesioni interaktiv i lëshuar nga A vlen te B (pa login të ri).
            using (var session = await clientB.GetAsync(new Uri(pendingAuthorize, UriKind.Relative), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, session.StatusCode);
            }

            // (2) Consent-i në proces (formular + antiforgery të lëshuara nga A) përfundon te B.
            using (var granted = await InteractiveClient.DecideAsync(clientB, pendingConsent, InteractiveAuthorizationContract.ApproveValue))
            {
                Assert.Equal(HttpStatusCode.Redirect, granted.StatusCode);
                Assert.False(string.IsNullOrEmpty(InteractiveClient.RedirectParameter(granted, "code")));
            }

            // (3) Kodi i lëshuar para restart-it shlyhet pas tij — VETËM një herë.
            using (var redeemed = await InteractiveClient.RedeemAsync(clientB, clientId, unredeemed.Code, unredeemed.Verifier))
            {
                Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
            }

            using (var replay = await InteractiveClient.RedeemAsync(clientB, clientId, unredeemed.Code, unredeemed.Verifier))
            {
                Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
            }

            // (4) Gjendja e KONSUMUAR para restart-it nuk ringjallet.
            using (var resurrected = await InteractiveClient.RedeemAsync(clientB, clientId, consumed.Code, consumed.Verifier))
            {
                Assert.Equal(HttpStatusCode.BadRequest, resurrected.StatusCode);
                Assert.DoesNotContain("access_token", await InteractiveClient.BodyAsync(resurrected), StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(keyDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task R6_restart_mid_flow_me_key_ring_te_ndryshem_kerkon_login_te_ri_por_kodi_i_leshuar_mbetet_i_vlefshem()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2br6b");
        // Default-i i framework-ut e ruan key ring-un në profilin e përdoruesit (i ndarë
        // mes proceseve të të njëjtit makinë/user) — prandaj ndarja provohet me dy
        // direktori TË NDRYSHME të kontrolluara nga testi, jo me “key ring efemer”.
        var keysA = Directory.CreateTempSubdirectory("idata-synthetic-keys-a-").FullName;
        var keysB = Directory.CreateTempSubdirectory("idata-synthetic-keys-b-").FullName;
        var cookies = new System.Net.CookieContainer();
        string clientId;
        string userName;
        string pendingAuthorize;
        Dictionary<string, string> pendingConsent;
        (string Code, string Verifier) unredeemed;
        try
        {
        await using (var hostA = lab.Host(lab.Settings with { DataProtectionKeyDirectory = keysA }))
        {
            using var clientA = InteractiveClient.CreateCookieClient(hostA, cookies);
            clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(hostA);
            (userName, _) = await SyntheticPrincipals.CreateUserAsync(hostA);
            unredeemed = await InteractiveClient.IssueCodeAsync(clientA, clientId, userName, State);
            var (_, challenge) = InteractiveClient.CreatePkcePair();
            pendingAuthorize = InteractiveClient.AuthorizationUrl(clientId, challenge, State);
            var (page, fields) = await InteractiveClient.ConsentPageAsync(clientA, pendingAuthorize);
            page.Dispose();
            pendingConsent = fields;
        }

        // Host B me key ring TJETËR: cookie-ja dhe antiforgery-i i A-së NUK njihen →
        // login i ri (fail-closed), asnjë kod nga formulari i vjetër.
        await using var hostB = lab.Host(lab.Settings with { DataProtectionKeyDirectory = keysB });
        using var clientB = InteractiveClient.CreateCookieClient(hostB, cookies);
        using (var session = await clientB.GetAsync(new Uri(pendingAuthorize, UriKind.Relative), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Redirect, session.StatusCode);
            Assert.Contains("/" + InteractiveAuthorizationContract.LoginPath, session.Headers.Location!.ToString(), StringComparison.Ordinal);
        }

        using (var stale = await InteractiveClient.DecideAsync(clientB, pendingConsent, InteractiveAuthorizationContract.ApproveValue))
        {
            Assert.NotEqual(HttpStatusCode.Redirect, stale.StatusCode);
            Assert.Null(InteractiveClient.RedirectParameter(stale, "code"));
        }

        // Kodi i lëshuar para restart-it është DB-backed (jo i lidhur me key ring-un): shlyhet një herë.
        using (var redeemed = await InteractiveClient.RedeemAsync(clientB, clientId, unredeemed.Code, unredeemed.Verifier))
        {
            Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        }

        using (var replay = await InteractiveClient.RedeemAsync(clientB, clientId, unredeemed.Code, unredeemed.Verifier))
        {
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        }

        // Pas login-it të ri flow-i përfundon normalisht te B.
        var (fresh, freshVerifier) = await InteractiveClient.IssueCodeAsync(clientB, clientId, userName, State);
        using var ok = await InteractiveClient.RedeemAsync(clientB, clientId, fresh, freshVerifier);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }
        finally
        {
            Directory.Delete(keysA, recursive: true);
            Directory.Delete(keysB, recursive: true);
        }
    }

    // ═══════════════ R7 — kërkesa KONKURRUESE me mbivendosje të matur ═══════════════

    [Fact]
    public async Task R7_flow_e_konkurruese_me_klient_state_dhe_challenge_te_ndryshem_mbeten_te_ndara()
    {
        const int flows = 4;
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2br7");
        await using var factory = lab.Host(lab.Settings with { LoginPermitLimit = 100 });
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);

        var clientIds = new string[flows];
        for (var index = 0; index < flows; index++)
        {
            clientIds[index] = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        }

        var timings = Enumerable.Range(0, flows).Select(_ => new RequestTimingHandler()).ToArray();
        var clients = Enumerable.Range(0, flows)
            .Select(index => InteractiveClient.CreateCookieClient(factory, timing: timings[index]))
            .ToArray();
        try
        {
            // Çdo flow: sesion i vet, klient i vet, state dhe challenge të vet — të nisura NJËKOHËSISHT.
            var results = await Task.WhenAll(Enumerable.Range(0, flows).Select(async index =>
            {
                var state = $"synthetic-state-{index}";
                var (code, verifier) = await InteractiveClient.IssueCodeAsync(clients[index], clientIds[index], userName, state);
                using var redeemed = await InteractiveClient.RedeemAsync(clients[index], clientIds[index], code, verifier);
                Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
                return (Index: index, Code: code, Verifier: verifier, State: state,
                    AccessToken: (await InteractiveClient.AccessTokenAsync(redeemed))!);
            }));

            // Ndarja: kode/token unikë; kodi i një flow-i nuk shlyhet me verifier-in e tjetrit.
            Assert.Equal(flows, results.Select(result => result.Code).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(flows, results.Select(result => result.AccessToken).Distinct(StringComparer.Ordinal).Count());
            using (var cross = await InteractiveClient.RedeemAsync(clients[0], clientIds[0], results[1].Code, results[1].Verifier))
            {
                Assert.Equal(HttpStatusCode.BadRequest, cross.StatusCode);
            }

            // Evidencë e MBIVENDOSJES reale: intervalet e kërkesave të autorizimit/shkëmbimit
            // të të paktën një çifti flow-esh mbivendosen në kohë (Stopwatch monotonik).
            var authorityRequests = timings
                .Select(timing => timing.Intervals
                    .Where(interval => interval.Path.StartsWith("/connect/", StringComparison.Ordinal))
                    .ToArray())
                .ToArray();
            Assert.All(authorityRequests, intervals => Assert.NotEmpty(intervals));
            var overlappingPairs = 0;
            for (var a = 0; a < flows; a++)
            {
                for (var b = a + 1; b < flows; b++)
                {
                    if (authorityRequests[a].Any(x => authorityRequests[b].Any(y => RequestTimingHandler.Overlap(x, y))))
                    {
                        overlappingPairs++;
                    }
                }
            }

            Assert.True(overlappingPairs > 0, "asnjë çift kërkesash `/connect/*` nuk u mbivendos në kohë — ekzekutimi ishte sekuencial");
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    // ═══════════════ R8 — ndërprerje e PËRKOHSHME dhe rikthim i së njëjtës DB ═══════════════

    [Fact]
    public async Task R8_rikthimi_i_se_njejtes_DB_lejon_flow_te_ri_dhe_nuk_ringjall_gjendjen_e_konsumuar()
    {
        await using var lab = await IdentityInteractiveLab.CreateAsync("b2br8");
        await using var factory = lab.Host();
        using var client = InteractiveClient.CreateCookieClient(factory);
        var clientId = await SyntheticPrincipals.RegisterPublicCodeClientAsync(factory);
        var (userName, _) = await SyntheticPrincipals.CreateUserAsync(factory);

        // Gjendje PARA ndërprerjes: një kod i konsumuar dhe një sesion aktiv.
        var consumed = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State);
        using (var redeemed = await InteractiveClient.RedeemAsync(client, clientId, consumed.Code, consumed.Verifier))
        {
            Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        }

        var (_, challenge) = InteractiveClient.CreatePkcePair();
        var authorize = InteractiveClient.AuthorizationUrl(clientId, challenge, State);

        await SyntheticOutage.SuspendAsync(lab.Database.DatabaseName);
        try
        {
            using var during = await client.GetAsync(new Uri(authorize, UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, during.StatusCode);
            Assert.Null(InteractiveClient.RedirectParameter(during, "code"));
        }
        finally
        {
            await SyntheticOutage.ResumeAsync(lab.Database.DatabaseName);
        }

        // PAS rikthimit të SË NJËJTËS DB: sesioni ekzistues dhe gjendja e saj janë aty
        // (consent page pa login të ri), flow-i i ri përfundon normalisht.
        using (var after = await client.GetAsync(new Uri(authorize, UriKind.Relative), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        }

        var (fresh, freshVerifier) = await InteractiveClient.IssueCodeAsync(client, clientId, userName, State);
        using (var ok = await InteractiveClient.RedeemAsync(client, clientId, fresh, freshVerifier))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        // Gjendja e konsumuar para ndërprerjes NUK ringjallet.
        using var resurrected = await InteractiveClient.RedeemAsync(client, clientId, consumed.Code, consumed.Verifier);
        Assert.Equal(HttpStatusCode.BadRequest, resurrected.StatusCode);
        using var document = JsonDocument.Parse(await InteractiveClient.BodyAsync(resurrected));
        Assert.False(document.RootElement.TryGetProperty("access_token", out _));
    }
}
