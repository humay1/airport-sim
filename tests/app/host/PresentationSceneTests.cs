using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AirportSim.App.Render;
using AirportSim.App.Ui;
using AirportSim.Sim.Core;
using AirportSim.Sim.Schedule;
using Xunit;
using B = AirportSim.App.Host.Tests.Bundles;

namespace AirportSim.App.Host.Tests
{
    /// <summary>
    /// The composed sim.Schedule, wrapped so a test sees each TryGetFlight the
    /// scene makes (15 §15.16, 16 §16.5). Every member forwards to the composed
    /// schedule unchanged.
    /// </summary>
    internal sealed class RecordingSchedule : IScheduleSystem
    {
        public readonly IScheduleSystem Inner;
        public readonly List<ulong> Asked = new List<ulong>();

        public RecordingSchedule(IScheduleSystem inner)
        {
            Inner = inner;
        }

        public SystemId Id => Inner.Id;

        public string Name => Inner.Name;

        public void Tick(in TickContext ctx)
        {
            Inner.Tick(ctx);
        }

        public ulong ComputeStateHash()
        {
            return Inner.ComputeStateHash();
        }

        public bool TryGetFlight(FlightId id, out FlightRecord flight)
        {
            Asked.Add(id.Value);
            return Inner.TryGetFlight(id, out flight);
        }

        public IReadOnlyList<FlightId> PublishedFlights()
        {
            return Inner.PublishedFlights();
        }

        public IReadOnlyList<FlightId> MovementsBetween(ulong fromInclusive, ulong toExclusive, MovementKind kind)
        {
            return Inner.MovementsBetween(fromInclusive, toExclusive, kind);
        }

        public bool TryGetRotation(FlightId flight, out FlightId counterpart)
        {
            return Inner.TryGetRotation(flight, out counterpart);
        }

        public int PendingInjectionCount(FlightId flight)
        {
            return Inner.PendingInjectionCount(flight);
        }
    }

    /// <summary>One aircraft primitive of one Scene.Build, copied out of the frame.</summary>
    internal readonly struct Sighting
    {
        public Sighting(ulong tick, ulong flight, VisualId visual, Paint paint, bool asked)
        {
            Tick = tick;
            Flight = flight;
            Visual = visual;
            Paint = paint;
            Asked = asked;
        }

        public ulong Tick { get; }

        public ulong Flight { get; }

        public VisualId Visual { get; }

        public Paint Paint { get; }

        /// <summary>Whether that Build called TryGetFlight for this flight on the composed sim.Schedule.</summary>
        public bool Asked { get; }
    }

    /// <summary>
    /// The Phase 1 checkpoints bundle composed by ISimComposer over data/, with
    /// the 15 §15.12 render layout (which matches its airside and flow
    /// fixtures) as render_layout.fixture, presented by IPresentationComposer.
    /// The ComposedSim handed to presentation is the composed one with its
    /// schedule wrapped, built with the eight-argument constructor (16 §16.4),
    /// or the seven-argument one when the test drops Content.
    /// </summary>
    internal sealed class Phase1Show
    {
        /// <summary>Every taxi node, runway and stand of the layout in view (15 §15.12).</summary>
        public static readonly CameraView Overview = new CameraView(new WorldPoint(-512f, 0f), 4096f, 1f);

        public readonly ComposedSim Composed;
        public readonly RecordingSchedule Schedule;
        public readonly Presentation Presentation;

