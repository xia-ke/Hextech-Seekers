using System;
using System.Linq;
using HextechSeekers.Native;
using NUnit.Framework;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativeHextechTests
    {
        static NativeMatchRules Two()
        {
            return new NativeMatchRules(
                new MatchParticipant(1, "Garen", MatchRole.Hunter, true),
                new MatchParticipant(2, "Yasuo", MatchRole.Hider, false));
        }

        static void StartPursuit(NativeMatchRules match)
        {
            match.Begin();
            match.Advance(NativeMatchRules.PreparationSeconds);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Pursuit));
        }

        [Test]
        public void ExactlyEightDistinctOriginalAugmentsAndIndependentThreeChoices()
        {
            Assert.That(NativeHextechPool.All.Length, Is.EqualTo(8));
            var ids = NativeHextechPool.All.Select(a => a.Id).ToArray();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(8));
            var random = new Random(1010);
            var hunter = NativeHextechPool.Deal(MatchRole.Hunter, random);
            var hider = NativeHextechPool.Deal(MatchRole.Hider, random);
            Assert.That(hunter.Length, Is.EqualTo(3));
            Assert.That(hider.Length, Is.EqualTo(3));
            Assert.That(hunter.Select(x => x.Id).Distinct().Count(), Is.EqualTo(3));
            Assert.That(hider.Select(x => x.Id).Distinct().Count(), Is.EqualTo(3));
            Assert.That(hunter.Any(x => x.Id == NativeHextech.MirrorSilver), Is.False);
        }

        [Test]
        public void DuplicateAugmentsAcrossCharactersAreLegalButNotDuplicateHeroes()
        {
            var m = Two();
            var e = new NativeHextechRuntime(m);
            Assert.IsTrue(e.Select(1, NativeHextech.FreezeGold));
            Assert.IsTrue(e.Select(2, NativeHextech.FreezeGold));
            Assert.IsFalse(e.Select(2, NativeHextech.CardSenseSilver));
            Assert.IsFalse(new NativeHextechRuntime(m).Select(1, NativeHextech.MirrorSilver));
        }

        [Test]
        public void FreezeIsTwoSecondStunWithTwoMinuteIndependentCooldown()
        {
            var m = Two();
            var e = new NativeHextechRuntime(m);
            Assert.IsTrue(e.Select(1, NativeHextech.FreezeGold));
            StartPursuit(m);
            Assert.IsFalse(e.TryActive(1, 2, false, 0, null, out _));
            Assert.IsTrue(e.TryActive(1, 2, true, 0, null, out _));
            Assert.IsTrue(e.Stunned(2));
            Assert.IsFalse(e.CanUseHeroSkill(2));
            Assert.IsFalse(e.TryActive(1, 2, true, 0, null, out _));
            e.Advance(2.1f);
            Assert.IsFalse(e.Stunned(2));
            e.Advance(117.9f);
            Assert.IsTrue(e.CanUseActiveHextech(1));
        }

        [Test]
        public void SilenceForbidsOnlyHeroSkillsAndDoesNotBlockActiveAugment()
        {
            var m = Two();
            var e = new NativeHextechRuntime(m);
            Assert.IsTrue(e.Select(1, NativeHextech.AstralSilenceSilver));
            Assert.IsTrue(e.Select(2, NativeHextech.FreezeGold));
            StartPursuit(m);
            Assert.IsTrue(e.TryActive(1, 2, true, 0, null, out _));
            Assert.IsTrue(e.Silenced(2));
            Assert.IsFalse(e.CanUseHeroSkill(2));
            Assert.IsTrue(e.CanUseActiveHextech(2));
            Assert.IsTrue(e.TryActive(2, 1, true, 0, null, out _));
            e.Advance(5f);
            Assert.IsFalse(e.Silenced(2));
        }

        [Test]
        public void ControlDurationIsReducedByActiveGarenTenacity()
        {
            var m = Two();
            var e = new NativeHextechRuntime(m);
            Assert.IsTrue(e.Select(2, NativeHextech.FreezeGold));
            StartPursuit(m);
            Assert.IsTrue(e.TryActive(2, 1, true, 0, null, out _, .5f));
            e.Advance(1.01f);
            Assert.IsFalse(e.Stunned(1));
        }

        [Test]
        public void MirrorAbsorbsFirstValidHitAndProtectsForProvisionalGrace()
        {
            var m = Two();
            var e = new NativeHextechRuntime(m);
            Assert.IsTrue(e.Select(2, NativeHextech.MirrorSilver));
            StartPursuit(m);
            Assert.IsFalse(e.TryBasicAttackCapture(1, 2, true, true, 200f));
            Assert.IsFalse(e.TryBasicAttackCapture(1, 2, true, true, 40f));
            Assert.IsFalse(m.Find(2).Eliminated);
            Assert.IsFalse(e.TryBasicAttackCapture(1, 2, true, true, 40f));
            e.Advance(NativeHextechRuntime.MirrorGraceSecondsProvisional + .01f);
            Assert.IsTrue(e.TryBasicAttackCapture(1, 2, true, true, 40f));
            Assert.IsTrue(m.Find(2).Eliminated);
        }

        [Test]
        public void ClockChangesEndButNeverChangesEarlyCaptureBonusThreshold()
        {
            var m = Two();
            var e = new NativeHextechRuntime(m);
            Assert.IsTrue(e.Select(1, NativeHextech.TimeClockSilver));
            StartPursuit(m);
            Assert.That(m.RemainingSeconds, Is.EqualTo(180f));
            Assert.IsTrue(e.TryActive(1, -1, false, -1, null, out _));
            Assert.That(m.RemainingSeconds, Is.EqualTo(120f));
            Assert.That(NativeMatchRules.EarlyCaptureBonusSeconds(1), Is.EqualTo(60f));
            Assert.IsFalse(e.TryActive(1, -1, false, 1, null, out _));
        }

        [Test]
        public void LevelUpGrantsSecondDifferentSkillOnlyDuringPreparation()
        {
            var m = Two();
            var e = new NativeHextechRuntime(m);
            var skills = new NativeHeroLoadout("Garen", MatchRole.Hunter);
            Assert.IsTrue(skills.Choose(NativeSkillSlot.Q));
            Assert.IsFalse(skills.Choose(NativeSkillSlot.W));
            Assert.IsTrue(e.Select(1, NativeHextech.LevelUpGold));
            Assert.IsFalse(e.ApplyPreparationPassives(1, skills));
            m.Begin();
            Assert.IsTrue(e.ApplyPreparationPassives(1, skills));
            Assert.IsTrue(skills.Choose(NativeSkillSlot.W));
            Assert.IsFalse(skills.Choose(NativeSkillSlot.W));
            Assert.That(skills.Level, Is.EqualTo(2));
            Assert.IsFalse(skills.Choose(NativeSkillSlot.E));
        }

        [Test]
        public void SkillWhiteListRespectsDamageOnlyAndHiderNoBasicAttack()
        {
            Assert.IsFalse(NativeSkillCatalog.Allowed("Garen", MatchRole.Hunter, NativeSkillSlot.E));
            Assert.IsTrue(NativeSkillCatalog.Allowed("Garen", MatchRole.Hider, NativeSkillSlot.Q));
            Assert.IsFalse(NativeSkillCatalog.Allowed("Blitzcrank", MatchRole.Hider, NativeSkillSlot.E));
            Assert.IsTrue(NativeSkillCatalog.Allowed("Blitzcrank", MatchRole.Hunter, NativeSkillSlot.E));
            Assert.IsTrue(NativeSkillCatalog.Allowed("Riven", MatchRole.Hider, NativeSkillSlot.W));
            Assert.IsTrue(NativeSkillCatalog.Allowed("Yasuo", MatchRole.Hider, NativeSkillSlot.E));
            Assert.IsFalse(NativeSkillCatalog.Allowed("Unknown", MatchRole.Hunter, NativeSkillSlot.Q));
        }

        [Test]
        public void ShoesArePassiveAndDoNotSpendCooldown()
        {
            var m = Two();
            var e = new NativeHextechRuntime(m);
            Assert.IsTrue(e.Select(1, NativeHextech.ReadyShoesSilver));
            m.Begin();
            Assert.That(e.MoveMultiplier(1), Is.GreaterThan(1f));
            Assert.IsFalse(e.CanUseActiveHextech(1));
            Assert.That(e.RemainingCooldown(1), Is.EqualTo(0));
        }

        [Test]
        public void GarenQAndWRequireSelectedSkillsAndHonorSilence()
        {
            var m = Two();
            var e = new NativeHextechRuntime(m);
            var loadout = new NativeHeroLoadout("Garen", MatchRole.Hunter);
            var garen = new NativeGarenSkills();
            Assert.IsTrue(loadout.Choose(NativeSkillSlot.Q));
            m.Begin();
            Assert.IsFalse(garen.TryCast(NativeSkillSlot.Q, loadout, m, e, 1, true),
                           "Hunter locked in preparation");
            m.Advance(120);
            Assert.IsTrue(garen.TryCast(NativeSkillSlot.Q, loadout, m, e, 1, true));
            Assert.IsTrue(garen.SlowClearedByQ);
            Assert.That(garen.MovementMultiplier, Is.GreaterThan(1f));
            Assert.IsFalse(garen.TryCast(NativeSkillSlot.Q, loadout, m, e, 1, false));
            Assert.IsFalse(garen.TryCast(NativeSkillSlot.W, loadout, m, e, 1, false));
            garen.Advance(2f);
            Assert.That(garen.MovementMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void FourDistinctHeroSlotsGiveOneHunterAndThreeHiders()
        {
            var match = new NativeMatchRules(
                new MatchParticipant(1, "Garen", MatchRole.Hider, true),
                new MatchParticipant(2, "Blitzcrank", MatchRole.Hunter, false),
                new MatchParticipant(3, "Riven", MatchRole.Hider, false),
                new MatchParticipant(4, "Yasuo", MatchRole.Hider, false));
            Assert.That(match.HiderCount, Is.EqualTo(3));
            Assert.That(match.Players.Count, Is.EqualTo(4));
            Assert.That(NativeMatchRules.ChaseSeconds(match.HiderCount), Is.EqualTo(420f));
        }

        [Test]
        public void CardSenseWithUnverifiedCampDataRefusesWithoutSpendingCooldown()
        {
            var m = Two();
            var e = new NativeHextechRuntime(m);
            Assert.IsTrue(e.Select(1, NativeHextech.CardSenseGold));
            StartPursuit(m);
            Assert.IsFalse(e.TryActive(1, 2, true, 0, null, out var msg));
            Assert.That(msg, Does.Contain("camp"));
            Assert.IsTrue(e.CanUseActiveHextech(1));
        }
    }
}
