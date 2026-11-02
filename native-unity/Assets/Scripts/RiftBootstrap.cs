using System;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>Stage 1: real original 3D Rift + navigation/camera smoke test.
    /// Hero artwork is intentionally not replaced by another hero or a fake model.
    /// A visible cyan navigation ring is strictly a debug marker.</summary>
    public sealed class RiftBootstrap : MonoBehaviour
    {
        public string mapFile = "Rift/map-unity.glb";
        public string navigationFile = "Rift/navigation.bin";
        public Vector2 initialPosition = new Vector2(7765f, -4020f);
        public float moveSpeed = 340f;
        public float worldScale = 1f;
        public float mapYawCorrection = 180f;
        public float cameraOrthographicSize = 520f;
        public float cameraPitch = 55f;


        Camera sceneCamera;
        RiftGrid nav;
        Vector2 location;
        Vector2[] route = Array.Empty<Vector2>();
        int waypoint;
        GarenRigRuntime heroRig;
        Vector2 previousLocation;
        GameObject loadedMap;
        GltfImport mapImport;
        bool ready;
        public bool IsReady => ready;
        public bool HasLoadFailed => !ready && !string.Equals(problem, "Initializing 3D Rift...", StringComparison.Ordinal);
        public string LoadProblem => problem;
        public GarenRigRuntime HeroRig => heroRig;
        public GameObject LoadedMap => loadedMap;
        public RiftGrid Navigation => nav;
        public Vector2 PlayerPosition => location;
        public bool ExternalGameplayControl { get; set; }
        public bool ExternalMoveEnabled { get; set; } = true;
        public float ExternalMoveMultiplier { get; set; } = 1f;
        public bool PlayerMoving => waypoint < route.Length;
        string problem = "Initializing 3D Rift...";

        async void Start()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            sceneCamera = Camera.main;
            if (!sceneCamera) { problem = "Missing MainCamera"; Debug.LogError(problem); return; }
            try
            {
                string root = Application.streamingAssetsPath;
                string navPath = Path.Combine(root, navigationFile);
                string glbPath = Path.Combine(root, mapFile);
                if (!File.Exists(navPath) || !File.Exists(glbPath))
                    throw new FileNotFoundException("Run tools/prepare_assets.py before loading the Rift scene. Both original map.glb and navigation.bin are required.");
                nav = new RiftGrid(File.ReadAllBytes(navPath));
                if (!nav.TryNearest(initialPosition, out location))
                    throw new InvalidDataException("Spawn is outside the original lower-jungle navigation region");
                loadedMap = new GameObject("ORIGINAL_RIFT_3D_Map11");
                loadedMap.transform.SetParent(transform, false);
                loadedMap.transform.localRotation = Quaternion.Euler(0, mapYawCorrection, 0);
                loadedMap.transform.localScale = Vector3.one * worldScale;
                // glTFast's default logger suppresses detailed diagnostics. Forward
                // importer errors/warnings to Player.log for automated root-cause triage.
                mapImport = new GltfImport(logger: new ConsoleLogger());
                // Only import our verified, pinned GLB file, not a network model.
                var glbInfo = new FileInfo(glbPath);
                string uri = new Uri(glbInfo.FullName).AbsoluteUri;
                Debug.Log("NATIVE_MAP_LOAD_BEGIN: bytes=" + glbInfo.Length +
                          " uri=" + uri + " GPU=" + SystemInfo.graphicsDeviceName);
                bool loaded = await mapImport.Load(new Uri(uri));
                Debug.Log("NATIVE_MAP_LOAD_DONE: success=" + loaded);
                if (!loaded)
                    throw new InvalidDataException("Original Rift map.glb failed during glTFast Load; inspect preceding glTFast importer messages in Player.log");
                bool instantiated = await mapImport.InstantiateMainSceneAsync(loadedMap.transform);
                Debug.Log("NATIVE_MAP_INSTANTIATE_DONE: success=" + instantiated +
                          " renderers=" + loadedMap.GetComponentsInChildren<Renderer>(true).Length);
                if (!instantiated)
                    throw new InvalidDataException("Original Rift map.glb loaded but glTFast scene instantiation failed; inspect Player.log");
                // A real skinned original hero is a hard requirement for phase 2.
                // Do not silently fall back to the old cyan diagnostic disc.
                string heroPath = Path.Combine(root, "Champions", "Garen");
                heroRig = GarenRigRuntime.Load(transform, heroPath);
                previousLocation = location;
                heroRig.transform.position = ToScene(location);
                ready = true;
                problem = string.Empty;
                Debug.Log("Hextech Native Phase1: original GLB loaded; original navigation ready. Original Garen rig and three native animations loaded.");
            }
            catch (Exception e)
            {
                problem = e.Message;
                Debug.LogException(e);
            }
        }
        void LateUpdate()
        {
            if (!ready) return;
            if (!ExternalGameplayControl)
            {
                if (Input.GetMouseButtonDown(1)) SetDestination();
                if (Input.GetKeyDown(KeyCode.A)) heroRig.PlayAttack();
            }
            if (waypoint < route.Length && (!ExternalGameplayControl || ExternalMoveEnabled))
            {
                float delta = moveSpeed * (ExternalGameplayControl ? ExternalMoveMultiplier : 1f) * Time.deltaTime;
                while (delta > 0 && waypoint < route.Length)
                {
                    Vector2 to = route[waypoint] - location;
                    float distance = to.magnitude;
                    if (distance <= delta) { location = route[waypoint++]; delta -= distance; }
                    else { location += to.normalized * delta; delta = 0; }
                }
            }
            var at = ToScene(location);
            if (heroRig)
            {
                heroRig.transform.position = at;
                Vector2 delta = location - previousLocation;
                if (delta.sqrMagnitude > .0001f)
                    heroRig.transform.rotation = Quaternion.LookRotation(new Vector3(delta.x,0,delta.y), Vector3.up);
                heroRig.Tick(Time.deltaTime, waypoint < route.Length);
                previousLocation = location;
            }
            var forward = Quaternion.Euler(cameraPitch, 0, 0) * Vector3.forward;
            sceneCamera.transform.position = at - forward * 900f;
            sceneCamera.transform.rotation = Quaternion.Euler(cameraPitch, 0, 0);
            sceneCamera.orthographic = true;
            sceneCamera.orthographicSize = cameraOrthographicSize;
        }
        public bool TryTeleportTo(Vector2 desired)
        {
            if (!ready || !nav.TryNearest(desired, out var destination, 65)) return false;
            location = destination;
            previousLocation = destination;
            route = Array.Empty<Vector2>();
            waypoint = 0;
            heroRig.transform.position = ToScene(location);
            return true;
        }

        public void StopMovement()
        {
            route = Array.Empty<Vector2>();
            waypoint = 0;
        }

        public bool TryNavigateTo(Vector2 target)
        {
            if (!ready || (ExternalGameplayControl && !ExternalMoveEnabled)) return false;
            var result = new System.Collections.Generic.List<Vector2>();
            if (!nav.TryPath(location, target, result) || result.Count == 0) return false;
            route = result.ToArray();
            waypoint = 0;
            return true;
        }

        public bool TryNavigateFromScreen(Vector2 screen)
        {
            if (!ready || !sceneCamera) return false;
            var ray = sceneCamera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, ToScene(location));
            if (!plane.Raycast(ray, out float distance)) return false;
            var point = ray.GetPoint(distance);
            return TryNavigateTo(new Vector2(point.x, point.z));
        }

        public void PerformBasicAttack()
        {
            if (ready && heroRig) heroRig.PlayAttack();
        }

        public bool VisualQAMove(float distance)
        {
            if (!ready || nav == null || !nav.TryNearest(location + new Vector2(distance, 0), out var destination))
                return false;
            var result = new System.Collections.Generic.List<Vector2>();
            if (!nav.TryPath(location, destination, result) || result.Count == 0) return false;
            route = result.ToArray(); waypoint = 0;
            return true;
        }
        Vector3 ToScene(Vector2 p) => new Vector3(p.x, nav.HeightAt(p), p.y);
        void SetDestination()
        {
            TryNavigateFromScreen(Input.mousePosition);
        }
        void OnGUI()
        {
            GUI.color = Color.white;
            // Do not paint the old debug HUD over the playable role-selection
            // panel. Keep the debug HUD for the standalone visual-QA mode.
            var match = GetComponent<NativeMatchController>();
            if (ExternalGameplayControl || (match && match.enabled)) return;
            GUI.Label(new Rect(18, 16, 1100, 32), "HEXTECH SEEKERS — NATIVE / PHASE 2 · ORIGINAL 3D RIFT");
            if (!ready) { GUI.Label(new Rect(18, 44, Screen.width - 36, 90), "Not ready: " + problem); return; }
            GUI.Label(new Rect(18, 44, 900, 30), $"RMB move | World x={location.x:F0} z={location.y:F0} | Path nodes={route.Length - waypoint}");
            GUI.Label(new Rect(18, 72, 950, 30), "Original skinned Garen | RMB move | A attack | idle/run/attack1 clips");
        }
        void OnDestroy() { if (loadedMap) Destroy(loadedMap); mapImport?.Dispose(); }
    }
}