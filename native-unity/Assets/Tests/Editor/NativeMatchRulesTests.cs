using System;
using System.IO;
using HextechSeekers.Native;
using NUnit.Framework;
using UnityEngine;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativeMatchRulesTests
    {
        static NativeMatchRules Round(int hiders = 1, bool humanHunter = true)
        {
            var participants = new MatchParticipant[hiders + 1];
            participants[0] = new MatchParticipant(1, "Garen",
                MatchRole.Hunter, humanHunter);
            for (int i = 0; i < hiders; i++)
                participants[i + 1] = new MatchParticipant(i + 2,
                    "AI-" + i, MatchRole.Hider, !humanHunter && i == 0);
            return new NativeMatchRules(participants);
        }

        [Test]
        public void TwoMinutesPreparationLocksOnlyHunter()
        {
            var m = Round();
            m.Begin();
            Assert.AreEqual(MatchPhase.Preparation, m.Phase);
            Assert.IsFalse(m.CanMove(1));
            Assert.IsTrue(m.CanMove(2));
            Assert.IsFalse(m.CanAttack(1));
            Assert.IsFalse(m.TryCapture(1, 2, true, true, 10));
            m.Advance(119.5f);
            Assert.AreEqual(MatchPhase.Preparation, m.Phase);
            m.Advance(.5f);
            Assert.AreEqual(MatchPhase.Pursuit, m.Phase);
            Assert.AreEqual(180f, m.RemainingSeconds);
            Assert.IsTrue(m.CanMove(1));
        }

        [Test]
        public void PursuitTimeAndBonusFollowConfirmedFormula()
        {
            Assert.AreEqual(180f, NativeMatchRules.ChaseSeconds(1));
            Assert.AreEqual(300f, NativeMatchRules.ChaseSeconds(2));
            Assert.AreEqual(420f, NativeMatchRules.ChaseSeconds(3));
            Assert.AreEqual(60f, NativeMatchRules.EarlyCaptureBonusSeconds(1));
            Assert.AreEqual(180f, NativeMatchRules.EarlyCaptureBonusSeconds(2));
            Assert.AreEqual(300f, NativeMatchRules.EarlyCaptureBonusSeconds(3));
        }

        [Test]
        public void ValidBasicAttackCapturesOnceButSkillsAndMissesNeverCapture()
        {
            var m = Round();
            m.Begin(); m.Advance(120);
            Assert.IsFalse(m.TryCapture(1, 2, false, true, 40), "Skill is not an attack");
            Assert.IsFalse(m.TryCapture(1, 2, true, false, 40), "Unseen enemy");
            Assert.IsFalse(m.TryCapture(1, 2, true, true, NativeMatchRules.BasicAttackReach + 1));
            Assert.IsFalse(m.TryCapture(2, 1, true, true, 20), "Hider cannot capture");
            Assert.IsTrue(m.TryCapture(1, 2, true, true, 40));
            Assert.AreEqual(1, m.CapturedCount);
            Assert.AreEqual(MatchPhase.Finished, m.Phase);
            Assert.IsFalse(m.TryCapture(1, 2, true, true, 10));
            Assert.IsFalse(m.CanMove(2));
        }

        [Test]
        public void SoloHunterEarlyCaptureScoresBonusAndHiderZero()
        {
            var m = Round();
            m.Begin(); m.Advance(120 + 15);
            Assert.IsTrue(m.TryCapture(1, 2, true, true, 20));
            Assert.AreEqual(2, m.Find(1).Score);
            Assert.AreEqual(0, m.Find(2).Score);
            Assert.IsTrue(m.HunterWon);
            m.Advance(1000);
            Assert.AreEqual(2, m.Find(1).Score, "Finish must apply once");
        }

        [Test]
        public void OnlySurvivorGetsCeilXByTwoAndCaughtHiderGetsOne()
        {
            var m = Round(3);
            m.Begin(); m.Advance(120 + 350);
            Assert.IsTrue(m.TryCapture(1, 2, true, true, 10));
            Assert.IsTrue(m.TryCapture(1, 3, true, true, 10));
            m.Advance(70);
            Assert.AreEqual(MatchPhase.Finished, m.Phase);
            Assert.AreEqual(2, m.Find(4).Score);
            Assert.AreEqual(1, m.Find(2).Score);
            Assert.AreEqual(1, m.Find(3).Score);
            Assert.AreEqual(2, m.Find(1).Score);
        }

        [Test]
        public void TwoSurvivorsEachEarnTwoCaughtHiderZero()
        {
            var m = Round(3);
            m.Begin(); m.Advance(120);
            Assert.IsTrue(m.TryCapture(1, 2, true, true, 50));
            m.Advance(420);
            Assert.AreEqual(0, m.Find(2).Score);
            Assert.AreEqual(2, m.Find(3).Score);
            Assert.AreEqual(2, m.Find(4).Score);
            Assert.AreEqual(1, m.Find(1).Score);
        }

        [Test]
        public void NonEarlyAllCaptureHasNoBonus()
        {
            var m = Round(2);
            m.Begin(); m.Advance(120 + 190);
            Assert.IsTrue(m.TryCapture(1, 2, true, true, 10));
            Assert.IsTrue(m.TryCapture(1, 3, true, true, 10));
            Assert.AreEqual(2, m.Find(1).Score);
            Assert.IsTrue(m.HunterWon);
        }

        [Test]
        public void PreparationOverflowCarriesOnlyIntoPursuit()
        {
            var m = Round();
            m.Begin(); m.Advance(121.5f);
            Assert.AreEqual(MatchPhase.Pursuit, m.Phase);
            Assert.AreEqual(178.5f, m.RemainingSeconds, .0001f);
            Assert.AreEqual(1.5f, m.ChaseElapsedSeconds, .0001f);
        }

        [Test]
        public void NonUniqueHeroAndBadRosterAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new NativeMatchRules(
                new MatchParticipant(1, "Garen", MatchRole.Hunter, true),
                new MatchParticipant(2, "garen", MatchRole.Hider, false)));
            Assert.Throws<ArgumentException>(() => new NativeMatchRules(
                new MatchParticipant(1, "Garen", MatchRole.Hunter, true),
                new MatchParticipant(2, "Yasuo", MatchRole.Hunter, false)));
            Assert.Throws<ArgumentException>(() => new NativeMatchRules(
                new MatchParticipant(1, "Garen", MatchRole.Hunter, true)));
            Assert.Throws<ArgumentOutOfRangeException>(() => NativeMatchRules.ChaseSeconds(4));
        }

        [Test]
        public void AIDoesNotGainUnrestrictedVisibility()
        {
            byte[] buffer = new byte[RiftGrid.BinaryBytes];
            for (int i = 0; i < RiftGrid.Width * RiftGrid.Height; i++) buffer[i] = 1;
            var nav = new RiftGrid(buffer);
            Vector2 observer = nav.Center(210, 210);
            Vector2 close = nav.Center(212, 210);
            Vector2 beyond = nav.Center(240, 210);
            Assert.IsTrue(NativeSight.Visible(nav, observer, close));
            Assert.IsFalse(NativeSight.Visible(nav, observer, beyond));
            var cell = nav.Cell(close.x, close.y);
            buffer[RiftGrid.BushOffset + cell.y * RiftGrid.Width + cell.x] = 4;
            Vector2 fartherBush = nav.Center(217, 210);
            var fartherCell = nav.Cell(fartherBush.x, fartherBush.y);
            buffer[RiftGrid.BushOffset + fartherCell.y * RiftGrid.Width + fartherCell.x] = 4;
            nav = new RiftGrid(buffer);
            Assert.IsTrue(NativeSight.Visible(nav, observer, close), "Proximity reveals bush");
            Assert.IsFalse(NativeSight.Visible(nav, observer, fartherBush));
        }

        [Test]
        public void GrassHidesEnemyBeyondRevealProximityUnlessSharedBush()
        {
            byte[] buffer = new byte[RiftGrid.BinaryBytes];
            for (int i = 0; i < RiftGrid.Width * RiftGrid.Height; i++) buffer[i] = 1;
            var nav = new RiftGrid(buffer);
            Vector2 hunter = nav.Center(210, 210);
            Vector2 hider = nav.Center(218, 210);
            var pos = nav.Cell(hider.x, hider.y);
            buffer[RiftGrid.BushOffset + pos.y * RiftGrid.Width + pos.x] = 2;
            nav = new RiftGrid(buffer);
            Assert.IsFalse(NativeSight.Visible(nav, hunter, hider));
            pos = nav.Cell(hunter.x, hunter.y);
            buffer[RiftGrid.BushOffset + pos.y * RiftGrid.Width + pos.x] = 2;
            nav = new RiftGrid(buffer);
            Assert.IsTrue(NativeSight.Visible(nav, hunter, hider));
        }
    }
}
