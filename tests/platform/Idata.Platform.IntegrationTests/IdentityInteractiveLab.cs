using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Idata.Platform.Modules.Identity.Application;
using Idata.Platform.Modules.Identity.Infrastructure;
using Idata.Platform.Persistence;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Laboratori i interaksionit (B.2b): DB sintetike e migruar, certifikata
/// sintetike, host REAL mbi `Program`, klient HTTP me cookie container, klientë
/// OAuth publikë me PKCE dhe përdorues sintetikë të krijuar në RUNTIME. Zero
/// kredencial real, zero client id/secret/redirect i produktit, zero material
/// në repo.
/// </summary>
internal sealed class IdentityInteractiveLab : IAsyncDisposable
{
    private const string PfxPassphrase = "synthetic-interactive-pfx-passphrase";

    /// <summary>Kredencial SINTETIK i gjeneruar për këtë proces (kurrë real, kurrë i ricikluar).</summary>
    internal const string SyntheticCredential = "Synthetic-interactive-1";

    internal const string SyntheticRedirectUri = "https://synthetic-client.localhost/callback";

    internal const string SyntheticScope = "synthetic-scope";

    private string? previousDatabase;

    internal PlatformTestDatabase Database { get; private init; } = null!;

    internal X509Certificate2 Signing { get; } = SyntheticCertificates.Signing("CN=synthetic-interactive-signing");

    internal X509Certificate2 Encryption { get; } = SyntheticCertificates.Encryption("CN=synthetic-interactive-encryption");

    internal IdentityAuthorityHostSettings Settings => new()
    {
        Environment = "Production",
        Signing = [(SyntheticCertificates.ToPfxBase64(Signing, PfxPassphrase), PfxPassphrase)],
        Encryption = [(SyntheticCertificates.ToPfxBase64(Encryption, PfxPassphrase), PfxPassphrase)],
    };

    internal static async Task<IdentityInteractiveLab> CreateAsync(string label)
    {
        var database = await PlatformTestDatabase.CreateAsync(label);
        var lab = new IdentityInteractiveLab { Database = database };
        lab.previousDatabase = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", database.DatabaseName);
        Assert.Equal(0, await Idata.Platform.DbMigrator.MigratorApp.RunAsync(["migrate"]));
        return lab;
    }

    internal IdentityAuthorityFactory Host(IdentityAuthorityHostSettings? settings = null)
        => new(settings ?? Settings);

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previousDatabase);
        Signing.Dispose();
        Encryption.Dispose();
        await Database.DisposeAsync();
    }
}

/// <summary>Ndihmësit e sipërfaqes interaktive: klient me cookie, PKCE, formularë HTML dhe redirect-e.</summary>
internal static class InteractiveClient
{
    private static readonly Regex HiddenField = new(
        "<input type=\"hidden\" name=\"([^\"]*)\" value=\"([^\"]*)\">", RegexOptions.CultureInvariant);

    /// <summary>
    /// Klient HTTPS me cookie container dhe PA ndjekje automatike të redirect-eve.
    /// `cookies` i ndarë lejon të njëjtin sesion mbi host-e të njëpasnjëshëm (restart);
    /// `timing` regjistron intervalet reale të çdo kërkese (provë concurrency).
    /// </summary>
    internal static HttpClient CreateCookieClient(
        IdentityAuthorityFactory factory, System.Net.CookieContainer? cookies = null, RequestTimingHandler? timing = null)
    {
        var handlers = new List<System.Net.Http.DelegatingHandler>();
        if (timing is not null)
        {
            handlers.Add(timing);
        }

        handlers.Add(cookies is null ? new CookieContainerHandler() : new CookieContainerHandler(cookies));
        return factory.CreateDefaultClient(new Uri("https://localhost/"), [.. handlers]);
    }

