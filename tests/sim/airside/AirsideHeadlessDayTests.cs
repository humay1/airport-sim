using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using AirportSim.Sim.Schedule;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 07 "Testing": the headless day. One full sim-day of
    /// tests/fixtures/schedule/phase0-200.csv against the §12.13 layout with
    /// sim.turnaround absent (the fallback path), asserting the spec's own
    /// rules after every tick and over the day's events: §12.3 ownership and
    /// PlannedTick, §12.5 pacing and pairing, §12.6 edge capacity and
    /// holding at nodes, §12.7 stands, §12.8 fallback and boarding hold,
    /// §12.9 track shape, 10 §10.3 rule 2. Run once without sim.flow and once
    /// with a fake that keeps many departures' passengers outstanding.
    /// </summary>
    public sealed class AirsideHeadlessDayTests
    {
        private const ulong HoldMinutes = 10UL;

        private static readonly FlightMilestone[] ArrivalOrder =
        {
            FlightMilestone.InboundAirborne, FlightMilestone.Landed, FlightMilestone.OffRunway, FlightMilestone.OnStand, FlightMilestone.DoorsOpen,
        };

        private static readonly FlightMilestone[] DepartureOrder =
        {
            FlightMilestone.OnStand, FlightMilestone.DoorsClosed, FlightMilestone.Pushback, FlightMilestone.TakeoffRoll, FlightMilestone.Airborne,
        };

        [Fact]
        public void test_airside_headless_day_holds_invariants_without_flow()
        {
            Run(withFlow: false);
        }

        [Fact]
        public void test_airside_headless_day_holds_invariants_with_boarding_holds()
        {
            Run(withFlow: true);
        }

        private static void Run(bool withFlow)
        {
            RuleFlow? flow = withFlow ? new RuleFlow() : null;
            var rig = new HostRig(ScheduleFixture.Bytes(), holdMinutes: (uint)HoldMinutes, flow: flow);
            AirsideLayout layout = rig.Layout;
            var standOfNode = new Dictionary<ushort, ushort>();
            foreach (StandDef s in layout.Stands)
            {
                standOfNode.Add(s.Node.Value, s.Id.Value);
            }

            var edges = new Dictionary<ushort, TaxiEdgeDef>();
            foreach (TaxiEdgeDef e in layout.Edges)
            {
                edges.Add(e.Id.Value, e);
            }

            int scanned = 0;
            int openRunwayHolds = 0;
            rig.StepEach(AirConst.TicksPerDay, t =>
            {
                for (; scanned < rig.Rec.All.Count; scanned++)
                {
                    object p = rig.Rec.All[scanned].Payload;
                    if (p is AircraftHeldForRunway)
                    {
                        openRunwayHolds++;
                    }
                    else if (p is AircraftHeldForRunwayReleased)
                    {
                        openRunwayHolds--;
                    }
                }

                string? why = TickViolation(rig, t, withFlow, standOfNode, edges, openRunwayHolds);
                Assert.True(why == null, why);
            });

            AssertDay(rig, flow, standOfNode);
        }

        private static string? TickViolation(HostRig rig, ulong t, bool withFlow, Dictionary<ushort, ushort> standOfNode, Dictionary<ushort, TaxiEdgeDef> edges, int openRunwayHolds)
        {
            IAirsideSystem a = rig.Airside;
            string at = "t=" + t.ToString(CultureInfo.InvariantCulture) + ": ";
            string? taxi = AirsideAsserts.TaxiViolation(a, t);
            if (taxi != null)
            {
                return taxi;
            }

            int queue = a.RunwayQueueLength(new RunwayId(FixtureLayout.Runway));
            if (queue != openRunwayHolds)
            {
                return at + "RunwayQueueLength " + queue + " but " + openRunwayHolds + " runway holds open";
            }

            IReadOnlyList<FlightId> tracked = a.TrackedFlights();
            for (int i = 0; i < tracked.Count; i++)
            {
                if (i > 0 && tracked[i - 1].Value >= tracked[i].Value)
                {
                    return at + "TrackedFlights not strictly ascending";
                }

                if (!a.TryGetTrack(tracked[i], out AircraftTrack tr) || tr.Flight != tracked[i])
                {
                    return at + "no matching track for tracked flight " + tracked[i].Value;
                }

                if (!rig.Schedule.TryGetFlight(tr.Flight, out FlightRecord rec) || rec.Kind != tr.Kind)
                {
                    return at + "track kind disagrees with the schedule: " + Show.Track(tr);
                }

                if (tr.OnEdge.HasValue)
                {
                    TaxiEdgeDef e = edges[tr.OnEdge.Value.Value];
                    bool endpoint = tr.AtNode.HasValue && (tr.AtNode.Value == e.From || (e.Bidirectional && tr.AtNode.Value == e.To));
                    if (!endpoint)
                    {
                        return at + "on an edge, but AtNode is not the entry node: " + Show.Track(tr);
                    }

                    if (tr.EdgeProgress < Fx.Zero || tr.EdgeProgress > Fx.One)
                    {
                        return at + "EdgeProgress out of 0..1: " + Show.Track(tr);
                    }
                }

                bool beforeLanding = tr.Phase == AircraftLegPhase.AwaitingApproach || tr.Phase == AircraftLegPhase.HeldForRunway;
                if (tr.Kind == MovementKind.Arrival && beforeLanding && (tr.AtNode.HasValue || tr.OnEdge.HasValue))
                {
                    return at + "arrival before Landed is not off-graph: " + Show.Track(tr);
                }

                if (tr.Kind == MovementKind.Departure && tr.Phase == AircraftLegPhase.HeldForRunway
                    && (tr.OnEdge.HasValue || !tr.AtNode.HasValue || tr.AtNode.Value.Value != FixtureLayout.Threshold))
                {
                    return at + "departure held for the runway away from the threshold: " + Show.Track(tr);
                }

                if (tr.Phase == AircraftLegPhase.OnStand
                    && (!tr.Stand.HasValue || tr.OnEdge.HasValue || !tr.AtNode.HasValue || tr.AtNode.Value.Value != FixtureLayout.StandNode(tr.Stand.Value.Value)))
                {
                    return at + "OnStand but not at its stand's node: " + Show.Track(tr);
                }

                if (!withFlow && tr.PassengerHoldSince != AirConst.TickUnscheduled)
                {
                    return at + "boarding hold without sim.flow: " + Show.Track(tr);
                }

                // 12 §12.9 (Q-062): always EventRef.None with turnaroundRegistered false.
                if (tr.RecordedCause.HasValue)
                {
                    return at + "RecordedCause set in the fallback: " + Show.Track(tr);
                }

                // 12 §12.8 (Q-062): an arrival leaves tracked state at its handoff,
                // so it is never tracked beside its departure.
                if (tr.Kind == MovementKind.Arrival && rec.HasRotation && a.TryGetTrack(rec.Rotation, out _))
                {
                    return at + "arrival " + tr.Flight.Value + " still tracked after its handoff to " + rec.Rotation.Value;
                }
            }

            IReadOnlyList<StandId> free = a.FreeStands();
            for (int i = 0; i < free.Count; i++)
            {
                if (i > 0 && free[i - 1].Value >= free[i].Value)
                {
                    return at + "FreeStands not strictly ascending: " + Show.Ids(free);
                }

                if (!a.TryGetStand(free[i], out StandState st) || st.Occupant.HasValue)
                {
                    return at + "free stand " + free[i].Value + " has an occupant";
                }
            }

            var occupants = new HashSet<ulong>();
            for (ushort s = FixtureLayout.S1; s <= FixtureLayout.S4; s++)
            {
                if (!a.TryGetStand(new StandId(s), out StandState st))
                {
                    return at + "TryGetStand(" + s + ") false";
                }

                if (st.Occupant.HasValue && !occupants.Add(st.Occupant.Value.Value))
                {
                    return at + "flight " + st.Occupant.Value.Value + " occupies two stands";
                }
            }

            return null;
        }

        private static void AssertDay(HostRig rig, RuleFlow? flow, Dictionary<ushort, ushort> standOfNode)
        {
            Recorder rec = rig.Rec;
            string trace = string.Join("\n", rec.Trace());

            // 12 §12.13: the declared capacity makes AircraftHeldForRunway fire in one day.
            Assert.NotEmpty(rec.Of<AircraftHeldForRunway>());

            // 12 §12.5 (Q-063): queuePosition is the queue's length just after
            // the flight joins (1-based), and 0 on every release. One runway,
            // so the queue length is the holds open in event order.
            int queued = 0;
            foreach (Rec r in rec.All)
            {
                if (r.Payload is AircraftHeldForRunway h)
                {
                    queued++;
                    Assert.True(h.QueuePosition == queued, "queuePosition " + h.QueuePosition + ", expected " + queued + ": " + r);
                }
                else if (r.Payload is AircraftHeldForRunwayReleased rel)
                {
                    queued--;
                    Assert.True(rel.QueuePosition == 0, "release queuePosition " + rel.QueuePosition + ": " + r);
                }
            }

            // Arrivals first, so a departure can check against its arrival.
            var ids = new SortedSet<ulong>();
            foreach (Rec r in rec.All)
            {
                if (r.FromAirside)
                {
                    ids.Add(r.Flight);
                }
            }

            var flights = new List<ulong>();
            foreach (MovementKind kind in new[] { MovementKind.Arrival, MovementKind.Departure })
            {
                foreach (ulong f in ids)
                {
                    if (rig.Flight(f).Kind == kind)
                    {
                        flights.Add(f);
                    }
                }
            }

            var slotTicks = new List<ulong>();
            var pushbackIds = new Dictionary<EventId, (ushort Stand, ulong Tick)>();
            var arrivalStand = new Dictionary<ulong, ushort>();
            var doorsOpenAt = new Dictionary<ulong, ulong>();
            int doorsClosed = 0;
            foreach (ulong f in flights)
            {
                FlightRecord fr = rig.Flight(f);
                List<Rec> ms = rec.Milestones(f);
                FlightMilestone[] order = fr.Kind == MovementKind.Arrival ? ArrivalOrder : DepartureOrder;
                int last = -1;
                ulong lastTick = 0UL;
                var seen = new Dictionary<FlightMilestone, Rec>();
                foreach (Rec r in ms)
                {
                    FlightMilestone m = r.Milestone.Milestone;
                    int idx = Array.IndexOf(order, m);
                    Assert.True(idx >= 0, fr.Kind + " flight " + f + " got " + m + "\n" + trace);
                    Assert.True(idx > last, "flight " + f + ": " + m + " out of order or repeated");
                    Assert.True(r.Milestone.ActualTick >= lastTick && r.Milestone.ActualTick == r.Tick, "flight " + f + ": " + m + " actual tick");
                    last = idx;
                    lastTick = r.Milestone.ActualTick;
                    seen.Add(m, r);
                }

                ulong sched = fr.ScheduledTick;
                ulong occ = FixtureLayout.OccupancyTicks;
                if (fr.Kind == MovementKind.Arrival)
                {
                    // Every arrival this module tracks started with InboundAirborne,
                    // at max(0, STA - CRUISE_LEAD_TICKS) (12 §12.6, Q-048).
                    Assert.True(seen.TryGetValue(FlightMilestone.InboundAirborne, out Rec? ia), "arrival " + f + " never got InboundAirborne");
                    ulong airborne = sched >= AirConst.CruiseLead ? sched - AirConst.CruiseLead : 0UL;
                    Assert.Equal(airborne, ia!.Milestone.PlannedTick);
                    Assert.Equal(airborne, ia.Milestone.ActualTick);

                    if (seen.TryGetValue(FlightMilestone.Landed, out Rec? landed))
                    {
                        Assert.Equal(sched, landed.Milestone.PlannedTick);
                        Assert.True(landed.Milestone.ActualTick >= sched, "landed before STA: " + landed);
                        slotTicks.Add(landed.Milestone.ActualTick);
                        if (seen.TryGetValue(FlightMilestone.OffRunway, out Rec? off))
                        {
                            Assert.Equal(sched + occ, off.Milestone.PlannedTick);
                            Assert.Equal(landed.Milestone.ActualTick + occ, off.Milestone.ActualTick);
                        }
                    }

                    if (seen.TryGetValue(FlightMilestone.OnStand, out Rec? on))
                    {
                        Assert.True(on.HasTrack && on.Track.Stand.HasValue, "arrival on stand with no stand: " + on);
                        ushort s = on.Track.Stand!.Value.Value;
                        arrivalStand[f] = s;
                        Assert.Equal(sched + occ + FixtureLayout.RouteTicks(s), on.Milestone.PlannedTick);
                        Assert.True(AirsideContent.Fits(fr.AircraftType.Value, FixtureLayout.MaxSize(s)), "incompatible stand: " + on);
                        if (seen.TryGetValue(FlightMilestone.DoorsOpen, out Rec? open))
                        {
                            Assert.Equal(on.Milestone.PlannedTick + AirConst.FixtureDoorDelayTicks, open.Milestone.PlannedTick);
                            Assert.Equal(on.Milestone.ActualTick + AirConst.FixtureDoorDelayTicks, open.Milestone.ActualTick);
                            doorsOpenAt[f] = open.Milestone.ActualTick;
                        }
                    }
                }
                else
                {
                    ulong minTurn = (ulong)Fx.Floor(fr.MinTurnaround) * AirConst.TicksPerMinute;
                    Rec on = seen[FlightMilestone.OnStand];
                    ulong due = sched >= minTurn ? sched - minTurn : 0UL;
                    Assert.Equal(due, on.Milestone.PlannedTick);
                    if (seen.TryGetValue(FlightMilestone.DoorsClosed, out Rec? closed))
                    {
                        doorsClosed++;
                        Assert.Equal(sched, closed.Milestone.PlannedTick);
                        Rec push = seen[FlightMilestone.Pushback];
                        Assert.Equal(sched, push.Milestone.PlannedTick);
                        Assert.Equal(closed.Milestone.ActualTick, push.Milestone.ActualTick);
                        Assert.True(push.HasTrack && push.Track.AtNode.HasValue, "untracked or off-graph at Pushback: " + push);
                        ushort s = standOfNode[push.Track.AtNode!.Value.Value];
                        pushbackIds.Add(push.Id, (s, push.Tick));
                        if (fr.HasRotation && arrivalStand.TryGetValue(fr.Rotation.Value, out ushort arrS))
                        {
                            Assert.Equal(arrS, s);
                        }

                        if (seen.TryGetValue(FlightMilestone.TakeoffRoll, out Rec? roll))
                        {
                            Assert.Equal(sched + FixtureLayout.RouteTicks(s), roll.Milestone.PlannedTick);
                            slotTicks.Add(roll.Milestone.ActualTick);
                            if (seen.TryGetValue(FlightMilestone.Airborne, out Rec? air))
                            {
                                Assert.Equal(roll.Milestone.PlannedTick + occ, air.Milestone.PlannedTick);
                                Assert.Equal(roll.Milestone.ActualTick + occ, air.Milestone.ActualTick);
                            }
                        }

                        AssertDoorsClosePoint(rec, flow, f, on, closed);
                    }

                    // The fallback: the handoff lands at the arrival's DoorsOpen + its MinTurnaround.
                    if (fr.HasRotation)
                    {
                        FlightRecord arr = rig.Flight(fr.Rotation.Value);
                        Assert.True(doorsOpenAt.ContainsKey(arr.Id.Value), "departure " + f + " created before its arrival's DoorsOpen");
                        ulong arrTurn = (ulong)Fx.Floor(arr.MinTurnaround) * AirConst.TicksPerMinute;
                        Assert.Equal(doorsOpenAt[arr.Id.Value] + arrTurn, on.Milestone.ActualTick);
                    }
                    else
                    {
                        // Rotation-less: at the due tick, or later if it waited for a stand.
                        Assert.True(on.Milestone.ActualTick >= due);
                    }
                }

                // Holds pair up, and nothing is left open once a departure is airborne.
                var runway = AirsideAsserts.Pairs<AircraftHeldForRunway, AircraftHeldForRunwayReleased>(rec, f);
                var taxi = AirsideAsserts.Pairs<AircraftHeldOnTaxiway, AircraftHeldOnTaxiwayReleased>(rec, f);
                var stand = AirsideAsserts.Pairs<StandUnavailable, StandAssigned>(rec, f);
                var pax = AirsideAsserts.Pairs<DepartureHeldForPassengers, DepartureHeldForPassengersReleased>(rec, f);
                if (seen.ContainsKey(FlightMilestone.Airborne))
                {
                    Assert.Equal(rec.Of<AircraftHeldForRunway>(f).Count, runway.Count);
                    Assert.Equal(rec.Of<AircraftHeldOnTaxiway>(f).Count, taxi.Count);
                    Assert.Equal(rec.Of<DepartureHeldForPassengers>(f).Count, pax.Count);
                }

                foreach (var (_, r) in runway)
                {
                    Rec? next = rec.All.Find(x => x.FromAirside && x.Flight == f && x.Id.Tick == r.Id.Tick && x.Id.Sequence == r.Id.Sequence + 1U);
                    Assert.True(
                        next != null && (next.IsMilestone(FlightMilestone.Landed) || next.IsMilestone(FlightMilestone.TakeoffRoll)),
                        "runway release not immediately before Landed/TakeoffRoll: " + r);
                }

                foreach (var (h, r) in taxi)
                {
                    // 12 §12.6 (Q-060): set on the opener, naming another flight; null on the release.
                    var held = (AircraftHeldOnTaxiway)h.Payload;
                    Assert.True(held.Blocking.HasValue && held.Blocking.Value.Value != f, "taxi hold without another blocking flight: " + h);
                    Assert.False(((AircraftHeldOnTaxiwayReleased)r.Payload).Blocking.HasValue, "taxi release carries a blocker: " + r);
                }

                foreach ((Rec h, Rec r) in stand)
                {
                    var un = (StandUnavailable)h.Payload;
                    Assert.False(un.Stand.HasValue || un.Occupying.HasValue, "StandUnavailable must carry null stand and occupant: " + h);
                    var asg = (StandAssigned)r.Payload;
                    Assert.True(asg.Stand.HasValue && !asg.Occupying.HasValue, "StandAssigned shape: " + r);
                }
            }

            // Every StandAssigned was caused by the Pushback that vacated that
            // stand, one tick earlier (12 §12.7, Q-054); no command runs here.
            foreach (var (r, asg) in rec.Of<StandAssigned>())
            {
                Assert.True(pushbackIds.TryGetValue(r.Env.Cause.Id, out (ushort Stand, ulong Tick) p), "StandAssigned not caused by a Pushback: " + r);
                Assert.Equal(p.Tick + 1UL, r.Tick);
                Assert.Equal(p.Stand, asg.Stand!.Value.Value);
            }

            // 12 §12.5 pacing: slots at least minSeparationTicks apart, arrivals and departures pooled.
            slotTicks.Sort();
            ulong sep = AirConst.MinSeparation(FixtureLayout.CapacityPerHour);
            for (int i = 1; i < slotTicks.Count; i++)
            {
                Assert.True(slotTicks[i] - slotTicks[i - 1] >= sep, "runway slots " + slotTicks[i - 1] + " and " + slotTicks[i] + " closer than " + sep);
            }

            BRW100(rig);
            if (flow == null)
            {
                Assert.Empty(rec.Of<DepartureHeldForPassengers>());
                Assert.Empty(rec.Of<DepartureHeldForPassengersReleased>());
            }
            else
            {
                Assert.NotEmpty(rec.Of<DepartureHeldForPassengers>());
                Assert.Equal(0L, flow.InjectCalls);
                Assert.Equal((long)doorsClosed, flow.AbsorbCalls);
            }
        }

        /// <summary>
        /// The doors-close point is the departure's OnStand tick (fallback).
        /// Without flow the doors close there; with the RuleFlow they close
        /// there iff nobody is outstanding, else at the first later tick that
        /// either has nobody outstanding or reaches DueAt (12 §12.8).
        /// </summary>
        private static void AssertDoorsClosePoint(Recorder rec, RuleFlow? flow, ulong f, Rec onStand, Rec closed)
        {
            ulong point = onStand.Tick;
            var held = rec.Of<DepartureHeldForPassengers>(f);
            var released = rec.Of<DepartureHeldForPassengersReleased>(f);
            if (flow == null || !RuleFlow.Outstanding(point, f))
            {
                Assert.Empty(held);
                Assert.Equal(point, closed.Milestone.ActualTick);
                return;
            }

            Assert.Single(held);
            Assert.Single(released);
            Assert.Equal(point, held[0].Rec.Tick);
            Assert.Equal(onStand.Id, held[0].Rec.Env.Cause.Id);
            Assert.Equal(RuleFlow.Count(f), held[0].Evt.Outstanding);
            Assert.Equal(RuleFlow.Node(f), held[0].Evt.HeldAt!.Value.Value);
            Assert.Equal(point, held[0].Rec.Track.PassengerHoldSince);
            Assert.Equal(point + (HoldMinutes * AirConst.TicksPerMinute), held[0].Rec.Track.DueAt);

            ulong due = point + (HoldMinutes * AirConst.TicksPerMinute);
            ulong expected = point + 1UL;
            while (expected < due && RuleFlow.Outstanding(expected, f))
            {
                expected++;
            }

            Assert.Equal(expected, released[0].Rec.Tick);
            Assert.Equal(RuleFlow.Outstanding(expected, f) ? RuleFlow.Count(f) : 0, released[0].Evt.Outstanding);
            Assert.False(released[0].Evt.HeldAt.HasValue);
            Assert.Equal(expected, closed.Milestone.ActualTick);
        }

        /// <summary>The fixture's lone day-0 departure: created on stand at STD - MinTurnaround (12 §12.7), all stands free then.</summary>
        private static void BRW100(HostRig rig)
        {
            ulong id = rig.Id("BRW100");
            FlightRecord fr = rig.Flight(id);
            Assert.False(fr.HasRotation);
            ulong created = fr.ScheduledTick - ((ulong)Fx.Floor(fr.MinTurnaround) * AirConst.TicksPerMinute);
            Rec on = rig.Rec.Milestone(id, FlightMilestone.OnStand);
            Assert.Equal(created, on.Milestone.PlannedTick);
            Assert.Equal(created, on.Milestone.ActualTick);
        }
    }
}
