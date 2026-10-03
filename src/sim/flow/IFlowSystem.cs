using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Flow
{
    /// <summary>
    /// Passenger cohorts, queue nodes and corridors. Spec: 09-interfaces-flow.md §9.7,
    /// §9.7a, §9.7b.
    /// </summary>
    public interface IFlowSystem : ISimSystem
    {
        /// <summary>The head count on <paramref name="node"/>. Throws <see cref="System.ArgumentException"/> for an unknown node.</summary>
        int Population(NodeId node);

        /// <summary>The predicted wait, in sim-minutes, for <paramref name="node"/>; 0 for a non-Queue node.
        /// Throws <see cref="System.ArgumentException"/> for an unknown node.</summary>
        Fx PredictedWaitMinutes(NodeId node);

        /// <summary>The head count of <paramref name="flight"/>'s cohorts of the given direction, over every node.</summary>
        int PopulationForFlight(FlightId flight, FlowDirection direction);

        /// <summary>Every cohort id on <paramref name="node"/>, ascending.</summary>
        IReadOnlyList<CohortId> CohortsAt(NodeId node);

        /// <summary>Looks up a cohort by id.</summary>
        bool TryGetCohort(CohortId id, out PassengerCohort cohort);

        /// <summary>The flight's passengers still upstream of a gate (§9.7a); false iff none.</summary>
        bool TryGetOutstanding(FlightId flight, out OutstandingPassengers outstanding);

        /// <summary>A queue node's lane state (§9.7b); false unless <paramref name="node"/> is a Queue.</summary>
        bool TryGetLaneState(NodeId node, out LaneState lanes);

        /// <summary>
        /// Creates a cohort of <paramref name="count"/> passengers at <paramref name="at"/>, a
        /// Source node. Throws <see cref="System.ArgumentException"/> for an unknown node
        /// or one that is not a Source, and <see cref="System.ArgumentOutOfRangeException"/>
        /// for a non-positive count.
        /// </summary>
        CohortId Inject(in CohortKey key, int count, NodeId at);

        /// <summary>
        /// Boards <paramref name="flight"/>'s Departing passengers waiting on any Gate node into
        /// <paramref name="sink"/>, and reports the rest missed. Throws
        /// <see cref="System.ArgumentException"/> for an unknown node or one that is not a Sink,
        /// and <see cref="System.InvalidOperationException"/> if called outside a tick.
        /// </summary>
        int Absorb(NodeId sink, FlightId flight);

        /// <summary>Promotes or demotes <paramref name="node"/>'s agent view; changes no hashed state (§9.1).</summary>
        void SetPromoted(NodeId node, bool promoted);

        /// <summary>The promoted agent views on <paramref name="node"/>; empty unless promoted.</summary>
        IReadOnlyList<AgentView> AgentsAt(NodeId node);

        /// <summary>
        /// The <see cref="NodeKind"/> the system's graph gives <paramref name="node"/>. Throws
        /// <see cref="System.ArgumentException"/> for an unknown node. A query: callable at any time.
        /// </summary>
        NodeKind KindOf(NodeId node);
    }
}