    /// <summary>Login + consent → kod autorizimi (i lëshuar nga flow-i REAL) dhe verifier-i i tij.</summary>
    internal static async Task<(string Code, string Verifier)> IssueCodeAsync(
        HttpClient client, string clientId, string userName, string state)
    {
        var (verifier, challenge) = CreatePkcePair();
        var authorize = AuthorizationUrl(clientId, challenge, state);
        using (var login = await SignInAsync(client, userName, IdentityInteractiveLab.SyntheticCredential, authorize))
        {
            Assert.Equal(System.Net.HttpStatusCode.Redirect, login.StatusCode);
        }

        var (page, fields) = await ConsentPageAsync(client, authorize);
        page.Dispose();
        using var granted = await DecideAsync(client, fields, InteractiveAuthorizationContract.ApproveValue);
        var code = RedirectParameter(granted, "code");
        Assert.False(string.IsNullOrEmpty(code), $"asnjë kod: status={granted.StatusCode} location={granted.Headers.Location}");

        // C2: `state`-i LEXOHET nga redirect-i real i consent-it dhe krahasohet EKZAKT me
        // atë të kërkesës — mungesa ose mospërputhja dështon lëshimin (jo input-i i ruajtur).
        var returnedState = RedirectParameter(granted, "state");
        Assert.True(returnedState is not null, $"redirect-i pa `state`: location={granted.Headers.Location}");
        Assert.Equal(state, returnedState);
        return (code!, verifier);
    }

    internal static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifier = Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlTextEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    internal static string AuthorizationUrl(
        string clientId, string? challenge, string state, string? scope = null,
        string? redirectUri = null, string challengeMethod = "S256")
    {
        var query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri ?? IdentityInteractiveLab.SyntheticRedirectUri,
            ["response_type"] = ResponseTypes.Code,
            ["state"] = state,
        };
        if (challenge is not null)
        {
            query["code_challenge"] = challenge;
            query["code_challenge_method"] = challengeMethod;
        }

        if (scope is not null)
        {
            query["scope"] = scope;
        }

        return QueryHelpers.AddQueryString("/" + InteractiveAuthorizationContract.AuthorizationPath, query);
    }

    /// <summary>Fushat hidden të një formulari të renderuar (të dekoduara nga HTML).</summary>
    internal static Dictionary<string, string> HiddenFields(string html)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in HiddenField.Matches(html))
        {
            fields[System.Net.WebUtility.HtmlDecode(match.Groups[1].Value)] =
                System.Net.WebUtility.HtmlDecode(match.Groups[2].Value);
        }

        return fields;
    }

    internal static async Task<string> BodyAsync(HttpResponseMessage response)
        => await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    /// <summary>Vlera e një parametri nga redirect-i (query ose fragment nuk përdoret në code flow).</summary>
    internal static string? RedirectParameter(HttpResponseMessage response, string name)
    {
        var location = response.Headers.Location;
        if (location is null)
        {
            return null;
        }

        var absolute = location.IsAbsoluteUri ? location : new Uri(new Uri("https://localhost/"), location);
        return QueryHelpers.ParseQuery(absolute.Query).TryGetValue(name, out var value) ? value.ToString() : null;
    }

    /// <summary>Login i plotë: GET formularin, POST kredencialet sintetike, kthen përgjigjen e POST-it.</summary>
    internal static async Task<HttpResponseMessage> SignInAsync(
        HttpClient client, string username, string credential, string? returnUrl = null)
    {
        var loginUrl = "/" + InteractiveAuthorizationContract.LoginPath
            + (returnUrl is null
                ? string.Empty
                : "?" + InteractiveAuthorizationContract.ReturnUrlParameter + "=" + Uri.EscapeDataString(returnUrl));
        using var form = await client.GetAsync(new Uri(loginUrl, UriKind.Relative), TestContext.Current.CancellationToken);
        form.EnsureSuccessStatusCode();
        var fields = HiddenFields(await BodyAsync(form));

        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [InteractiveAuthorizationContract.UsernameField] = username,
            [InteractiveAuthorizationContract.CredentialField] = credential,
        };
        foreach (var (key, value) in fields)
        {
            payload[key] = value;
        }

        return await client.PostAsync(
            new Uri("/" + InteractiveAuthorizationContract.LoginPath, UriKind.Relative),
            new FormUrlEncodedContent(payload),
            TestContext.Current.CancellationToken);
    }

    /// <summary>Merr faqen e consent-it për një kërkesë autorizimi (kërkon sesion aktiv).</summary>
    internal static async Task<(HttpResponseMessage Response, Dictionary<string, string> Fields)> ConsentPageAsync(
        HttpClient client, string authorizationUrl)
    {
        var response = await client.GetAsync(new Uri(authorizationUrl, UriKind.Relative), TestContext.Current.CancellationToken);
        return (response, response.IsSuccessStatusCode ? HiddenFields(await BodyAsync(response)) : []);
    }

    /// <summary>Dërgon vendimin e consent-it me fushat origjinale të kërkesës.</summary>
    internal static Task<HttpResponseMessage> DecideAsync(
        HttpClient client, IReadOnlyDictionary<string, string> fields, string decision,
        IReadOnlyDictionary<string, string>? extra = null)
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in fields)
        {
            payload[key] = value;
        }

        if (extra is not null)
        {
            foreach (var (key, value) in extra)
            {
                payload[key] = value;
            }
        }

        payload[InteractiveAuthorizationContract.DecisionField] = decision;
        return client.PostAsync(
            new Uri("/" + InteractiveAuthorizationContract.AuthorizationPath, UriKind.Relative),
            new FormUrlEncodedContent(payload),
            TestContext.Current.CancellationToken);
    }

    /// <summary>Shkëmbimi i kodit në `/connect/token` (pipeline REAL, zero passthrough).</summary>
    internal static Task<HttpResponseMessage> RedeemAsync(
        HttpClient client, string clientId, string code, string? verifier,
        string? redirectUri = null, string grantType = GrantTypes.AuthorizationCode)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = grantType,
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri ?? IdentityInteractiveLab.SyntheticRedirectUri,
        };
        if (verifier is not null)
        {
            form["code_verifier"] = verifier;
        }

        return client.PostAsync(
            new Uri("/" + IdentityAuthorityServiceCollectionExtensions.TokenEndpointPath, UriKind.Relative),
            new FormUrlEncodedContent(form),
            TestContext.Current.CancellationToken);
    }

    internal static async Task<string?> AccessTokenAsync(HttpResponseMessage response)
    {
        var body = await BodyAsync(response);
        using var document = System.Text.Json.JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("access_token", out var token) ? token.GetString() : null;
    }

    internal static Task<HttpResponseMessage> ProtectedAsync(HttpClient client, string? accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/__b2a/protected", UriKind.Relative));
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}

