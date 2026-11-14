using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Hide-and-seek gameplay geofence: source-verified jungle camps, their
    /// shortest real Map11 walkable connections, and the nearest navigable
    /// mid-river grass. This is a curated MODE boundary, not an assertion
    /// that Riot supplies official jungle-region polygons in navigation.bin.
    /// Rendering/terrain remain untouched. All actors and skills share the
    /// restricted RiftGrid once an actual match is configured.
    /// </summary>
    public sealed class NativeJunglePlayArea
    {
        const float MidRiverX = 7500f;
        const float MidRiverZ = -7500f;
        const float MaxRiverBrushDistance = 2500f;
        const float CampRadius = 1280f;
        const float PathRadius = 820f;
        readonly bool[] allowed;
        readonly Vector2[] campAnchors;

        public Vector2 HunterRiverBrush { get; }
        public Vector2 ExcludedWalkable { get; }
        public int AllowedCellCount { get; }
        public int CampCount => campAnchors.Length;
        public int HunterBrushGroup { get; }

        NativeJunglePlayArea(bool[] mask, Vector2[] camps, Vector2 hunter,
                             Vector2 excluded, int count, int grassGroup)
        {
            allowed = mask;
            campAnchors = camps;
            HunterRiverBrush = hunter;
            ExcludedWalkable = excluded;
            AllowedCellCount = count;
            HunterBrushGroup = grassGroup;
        }

        public bool Contains(Vector2 position)
        {
            int x = Mathf.FloorToInt((position.x - RiftGrid.MinX) / RiftGrid.CellSize);
            int y = Mathf.FloorToInt((position.y - RiftGrid.MinZ) / RiftGrid.CellSize);
            return x >= 0 && y >= 0 && x < RiftGrid.Width &&
                   y < RiftGrid.Height && allowed[y * RiftGrid.Width + x];
        }

        public bool[] SnapshotMask() => (bool[])allowed.Clone();

        static bool TryFindMidRiverBrush(RiftGrid nav, out Vector2 anchor,
                                        out int grassGroup)
        {
            anchor = default;
            grassGroup = 0;
            float best = float.PositiveInfinity;
            var center = new Vector2(MidRiverX, MidRiverZ);
            for (int y = 2; y < RiftGrid.Height - 2; y++)
            for (int x = 2; x < RiftGrid.Width - 2; x++)
            {
                Vector2 cell = nav.Center(x, y);
                if (!nav.CanWalk(cell) || Vector2.Distance(cell, center) >
                    MaxRiverBrushDistance) continue;
                byte grass = nav.BushAt(cell);
                if (grass == 0) continue;
                // Favor a stable place INSIDE the original grass polygon;
                // not an exposed one-cell border that looks hidden from UI.
                int connected = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (nav.BushAt(nav.Center(x + dx, y + dy)) == grass)
                        connected++;
                if (connected < 5) continue;
                float score = Vector2.Distance(cell, center) -
                              connected * 65f;
                if (score >= best) continue;
                best = score;
                anchor = cell;
                grassGroup = grass;
            }
            return grassGroup != 0;
        }

        public static NativeJunglePlayArea Build(RiftGrid nav,
                                                 NativeCampCatalog catalog)
        {
            if (nav == null || catalog == null || catalog.Camps.Count != 8)
                throw new InvalidDataException(
                    "Jungle-only mode requires eight source-verified Map11 camps");
            if (nav.IsPlayAreaRestricted)
                throw new InvalidOperationException(
                    "Build jungle region from the pristine original navigation");
            if (!TryFindMidRiverBrush(nav, out var river, out int grassGroup))
                throw new InvalidDataException(
                    "Original navigable mid-river brush is missing; no fake staging");

            var camps = new Vector2[catalog.Camps.Count];
            var all = new Vector2[camps.Length + 1];
            all[0] = river;
            for (int i = 0; i < camps.Length; i++)
            {
                if (!nav.TryNearest(catalog.Camps[i].Location, out camps[i], 15))
                    throw new InvalidDataException(
                        "Verified camp unreachable: " + catalog.Camps[i].Name);
                all[i + 1] = camps[i];
            }

            var mask = new bool[RiftGrid.Width * RiftGrid.Height];
            void Paint(Vector2 point, float radius)
            {
                int minX = Mathf.Max(1, Mathf.FloorToInt(
                    (point.x - radius - RiftGrid.MinX) / RiftGrid.CellSize));
                int maxX = Mathf.Min(RiftGrid.Width - 2, Mathf.CeilToInt(
                    (point.x + radius - RiftGrid.MinX) / RiftGrid.CellSize));
                int minY = Mathf.Max(1, Mathf.FloorToInt(
                    (point.y - radius - RiftGrid.MinZ) / RiftGrid.CellSize));
                int maxY = Mathf.Min(RiftGrid.Height - 2, Mathf.CeilToInt(
                    (point.y + radius - RiftGrid.MinZ) / RiftGrid.CellSize));
                float r2 = radius * radius;
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    var p = nav.Center(x, y);
                    if ((p - point).sqrMagnitude <= r2 && nav.Valid(x, y))
                        mask[y * RiftGrid.Width + x] = true;
                }
            }
            void PaintPath(Vector2 start, Vector2 goal, float radius)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(
                    Vector2.Distance(start, goal) / 110f));
                for (int i = 0; i <= steps; i++)
                    Paint(Vector2.Lerp(start, goal, i / (float)steps), radius);
            }

            foreach (var p in all) Paint(p, CampRadius);

            // Build a minimum-distance set of source-camp connectors,
            // but paint their ACTUAL walkable A* routes rather than cutting
            // across impassable cliff faces or creating fictional jungle paths.
            bool[] attached = new bool[all.Length];
            attached[0] = true;
            for (int step = 1; step < all.Length; step++)
            {
                int from = -1, to = -1;
                float shortest = float.PositiveInfinity;
                for (int i = 0; i < all.Length; i++)
                {
                    if (!attached[i]) continue;
                    for (int j = 0; j < all.Length; j++)
                    {
                        if (attached[j]) continue;
                        float d = (all[i] - all[j]).sqrMagnitude;
                        if (d >= shortest) continue;
                        from = i; to = j; shortest = d;
                    }
                }
                if (from < 0 || to < 0)
                    throw new InvalidDataException("Jungle region connector graph invalid");
                var route = new List<Vector2>();
                if (!nav.TryPath(all[from], all[to], route) || route.Count == 0)
                    throw new InvalidDataException(
                        "Map11 camp has no original walkable route from river brush");
                Vector2 cursor = all[from];
                foreach (var next in route)
                {
                    PaintPath(cursor, next, PathRadius);
                    cursor = next;
                }
                attached[to] = true;
            }

            // Validate playable connectivity after masking, using actual
            // corner-cut-safe A*. Disconnected jungle pockets fail early.
            int count = 0;
            Vector2 excluded = default;
            bool excludedFound = false;
            for (int y = 1; y < RiftGrid.Height - 1; y++)
            for (int x = 1; x < RiftGrid.Width - 1; x++)
            {
                if (mask[y * RiftGrid.Width + x]) count++;
                else if (!excludedFound && nav.Valid(x, y))
                {
                    excludedFound = true;
                    excluded = nav.Center(x, y);
                }
            }
            if (!excludedFound || count < 1000 ||
                !mask[nav.Cell(river.x, river.y).y * RiftGrid.Width +
                      nav.Cell(river.x, river.y).x])
                throw new InvalidDataException("Jungle region mask invalid or all-map");

            // Temporarily enable the exact same mask used by gameplay, and
            // demand paths from the hunter brush to every verified camp.
            nav.SetPlayArea(mask);
            try
            {
                foreach (var camp in camps)
                {
                    var route = new List<Vector2>();
                    if (!nav.CanWalk(camp) || !nav.TryPath(river, camp, route))
                        throw new InvalidDataException(
                            "Playable jungle boundary disconnected a verified camp");
                }
                if (nav.CanWalk(excluded))
                    throw new InvalidDataException("Jungle boundary leaks to outside cells");
            }
            finally { nav.SetPlayArea(null); }

            Debug.Log("NATIVE_JUNGLE_REGION_READY: cells=" + count +
                      " verifiedCamps=" + camps.Length +
                      " midRiverBrushGroup=" + grassGroup +
                      " hunterX=" + river.x.ToString("F0") +
                      " hunterZ=" + river.y.ToString("F0") +
                      " excluded=" + excluded +
                      " terrainUnchanged=true");
            return new NativeJunglePlayArea(
                mask, camps, river, excluded, count, grassGroup);
        }
    }
}
