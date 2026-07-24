using Idata.Platform.Modules.PlatformStatus.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Idata.Platform.Modules.PlatformStatus.Presentation;

/// <summary>Endpoint-et e modulit teknik PlatformStatus (zero rrugë biznesi/fiskale).</summary>
public static class PlatformStatusEndpoints
{
    public static IEndpointRouteBuilder MapPlatformStatus(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/platform/status",
                async (GetPlatformStatusHandler handler, CancellationToken cancellationToken) =>
                    TypedResults.Ok(PlatformStatusResponse.From(
                        await handler.HandleAsync(cancellationToken).ConfigureAwait(false))))
            .WithName("GetPlatformStatus")
            .WithSummary("Statusi teknik i platformës (Bootstrap smoke slice).");
        return endpoints;
    }
}