        public Phase1Show(bool content = true, bool schedule = true, RenderLooks? looks = null)
        {
            Composed = ComposeTests.Compose(B.Phase1Bundle(), B.Phase1Content, new RecordingSink());
            Assert.NotNull(Composed.Schedule);
            Assert.NotNull(Composed.Airside);
            Schedule = new RecordingSchedule(Composed.Schedule!);
            IScheduleSystem? passed = schedule ? Schedule : null;
            ComposedSim sim = content
                ? new ComposedSim(Composed.Host, Composed.World, passed, Composed.Airside, Composed.Flow, Composed.Turnaround, Composed.Delay, Composed.Content)
                : new ComposedSim(Composed.Host, Composed.World, passed, Composed.Airside, Composed.Flow, Composed.Turnaround, Composed.Delay);

            MemoryBundle bundle = B.Phase1Bundle().Put("render_layout.fixture", Repo.Read(B.RenderLayout));
            IPresentationComposer composer = HostFactory.CreatePresentationComposer();
            Presentation = looks.HasValue
                ? composer.Compose(sim, bundle, new FakePreferences(), looks.Value)
                : composer.Compose(sim, bundle, new FakePreferences());
        }

        /// <summary>
        /// Steps the composed host through one day, and after every
        /// <paramref name="every"/> ticks builds the scene once and records each
        /// aircraft primitive.
        /// </summary>
        public List<Sighting> Day(uint every = 60)
        {
            var seen = new List<Sighting>();
            GraphicsSettings graphics = Gfx.Of(GraphicsPreset.High);
            for (uint t = 0; t < B.Day; t += every)
            {
                Composed.Host.Step(every);
                Schedule.Asked.Clear();
                RenderFrame frame = Presentation.Scene.Build(Overview, graphics);
                foreach (DrawPrimitive p in frame.Primitives)
                {
                    if (p.Source.Kind == SourceKind.Aircraft)
                    {
                        seen.Add(new Sighting(frame.Tick, p.Source.Id, p.Visual, p.Paint, Schedule.Asked.Contains(p.Source.Id)));
                    }
                }
            }

            Assert.True(seen.Count > 0, "no aircraft was drawn in a whole Phase 1 day");
            return seen;
        }

        /// <summary>The composed schedule's record of a drawn flight, read past the wrapper.</summary>
        public FlightRecord Record(ulong flight)
        {
            Assert.True(Schedule.Inner.TryGetFlight(new FlightId(flight), out FlightRecord record), "the composed schedule does not know drawn flight " + flight);
            return record;
        }

        public static string Show(in Paint p)
        {
            var sb = new StringBuilder();
            foreach (Rgb c in new[] { p.Region0, p.Region1, p.Region2, p.Region3, p.Region4 })
            {
                sb.Append('#').Append(c.R.ToString("X2", CultureInfo.InvariantCulture)).Append(c.G.ToString("X2", CultureInfo.InvariantCulture)).Append(c.B.ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
            }

            return sb.Append("mark ").Append(p.Mark.ToString(CultureInfo.InvariantCulture)).ToString();
        }

        /// <summary>15 §15.16: Region0..4 = Fuselage, Tail, Cheatline, Engines, Logo; Mark = (uint8)Livery.Mark.</summary>
        public static Paint PaintOf(in Livery l)
        {
            return new Paint(l.Fuselage, l.Tail, l.Cheatline, l.Engines, l.Logo, (byte)l.Mark);
        }

        /// <summary>15 §15.16: the Airlines entry with that AirlineId, else DefaultLivery.</summary>
        public static Livery LiveryFor(in RenderLooks looks, AirlineId airline)
        {
            foreach (AirlineLivery a in looks.Airlines)
            {
                if (a.Airline.Value == airline.Value)
                {
                    return a.Livery;
                }
            }

            return looks.DefaultLivery;
        }
    }

    /// <summary>
    /// T-054. What presentation passes into the scene (16 §16.5, Q-130): the
    /// composed schedule and content in RenderSources, and the looks.
    /// </summary>
    public sealed class PresentationSceneTests
    {
        /// <summary>FNV-1a-32 over the code's UTF-8 bytes, as the schedule maps it (11 §11.4, 15 §15.16).</summary>
        private static AirlineId Airline(string code)
        {
            uint h = 0x811C9DC5U;
            unchecked
            {
                foreach (byte b in Encoding.UTF8.GetBytes(code))
                {
                    h ^= b;
                    h *= 0x01000193U;
                }
            }

            return new AirlineId(h);
        }

