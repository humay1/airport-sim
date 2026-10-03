using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    // Shared helpers for the T-020 suite, written from 15-interfaces-render.md,
    // 16 §16.6, 09 §9.7/§9.7b, 12 §12.4/§12.9 and 07-conventions.md only.

    internal static class RenderConst
    {
        // 15 §15.2, as literals: the spec names no class holding the
        // presentation constants, so the tests do not reference one.
        public const float AgentZoomThreshold = 120f;
        public const int MaxDrawnAgentsPerNode = 256;
        public const int MaxDrawnLanesPerNode = 32;
        public const uint MaxCatchupTicksPerFrame = 3;
        public const long RealMicrosecondsPerTick1X = 100000;

        // 08 §8.1.
        public const ulong TicksPerDay = 14400UL;
        public const ulong HashCheckpointTicks = 600UL;
    }

    /// <summary>08 §8.8's SplitMix64, the 07 L4 input generator for property tests.</summary>
    internal sealed class SplitMix64
    {
        private ulong _state;

        public SplitMix64(ulong seed)
        {
            _state = seed;
        }

        public ulong Next()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Integer in [lo, hi]; test inputs only.</summary>
        public int Range(int lo, int hi)
        {
            return lo + (int)(Next() % (ulong)(hi - lo + 1));
        }
    }

    /// <summary>
    /// The repository root is the nearest ancestor of AppContext.BaseDirectory
    /// holding AirportSim.sln (07 "Fixture location", Q-031). A missing root
    /// fails the test.
    /// </summary>
    internal static class Repo
    {
        public static byte[] Read(params string[] relative)
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AirportSim.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.True(dir != null, "no ancestor of " + AppContext.BaseDirectory + " contains AirportSim.sln");
            var parts = new List<string> { dir! };
            parts.AddRange(relative);
            return File.ReadAllBytes(Path.Combine(parts.ToArray()));
        }
    }

    /// <summary>
    /// GC.GetAllocatedBytesForCurrentThread counts whole allocation contexts,
    /// so a full blocking collection right before the first read leaves the
    /// thread with no partly used context to retire inside the window.
    /// </summary>
    internal static class Allocation
    {
        public static long Start()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return GC.GetAllocatedBytesForCurrentThread();
        }

        public static long Since(long start)
        {
            return GC.GetAllocatedBytesForCurrentThread() - start;
        }
    }

    /// <summary>
    /// 03 "Budget tests: window and arithmetic" (Q-045): long arithmetic only,
    /// rounding up to whole microseconds, capped at C = B × n + 1, mean by
    /// Σu ≤ B × n and p99 by nearest rank.
    /// </summary>
    internal sealed class BudgetWindow
    {
        private readonly long[] _u;
        private readonly long _budgetMicros;
        private readonly long _cap;
        private int _count;

        public BudgetWindow(int n, long budgetMicros)
        {
            _u = new long[n];
            _budgetMicros = budgetMicros;
            _cap = (budgetMicros * n) + 1;
        }

        public int Count => _count;

        public void Add(long timestampDelta)
        {
            long f = System.Diagnostics.Stopwatch.Frequency;
            long u;
            if (timestampDelta > (long.MaxValue - f + 1) / 1000000L)
            {
                u = _cap;
            }
            else
            {
                u = Math.Min(((timestampDelta * 1000000L) + f - 1) / f, _cap);
            }

            _u[_count++] = u;
        }

        /// <summary>Null when both conditions pass, else a message with the reported mean and p99.</summary>
        public string? Verdict(long p99LimitMicros, string what)
        {
            Assert.Equal(_u.Length, _count);
            long n = _u.Length;
            long sum = 0;
            foreach (long u in _u)
            {
                sum += u;
            }

            var sorted = (long[])_u.Clone();
            Array.Sort(sorted);
            long p99 = sorted[(int)(((99 * n) + 99) / 100) - 1];
            long reportedMean = (sum + n - 1) / n;
            bool meanOk = sum <= _budgetMicros * n;
            bool p99Ok = p99 <= p99LimitMicros;
            if (meanOk && p99Ok)
            {
                return null;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}: mean {1} us (budget {2}), p99 {3} us (limit {4}) over {5} samples",
                what,
                reportedMean,
                _budgetMicros,
                p99,
                p99LimitMicros,
                n);
        }
    }

    internal static class Gfx
    {
        /// <summary>15 §15.14's High row, which is the pre-D10 Phase 1 behaviour.</summary>
        public static GraphicsSettings High()
        {
            return new GraphicsSettings(GraphicsPreset.High, true, RenderConst.MaxDrawnAgentsPerNode, 0, 100, true);
        }

        public static GraphicsSettings Custom(bool drawAgents, int maxAgents, int frameRateCap = 0, int resolution = 100, bool antiAliasing = true)
        {
            return new GraphicsSettings(GraphicsPreset.Custom, drawAgents, maxAgents, frameRateCap, resolution, antiAliasing);
        }

        public static string Show(in GraphicsSettings g)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} agents={1} max={2} fps={3} res={4} aa={5}",
                g.Preset,
                g.DrawAgents,
                g.MaxDrawnAgentsPerNode,
                g.FrameRateCap,
                g.ResolutionScalePercent,
                g.AntiAliasing);
        }
    }

    internal static class Cam
    {
        public static CameraView At(float x, float y, float viewHeight, float aspect = 1f)
        {
            return new CameraView(new WorldPoint(x, y), viewHeight, aspect);
        }

        /// <summary>A camera centred on the box, at the given view height.</summary>
        public static CameraView On(in FlowNodeBox box, float viewHeight, float aspect = 1f)
        {
            return At((box.MinX + box.MaxX) / 2f, (box.MinY + box.MaxY) / 2f, viewHeight, aspect);
        }

        /// <summary>A camera far from every layout in this suite.</summary>
        public static CameraView Away(float viewHeight = 60f)
        {
            return At(-1000000f, -1000000f, viewHeight);
        }
    }

    internal static class Prims
    {
        public static List<DrawPrimitive> Copy(in RenderFrame frame)
        {
            // RenderFrame.Primitives is valid only until the next Build (15 §15.11).
            var list = new List<DrawPrimitive>(frame.Primitives.Count);
            for (int i = 0; i < frame.Primitives.Count; i++)
            {
                list.Add(frame.Primitives[i]);
            }

            return list;
        }

        public static List<DrawPrimitive> InLayer(IReadOnlyList<DrawPrimitive> all, DrawLayer layer)
        {
            var list = new List<DrawPrimitive>();
            foreach (DrawPrimitive p in all)
            {
                if (p.Layer == layer)
                {
                    list.Add(p);
                }
            }

            return list;
        }

        public static List<DrawPrimitive> Of(IReadOnlyList<DrawPrimitive> all, SourceKind kind, ulong id)
        {
            var list = new List<DrawPrimitive>();
            foreach (DrawPrimitive p in all)
            {
                if (p.Source.Kind == kind && p.Source.Id == id)
                {
                    list.Add(p);
                }
            }

            return list;
        }

        public static DrawPrimitive Single(IReadOnlyList<DrawPrimitive> all, SourceKind kind, ulong id)
        {
            List<DrawPrimitive> found = Of(all, kind, id);
            Assert.True(found.Count == 1, "expected one " + kind + " primitive for id " + id + ", found " + found.Count + ": " + Show(found));
            return found[0];
        }

        /// <summary>Every field, floats in round-trip form, so equal strings mean equal primitives.</summary>
        public static string Show(in DrawPrimitive p)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}/{1}/{2} A=({3:R},{4:R}) B=({5:R},{6:R}) size={7:R} src={8}:{9}:{10}",
                p.Kind,
                p.Layer,
                p.Colour,
                p.A.X,
                p.A.Y,
                p.B.X,
                p.B.Y,
                p.Size,
                p.Source.Kind,
                p.Source.Id,
                p.Source.Sub);
        }

        public static string Show(IReadOnlyList<DrawPrimitive> all)
        {
            var parts = new List<string>();
            foreach (DrawPrimitive p in all)
            {
                parts.Add(Show(p));
            }

            return "[" + string.Join("; ", parts) + "]";
        }

        public static List<string> Lines(IReadOnlyList<DrawPrimitive> all, bool skipAgents = false)
        {
            var lines = new List<string>();
            foreach (DrawPrimitive p in all)
            {
                if (!(skipAgents && p.Layer == DrawLayer.Agent))
                {
                    lines.Add(Show(p));
                }
            }

            return lines;
        }

        public static void AssertPoint(float x, float y, in WorldPoint p, string what)
        {
            Assert.True(p.X == x && p.Y == y, string.Format(CultureInfo.InvariantCulture, "{0}: expected ({1:R},{2:R}), got ({3:R},{4:R})", what, x, y, p.X, p.Y));
        }

        /// <summary>A dot's centre lies in the box, closed intervals (15 §15.5: "inside the box").</summary>
        public static bool Inside(in DrawPrimitive dot, in FlowNodeBox box)
        {
            return dot.A.X >= box.MinX && dot.A.X <= box.MaxX && dot.A.Y >= box.MinY && dot.A.Y <= box.MaxY;
        }

        /// <summary>
        /// 15 §15.5: DrawLayer ascending, then SourceRef ascending within a
        /// layer. SourceRef is compared field by field in declared order
        /// (Kind, Id, Sub). Every primitive has its own source, so the order
        /// is strict.
        /// </summary>
        public static string? OrderViolation(IReadOnlyList<DrawPrimitive> all)
        {
            for (int i = 1; i < all.Count; i++)
            {
                if (Compare(all[i - 1], all[i]) >= 0)
                {
                    return "primitives " + (i - 1) + " and " + i + " out of order: " + Show(all[i - 1]) + " then " + Show(all[i]);
                }
            }

            return null;
        }

        private static int Compare(in DrawPrimitive a, in DrawPrimitive b)
        {
            int c = ((int)a.Layer).CompareTo((int)b.Layer);
            if (c != 0)
            {
                return c;
            }

            c = ((int)a.Source.Kind).CompareTo((int)b.Source.Kind);
            if (c != 0)
            {
                return c;
            }

            c = a.Source.Id.CompareTo(b.Source.Id);
            return c != 0 ? c : a.Source.Sub.CompareTo(b.Source.Sub);
        }
    }
}
