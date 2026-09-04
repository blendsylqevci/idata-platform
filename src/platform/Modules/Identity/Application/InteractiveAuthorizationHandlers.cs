namespace Idata.Platform.Modules.Identity.Application;

/// <summary>
/// Use-case i login-it interaktiv: validim minimal i hyrjes dhe delegim te porta
/// e verifikimit. Zero transport, zero store, zero vendim protokolli këtu.
/// </summary>
public sealed class InteractiveSignInHandler(IInteractiveCredentialsVerifier verifier)
{
    public Task<InteractiveSignInResult> HandleAsync(InteractiveSignInCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Hyrje bosh nuk prek kurrë store-in — e njëjta përgjigje si çdo refuzim tjetër.
        return string.IsNullOrWhiteSpace(command.Username) || string.IsNullOrEmpty(command.Credential)
            ? Task.FromResult(InteractiveSignInResult.Rejected(InteractiveSignInRejection.InvalidCredential))
            : verifier.VerifyAsync(command, cancellationToken);
    }
}

/// <summary>
/// Use-case i consent-it: përshkruan kërkesën aktuale për faqen e consent-it dhe
/// përcjell vendimin te kufiri i protokollit. Vendimi është FAIL-CLOSED — çdo
/// vlerë e panjohur e fushës së vendimit trajtohet si refuzim.
/// </summary>
public sealed class InteractiveConsentHandler(IAuthorizationProtocolGateway gateway)
{
    public Task<AuthorizationRequestSnapshot?> DescribeAsync(CancellationToken cancellationToken)
        => gateway.DescribeCurrentRequestAsync(cancellationToken);

    public Task<AuthorizationGrantOutcome> DecideAsync(
        ConsentDecision decision, string subjectId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        return decision == ConsentDecision.Approve
            ? gateway.GrantAsync(subjectId, cancellationToken)
            : gateway.DenyAsync(cancellationToken);
    }

    /// <summary>Parse PURE dhe fail-closed: vetëm vlera EKZAKTE `approve` aprovon.</summary>
    public static ConsentDecision ParseDecision(string? value)
        => string.Equals(value, InteractiveAuthorizationContract.ApproveValue, StringComparison.Ordinal)
            ? ConsentDecision.Approve
            : ConsentDecision.Deny;
}
