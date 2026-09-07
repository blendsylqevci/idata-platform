using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace Idata.Platform.Modules.Identity.Presentation.Controllers;

/// <summary>
/// Baza MODULE-LOCAL e controller-ëve të Identity/Presentation (ADR-029, B.2c):
/// mban VETËM <see cref="IMediator"/>. Zero service locator, zero DbContext/UoW/
/// repository, zero store/protokoll, zero authorization/tenant resolution.
/// Ndjek pattern-in e PlatformStatus PA varësi ndaj atij moduli dhe PA nxjerrje
/// të një shared framework-u (Increment C decision gate — zero Common/BuildingBlocks).
/// </summary>
public abstract class BaseApiController(IMediator mediator) : ControllerBase
{
    /// <summary>Surface i vetëm i lejuar i Presentation-it drejt Application-it.</summary>
    protected IMediator Mediator { get; } = mediator;
}