        private static Livery Livery(byte seed, LogoMark mark)
        {
            return new Livery(new Rgb(seed, 11, 12), new Rgb(13, seed, 14), new Rgb(15, 16, seed), new Rgb(seed, seed, 17), new Rgb(18, seed, seed), mark);
        }

        private static List<Rgb> List(byte seed, int count)
        {
            var list = new List<Rgb>();
            for (int i = 0; i < count; i++)
            {
                list.Add(new Rgb(seed, (byte)i, (byte)(255 - i)));
            }

            return list;
        }

        /// <summary>15 §15.16: AircraftA + min(Ordinal, 5) of the type's size category, else AircraftC.</summary>
        private static VisualId ExpectedVisual(IContentIndex content, in FlightRecord record)
        {
            if (!content.TryGet(record.AircraftType, out AircraftDefinition aircraft))
            {
                return VisualId.AircraftC;
            }

            if (!content.TryGet(aircraft.SizeCategory, out SizeCategoryDefinition size))
            {
                return VisualId.AircraftC;
            }

            return (VisualId)((int)VisualId.AircraftA + Math.Min(size.Ordinal, 5));
        }

        [Fact]
        public void test_presentation_render_sources_carry_schedule_and_content()
        {
            var show = new Phase1Show();
            IContentIndex? content = show.Composed.Content;
            Assert.True(content != null, "ISimComposer.Compose gave no Content (16 §16.4)");
            List<Sighting> seen = show.Day();

            var visuals = new HashSet<VisualId>();
            foreach (Sighting s in seen)
            {
                string what = "tick " + s.Tick + ", flight " + s.Flight;

                // Each Build asks the composed sim.Schedule for every aircraft it draws.
                Assert.True(s.Asked, what + ": Scene.Build drew the aircraft without TryGetFlight on the composed sim.Schedule (16 §16.5: RenderSources.Schedule)");

                // Its visual follows its type's size category in the composed content.
                FlightRecord record = show.Record(s.Flight);
                VisualId expected = ExpectedVisual(content!, record);
                Assert.True(expected == s.Visual, what + " (" + record.AircraftType.Value + "): visual " + s.Visual + ", expected " + expected + " (16 §16.5: RenderSources.Content)");
                visuals.Add(expected);
            }

            // The day must tell the composed content from the AircraftC default.
            Assert.True(visuals.Count >= 2 && visuals.Any(v => v != VisualId.AircraftC), "the day drew only " + string.Join(", ", visuals) + "; the test cannot tell the content from the default");

            // Control: the seven-argument ComposedSim has no Content, so every
            // aircraft is AircraftC (15 §15.16), while the schedule is still asked.
            List<Sighting> noContent = new Phase1Show(content: false).Day();
            foreach (Sighting s in noContent)
            {
                Assert.True(s.Visual == VisualId.AircraftC, "Content null: flight " + s.Flight + " drawn as " + s.Visual + " (15 §15.16: AircraftC)");
            }

            // Control: with sim.Schedule null the visual is AircraftC too, whatever the content.
            List<Sighting> noSchedule = new Phase1Show(schedule: false).Day();
            foreach (Sighting s in noSchedule)
            {
                Assert.False(s.Asked, "Schedule null: the scene still reached the wrapped schedule");
                Assert.True(s.Visual == VisualId.AircraftC, "Schedule null: flight " + s.Flight + " drawn as " + s.Visual + " (15 §15.16: AircraftC)");
            }
        }

