using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.App.Render;
using AirportSim.App.Ui;
using AirportSim.Sim.Core;
using Xunit;
using B = AirportSim.App.Host.Tests.Bundles;

namespace AirportSim.App.Host.Tests
{
    /// <summary>A RenderFrame's primitives copied out, every float by its bits, so two builds compare exactly (15 §15.23).</summary>
    internal static class Prims
    {
        private static string Bits(float f)
        {
            return BitConverter.SingleToInt32Bits(f).ToString("x8", CultureInfo.InvariantCulture);
        }

        private static string Rgb(in Rgb c)
        {
            return c.R.ToString(CultureInfo.InvariantCulture) + "," + c.G.ToString(CultureInfo.InvariantCulture) + "," + c.B.ToString(CultureInfo.InvariantCulture);
        }

        public static string Show(in DrawPrimitive p)
        {
            return string.Join(
                " ",
                p.Kind,
                p.Layer,
                p.Colour,
                p.Visual,
                Bits(p.A.X),
                Bits(p.A.Y),
                Bits(p.B.X),
                Bits(p.B.Y),
                Bits(p.Size),
                Bits(p.Facing.X),
                Bits(p.Facing.Y),
                Rgb(p.Paint.Region0),
                Rgb(p.Paint.Region1),
                Rgb(p.Paint.Region2),
                Rgb(p.Paint.Region3),
                Rgb(p.Paint.Region4),
                p.Paint.Mark.ToString(CultureInfo.InvariantCulture),
                p.Source.Kind,
                p.Source.Id.ToString(CultureInfo.InvariantCulture),
                p.Source.Sub.ToString(CultureInfo.InvariantCulture),
                Bits(p.Elevation));
        }

        public static List<string> Copy(in RenderFrame frame)
        {
            var list = new List<string>(frame.Primitives.Count);
            foreach (DrawPrimitive p in frame.Primitives)
            {
                list.Add(Show(p));
            }

            return list;
        }

        /// <summary>The first difference between two copied frames, or null when they are identical.</summary>
        public static string? Diff(List<string> expected, List<string> actual)
        {
            int n = Math.Min(expected.Count, actual.Count);
            for (int i = 0; i < n; i++)
            {
                if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
                {
                    return "primitive " + i + ": expected [" + expected[i] + "], got [" + actual[i] + "]";
                }
            }

            return expected.Count == actual.Count ? null : "primitive count: expected " + expected.Count + ", got " + actual.Count;
        }
    }

    /// <summary>
    /// T-058 (Q-132). 16 §16.6 step 4: the frame loop builds the scene with
    /// Pacer.SubTickMicroseconds, read after step 3's Advance, so motion glides
    /// between ticks (15 §15.19). The playtest bundle is composed and presented
    /// by the host; a reference scene builder over the same composed sim and
    /// layout, and an independent pacer fed the same frames, give the frame
    /// each RunFrame must return.
    /// </summary>
    public sealed class FrameLoopSubTickTests
    {
        private const long OneTick = RenderConstants.REAL_MICROSECONDS_PER_TICK_1X;

        /// <summary>Every taxi node, runway and stand of the playtest layout in view; dyadic (07 L4).</summary>
        private static readonly CameraView Camera = new CameraView(new WorldPoint(-512f, 0f), 4096f, 1f);

        private static FrameInput Input(long elapsed, params UiInput[] ui)
        {
            return new FrameInput(Camera, 1024f, 1024f, ui, elapsed);
        }

