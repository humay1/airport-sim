using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>Assertions several test classes share, each citing the rule it checks.</summary>
    internal static class AirsideAsserts
    {
        /// <summary>
        /// 12 §12.5: a held movement is released "immediately before the
        /// milestone", with Cause set to the hold event. The milestone is at
        /// <paramref name="tick"/>, the release is the event just before it
        /// in the same tick, and its Cause is this flight's latest earlier
        /// AircraftHeldForRunway.
        /// </summary>
        public static void RunwayReleasedInto(Recorder rec, ulong flight, FlightMilestone milestone, ulong tick)
        {
            Rec ms = rec.Milestone(flight, milestone);
            Assert.Equal(tick, ms.Milestone.ActualTick);
            Assert.Equal(tick, ms.Tick);

            List<(Rec Rec, AircraftHeldForRunwayReleased Evt)> releases = rec.Of<AircraftHeldForRunwayReleased>(flight);
            (Rec Rec, AircraftHeldForRunwayReleased Evt)? release = null;
            foreach (var r in releases)
            {
                if (r.Rec.Id.Tick == ms.Id.Tick && r.Rec.Id.Sequence + 1U == ms.Id.Sequence)
                {
                    release = r;
                }
            }

            Assert.True(release.HasValue, "no AircraftHeldForRunwayReleased immediately before " + milestone + " of flight " + flight.ToString(CultureInfo.InvariantCulture) + ":\n" + string.Join("\n", rec.Trace()));
            Assert.Equal(FixtureLayout.Runway, release!.Value.Evt.Runway.Value);
            Assert.True(release.Value.Rec.Env.Cause.HasValue, "release carries no Cause");

            (Rec Rec, AircraftHeldForRunway Evt)? hold = null;
            foreach (var h in rec.Of<AircraftHeldForRunway>(flight))
            {
                if (h.Rec.Id.CompareTo(release.Value.Rec.Id) < 0)
                {
                    hold = h;
                }
            }

            Assert.True(hold.HasValue, "release without an earlier hold");
            Assert.Equal(hold!.Value.Rec.Id, release.Value.Rec.Env.Cause.Id);
            Assert.Equal(FixtureLayout.Runway, hold.Value.Evt.Runway.Value);
        }

        /// <summary>
        /// 12 §12.6 TAXI_EDGE_CAPACITY = 1, and "holds at the node, never
        /// mid-edge". Returns null if the tracked aircraft satisfy both.
        /// </summary>
        public static string? TaxiViolation(IAirsideSystem airside, ulong tick)
        {
            var onEdge = new Dictionary<ushort, ulong>();
            foreach (FlightId f in airside.TrackedFlights())
            {
                if (!airside.TryGetTrack(f, out AircraftTrack t))
                {
                    return string.Format(CultureInfo.InvariantCulture, "t={0}: tracked flight {1} has no track", tick, f.Value);
                }

                if (t.OnEdge.HasValue)
                {
                    ushort e = t.OnEdge.Value.Value;
                    if (onEdge.TryGetValue(e, out ulong other))
                    {
                        return string.Format(CultureInfo.InvariantCulture, "t={0}: flights {1} and {2} both on edge {3}", tick, other, f.Value, e);
                    }

                    onEdge.Add(e, f.Value);
                }

                if (t.Phase == AircraftLegPhase.HeldOnTaxiway && (t.OnEdge.HasValue || !t.AtNode.HasValue))
                {
                    return string.Format(CultureInfo.InvariantCulture, "t={0}: held on taxiway but not at a node: {1}", tick, Show.Track(t));
                }
            }

            return null;
        }

        /// <summary>The flight's hold/release pairs of one kind, checked for pairing and Cause (10 §10.3 rule 2, 12 §12.5/§12.6).</summary>
        public static List<(Rec Hold, Rec Release)> Pairs<THold, TRelease>(Recorder rec, ulong flight)
            where THold : struct, ISimEvent
            where TRelease : struct, ISimEvent
        {
            var result = new List<(Rec Hold, Rec Release)>();
            Rec? open = null;
            foreach (Rec r in rec.All)
            {
                if (r.Flight != flight)
                {
                    continue;
                }

                if (r.Payload is THold)
                {
                    Assert.True(open == null, "second " + typeof(THold).Name + " while one is open: " + r);
                    open = r;
                }
                else if (r.Payload is TRelease)
                {
                    Assert.True(open != null, typeof(TRelease).Name + " without an open " + typeof(THold).Name + ": " + r);
                    Assert.True(r.Env.Cause.HasValue, "release carries no Cause: " + r);
                    Assert.Equal(open!.Id, r.Env.Cause.Id);
                    result.Add((open, r));
                    open = null;
                }
            }

            return result;
        }
    }
}
