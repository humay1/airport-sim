using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    // Shared helpers for the Q-132 scene tests, written from 15 §15.19 to
    // §15.21 and §15.23, 12 §12.4, §12.9 and §12.13, and 19 §19.2c only.
    // Expected motion values are computed here from the spec's formulas in
    // double, in the order written, and converted to float once (15 §15.23's
    // tolerance note); they are compared within Motion.Tolerance.

    internal static class Motion
    {
        /// <summary>15 §15.23: motion positions and Elevation, in world units.</summary>
        public const double Tolerance = 0.01;

        // 15 §15.19's constants, read from RenderConstants so the tests check
        // the shipped values against the table in RenderSurfaceTests.
        public const int ApproachTicks = RenderConstants.APPROACH_TICKS;
        public const int ApproachEntryM = RenderConstants.APPROACH_ENTRY_M;
        public const int FinalFixM = RenderConstants.FINAL_FIX_M;
        public const int HoldLegM = RenderConstants.HOLD_LEG_M;
        public const int HoldLegTicks = RenderConstants.HOLD_LEG_TICKS;
        public const int ClimbOutM = RenderConstants.CLIMB_OUT_M;
        public const int GlideRatio = RenderConstants.GLIDE_RATIO;
        public const int ClimbRatio = RenderConstants.CLIMB_RATIO;
        public const int BridgeWalkTicks = RenderConstants.BRIDGE_WALK_TICKS;
        public const int MaxBridgeWalkers = RenderConstants.MAX_BRIDGE_WALKERS;

        /// <summary>15 §15.19: τ = (double)CurrentTick − 1 + subTickMicroseconds / 100 000.0.</summary>
        public static double Tau(ulong currentTick, long sub)
        {
            return (double)currentTick - 1 + (sub / 100000.0);
        }

        public static double Clamp01(double x)
        {
            return x < 0 ? 0 : (x > 1 ? 1 : x);
        }

        public static void Near(double x, double y, in WorldPoint p, string what)
        {
            float fx = (float)x;
            float fy = (float)y;
            Assert.True(
                Math.Abs((double)p.X - fx) <= Tolerance && Math.Abs((double)p.Y - fy) <= Tolerance,
                string.Format(CultureInfo.InvariantCulture, "{0}: expected ({1:R},{2:R}) within {3}, got ({4:R},{5:R})", what, fx, fy, Tolerance, p.X, p.Y));
        }

        public static void NearElevation(double e, in DrawPrimitive p, string what)
        {
            float fe = (float)e;
            Assert.True(
                Math.Abs((double)p.Elevation - fe) <= Tolerance,
                string.Format(CultureInfo.InvariantCulture, "{0}: Elevation expected {1:R} within {2}, got {3:R} on {4}", what, fe, Tolerance, p.Elevation, Prims.Show(p)));
        }

        /// <summary>The aircraft primitive of a flight, which must be drawn once.</summary>
        public static DrawPrimitive Aircraft(IReadOnlyList<DrawPrimitive> all, ulong flight, string what)
        {
            List<DrawPrimitive> found = Prims.Of(all, SourceKind.Aircraft, flight);
            Assert.True(found.Count == 1, what + ": expected flight " + flight + " drawn once, found " + found.Count + ": " + Prims.Show(found));
            return found[0];
        }

        public static void NotDrawn(IReadOnlyList<DrawPrimitive> all, ulong flight, string what)
        {
            List<DrawPrimitive> found = Prims.Of(all, SourceKind.Aircraft, flight);
            Assert.True(found.Count == 0, what + ": flight " + flight + " should not be drawn: " + Prims.Show(found));
        }
    }

    /// <summary>A double point, for expected values only.</summary>
    internal readonly struct P2
    {
        public readonly double X;
        public readonly double Y;

        public P2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static P2 operator +(P2 a, P2 b) => new P2(a.X + b.X, a.Y + b.Y);

        public static P2 operator -(P2 a, P2 b) => new P2(a.X - b.X, a.Y - b.Y);

        public static P2 operator *(P2 a, double k) => new P2(a.X * k, a.Y * k);

        public double Length => Math.Sqrt((X * X) + (Y * Y));

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:R},{1:R})", X, Y);
        }
    }

    /// <summary>What a row of 15 §15.20's off-graph table gives at one τ.</summary>
    internal readonly struct Expected
    {
        public readonly P2 Position;
        public readonly double Elevation;
        public readonly long FacingX;
        public readonly long FacingY;

        public Expected(P2 position, double elevation, long facingX, long facingY)
        {
            Position = position;
            Elevation = elevation;
            FacingX = facingX;
            FacingY = facingY;
        }

        public void AssertOn(in DrawPrimitive p, string what)
        {
            Motion.Near(Position.X, Position.Y, p.A, what);
            Motion.NearElevation(Elevation, p, what);
            Art.AssertFacing((float)FacingX, (float)FacingY, p, what);
        }
    }

    /// <summary>
    /// 15 §15.20's runway frame, from the geometry P0, P1, the threshold
    /// position T and the exit position X, and the four off-graph rows.
    /// </summary>
    internal sealed class RunwayFrame
    {
        public readonly P2 N;
        public readonly P2 F;
        public readonly P2 T;
        public readonly P2 X;
        public readonly P2 U;
        public readonly double Lr;
        public readonly P2 TD;
        public readonly P2 LO;
        public readonly P2 FF;
        public readonly P2 AE;
        public readonly P2 CE;
        public readonly long DepX;
        public readonly long DepY;
        public readonly long ArrX;
        public readonly long ArrY;

        public RunwayFrame(int x0, int y0, int x1, int y1, int tx, int ty, int xx, int xy)
        {
            // N is P0 if |P0 − T|² ≤ |P1 − T|² in int64, else P1.
            long d0 = Sq((long)x0 - tx) + Sq((long)y0 - ty);
            long d1 = Sq((long)x1 - tx) + Sq((long)y1 - ty);
            long nx = d0 <= d1 ? x0 : x1;
            long ny = d0 <= d1 ? y0 : y1;
            long fx = d0 <= d1 ? x1 : x0;
            long fy = d0 <= d1 ? y1 : y0;
            N = new P2(nx, ny);
            F = new P2(fx, fy);
            T = new P2(tx, ty);
            X = new P2(xx, xy);
            Lr = (F - N).Length;
            U = new P2((F.X - N.X) / Lr, (F.Y - N.Y) / Lr);
            TD = N + (U * (Lr / 8));
            LO = N + (U * (Lr * 3 / 4));
            FF = TD - (U * Motion.FinalFixM);
            AE = TD - (U * Motion.ApproachEntryM);
            CE = LO + (U * Motion.ClimbOutM);
            DepX = fx - nx;
            DepY = fy - ny;
            ArrX = nx - fx;
            ArrY = ny - fy;
        }

        /// <summary>AwaitingApproach, arrival: v from the window's start (due − APPROACH_TICKS).</summary>
        public Expected Approach(double tau, ulong due)
        {
            double v = Motion.Clamp01((tau - ((double)due - Motion.ApproachTicks)) / Motion.ApproachTicks);
            P2 p = AE + ((FF - AE) * v);
            return new Expected(p, (p - TD).Length / Motion.GlideRatio, DepX, DepY);
        }

        /// <summary>w = clamp((τ − PhaseEnteredAt) / O, 0, 1), O = DueAt − PhaseEnteredAt; 1 when O ≤ 0.</summary>
        public static double W(double tau, ulong entered, ulong due)
        {
            double o = (double)due - (double)entered;
            return o <= 0 ? 1.0 : Motion.Clamp01((tau - entered) / o);
        }

        /// <summary>OnRunway, arrival: the final from FF to TD, then the rollout to X.</summary>
        public Expected Landing(double tau, ulong entered, ulong due)
        {
            double w = W(tau, entered, due);
            if (w <= 0.5)
            {
                double v = 2 * w;
                P2 p = FF + ((TD - FF) * v);
                return new Expected(p, (p - TD).Length / Motion.GlideRatio, DepX, DepY);
            }
            else
            {
                double v = (2 * w) - 1;
                P2 p = TD + ((X - TD) * (1 - ((1 - v) * (1 - v))));
                return new Expected(p, 0, DepX, DepY);
            }
        }

        /// <summary>OnRunway, departure: the roll from T to LO, then the climb to CE.</summary>
        public Expected Takeoff(double tau, ulong entered, ulong due)
        {
            double w = W(tau, entered, due);
            if (w <= 0.5)
            {
                double v = 2 * w;
                P2 p = T + ((LO - T) * (v * v));
                return new Expected(p, 0, DepX, DepY);
            }
            else
            {
                double v = (2 * w) - 1;
                P2 p = LO + (U * (Motion.ClimbOutM * v));
                return new Expected(p, Motion.ClimbOutM * v / Motion.ClimbRatio, DepX, DepY);
            }
        }

        /// <summary>The hold's corners C0..C3 (15 §15.20).</summary>
        public P2[] HoldCorners()
        {
            var l = new P2(U.Y, -U.X);
            P2 c0 = FF;
            P2 c1 = FF + (l * Motion.HoldLegM);
            P2 c2 = c1 - (U * Motion.HoldLegM);
            P2 c3 = FF - (U * Motion.HoldLegM);
            return new[] { c0, c1, c2, c3 };
        }

        /// <summary>HeldForRunway, arrival: leg k = floor(s) mod 4, s = max(0, τ − PhaseEnteredAt) / HOLD_LEG_TICKS.</summary>
        public Expected Hold(double tau, ulong entered)
        {
            double s = Math.Max(0, tau - entered) / Motion.HoldLegTicks;
            double fl = Math.Floor(s);
            int k = (int)(fl % 4);
            P2[] c = HoldCorners();
            P2 p = c[k] + ((c[(k + 1) % 4] - c[k]) * (s - fl));
            long fxv;
            long fyv;
            switch (k)
            {
                case 0: fxv = -ArrY; fyv = ArrX; break;
                case 1: fxv = ArrX; fyv = ArrY; break;
                case 2: fxv = ArrY; fyv = -ArrX; break;
                default: fxv = DepX; fyv = DepY; break;
            }

            return new Expected(p, (double)Motion.FinalFixM / Motion.GlideRatio, fxv, fyv);
        }

        private static long Sq(long v)
        {
            return v * v;
        }
    }

    /// <summary>
    /// A fake-sourced scene over a given airside layout and render layout,
    /// for the motion tests. Overview sees every point of 15 §15.20's frame
    /// in the playtest layout, from above the zoom threshold.
    /// </summary>
    internal sealed class MotionScene
    {
        public readonly CallGuard Guard = new CallGuard();
        public readonly FakeHost Host;
        public readonly FakeAirside Airside;
        public readonly FakeFlow Flow;
        public readonly FakeSchedule Schedule;
        public readonly RenderLayout Layout;

        public MotionScene(AirsideLayout airside, RenderLayout layout)
        {
            Host = new FakeHost(Guard, 1UL);
            Airside = new FakeAirside(Guard, airside);
            Flow = new FakeFlow(Guard);
            foreach (FlowNodeBox b in layout.FlowNodes)
            {
                Flow.Node(b.Node.Value);
            }

            Schedule = new FakeSchedule(Guard);
            Layout = layout;
        }

        public static CameraView Overview => Cam.At(0f, 0f, 20000f, 1f);

        /// <summary>The playtest's layouts: 12 §12.13 plus 19 §19.2c's exit, and playtest-layout.json.</summary>
        public static MotionScene Playtest()
        {
            AirsideLayout airside = PlaytestAirside.Layout();
            RenderLayout layout = RenderFactory.CreateLayoutLoader().Load(
                Repo.Read("tests", "fixtures", "render", "playtest-layout.json"),
                "playtest-layout.json",
                airside);
            return new MotionScene(airside, layout);
        }

        /// <summary>12 §12.13's fixture, whose runway has no exit node (ExitNode = ThresholdNode), and phase1-layout.json.</summary>
        public static MotionScene Phase1()
        {
            AirsideLayout airside = Phase1Sim.AirsideLayout();
            RenderLayout layout = RenderFactory.CreateLayoutLoader().Load(
                Repo.Read("tests", "fixtures", "render", "phase1-layout.json"),
                "phase1-layout.json",
                airside);
            return new MotionScene(airside, layout);
        }

        /// <summary>The frame of runway 1 in both: (−2000,0)–(0,0), T = node 1 at (0,0), X as given.</summary>
        public static RunwayFrame Runway1(int exitX, int exitY)
        {
            return new RunwayFrame(-2000, 0, 0, 0, 0, 0, exitX, exitY);
        }

        public RenderSources Sources => new RenderSources(Host, Airside, Flow);

        public ISceneBuilder Builder()
        {
            return RenderFactory.CreateSceneBuilder(Sources, Layout);
        }

        /// <summary>Sets CurrentTick, builds at the sub-tick, and copies the frame.</summary>
        public List<DrawPrimitive> At(ISceneBuilder b, ulong tick, long sub)
        {
            Host.Tick = tick;
            return Prims.Copy(b.Build(Overview, Gfx.High(), sub));
        }
    }
}
