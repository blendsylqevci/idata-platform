using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Sipërfaqja e API-së (§10.G) + sjellja e kontrolluar me DB të paarritshme:
/// vetëm endpoint-et e lejuara; ZERO segment `/poc` dhe ZERO test-auth në
/// metadata/OpenAPI të API-së së re .NET; kontratat 403/404/409 +
/// Idempotency-Key të dokumentuara në spec. (Rrugët legacy TypeScript PoC
/// mbeten të paprekura si oracle — jashtë kësaj API-je.)
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class ApiSurfaceTests
{
    [Fact]
    public async Task Route_table_permban_vetem_endpointet_e_lejuara()
    {
        await using var factory = new AccessControlApiFactory();
        _ = factory.CreateClient(); // ndez host-in
        var dataSource = factory.Services.GetRequiredService<EndpointDataSource>();
        var patterns = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .Order()
            .ToArray();

        Assert.Equal(
            [
                "/api/platform/access-context",
                "/api/platform/memberships/{membershipId:guid}/role",
                "/api/platform/status",
                "/openapi/{documentName}.json",
            ],
            patterns);

        Assert.DoesNotContain(patterns, pattern => pattern.Contains("/poc", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(patterns, pattern => pattern.Contains("test", StringComparison.OrdinalIgnoreCase));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task OpenAPI_dokumenton_kontratat_dhe_ska_poc_as_test_auth()
    {
        await using var factory = new AccessControlApiFactory();
        using var client = factory.CreateClient();
        var raw = await client.GetStringAsync(
            new Uri("/openapi/platform-v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("/poc", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("X-Test-", raw, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(raw);
        var paths = document.RootElement.GetProperty("paths");
        var membershipPath = paths.GetProperty("/api/platform/memberships/{membershipId}/role").GetProperty("put");
        Assert.Equal("UpdateMembershipRole", membershipPath.GetProperty("operationId").GetString());

        // Idempotency-Key i dokumentuar si parametër header i DETYRUESHËM.
        var idempotencyParam = membershipPath.GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.Equal("header", idempotencyParam.GetProperty("in").GetString());
        Assert.True(idempotencyParam.GetProperty("required").GetBoolean());

        // Request body kërkon roleId + expectedVersion (CAS).
        var requestSchema = Resolve(document, membershipPath.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"));
        var requiredProps = requestSchema.GetProperty("required").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .Order()
            .ToArray();
        Assert.Equal(["expectedVersion", "roleId"], requiredProps);

        // Kontratat e përgjigjeve 400/403/404/409/503 të dokumentuara.
        var responseCodes = membershipPath.GetProperty("responses").EnumerateObject()
            .Select(property => property.Name)
            .Order()
            .ToArray();
        Assert.Equal(["200", "400", "403", "404", "409", "503"], responseCodes);

        // Response-i i suksesit përmban version-in e ri.
        var successSchema = Resolve(document, membershipPath.GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        var successProps = successSchema.GetProperty("required").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
        Assert.Contains("version", successProps);

        var contextPath = paths.GetProperty("/api/platform/access-context").GetProperty("get");
        Assert.Equal("GetAccessContext", contextPath.GetProperty("operationId").GetString());
        var contextCodes = contextPath.GetProperty("responses").EnumerateObject()
            .Select(property => property.Name)
            .Order()
            .ToArray();
        Assert.Equal(["200", "403", "503"], contextCodes);
    }

    /// <summary>Ndjek `$ref` te components/schemas; kthen schema-n inline ndryshe.</summary>
    private static JsonElement Resolve(JsonDocument document, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var refElement))
        {
            return schema;
        }

        var name = refElement.GetString()!.Split('/')[^1];
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }

    [Fact]
    public async Task DB_e_paarritshme_jep_503_te_kontrolluar_pa_detaje_lidhjeje()
    {
        // DB inekzistente (serveri arrihet, katalogu jo) → dështim i
        // kontrolluar 503 — pa 500, pa stack trace, pa emra/connection string.
        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", "idata_platform_test_inekzistente");
        try
        {
            await using var factory = new AccessControlApiFactory();
            using var client = factory.CreateClient();

            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri("/api/platform/access-context", UriKind.Relative));
            request.Headers.Add("X-Test-UserId", AccessControlSeed.UserA.ToString("D"));
            request.Headers.Add("X-Test-BusinessId", AccessControlSeed.BusinessA.ToString("D"));
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("PLATFORM_UNAVAILABLE", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("idata_platform_test_inekzistente", body, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }
}
