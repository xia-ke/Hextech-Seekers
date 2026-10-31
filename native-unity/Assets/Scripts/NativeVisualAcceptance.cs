using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>GPU-based scene evidence. Executed only with -hextechVisualQA.</summary>
    public sealed class NativeVisualAcceptance : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public string status, unityVersion, device, graphicsApi, scene, hero;
            public string[] screenshots;
            public int screenshotCount, frames;
            public float fpsAverage, worstFrameMilliseconds;
            public bool originalSceneLoaded, skinnedModelExists, hasIdle, hasRun, hasAttack;
        }

        void Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hextechVisualQA") >= 0)
                StartCoroutine(Capture());
        }

        IEnumerator Capture()
        {
            var bootstrap = GetComponent<RiftBootstrap>();
            if (!bootstrap) { Debug.LogError("NATIVE_QA_FAIL: RiftBootstrap absent"); yield break; }
            string evidence = Path.Combine(Application.persistentDataPath, "HextechVisualQA");
            Directory.CreateDirectory(evidence);
            var report = new Report {
                status = "INCOMPLETE", unityVersion = Application.unityVersion,
                device = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
            };
            var names = new List<string>();
            for (int i = 0; i < 900 && !bootstrap.IsReady; i++) yield return null;
            report.originalSceneLoaded = bootstrap.IsReady;
            var rig = bootstrap.HeroRig;
            report.skinnedModelExists = rig && rig.GetComponent<SkinnedMeshRenderer>() &&
                rig.GetComponent<SkinnedMeshRenderer>().sharedMesh;
            if (!report.originalSceneLoaded || !report.skinnedModelExists)
            {
                report.status = "FAIL_LOAD";
                File.WriteAllText(Path.Combine(evidence, "result.json"), JsonUtility.ToJson(report, true));
                Debug.LogError("NATIVE_QA_FAIL: original 3D Rift/Garen failed to load");
                yield break;
            }

            report.hero = "Garen";
            string[] clips = { "idle", "run", "attack" };
            for (int j = 0; j < clips.Length; j++)
            {
                string state = clips[j];
                if (state == "run") bootstrap.VisualQAMove(500f);
                if (state == "attack") rig.PlayAttack();
                for (int f = 0; f < 30; f++) yield return null;
                if (state == "idle") report.hasIdle = rig.CurrentClip.StartsWith("idle", StringComparison.OrdinalIgnoreCase);
                if (state == "run") report.hasRun = rig.CurrentClip == "run";
                if (state == "attack") report.hasAttack = rig.CurrentClip == "attack1";
                string name = "native-" + state + ".png";
                ScreenCapture.CaptureScreenshot(Path.Combine(evidence, name));
                names.Add(name);
                // Let Unity complete the asynchronous screenshot write.
                yield return new WaitForEndOfFrame();
            }
            report.screenshots = names.ToArray();
            report.screenshotCount = names.Count;
            float total = 0, worst = 0;
            for (int frame = 0; frame < 180; frame++)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                if (dt > 0) { total += dt; worst = Mathf.Max(worst, dt); report.frames++; }
            }
            report.fpsAverage = total > 0 ? report.frames / total : 0;
            report.worstFrameMilliseconds = worst * 1000f;
            // Existence and image legibility must also be validated independently.
            for (int i = 0; i < 120; i++)
            {
                bool ready = true;
                foreach (string name in names)
                {
                    string file = Path.Combine(evidence, name);
                    if (!File.Exists(file) || new FileInfo(file).Length < 16384) ready = false;
                }
                if (ready) break;
                yield return null;
            }
            bool images = true;
            foreach (string name in names)
            {
                string file = Path.Combine(evidence, name);
                images &= File.Exists(file) && new FileInfo(file).Length >= 16384;
            }
            report.status = report.hasIdle && report.hasRun && report.hasAttack && images
                ? "CAPTURED_REQUIRES_VISUAL_REVIEW" : "FAIL_EVIDENCE";
            File.WriteAllText(Path.Combine(evidence, "result.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_QA_RESULT: " + report.status + " evidence=" + evidence +
                " FPS=" + report.fpsAverage.ToString("F1"));
        }
    }
}
