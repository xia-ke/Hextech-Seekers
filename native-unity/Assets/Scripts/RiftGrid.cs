using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>Native port of the original 295x296 Rift navigation cells.
    /// All navigation is in the *browser game world coordinate system*: x,z,
    /// with negative z, and is kept independent from glTF/Unity import handedness.
    /// </summary>
    public sealed class RiftGrid
    {
        public const int Width = 295;
        public const int Height = 296;
        public const float MinX = -1.1048965454101562f;
        public const float MinZ = -14832.755798339844f;
        public const float CellSize = 50f;
        public const int FlagsOffset = Width * Height * 3;
        public const int BushOffset = Width * Height * 5;
        public const int BinaryBytes = Width * Height * 6;
        readonly byte[] walk;
        readonly short[] heights;
        readonly byte[] clearance;
        readonly byte[] bush;
        bool[] playArea;
        public bool IsPlayAreaRestricted => playArea != null;

        public void SetPlayArea(bool[] allowed)
        {
            if (allowed != null && allowed.Length != Width * Height)
                throw new ArgumentException("Jungle play-area mask must cover the original Map11 grid");
            playArea = allowed == null ? null : (bool[])allowed.Clone();
        }
        public RiftGrid(byte[] raw)
        {
            if (raw == null || raw.Length != BinaryBytes)
                throw new InvalidDataException($"Expected {BinaryBytes} bytes of original navigation.bin, got {raw?.Length ?? 0}");
            int cells = Width * Height;
            walk = new byte[cells];
            heights = new short[cells];
            clearance = new byte[cells];
            bush = new byte[cells];
            Array.Copy(raw, 0, walk, 0, cells);
            for (int i = 0; i < cells; i++)
            {
                heights[i] = (short)(raw[cells + i * 2] | raw[cells + i * 2 + 1] << 8);
                // Original Map11 navigation.bin encodes NO BUSH as 255.
                // Real bush groups are zero-based IDs 0..38. Expose 0
                // consistently as "not in a bush", and 1..39 as real
                // groups; otherwise 255 makes all normal terrain look like
                // one giant bush and breaks unit visibility.
                byte rawBush = raw[BushOffset + i];
                bush[i] = rawBush == byte.MaxValue ?
                    (byte)0 : (byte)(rawBush + 1);
            }
            for (int y = 1; y < Height - 1; y++)
                for (int x = 1; x < Width - 1; x++)
                {
                    bool open = true;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (walk[(y + dy) * Width + x + dx] == 0) open = false;
                    clearance[y * Width + x] = (byte)(open ? 1 : 0);
                }
        }
        public Vector2Int Cell(float x, float z) => new Vector2Int(
            Mathf.FloorToInt((x - MinX) / CellSize), Mathf.FloorToInt((z - MinZ) / CellSize));
        public Vector2 Center(int x, int y) => new Vector2(
            MinX + (x + .5f) * CellSize, MinZ + (y + .5f) * CellSize);
        // Original Map11 walkability, independent of the game-mode mask.
        public bool OriginalCellWalkable(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height ||
                clearance[y * Width + x] == 0) return false;
            Vector2 p = Center(x, y);
            return p.x + p.y >= -120f;
        }
        public bool Valid(int x, int y)
        {
            return OriginalCellWalkable(x, y) &&
                   (playArea == null || playArea[y * Width + x]);
        }
        public bool CanWalk(Vector2 p) { var c = Cell(p.x, p.y); return Valid(c.x, c.y); }
        public short HeightAt(Vector2 p)
        {
            var c = Cell(p.x, p.y);
            int x = Mathf.Clamp(c.x, 0, Width - 1), y = Mathf.Clamp(c.y, 0, Height - 1);
            return heights[y * Width + x];
        }
        public byte BushAt(Vector2 p)
        {
            var c = Cell(p.x, p.y);
            return c.x < 0 || c.y < 0 || c.x >= Width || c.y >= Height ? (byte)0 : bush[c.y * Width + c.x];
        }
        public bool TryNearest(Vector2 world, out Vector2 found, int radius = 15)
        {
            var c = Cell(world.x, world.y);
            for (int r = 0; r <= radius; r++)
            {
                bool any = false;
                float nearest = float.MaxValue;
                Vector2 best = default;
                for (int y = c.y - r; y <= c.y + r; y++)
                    for (int x = c.x - r; x <= c.x + r; x++)
                    {
                        if (r > 0 && Mathf.Abs(x - c.x) != r && Mathf.Abs(y - c.y) != r) continue;
                        if (!Valid(x, y)) continue;
                        Vector2 p = Center(x, y);
                        float d = (world - p).sqrMagnitude;
                        if (d >= nearest) continue;
                        nearest = d; best = p; any = true;
                    }
                if (any) { found = best; return true; }
            }
            found = default;
            return false;
        }
        public bool LineClear(Vector2 a, Vector2 b)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / (CellSize * .3f)));
            for (int i = 0; i <= steps; i++) if (!CanWalk(Vector2.Lerp(a, b, i / (float)steps))) return false;
            return true;
        }
        /// <summary>Deterministic eight-neighbor A*; corner cutting is forbidden.
        /// Uses the original walkability clearance and the same octile heuristic.</summary>
        public bool TryPath(Vector2 start, Vector2 destination, List<Vector2> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            if (!TryNearest(start, out var a, 6) || !TryNearest(destination, out var b)) return false;
            if (LineClear(start, b)) { output.Add(b); return true; }
            var startCell = Cell(a.x, a.y); var endCell = Cell(b.x, b.y);
            int n = Width * Height, first = startCell.y * Width + startCell.x, last = endCell.y * Width + endCell.x;
            var distances = new float[n]; var previous = new int[n]; var closed = new bool[n];
            Array.Fill(distances, float.PositiveInfinity); Array.Fill(previous, -1);
            distances[first] = 0;
            // Priority queue implemented with an indexed binary heap, no per-node allocation.
            var heap = new List<(int id, float priority)>();
            float Heuristic(int index)
            {
                int dx = Mathf.Abs(index % Width - endCell.x), dy = Mathf.Abs(index / Width - endCell.y);
                return Mathf.Max(dx, dy) + (Mathf.Sqrt(2f) - 1f) * Mathf.Min(dx, dy);
            }
            void Push(int id, float priority)
            {
                int i = heap.Count; heap.Add((id, priority));
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (heap[p].priority <= priority) break;
                    heap[i] = heap[p]; i = p;
                }
                heap[i] = (id, priority);
            }
            (int id, float priority) Pop()
            {
                var result = heap[0]; var tail = heap[heap.Count - 1]; heap.RemoveAt(heap.Count - 1);
                if (heap.Count > 0)
                {
                    int i = 0;
                    while (i * 2 + 1 < heap.Count)
                    {
                        int child = i * 2 + 1;
                        if (child + 1 < heap.Count && heap[child + 1].priority < heap[child].priority) child++;
                        if (heap[child].priority >= tail.priority) break;
                        heap[i] = heap[child]; i = child;
                    }
                    heap[i] = tail;
                }
                return result;
            }
            Push(first, Heuristic(first));
            for (int visited = 0; heap.Count > 0 && visited < 60000; visited++)
            {
                int at = Pop().id;
                if (closed[at]) continue;
                closed[at] = true;
                if (at == last)
                {
                    var rev = new List<Vector2>();
                    for (int pos = last; pos != first; pos = previous[pos])
                    {
                        if (pos < 0) return false;
                        rev.Add(Center(pos % Width, pos / Width));
                    }
                    rev.Reverse();
                    // Smooth only through navigable cells; retain the actual final destination.
                    Vector2 from = start;
                    for (int i = 0; i < rev.Count;)
                    {
                        int far = i;
                        while (far + 1 < rev.Count && LineClear(from, rev[far + 1])) far++;
                        output.Add(rev[far]); from = rev[far]; i = far + 1;
                    }
                    return true;
                }
                int cx = at % Width, cy = at / Width;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dy == 0) || !Valid(cx + dx, cy + dy)) continue;
                    if (dx != 0 && dy != 0 && (!Valid(cx + dx, cy) || !Valid(cx, cy + dy))) continue;
                    int next = (cy + dy) * Width + (cx + dx);
                    if (closed[next]) continue;
                    float g = distances[at] + (dx != 0 && dy != 0 ? Mathf.Sqrt(2f) : 1f);
                    if (g >= distances[next]) continue;
                    distances[next] = g; previous[next] = at;
                    Push(next, g + Heuristic(next));
                }
            }
            return false;
        }
    }
}