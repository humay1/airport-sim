using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using AirportSim.Sim.Schedule;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.11 "The Cause of each emitted event" (Q-078), over the fixture
    /// day: tests/fixtures/schedule/phase0-200.csv on the §12.13 layout, with
    /// sim.turnaround absent (the fallback), a fake flow that holds many
    /// departures, DoorsOpenDelayMinutes 2 and every MinTurnaround nonzero.
    /// So the table's zero-delay and handshake rows do not apply here; those
    /// are ZeroDoorDelayTests' and HandshakeTests'. No command is submitted,
    /// so every freed stand was freed by a Pushback.
    /// </summary>
    public sealed class AirsideEventTests
    {
        [Fact]
        public void test_airside_event_causes_follow_cause_table()
        {
            var rig = new HostRig(ScheduleFixture.Bytes(), flow: new RuleFlow());
            rig.RunTo(AirConst.TicksPerDay);
            Recorder rec = rig.Rec;

            List<Rec> mine = rec.All.FindAll(r => r.FromAirside);
            Assert.NotEmpty(mine);

            // Each Pushback's stand, read from the track at Pushback (AtNode is the stand node).
            var standOfNode = new Dictionary<ushort, ushort>();
            foreach (StandDef s in rig.Layout.Stands)
            {
                standOfNode.Add(s.Node.Value, s.Id.Value);
            }

            var pushbackStand = new Dictionary<EventId, ushort>();
            foreach (Rec r in mine)
            {
                if (r.IsMilestone(FlightMilestone.Pushback))
                {
                    Assert.True(r.HasTrack && r.Track.AtNode.HasValue, "untracked or off-graph at Pushback: " + r);
                    pushbackStand.Add(r.Id, standOfNode[r.Track.AtNode!.Value.Value]);
                }
            }

            // Opening events of the three families that close with their opener.
            var openHold = new Dictionary<(ulong Flight, string Family), EventId>();
            int checkedStandAssigned = 0;
            int checkedHeldTaxi = 0;
            foreach (Rec r in mine)
            {
                EventRef cause = r.Env.Cause;
                string what = r.ToString();
                switch (r.Payload)
                {
                    case AircraftHeldForRunway _:
                        AssertNone(cause, what);
                        openHold[(r.Flight, "runway")] = r.Id;
                        break;
                    case AircraftHeldForRunwayReleased _:
                        AssertIs(cause, openHold[(r.Flight, "runway")], what);
                        break;
                    case AircraftHeldOnTaxiway _:
                        AssertIs(cause, TaxiHoldTrigger(mine, r), what);
                        openHold[(r.Flight, "taxi")] = r.Id;
                        checkedHeldTaxi++;
                        break;
                    case AircraftHeldOnTaxiwayReleased _:
                        AssertIs(cause, openHold[(r.Flight, "taxi")], what);
                        break;
                    case DepartureHeldForPassengers _:
                        // Fallback: the departure's OnStand, just emitted.
                        AssertIs(cause, SameTick(mine, r, FlightMilestone.OnStand), what);
                        openHold[(r.Flight, "pax")] = r.Id;
                        break;
                    case DepartureHeldForPassengersReleased _:
                        AssertIs(cause, openHold[(r.Flight, "pax")], what);
                        break;
                    case StandUnavailable _:
                        AssertIs(cause, SameTick(mine, r, FlightMilestone.OffRunway), what);
                        break;
                    case StandAssigned a:
                        // The stand's VacatedBy: the Pushback that freed it, one tick
                        // earlier (12 §12.7, Q-054), never the StandUnavailable.
                        Assert.True(cause.HasValue, "StandAssigned without a Cause: " + what);
                        Assert.True(pushbackStand.TryGetValue(cause.Id, out ushort freed), "StandAssigned not caused by a Pushback: " + what);
                        Assert.Equal(a.Stand!.Value.Value, freed);
                        Assert.Equal(r.Tick - 1UL, cause.Id.Tick);
                        checkedStandAssigned++;
                        break;
                    case FlightMilestoneReached m:
                        AssertIs(cause, MilestoneCause(rig, mine, pushbackStand, r, m), what);
                        break;
                }
            }

            // The day exercised the families the table distinguishes.
            Assert.True(checkedStandAssigned > 0, "no StandAssigned in the fixture day");
            Assert.True(checkedHeldTaxi > 0, "no taxi hold in the fixture day");
            Assert.Contains(mine, r => r.Payload is DepartureHeldForPassengers);
            Assert.Contains(mine, r => r.Payload is AircraftHeldForRunway);
        }

        /// <summary>The table's row for a FlightMilestoneReached, in this build (fallback, delays nonzero).</summary>
        private static EventRef MilestoneCause(HostRig rig, List<Rec> mine, Dictionary<EventId, ushort> pushbackStand, Rec r, FlightMilestoneReached m)
        {
            FlightRecord fr = rig.Flight(r.Flight);
            switch (m.Milestone)
            {
                case FlightMilestone.InboundAirborne:
                case FlightMilestone.OffRunway:
                case FlightMilestone.Airborne:
                case FlightMilestone.DoorsOpen: // DoorsOpenDelayMinutes is 2
                    return EventRef.None;
                case FlightMilestone.Landed:
                case FlightMilestone.TakeoffRoll:
                    Rec? released = Find(mine, x => x.Flight == r.Flight && x.Tick == r.Tick && x.Payload is AircraftHeldForRunwayReleased);
                    return released != null ? new EventRef(released.Id, true) : EventRef.None;
                case FlightMilestone.OnStand:
                    if (fr.Kind == MovementKind.Arrival)
                    {
                        // After its edges (TraversalTicks >= 1): None.
                        return EventRef.None;
                    }

                    if (fr.HasRotation)
                    {
                        // Fallback handoff at the arrival's DoorsOpen + the arrival's
                        // MinTurnaround (12 §12.8): None when that is nonzero, else the
                        // DoorsOpen just emitted.
                        FlightRecord arrival = rig.Flight(fr.Rotation.Value);
                        return Fx.Floor(arrival.MinTurnaround) > 0
                            ? EventRef.None
                            : new EventRef(Find(mine, x => x.Flight == arrival.Id.Value && x.Tick == r.Tick && x.IsMilestone(FlightMilestone.DoorsOpen))!.Id, true);
                    }

                    // Rotation-less: a new request at its start tick has None; from the
                    // stand-wait queue it is the stand's VacatedBy, the Pushback that
                    // freed it the tick before.
                    ulong minTurn = (ulong)Fx.Floor(fr.MinTurnaround) * AirConst.TicksPerMinute;
                    ulong due = fr.ScheduledTick >= minTurn ? fr.ScheduledTick - minTurn : 0UL;
                    ulong start = fr.DayIndex == 0U ? due : System.Math.Max(due, fr.PublishTick + 1UL);
                    if (r.Tick == start)
                    {
                        return EventRef.None;
                    }

                    ushort stand = r.Track.Stand!.Value.Value;
                    foreach (KeyValuePair<EventId, ushort> p in pushbackStand)
                    {
                        if (p.Value == stand && p.Key.Tick == r.Tick - 1UL)
                        {
                            return new EventRef(p.Key, true);
                        }
                    }

                    Assert.Fail("rotation-less OnStand from the queue with no freeing Pushback: " + r);
                    return EventRef.None;
                case FlightMilestone.DoorsClosed:
                    Rec? paxReleased = Find(mine, x => x.Flight == r.Flight && x.Tick == r.Tick && x.Payload is DepartureHeldForPassengersReleased);
                    return paxReleased != null ? new EventRef(paxReleased.Id, true) : SameTick(mine, r, FlightMilestone.OnStand);
                case FlightMilestone.Pushback:
                    return SameTick(mine, r, FlightMilestone.DoorsClosed);
                default:
                    Assert.Fail("sim.airside emitted a milestone it does not own: " + r);
                    return EventRef.None;
            }
        }

        /// <summary>
        /// The table's AircraftHeldOnTaxiway row: the event that placed the
        /// flight at a node this tick, just emitted (its Pushback, its
        /// StandAssigned, or its OffRunway when the stand came in S5 of that
        /// tick), or None at a node reached along the route.
        /// </summary>
        private static EventRef TaxiHoldTrigger(List<Rec> mine, Rec hold)
        {
            // At most one applies: a departure is placed by its Pushback; an
            // arrival either got its stand in S5 of its OffRunway tick, or
            // joined the stand-wait queue then and gets its StandAssigned in a
            // later tick (a new waiter joins after the queue walk, §12.7).
            Rec? placed = Find(mine, x => x.Flight == hold.Flight && x.Tick == hold.Tick && x.Id.CompareTo(hold.Id) < 0
                && (x.IsMilestone(FlightMilestone.Pushback) || x.IsMilestone(FlightMilestone.OffRunway) || x.Payload is StandAssigned));
            return placed != null ? new EventRef(placed.Id, true) : EventRef.None;
        }

        /// <summary>The flight's milestone of this kind, just emitted in the same tick; it must exist.</summary>
        private static EventRef SameTick(List<Rec> mine, Rec r, FlightMilestone m)
        {
            Rec? found = Find(mine, x => x.Flight == r.Flight && x.Tick == r.Tick && x.IsMilestone(m) && x.Id.CompareTo(r.Id) < 0);
            Assert.True(found != null, "no " + m + " just emitted before " + r);
            return new EventRef(found!.Id, true);
        }

        private static Rec? Find(List<Rec> list, System.Predicate<Rec> match)
        {
            return list.Find(match);
        }

        private static void AssertNone(EventRef cause, string what)
        {
            Assert.False(cause.HasValue, "expected Cause None: " + what);
        }

        private static void AssertIs(EventRef cause, EventRef expected, string what)
        {
            if (!expected.HasValue)
            {
                AssertNone(cause, what);
                return;
            }

            Assert.True(cause.HasValue, "expected Cause " + Show.Cause(expected) + ", got None: " + what);
            Assert.True(cause.Id.Equals(expected.Id), string.Format(CultureInfo.InvariantCulture, "expected Cause {0}, got {1}: {2}", Show.Cause(expected), Show.Cause(cause), what));
        }

        private static void AssertIs(EventRef cause, EventId expected, string what)
        {
            AssertIs(cause, new EventRef(expected, true), what);
        }
    }
}
