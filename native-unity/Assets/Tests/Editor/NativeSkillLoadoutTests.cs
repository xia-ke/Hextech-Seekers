using HextechSeekers.Native;
using NUnit.Framework;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativeSkillLoadoutTests
    {
        [Test]
        public void GarenKeepsOnlyNonDamageQAndWInTheAdaptedRules()
        {
            Assert.IsTrue(NativeSkillCatalog.Allowed("Garen", MatchRole.Hunter, NativeSkillSlot.Q));
            Assert.IsTrue(NativeSkillCatalog.Allowed("Garen", MatchRole.Hider, NativeSkillSlot.W));
            Assert.IsFalse(NativeSkillCatalog.Allowed("Garen", MatchRole.Hunter, NativeSkillSlot.E),
                "Garen E is damage-only and remains excluded by the agreed rules");
        }

        [Test]
        public void BlitzEIsOnlyAvailableToHunterWhileQAndWRemainRoleNeutral()
        {
            Assert.IsTrue(NativeSkillCatalog.Allowed("Blitzcrank", MatchRole.Hunter, NativeSkillSlot.Q));
            Assert.IsTrue(NativeSkillCatalog.Allowed("Blitzcrank", MatchRole.Hider, NativeSkillSlot.W));
            Assert.IsTrue(NativeSkillCatalog.Allowed("Blitzcrank", MatchRole.Hunter, NativeSkillSlot.E));
            Assert.IsFalse(NativeSkillCatalog.Allowed("Blitzcrank", MatchRole.Hider, NativeSkillSlot.E));
        }

        [Test]
        public void RivenAndYasuoRetainAllThreeAdaptedControlOrMobilitySlots()
        {
            foreach (string hero in new[] { "Riven", "Yasuo" })
            foreach (MatchRole role in new[] { MatchRole.Hunter, MatchRole.Hider })
            {
                Assert.IsTrue(NativeSkillCatalog.Allowed(hero, role, NativeSkillSlot.Q));
                Assert.IsTrue(NativeSkillCatalog.Allowed(hero, role, NativeSkillSlot.W));
                Assert.IsTrue(NativeSkillCatalog.Allowed(hero, role, NativeSkillSlot.E));
            }
        }

        [Test]
        public void LevelOneChoosesOneSkillAndLevelTwoMayLearnOneDifferentSkill()
        {
            var loadout = new NativeHeroLoadout("Yasuo", MatchRole.Hider);
            Assert.IsTrue(loadout.Choose(NativeSkillSlot.Q));
            Assert.IsFalse(loadout.Choose(NativeSkillSlot.E));
            Assert.IsFalse(loadout.Knows(NativeSkillSlot.E));
            Assert.IsTrue(loadout.UpgradeDuringPreparation(MatchPhase.Preparation));
            Assert.IsTrue(loadout.Choose(NativeSkillSlot.E));
            Assert.IsTrue(loadout.Knows(NativeSkillSlot.Q));
            Assert.IsTrue(loadout.Knows(NativeSkillSlot.E));
            Assert.IsFalse(loadout.Choose(NativeSkillSlot.Q));
            Assert.IsFalse(loadout.Choose(NativeSkillSlot.W));
        }

        [Test]
        public void BlitzHiderCannotLearnEAsEitherPrimaryOrSecondary()
        {
            var loadout = new NativeHeroLoadout("Blitzcrank", MatchRole.Hider);
            Assert.IsFalse(loadout.Choose(NativeSkillSlot.E));
            Assert.IsTrue(loadout.Choose(NativeSkillSlot.Q));
            Assert.IsTrue(loadout.UpgradeDuringPreparation(MatchPhase.Preparation));
            Assert.IsFalse(loadout.Choose(NativeSkillSlot.E));
            Assert.IsTrue(loadout.Choose(NativeSkillSlot.W));
        }
    }
}
