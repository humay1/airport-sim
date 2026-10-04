using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;

namespace AirportSim.App.Ui.Tests
{
    // Test doubles for the controller's ILaneCommandSink and the production
    // sink's ISimHost and IFlowSystem (17 §17.7: "Controller tests use a fake
    // sink. Sink tests use a fake host and a fake flow."). Every sim member
    // that 17 §17.6 does not list is recorded as a violation and throws, so a
    // call is caught even if the caller swallows the exception.

    internal sealed class CallGuard
    {
        public readonly List<string> Violations = new List<string>();

        public Exception Forbidden(string member)
        {
            Violations.Add(member);
            return new InvalidOperationException("app.ui called " + member + ", which 17 §17.6 does not list");
        }
    }

    /// <summary>Records every Request, in call order.</summary>
    internal sealed class RecordingSink : ILaneCommandSink
    {
        public readonly List<(uint Node, int Delta)> Requests = new List<(uint Node, int Delta)>();

        public void Request(NodeId node, int delta)
        {
            Requests.Add((node.Value, delta));
        }

        public string Show()
        {
            var parts = new List<string>();
            foreach ((uint node, int delta) in Requests)
            {
                parts.Add("(" + node + "," + (delta > 0 ? "+" : string.Empty) + delta + ")");
            }

            return "[" + string.Join(" ", parts) + "]";
        }
    }

    /// <summary>Counts requests without allocating, for the 17 §17.9 allocation test.</summary>
    internal sealed class CountingSink : ILaneCommandSink
    {
        public long Count;
        public long NodeSum;
        public long DeltaSum;

        public void Request(NodeId node, int delta)
        {
            Count++;
            NodeSum += node.Value;
            DeltaSum += delta;
        }
    }

    /// <summary>One TrySubmit call as the fake host saw it, payload copied.</summary>
    internal readonly struct Submitted
    {
        public Submitted(in Command cmd)
        {
            Tick = cmd.Tick;
            Issuer = cmd.Issuer.Value;
            Kind = cmd.Kind;
            Payload = cmd.Payload == null ? null : (byte[])cmd.Payload.Clone();
        }

        public ulong Tick { get; }

        public ushort Issuer { get; }

        public CommandKind Kind { get; }

        public byte[]? Payload { get; }

        /// <summary>08 §8.7's SetServersOpen layout: NodeId.Value uint32 LE, then count int32 LE.</summary>
        public uint Node
        {
            get
            {
                byte[] p = Payload!;
                return (uint)(p[0] | (p[1] << 8) | (p[2] << 16) | (p[3] << 24));
            }
        }

        public int Count
        {
            get
            {
                byte[] p = Payload!;
                return p[4] | (p[5] << 8) | (p[6] << 16) | (p[7] << 24);
            }
        }

        public override string ToString()
        {
            string payload = Payload == null ? "null" : BitConverter.ToString(Payload);
            return "{ Tick " + Tick + ", Issuer " + Issuer + ", " + Kind + ", payload " + payload + " }";
        }
    }

    /// <summary>
    /// An ISimHost whose permitted members are CurrentTick and TrySubmit
    /// (17 §17.6). TrySubmit records the command and answers with Answer:
    /// None admits, anything else rejects with that reason.
    /// </summary>
    internal sealed class FakeHost : ISimHost
    {
        public readonly CallGuard Guard;
        public readonly List<Submitted> Submits = new List<Submitted>();
        public ulong Tick;
        public CommandRejection Answer = CommandRejection.None;

        public FakeHost(CallGuard guard, ulong tick)
        {
            Guard = guard;
            Tick = tick;
        }

        public ulong CurrentTick => Tick;

        public void Step(uint ticks)
        {
            throw Guard.Forbidden("ISimHost.Step");
        }

        public ulong WorldStateHash()
        {
            throw Guard.Forbidden("ISimHost.WorldStateHash");
        }

        public bool TrySubmit(in Command cmd, out CommandRejection reason)
        {
            Submits.Add(new Submitted(cmd));
            reason = Answer;
            return Answer == CommandRejection.None;
        }

        public IReadOnlyList<Command> CommandLogSince(ulong tick)
        {
            throw Guard.Forbidden("ISimHost.CommandLogSince");
        }

