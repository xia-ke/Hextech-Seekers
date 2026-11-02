using System;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Private exploration state for exactly one observer. Never union
    /// visibility across living hiders. The GPU fog material uses the same
    /// authoritative visibility state as this independent navigation grid.
    ///
    /// Visible=0, previously explored=170, never seen=238.
    /// Terrain visibility does not treat a bush as an opaque wall; bushes
    /// conceal enemy units separately through NativeSight.Visible.
    /// </summary>
    public sealed class NativeVisionGrid
    {
        public const byte VisibleShade = 0;
        public const byte ExploredShade = 170;
        public const byte UnknownShade = 238;

        readonly RiftGrid nav;
        readonly byte[] explored;
        readonly byte[] visible;
        readonly int width;
        readonly int height;
        readonly float sightRadius;
        int visibleCells;
        public int VisibleCells => visibleCells;
        public int Width => width;
        public int Height => height;

        public NativeVisionGrid(RiftGrid navigation, float radius = NativeSight.BasicSightDistance)
        {
            nav = navigation ?? throw new ArgumentNullException(nameof(navigation));
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0)
                throw new ArgumentOutOfRangeException(nameof(radius));
            sightRadius = radius;
            width = RiftGrid.Width;
            height = RiftGrid.Height;
            explored = new byte[width * height];
            visible = new byte[width * height];
        }

        public void Reset()
        {
            Array.Clear(explored, 0, explored.Length);
            Array.Clear(visible, 0, visible.Length);
            visibleCells = 0;
        }

        public void ClearActiveSight()
        {
            Array.Clear(visible, 0, visible.Length);
            visibleCells = 0;
        }

        public void Reveal(Vector2 observer)
        {
            ClearActiveSight();
            if (!nav.CanWalk(observer)) return;
            Vector2Int center = nav.Cell(observer.x, observer.y);
            int steps = Mathf.CeilToInt(sightRadius / RiftGrid.CellSize) + 1;
            int minX = Mathf.Max(0, center.x - steps);
            int maxX = Mathf.Min(width - 1, center.x + steps);
            int minY = Mathf.Max(0, center.y - steps);
            int maxY = Mathf.Min(height - 1, center.y + steps);
            float radiusSq = sightRadius * sightRadius;
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 point = nav.Center(x, y);
                if ((point - observer).sqrMagnitude > radiusSq) continue;
                // Reveal the near face of a solid wall but never see *through*
                // the obstacle. Both map rendering and tactical AI depend on
                // the same source navigation/clearance geometry.
                if (!CanSeeTerrain(observer, point)) continue;
                int index = y * width + x;
                explored[index] = 1;
                visible[index] = 1;
                visibleCells++;
            }
        }

        bool CanSeeTerrain(Vector2 observer, Vector2 target)
        {
            float distance = Vector2.Distance(observer, target);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / (RiftGrid.CellSize * .45f)));
            // Target cell itself may be a wall, which should still be visible.
            // Interior samples must be navigable to prevent wall-penetrating
            // vision. The bush rule is intentionally not used for terrain.
            for (int i = 1; i < steps; i++)
                if (!nav.CanWalk(Vector2.Lerp(observer, target, i / (float)steps)))
                    return false;
            return true;
        }

        public byte ShadeAt(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return UnknownShade;
            int index = y * width + x;
            return visible[index] != 0 ? VisibleShade :
                   explored[index] != 0 ? ExploredShade : UnknownShade;
        }

        public bool IsCurrentlyVisible(Vector2 world)
        {
            Vector2Int c = nav.Cell(world.x, world.y);
            if (c.x < 0 || c.y < 0 || c.x >= width || c.y >= height) return false;
            return visible[c.y * width + c.x] != 0;
        }

        public bool WasExplored(Vector2 world)
        {
            Vector2Int c = nav.Cell(world.x, world.y);
            if (c.x < 0 || c.y < 0 || c.x >= width || c.y >= height) return false;
            return explored[c.y * width + c.x] != 0;
        }

        public void CopyFogColors(Color32[] buffer)
        {
            if (buffer == null || buffer.Length != width * height)
                throw new ArgumentException("Fog texture must match Rift grid dimensions", nameof(buffer));
            for (int i = 0; i < buffer.Length; i++)
            {
                byte opacity = visible[i] != 0 ? VisibleShade :
                               explored[i] != 0 ? ExploredShade : UnknownShade;
                buffer[i] = new Color32(opacity, opacity, opacity, 255);
            }
        }
    }
}
