using Idata.Platform.Modules.AccessControl.Domain;
using Idata.Platform.Persistence;
using Xunit;

namespace Idata.Platform.UnitTests;

/// <summary>
/// §5.B — `EntityStatus` (Domain i AccessControl) dhe `RecordStatus`
/// (Persistence NEUTRAL) janë me qëllim dy vokabularë të NDARË: Persistence
/// nuk referon asnjë Domain moduli (neutraliteti i §8 i mbyll varësitë
/// transitive). Ky test MACHINE-CHECKABLE siguron që vlerat e tyre mbeten
/// SINKRON — çdo divergjencë do ta bënte mapping-un records↔domain të heshtur
/// të gabuar dhe do ta prishte CHECK constraint-in e migrimit 0002.
/// Assembly-t e testit i shohin të dyja; kjo NUK krijon varësi prodhimi.
/// </summary>
public sealed class StatusVocabularyParityTests
{
    [Fact]
    public void Active_dhe_Disabled_jane_identike_ne_Domain_dhe_Persistence()
    {
        Assert.Equal(EntityStatus.Active, RecordStatus.Active);
        Assert.Equal(EntityStatus.Disabled, RecordStatus.Disabled);
    }

    [Fact]
    public void Vlerat_perputhen_me_CHECK_constraint_in_e_migrimit_0002()
    {
        // Migrimi 0002 lejon EKZAKT ('active','disabled') për app_user,
        // business dhe business_membership.
        Assert.Equal("active", RecordStatus.Active);
        Assert.Equal("disabled", RecordStatus.Disabled);
        Assert.Equal("active", EntityStatus.Active);
        Assert.Equal("disabled", EntityStatus.Disabled);
    }
}
