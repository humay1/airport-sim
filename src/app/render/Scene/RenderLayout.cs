using System;
using System.Collections.Generic;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;

namespace AirportSim.App.Render
{
    /// <summary>Position of one taxi node in world units. Spec: 15 §15.4.</summary>
    public readonly struct TaxiNodePosition
    {
        /// <summary>The taxi node.</summary>
        public TaxiNodeId Node { get; }

        /// <summary>World X.</summary>
        public int X { get; }

        /// <summary>World Y.</summary>
        public int Y { get; }

        /// <summary>Constructs the position.</summary>
        public TaxiNodePosition(TaxiNodeId node, int x, int y)
        {
            Node = node;
            X = x;
            Y = y;
        }
    }

    /// <summary>Geometry of one runway in world units. Spec: 15 §15.4.</summary>
    public readonly struct RunwayGeometry
    {
        /// <summary>The runway.</summary>
        public RunwayId Runway { get; }

        /// <summary>Start X.</summary>
        public int X0 { get; }

        /// <summary>Start Y.</summary>
        public int Y0 { get; }

        /// <summary>End X.</summary>
        public int X1 { get; }

        /// <summary>End Y.</summary>
        public int Y1 { get; }

        /// <summary>Drawn width.</summary>
        public int Width { get; }

        /// <summary>Constructs the geometry.</summary>
        public RunwayGeometry(RunwayId runway, int x0, int y0, int x1, int y1, int width)
        {
            Runway = runway;
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            Width = width;
        }
    }

    /// <summary>The box a landside flow node is drawn as. Spec: 15 §15.4.</summary>
    public readonly struct FlowNodeBox
    {
        /// <summary>The flow node.</summary>
        public NodeId Node { get; }

        /// <summary>Minimum X.</summary>
        public int MinX { get; }

        /// <summary>Minimum Y.</summary>
        public int MinY { get; }

        /// <summary>Maximum X.</summary>
        public int MaxX { get; }

        /// <summary>Maximum Y.</summary>
        public int MaxY { get; }

        /// <summary>Population at which the queue fill spans the whole box.</summary>
        public int FillCapacity { get; }

        /// <summary>Constructs the box.</summary>
        public FlowNodeBox(NodeId node, int minX, int minY, int maxX, int maxY, int fillCapacity)
        {
            Node = node;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            FillCapacity = fillCapacity;
        }
    }

    /// <summary>What a layout area is. Spec: 15 §15.4 (Q-130).</summary>
    public enum AreaKind
    {
        /// <summary>Paved apron.</summary>
        Apron,

        /// <summary>Terminal building.</summary>
        Terminal,

        /// <summary>Pier.</summary>
        Pier,

        /// <summary>Control tower; its box is square.</summary>
        ControlTower,
    }

    /// <summary>Scenery: a paved apron or a building footprint. Spec: 15 §15.4 (Q-130).</summary>
    public readonly struct LayoutArea
    {
        /// <summary>The area id.</summary>
        public uint Id { get; }

        /// <summary>What the area is.</summary>
        public AreaKind Kind { get; }

        /// <summary>Minimum X.</summary>
        public int MinX { get; }

        /// <summary>Minimum Y.</summary>
        public int MinY { get; }

        /// <summary>Maximum X.</summary>
        public int MaxX { get; }

        /// <summary>Maximum Y.</summary>
        public int MaxY { get; }

        /// <summary>Constructs the area.</summary>
        public LayoutArea(uint id, AreaKind kind, int minX, int minY, int maxX, int maxY)
        {
            Id = id;
            Kind = kind;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }
    }

    /// <summary>Scenery: a jet bridge. Spec: 15 §15.4 (Q-130).</summary>
    public readonly struct LayoutBridge
    {
        /// <summary>The bridge id.</summary>
        public uint Id { get; }

        /// <summary>Start X.</summary>
        public int X0 { get; }

        /// <summary>Start Y.</summary>
        public int Y0 { get; }

        /// <summary>End X.</summary>
        public int X1 { get; }

        /// <summary>End Y.</summary>
        public int Y1 { get; }

        /// <summary>Drawn width.</summary>
        public int Width { get; }

        /// <summary>The stand the bridge serves; null in version 1 and 2 files (Q-132).</summary>
        public StandId? Stand { get; }

        /// <summary>Constructs the bridge with no stand (07 L10 kept constructor).</summary>
        public LayoutBridge(uint id, int x0, int y0, int x1, int y1, int width)
            : this(id, x0, y0, x1, y1, width, null)
        {
        }

        /// <summary>Constructs the bridge.</summary>
        public LayoutBridge(uint id, int x0, int y0, int x1, int y1, int width, StandId? stand)
        {
            Stand = stand;
            Id = id;
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            Width = width;
        }
    }

