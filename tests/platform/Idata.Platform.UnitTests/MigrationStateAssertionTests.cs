using Idata.Platform.DbMigrator;
using Xunit;

namespace Idata.Platform.UnitTests;

/// <summary>
/// Fixtures pozitive dhe NEGATIVE të verifikuesit machine-checkable —
/// counts/gjendje të gabuara duhet të japin failure, jo silent pass.
/// </summary>
public sealed class MigrationStateAssertionTests
{
    private static readonly string[] Assembly = ["0001_InitialPlatformSchema"];

    [Fact]
    public void Gjendja_e_sakte_migruar_kalon()
        => Assert.Empty(MigrationStateAssertion.Check(
            1, 0, true, Assembly, Assembly, sentinelPresent: true));

    [Fact]
    public void Gjendja_e_sakte_zero_kalon()
        => Assert.Empty(MigrationStateAssertion.Check(
            0, 1, false, [], Assembly, sentinelPresent: false));

    [Fact]
    public void Expected_count_i_gabuar_deshton()
    {
        var errors = MigrationStateAssertion.Check(2, 0, true, Assembly, Assembly, true);
        Assert.Contains(errors, e => e.StartsWith("applied-count expected=2 actual=1"));
    }

    [Fact]
    public void Expected_state_i_gabuar_i_sentinelit_deshton()
    {
        var errors = MigrationStateAssertion.Check(1, 0, false, Assembly, Assembly, sentinelPresent: true);
        Assert.Contains(errors, e => e.StartsWith("sentinel expected=absent actual=present"));
    }

    [Fact]
    public void Pending_i_gabuar_deshton()
    {
        var errors = MigrationStateAssertion.Check(0, 0, false, [], Assembly, false);
        Assert.Contains(errors, e => e.StartsWith("pending-count expected=0 actual=1"));
    }

    [Fact]
    public void Id_e_aplikuar_e_panjohur_nga_assembly_deshton()
    {
        var errors = MigrationStateAssertion.Check(
            1, 1, true, ["9999_Fantazme"], Assembly, sentinelPresent: true);
        Assert.Contains(errors, e => e.Contains("applied-id-unknown-to-assembly actual=9999_Fantazme"));
    }
}
