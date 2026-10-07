using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.4 "The exit node" (Q-132) in behaviour: an arrival enters the
    /// taxi graph at its runway's ExitNode at OffRunway (§12.6, §12.8a S3),
    /// waits for a stand there (§12.7), routes exit to stand (§12.4
    /// "Routing"), and plans OnStand with RouteTicks(exit, stand) (§12.3). A
    /// departure's node is always ThresholdNode. A layout whose ExitNode is
    /// its ThresholdNode behaves byte for byte as before (§12.13 determinism
    /// note). Distinct-exit layouts are built in code (ExitLayout); no
    /// fixture file changes.
    /// </summary>
    public sealed class ExitNodeArrivalTests
    {
        private static ulong OnStandPlan(ulong sta, AirsideLayout layout, ushort stand)
        {
            ulong route = Routes.LeastTicks(layout, ExitLayout.Exit, FixtureLayout.StandNode(stand));
            return sta + FixtureLayout.OccupancyTicks + route;
        }

        /// <summary>Steps one tick at a time to <paramref name="until"/>, recording each flight's distinct edges in order.</summary>
        private static Dictionary<ulong, List<ushort>> EdgesTaken(HostRig rig, ulong until)
        {
            var edges = new Dictionary<ulong, List<ushort>>();
            rig.StepEach(until, t =>
            {
                foreach (FlightId f in rig.Airside.TrackedFlights())
                {
                    if (rig.Airside.TryGetTrack(f, out AircraftTrack tr) && tr.OnEdge.HasValue)
                    {
                        if (!edges.TryGetValue(f.Value, out List<ushort>? list))
                        {
                            list = new List<ushort>();
                            edges.Add(f.Value, list);
                        }

                        ushort e = tr.OnEdge.Value.Value;
                        if (list.Count == 0 || list[list.Count - 1] != e)
                        {
                            list.Add(e);
                        }
                    }
                }
            });
            return edges;
        }

        private static List<ushort> Of(Dictionary<ulong, List<ushort>> edges, ulong flight)
        {
            return edges.TryGetValue(flight, out List<ushort>? list) ? list : new List<ushort>();
        }

        [Fact]
        public void test_arrival_leaves_runway_at_exit_node_and_taxis_from_it()
        {
            // Five a320 arrivals 10 minutes apart (no runway or taxi contention).
            // A1 turns round as D1; A2-A4 are rotation-less and keep stands 2-4.
            // A5 finds no free stand, waits at the exit, and takes stand 1 the
            // tick after D1's Pushback (12 §12.7, Q-054).
            var rows = new List<string>(Csv.Pair("A1", "D1", "06:00", "08:00"));
            rows.Add(Csv.Row("A2", "A", "06:10"));
            rows.Add(Csv.Row("A3", "A", "06:20"));
            rows.Add(Csv.Row("A4", "A", "06:30"));
            rows.Add(Csv.Row("A5", "A", "06:40"));
            AirsideLayout layout = ExitLayout.Layout();
            var rig = new HostRig(Csv.Of(rows.ToArray()), layout: layout);
            Dictionary<ulong, List<ushort>> edges = EdgesTaken(rig, 6000UL);

            // The oracle's routes are the hand-computed ones: 55 ticks from the exit to J1.
            for (ushort s = FixtureLayout.S1; s <= FixtureLayout.S4; s++)
            {
                Assert.Equal(ExitLayout.RouteTicks(s), Routes.LeastTicks(layout, ExitLayout.Exit, FixtureLayout.StandNode(s)));
            }

            string[] refs = { "A1", "A2", "A3", "A4" };
            ushort[][] routes =
            {
                new ushort[] { ExitLayout.E7, ExitLayout.E8, ExitLayout.E9, FixtureLayout.E2 },
                new ushort[] { ExitLayout.E7, ExitLayout.E8, ExitLayout.E9, FixtureLayout.E3, FixtureLayout.E4 },
                new ushort[] { ExitLayout.E7, ExitLayout.E8, ExitLayout.E9, FixtureLayout.E3, FixtureLayout.E5 },
                new ushort[] { ExitLayout.E7, ExitLayout.E8, ExitLayout.E9, FixtureLayout.E3, FixtureLayout.E6 },
            };
            for (int i = 0; i < refs.Length; i++)
            {
                ulong a = rig.Id(refs[i]);
                ushort stand = (ushort)(i + 1);
                ulong sta = AirConst.At(6, 10 * i);
                ulong off = sta + FixtureLayout.OccupancyTicks;

                // OffRunway puts it at the exit; at the end of that tick it is on the exit's first edge.
                Rec offRunway = rig.Rec.Milestone(a, FlightMilestone.OffRunway);
                Assert.Equal(off, offRunway.Milestone.ActualTick);
                Assert.True(offRunway.HasTrack, offRunway.ToString());
                Assert.Equal(AircraftLegPhase.Taxiing, offRunway.Track.Phase);
                Assert.Equal(ExitLayout.Exit, offRunway.Track.AtNode!.Value.Value);
                Assert.Equal(ExitLayout.E7, offRunway.Track.OnEdge!.Value.Value);
                Assert.Equal(stand, offRunway.Track.Stand!.Value.Value);
                Assert.Equal(off, offRunway.Track.PhaseEnteredAt);
                Assert.Equal(off + 5UL, offRunway.Track.DueAt);

                // The route is exit to stand, never the threshold's edge 1.
                Assert.Equal(new List<ushort>(routes[i]), Of(edges, a));

                // Planned OnStand uses RouteTicks(exit, stand); unimpeded, actual equals planned.
                ulong planned = sta + FixtureLayout.OccupancyTicks + ExitLayout.RouteTicks(stand);
                Assert.Equal(OnStandPlan(sta, layout, stand), planned);
                Rec onStand = rig.Rec.Milestone(a, FlightMilestone.OnStand);
                Assert.Equal(planned, onStand.Milestone.PlannedTick);
                Assert.Equal(planned, onStand.Milestone.ActualTick);
                Assert.Equal(stand, onStand.Track.Stand!.Value.Value);
                Assert.Equal(FixtureLayout.StandNode(stand), onStand.Track.AtNode!.Value.Value);
                Assert.Equal(planned, onStand.Track.PlannedOnStand);
                Assert.Equal(planned + AirConst.FixtureDoorDelayTicks, rig.Rec.Milestone(a, FlightMilestone.DoorsOpen).Milestone.PlannedTick);
            }

            // A5 waits for a stand at the exit (12 §12.7, §12.9 phase table).
            ulong a5 = rig.Id("A5");
            ulong sta5 = AirConst.At(6, 40);
            ulong off5 = sta5 + FixtureLayout.OccupancyTicks;
            Assert.Equal(off5, rig.Rec.Milestone(a5, FlightMilestone.OffRunway).Milestone.ActualTick);
            List<(Rec Rec, StandUnavailable Evt)> unavailable = rig.Rec.Of<StandUnavailable>(a5);
            Assert.Single(unavailable);
            Rec wait = unavailable[0].Rec;
            Assert.Equal(off5, wait.Tick);
            Assert.True(wait.HasTrack, wait.ToString());
            Assert.Equal(AircraftLegPhase.HeldOnTaxiway, wait.Track.Phase);
            Assert.Equal(ExitLayout.Exit, wait.Track.AtNode!.Value.Value);
            Assert.False(wait.Track.OnEdge.HasValue);
            Assert.False(wait.Track.Stand.HasValue);
            Assert.Equal(FixtureLayout.Runway, wait.Track.Runway!.Value.Value);
            Assert.Equal(off5, wait.Track.PhaseEnteredAt);
            Assert.Equal(AirConst.TickUnscheduled, wait.Track.DueAt);

            // D1 pushes back from stand 1; A5 gets it the next tick and taxis from the exit.
            ulong d1 = rig.Id("D1");
            ulong pushback = rig.Rec.Milestone(d1, FlightMilestone.Pushback).Milestone.ActualTick;
            Assert.True(pushback > off5, "D1 pushed back at " + pushback.ToString(CultureInfo.InvariantCulture) + ", before A5 waited");
            List<(Rec Rec, StandAssigned Evt)> assigned = rig.Rec.Of<StandAssigned>(a5);
            Assert.Single(assigned);
            Rec granted = assigned[0].Rec;
            Assert.Equal(pushback + 1UL, granted.Tick);
            Assert.Equal(FixtureLayout.S1, assigned[0].Evt.Stand!.Value.Value);
            Assert.Equal(AircraftLegPhase.Taxiing, granted.Track.Phase);
            Assert.Equal(ExitLayout.Exit, granted.Track.AtNode!.Value.Value);
            Assert.Equal(ExitLayout.E7, granted.Track.OnEdge!.Value.Value);
            Assert.Equal(pushback + 1UL, granted.Track.PhaseEnteredAt);
            Assert.Equal(pushback + 6UL, granted.Track.DueAt);
            Assert.Equal(new List<ushort>(routes[0]), Of(edges, a5));

            // Planned OnStand is still STA + OccupancyTicks + RouteTicks(exit, stand); actual is late.
            Rec a5OnStand = rig.Rec.Milestone(a5, FlightMilestone.OnStand);
            Assert.Equal(sta5 + FixtureLayout.OccupancyTicks + ExitLayout.RouteTicks(FixtureLayout.S1), a5OnStand.Milestone.PlannedTick);
            Assert.Equal(pushback + 1UL + ExitLayout.RouteTicks(FixtureLayout.S1), a5OnStand.Milestone.ActualTick);

            // Departures are unchanged: stand to ThresholdNode over the fixture's edges,
            // planned TakeoffRoll from RouteTicks(stand, threshold) (12 §12.3).
            Assert.Equal(new List<ushort> { FixtureLayout.E2, FixtureLayout.E1 }, Of(edges, d1));
            Assert.Equal(AirConst.At(8, 0) + FixtureLayout.RouteTicks(FixtureLayout.S1), rig.Rec.Milestone(d1, FlightMilestone.TakeoffRoll).Milestone.PlannedTick);
            Assert.Equal(pushback + FixtureLayout.RouteTicks(FixtureLayout.S1), rig.Rec.Milestone(d1, FlightMilestone.TakeoffRoll).Milestone.ActualTick);
            Assert.True(rig.Rec.Has(d1, FlightMilestone.Airborne));
        }

        [Fact]
        public void test_arrival_held_for_first_edge_holds_at_exit_node()
        {
            // Edge 7 lengthened to 60 ticks: A1 is still on it when A2 leaves
            // the runway, so A2's first edge request (12 §12.6) holds at the
            // exit, caused by its OffRunway (12 §12.11), and is released the
            // tick after A1 leaves the edge (Q-054).
            AirsideLayout layout = ExitLayout.Layout(60U);
            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:00"), Csv.Row("A2", "A", "06:04")), layout: layout);
            ulong a1 = rig.Id("A1");
            ulong a2 = rig.Id("A2");
            rig.RunTo(4200UL);

            ulong off1 = AirConst.At(6, 0) + FixtureLayout.OccupancyTicks;
            ulong off2 = AirConst.At(6, 4) + FixtureLayout.OccupancyTicks;
            Assert.Equal(off2, rig.Rec.Milestone(a2, FlightMilestone.OffRunway).Milestone.ActualTick);

            List<(Rec Rec, AircraftHeldOnTaxiway Evt)> held = rig.Rec.Of<AircraftHeldOnTaxiway>(a2);
            Assert.Single(held);
            Rec h = held[0].Rec;
            Assert.Equal(off2, h.Tick);
            Assert.Equal(ExitLayout.E7, held[0].Evt.Edge.Value);
            Assert.Equal(a1, held[0].Evt.Blocking!.Value.Value);
            Assert.True(h.Env.Cause.HasValue);
            Assert.Equal(rig.Rec.Milestone(a2, FlightMilestone.OffRunway).Id, h.Env.Cause.Id);
            Assert.Equal(AircraftLegPhase.HeldOnTaxiway, h.Track.Phase);
            Assert.Equal(ExitLayout.Exit, h.Track.AtNode!.Value.Value);
            Assert.False(h.Track.OnEdge.HasValue);
            Assert.Equal(FixtureLayout.S2, h.Track.Stand!.Value.Value);
            Assert.Equal(off2, h.Track.PhaseEnteredAt);
            Assert.Equal(AirConst.TickUnscheduled, h.Track.DueAt);

            List<(Rec Rec, AircraftHeldOnTaxiwayReleased Evt)> released = rig.Rec.Of<AircraftHeldOnTaxiwayReleased>(a2);
            Assert.Single(released);
            Assert.Equal(off1 + 60UL + 1UL, released[0].Rec.Tick);
            Assert.Equal(ExitLayout.Exit, released[0].Rec.Track.AtNode!.Value.Value);
            Assert.Equal(ExitLayout.E7, released[0].Rec.Track.OnEdge!.Value.Value);

            Assert.Equal(OnStandPlan(AirConst.At(6, 4), layout, FixtureLayout.S2), rig.Rec.Milestone(a2, FlightMilestone.OnStand).Milestone.PlannedTick);
        }

        [Fact]
        public void test_exit_node_leaves_departures_unchanged()
        {
            // Rotation-less departures only: no arrival ever enters the graph, so
            // the exit changes nothing (the layout is not hashed, 12 §12.12).
            byte[] csv = Csv.Of(
                Csv.Row("D1", "D", "06:35"),
                Csv.Row("D2", "D", "06:35", aircraft: "b789"),
                Csv.Row("D3", "D", "06:36"),
                Csv.Row("D4", "D", "06:40", aircraft: "a388"),
                Csv.Row("D5", "D", "06:40"),
                Csv.Row("D6", "D", "07:00", aircraft: "atr72"));
            var plain = new HostRig(csv);
            var exit = new HostRig(csv, layout: ExitLayout.Layout());
            while (plain.Host.CurrentTick < 6000UL)
            {
                ulong t = plain.Host.CurrentTick;
                plain.Host.Step(1);
                exit.Host.Step(1);
                Assert.True(plain.Airside.ComputeStateHash() == exit.Airside.ComputeStateHash(), "hash differs at t=" + t.ToString(CultureInfo.InvariantCulture));
            }

            Assert.Equal(plain.Rec.Trace(), exit.Rec.Trace());
            foreach (string d in new[] { "D1", "D2", "D3", "D4", "D5", "D6" })
            {
                Assert.True(exit.Rec.Has(exit.Id(d), FlightMilestone.Airborne), d + " never took off");
            }

            foreach ((Rec rec, AircraftHeldForRunway _) in exit.Rec.Of<AircraftHeldForRunway>())
            {
                Assert.Equal(FixtureLayout.Threshold, rec.Track.AtNode!.Value.Value);
            }
        }

        [Fact]
        public void test_exit_node_changes_airside_hash_from_first_off_runway()
        {
            // 12 §12.13 determinism note: no new hashed field; the exit changes
            // fed values from an arrival's OffRunway on, and nothing before.
            var plain = new HostRig(ScheduleFixture.Bytes(), flow: new RuleFlow());
            var exit = new HostRig(ScheduleFixture.Bytes(), layout: ExitLayout.Layout(), flow: new RuleFlow());
            ulong firstDiff = ulong.MaxValue;
            while (firstDiff == ulong.MaxValue && plain.Host.CurrentTick < AirConst.TicksPerDay)
            {
                ulong t = plain.Host.CurrentTick;
                plain.Host.Step(1);
                exit.Host.Step(1);
                if (plain.Airside.ComputeStateHash() != exit.Airside.ComputeStateHash())
                {
                    firstDiff = t;
                }
            }

            ulong firstOff = ulong.MaxValue;
            foreach ((Rec rec, FlightMilestoneReached m) in plain.Rec.Of<FlightMilestoneReached>())
            {
                if (rec.FromAirside && m.Milestone == FlightMilestone.OffRunway && rec.Tick < firstOff)
                {
                    firstOff = rec.Tick;
                }
            }

            Assert.True(firstOff != ulong.MaxValue, "no OffRunway in the fixture day");
            Assert.True(firstDiff == firstOff, "hashes first differ at " + firstDiff.ToString(CultureInfo.InvariantCulture) + ", first OffRunway at " + firstOff.ToString(CultureInfo.InvariantCulture));
        }

        [Fact]
        public void test_exit_layout_day_every_arrival_enters_at_exit_and_plans_from_it()
        {
            // The fixture day on the exit layout: every arrival is at the exit at the
            // end of its OffRunway tick, never on edge 1, and plans OnStand from the
            // exit; every departure holds for the runway at the threshold.
            AirsideLayout layout = ExitLayout.Layout();
            var rig = new HostRig(ScheduleFixture.Bytes(), layout: layout, flow: new RuleFlow());
            int onEdge1 = 0;
            rig.StepEach(AirConst.TicksPerDay, t =>
            {
                foreach (FlightId f in rig.Airside.TrackedFlights())
                {
                    if (rig.Airside.TryGetTrack(f, out AircraftTrack tr) && tr.Kind == MovementKind.Arrival && tr.OnEdge.HasValue && tr.OnEdge.Value.Value == FixtureLayout.E1)
                    {
                        onEdge1++;
                    }
                }
            });
            Assert.Equal(0, onEdge1);

            int offRunways = 0;
            int onStands = 0;
            foreach ((Rec rec, FlightMilestoneReached m) in rig.Rec.Of<FlightMilestoneReached>())
            {
                if (!rec.FromAirside || !rec.HasTrack || rec.Track.Kind != MovementKind.Arrival)
                {
                    continue;
                }

                if (m.Milestone == FlightMilestone.OffRunway)
                {
                    Assert.True(rec.Track.AtNode.HasValue && rec.Track.AtNode.Value.Value == ExitLayout.Exit, rec.ToString());
                    offRunways++;
                }
                else if (m.Milestone == FlightMilestone.OnStand)
                {
                    ulong sta = rig.Flight(rec.Flight).ScheduledTick;
                    ushort stand = rec.Track.Stand!.Value.Value;
                    Assert.True(OnStandPlan(sta, layout, stand) == m.PlannedTick, rec.ToString());
                    onStands++;
                }
            }

            Assert.True(offRunways > 0 && onStands > 0, "no arrival reached the stands in the fixture day");
            foreach ((Rec rec, AircraftHeldForRunway _) in rig.Rec.Of<AircraftHeldForRunway>())
            {
                if (rec.HasTrack && rec.Track.Kind == MovementKind.Departure)
                {
                    Assert.Equal(FixtureLayout.Threshold, rec.Track.AtNode!.Value.Value);
                }
            }
        }

        [Fact]
        public void test_layout_without_exit_node_runs_byte_identical()
        {
            // 12 §12.13: the §12.13 fixture, as is and with "exit_node": 1
            // written out, gives the same checkpoint hashes; so do the
            // five-field and six-field code-built copies (07 L10 kept constructor).
            List<string> lines = new List<string>(System.Text.Encoding.UTF8.GetString(AirsideFixture.Bytes()).Split('\n'));
            Assert.Equal(
                "    { \"id\": 1, \"threshold_node\": 1, \"active_direction_deg\": 270, \"declared_capacity_per_hour\": 15, \"occupancy_ticks\": 10 }",
                lines[3]);
            lines[3] = "    { \"id\": 1, \"threshold_node\": 1, \"active_direction_deg\": 270, \"declared_capacity_per_hour\": 15, \"occupancy_ticks\": 10, \"exit_node\": 1 }";
            AirsideLayout written = AirsideFactory.CreateLayoutLoader().Parse(Csv.Utf8(string.Join("\n", lines)), "exit-written.json");
            Assert.Equal(FixtureLayout.Threshold, written.Runways[0].ExitNode.Value);

            LayoutBuilder six = FixtureLayout.Builder();
            six.Runways.Clear();
            six.RunwayWithExit(FixtureLayout.Runway, FixtureLayout.Threshold, FixtureLayout.Threshold, FixtureLayout.CapacityPerHour, FixtureLayout.OccupancyTicks);

            var rigs = new[]
            {
                new HostRig(ScheduleFixture.Bytes(), layout: AirsideFixture.Parse(), flow: new RuleFlow()),
                new HostRig(ScheduleFixture.Bytes(), layout: written, flow: new RuleFlow()),
                new HostRig(ScheduleFixture.Bytes(), layout: FixtureLayout.Layout(), flow: new RuleFlow()),
                new HostRig(ScheduleFixture.Bytes(), layout: six.Load(), flow: new RuleFlow()),
            };
            while (rigs[0].Host.CurrentTick < AirConst.TicksPerDay)
            {
                ulong t = rigs[0].Host.CurrentTick;
                foreach (HostRig r in rigs)
                {
                    r.Host.Step(1);
                }

                ulong h = rigs[0].Airside.ComputeStateHash();
                for (int i = 1; i < rigs.Length; i++)
                {
                    Assert.True(h == rigs[i].Airside.ComputeStateHash(), "rig " + i.ToString(CultureInfo.InvariantCulture) + " hash differs at t=" + t.ToString(CultureInfo.InvariantCulture));
                }
            }

            Assert.Equal(24, rigs[0].Sink.Recorded.Count);
            for (int i = 1; i < rigs.Length; i++)
            {
                Assert.Equal(rigs[0].Sink.Describe(), rigs[i].Sink.Describe());
                Assert.Equal(rigs[0].Rec.Trace(), rigs[i].Rec.Trace());
                Assert.Equal(AirsideDeterminismTests.Snapshot(rigs[0].Airside), AirsideDeterminismTests.Snapshot(rigs[i].Airside));
            }
        }

        [Fact]
        public void test_exit_layout_determinism_same_seed_same_hashes()
        {
            HostRig a = new HostRig(ScheduleFixture.Bytes(), layout: ExitLayout.Layout(), flow: new RuleFlow());
            HostRig b = new HostRig(ScheduleFixture.Bytes(), layout: ExitLayout.Layout(), flow: new RuleFlow());
            HostRig chunked = new HostRig(ScheduleFixture.Bytes(), layout: ExitLayout.Layout(), flow: new RuleFlow());
            HostRig otherSeed = new HostRig(ScheduleFixture.Bytes(), layout: ExitLayout.Layout(), flow: new RuleFlow(), seed: 0xFFFF_FFFF_FFFF_FFFFUL);
            a.RunTo(AirConst.TicksPerDay);
            b.RunTo(AirConst.TicksPerDay);
            otherSeed.RunTo(AirConst.TicksPerDay);
            uint[] sizes = { 1U, 7U, 599U, 1000U, 13U, 2U, 3571U };
            int k = 0;
            while (chunked.Host.CurrentTick < AirConst.TicksPerDay)
            {
                ulong left = AirConst.TicksPerDay - chunked.Host.CurrentTick;
                uint n = sizes[k++ % sizes.Length];
                chunked.Host.Step(n < left ? n : (uint)left);
            }

            Assert.NotEmpty(a.Rec.Of<AircraftHeldForRunway>());
            Assert.Equal(24, a.Sink.Recorded.Count);
            Assert.Equal(a.Rec.Trace(), b.Rec.Trace());
            Assert.Equal(a.Sink.Describe(), b.Sink.Describe());
            Assert.Equal(AirsideDeterminismTests.Snapshot(a.Airside), AirsideDeterminismTests.Snapshot(b.Airside));
            Assert.Equal(a.Host.WorldStateHash(), b.Host.WorldStateHash());

            Assert.Equal(a.Rec.Trace(), chunked.Rec.Trace());
            Assert.Equal(a.Sink.Describe(), chunked.Sink.Describe());

            // The master seed cannot reach sim.airside, which has no stream (12 §12.12).
            Assert.Equal(a.Rec.Trace(), otherSeed.Rec.Trace());
            Assert.Equal(a.Airside.ComputeStateHash(), otherSeed.Airside.ComputeStateHash());
            Assert.Equal(AirsideDeterminismTests.Snapshot(a.Airside), AirsideDeterminismTests.Snapshot(otherSeed.Airside));
        }
    }
}
