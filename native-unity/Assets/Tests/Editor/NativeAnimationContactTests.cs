using System;
using System.IO;
using HextechSeekers.Native;
using NUnit.Framework;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativeAnimationContactTests
    {
        static string Temporary(string contents)
        {
            var path = Path.Combine(Path.GetTempPath(),
                "hextech-phase9-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, contents);
            return path;
        }

        [Test]
        public void ContactWindowOnlyResolvesAfterWindup()
        {
            var action = new NativeBasicContactWindow();
            Assert.IsFalse(action.Pending);
            Assert.IsTrue(action.Begin(7, .25f));
            Assert.IsTrue(action.Pending);
            Assert.IsFalse(action.Tick(.1f, out int early));
            Assert.AreEqual(-1, early);
            Assert.IsFalse(action.Tick(.1f, out _));
            Assert.IsTrue(action.Tick(.05f, out int actualTarget));
            Assert.AreEqual(7, actualTarget);
            Assert.IsFalse(action.Pending);
            Assert.IsFalse(action.Tick(1f, out _), "A hit can fire once only");
        }

        [Test]
        public void ContactCannotBeRearmedUntilPreviousHitOrCancel()
        {
            var action = new NativeBasicContactWindow();
            Assert.IsTrue(action.Begin(2));
            Assert.IsFalse(action.Begin(3));
            Assert.AreEqual(2, action.TargetId);
            action.Cancel();
            Assert.IsTrue(action.Begin(3));
            Assert.IsTrue(action.Tick(1f, out int target));
            Assert.AreEqual(3, target);
        }

        [Test]
        public void InvalidContactTimingCannotStart()
        {
            var action = new NativeBasicContactWindow();
            Assert.IsFalse(action.Begin(-1));
            Assert.IsFalse(action.Begin(1, 0));
            Assert.IsFalse(action.Begin(1, float.NaN));
            Assert.IsFalse(action.Begin(1, float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => action.Tick(-.01f, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => action.Tick(float.NaN, out _));
            Assert.IsFalse(action.Pending);
        }

        [Test]
        public void MissingBindingFileNeverInventsAnAnimation()
        {
            string nonexistent = Path.Combine(Path.GetTempPath(),
                "no-phase9-" + Guid.NewGuid().ToString("N") + ".json");
            Assert.IsNull(NativeAbilityAnimationBindings.LoadVerified(
                nonexistent, "Riven", x => true));
        }

        [Test]
        public void ExplicitOriginalClipsSupportDistinctThreeStageRivenQ()
        {
            string file = Temporary("{\"version\":1,\"hero\":\"Riven\"," +
                "\"sourceVerified\":true,\"q1\":\"spell1\",\"q2\":\"spell1a\"," +
                "\"q3\":\"spell1b\",\"w\":\"spell2\",\"e\":\"spell3\"}");
            try
            {
                var clips = new[] { "spell1", "spell1a", "spell1b", "spell2", "spell3" };
                var bindings = NativeAbilityAnimationBindings.LoadVerified(
                    file, "Riven", clip => Array.IndexOf(clips, clip) >= 0);
                Assert.AreEqual("spell1", bindings.Resolve(NativeSkillSlot.Q, 1));
                Assert.AreEqual("spell1a", bindings.Resolve(NativeSkillSlot.Q, 2));
                Assert.AreEqual("spell1b", bindings.Resolve(NativeSkillSlot.Q, 3));
                Assert.AreEqual("spell2", bindings.Resolve(NativeSkillSlot.W));
                Assert.AreEqual("spell3", bindings.Resolve(NativeSkillSlot.E));
                Assert.IsNull(bindings.Resolve(NativeSkillSlot.Q, 4));
            }
            finally { File.Delete(file); }
        }

        [Test]
        public void WrongHeroOrUnverifiedAnimationSourceIsRejected()
        {
            string file = Temporary("{\"version\":1,\"hero\":\"Yasuo\"," +
                "\"sourceVerified\":false,\"q1\":\"spell1\"}");
            try
            {
                Assert.Throws<InvalidDataException>(() =>
                    NativeAbilityAnimationBindings.LoadVerified(file, "Yasuo", x => true));
                Assert.Throws<InvalidDataException>(() =>
                    NativeAbilityAnimationBindings.LoadVerified(file, "Riven", x => true));
            }
            finally { File.Delete(file); }
        }

        [Test]
        public void UnknownClipNamesAreRejectedRatherThanMappedToRandomSpell()
        {
            string file = Temporary("{\"version\":1,\"hero\":\"Blitzcrank\"," +
                "\"sourceVerified\":true,\"q1\":\"invented_hook\"}");
            try
            {
                Assert.Throws<InvalidDataException>(() =>
                    NativeAbilityAnimationBindings.LoadVerified(file, "Blitzcrank",
                        clip => clip == "spell1"));
            }
            finally { File.Delete(file); }
        }

        [Test]
        public void EmptyVerifiedMapIsRejected()
        {
            string file = Temporary("{\"version\":1,\"hero\":\"Yasuo\"," +
                "\"sourceVerified\":true}");
            try
            {
                Assert.Throws<InvalidDataException>(() =>
                    NativeAbilityAnimationBindings.LoadVerified(file, "Yasuo", x => true));
            }
            finally { File.Delete(file); }
        }

        [Test]
        public void BindingsForNonFourHeroIdentitiesCannotLoad()
        {
            string file = Temporary("{\"version\":1,\"hero\":\"Unknown\"," +
                "\"sourceVerified\":true,\"q1\":\"spell1\"}");
            try
            {
                Assert.Throws<ArgumentException>(() =>
                    NativeAbilityAnimationBindings.LoadVerified(file, "Unknown", x => true));
            }
            finally { File.Delete(file); }
        }
    }
}
