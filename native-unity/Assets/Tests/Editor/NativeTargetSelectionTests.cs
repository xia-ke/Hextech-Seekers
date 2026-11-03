using System;
using System.Collections.Generic;
using HextechSeekers.Native;
using NUnit.Framework;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativeTargetSelectionTests
    {
        static NativeTargetSelection.Candidate C(int id, float distance,
            bool visible = true, bool alive = true, bool opponent = true) =>
            new NativeTargetSelection.Candidate(id, opponent, alive, visible, distance);

        [Test]
        public void PreferredVisibleTargetOverridesCloserEnemyInMeleeRange()
        {
            var actors = new[] { C(2, 60f), C(3, 110f) };
            Assert.AreEqual(3, NativeTargetSelection.ChooseBasicTarget(3,
                actors, NativeMatchRules.BasicAttackReach));
        }

        [Test]
        public void OutOfRangePreferredTargetFallsBackToNearestInRange()
        {
            var actors = new[] { C(2, 55f), C(3, 180f) };
            Assert.AreEqual(2, NativeTargetSelection.ChooseBasicTarget(3,
                actors, NativeMatchRules.BasicAttackReach));
        }

        [Test]
        public void UnseenPreferredTargetNeverBecomesBlindBasicAttack()
        {
            var actors = new[] { C(2, 55f), C(3, 70f, visible: false) };
            Assert.AreEqual(2, NativeTargetSelection.ChooseBasicTarget(3,
                actors, NativeMatchRules.BasicAttackReach));
            Assert.AreEqual(-1, NativeTargetSelection.ChooseBasicTarget(3,
                new[] { C(3, 70f, visible: false) },
                NativeMatchRules.BasicAttackReach));
        }

        [Test]
        public void DeadAndAlliedTargetsCannotBeSelected()
        {
            var actors = new[] {
                C(2, 50f, alive: false), C(3, 40f, opponent: false),
                C(4, 100f)
            };
            Assert.AreEqual(4, NativeTargetSelection.ChooseBasicTarget(2,
                actors, NativeMatchRules.BasicAttackReach));
        }

        [Test]
        public void ExactlyOnReachAllowedButOutsideRejected()
        {
            float reach = NativeMatchRules.BasicAttackReach;
            Assert.AreEqual(2, NativeTargetSelection.ChooseBasicTarget(-1,
                new[] { C(2, reach) }, reach));
            Assert.AreEqual(-1, NativeTargetSelection.ChooseBasicTarget(-1,
                new[] { C(2, reach + .001f) }, reach));
        }

        [Test]
        public void MalformedPositionsAndReachCannotGrantAHit()
        {
            var actors = new[] {
                C(2, float.NaN), C(3, float.PositiveInfinity), C(4, -1f)
            };
            Assert.AreEqual(-1, NativeTargetSelection.ChooseBasicTarget(2,
                actors, NativeMatchRules.BasicAttackReach));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                NativeTargetSelection.ChooseBasicTarget(2, actors, float.NaN));
            Assert.Throws<ArgumentNullException>(() =>
                NativeTargetSelection.ChooseBasicTarget(2, null, 130f));
        }

        [Test]
        public void HiddenCardSenseTargetCannotRevealEnemyMarker()
        {
            Assert.IsFalse(NativeTargetSelection.CanDisplaySelectedEnemy(
                false, true, true, false));
            Assert.IsFalse(NativeTargetSelection.CanDisplaySelectedEnemy(
                true, true, true, true));
            Assert.IsFalse(NativeTargetSelection.CanDisplaySelectedEnemy(
                false, false, true, true));
            Assert.IsFalse(NativeTargetSelection.CanDisplaySelectedEnemy(
                false, true, false, true));
            Assert.IsTrue(NativeTargetSelection.CanDisplaySelectedEnemy(
                false, true, true, true));
        }

        [Test]
        public void DoesNotReorderCandidatesOrRememberStaleTargets()
        {
            var actors = new List<NativeTargetSelection.Candidate> {
                C(2, 95f), C(3, 80f), C(4, 110f)
            };
            Assert.AreEqual(3, NativeTargetSelection.ChooseBasicTarget(3,
                actors, NativeMatchRules.BasicAttackReach));
            Assert.AreEqual(3, NativeTargetSelection.ChooseBasicTarget(-1,
                actors, NativeMatchRules.BasicAttackReach));
            Assert.AreEqual(3, actors[1].Id);
        }
    }
}
