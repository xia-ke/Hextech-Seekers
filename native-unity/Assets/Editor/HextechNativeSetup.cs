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
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Generated 3D URP scene at " + ScenePath + ". Before Play, prepare the pinned original map assets.");
        }
        [MenuItem("Hextech Seekers/2. Build Windows")]
        public static void BuildWindows()
        {
            CreateScene();
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
            bool hasCamera = false, hasBootstrap = false, hasVisualQa = false;
            foreach (var root in roots)
            {
                if (root.GetComponentInChildren<Camera>(true)) hasCamera = true;
                if (root.GetComponentInChildren<RiftBootstrap>(true)) hasBootstrap = true;
                if (root.GetComponentInChildren<NativeVisualAcceptance>(true)) hasVisualQa = true;
            }
            Debug.Log("NATIVE_BUILD_SCENE_CHECK: path=" + ScenePath +
                      " roots=" + roots.Length + " camera=" + hasCamera +
                      " bootstrap=" + hasBootstrap + " visualQA=" + hasVisualQa);
            if (!hasCamera || !hasBootstrap || !hasVisualQa)
                throw new InvalidDataException("Native Rift scene is missing MainCamera, RiftBootstrap or NativeVisualAcceptance");
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            var options = new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("Windows native build failed: " + report.summary.result);
            Debug.Log("Native Rift Phase2 Windows build: " + output);
        }
    }
}