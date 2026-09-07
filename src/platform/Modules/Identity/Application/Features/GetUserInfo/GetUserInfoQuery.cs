using Mediator;

namespace Idata.Platform.Modules.Identity.Application.Features.GetUserInfo;

/// <summary>
/// Query e vertical slice-it UserInfo (ADR-029, CQRS selektiv): NJË pyetje
/// read-only mbi kërkesën aktuale — "kush është subjekti i autentikuar?".
/// Pa input: identiteti vjen VETËM nga access token-i i validuar nga pipeline-i
/// real i protokollit (porti <see cref="IUserInfoSubjectResolver"/>).
/// </summary>
public sealed record GetUserInfoQuery : IQuery<GetUserInfoResponse>;

/// <summary>
/// Përgjigjja publike e slice-it: rezultati i autentikimit + subjekti (vetëm
/// kur është i autentikuar). Mapimi HTTP (200/401/403) bëhet në Presentation.
/// </summary>
public sealed record GetUserInfoResponse(UserInfoAuthenticationOutcome Outcome, string? Subject)
{
    public static GetUserInfoResponse Unauthenticated() => new(UserInfoAuthenticationOutcome.Unauthenticated, null);

    public static GetUserInfoResponse InsufficientScope() => new(UserInfoAuthenticationOutcome.InsufficientScope, null);

    public static GetUserInfoResponse Authenticated(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        return new GetUserInfoResponse(UserInfoAuthenticationOutcome.Authenticated, subject);
    }
}
