using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Airside
{
    /// <summary>Where an aircraft is in its leg. Spec: 12-interfaces-airside.md §12.9.</summary>
    public enum AircraftLegPhase
    {
        /// <summary>Tracked, not yet requesting the runway.</summary>
        AwaitingApproach,

        /// <summary>Held for the runway or its slot.</summary>
        HeldForRunway,

        /// <summary>On the runway surface.</summary>
        OnRunway,

        /// <summary>Held at a node, for an edge or a stand.</summary>
        HeldOnTaxiway,

        /// <summary>On a taxi edge.</summary>
        Taxiing,

        /// <summary>On its stand.</summary>
        OnStand,

        /// <summary>Reserved by the spec's enum; not used at Phase 0/1.</summary>
        AwaitingPushbackClearance,

        /// <summary>Reserved by the spec's enum; not used at Phase 0/1.</summary>
        Departed
    }

    /// <summary>One tracked aircraft. Spec: 12-interfaces-airside.md §12.9.</summary>
    public readonly struct AircraftTrack
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>Arrival or departure.</summary>
        public MovementKind Kind { get; }

        /// <summary>The leg phase.</summary>
        public AircraftLegPhase Phase { get; }

        /// <summary>The node the aircraft is at, or entered its edge from.</summary>
        public TaxiNodeId? AtNode { get; }

        /// <summary>The edge the aircraft is on.</summary>
        public TaxiEdgeId? OnEdge { get; }

        /// <summary>0 to 1 along the edge; meaningful only while OnEdge is set.</summary>
        public Fx EdgeProgress { get; }

        /// <summary>The stand held.</summary>
        public StandId? Stand { get; }

        /// <summary>The runway chosen.</summary>
        public RunwayId? Runway { get; }

        /// <summary>The tick the phase was entered.</summary>
        public ulong PhaseEnteredAt { get; }

        /// <summary>The tick the next action falls due, or TICK_UNSCHEDULED.</summary>
        public ulong DueAt { get; }

        /// <summary>The tick a boarding hold began, or TICK_UNSCHEDULED.</summary>
        public ulong PassengerHoldSince { get; }

        /// <summary>A consumed turnaround event awaiting action, or EventRef.None.</summary>
        public EventRef RecordedCause { get; }

        /// <summary>The open runway, taxiway or passenger hold event, or EventRef.None.</summary>
        public EventRef OpenHold { get; }

        /// <summary>Constructs the track from its members in declared order.</summary>
        public AircraftTrack(
            FlightId flight,
            MovementKind kind,
            AircraftLegPhase phase,
            TaxiNodeId? atNode,
            TaxiEdgeId? onEdge,
            Fx edgeProgress,
            StandId? stand,
            RunwayId? runway,
            ulong phaseEnteredAt,
            ulong dueAt,
            ulong passengerHoldSince,
            EventRef recordedCause,
            EventRef openHold)
        {
            Flight = flight;
            Kind = kind;
            Phase = phase;
            AtNode = atNode;
            OnEdge = onEdge;
            EdgeProgress = edgeProgress;
            Stand = stand;
            Runway = runway;
            PhaseEnteredAt = phaseEnteredAt;
            DueAt = dueAt;
            PassengerHoldSince = passengerHoldSince;
            RecordedCause = recordedCause;
            OpenHold = openHold;
        }
    }

    /// <summary>A stand and its occupant. Spec: 12-interfaces-airside.md §12.9.</summary>
    public readonly struct StandState
    {
        /// <summary>The stand.</summary>
        public StandId Id { get; }

        /// <summary>The flight holding the stand, if any.</summary>
        public FlightId? Occupant { get; }

        /// <summary>The Pushback that last freed the stand; EventRef.None while occupied.</summary>
        public EventRef VacatedBy { get; }

        /// <summary>Constructs the state.</summary>
        public StandState(StandId id, FlightId? occupant, EventRef vacatedBy)
        {
            Id = id;
            Occupant = occupant;
            VacatedBy = vacatedBy;
        }
    }

    /// <summary>The airside system. Spec: 12-interfaces-airside.md §12.9.</summary>
    public interface IAirsideSystem : ISimSystem
    {
        /// <summary>The track of a tracked flight.</summary>
        bool TryGetTrack(FlightId flight, out AircraftTrack track);

        /// <summary>The state of a stand.</summary>
        bool TryGetStand(StandId id, out StandState stand);

        /// <summary>Free stands, ascending StandId.</summary>
        IReadOnlyList<StandId> FreeStands();

        /// <summary>Aircraft in the runway's hold queue.</summary>
        int RunwayQueueLength(RunwayId runway);

        /// <summary>Tracked flights, ascending FlightId.</summary>
        IReadOnlyList<FlightId> TrackedFlights();

        /// <summary>The validated layout.</summary>
        AirsideLayout Layout();
    }
}
