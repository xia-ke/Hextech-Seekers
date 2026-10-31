using NUnit.Framework;
using System;
using System.IO;
using UnityEngine;
using HextechSeekers.Native;

namespace HextechSeekers.Native.Tests
{
    public sealed class GarenMeshSourceTests
    {
        [Test]
        public void SourceRigJsonIsSupportedByUnityJsonUtility()
        {
            string path = Path.Combine(Application.dataPath, "StreamingAssets", "Champions", "Garen", "garen.json");
            Assert.That(File.Exists(path), Is.True, "Prepare original assets before editor tests");
            var model = JsonUtility.FromJson<GarenRigRuntime.Manifest>(File.ReadAllText(path));
            Assert.That(model, Is.Not.Null);
            Assert.That(model.hero, Is.EqualTo("Garen"));
            Assert.That(model.bones.Length, Is.EqualTo(68));
            Assert.That(model.geometry.skinWeight.count, Is.EqualTo(model.geometry.skinIndex.count));
            Assert.That(Array.Exists(model.clips, c => c.name == "idle1"), Is.True);
            Assert.That(Array.Exists(model.clips, c => c.name == "run"), Is.True);
            Assert.That(Array.Exists(model.clips, c => c.name == "attack1"), Is.True);
        }
    }
}