    /// <summary>The path the agents of a corridor node walk, inside its box. Spec: 15 §15.4, §15.21 (Q-132).</summary>
    public readonly struct LayoutWalkway
    {
        /// <summary>The corridor node.</summary>
        public NodeId Node { get; }

        /// <summary>Start X.</summary>
        public int X0 { get; }

        /// <summary>Start Y.</summary>
        public int Y0 { get; }

        /// <summary>End X.</summary>
        public int X1 { get; }

        /// <summary>End Y.</summary>
        public int Y1 { get; }

        /// <summary>Width of the walkway's rectangle.</summary>
        public int Width { get; }

        /// <summary>Constructs the walkway.</summary>
        public LayoutWalkway(NodeId node, int x0, int y0, int x1, int y1, int width)
        {
            Node = node;
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            Width = width;
        }
    }

    /// <summary>The presentation layout: where things are drawn. Not sim state. Spec: 15 §15.4.</summary>
    public readonly struct RenderLayout
    {
        /// <summary>Taxi node positions, ascending node id when produced by the loader.</summary>
        public IReadOnlyList<TaxiNodePosition> TaxiNodes { get; }

        /// <summary>Runway geometry, ascending runway id when produced by the loader.</summary>
        public IReadOnlyList<RunwayGeometry> Runways { get; }

        /// <summary>Flow node boxes, ascending node id when produced by the loader.</summary>
        public IReadOnlyList<FlowNodeBox> FlowNodes { get; }

        /// <summary>Side of a stand box.</summary>
        public int StandSize { get; }

        /// <summary>Diameter of an aircraft dot.</summary>
        public int AircraftSize { get; }

        /// <summary>Diameter of an agent dot.</summary>
        public int AgentSize { get; }

        /// <summary>Width of a taxiway segment.</summary>
        public int TaxiwayWidth { get; }

        /// <summary>Scenery: aprons and building footprints, ascending id when produced by the loader (Q-130).</summary>
        public IReadOnlyList<LayoutArea> Areas { get; }

        /// <summary>Scenery: jet bridges, ascending id when produced by the loader (Q-130).</summary>
        public IReadOnlyList<LayoutBridge> Bridges { get; }

        /// <summary>Constructs the layout with no scenery (07 L10 kept constructor).</summary>
        public RenderLayout(
            IReadOnlyList<TaxiNodePosition> taxiNodes,
            IReadOnlyList<RunwayGeometry> runways,
            IReadOnlyList<FlowNodeBox> flowNodes,
            int standSize,
            int aircraftSize,
            int agentSize,
            int taxiwayWidth)
            : this(taxiNodes, runways, flowNodes, standSize, aircraftSize, agentSize, taxiwayWidth, Array.Empty<LayoutArea>(), Array.Empty<LayoutBridge>())
        {
        }

        /// <summary>Walkways, ascending node id when produced by the loader (Q-132).</summary>
        public IReadOnlyList<LayoutWalkway> Walkways { get; }

        /// <summary>Constructs the layout with no walkways (07 L10 kept constructor).</summary>
        public RenderLayout(
            IReadOnlyList<TaxiNodePosition> taxiNodes,
            IReadOnlyList<RunwayGeometry> runways,
            IReadOnlyList<FlowNodeBox> flowNodes,
            int standSize,
            int aircraftSize,
            int agentSize,
            int taxiwayWidth,
            IReadOnlyList<LayoutArea> areas,
            IReadOnlyList<LayoutBridge> bridges)
            : this(taxiNodes, runways, flowNodes, standSize, aircraftSize, agentSize, taxiwayWidth, areas, bridges, Array.Empty<LayoutWalkway>())
        {
        }

        /// <summary>Constructs the layout.</summary>
        public RenderLayout(
            IReadOnlyList<TaxiNodePosition> taxiNodes,
            IReadOnlyList<RunwayGeometry> runways,
            IReadOnlyList<FlowNodeBox> flowNodes,
            int standSize,
            int aircraftSize,
            int agentSize,
            int taxiwayWidth,
            IReadOnlyList<LayoutArea> areas,
            IReadOnlyList<LayoutBridge> bridges,
            IReadOnlyList<LayoutWalkway> walkways)
        {
            Walkways = walkways;
            TaxiNodes = taxiNodes;
            Runways = runways;
            FlowNodes = flowNodes;
            StandSize = standSize;
            AircraftSize = aircraftSize;
            AgentSize = agentSize;
            TaxiwayWidth = taxiwayWidth;
            Areas = areas;
            Bridges = bridges;
        }
    }

    /// <summary>Parses and validates a layout file. Spec: 15 §15.4 (Q-094).</summary>
    public interface IRenderLayoutLoader
    {
        /// <summary>
        /// Parses <paramref name="file"/> and validates it, against <paramref name="airside"/> when given.
        /// Every failure throws <see cref="FormatException"/> whose message starts with <paramref name="sourceName"/> and ": ".
        /// </summary>
        RenderLayout Load(ReadOnlySpan<byte> file, string sourceName, in AirsideLayout? airside);
    }
}
