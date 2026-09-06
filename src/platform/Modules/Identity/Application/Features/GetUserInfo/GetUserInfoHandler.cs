using Mediator;

namespace Idata.Platform.Modules.Identity.Application.Features.GetUserInfo;

/// <summary>
/// Handler-i i query-t — i zgjidhur përmes mediator-it me lifetime Scoped
/// (ADR-029). Orkestron VETËM portin neutral <see cref="IUserInfoSubjectResolver"/>;
/// zero transport, zero protokoll, zero store. Fail-closed: subjekt bosh me
/// rezultat "Authenticated" NUK pranohet si i autentikuar.
/// </summary>
public sealed class GetUserInfoHandler(IUserInfoSubjectResolver subjectResolver)
    : IQueryHandler<GetUserInfoQuery, GetUserInfoResponse>
{
    public async ValueTask<GetUserInfoResponse> Handle(GetUserInfoQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var authentication = await subjectResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        return authentication.Outcome switch
        {
            UserInfoAuthenticationOutcome.Authenticated when !string.IsNullOrWhiteSpace(authentication.Subject)
                => GetUserInfoResponse.Authenticated(authentication.Subject),
            UserInfoAuthenticationOutcome.InsufficientScope => GetUserInfoResponse.InsufficientScope(),
            _ => GetUserInfoResponse.Unauthenticated(),
        };
    }
}
