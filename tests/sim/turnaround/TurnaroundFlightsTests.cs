using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 13 §13.10 "Retention and the capacity bound" (Q-092):
    /// TURNAROUND_FLIGHTS_CAPACITY = 4096 flights with jobs in state, storage
    /// preallocated at construction. The OnStand that would create jobs for
    /// flight 4097 throws SimInvariantException naming its FlightId, before
    /// creating any of its jobs; the 4096 creations before it allocate nothing.
    /// </summary>
    public sealed class TurnaroundFlightsTests
    {
        private const int Capacity = 4096;
        private const int PerTick = 100;

        [Fact]
        public void test_turnaround_flights_overflow_throws_sim_invariant()
        {
            // 4097 rotation-less departures on day 1, STDs spread from 12:00 so
            // that their FlightPlanPublished (one day ahead) never falls in the
            // ticks below. The refs sort in row order, so FlightId =
            // 100000 + i + 1 (11 §11.3). With no vehicles at all, every job
            // blocks and no flight ever finishes.
            var rows = new string[Capacity + 1];
            var ids = new ulong[Capacity + 1];
            var script = new List<(ulong Tick, ulong Flight, ulong Planned)>();
            for (int i = 0; i <= Capacity; i++)
            {
                int minute = 720 + (i / 6);
                string sched = (minute / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (minute % 60).ToString("00", CultureInfo.InvariantCulture);
                rows[i] = Csv.Row("F" + i.ToString("0000", CultureInfo.InvariantCulture), "D", sched, day: "1");
                ids[i] = TConst.DayStride + (ulong)i + 1UL;
                ulong std = TConst.TicksPerDay + ((ulong)minute * TConst.TicksPerMinute);

                // Flights 0-99 at tick 1 (warm-up), then 100 a tick from tick 2
                // to 41; flight 4096, the overflow, alone at tick 50.
                ulong tick = i == Capacity ? 50UL : 1UL + (ulong)(i / PerTick);
                script.Add((tick, ids[i], std - 350UL));
            }

            var rig = new Rig(Csv.Of(rows), Setups.Unit(Setups.Plenty(0)), record: false, script: script);
            rig.RunTo(2UL);
            Assert.Equal(6, rig.Turnaround.JobsForFlight(new FlightId(ids[0])).Count);

            // Ticks 2-41: no checkpoint, no schedule day boundary or publication.
            long start = Allocation.Start();
            rig.Host.Step(40);
            long bytes = Allocation.Since(start);
            Assert.True(bytes == 0L, "creating jobs for flights 101-4096 allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes");
            Assert.Equal(Capacity, rig.Driver.Published);
            Assert.Equal(6, rig.Turnaround.JobsForFlight(new FlightId(ids[Capacity - 1])).Count);

            rig.RunTo(50UL);
            SimInvariantException e = Assert.Throws<SimInvariantException>(() => rig.Host.Step(1));
            Assert.Equal(50UL, e.Tick);
            string overflow = ids[Capacity].ToString(CultureInfo.InvariantCulture);
            Exception? inner = e.InnerException;
            Assert.True(inner is SimInvariantException, "sim.turnaround did not throw SimInvariantException: " + e);
            Assert.True(inner!.Message.Contains(overflow, StringComparison.Ordinal), "the exception does not name flight " + overflow + ": " + inner.Message);

            // Before creating any of its jobs.
            Assert.Empty(rig.Turnaround.JobsForFlight(new FlightId(ids[Capacity])));
            foreach (JobKind k in TConst.DepartureJobs)
            {
                Assert.False(rig.Turnaround.TryGetJob(TConst.Job(ids[Capacity], k), out _));
            }

            Assert.Equal(6, rig.Turnaround.JobsForFlight(new FlightId(ids[0])).Count);
        }
    }
}
