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
        const string ScenePath = "Assets/Scenes/NativeRiftPhase1.unity";
        const string RendererPath = "Assets/Settings/HextechURPRenderer.asset";
        const string PipelinePath = "Assets/Settings/HextechURPPipeline.asset";
        [MenuItem("Hextech Seekers/1. Create Native Rift Phase 1")]
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
            string nav = Path.Combine(Application.dataPath, "StreamingAssets", "Rift", "navigation.bin");
            if (!File.Exists(nav) || new FileInfo(nav).Length != RiftGrid.BinaryBytes)
                throw new InvalidDataException("Original nav binary absent or wrong size");
            string output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Build", "Windows", "HextechSeekers.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var options = new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("Windows native build failed: " + report.summary.result);
            Debug.Log("Native Rift Phase1 Windows build: " + output);
        }
    }
}