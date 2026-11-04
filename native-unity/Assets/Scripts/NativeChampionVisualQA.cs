using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Phase 14: capture actual Windows GPU evidence for every selectable hero.
    /// Optional original art must be installed by its owner. Missing models are
    /// counted and photographed as tactical markers, never accepted as original.
    /// Technical model checks do NOT attest visual fidelity or redistribution.
    /// </summary>
    public sealed class NativeChampionVisualQA : MonoBehaviour
    {
        [Serializable]
        public sealed class HeroEvidence
        {
            public string hero, assetState, notes;
            public bool sourcePresent, realSkinnedMesh, markerShown;
            public bool idle, run, attack, pivotInView;
            public float viewportX, viewportY, localWidth, localHeight, localDepth;
            public int vertices, bones, triangles;
            public string shader;
            public string[] images = Array.Empty<string>();
        }

        [Serializable]
        public sealed class Report
        {
            public string status = "FAIL", reason = "", device;
            public bool hardwareGpu, mapLoaded, trialWatermarkVisible;
            public int selectableHeroes, originalRigCount, missingSourceCount, screenshots;
            public HeroEvidence[] heroes = Array.Empty<HeroEvidence>();
            public string[] images = Array.Empty<string>();
        }

        static bool Requested() =>
            Array.IndexOf(Environment.GetCommandLineArgs(), "-hextechChampionVisualQA") >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Requested()) return;
            Application.runInBackground = true;
            var bootstrap = FindObjectOfType<RiftBootstrap>();
            if (!bootstrap)
            {
                Debug.LogError("CHAMPION_ART_QA_BOOTSTRAP_MISSING");
                return;
            }
            if (!bootstrap.GetComponent<NativeChampionVisualQA>())
                bootstrap.gameObject.AddComponent<NativeChampionVisualQA>();
        }

        static string EvidenceDirectory()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-hextechEvidenceDir")
                    return Path.GetFullPath(args[i + 1]);
            return Path.Combine(Application.persistentDataPath, "ChampionVisualQA");
        }

        RiftBootstrap rift;

        void Start()
        {
            if (!Requested()) return;
            rift = GetComponent<RiftBootstrap>();
            var menu = GetComponent<NativeMatchController>();
            if (menu) menu.enabled = false; // no role-selection HUD in evidence
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            string output = EvidenceDirectory();
            Directory.CreateDirectory(output);
            var report = new Report {
                device = SystemInfo.graphicsDeviceName,
                trialWatermarkVisible = Watermark.IsVisible()
            };
            var captures = new List<string>();
            var heroes = new List<HeroEvidence>();
            IEnumerator worker = RunCore(report, heroes, captures, output);
            while (true)
            {
                bool more;
                object instruction = null;
                try
                {
                    more = worker.MoveNext();
                    if (more) instruction = worker.Current;
                }
                catch (Exception failure)
                {
                    report.status = "FAIL";
                    report.reason = failure.ToString();
                    Debug.LogException(failure);
                    break;
                }
                if (!more) break;
                yield return instruction;
            }
            report.heroes = heroes.ToArray();
            report.images = captures.ToArray();
            report.screenshots = captures.Count;
            File.WriteAllText(Path.Combine(output, "champion-visual-result.json"),
                              JsonUtility.ToJson(report, true));
            Debug.Log("CHAMPION_ART_QA_RESULT: " + report.status +
                      " originalRigs=" + report.originalRigCount +
                      " sourceMissing=" + report.missingSourceCount +
                      " images=" + report.screenshots +
                      (report.reason.Length > 0 ? " reason=" + report.reason : ""));
            if (!Application.isEditor)
                Application.Quit(report.status == "CAPTURED_REQUIRES_VISUAL_REVIEW" ? 0 : 1);
        }

        IEnumerator RunCore(Report report, List<HeroEvidence> heroes,
                            List<string> captures, string output)
        {
            string gpu = (report.device ?? "").ToLowerInvariant();
            report.hardwareGpu =
                SystemInfo.graphicsDeviceType.ToString() != "Null" &&
                !gpu.Contains("microsoft basic render") &&
                !gpu.Contains("swiftshader") && !gpu.Contains("warp");
            if (!report.hardwareGpu)
                throw new InvalidOperationException("Champion art QA requires a real GPU");

            float started = Time.realtimeSinceStartup;
            while (rift && !rift.IsReady && !rift.HasLoadFailed &&
                   Time.realtimeSinceStartup - started < 180f)
                yield return null;
            if (!rift || !rift.IsReady)
                throw new InvalidOperationException("Map11 unavailable: " +
                    (rift ? rift.LoadProblem : "no RiftBootstrap"));
            report.mapLoaded = true;

            Vector2 initial = rift.PlayerPosition;
            rift.ExternalGameplayControl = true;
            rift.ExternalMoveEnabled = true;
            var camera = Camera.main;
            if (!camera) throw new InvalidOperationException("No native scene camera");
            foreach (string hero in NativePlayerRoster.Heroes)
            {
                rift.StopMovement();
                if (!rift.TryTeleportTo(initial) || !rift.SelectPlayerChampion(hero))
                    throw new InvalidOperationException("Cannot select or stage " + hero);
                // Ensure the previous imported rig is actually destroyed.
                yield return null;
                yield return null;

                bool source = hero == "Garen" ||
                    GarenRigRuntime.SourceFilesPresent(
                        Path.Combine(Application.streamingAssetsPath, "Champions", hero), hero);
                var rig = rift.HeroRig;
                var item = new HeroEvidence {
                    hero = hero, sourcePresent = source,
                    realSkinnedMesh = rig != null,
                    markerShown = rift.PlayerHasTacticalMarker
                };
                item.assetState = NativeChampionRigAudit.AssetState(
                    source, item.realSkinnedMesh, item.markerShown);

                var entryImages = new List<string>();
                if (source)
                {
                    var geometry = NativeChampionRigAudit.Inspect(rig, hero);
                    item.vertices = geometry.vertices;
                    item.bones = geometry.bones;
                    item.triangles = geometry.triangles;
                    item.localWidth = geometry.localWidth;
                    item.localHeight = geometry.localHeight;
                    item.localDepth = geometry.localDepth;
                    item.shader = geometry.materialShader;
                    report.originalRigCount++;

                    rift.StopMovement();
                    for (int frame = 0; frame < 4; frame++) yield return null;
                    item.idle = rig.CurrentClip.StartsWith("idle", StringComparison.OrdinalIgnoreCase);
                    var position = camera.WorldToViewportPoint(rig.transform.position);
                    item.viewportX = position.x;
                    item.viewportY = position.y;
                    item.pivotInView = position.z > 0f &&
                        position.x >= 0f && position.x <= 1f &&
                        position.y >= 0f && position.y <= 1f;
                    if (!item.pivotInView || !item.idle)
                        throw new InvalidOperationException(
                            hero + ": idle clip or model origin is outside the camera view");
                    yield return new WaitForEndOfFrame();
                    Capture(output, hero, "idle", captures, entryImages);
                    yield return null;

                    if (!rift.VisualQAMove(500f))
                        throw new InvalidOperationException(
                            hero + ": cannot navigate on the real Map11 grid");
                    for (int frame = 0; frame < 4; frame++) yield return null;
                    item.run = rig.CurrentClip == "run";
                    if (!item.run)
                        throw new InvalidOperationException(hero + ": original run animation absent");
                    yield return new WaitForEndOfFrame();
                    Capture(output, hero, "run", captures, entryImages);
                    yield return null;

                    rift.StopMovement();
                    rift.PerformBasicAttack();
                    for (int frame = 0; frame < 4; frame++) yield return null;
                    item.attack = rig.CurrentClip == "attack1";
                    if (!item.attack)
                        throw new InvalidOperationException(hero + ": original attack clip failed");
                    yield return new WaitForEndOfFrame();
                    Capture(output, hero, "attack", captures, entryImages);
                    yield return null;

                    // Record scale for human art review. No arbitrary scaling,
                    // rotations or visual fidelity assumptions are invented.
                    item.notes = "SOURCE_LOADED_VISUAL_REVIEW_REQUIRED";
                }
                else
                {
                    report.missingSourceCount++;
                    item.notes = "ORIGINAL_MODEL_NOT_SUPPLIED_TACTICAL_MARKER_ONLY";
                    yield return new WaitForEndOfFrame();
                    Capture(output, hero, "missing-source-marker", captures, entryImages);
                    yield return null;
                }
                item.images = entryImages.ToArray();
                report.selectableHeroes++;
                heroes.Add(item);
                Debug.Log("CHAMPION_ART_QA_HERO: hero=" + hero +
                          " source=" + source + " state=" + item.assetState +
                          " vertices=" + item.vertices + " images=" + entryImages.Count);
            }

            if (report.selectableHeroes != 4 || report.originalRigCount < 1 ||
                report.originalRigCount + report.missingSourceCount != 4 ||
                captures.Count != report.originalRigCount * 3 + report.missingSourceCount)
                throw new InvalidOperationException("Original hero artwork inventory is incomplete");
            rift.StopMovement();

            // GPU screenshot writes are asynchronous. Wait for real PNG files,
            // not just calls to CaptureScreenshot.
            float screenshotStart = Time.realtimeSinceStartup;
            bool filesReady = false;
            while (Time.realtimeSinceStartup - screenshotStart < 25f)
            {
                filesReady = true;
                foreach (string name in captures)
                {
                    string path = Path.Combine(output, name);
                    if (!File.Exists(path) || new FileInfo(path).Length < 16384)
                    {
                        filesReady = false;
                        break;
                    }
                }
                if (filesReady) break;
                yield return null;
            }
            if (!filesReady)
                throw new InvalidOperationException("Original hero GPU PNG evidence was not written");
            report.status = "CAPTURED_REQUIRES_VISUAL_REVIEW";
        }

        static void Capture(string folder, string hero, string state,
                            List<string> all, List<string> own)
        {
            string name = "champion-" + hero.ToLowerInvariant() + "-" + state + ".png";
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, name));
            all.Add(name);
            own.Add(name);
        }
    }
}
