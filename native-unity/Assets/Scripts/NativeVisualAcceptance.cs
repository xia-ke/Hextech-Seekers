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
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hextechVisualQA") >= 0)
                StartCoroutine(Capture());
        }

        IEnumerator Capture()
        {
            string evidence = EvidenceDirectory();
            Directory.CreateDirectory(evidence);
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
            while (!bootstrap.IsReady && Time.realtimeSinceStartup - start < 180f)
                yield return null;
            report.originalSceneLoaded = bootstrap.IsReady;
            if (!report.originalSceneLoaded)
            {
                Finish(report, evidence, "FAIL_LOAD", "Original Rift GLB/hero not ready after 180 seconds");
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
