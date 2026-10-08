using System;
using System.Collections.Generic;
using System.Text;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using Xunit;
using B = AirportSim.App.Host.Tests.Bundles;

namespace AirportSim.App.Host.Tests
{
    /// <summary>
    /// T-058 (Q-132). The playtest bundle's airside.fixture is 19 §19.2c's
    /// substituted copy of 12 §12.13's fixture plus the runway exit: runway 1's
    /// exit_node 4, junctions 4, 5 and 6, and the one-way edges 7 (4 to 5),
    /// 8 (5 to 6) and 9 (6 to 2). Its render_layout.fixture is
    /// playtest-layout.json (16 §16.3, 15 §15.23), the layout with positions for
    /// those taxi nodes, and the host composes and presents the two together.
    /// </summary>
    public sealed class PlaytestBundleTests
    {
        private const string SingleRunway = "tests/fixtures/airside/phase1-single-runway.json";

        /// <summary>19 §19.2c's three substitutions, applied to max_aircraft_size_category values only.</summary>
        private static byte[] Substituted(byte[] source)
        {
            string text = Encoding.UTF8.GetString(source);
            var map = new (string From, string To)[] { ("medium", "size_c"), ("heavy", "size_e"), ("super", "size_f") };
            int replaced = 0;
            foreach ((string from, string to) in map)
            {
                string key = "\"max_aircraft_size_category\": \"" + from + "\"";
                int count = (text.Length - text.Replace(key, string.Empty, StringComparison.Ordinal).Length) / key.Length;
                replaced += count;
                text = text.Replace(key, "\"max_aircraft_size_category\": \"" + to + "\"", StringComparison.Ordinal);
            }

            Assert.True(replaced == 4, "12 §12.13's fixture has " + replaced + " substitutable size categories; the test expects its four stands");
            return B.Utf8(text);
        }

        private static AirsideLayout Parse(byte[] file)
        {
            return AirsideFactory.CreateLayoutLoader().Parse(file, "airside.fixture");
        }

        private static string Show(in TaxiEdgeDef e)
        {
            return "edge " + e.Id.Value + " " + e.From.Value + "->" + e.To.Value + " " + e.TraversalTicks + (e.Bidirectional ? " both" : " one-way");
        }

        private static void AssertBundleFileIsTheSubstitutedCopyWithTheExit(byte[] file)
        {
            AirsideLayout playtest = Parse(file);
            AirsideLayout copy = Parse(Substituted(Repo.Read(SingleRunway)));

            // Runway 1: unchanged, except that the exit is node 4 (12 §12.4).
            Assert.Single(copy.Runways);
            Assert.Single(playtest.Runways);
            RunwayDef was = copy.Runways[0];
            RunwayDef r = playtest.Runways[0];
            Assert.Equal(was.ThresholdNode.Value, was.ExitNode.Value);
            Assert.Equal(was.Id.Value, r.Id.Value);
            Assert.Equal(was.ThresholdNode.Value, r.ThresholdNode.Value);
            Assert.Equal(was.ActiveDirectionDeg, r.ActiveDirectionDeg);
            Assert.Equal(was.DeclaredCapacityPerHour, r.DeclaredCapacityPerHour);
            Assert.Equal(was.OccupancyTicks, r.OccupancyTicks);
            Assert.True(r.ExitNode.Value == 4, "runway 1's ExitNode is " + r.ExitNode.Value + ", 19 §19.2c gives 4");

            // Nodes: the copy's, plus junctions 4, 5 and 6; Load sorts by id.
            var nodes = new List<(ushort, TaxiNodeKind)>();
            foreach (TaxiNodeDef n in copy.Nodes)
            {
                nodes.Add((n.Id.Value, n.Kind));
            }

            nodes.Add((4, TaxiNodeKind.Junction));
            nodes.Add((5, TaxiNodeKind.Junction));
            nodes.Add((6, TaxiNodeKind.Junction));
            nodes.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            var actualNodes = new List<(ushort, TaxiNodeKind)>();
            foreach (TaxiNodeDef n in playtest.Nodes)
            {
                actualNodes.Add((n.Id.Value, n.Kind));
            }

            Assert.Equal(nodes, actualNodes);

            // Edges: the copy's, plus the three one-way edges toward junction 2.
            var edges = new List<string>();
            foreach (TaxiEdgeDef e in copy.Edges)
            {
                edges.Add(Show(e));
            }

            edges.Add("edge 7 4->5 5 one-way");
            edges.Add("edge 8 5->6 40 one-way");
            edges.Add("edge 9 6->2 10 one-way");
            var actualEdges = new List<string>();
            foreach (TaxiEdgeDef e in playtest.Edges)
            {
                actualEdges.Add(Show(e));
            }

            Assert.Equal(edges, actualEdges);

            // Stands: the substituted copy's, unchanged.
            Assert.Equal(copy.Stands.Count, playtest.Stands.Count);
            for (int i = 0; i < copy.Stands.Count; i++)
            {
                Assert.Equal(copy.Stands[i].Id.Value, playtest.Stands[i].Id.Value);
                Assert.Equal(copy.Stands[i].Node.Value, playtest.Stands[i].Node.Value);
                Assert.Equal(copy.Stands[i].MaxAircraftSizeCategory, playtest.Stands[i].MaxAircraftSizeCategory);
                Assert.Equal(copy.Stands[i].DepartureSinkNode.Value, playtest.Stands[i].DepartureSinkNode.Value);
            }

            string text = Encoding.UTF8.GetString(file);
            foreach (string gone in new[] { "\"medium\"", "\"heavy\"", "\"super\"" })
            {
                Assert.DoesNotContain(gone, text, StringComparison.Ordinal);
            }
        }