/// <summary>Intervalet REALE (tick-e monotonikë) të kërkesave — evidencë mbivendosjeje, jo vetëm API paralelizimi.</summary>
internal sealed class RequestTimingHandler : System.Net.Http.DelegatingHandler
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Path, long Start, long End)> intervals = new();

    internal IReadOnlyCollection<(string Path, long Start, long End)> Intervals => intervals;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var response = await base.SendAsync(request, cancellationToken);
        intervals.Enqueue((request.RequestUri?.AbsolutePath ?? string.Empty, start, System.Diagnostics.Stopwatch.GetTimestamp()));
        return response;
    }

    internal static bool Overlap((string Path, long Start, long End) a, (string Path, long Start, long End) b)
        => a.Start < b.End && b.Start < a.End;
}

/// <summary>
/// Ndërprerje e PËRKOHSHME e DB-së SINTETIKE të testit (vetëm e saj): refuzim i
/// lidhjeve të reja + ndërprerje e sesioneve ekzistuese, me rikthim të së NJËJTËS DB
/// dhe gjendjes së saj — jo DROP, jo DB e re bosh.
/// </summary>
internal static class SyntheticOutage
{
    internal static async Task SuspendAsync(string databaseName)
    {
        Assert.True(SyntheticDatabaseSafety.IsSynthetic(databaseName));
        await using var connection = new Npgsql.NpgsqlConnection(PlatformTestDatabase.MaintenanceConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await SyntheticDatabaseSafety.AssertMaintenanceAsync(connection);
        await ExecuteAsync(connection, $"ALTER DATABASE \"{databaseName}\" WITH ALLOW_CONNECTIONS false");
        await ExecuteAsync(connection,
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{databaseName}' AND pid <> pg_backend_pid()");
    }

    internal static async Task ResumeAsync(string databaseName)
    {
        Assert.True(SyntheticDatabaseSafety.IsSynthetic(databaseName));
        await using var connection = new Npgsql.NpgsqlConnection(PlatformTestDatabase.MaintenanceConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await SyntheticDatabaseSafety.AssertMaintenanceAsync(connection);
        await ExecuteAsync(connection, $"ALTER DATABASE \"{databaseName}\" WITH ALLOW_CONNECTIONS true");
    }

    private static async Task ExecuteAsync(Npgsql.NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>Krijimi RUNTIME i klientëve OAuth dhe përdoruesve sintetikë mbi store-in real.</summary>
internal static class SyntheticPrincipals
{
    /// <summary>Regjistron një scope SINTETIK në store-in real (idempotent për host).</summary>
    internal static async Task EnsureScopeAsync(IdentityAuthorityFactory factory, string name)
    {
        using var scope = factory.Services.CreateScope();
        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        if (await scopes.FindByNameAsync(name, TestContext.Current.CancellationToken) is null)
        {
            await scopes.CreateAsync(
                new OpenIddictScopeDescriptor { Name = name, DisplayName = $"Synthetic scope {name}" },
                TestContext.Current.CancellationToken);
        }
    }

    internal static async Task<string> RegisterPublicCodeClientAsync(
        IdentityAuthorityFactory factory, string? redirectUri = null, bool withScope = true,
        string? displayName = null, bool requirePkceAtClient = true)
    {
        await EnsureScopeAsync(factory, IdentityInteractiveLab.SyntheticScope);
        var clientId = $"synthetic-interactive-{Guid.NewGuid():N}";
        using var scope = factory.Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            DisplayName = displayName ?? "Synthetic interactive client",
            RedirectUris = { new Uri(redirectUri ?? IdentityInteractiveLab.SyntheticRedirectUri) },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.ResponseTypes.Code,
            },
        };
        if (requirePkceAtClient)
        {
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        }

        if (withScope)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + IdentityInteractiveLab.SyntheticScope);
        }

        await applications.CreateAsync(descriptor, TestContext.Current.CancellationToken);
        return clientId;
    }

    internal static async Task<(string UserName, Guid Id)> CreateUserAsync(
        IdentityAuthorityFactory factory, bool twoFactor = false)
    {
        var userName = $"synthetic-user-{Guid.NewGuid():N}";
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<UserAccountRecord>>();
        var account = new UserAccountRecord { UserName = userName, Email = $"{userName}@synthetic.invalid" };
        var created = await users.CreateAsync(account, IdentityInteractiveLab.SyntheticCredential);
        Assert.True(created.Succeeded, string.Join(" · ", created.Errors.Select(error => error.Code)));
        if (twoFactor)
        {
            Assert.True((await users.SetTwoFactorEnabledAsync(account, true)).Succeeded);
        }

        return (userName, account.Id);
    }

    internal static async Task<bool> IsLockedOutAsync(IdentityAuthorityFactory factory, string userName)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<UserAccountRecord>>();
        var account = await users.FindByNameAsync(userName);
        return account is not null && await users.IsLockedOutAsync(account);
    }

