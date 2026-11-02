using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Explicit real-GPU diagnostic effect smoke, separate from the native
    /// gameplay and Rift/Garen animation acceptance. It exercises the Phase 10
    /// renderers on a real, loaded Map11 scene, saving inspectable screenshots.
    /// These are NOT original licensed champion VFX or proof of art fidelity.
    /// </summary>
    public sealed class NativePhase10VisualQA : MonoBehaviour
    {
        [Serializable]
        sealed class Report
        {
            public string status, reason, graphicsDevice;
            public bool mapLoaded, hardwareGraphics, hiddenFxInvisible;
            public int scenarios, screenshots, width, height;
            public string[] images;
        }

        static bool Requested() =>
            Array.IndexOf(Environment.GetCommandLineArgs(), "-hextechPhase10QA") >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Requested()) return;
            Application.runInBackground = true;
            var bootstrap = FindObjectOfType<RiftBootstrap>();
            if (!bootstrap)
            {
                Debug.LogError("PHASE10_QA_NO_BOOTSTRAP");
                return;
            }
            if (!bootstrap.GetComponent<NativePhase10VisualQA>())
                bootstrap.gameObject.AddComponent<NativePhase10VisualQA>();
        }

        static string EvidenceDirectory()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-hextechEvidenceDir")
                    return Path.GetFullPath(args[i + 1]);
            return Path.Combine(Application.persistentDataPath, "Phase10VisualQA");
        }

        RiftBootstrap rift;
        NativeCombatVfx fx;

        void Start()
        {
            if (!Requested()) return;
            rift = GetComponent<RiftBootstrap>();
            var menu = GetComponent<NativeMatchController>();
            if (menu) menu.enabled = false; // suppress role-selection GUI for screenshots
            StartCoroutine(Run());
        }

        static Vector2 SafeDirection(RiftGrid nav, Vector2 origin)
        {
            foreach (var direction in new[] {
                Vector2.right, Vector2.left, Vector2.up, Vector2.down,
                new Vector2(1f, 1f).normalized,
                new Vector2(-1f, 1f).normalized,
                new Vector2(1f, -1f).normalized,
                new Vector2(-1f, -1f).normalized
            })
            {
                Vector2 point = origin + direction * 220f;
                if (nav.CanWalk(point) && nav.LineClear(origin, point))
                    return direction;
            }
            throw new InvalidOperationException("No 220-unit clear path for Phase 10 FX at calibrated player spawn");
        }

        bool IsDebugRenderer(Renderer renderer)
        {
            for (Transform parent = renderer.transform;
                 parent && parent != fx.transform; parent = parent.parent)
                if (parent.name.StartsWith("DEBUG_", StringComparison.Ordinal))
                    return true;
            return false;
        }

        int VisibleFxRenderers()
        {
            int visible = 0;
            foreach (var renderer in fx.GetComponentsInChildren<Renderer>(true))
                if (IsDebugRenderer(renderer) &&
                    renderer.enabled && renderer.gameObject.activeInHierarchy)
                    visible++;
            return visible;
        }

        IEnumerator Run()
        {
            var output = EvidenceDirectory();
            Directory.CreateDirectory(output);
            var report = new Report {
                status = "FAIL",
                images = Array.Empty<string>(),
                graphicsDevice = SystemInfo.graphicsDeviceName,
                width = Screen.width, height = Screen.height
            };
            var images = new List<string>();
            IEnumerator worker = RunCore(report, images, output);
            while (true)
            {
                bool next;
                object yielded = null;
                try
                {
                    next = worker.MoveNext();
                    if (next) yielded = worker.Current;
                }
                catch (Exception failure)
                {
                    report.status = "FAIL";
                    report.reason = failure.ToString();
                    Debug.LogException(failure);
                    break;
                }
                if (!next) break;
                yield return yielded;
            }
            if (fx) fx.End();
            File.WriteAllText(Path.Combine(output, "phase10-visual-result.json"),
                JsonUtility.ToJson(report, true));
            Debug.Log("PHASE10_QA_RESULT: " + report.status +
                      " scenarios=" + report.scenarios +
                      " screenshots=" + report.screenshots +
                      " fog=" + report.hiddenFxInvisible);
            if (!Application.isEditor)
                Application.Quit(report.status == "CAPTURED_REQUIRES_VISUAL_REVIEW" ? 0 : 1);
        }

        IEnumerator RunCore(Report report, List<string> images, string output)
        {
                string name = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
                report.hardwareGraphics =
                    SystemInfo.graphicsDeviceType.ToString() != "Null" &&
                    !name.Contains("microsoft basic render") &&
                    !name.Contains("swiftshader") && !name.Contains("warp");
                if (!report.hardwareGraphics)
                    throw new InvalidOperationException("A real hardware GPU is required");
                float started = Time.realtimeSinceStartup;
                while (rift && !rift.IsReady && !rift.HasLoadFailed &&
                       Time.realtimeSinceStartup - started < 180f)
                    yield return null;
                if (!rift || !rift.IsReady)
                    throw new InvalidOperationException("Map11 not loaded: " +
                        (rift ? rift.LoadProblem : "missing bootstrap"));
                report.mapLoaded = true;

                var nav = rift.Navigation;
                Vector2 origin = rift.PlayerPosition;
                Vector2 direction = SafeDirection(nav, origin);
                rift.StopMovement();
                rift.CameraFollowOverride = origin;
                fx = GetComponent<NativeCombatVfx>();
                if (!fx) fx = gameObject.AddComponent<NativeCombatVfx>();

                // Each scenario resets materials and objects. A separate
                // frame is needed to finish Unity's deferred Destroy calls.
                for (int stage = 0; stage < 5; stage++)
                {
                    fx.End();
                    yield return null;
                    fx.Begin(nav);
                    string label;
                    switch (stage)
                    {
                        case 0:
                            label = "phase10-blitz-hook.png";
                            fx.SetHook(21, true, origin, origin + direction * 175f);
                            break;
                        case 1:
                            label = "phase10-yasuo-tornado.png";
                            fx.SetYasuoQ(22, true, origin,
                                origin + direction * 175f, true);
                            fx.Pulse(22, origin, "Yasuo Q Tornado");
                            break;
                        case 2:
                            label = "phase10-yasuo-windwall.png";
                            fx.SetWindWall(23, new NativeWindWall(
                                origin + direction * 145f,
                                new Vector2(-direction.y, direction.x), 100f,
                                30f));
                            break;
                        case 3:
                            label = "phase10-riven-dash.png";
                            var dash = new NativeDashMotion();
                            if (!dash.Begin(nav, origin, origin + direction * 200f))
                                throw new InvalidOperationException("Riven dash has no legal trajectory");
                            Vector2 midway = dash.Step(.1f);
                            if (!dash.Active || (midway - origin).sqrMagnitude < 1f)
                                throw new InvalidOperationException("Dash failed to interpolate");
                            fx.SetDash(24, true, origin, midway, "Riven");
                            fx.Pulse(24, midway, "Riven Q1");
                            break;
                        default:
                            label = "phase10-visible-impact.png";
                            fx.Impact(25, origin + direction * 125f,
                                      "Riven W", false);
                            if (fx.TransientCount != 0)
                                throw new InvalidOperationException(
                                    "Unseen target impact disclosed through a transient");
                            fx.Impact(25, origin + direction * 125f,
                                      "Riven W", true);
                            break;
                    }
                    fx.Tick(actor => true);
                    if (VisibleFxRenderers() < 1)
                        throw new InvalidOperationException("No visible Phase 10 renderer in " + label);
                    fx.Tick(actor => false);
                    if (VisibleFxRenderers() != 0)
                        throw new InvalidOperationException("Hidden Phase 10 FX leaked in " + label);
                    fx.Tick(actor => true);
                    if (VisibleFxRenderers() < 1)
                        throw new InvalidOperationException("Phase 10 FX did not restore on reveal: " + label);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(output, label));
                    images.Add(label);
                    report.scenarios++;
                    Debug.Log("PHASE10_QA_SCENARIO: " + label + " visible=true hidden=false");
                    yield return null;
                }

                report.hiddenFxInvisible = true;
                report.images = images.ToArray();
                report.screenshots = images.Count;
                float waitingSince = Time.realtimeSinceStartup;
                bool written = false;
                while (Time.realtimeSinceStartup - waitingSince < 20f)
                {
                    written = true;
                    foreach (var image in images)
                    {
                        string path = Path.Combine(output, image);
                        if (!File.Exists(path) || new FileInfo(path).Length < 16384)
                        {
                            written = false;
                            break;
                        }
                    }
                    if (written) break;
                    yield return null;
                }
                if (!written)
                    throw new InvalidOperationException("Phase 10 screenshot files missing or too small");
                if (report.scenarios != 5)
                    throw new InvalidOperationException("Not all Phase 10 visuals were exercised");
                report.status = "CAPTURED_REQUIRES_VISUAL_REVIEW";
        }
    }
}
