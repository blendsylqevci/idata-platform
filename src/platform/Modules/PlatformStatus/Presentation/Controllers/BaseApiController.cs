using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace Idata.Platform.Modules.PlatformStatus.Presentation.Controllers;

/// <summary>
/// Baza MODULE-LOCAL e controller-ëve të PlatformStatus (ADR-029): mban VETËM
/// <see cref="IMediator"/>. Zero service locator, zero DbContext/UoW/
/// repository, zero authorization/tenant resolution, zero transaction/
/// idempotency/audit logic. Promovimi në abstraksion të përbashkët kërkon
/// konsumator të dytë real + review + Human Gate (Increment C decision gate —
/// shih PlatformStatus_Architecture_Pilot_Record.md).
/// </summary>
public abstract class BaseApiController(IMediator mediator) : ControllerBase
{
    /// <summary>Surface i vetëm i lejuar i Presentation-it drejt Application-it.</summary>
    protected IMediator Mediator { get; } = mediator;
}
