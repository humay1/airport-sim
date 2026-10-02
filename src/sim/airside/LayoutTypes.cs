using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Airside
{
    /// <summary>The kind of a taxiway graph node. Spec: 12-interfaces-airside.md §12.4.</summary>
    public enum TaxiNodeKind
    {
        /// <summary>The node a runway's aircraft enter and leave the graph at.</summary>
        RunwayThreshold,

        /// <summary>A plain junction.</summary>
        Junction,

        /// <summary>The node a stand sits on.</summary>
        StandPosition
    }

    /// <summary>A runway. Spec: 12-interfaces-airside.md §12.4.</summary>
    public readonly struct RunwayDef
    {
        /// <summary>The runway id.</summary>
        public RunwayId Id { get; }

        /// <summary>The runway's threshold node.</summary>
        public TaxiNodeId ThresholdNode { get; }

        /// <summary>The fixed active direction in degrees.</summary>
        public int ActiveDirectionDeg { get; }

        /// <summary>Declared movements per hour, arrivals and departures pooled.</summary>
        public int DeclaredCapacityPerHour { get; }

        /// <summary>Runway-surface time per movement, in ticks.</summary>
        public uint OccupancyTicks { get; }

        /// <summary>Constructs the runway definition.</summary>
        public RunwayDef(RunwayId id, TaxiNodeId thresholdNode, int activeDirectionDeg, int declaredCapacityPerHour, uint occupancyTicks)
        {
            Id = id;
            ThresholdNode = thresholdNode;
            ActiveDirectionDeg = activeDirectionDeg;
            DeclaredCapacityPerHour = declaredCapacityPerHour;
            OccupancyTicks = occupancyTicks;
        }
    }

    /// <summary>A taxiway graph node. Spec: 12-interfaces-airside.md §12.4.</summary>
    public readonly struct TaxiNodeDef
    {
        /// <summary>The node id.</summary>
        public TaxiNodeId Id { get; }

        /// <summary>The node kind.</summary>
        public TaxiNodeKind Kind { get; }

        /// <summary>Constructs the node definition.</summary>
        public TaxiNodeDef(TaxiNodeId id, TaxiNodeKind kind)
        {
            Id = id;
            Kind = kind;
        }
    }

    /// <summary>A taxiway edge. Spec: 12-interfaces-airside.md §12.4.</summary>
    public readonly struct TaxiEdgeDef
    {
        /// <summary>The edge id.</summary>
        public TaxiEdgeId Id { get; }

        /// <summary>The node the edge starts at.</summary>
        public TaxiNodeId From { get; }

        /// <summary>The node the edge ends at.</summary>
        public TaxiNodeId To { get; }

        /// <summary>Ticks to traverse the edge.</summary>
        public uint TraversalTicks { get; }

        /// <summary>False: From to To only.</summary>
        public bool Bidirectional { get; }

        /// <summary>Constructs the edge definition.</summary>
        public TaxiEdgeDef(TaxiEdgeId id, TaxiNodeId from, TaxiNodeId to, uint traversalTicks, bool bidirectional)
        {
            Id = id;
            From = from;
            To = to;
            TraversalTicks = traversalTicks;
            Bidirectional = bidirectional;
        }
    }

    /// <summary>A contact stand. Spec: 12-interfaces-airside.md §12.4.</summary>
    public readonly struct StandDef
    {
        /// <summary>The stand id.</summary>
        public StandId Id { get; }

        /// <summary>The stand's node, a StandPosition.</summary>
        public TaxiNodeId Node { get; }

        /// <summary>The largest aircraft size category the stand takes.</summary>
        public ContentId MaxAircraftSizeCategory { get; }

        /// <summary>The sim.flow Sink node departing passengers are absorbed at.</summary>
        public NodeId DepartureSinkNode { get; }

        /// <summary>Constructs the stand definition.</summary>
        public StandDef(StandId id, TaxiNodeId node, ContentId maxAircraftSizeCategory, NodeId departureSinkNode)
        {
            Id = id;
            Node = node;
            MaxAircraftSizeCategory = maxAircraftSizeCategory;
            DepartureSinkNode = departureSinkNode;
        }
    }

    /// <summary>The airside graph. Spec: 12-interfaces-airside.md §12.4.</summary>
    public readonly struct AirsideLayout
    {
        /// <summary>The runways.</summary>
        public IReadOnlyList<RunwayDef> Runways { get; }

        /// <summary>The nodes.</summary>
        public IReadOnlyList<TaxiNodeDef> Nodes { get; }

        /// <summary>The edges.</summary>
        public IReadOnlyList<TaxiEdgeDef> Edges { get; }

        /// <summary>The stands.</summary>
        public IReadOnlyList<StandDef> Stands { get; }

        /// <summary>Constructs the layout from its four lists.</summary>
        public AirsideLayout(
            IReadOnlyList<RunwayDef> runways,
            IReadOnlyList<TaxiNodeDef> nodes,
            IReadOnlyList<TaxiEdgeDef> edges,
            IReadOnlyList<StandDef> stands)
        {
            Runways = runways;
            Nodes = nodes;
            Edges = edges;
            Stands = stands;
        }
    }

    /// <summary>Construction data beside the layout. Spec: 12-interfaces-airside.md §12.4.</summary>
    public readonly struct AirsideRules
    {
        /// <summary>The boarding hold limit in sim-minutes; 0 disables the hold.</summary>
        public uint BoardingHoldMaxMinutes { get; }

        /// <summary>Sim-minutes from OnStand to DoorsOpen; 0 is the same tick.</summary>
        public uint DoorsOpenDelayMinutes { get; }

        /// <summary>Constructs the rules.</summary>
        public AirsideRules(uint boardingHoldMaxMinutes, uint doorsOpenDelayMinutes)
        {
            BoardingHoldMaxMinutes = boardingHoldMaxMinutes;
            DoorsOpenDelayMinutes = doorsOpenDelayMinutes;
        }
    }

    /// <summary>Validates and parses airside layouts. Spec: 12-interfaces-airside.md §12.4.</summary>
    public interface IAirsideLayoutLoader
    {
        /// <summary>Validates a raw layout and returns it with every list sorted by id.</summary>
        AirsideLayout Load(AirsideLayout raw);

        /// <summary>Parses the fixture file format, then validates it with <see cref="Load"/>.</summary>
        AirsideLayout Parse(ReadOnlySpan<byte> file, string sourceName);
    }
}
