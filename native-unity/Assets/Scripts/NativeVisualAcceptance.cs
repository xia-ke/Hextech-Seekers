using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Real Windows Player GPU evidence. Activated only with -hextechVisualQA.
    /// -hextechEvidenceDir <absolute path> allows CI to collect outputs without
    /// assuming Unity's per-user persistentDataPath.
    /// A capture pass still requires human review of artwork/orientation.
    /// </summary>
    public sealed class NativeVisualAcceptance : MonoBehaviour
    {
        [Serializable]
        sealed class Report
        {
            public string status, reason, unityVersion, device, graphicsApi, scene, hero;
            public string[] screenshots;
            public int screenshotCount, frames, width, height, mapRendererCount;
            public float fpsAverage, worstFrameMilliseconds;
            public bool originalSceneLoaded, skinnedModelExists, cameraSeesHero, hardwareGraphics;
            public bool hasIdle, hasRun, hasAttack;
        }

        static bool VisualQaRequested()
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-hextechVisualQA") >= 0;
        }

        // Do not rely solely on a serialized scene component: generated/build
        // scenes can be replaced or lose script references during CI import.
        // Install the visual-QA driver at runtime whenever the explicit flag is set.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureVisualQaDriver()
        {
            var args = Environment.GetCommandLineArgs();
            bool requested = Array.IndexOf(args, "-hextechVisualQA") >= 0;
            Debug.Log("NATIVE_QA_RUNTIME_INIT: requested=" + requested +
                      " scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                      " args=" + string.Join(" ", args));
            if (!requested) return;

            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;

            var driver = FindObjectOfType<NativeVisualAcceptance>();
            if (driver)
            {
                driver.enabled = true;
                Debug.Log("NATIVE_QA_DRIVER_FOUND: scene component is active and enabled");
                return;
            }

            var bootstrap = FindObjectOfType<RiftBootstrap>();
            if (!Camera.main)
            {
                var cameraObject = new GameObject("Main Camera (Native Visual QA)");
                cameraObject.tag = "MainCamera";
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 520f;
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 4000f;
                // The runtime assembly intentionally excludes UnityEngine.AudioModule; QA screenshots do not need audio.
                Debug.LogWarning("NATIVE_QA_CAMERA_CREATED: no MainCamera was present in the built scene");
            }

            if (!bootstrap)
            {
                var root = new GameObject("HextechNativeBootstrap (Runtime Visual QA)");
                bootstrap = root.AddComponent<RiftBootstrap>();
                Debug.LogWarning("NATIVE_QA_BOOTSTRAP_CREATED: no RiftBootstrap was present in the built scene");
            }

            bootstrap.gameObject.AddComponent<NativeVisualAcceptance>();
            Debug.Log("NATIVE_QA_DRIVER_INSTALLED: visual acceptance coroutine will start");
        }

        static string EvidenceDirectory()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-hextechEvidenceDir" && !string.IsNullOrWhiteSpace(args[i + 1]))
                    return Path.GetFullPath(args[i + 1]);
            return Path.Combine(Application.persistentDataPath, "HextechVisualQA");
        }

        static void Finish(Report report, string directory, string status, string reason = "")
        {
            report.status = status;
            report.reason = reason;
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonUtility.ToJson(report, true));
            if (status == "CAPTURED_REQUIRES_VISUAL_REVIEW")
                Debug.Log("NATIVE_QA_RESULT: " + status + " path=" + directory +
                          " GPU=" + report.device + " FPS=" + report.fpsAverage.ToString("F1"));
            else
                Debug.LogError("NATIVE_QA_FAIL: " + status + " " + reason);
            // The compiled Player must never hang after producing evidence.
            if (!Application.isEditor) Application.Quit(status == "CAPTURED_REQUIRES_VISUAL_REVIEW" ? 0 : 1);
        }

        void Start()
        {
            Debug.Log("NATIVE_QA_START: requested=" + VisualQaRequested() +
                      " scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                      " graphics=" + SystemInfo.graphicsDeviceType +
                      " device=" + SystemInfo.graphicsDeviceName);
            if (!VisualQaRequested()) return;
            Application.runInBackground = true;
            StartCoroutine(Capture());
        }

        void OnApplicationQuit()
        {
            if (!VisualQaRequested()) return;
            string directory = EvidenceDirectory();
            try
            {
                Directory.CreateDirectory(directory);
                string result = Path.Combine(directory, "result.json");
                if (!File.Exists(result))
                {
                    var report = new Report {
                        status = "FAIL_PLAYER_QUIT_BEFORE_CAPTURE",
                        reason = "Unity Player shut down before the visual-QA coroutine wrote its final report",
                        unityVersion = Application.unityVersion,
                        device = SystemInfo.graphicsDeviceName,
                        graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                        scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                        hero = "Garen",
                        width = Screen.width,
                        height = Screen.height
                    };
                    File.WriteAllText(result, JsonUtility.ToJson(report, true));
                    Debug.LogError("NATIVE_QA_FAIL: Player quit before evidence capture completed");
                }
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        IEnumerator Capture()
        {
            string evidence = EvidenceDirectory();
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, "player-started.txt"),
                "Unity=" + Application.unityVersion + Environment.NewLine + "Scene=" +
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                Environment.NewLine + "GPU=" + SystemInfo.graphicsDeviceName +
                Environment.NewLine + "API=" + SystemInfo.graphicsDeviceType);
            Debug.Log("NATIVE_QA_CAPTURE_BEGIN: evidence=" + evidence);
            var report = new Report
            {
                status = "INCOMPLETE",
                unityVersion = Application.unityVersion,
                device = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                hero = "Garen",
                width = Screen.width,
                height = Screen.height,
                screenshots = Array.Empty<string>()
            };
            string renderer = report.device == null ? "" : report.device.ToLowerInvariant();
            report.hardwareGraphics =
                report.graphicsApi != "Null" &&
                !renderer.Contains("microsoft basic render") &&
                !renderer.Contains("swiftshader") && !renderer.Contains("warp");
            if (!report.hardwareGraphics)
            {
                Finish(report, evidence, "FAIL_NO_HARDWARE_GPU", report.device + " (" + report.graphicsApi + ")");
                yield break;
            }
            var bootstrap = GetComponent<RiftBootstrap>();
            if (!bootstrap)
            {
                Finish(report, evidence, "FAIL_NO_BOOTSTRAP");
                yield break;
            }

            float start = Time.realtimeSinceStartup;
            while (!bootstrap.IsReady && !bootstrap.HasLoadFailed &&
                   Time.realtimeSinceStartup - start < 180f)
                yield return null;
            report.originalSceneLoaded = bootstrap.IsReady;
            if (!report.originalSceneLoaded)
            {
                string reason = bootstrap.HasLoadFailed
                    ? bootstrap.LoadProblem
                    : "Original Rift GLB/hero not ready after 180 seconds";
                Finish(report, evidence, "FAIL_LOAD", reason);
                yield break;
            }
            var rig = bootstrap.HeroRig;
            var skinned = rig ? rig.GetComponent<SkinnedMeshRenderer>() : null;
            report.skinnedModelExists = skinned && skinned.sharedMesh && skinned.sharedMesh.vertexCount > 0;
            report.mapRendererCount = bootstrap.LoadedMap
                ? bootstrap.LoadedMap.GetComponentsInChildren<Renderer>(true).Length : 0;
            var camera = Camera.main;
            if (camera && rig)
            {
                // Allow one frame for RiftBootstrap.LateUpdate to place the camera.
                yield return null;
                var screenPoint = camera.WorldToViewportPoint(rig.transform.position);
                report.cameraSeesHero = screenPoint.z > 0 &&
                    screenPoint.x >= 0 && screenPoint.x <= 1 &&
                    screenPoint.y >= 0 && screenPoint.y <= 1;
            }
            if (!report.skinnedModelExists || report.mapRendererCount == 0 || !report.cameraSeesHero)
            {
                Finish(report, evidence, "FAIL_SCENE_CONTENT",
                    "heroMesh=" + report.skinnedModelExists +
                    " mapRenderers=" + report.mapRendererCount +
                    " cameraSeesHero=" + report.cameraSeesHero);
                yield break;
            }

            var names = new List<string>();
            string[] clips = { "idle", "run", "attack" };
            for (int j = 0; j < clips.Length; j++)
            {
                string state = clips[j];
                if (state == "run" && !bootstrap.VisualQAMove(500f))
                {
                    Finish(report, evidence, "FAIL_MOVEMENT", "Cannot reach a navigable point");
                    yield break;
                }
                if (state == "attack") rig.PlayAttack();
                // Observe the actual animated state after RiftBootstrap.LateUpdate.
                for (int frame = 0; frame < 4; frame++) yield return null;
                if (state == "idle")
                    report.hasIdle = rig.CurrentClip.StartsWith("idle", StringComparison.OrdinalIgnoreCase);
                if (state == "run") report.hasRun = rig.CurrentClip == "run";
                if (state == "attack") report.hasAttack = rig.CurrentClip == "attack1";
                yield return new WaitForEndOfFrame();
                string fileName = "native-" + state + ".png";
                ScreenCapture.CaptureScreenshot(Path.Combine(evidence, fileName));
                names.Add(fileName);
                yield return null;
            }
            report.screenshots = names.ToArray();
            report.screenshotCount = names.Count;

            float total = 0, worst = 0;
            for (int frame = 0; frame < 120; frame++)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                if (dt > 0)
                {
                    total += dt;
                    worst = Mathf.Max(worst, dt);
                    report.frames++;
                }
            }
            report.fpsAverage = total > 0 ? report.frames / total : 0;
            report.worstFrameMilliseconds = worst * 1000f;
            // Screenshot writes are asynchronous; do not close the Player prematurely.
            float waitStart = Time.realtimeSinceStartup;
            bool ready = false;
            while (Time.realtimeSinceStartup - waitStart < 15f)
            {
                ready = true;
                foreach (string name in names)
                {
                    string file = Path.Combine(evidence, name);
                    if (!File.Exists(file) || new FileInfo(file).Length < 16384) ready = false;
                }
                if (ready) break;
                yield return null;
            }
            bool passed = ready && report.hasIdle && report.hasRun && report.hasAttack &&
                report.frames >= 100 && report.fpsAverage > 0;
            Finish(report, evidence, passed ? "CAPTURED_REQUIRES_VISUAL_REVIEW" : "FAIL_EVIDENCE",
                passed ? "" : "Screenshot, movement, animation or frame validation failed");
        }
    }
}
