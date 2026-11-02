using System;
using System.Collections.Generic;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// GPU world-depth fog composited over the original glTFast 3D map.
    /// One private explored/visible mask per participant: no hider vision
    /// union and no enemy position feed into a hider's camera.
    ///
    /// The shader reconstructs each opaque pixel's world position from the
    /// camera depth texture. No "flat 2D map" or shared full-map overlay.
    /// It intentionally does not claim pixel-perfect League wall/brush vision.
    /// </summary>
    public sealed class NativeFogOfWar : MonoBehaviour
    {
        public const float RefreshSeconds = .17f;
        const int FogQueue = 4500;
        readonly Dictionary<int, NativeVisionGrid> observers =
            new Dictionary<int, NativeVisionGrid>();
        Color32[] pixels;
        Texture2D mask;
        Mesh screen;
        Material material;
        GameObject quad;
        RiftGrid nav;
        Camera sceneCamera;
        float nextRefresh;
        bool running;

        public bool Running => running;
        public int ObserverCount => observers.Count;

        public void Begin(RiftGrid navigation, NativeMatchRules match, Camera camera)
        {
            if (navigation == null || match == null || !camera)
                throw new ArgumentException("A navigable Rift scene, match and camera are required");
            End();
            nav = navigation;
            sceneCamera = camera;
            observers.Clear();
            foreach (var person in match.Players)
                observers.Add(person.Id, new NativeVisionGrid(nav));
            pixels = new Color32[RiftGrid.Width * RiftGrid.Height];
            mask = new Texture2D(RiftGrid.Width, RiftGrid.Height, TextureFormat.RGBA32, false, true)
            {
                name = "Private Rift visibility map",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0
            };

            Shader fogShader = Resources.Load<Shader>("Native/WorldDepthFog");
            if (!fogShader || !fogShader.isSupported)
            {
                End();
                throw new InvalidOperationException(
                    "Native WorldDepthFog URP shader is missing/unsupported. " +
                    "Ensure Assets/Resources/Native/WorldDepthFog.shader is packaged.");
            }
            material = new Material(fogShader)
            {
                name = "Private 3D Rift Fog",
                renderQueue = FogQueue
            };
            material.SetTexture("_FogMask", mask);
            material.SetColor("_FogTint", new Color(.025f, .042f, .07f, .96f));
            material.SetVector("_MapMin", new Vector4(RiftGrid.MinX, RiftGrid.MinZ, 0, 0));
            material.SetVector("_MapSpan", new Vector4(
                RiftGrid.Width * RiftGrid.CellSize,
                RiftGrid.Height * RiftGrid.CellSize, 0, 0));

            // URP must produce a depth texture for the post-scene overlay.
            // The pipeline asset also has supportsCameraDepthTexture enabled
            // by HextechNativeSetup.CreateScene().
            sceneCamera.depthTextureMode |= DepthTextureMode.Depth;
            quad = new GameObject("NATIVE_PRIVATE_WORLD_DEPTH_FOG");
            quad.transform.SetParent(sceneCamera.transform, false);
            quad.transform.localPosition = Vector3.forward * 2f;
            screen = new Mesh { name = "Clip-space world-depth fog quad" };
            screen.vertices = new[] {
                new Vector3(-1, -1, 0), new Vector3(1, -1, 0),
                new Vector3(1, 1, 0), new Vector3(-1, 1, 0)
            };
            screen.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            // Frustum culling uses world bounds, not our clip-space vertex
            // override. Keep the quad registered from any Rift camera position.
            screen.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            quad.AddComponent<MeshFilter>().sharedMesh = screen;
            var renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 32760;

            running = true;
            nextRefresh = 0;
            FillBlank();
            Debug.Log("NATIVE_PRIVATE_FOG_READY: participants=" + observers.Count +
                      " resolution=" + mask.width + "x" + mask.height +
                      " depthAPI=" + SystemInfo.graphicsDeviceType);
        }

        void FillBlank()
        {
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(NativeVisionGrid.UnknownShade,
                    NativeVisionGrid.UnknownShade, NativeVisionGrid.UnknownShade, 255);
            mask.SetPixels32(pixels);
            mask.Apply(false, false);
        }

        /// <summary>
        /// Refresh all living participants independently. Present only the
        /// human player or a legally selected living ally in spectator mode.
        /// Null viewer means no active team vision and keeps the scene dark.
        /// </summary>
        public void UpdateView(NativeMatchRules match, int viewerId,
                               Func<int, Vector2> getPosition)
        {
            if (!running || match == null || getPosition == null ||
                Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;

            foreach (var person in match.Players)
            {
                NativeVisionGrid grid;
                if (!observers.TryGetValue(person.Id, out grid)) continue;
                if (person.Eliminated) grid.ClearActiveSight();
                else grid.Reveal(getPosition(person.Id));
            }

            NativeVisionGrid displayed;
            if (viewerId >= 0 && observers.TryGetValue(viewerId, out displayed))
            {
                displayed.CopyFogColors(pixels);
                Debug.Assert(displayed.Width == mask.width && displayed.Height == mask.height);
            }
            else
            {
                // Do not continue displaying the eliminated player's last
                // explored positions: no living allied viewer is available.
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(NativeVisionGrid.UnknownShade,
                        NativeVisionGrid.UnknownShade, NativeVisionGrid.UnknownShade, 255);
            }
            mask.SetPixels32(pixels);
            mask.Apply(false, false);
        }

        public void End()
        {
            running = false;
            observers.Clear();
            nav = null;
            sceneCamera = null;
            pixels = null;
            if (quad) Destroy(quad);
            if (screen) Destroy(screen);
            if (material) Destroy(material);
            if (mask) Destroy(mask);
            quad = null;
            screen = null;
            material = null;
            mask = null;
        }

        void OnDestroy() { End(); }
    }
}
