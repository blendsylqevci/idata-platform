using Idata.Platform.Modules.PlatformStatus.Application.Features.GetPlatformStatus;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Idata.Platform.Modules.PlatformStatus.Presentation.Controllers;

/// <summary>
/// Thin controller (ADR-029): binding → <c>Mediator.Send</c> → mapping në
/// kontratën HTTP. Kontrata publike mbetet BYTE-IDENTIKE me minimal API-n e
/// mëparshëm: route, operationId, summary, tag dhe skema të pandryshuara.
/// </summary>
[ApiController]
public sealed class PlatformStatusController(Mediator.IMediator mediator) : BaseApiController(mediator)
{
    [HttpGet("/api/platform/status")]
    [Produces("application/json")]
    [EndpointName("GetPlatformStatus")]
    [EndpointSummary("Statusi teknik i platformës (Bootstrap smoke slice).")]
    [Tags("Idata.Platform.Api")]
    public async Task<PlatformStatusResponse> GetAsync(CancellationToken cancellationToken)
        => PlatformStatusResponse.From(
            await Mediator.Send(new GetPlatformStatusQuery(), cancellationToken).ConfigureAwait(false));
}
