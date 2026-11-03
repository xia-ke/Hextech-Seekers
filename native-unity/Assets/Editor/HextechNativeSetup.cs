using System;
using System.IO;
using HextechSeekers.Native;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace HextechSeekers.Native.Editor
{
    public static class HextechNativeSetup
    {
        const string ScenePath = "Assets/Scenes/NativeRiftPhase2.unity";
        const string RendererPath = "Assets/Settings/HextechURPRenderer.asset";
        const string PipelinePath = "Assets/Settings/HextechURPPipeline.asset";
        const string GarenMaterialPath = "Assets/Resources/Native/GarenLit.mat";

        static readonly string[] GltfShaderNames = {
            "Shader Graphs/glTF-unlit",
            "Shader Graphs/glTF-pbrMetallicRoughness"
        };

        static Material EnsureRetainedMaterial(string assetPath, Shader shader, Action<Material> configure)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (!material)
            {
                material = new Material(shader) {
                    name = Path.GetFileNameWithoutExtension(assetPath)
                };
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            configure?.Invoke(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void EnsureGltfShaderResources()
        {
            const string folder = "Assets/Resources/Native/GLTFast";
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Native"))
                AssetDatabase.CreateFolder("Assets/Resources", "Native");
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/Resources/Native", "GLTFast");

            var unlitShader = Shader.Find("Shader Graphs/glTF-unlit");
            var pbrShader = Shader.Find("Shader Graphs/glTF-pbrMetallicRoughness");
            if (!unlitShader)
                throw new InvalidDataException("Required glTFast shader not found in Editor: Shader Graphs/glTF-unlit");
            if (!pbrShader)
                throw new InvalidDataException("Required glTFast shader not found in Editor: Shader Graphs/glTF-pbrMetallicRoughness");

            EnsureRetainedMaterial(folder + "/GltfUnlit.mat", unlitShader, null);
            EnsureRetainedMaterial(folder + "/GltfPbrMetallicRoughness.mat", pbrShader, null);

            // glTFast enables _ALPHATEST_ON and _AlphaClip for glTF MASK
            // materials at runtime. Keep that exact Shader Graph variant in a
            // Resources material so Unity's Player build cannot strip it.
            var alphaMask = EnsureRetainedMaterial(
                folder + "/GltfPbrMetallicRoughnessAlphaMask.mat",
                pbrShader,
                material =>
                {
                    if (!material.HasProperty("alphaCutoff") ||
                        !material.HasProperty("_AlphaClip") ||
                        !material.HasProperty("_Cull"))
                        throw new InvalidDataException("glTFast PBR Shader Graph lacks alpha-mask properties.");

                    material.SetFloat("alphaCutoff", 0.5f);
                    material.SetFloat("_AlphaClip", 1f);
                    // Foliage cards are commonly authored double-sided. The
                    // runtime importer still applies each glTF material's
                    // doubleSided flag; this retained variant only preserves
                    // the alpha-test shader variant.
                    material.SetFloat("_Cull", 0f);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                });

            // Preserve the separate BLEND variant as well. It is not a
            // substitute for MASK; it prevents alpha-blended glTF materials
            // from silently falling back to an opaque Player variant.
            var alphaBlend = EnsureRetainedMaterial(
                folder + "/GltfPbrMetallicRoughnessAlphaBlend.mat",
                pbrShader,
                material =>
                {
                    if (!material.HasProperty("_Surface"))
                        throw new InvalidDataException("glTFast PBR Shader Graph lacks _Surface property.");

                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_SrcBlend", (int)BlendMode.SrcAlpha);
                    material.SetFloat("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_AlphaDstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", 0f);
                    material.SetFloat("_Cull", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
                    material.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = (int)RenderQueue.Transparent;
                });

            var unlitAlphaMask = EnsureRetainedMaterial(
                folder + "/GltfUnlitAlphaMask.mat",
                unlitShader,
                material =>
                {
                    if (!material.HasProperty("alphaCutoff") ||
                        !material.HasProperty("_AlphaClip") ||
                        !material.HasProperty("_Cull"))
                        throw new InvalidDataException("glTFast Unlit Shader Graph lacks alpha-mask properties.");
                    material.SetFloat("alphaCutoff", 0.5f);
                    material.SetFloat("_AlphaClip", 1f);
                    material.SetFloat("_Cull", 0f);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                });

            var unlitAlphaBlend = EnsureRetainedMaterial(
                folder + "/GltfUnlitAlphaBlend.mat",
                unlitShader,
                material =>
                {
                    if (!material.HasProperty("_Surface"))
                        throw new InvalidDataException("glTFast Unlit Shader Graph lacks _Surface property.");
                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_SrcBlend", (int)BlendMode.SrcAlpha);
                    material.SetFloat("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_AlphaDstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", 0f);
                    material.SetFloat("_Cull", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
                    material.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = (int)RenderQueue.Transparent;
                });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var retained = Resources.LoadAll<Material>("Native/GLTFast");
            foreach (string shaderName in GltfShaderNames)
            {
                bool found = false;
                foreach (var material in retained)
                    if (material && material.shader && material.shader.name == shaderName)
                        found = true;
                if (!found)
                    throw new InvalidDataException("glTFast shader not retained in Resources: " + shaderName);
            }

            if (!alphaMask || !alphaMask.IsKeywordEnabled("_ALPHATEST_ON") ||
                alphaMask.GetFloat("_AlphaClip") < 0.5f ||
                alphaMask.renderQueue != (int)RenderQueue.AlphaTest)
                throw new InvalidDataException("glTFast MASK shader variant was not retained correctly.");
            if (!alphaBlend || !alphaBlend.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
                alphaBlend.renderQueue != (int)RenderQueue.Transparent)
                throw new InvalidDataException("glTFast PBR BLEND shader variant was not retained correctly.");
            if (!unlitAlphaMask || !unlitAlphaMask.IsKeywordEnabled("_ALPHATEST_ON") ||
                unlitAlphaMask.GetFloat("_AlphaClip") < 0.5f ||
                unlitAlphaMask.renderQueue != (int)RenderQueue.AlphaTest)
                throw new InvalidDataException("glTFast Unlit MASK shader variant was not retained correctly.");
            if (!unlitAlphaBlend || !unlitAlphaBlend.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
                unlitAlphaBlend.renderQueue != (int)RenderQueue.Transparent)
                throw new InvalidDataException("glTFast Unlit BLEND shader variant was not retained correctly.");

            Debug.Log("NATIVE_GLTF_SHADER_RESOURCES_READY: " + retained.Length);
            Debug.Log("NATIVE_GLTF_ALPHA_VARIANTS_READY: mask=true blend=true");
            Debug.Log("NATIVE_GLTF_ALPHA_VARIANTS_DETAIL: pbrMask=true pbrBlend=true unlitMask=true unlitBlend=true");
        }

        static void EnsureGarenRuntimeMaterial()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Native"))
                AssetDatabase.CreateFolder("Assets/Resources", "Native");

            // This persistent material is intentionally referenced through Resources
            // at runtime. It prevents Unity's Player shader-stripping pass from
            // removing URP/Lit when the only previous reference was Shader.Find().
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader)
                throw new InvalidDataException("URP/Lit shader is unavailable in the Editor; verify URP 17.0.3 is installed.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(GarenMaterialPath);
            if (!material)
            {
                material = new Material(shader) { name = "Garen Runtime URP Lit" };
                AssetDatabase.CreateAsset(material, GarenMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(GarenMaterialPath, ImportAssetOptions.ForceUpdate);
            var verified = AssetDatabase.LoadAssetAtPath<Material>(GarenMaterialPath);
            if (!verified || verified.shader != shader)
                throw new InvalidDataException("Failed to serialize the Garen URP/Lit Resources material.");

            Debug.Log("NATIVE_SHADER_RESOURCE_READY: " + GarenMaterialPath +
                      " shader=" + verified.shader.name);
        }
        [MenuItem("Hextech Seekers/1. Create Native Rift Phase 2")]
        public static void CreateScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (!data)
            {
                data = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(data, RendererPath);
            }
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (!urp)
            {
                urp = UniversalRenderPipelineAsset.Create(data);
                AssetDatabase.CreateAsset(urp, PipelinePath);
            }
            // The native fog-of-war samples real URP scene depth to darken
            // world geometry rather than drawing a fake 2D map overlay.
            urp.supportsCameraDepthTexture = true;
            EditorUtility.SetDirty(urp);
            GraphicsSettings.defaultRenderPipeline = urp;
            QualitySettings.renderPipeline = urp;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject camera = new GameObject("Main Camera");
            camera.tag = "MainCamera";
            var cam = camera.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 520;
            cam.nearClipPlane = .3f;
            cam.farClipPlane = 4000f;
            camera.AddComponent<AudioListener>();
            GameObject sun = new GameObject("Rift Daylight");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            sun.transform.rotation = Quaternion.Euler(46, -35, 0);
            var bootstrap = new GameObject("HextechNativeBootstrap");
            bootstrap.AddComponent<RiftBootstrap>();
            bootstrap.AddComponent<NativeVisualAcceptance>();
            bootstrap.AddComponent<NativeMatchController>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Generated 3D URP scene at " + ScenePath + ". Before Play, prepare the pinned original map assets.");
        }
        [MenuItem("Hextech Seekers/2. Build Windows")]
        public static void BuildWindows()
        {
            CreateScene();
            EnsureGarenRuntimeMaterial();
            EnsureGltfShaderResources();
            var rift = Path.Combine(Application.dataPath, "StreamingAssets", "Rift", "map.glb");
            if (!File.Exists(rift)) throw new FileNotFoundException("Original map.glb is missing. Run tools/prepare_assets.py first.");
            var compatibleRift = Path.Combine(Application.dataPath, "StreamingAssets", "Rift", "map-unity.glb");
            if (!File.Exists(compatibleRift)) throw new FileNotFoundException("Unity compatible map-unity.glb is missing. Run tools/convert_webp_glb.py on the SHA-verified original map.glb before building.");
            string nav = Path.Combine(Application.dataPath, "StreamingAssets", "Rift", "navigation.bin");
            if (!File.Exists(nav) || new FileInfo(nav).Length != RiftGrid.BinaryBytes)
                throw new InvalidDataException("Original nav binary absent or wrong size");
            string heroDir = Path.Combine(Application.dataPath, "StreamingAssets", "Champions", "Garen");
            foreach (string name in new[] { "garen.json", "garen.bin", "garen_base_tx_cm.png" })
            {
                if (!File.Exists(Path.Combine(heroDir, name)))
                    throw new FileNotFoundException("Missing original skinned Garen asset: " + name);
            }
            string output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Build", "Windows", "HextechSeekers.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            // Validate the exact scene that will be included in the Player.
            var builtScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = builtScene.GetRootGameObjects();
            bool hasCamera = false, hasBootstrap = false, hasVisualQa = false, hasMatchController = false;
            foreach (var root in roots)
            {
                if (root.GetComponentInChildren<Camera>(true)) hasCamera = true;
                if (root.GetComponentInChildren<RiftBootstrap>(true)) hasBootstrap = true;
                if (root.GetComponentInChildren<NativeVisualAcceptance>(true)) hasVisualQa = true;
                if (root.GetComponentInChildren<NativeMatchController>(true)) hasMatchController = true;
            }
            Debug.Log("NATIVE_BUILD_SCENE_CHECK: path=" + ScenePath +
                      " roots=" + roots.Length + " camera=" + hasCamera +
                      " bootstrap=" + hasBootstrap + " visualQA=" + hasVisualQa +
                      " gameplay=" + hasMatchController);
            if (!hasCamera || !hasBootstrap || !hasVisualQa || !hasMatchController)
                throw new InvalidDataException("Native Rift scene is missing MainCamera, RiftBootstrap, NativeVisualAcceptance or NativeMatchController");
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            var options = new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("Windows native build failed: " + report.summary.result);
            Debug.Log("Native Rift Phase2 Windows build: " + output);
        }
    }
}