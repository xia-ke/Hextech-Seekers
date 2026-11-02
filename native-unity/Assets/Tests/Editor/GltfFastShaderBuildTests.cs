using NUnit.Framework;
using UnityEngine;

namespace HextechSeekers.Native.Tests
{
    public sealed class GltfFastShaderBuildTests
    {
        [TestCase("Shader Graphs/glTF-unlit")]
        [TestCase("Shader Graphs/glTF-pbrMetallicRoughness")]
        public void RuntimeImportShaderGraphMustResolveInEditor(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null,
                "The glTFast 6.20.0 runtime shader graph must be present before a Player build: " + shaderName);
            Assert.That(shader.isSupported, Is.True,
                "The current URP target must support the glTFast shader graph: " + shaderName);
        }
    }
}
