using System;
using System.Collections.Generic;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;

namespace AirportSim.App.Render.Tests
{
    // Test doubles for RenderSources (15 §15.9). Every sim member that 15
    // §15.6 does not list is recorded as a violation and throws, so a call is
    // caught even if the caller swallows the exception (15 §15.12: "fakes
    // throw on any member not in §15.6"). The listed members allocate nothing,
    // so the budget and allocation tests can meter through them.

    internal sealed class CallGuard
    {
        public readonly List<string> Violations = new List<string>();

        public Exception Forbidden(string member)
        {
            Violations.Add(member);
            return new InvalidOperationException("app.render called " + member + ", which 15 §15.6 does not list");
        }
    }

    /// <summary>An ISimHost whose only permitted member is CurrentTick (15 §15.6).</summary>
    internal sealed class FakeHost : ISimHost
    {
        public readonly CallGuard Guard;
        public ulong Tick;
        public long CurrentTickReads;

        public FakeHost(CallGuard guard, ulong tick = 0UL)
        {
            Guard = guard;
            Tick = tick;
        }

        public ulong CurrentTick
        {
            get
            {
                CurrentTickReads++;
                return Tick;
            }
        }

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
            throw Guard.Forbidden("ISimHost.TrySubmit");
        }

        public IReadOnlyList<Command> CommandLogSince(ulong tick)
        {
            throw Guard.Forbidden("ISimHost.CommandLogSince");
        }
    }

    /// <summary>
    /// An IAirsideSystem over a fixed layout. Stands, tracks and runway queues
    /// are set by the test between builds.
    /// </summary>
    internal sealed class FakeAirside : IAirsideSystem
    {
        public readonly CallGuard Guard;
        public long LayoutCalls;
        public long TrackedCalls;
        public long TrackCalls;
        public long StandCalls;
        public long QueueCalls;

        private readonly AirsideLayout _layout;
        private readonly Dictionary<ushort, FlightId?> _occupant = new Dictionary<ushort, FlightId?>();
        private readonly Dictionary<ushort, int> _queue = new Dictionary<ushort, int>();
        private readonly Dictionary<ulong, AircraftTrack> _tracks = new Dictionary<ulong, AircraftTrack>();
        private FlightId[] _tracked = Array.Empty<FlightId>();

        public FakeAirside(CallGuard guard, AirsideLayout layout)
        {
            Guard = guard;
            _layout = layout;
            foreach (StandDef s in layout.Stands)
            {
                _occupant.Add(s.Id.Value, null);
            }

            foreach (RunwayDef r in layout.Runways)
            {
                _queue.Add(r.Id.Value, 0);
            }
        }

        /// <summary>Calls of the per-rebuild queries (15 §15.6), Layout excluded.</summary>
        public long QueryCalls => TrackedCalls + TrackCalls + StandCalls + QueueCalls;

        public SystemId Id => throw Guard.Forbidden("IAirsideSystem.Id");

        public string Name => throw Guard.Forbidden("IAirsideSystem.Name");

        public void Occupy(ushort stand, ulong? flight)
        {
            _occupant[stand] = flight.HasValue ? new FlightId(flight.Value) : (FlightId?)null;
        }

        public void SetQueue(ushort runway, int length)
        {
            _queue[runway] = length;
        }

        /// <summary>Replaces every track. TrackedFlights is ascending FlightId (12 §12.9).</summary>
        public void SetTracks(params AircraftTrack[] tracks)
        {
            _tracks.Clear();
            var ids = new List<ulong>();
            foreach (AircraftTrack t in tracks)
            {
                _tracks.Add(t.Flight.Value, t);
                ids.Add(t.Flight.Value);
            }

            ids.Sort();
            _tracked = new FlightId[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                _tracked[i] = new FlightId(ids[i]);
            }
        }

        public void Tick(in TickContext ctx)
        {
            throw Guard.Forbidden("IAirsideSystem.Tick");
        }

        public ulong ComputeStateHash()
        {
            throw Guard.Forbidden("IAirsideSystem.ComputeStateHash");
        }

        public bool TryGetTrack(FlightId flight, out AircraftTrack track)
        {
            TrackCalls++;
            return _tracks.TryGetValue(flight.Value, out track);
        }

        public bool TryGetStand(StandId id, out StandState stand)
        {
            StandCalls++;
            if (_occupant.TryGetValue(id.Value, out FlightId? occ))
            {
                stand = new StandState(id, occ, EventRef.None);
                return true;
            }

            stand = default;
            return false;
        }

        public IReadOnlyList<StandId> FreeStands()
        {
            throw Guard.Forbidden("IAirsideSystem.FreeStands");
        }

        public int RunwayQueueLength(RunwayId runway)
        {
            QueueCalls++;
            return _queue.TryGetValue(runway.Value, out int q) ? q : 0;
        }

        public IReadOnlyList<FlightId> TrackedFlights()
        {
            TrackedCalls++;
            return _tracked;
        }

        public AirsideLayout Layout()
        {
            LayoutCalls++;
            return _layout;
        }
    }

    /// <summary>
    /// An IFlowSystem over a fixed node set. AgentsAt follows 09 §9.7: one
    /// view per passenger on a promoted node, sorted (Cohort, Index), empty
    /// otherwise; SetPromoted and AgentsAt throw ArgumentException on an
    /// unknown node. Promotion here is state the test can read.
    /// </summary>
    internal sealed class FakeFlow : IFlowSystem
    {
        public const int CohortSize = 50;

        public readonly CallGuard Guard;
        public readonly List<(uint Node, bool Promoted)> SetPromotedCalls = new List<(uint Node, bool Promoted)>();
        public bool RecordPromotions = true;
        public long PopulationCalls;
        public long AgentsAtCalls;
        public long LaneCalls;
        public long SetPromotedCount;

        private readonly Dictionary<uint, NodeState> _nodes = new Dictionary<uint, NodeState>();

        public FakeFlow(CallGuard guard)
        {
            Guard = guard;
        }

        /// <summary>Calls the scene builder makes (15 §15.6), SetPromoted excluded.</summary>
        public long QueryCalls => PopulationCalls + AgentsAtCalls + LaneCalls;

        public SystemId Id => throw Guard.Forbidden("IFlowSystem.Id");

        public string Name => throw Guard.Forbidden("IFlowSystem.Name");

        public FakeFlow Node(uint node, int population = 0, LaneState? lanes = null)
        {
            _nodes[node] = new NodeState(node, population, lanes);
            return this;
        }

        public void SetPopulation(uint node, int population)
        {
            NodeState s = _nodes[node];
            _nodes[node] = new NodeState(node, population, s.Lanes) { Promoted = s.Promoted };
        }

        public bool IsPromoted(uint node)
        {
            return _nodes[node].Promoted;
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
            PopulationCalls++;
            return _nodes.TryGetValue(node.Value, out NodeState? s) ? s.Population : 0;
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
            if (_nodes.TryGetValue(node.Value, out NodeState? s) && s.Lanes.HasValue)
            {
                lanes = s.Lanes.Value;
                return true;
            }

            lanes = default;
            return false;
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
            if (!_nodes.TryGetValue(node.Value, out NodeState? s))
            {
                throw new ArgumentException("unknown node " + node.Value, nameof(node));
            }

            SetPromotedCount++;
            if (RecordPromotions)
            {
                SetPromotedCalls.Add((node.Value, promoted));
            }

            s.Promoted = promoted;
        }

        public IReadOnlyList<AgentView> AgentsAt(NodeId node)
        {
            AgentsAtCalls++;
            if (!_nodes.TryGetValue(node.Value, out NodeState? s))
            {
                throw new ArgumentException("unknown node " + node.Value, nameof(node));
            }

            return s.Promoted ? s.Agents : Array.Empty<AgentView>();
        }

        private sealed class NodeState
        {
            public NodeState(uint node, int population, LaneState? lanes)
            {
                Population = population;
                Lanes = lanes;
                Agents = new AgentView[population];
                for (int i = 0; i < population; i++)
                {
                    var r = new PassengerRef(new CohortId((ulong)(i / CohortSize) + 1UL), i % CohortSize);
                    Agents[i] = new AgentView(r, new NodeId(node), Fx.Zero);
                }
            }

            public int Population { get; }

            public LaneState? Lanes { get; }

            public AgentView[] Agents { get; }

            public bool Promoted { get; set; }
        }
    }

    /// <summary>
    /// Forwards the 15 §15.6 members to a real sim and records every
    /// SetPromoted, for the integration tests. Anything else is a violation.
    /// Step is not forwarded: the test drives the real host itself (16 §16.6).
    /// </summary>
    internal sealed class GuardedHost : ISimHost
    {
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
            throw _guard.Forbidden("ISimHost.TrySubmit");
        }

        public IReadOnlyList<Command> CommandLogSince(ulong tick)
        {
            throw _guard.Forbidden("ISimHost.CommandLogSince");
        }
    }

    internal sealed class GuardedAirside : IAirsideSystem
    {
        private readonly IAirsideSystem _inner;
        private readonly CallGuard _guard;

        public GuardedAirside(IAirsideSystem inner, CallGuard guard)
        {
            _inner = inner;
            _guard = guard;
        }

        public SystemId Id => throw _guard.Forbidden("IAirsideSystem.Id");

        public string Name => throw _guard.Forbidden("IAirsideSystem.Name");

        public void Tick(in TickContext ctx)
        {
            throw _guard.Forbidden("IAirsideSystem.Tick");
        }

        public ulong ComputeStateHash()
        {
            throw _guard.Forbidden("IAirsideSystem.ComputeStateHash");
        }

        public bool TryGetTrack(FlightId flight, out AircraftTrack track)
        {
            return _inner.TryGetTrack(flight, out track);
        }

        public bool TryGetStand(StandId id, out StandState stand)
        {
            return _inner.TryGetStand(id, out stand);
        }

        public IReadOnlyList<StandId> FreeStands()
        {
            throw _guard.Forbidden("IAirsideSystem.FreeStands");
        }

        public int RunwayQueueLength(RunwayId runway)
        {
            return _inner.RunwayQueueLength(runway);
        }

        public IReadOnlyList<FlightId> TrackedFlights()
        {
            return _inner.TrackedFlights();
        }

        public AirsideLayout Layout()
        {
            return _inner.Layout();
        }
    }

    internal sealed class GuardedFlow : IFlowSystem
    {
        public readonly List<(uint Node, bool Promoted)> SetPromotedCalls = new List<(uint Node, bool Promoted)>();
        public long AgentViewsSeen;

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
            return _inner.Population(node);
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
            SetPromotedCalls.Add((node.Value, promoted));
            _inner.SetPromoted(node, promoted);
        }

        public IReadOnlyList<AgentView> AgentsAt(NodeId node)
        {
            IReadOnlyList<AgentView> views = _inner.AgentsAt(node);
            AgentViewsSeen += views.Count;
            return views;
        }
    }
}
