using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// The harness-internal boarding stand-in in sim.airside's empty registry slot: it
    /// calls <c>flow.Absorb(PHASE0_DEPARTURE_SINK, flight)</c> for each departure at its
    /// STD, and hashes 0. Spec: 19-interfaces-harness.md §19.2a (Q-043).
    /// </summary>
    internal sealed class BoardingStandIn : ISimSystem
    {
        /// <summary>The only Sink node of tests/fixtures/flow/phase0-landside.flow.json.</summary>
        internal static readonly NodeId PHASE0_DEPARTURE_SINK = new NodeId(9);

        private const ulong TicksPerDay = SimConstants.TICKS_PER_SIM_DAY;

        private readonly IScheduleSystem _schedule;
        private readonly IFlowSystem _flow;

        // The day list and its STDs, parallel and in ascending (STD, FlightId) order.
        // Both are replaced only at a day boundary.
        private IReadOnlyList<FlightId> _day = Array.Empty<FlightId>();
        private ulong[] _stds = Array.Empty<ulong>();
        private int _cursor;

        internal BoardingStandIn(IScheduleSystem schedule, IFlowSystem flow)
        {
            _schedule = schedule;
            _flow = flow;
        }

        public SystemId Id => new SystemId(3);

        public string Name => "harness.boarding-standin";

        public void Tick(in TickContext ctx)
        {
            ulong t = ctx.Tick;
            if (t % TicksPerDay == 0)
            {
                _day = _schedule.MovementsBetween(t, t + TicksPerDay, MovementKind.Departure);
                var stds = new ulong[_day.Count];
                for (int i = 0; i < stds.Length; i++)
                {
                    if (!_schedule.TryGetFlight(_day[i], out FlightRecord record))
                    {
                        throw new InvalidOperationException("harness: day-list flight " + _day[i].Value + " not found");
                    }
                    stds[i] = record.ScheduledTick;
                }
                _stds = stds;
                _cursor = 0;
            }

            while (_cursor < _stds.Length && _stds[_cursor] <= t)
            {
                if (_stds[_cursor] == t)
                {
                    _flow.Absorb(PHASE0_DEPARTURE_SINK, _day[_cursor]);
                }
                _cursor++;
            }
        }

        public ulong ComputeStateHash()
        {
            return 0;
        }
    }
}
