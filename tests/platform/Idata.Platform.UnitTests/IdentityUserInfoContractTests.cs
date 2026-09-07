using Idata.Platform.Modules.Identity.Application;
using Idata.Platform.Modules.Identity.Application.Features.GetUserInfo;
using Xunit;

namespace Idata.Platform.UnitTests;

/// <summary>
/// Vendimet PURE të slice-it UserInfo (B.2c): handler-i orkestron VETËM portin
/// neutral dhe mapon rezultatin fail-closed (subjekt bosh me “Authenticated” nuk
/// pranohet); kontrata mban vetëm `sub` dhe kodet RFC 6750. Zero HTTP, zero
/// OpenIddict, zero DB — provë e izoluar e rregullave, jo e transportit.
/// </summary>
public sealed class IdentityUserInfoContractTests
{
    private sealed class FixedResolver(UserInfoAuthentication result) : IUserInfoSubjectResolver
    {
        public int Calls { get; private set; }

        public Task<UserInfoAuthentication> ResolveAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task Subjekt_i_autentikuar_kthehet_i_pandryshuar_dhe_porti_thirret_nje_here()
    {
        var resolver = new FixedResolver(UserInfoAuthentication.Authenticated("synthetic-subject"));
        var response = await new GetUserInfoHandler(resolver).Handle(new GetUserInfoQuery(), CancellationToken.None);
        Assert.Equal(UserInfoAuthenticationOutcome.Authenticated, response.Outcome);
        Assert.Equal("synthetic-subject", response.Subject);
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public async Task Scope_i_pamjaftueshem_mapohet_pa_subjekt()
    {
        var response = await new GetUserInfoHandler(new FixedResolver(UserInfoAuthentication.InsufficientScope()))
            .Handle(new GetUserInfoQuery(), CancellationToken.None);
        Assert.Equal(UserInfoAuthenticationOutcome.InsufficientScope, response.Outcome);
        Assert.Null(response.Subject);
    }

    [Fact]
    public async Task I_paautentikuar_mapohet_pa_subjekt()
    {
        var response = await new GetUserInfoHandler(new FixedResolver(UserInfoAuthentication.Unauthenticated()))
            .Handle(new GetUserInfoQuery(), CancellationToken.None);
        Assert.Equal(UserInfoAuthenticationOutcome.Unauthenticated, response.Outcome);
        Assert.Null(response.Subject);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Authenticated_me_subjekt_bosh_bie_fail_closed_ne_te_paautentikuar(string? subject)
    {
        // Porti nuk mund ta ndërtojë vetë (fabrika refuzon), por handler-i mbetet fail-closed
        // edhe ndaj një rezultati të ndërtuar drejtpërdrejt.
        var raw = new UserInfoAuthentication(UserInfoAuthenticationOutcome.Authenticated, subject);
        var response = await new GetUserInfoHandler(new FixedResolver(raw)).Handle(new GetUserInfoQuery(), CancellationToken.None);
        Assert.Equal(UserInfoAuthenticationOutcome.Unauthenticated, response.Outcome);
        Assert.Null(response.Subject);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Fabrikat_refuzojne_subjekt_bosh(string? subject)
    {
        Assert.ThrowsAny<ArgumentException>(() => UserInfoAuthentication.Authenticated(subject!));
        Assert.ThrowsAny<ArgumentException>(() => GetUserInfoResponse.Authenticated(subject!));
    }

    [Fact]
    public void Kufijte_e_kontrates_jane_minimale_dhe_neutrale()
    {
        Assert.Equal("connect/userinfo", UserInfoContract.Path);
        Assert.Equal("openid", UserInfoContract.RequiredScope);
        Assert.Equal("sub", UserInfoContract.SubjectMember);
        Assert.Equal("invalid_token", UserInfoContract.InvalidTokenError);
        Assert.Equal("insufficient_scope", UserInfoContract.InsufficientScopeError);

        // Application-i nuk referon transport/provider/store: asnjë tip nga OpenIddict/ASP.NET/Identity.
        var referenced = typeof(UserInfoContract).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToArray();
        Assert.DoesNotContain(referenced, name => name.StartsWith("OpenIddict", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.Contains("Persistence", StringComparison.Ordinal));
    }
}