        /// <summary>Consecutive distinct taxi edges a flight was seen on, at the end of each tick.</summary>
        private static void Note(Dictionary<ulong, List<ushort>> seen, ulong flight, ushort edge)
        {
            if (!seen.TryGetValue(flight, out List<ushort>? list))
            {
                list = new List<ushort>();
                seen.Add(flight, list);
            }

            if (list.Count == 0 || list[list.Count - 1] != edge)
            {
                list.Add(edge);
            }
        }

        [Fact]
        public void test_playtest_bundle_lands_arrivals_at_the_far_exit()
        {
            MemoryBundle bundle = B.PlaytestBundle();

            // 1) The bundle's airside.fixture (16 §16.3: the Phase 1 checkpoints
            // bundle's) parses with ExitNode 4 and 19 §19.2c's lines, and is
            // otherwise the substituted copy of 12 §12.13's fixture.
            AssertBundleFileIsTheSubstitutedCopyWithTheExit(bundle.Bytes("airside.fixture"));

            // 2) The host composes the bundle over data/ and presents it with
            // playtest-layout.json (16 §16.3, §16.5).
            ComposedSim sim = ComposeTests.Compose(bundle, B.Phase1Content, new RecordingSink());
            Assert.NotNull(sim.Airside);
            Assert.True(sim.Airside!.Layout().Runways[0].ExitNode.Value == 4, "the composed sim.airside's runway 1 has ExitNode " + sim.Airside.Layout().Runways[0].ExitNode.Value);
            Presentation presentation = HostFactory.CreatePresentationComposer().Compose(sim, bundle, new FakePreferences());
            Assert.NotNull(presentation.Frame);

            // Control: phase1-layout.json has no position for taxi nodes 4 to 6,
            // so with this airside.fixture it fails 15 §15.4 check 4 at
            // presentation assembly. That is why the two files change together.
            MemoryBundle stale = B.PlaytestBundle().Put("render_layout.fixture", Repo.Read(B.RenderLayout));
            ComposedSim staleSim = ComposeTests.Compose(stale, B.Phase1Content, new RecordingSink());
            FormatException e = Assert.Throws<FormatException>(() => HostFactory.CreatePresentationComposer().Compose(staleSim, stale, new FakePreferences()));
            Assert.StartsWith("render_layout.fixture: ", e.Message, StringComparison.Ordinal);

            // 3) Over a day, every arrival leaves the runway at the exit and taxis
            // 4 -> 5 -> 6 -> 2 before its stand, and never takes edge 1; no
            // departure takes the one-way exit edges (12 §12.13).
            var arrivals = new Dictionary<ulong, List<ushort>>();
            var departures = new Dictionary<ulong, List<ushort>>();
            for (uint t = 0; t < B.Day; t++)
            {
                sim.Host.Step(1);
                foreach (FlightId f in sim.Airside.TrackedFlights())
                {
                    Assert.True(sim.Airside.TryGetTrack(f, out AircraftTrack track), "tracked flight " + f.Value + " has no track");
                    if (!track.OnEdge.HasValue)
                    {
                        continue;
                    }

                    Note(track.Kind == MovementKind.Arrival ? arrivals : departures, f.Value, track.OnEdge.Value.Value);
                }
            }

            int exited = 0;
            foreach (KeyValuePair<ulong, List<ushort>> a in arrivals)
            {
                string route = "arrival " + a.Key + " took edges " + string.Join(", ", a.Value);
                Assert.True(!a.Value.Contains(1), route + ": an arrival used edge 1 (12 §12.13: arrivals taxi exit -> 5 -> 6 -> 2)");

                // A route still under way at the day's end is a prefix of 7, 8, 9.
                ushort[] exit = { 7, 8, 9 };
                for (int i = 0; i < Math.Min(3, a.Value.Count); i++)
                {
                    Assert.True(a.Value[i] == exit[i], route + ": it does not start 7, 8, 9, from the exit node 4 to junction 2");
                }

                if (a.Value.Count > 3)
                {
                    exited++;
                }
            }

            Assert.True(exited >= 10, "only " + exited + " arrivals taxied from the exit on past junction 2 in a day");

            int usedEdge1 = 0;
            foreach (KeyValuePair<ulong, List<ushort>> d in departures)
            {
                string route = "departure " + d.Key + " took edges " + string.Join(", ", d.Value);
                Assert.True(!d.Value.Contains(7) && !d.Value.Contains(8) && !d.Value.Contains(9), route + ": a departure used a one-way exit edge");
                if (d.Value.Contains(1))
                {
                    usedEdge1++;
                }
            }

            Assert.True(usedEdge1 >= 10, "only " + usedEdge1 + " departures taxied to the threshold over edge 1 in a day");
        }
    }
}