        [Fact]
        public void test_frame_loop_builds_with_the_pacer_sub_tick()
        {
            MemoryBundle bundle = B.PlaytestBundle();
            ComposedSim sim = ComposeTests.Compose(bundle, B.Phase1Content, new RecordingSink());
            Assert.NotNull(sim.Airside);
            Assert.NotNull(sim.Flow);
            Assert.NotNull(sim.Schedule);
            Presentation presentation = HostFactory.CreatePresentationComposer().Compose(sim, bundle, new FakePreferences());

            // The reference: 16 §16.5's scene over the same sim and layout, with
            // the three-argument Compose's DefaultLooks() (the two-argument
            // CreateSceneBuilder), built only by this test.
            RenderLayout layout = RenderFactory.CreateLayoutLoader().Load(bundle.Bytes("render_layout.fixture"), "render_layout.fixture", sim.Airside!.Layout());
            var sources = new RenderSources(sim.Host, sim.Airside, sim.Flow, sim.Schedule, sim.Content);
            ISceneBuilder reference = RenderFactory.CreateSceneBuilder(sources, layout);
            ITickPacer pacer = RenderFactory.CreatePacer();

            // One cycle of frames per sample: two fractional frames, a frame that
            // presses pause (its elapsed time discarded, 15 §15.8), a paused
            // frame, and the frame that unpauses. The sub-tick is never a whole
            // number of ticks after the first frame of the run.
            (long Elapsed, bool Toggle)[] cycle =
            {
                (OneTick * 3 / 10, false),
                (OneTick * 45 / 100, false),
                (OneTick * 2, true),
                (OneTick, false),
                (OneTick * 17 / 100, true),
            };

            bool paused = false;
            ulong tick = 0;
            int frames = 0;
            int pausedFrames = 0;
            int glides = 0;
            int pausedGlides = 0;
            for (uint sample = 0; sample < B.Day / 60; sample++)
            {
                // Between cycles the test moves the sim on by itself, so the cycles
                // sample a whole day of approaches, landings, taxiing and walkers.
                sim.Host.Step(57);
                tick += 57;

                foreach ((long elapsed, bool toggle) in cycle)
                {
                    if (toggle)
                    {
                        paused = !paused;
                    }

                    uint n = pacer.Advance(elapsed, paused, GameSpeed.X1);
                    long sub = pacer.SubTickMicroseconds;
                    string what = "sample " + sample + ", frame " + frames + " (elapsed " + elapsed + " µs, paused " + paused + ", sub-tick " + sub + ")";

                    FrameOutput o = presentation.Frame.RunFrame(toggle ? Input(elapsed, In.TogglePause()) : Input(elapsed, In.None));
                    tick += n;
                    frames++;
                    Assert.True(o.Ui.Pacing.Paused == paused, what + ": UiFrame.Pacing.Paused");
                    Assert.True(sim.Host.CurrentTick == tick, what + ": the frame stepped " + (sim.Host.CurrentTick - (tick - n)) + " ticks, the pacer gives " + n);
                    Assert.True(presentation.Pacer.SubTickMicroseconds == sub, what + ": Presentation.Pacer.SubTickMicroseconds is " + presentation.Pacer.SubTickMicroseconds);
                    Assert.True(o.Render.Tick == tick, what + ": RenderFrame.Tick " + o.Render.Tick);
                    List<string> actual = Prims.Copy(o.Render);

                    GraphicsSettings graphics = presentation.Ui.Graphics;
                    List<string> expected = Prims.Copy(reference.Build(Camera, graphics, sub));
                    string? diff = Prims.Diff(expected, actual);
                    Assert.True(diff == null, what + ": the frame's RenderFrame is not Build(camera, Ui.Graphics, Pacer.SubTickMicroseconds) (16 §16.6 step 4): " + diff);

                    // Control: the frame tells the sub-tick from 0 (the two-argument
                    // Build), or the comparison above proves nothing for it.
                    List<string> whole = Prims.Copy(reference.Build(Camera, graphics, 0));
                    if (Prims.Diff(whole, expected) != null)
                    {
                        glides++;
                        if (paused)
                        {
                            pausedGlides++;
                        }
                    }

                    if (paused)
                    {
                        pausedFrames++;
                    }
                }
            }

            Assert.True(pausedFrames > 0, "the run had no paused frame");
            Assert.True(glides >= 100, "only " + glides + " of " + frames + " frames differ between the pacer's sub-tick and 0; the test cannot tell them apart");
            Assert.True(pausedGlides >= 20, "only " + pausedGlides + " of " + pausedFrames + " paused frames differ between the kept sub-tick and 0 (15 §15.8: the partial tick is kept while paused)");
        }
    }
}