        public string Show()
        {
            var parts = new List<string>();
            foreach (Submitted s in Submits)
            {
                parts.Add(s.ToString());
            }

            return "[" + string.Join("; ", parts) + "]";
        }
    }

    /// <summary>
    /// The FakeHost's allocation-free twin: TrySubmit decodes the payload in
    /// place and keeps running sums, for the 17 §17.9 allocation test.
    /// </summary>
    internal sealed class CountingHost : ISimHost
    {
        public readonly CallGuard Guard;
        public ulong Tick;
        public CommandRejection Answer = CommandRejection.None;
        public long Count;
        public long CountSum;
        public long BadShape;

        public CountingHost(CallGuard guard, ulong tick)
        {
            Guard = guard;
            Tick = tick;
        }

        public ulong CurrentTick => Tick;

        public void Step(uint ticks)
        {
            throw Guard.Forbidden("ISimHost.Step");
        }

        public ulong WorldStateHash()
        {
            throw Guard.Forbidden("ISimHost.WorldStateHash");
        }

        public bool TrySubmit(in Command cmd, out CommandRejection reason)
        {
            Count++;
            byte[] p = cmd.Payload;
            if (p == null || p.Length != 8 || cmd.Kind != CommandKind.SetServersOpen || cmd.Tick != Tick + UiConst.CommandMinLeadTicks)
            {
                BadShape++;
            }
            else
            {
                CountSum += p[4] | (p[5] << 8) | (p[6] << 16) | (p[7] << 24);
            }

            reason = Answer;
            return Answer == CommandRejection.None;
        }

        public IReadOnlyList<Command> CommandLogSince(ulong tick)
        {
            throw Guard.Forbidden("ISimHost.CommandLogSince");
        }
    }

    /// <summary>
    /// An IFlowSystem whose only permitted member is TryGetLaneState (17
    /// §17.6). A node set with Lanes is a Queue; any other node answers false.
    /// </summary>
    internal sealed class FakeFlow : IFlowSystem
    {
        public readonly CallGuard Guard;
        public long LaneCalls;

        private readonly Dictionary<uint, LaneState> _lanes = new Dictionary<uint, LaneState>();

        public FakeFlow(CallGuard guard)
        {
            Guard = guard;
        }

        public SystemId Id => throw Guard.Forbidden("IFlowSystem.Id");

        public string Name => throw Guard.Forbidden("IFlowSystem.Name");

        public FakeFlow Lanes(uint node, int serverCount, int serversOpen)
        {
            _lanes[node] = new LaneState(serverCount, serversOpen);
            return this;
        }

        public void Tick(in TickContext ctx)
        {
            throw Guard.Forbidden("IFlowSystem.Tick");
        }

        public ulong ComputeStateHash()
        {
            throw Guard.Forbidden("IFlowSystem.ComputeStateHash");
        }

        public int Population(NodeId node)
        {
            throw Guard.Forbidden("IFlowSystem.Population");
        }

        public Fx PredictedWaitMinutes(NodeId node)
        {
            throw Guard.Forbidden("IFlowSystem.PredictedWaitMinutes");
        }

        public int PopulationForFlight(FlightId flight, FlowDirection direction)
        {
            throw Guard.Forbidden("IFlowSystem.PopulationForFlight");
        }

        public IReadOnlyList<CohortId> CohortsAt(NodeId node)
        {
            throw Guard.Forbidden("IFlowSystem.CohortsAt");
        }

        public bool TryGetCohort(CohortId id, out PassengerCohort cohort)
        {
            throw Guard.Forbidden("IFlowSystem.TryGetCohort");
        }

        public bool TryGetOutstanding(FlightId flight, out OutstandingPassengers outstanding)
        {
            throw Guard.Forbidden("IFlowSystem.TryGetOutstanding");
        }

        public bool TryGetLaneState(NodeId node, out LaneState lanes)
        {
            LaneCalls++;
            return _lanes.TryGetValue(node.Value, out lanes);
        }

        public NodeKind KindOf(NodeId node)
        {
            throw Guard.Forbidden("IFlowSystem.KindOf");
        }

        public CohortId Inject(in CohortKey key, int count, NodeId at)
        {
            throw Guard.Forbidden("IFlowSystem.Inject");
        }

