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
        // The old Player used orthographicSize=520 and a height-following
        // camera, which distorted the scale and framing compared to LoL.
        // Keep the old scene field for compatibility, but DO NOT use it.
        public float cameraOrthographicSize = 520f;
        public float cameraPitch = NativeRiftCameraProfile.DefaultPitch;
        public float cameraYaw = NativeRiftCameraProfile.DefaultYaw;
        public bool CameraLocked { get; private set; } = true;
        public bool CameraInputEnabled { get; set; }
        public float CameraDistance { get; private set; } =
            NativeRiftCameraProfile.DefaultDistance;
        Vector2 freeCameraFocus;
        bool freeCameraFocusSet;

        public string CameraStatus =>
            (CameraLocked ? "LOCKED" : "FREE") + " / " +
            Mathf.RoundToInt(cameraPitch) + "° TILT / " +
            Mathf.RoundToInt(cameraYaw) + "° YAW";


        Camera sceneCamera;
        RiftGrid nav;
        Vector2 location;
        Vector2[] route = Array.Empty<Vector2>();
        int waypoint;
        GarenRigRuntime heroRig;
        GarenRigRuntime garenBaseline;
        GameObject playerTacticalMarker;
        Material playerMarkerMaterial;
        string playerHeroName = "Garen";
        Vector2 previousLocation;
        GameObject loadedMap;
        GltfImport mapImport;
        Material[] retainedGltfFastMaterials;
        bool ready;
        public bool IsReady => ready;
        public bool HasLoadFailed => !ready && !string.Equals(problem, "Initializing 3D Rift...", StringComparison.Ordinal);
        public string LoadProblem => problem;
        public GarenRigRuntime HeroRig => heroRig;
        public string PlayerHeroName => playerHeroName;
        public bool PlayerHasOriginalRig => heroRig != null;
        public bool PlayerHasTacticalMarker =>
            playerTacticalMarker && playerTacticalMarker.activeInHierarchy;
        public GameObject LoadedMap => loadedMap;
        public RiftGrid Navigation => nav;
        public NativeCampCatalog CampCatalog { get; private set; }
        public NativeJunglePlayArea JungleArea { get; private set; }
        public bool JungleOnlyActive => nav != null && nav.IsPlayAreaRestricted;

        public bool SetJungleOnly(bool enabled)
        {
            if (!ready || nav == null || (enabled && JungleArea == null))
                return false;
            nav.SetPlayArea(enabled ? JungleArea.SnapshotMask() : null);
            StopMovement();
            Debug.Log("NATIVE_JUNGLE_BOUNDARY: " + (enabled ? "ENABLED" : "DISABLED") +
                      " regionCells=" + (JungleArea?.AllowedCellCount ?? 0));
            return true;
        }

        public NativeSpawnCalibration SpawnCalibration { get; private set; }
        public NativeNeutralMonsterWorld NeutralMonsters { get; private set; }
        public NativeNeutralMonsterScene NeutralScene => neutralScene;
        NativeNeutralMonsterScene neutralScene;
        public Vector2 PlayerPosition => location;
        // Observer-only camera follow. Does not move the locally controlled
        // original Garen rig or grant any other participant vision.
        public Vector2? CameraFollowOverride { get; set; }
        public bool ExternalGameplayControl { get; set; }
        public bool ExternalMoveEnabled { get; set; } = true;
        public float ExternalMoveMultiplier { get; set; } = 1f;
        public bool PlayerMoving => waypoint < route.Length;
        string problem = "Initializing 3D Rift...";

        static readonly string[] RequiredGltfShaderNames = {
            "Shader Graphs/glTF-unlit",
            "Shader Graphs/glTF-pbrMetallicRoughness"
        };

        void PreloadGltfFastShaders()
        {
            retainedGltfFastMaterials = Resources.LoadAll<Material>("Native/GLTFast");
            foreach (string shaderName in RequiredGltfShaderNames)
            {
                bool found = false;
                foreach (var material in retainedGltfFastMaterials)
                    if (material && material.shader && material.shader.name == shaderName && material.shader.isSupported)
                        found = true;
                if (!found)
                    throw new InvalidDataException("Required glTFast Player shader is not retained/supported: " + shaderName);
            }

            bool pbrAlphaMask = false, pbrAlphaBlend = false;
            bool unlitAlphaMask = false, unlitAlphaBlend = false;
            foreach (var material in retainedGltfFastMaterials)
            {
                if (!material || !material.shader || !material.shader.isSupported) continue;
                bool mask = material.IsKeywordEnabled("_ALPHATEST_ON") &&
                    material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") >= 0.5f;
                bool blend = material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");
                string shaderName = material.shader.name;
                if (shaderName == "Shader Graphs/glTF-pbrMetallicRoughness")
                {
                    pbrAlphaMask |= mask;
                    pbrAlphaBlend |= blend;
                }
                else if (shaderName == "Shader Graphs/glTF-unlit")
                {
                    unlitAlphaMask |= mask;
                    unlitAlphaBlend |= blend;
                }
            }
            if (!pbrAlphaMask || !pbrAlphaBlend || !unlitAlphaMask || !unlitAlphaBlend)
                throw new InvalidDataException(
                    "Required glTFast alpha variants are not retained in the Player: " +
                    "pbrMask=" + pbrAlphaMask + " pbrBlend=" + pbrAlphaBlend +
                    " unlitMask=" + unlitAlphaMask + " unlitBlend=" + unlitAlphaBlend);

            Debug.Log("NATIVE_GLTF_SHADERS_PRELOADED: materials=" + retainedGltfFastMaterials.Length);
            Debug.Log("NATIVE_GLTF_ALPHA_VARIANTS_PRELOADED: pbrMask=true pbrBlend=true unlitMask=true unlitBlend=true");
        }

        static int ValidateMapMaterials(GameObject map)
        {
            int rendererCount = 0;
            int materialCount = 0;
            int alphaMaskCount = 0;
            int alphaBlendCount = 0;
            int doubleSidedCount = 0;

            foreach (var renderer in map.GetComponentsInChildren<Renderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var skinned = renderer as SkinnedMeshRenderer;
                if ((!filter || !filter.sharedMesh) && (!skinned || !skinned.sharedMesh)) continue;
                rendererCount++;

                var materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                    throw new InvalidDataException("Rift mesh has no material after glTFast import: " + renderer.name);

                foreach (var material in materials)
                {
                    if (!material || !material.shader || !material.shader.isSupported ||
                        material.shader.name == "Hidden/InternalErrorShader")
                        throw new InvalidDataException("Rift mesh has a missing/unsupported shader: " + renderer.name);

                    bool alphaMask = material.IsKeywordEnabled("_ALPHATEST_ON");
                    bool alphaBlend = material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");
                    if (alphaMask)
                    {
                        alphaMaskCount++;
                        if (!material.HasProperty("_AlphaClip") ||
                            material.GetFloat("_AlphaClip") < 0.5f ||
                            !material.HasProperty("alphaCutoff"))
                            throw new InvalidDataException(
                                "Rift MASK material is missing its Alpha Clip/Cutoff state: " +
                                renderer.name + "/" + material.name);
                    }
                    if (alphaBlend)
                    {
                        alphaBlendCount++;
                        if (material.renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent)
                            throw new InvalidDataException(
                                "Rift BLEND material is not in the transparent render queue: " +
                                renderer.name + "/" + material.name);
                    }
                    if (material.HasProperty("_Cull") &&
                        Mathf.RoundToInt(material.GetFloat("_Cull")) == 0)
                        doubleSidedCount++;

                    materialCount++;
                }
            }

            if (rendererCount == 0 || materialCount == 0)
                throw new InvalidDataException("Rift GLB instantiated without renderable mesh materials");

            Debug.Log("NATIVE_MAP_ALPHA_MATERIAL_AUDIT: mask=" + alphaMaskCount +
                      " blend=" + alphaBlendCount + " doubleSided=" + doubleSidedCount);
            Debug.Log("NATIVE_MAP_MATERIAL_AUDIT_PASS: renderers=" + rendererCount +
                      " materials=" + materialCount);
            return rendererCount;
        }

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
                string spawnPath = Path.Combine(root, "Rift", "spawns.json");
                if (File.Exists(spawnPath))
                {
                    try
                    {
                        SpawnCalibration = NativeSpawnCalibration.LoadVerified(spawnPath, nav);
                        Debug.Log("NATIVE_VERIFIED_SPAWNS_READY: " +
                                  SpawnCalibration.VerificationSource);
                    }
                    catch (Exception badSpawn)
                    {
                        SpawnCalibration = null;
                        Debug.LogWarning("NATIVE_VERIFIED_SPAWNS_UNAVAILABLE: " + badSpawn.Message);
                    }
                }
                string campPath = Path.Combine(root, "Rift", "camps.json");
                if (File.Exists(campPath))
                {
                    try
                    {
                        CampCatalog = NativeCampCatalog.LoadVerified(campPath, nav);
                        Debug.Log("NATIVE_CAMP_CATALOG_READY: " + CampCatalog.Camps.Count + " verified locations");
                    }
                    catch (Exception invalidCamp)
                    {
                        CampCatalog = null;
                        Debug.LogWarning("NATIVE_CAMP_CATALOG_UNAVAILABLE: " + invalidCamp.Message);
                    }
                }
                if (CampCatalog != null)
                    JungleArea = NativeJunglePlayArea.Build(nav, CampCatalog);
                if (!nav.TryNearest(initialPosition, out location))
                    throw new InvalidDataException("Spawn is outside the original lower-jungle navigation region");
                loadedMap = new GameObject("ORIGINAL_RIFT_3D_Map11");
                loadedMap.transform.SetParent(transform, false);
                loadedMap.transform.localRotation = Quaternion.Euler(0, mapYawCorrection, 0);
                loadedMap.transform.localScale = Vector3.one * worldScale;
                // Load the retained Shader Graph materials before glTFast creates runtime materials.
                PreloadGltfFastShaders();
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
                ValidateMapMaterials(loadedMap);
                // A real skinned original hero is a hard requirement for phase 2.
                // Do not silently fall back to the old cyan diagnostic disc.
                string heroPath = Path.Combine(root, "Champions", "Garen");
                heroRig = GarenRigRuntime.Load(transform, heroPath);
                garenBaseline = heroRig;
                previousLocation = location;
                heroRig.transform.position = ToScene(location);
                // Only verified camp coordinates may create monster entities.
                // No guessed camps and no false original monster models.
                NeutralMonsters = new NativeNeutralMonsterWorld(nav, CampCatalog);
                if (NeutralMonsters.HasVerifiedCamps)
                {
                    var neutralRoot = new GameObject("NATIVE_VERIFIED_NEUTRAL_TARGETS_DEBUG_ART");
                    neutralRoot.transform.SetParent(transform, false);
                    neutralScene = neutralRoot.AddComponent<NativeNeutralMonsterScene>();
                    neutralScene.Initialize(NeutralMonsters, nav);
                    bool requireOriginalBlue = Array.IndexOf(
                        Environment.GetCommandLineArgs(),
                        "-hextechRequireOriginalBlueArt") >= 0;
                    await neutralScene.LoadOriginalBlueAsync(
                        Path.Combine(root, "Neutrals"), requireOriginalBlue);
                }
                else Debug.Log("NATIVE_NEUTRALS_WAITING_FOR_VERIFIED_CAMP_COORDINATES");
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
        /// <summary>
        /// Camera-only calibration and free panning. Never translates the
        /// actual Map11, navigation, vision grids, hit targets or champions.
        /// </summary>
        public void ResetCameraForMatch()
        {
            CameraLocked = true;
            CameraInputEnabled = true;
            CameraDistance = NativeRiftCameraProfile.DefaultDistance;
            cameraPitch = NativeRiftCameraProfile.DefaultPitch;
            cameraYaw = NativeRiftCameraProfile.DefaultYaw;
            freeCameraFocus = location;
            freeCameraFocusSet = true;
            Debug.Log("NATIVE_RIFT_CAMERA_RESET: perspective=true FOV=" +
                      NativeRiftCameraProfile.FieldOfView + " pitch=" +
                      cameraPitch + " yaw=" + cameraYaw +
                      " distance=" + CameraDistance);
        }

        void TickCameraInput()
        {
            if (!CameraInputEnabled) return;
            if (Input.GetKeyDown(KeyCode.Y))
            {
                CameraLocked = !CameraLocked;
                freeCameraFocus = CameraFollowOverride ?? location;
                freeCameraFocusSet = true;
            }
            if (Input.GetKeyDown(KeyCode.Alpha0))
            {
                cameraPitch = NativeRiftCameraProfile.DefaultPitch;
                cameraYaw = NativeRiftCameraProfile.DefaultYaw;
                CameraDistance = NativeRiftCameraProfile.DefaultDistance;
                CameraLocked = true;
            }
            if (Input.GetKeyDown(KeyCode.LeftBracket))
                cameraYaw = NativeRiftCameraProfile.WrapYaw(cameraYaw - 15f);
            if (Input.GetKeyDown(KeyCode.RightBracket))
                cameraYaw = NativeRiftCameraProfile.WrapYaw(cameraYaw + 15f);
            if (Input.GetKeyDown(KeyCode.PageUp))
                cameraPitch = NativeRiftCameraProfile.ClampPitch(cameraPitch + 3f);
            if (Input.GetKeyDown(KeyCode.PageDown))
                cameraPitch = NativeRiftCameraProfile.ClampPitch(cameraPitch - 3f);

            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > .0001f)
                CameraDistance = NativeRiftCameraProfile.ClampDistance(
                    CameraDistance - wheel * 1200f);

            if (!CameraLocked && !CameraFollowOverride.HasValue)
            {
                if (!freeCameraFocusSet) { freeCameraFocus = location; freeCameraFocusSet = true; }
                float h = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) -
                          (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
                float v = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) -
                          (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
                if (Mathf.Abs(h) + Mathf.Abs(v) > 0)
                {
                    Vector2 direction = new Vector2(h, v).normalized;
                    freeCameraFocus += NativeRiftCameraProfile.PanPlane(cameraYaw,
                        direction) * NativeRiftCameraProfile.PanUnitsPerSecond *
                        Time.unscaledDeltaTime;
                    freeCameraFocus.x = Mathf.Clamp(freeCameraFocus.x,
                        RiftGrid.MinX + 200f,
                        RiftGrid.MinX + RiftGrid.Width * RiftGrid.CellSize - 200f);
                    freeCameraFocus.y = Mathf.Clamp(freeCameraFocus.y,
                        RiftGrid.MinZ + 200f,
                        RiftGrid.MinZ + RiftGrid.Height * RiftGrid.CellSize - 200f);
                }
            }
        }

        void LateUpdate()
        {
            if (!ready) return;
            if (!ExternalGameplayControl)
            {
                if (Input.GetMouseButtonDown(1)) SetDestination();
                if (Input.GetKeyDown(KeyCode.A) && heroRig) heroRig.PlayAttack();
            }
            if (waypoint < route.Length && (!ExternalGameplayControl || ExternalMoveEnabled))
            {
                float delta = moveSpeed * (ExternalGameplayControl ? ExternalMoveMultiplier : 1f) * Time.deltaTime;
                while (delta > 0 && waypoint < route.Length)
                {
                    Vector2 to = route[waypoint] - location;
                    float distance = to.magnitude;
                    Vector2 next = distance <= delta ? route[waypoint] :
                        location + to.normalized * delta;
                    if (nav.IsPlayAreaRestricted &&
                        (!nav.CanWalk(next) || !nav.LineClear(location, next)))
                    {
                        Debug.Log("NATIVE_JUNGLE_BOUNDARY_BLOCK: actor=human from=" +
                                  location + " to=" + next);
                        StopMovement();
                        break;
                    }
                    if (distance <= delta) { location = route[waypoint++]; delta -= distance; }
                    else { location = next; delta = 0; }
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
            }
            if (playerTacticalMarker)
                playerTacticalMarker.transform.position = at;
            previousLocation = location;
            TickCameraInput();
            // Classic Rift camera: perspective FOV 40 and 56-degree downward
            // pitch from a constant y=0 focus plane. This is intentionally
            // independent of the actual terrain/hero height, eliminating
            // vertical wobble while crossing slopes and river elevations.
            // Default yaw=0 retains verified original Map11 X/Z alignment;
            // bracket keys allow visual yaw calibration without ever rotating
            // map geometry or the authoritative nav grid.
            Vector2 cameraCenter = CameraFollowOverride ??
                (CameraLocked || !CameraInputEnabled ||
                 Input.GetKey(KeyCode.Space) || !freeCameraFocusSet ?
                 location : freeCameraFocus);
            Vector3 focus = NativeRiftCameraProfile.FocusOnMapPlane(cameraCenter);
            var rotation = NativeRiftCameraProfile.Rotation(cameraPitch, cameraYaw);
            sceneCamera.transform.SetPositionAndRotation(
                NativeRiftCameraProfile.CameraPosition(focus, cameraPitch,
                                                       cameraYaw, CameraDistance),
                rotation);
            sceneCamera.orthographic = false;
            sceneCamera.fieldOfView = NativeRiftCameraProfile.FieldOfView;
            sceneCamera.nearClipPlane = .3f;
            sceneCamera.farClipPlane = 6500f;
        }
        /// <summary>
        /// Change the locally controlled hero. If authorized original art is not
        /// installed, display a clearly named tactical ring, NEVER Garen in
        /// another hero's place. The gameplay identity remains the chosen hero.
        /// </summary>
        public bool SelectPlayerChampion(string chosen)
        {
            if (!ready || !NativePlayerRoster.IsSelectable(chosen)) return false;
            chosen = NativePlayerRoster.CanonicalHero(chosen);
            if (string.Equals(playerHeroName, chosen, StringComparison.Ordinal))
                return true;
            if (heroRig && heroRig != garenBaseline)
            {
                heroRig.gameObject.SetActive(false);
                Destroy(heroRig.gameObject);
            }
            if (playerTacticalMarker)
            {
                playerTacticalMarker.SetActive(false);
                Destroy(playerTacticalMarker);
            }
            playerTacticalMarker = null;
            if (playerMarkerMaterial) Destroy(playerMarkerMaterial);
            playerMarkerMaterial = null;
            garenBaseline.gameObject.SetActive(false);
            heroRig = null;
            playerHeroName = chosen;
            StopMovement();
            if (chosen == "Garen")
            {
                heroRig = garenBaseline;
                garenBaseline.gameObject.SetActive(true);
                heroRig.transform.position = ToScene(location);
                Debug.Log("NATIVE_HUMAN_ORIGINAL_HERO_READY: Garen");
                return true;
            }
            string folder = Path.Combine(Application.streamingAssetsPath,
                                         "Champions", chosen);
            if (GarenRigRuntime.SourceFilesPresent(folder, chosen))
            {
                try
                {
                    heroRig = GarenRigRuntime.LoadChampion(transform, folder, chosen);
                    heroRig.transform.position = ToScene(location);
                    Debug.Log("NATIVE_HUMAN_ORIGINAL_HERO_READY: " + chosen);
                    return true;
                }
                catch (Exception invalid)
                {
                    Debug.LogError("NATIVE_HUMAN_ORIGINAL_HERO_REJECTED: " +
                                   chosen + " " + invalid);
                }
            }
            var template = Resources.Load<Material>("Native/GarenLit");
            if (!template) throw new InvalidDataException(
                "Native/GarenLit material must exist for the tactical source warning");
            playerTacticalMarker = new GameObject(
                "HUMAN_TACTICAL_MARKER_" + chosen + "_ORIGINAL_ART_UNAVAILABLE");
            var ring = playerTacticalMarker.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = 32;
            ring.startWidth = 14f;
            ring.endWidth = 14f;
            for (int i = 0; i < 32; i++)
            {
                float a = 2f * Mathf.PI * i / 32;
                ring.SetPosition(i, new Vector3(65f * Mathf.Cos(a), 18f,
                                                65f * Mathf.Sin(a)));
            }
            playerMarkerMaterial = new Material(template);
            playerMarkerMaterial.name = "HUMAN_TACTICAL_ONLY_" + chosen;
            playerMarkerMaterial.SetColor("_BaseColor", new Color(.12f, .85f, 1f));
            ring.sharedMaterial = playerMarkerMaterial;
            playerTacticalMarker.transform.position = ToScene(location);
            Debug.LogWarning("NATIVE_HUMAN_ORIGINAL_HERO_ABSENT: " + chosen +
                             " tactical ring, no substituted champion art");
            return true;
        }

        public bool TryGetWorldPointFromScreen(Vector2 screen, out Vector2 point)
        {
            point = default;
            if (!ready || !sceneCamera) return false;
            var ray = sceneCamera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, ToScene(location));
            if (!plane.Raycast(ray, out float distance)) return false;
            Vector3 hit = ray.GetPoint(distance);
            point = new Vector2(hit.x, hit.z);
            return true;
        }

        public bool TryTeleportTo(Vector2 desired)
        {
            if (!ready || (JungleOnlyActive && !JungleArea.Contains(desired)) ||
                !nav.TryNearest(desired, out var destination, 65)) return false;
            location = destination;
            previousLocation = destination;
            route = Array.Empty<Vector2>();
            waypoint = 0;
            if (heroRig) heroRig.transform.position = ToScene(location);
            if (playerTacticalMarker) playerTacticalMarker.transform.position = ToScene(location);
            return true;
        }

        /// <summary>
        /// Authoritative continuous position update for native forced-motion
        /// states (e.g. Blitz hook pull). Unlike TryTeleportTo this preserves
        /// the exact interpolated point instead of snapping to a nearby cell.
        /// The caller must validate the full swept path before starting.
        /// </summary>
        public bool TrySetExternalPosition(Vector2 desired)
        {
            if (!ready || nav == null || !nav.CanWalk(desired) ||
                (JungleOnlyActive && !nav.LineClear(location, desired))) return false;
            location = desired;
            previousLocation = desired;
            route = Array.Empty<Vector2>();
            waypoint = 0;
            if (heroRig) heroRig.transform.position = ToScene(location);
            if (playerTacticalMarker) playerTacticalMarker.transform.position = ToScene(location);
            return true;
        }

        public void StopMovement()
        {
            route = Array.Empty<Vector2>();
            waypoint = 0;
        }

        public bool TryNavigateTo(Vector2 target)
        {
            if (!ready || (ExternalGameplayControl && !ExternalMoveEnabled) ||
                (JungleOnlyActive && !JungleArea.Contains(target))) return false;
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
        static bool DebugHudRequested()
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-hextechDebugHud") >= 0;
        }

        void OnGUI()
        {
            // Release Player and automated visual captures are clean by default.
            // The diagnostic overlay is available only through an explicit flag.
            if (!DebugHudRequested()) return;

            GUI.color = Color.white;
            var match = GetComponent<NativeMatchController>();
            if (ExternalGameplayControl || (match && match.enabled)) return;
            GUI.Label(new Rect(18, 16, 1100, 32), "HEXTECH SEEKERS — NATIVE / PHASE 2 · ORIGINAL 3D RIFT");
            if (!ready) { GUI.Label(new Rect(18, 44, Screen.width - 36, 90), "Not ready: " + problem); return; }
            GUI.Label(new Rect(18, 44, 900, 30), $"RMB move | World x={location.x:F0} z={location.y:F0} | Path nodes={route.Length - waypoint}");
            GUI.Label(new Rect(18, 72, 950, 30), "Original skinned Garen | RMB move | A attack | idle/run/attack1 clips");
        }
        void OnDestroy()
        {
            if (playerMarkerMaterial) Destroy(playerMarkerMaterial);
            if (neutralScene) Destroy(neutralScene.gameObject);
            if (loadedMap) Destroy(loadedMap);
            mapImport?.Dispose();
        }
    }
}