using System;
using System.Collections.Generic;
using HextechSeekers.Native;
using NUnit.Framework;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativePlayerRosterTests
    {
        [Test]
        public void EverySelectableChampionBuildsUniqueFourActorRound()
        {
            foreach (string hero in NativePlayerRoster.Heroes)
            {
                foreach (MatchRole role in new[] { MatchRole.Hunter, MatchRole.Hider })
                {
                    var participants = NativePlayerRoster.Build(hero, role, 4);
                    Assert.AreEqual(4, participants.Length);
                    Assert.AreEqual(hero, participants[0].HeroKey);
                    Assert.IsTrue(participants[0].Human);
                    Assert.AreEqual(role, participants[0].Role);
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int hunters = 0;
                    foreach (var participant in participants)
                    {
                        Assert.IsTrue(names.Add(participant.HeroKey),
                            "Duplicate hero: " + participant.HeroKey);
                        if (participant.Role == MatchRole.Hunter) hunters++;
                    }
                    Assert.AreEqual(1, hunters);
                    var rules = new NativeMatchRules(participants);
                    rules.Begin();
                    Assert.AreEqual(MatchPhase.Preparation, rules.Phase);
                    Assert.AreEqual(role == MatchRole.Hider, rules.CanMove(1));
                }
            }
        }

        [Test]
        public void TwoAndThreeActorRoundsExcludeSelectedHeroFromAi()
        {
            foreach (string hero in NativePlayerRoster.Heroes)
                foreach (int count in new[] { 2, 3 })
                {
                    var members = NativePlayerRoster.Build(hero, MatchRole.Hider, count);
                    Assert.AreEqual(count, members.Length);
                    Assert.IsTrue(members[1].Role == MatchRole.Hunter);
                    for (int i = 1; i < members.Length; i++)
                    {
                        Assert.AreNotEqual(hero, members[i].HeroKey);
                        Assert.IsFalse(members[i].Human);
                    }
                }
        }

        [Test]
        public void RejectsInvalidRosterAndUnsupportedHero()
        {
            Assert.IsFalse(NativePlayerRoster.IsSelectable("Unknown"));
            Assert.IsFalse(NativePlayerRoster.IsSelectable(null));
            Assert.IsTrue(NativePlayerRoster.IsSelectable("rIvEn"));
            Assert.AreEqual("Riven", NativePlayerRoster.CanonicalHero("riven"));
            Assert.Throws<ArgumentException>(() =>
                NativePlayerRoster.Build("Unknown", MatchRole.Hunter, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                NativePlayerRoster.Build("Garen", MatchRole.Hunter, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                NativePlayerRoster.Build("Garen", MatchRole.Hunter, 5));
        }

        [Test]
        public void SkillWhitelistRemainsRoleScopedForHumans()
        {
            foreach (string hero in NativePlayerRoster.Heroes)
            {
                var hunter = NativePlayerRoster.Build(hero, MatchRole.Hunter, 2);
                var hider = NativePlayerRoster.Build(hero, MatchRole.Hider, 2);
                Assert.AreEqual(hero, hunter[0].HeroKey);
                Assert.AreEqual(hero, hider[0].HeroKey);
                var hunterLoadout = new NativeHeroLoadout(hero, MatchRole.Hunter);
                var hiderLoadout = new NativeHeroLoadout(hero, MatchRole.Hider);
                Assert.IsTrue(hunterLoadout.Choose(NativeSkillSlot.Q));
                Assert.IsTrue(hiderLoadout.Choose(NativeSkillSlot.Q));
                Assert.AreEqual(hero != "Garen",
                    NativeSkillCatalog.Allowed(hero, MatchRole.Hunter, NativeSkillSlot.E));
                Assert.AreEqual(hero != "Garen" && hero != "Blitzcrank",
                    NativeSkillCatalog.Allowed(hero, MatchRole.Hider, NativeSkillSlot.E));
            }
        }
    }
}