        public int Absorb(NodeId sink, FlightId flight)
        {
            throw Guard.Forbidden("IFlowSystem.Absorb");
        }

        public void SetPromoted(NodeId node, bool promoted)
        {
            throw Guard.Forbidden("IFlowSystem.SetPromoted");
        }

        public IReadOnlyList<AgentView> AgentsAt(NodeId node)
        {
            throw Guard.Forbidden("IFlowSystem.AgentsAt");
        }
    }

    /// <summary>
    /// Forwards 17 §17.6's ISimHost members to a real host, for the
    /// integration tests' production sink. Step is not forwarded: the test's
    /// frame loop drives the real host itself (16 §16.6).
    /// </summary>
    internal sealed class GuardedHost : ISimHost
    {
        public long Submits;

        private readonly ISimHost _inner;
        private readonly CallGuard _guard;

        public GuardedHost(ISimHost inner, CallGuard guard)
        {
            _inner = inner;
            _guard = guard;
        }

        public ulong CurrentTick => _inner.CurrentTick;

        public void Step(uint ticks)
        {
            throw _guard.Forbidden("ISimHost.Step");
        }

        public ulong WorldStateHash()
        {
            throw _guard.Forbidden("ISimHost.WorldStateHash");
        }

        public bool TrySubmit(in Command cmd, out CommandRejection reason)
        {
            Submits++;
            return _inner.TrySubmit(cmd, out reason);
        }

        public IReadOnlyList<Command> CommandLogSince(ulong tick)
        {
            throw _guard.Forbidden("ISimHost.CommandLogSince");
        }
    }

    /// <summary>Forwards 17 §17.6's one IFlowSystem member to a real flow system.</summary>
    internal sealed class GuardedFlow : IFlowSystem
    {
        private readonly IFlowSystem _inner;
        private readonly CallGuard _guard;

        public GuardedFlow(IFlowSystem inner, CallGuard guard)
        {
            _inner = inner;
            _guard = guard;
        }

        public SystemId Id => throw _guard.Forbidden("IFlowSystem.Id");

        public string Name => throw _guard.Forbidden("IFlowSystem.Name");

        public void Tick(in TickContext ctx)
        {
            throw _guard.Forbidden("IFlowSystem.Tick");
        }

        public ulong ComputeStateHash()
        {
            throw _guard.Forbidden("IFlowSystem.ComputeStateHash");
        }

        public int Population(NodeId node)
        {
            throw _guard.Forbidden("IFlowSystem.Population");
        }

        public Fx PredictedWaitMinutes(NodeId node)
        {
            throw _guard.Forbidden("IFlowSystem.PredictedWaitMinutes");
        }

        public int PopulationForFlight(FlightId flight, FlowDirection direction)
        {
            throw _guard.Forbidden("IFlowSystem.PopulationForFlight");
        }

        public IReadOnlyList<CohortId> CohortsAt(NodeId node)
        {
            throw _guard.Forbidden("IFlowSystem.CohortsAt");
        }

        public bool TryGetCohort(CohortId id, out PassengerCohort cohort)
        {
            throw _guard.Forbidden("IFlowSystem.TryGetCohort");
        }

        public bool TryGetOutstanding(FlightId flight, out OutstandingPassengers outstanding)
        {
            throw _guard.Forbidden("IFlowSystem.TryGetOutstanding");
        }

        public bool TryGetLaneState(NodeId node, out LaneState lanes)
        {
            return _inner.TryGetLaneState(node, out lanes);
        }

        public NodeKind KindOf(NodeId node)
        {
            throw _guard.Forbidden("IFlowSystem.KindOf");
        }

        public CohortId Inject(in CohortKey key, int count, NodeId at)
        {
            throw _guard.Forbidden("IFlowSystem.Inject");
        }

        public int Absorb(NodeId sink, FlightId flight)
        {
            throw _guard.Forbidden("IFlowSystem.Absorb");
        }

        public void SetPromoted(NodeId node, bool promoted)
        {
            throw _guard.Forbidden("IFlowSystem.SetPromoted");
        }

        public IReadOnlyList<AgentView> AgentsAt(NodeId node)
        {
            throw _guard.Forbidden("IFlowSystem.AgentsAt");
        }
    }
}
