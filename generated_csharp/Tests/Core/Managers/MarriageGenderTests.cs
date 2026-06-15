// =============================================================================
//  ChronicleKnights — MarriageGenderTests.cs
// -----------------------------------------------------------------------------
//  Locks the gender contract added so that marriage is a male-female pairing:
//    - Unit.Gender is a first-class, persisted attribute.
//    - MarriageService requires opposite genders (father = Male, mother = Female).
//    - Scout-created units receive a valid gender.
//  These guard the UI fix that segregates the father/mother dropdowns by gender.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Naming;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Managers;

public class MarriageGenderTests
{
    private static Unit MakeUnit(Gender gender, Guid? id = null) => new()
    {
        Id           = id ?? Guid.NewGuid(),
        Job          = JobId.IronWallKnight,
        Age          = 30,
        MaxAge       = 60,
        FirstNameKey = "name-test",
        LastNameKey  = "name-test-family",
        Gender       = gender,
    };

    [Fact]
    public void AreOppositeGenders_IsTrueForMaleFemale_AndFalseForSame()
    {
        var male   = MakeUnit(Gender.Male);
        var female = MakeUnit(Gender.Female);

        Assert.True(MarriageService.AreOppositeGenders(male, female));
        Assert.False(MarriageService.AreOppositeGenders(male, MakeUnit(Gender.Male)));
        Assert.False(MarriageService.AreOppositeGenders(female, MakeUnit(Gender.Female)));
    }

    [Fact]
    public void ExecuteManualMarriage_SameGender_Throws()
    {
        var father  = MakeUnit(Gender.Male);
        var mother  = MakeUnit(Gender.Male); // 同性 → 婚姻不可
        var economy = PointsEconomy.CreateInitial();
        var newborn = new NewbornSpec { MaxAge = 60 };

        Assert.Throws<ArgumentException>(() =>
            MarriageService.ExecuteManualMarriage(economy, father, mother, newborn, new Random(1)));
    }

    [Fact]
    public void ExecuteManualMarriage_OppositeGenderNaturalPair_ProducesChildWithGender()
    {
        var fatherId = Guid.NewGuid();
        var motherId = Guid.NewGuid();

        // 自然婚姻（相互好感度 >= しきい値）にしてコスト 0 とし、残高に依存しない
        // 決定論的検証にする。男女ペアなので婚姻成立する。
        var father = MakeUnit(Gender.Male, fatherId) with
        {
            BattleAffinity = ImmutableDictionary<Guid, int>.Empty.Add(motherId, 200),
        };
        var mother = MakeUnit(Gender.Female, motherId) with
        {
            BattleAffinity = ImmutableDictionary<Guid, int>.Empty.Add(fatherId, 200),
        };
        var economy = PointsEconomy.CreateInitial();
        var newborn = new NewbornSpec { MaxAge = 60 };

        var result = MarriageService.ExecuteManualMarriage(
            economy, father, mother, newborn, new Random(1));

        Assert.NotNull(result.Child);
        Assert.True(result.WasNaturalMarriage);
        Assert.Contains(result.Child.Gender, NameTaxonomy.AllGenders);
    }

    [Fact]
    public void CreateOutsiderUnit_AssignsValidGender()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var unit = ScoutService.CreateOutsiderUnit(new Random(7), used);

        Assert.Contains(unit.Gender, NameTaxonomy.AllGenders);
    }
}