        [Fact]
        public void test_presentation_passes_looks_to_the_scene()
        {
            // Liveries for three fixture airlines, none for NVA, and a default
            // unlike DefaultLooks()'s, so every source of Paint is distinguishable.
            var airlines = new List<AirlineLivery>
            {
                new AirlineLivery(Airline("BRW"), Livery(40, LogoMark.Disc)),
                new AirlineLivery(Airline("CTX"), Livery(80, LogoMark.Star)),
                new AirlineLivery(Airline("DLN"), Livery(120, LogoMark.Chevron)),
            };
            airlines.Sort((a, b) => a.Airline.Value.CompareTo(b.Airline.Value));
            var looks = new RenderLooks(Livery(200, LogoMark.Crescent), airlines, List(1, 3), List(2, 2), List(3, 5), List(4, 4), List(5, 2));

            RenderLooks defaults = RenderFactory.DefaultLooks();
            Assert.True(Phase1Show.Show(Phase1Show.PaintOf(defaults.DefaultLivery)) != Phase1Show.Show(Phase1Show.PaintOf(looks.DefaultLivery)), "test setup: the given default livery equals DefaultLooks()'s");

            // The four-argument Compose: each aircraft wears its airline's livery
            // from the given looks, or the given default.
            var show = new Phase1Show(looks: looks);
            var withEntry = new HashSet<uint>();
            bool sawDefault = false;
            foreach (Sighting s in show.Day())
            {
                FlightRecord record = show.Record(s.Flight);
                Paint expected = Phase1Show.PaintOf(Phase1Show.LiveryFor(looks, record.Airline));
                Assert.True(
                    Phase1Show.Show(expected) == Phase1Show.Show(s.Paint),
                    "four-argument Compose, tick " + s.Tick + ", flight " + s.Flight + ": Paint " + Phase1Show.Show(s.Paint) + ", expected " + Phase1Show.Show(expected) + " (16 §16.5: the given looks)");
                if (looks.Airlines.Any(a => a.Airline.Value == record.Airline.Value))
                {
                    withEntry.Add(record.Airline.Value);
                }
                else
                {
                    sawDefault = true;
                }
            }

            Assert.True(withEntry.Count >= 2, "the day drew aircraft of " + withEntry.Count + " airline(s) with a livery entry; the test needs two");
            Assert.True(sawDefault, "the day drew no aircraft of an airline without a livery entry");

            // The three-argument Compose is the four-argument one with DefaultLooks().
            var plain = new Phase1Show();
            foreach (Sighting s in plain.Day())
            {
                FlightRecord record = plain.Record(s.Flight);
                Paint expected = Phase1Show.PaintOf(Phase1Show.LiveryFor(defaults, record.Airline));
                Assert.True(
                    Phase1Show.Show(expected) == Phase1Show.Show(s.Paint),
                    "three-argument Compose, tick " + s.Tick + ", flight " + s.Flight + ": Paint " + Phase1Show.Show(s.Paint) + ", expected DefaultLooks()'s " + Phase1Show.Show(expected));
            }
        }

        [Fact]
        public void test_presentation_four_argument_compose_rejects_null_and_missing_layout()
        {
            // 16 §16.4/§16.5: a null argument throws ArgumentNullException before
            // any check; a missing render layout is a load failure.
            RenderLooks looks = RenderFactory.DefaultLooks();
            var sim = new ComposedSim(new TraceHost(new Trace()), null, null, null, new TraceFlow(new Trace()), null, null);
            var bundle = new MemoryBundle().Put("render_layout.fixture", Repo.Read(B.RenderLayout));
            IPresentationComposer composer = HostFactory.CreatePresentationComposer();

            Assert.Throws<ArgumentNullException>(() => composer.Compose(sim, null!, new FakePreferences(), looks));
            Assert.Throws<ArgumentNullException>(() => composer.Compose(sim, bundle, null!, looks));

            FormatException e = Assert.Throws<FormatException>(() => composer.Compose(sim, new MemoryBundle(), new FakePreferences(), looks));
            Assert.StartsWith("render_layout.fixture: ", e.Message, StringComparison.Ordinal);

            // Control: the same arguments with the layout present compose.
            Presentation p = composer.Compose(sim, bundle, new FakePreferences(), looks);
            Assert.NotNull(p.Frame);
        }
    }
}
