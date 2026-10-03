using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Schedule;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// sim.schedule, the real sim.airside and sim.turnaround in a real host,
    /// with the event recorder at 7 unless <c>record</c> is false. Only the
    /// three 13 §13.11 tests that say sim.airside is registered use it (Q-087):
    /// the doors-close handshake test, the allocation test and the headless
    /// day. sim.airside gets tests/fixtures/airside/phase1-single-runway.json
    /// (12 §12.13), AirsideRules supplied directly (12 §12.12a), flow null and
    /// turnaroundRegistered true; with flow null there is no boarding hold.
    /// </summary>
    internal sealed class AirsideRig
    {
        public const string LayoutSourceName = "phase1-single-runway.json";

        /// <summary>The suite's AirsideRules values (Test Author's, 13 §13.11): hold 10 min, doors open 2 min after OnStand.</summary>
        public const uint HoldMinutes = 10U;
        public const uint DoorDelayMinutes = 2U;
        public const ulong DoorDelayTicks = DoorDelayMinutes * TConst.TicksPerMinute;

        public readonly ISimHost Host;
        public readonly IScheduleSystem Schedule;
        public readonly IAirsideSystem Airside;
        public readonly ITurnaroundSystem Turnaround;
        public readonly Recorder? Events;
        public readonly RecordingCheckpointSink Sink = new RecordingCheckpointSink();
        public readonly Dictionary<string, ulong> Ids;
        public readonly Dictionary<ulong, Movement> Moves = new Dictionary<ulong, Movement>();

        public AirsideRig(byte[] csv, TurnaroundSetup setup, bool record = true, ulong seed = 0x5EED_0022UL)
        {
            Ids = Csv.Ids(csv);
            foreach ((string _, Movement m) in Csv.Rows(csv, 1))
            {
                Moves[m.Flight] = m;
            }

            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(seed, TurnContent.Index(), Sink, new NullLog()));
            ScheduleTable table = ScheduleFactory.CreateLoader().Load(csv, ScheduleFixture.SourceName);
            Schedule = ScheduleFactory.CreateSystem(b.Services, table, null);
            AirsideLayout layout = AirsideFactory.CreateLayoutLoader().Parse(Repo.Read("tests", "fixtures", "airside", LayoutSourceName), LayoutSourceName);
            Airside = AirsideFactory.CreateSystem(b.Services, layout, new AirsideRules(HoldMinutes, DoorDelayMinutes), Schedule, null, true);
            Turnaround = TurnaroundFactory.CreateSystem(b.Services, setup, Schedule);
            b.Register(Schedule);
            b.Register(Airside);
            b.Register(Turnaround);
            if (record)
            {
                Events = new Recorder(b.Services.Events, TConst.RecorderSystemId) { Turnaround = Turnaround };
                b.Register(new ProbeSystem(TConst.RecorderSystemId));
            }

            Host = b.Build();
        }

        public Recorder Rec => Events!;

        public ulong Id(string flightRef)
        {
            return Ids[flightRef];
        }

        public void RunTo(ulong tick)
        {
            while (Host.CurrentTick < tick)
            {
                Host.Step((uint)Math.Min(tick - Host.CurrentTick, 1000000UL));
            }
        }

        public TurnaroundJob Job(string flightRef, JobKind kind)
        {
            ulong flight = Id(flightRef);
            Assert.True(
                Turnaround.TryGetJob(TConst.Job(flight, kind), out TurnaroundJob j),
                "TryGetJob(" + kind + " of " + flightRef + ") returned false at tick " + Host.CurrentTick.ToString(CultureInfo.InvariantCulture));
            return j;
        }

        /// <summary>sim.airside's milestone m for the flight; fails unless there is exactly one.</summary>
        public Rec AirsideMilestone(ulong flight, FlightMilestone m)
        {
            List<Rec> found = Rec.All.FindAll(r => r.Env.Source.Value == TConst.AirsideSystemId && r.Flight == flight && r.IsMilestone(m));
            Assert.True(found.Count == 1, "expected exactly one " + m + " from sim.airside for flight " + flight.ToString(CultureInfo.InvariantCulture) + ", found " + found.Count.ToString(CultureInfo.InvariantCulture) + "\n" + Rec.Dump());
            return found[0];
        }
    }
}