    /// <summary>Payload-i i RUAJTUR i një kodi autorizimi reference (artefakti që unwrap-on serveri).</summary>
    internal static async Task<string> ReadCodePayloadAsync(IdentityAuthorityFactory factory, string code)
    {
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var entry = await tokens.FindByReferenceIdAsync(code, TestContext.Current.CancellationToken);
        Assert.NotNull(entry);
        var payload = await tokens.GetPayloadAsync(entry, TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrEmpty(payload));
        return payload!;
    }

    /// <summary>Zëvendëson payload-in e ruajtur (vetëm në DB sintetike të testit).</summary>
    internal static async Task ReplaceCodePayloadAsync(
        IdentityAuthorityFactory factory, string code, string payload)
    {
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var entry = await tokens.FindByReferenceIdAsync(code, TestContext.Current.CancellationToken);
        Assert.NotNull(entry);
        var descriptor = new OpenIddictTokenDescriptor();
        await tokens.PopulateAsync(descriptor, entry, TestContext.Current.CancellationToken);
        descriptor.Payload = payload;
        await tokens.UpdateAsync(entry, descriptor, TestContext.Current.CancellationToken);
    }

    /// <summary>Rrotullon stamp-in e sigurisë — çdo sesion ekzistues bëhet i pavlefshëm.</summary>
    internal static async Task RotateSecurityStampAsync(IdentityAuthorityFactory factory, string userName)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<UserAccountRecord>>();
        var account = await users.FindByNameAsync(userName);
        Assert.NotNull(account);
        Assert.True((await users.UpdateSecurityStampAsync(account)).Succeeded);
    }
}
