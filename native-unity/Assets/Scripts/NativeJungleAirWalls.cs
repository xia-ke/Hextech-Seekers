using System;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Invisible physical barriers on EVERY walkable boundary between the
    /// jungle play area and exterior Map11. Direct-transform actors still use
    /// the restricted RiftGrid as their authoritative swept movement gate.
    /// </summary>
    public static class NativeJungleAirWalls
    {
        public static GameObject Create(RiftGrid nav, NativeJunglePlayArea area,
                                        Transform parent, out int segments,
                                        out int exitEdges)
        {
            if (nav == null || area == null)
                throw new ArgumentNullException(nav == null ? nameof(nav) : nameof(area));
            var mask = area.SnapshotMask();
            var root = new GameObject("NATIVE_JUNGLE_EXIT_AIR_WALLS");
            root.transform.SetParent(parent, false);
            int segmentCount = 0, edgeCount = 0;
            const float thickness = 10f;
            const float wallHeight = 16000f;

            void Add(string name, Vector3 center, Vector3 size, int covered)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = center;
                var wall = go.AddComponent<BoxCollider>();
                wall.isTrigger = false;
                wall.size = size;
                segmentCount++;
                edgeCount += covered;
            }

            // X-facing seams. Merge consecutive rows into one static collider.
            for (int x = 0; x < RiftGrid.Width - 1; x++)
            {
                int begin = -1;
                for (int y = 0; y <= RiftGrid.Height; y++)
                {
                    bool edge = y < RiftGrid.Height &&
                                nav.OriginalCellWalkable(x, y) &&
                                nav.OriginalCellWalkable(x + 1, y) &&
                                mask[y * RiftGrid.Width + x] !=
                                mask[y * RiftGrid.Width + x + 1];
                    if (edge && begin < 0) begin = y;
                    if (!edge && begin >= 0)
                    {
                        int length = y - begin;
                        Add("EXIT_X_" + x + "_" + begin,
                            new Vector3(RiftGrid.MinX + (x + 1) * RiftGrid.CellSize,
                                        0f, RiftGrid.MinZ +
                                        (begin + length * .5f) * RiftGrid.CellSize),
                            new Vector3(thickness, wallHeight,
                                        length * RiftGrid.CellSize + thickness), length);
                        begin = -1;
                    }
                }
            }

            // Z-facing seams. Merge consecutive columns.
            for (int y = 0; y < RiftGrid.Height - 1; y++)
            {
                int begin = -1;
                for (int x = 0; x <= RiftGrid.Width; x++)
                {
                    bool edge = x < RiftGrid.Width &&
                                nav.OriginalCellWalkable(x, y) &&
                                nav.OriginalCellWalkable(x, y + 1) &&
                                mask[y * RiftGrid.Width + x] !=
                                mask[(y + 1) * RiftGrid.Width + x];
                    if (edge && begin < 0) begin = x;
                    if (!edge && begin >= 0)
                    {
                        int length = x - begin;
                        Add("EXIT_Z_" + y + "_" + begin,
                            new Vector3(RiftGrid.MinX +
                                        (begin + length * .5f) * RiftGrid.CellSize,
                                        0f, RiftGrid.MinZ + (y + 1) * RiftGrid.CellSize),
                            new Vector3(length * RiftGrid.CellSize + thickness,
                                        wallHeight, thickness), length);
                        begin = -1;
                    }
                }
            }
            if (segmentCount == 0)
            {
                UnityEngine.Object.Destroy(root);
                throw new InvalidOperationException("No traversable jungle exits to seal");
            }
            segments = segmentCount;
            exitEdges = edgeCount;
            Debug.Log("NATIVE_JUNGLE_AIRWALLS_READY: segments=" + segments +
                      " exitEdges=" + exitEdges + " invisible=true map11Unchanged=true");
            return root;
        }
    }
}
