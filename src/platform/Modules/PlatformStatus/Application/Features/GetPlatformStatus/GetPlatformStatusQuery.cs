using Idata.Platform.Modules.PlatformStatus.Domain;
using Mediator;

namespace Idata.Platform.Modules.PlatformStatus.Application.Features.GetPlatformStatus;

/// <summary>
/// Query e vertical slice-it (ADR-029, CQRS selektiv): NJË pyetje read-only —
/// statusi teknik i platformës. Pa command artificial, pa validator ceremonial.
/// </summary>
public sealed record GetPlatformStatusQuery : IQuery<PlatformStatusReport>;
