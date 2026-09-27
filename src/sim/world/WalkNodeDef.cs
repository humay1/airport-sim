using AirportSim.Sim.Core;

namespace AirportSim.Sim.World
{
    /// <summary>
    /// One node of the walk graph: walked when crossing it. Spec:
    /// 18-interfaces-world.md §18.2. <see cref="LengthMetres"/> of 0 means "no
    /// length" (a junction, not a corridor).
    /// </summary>
    public readonly struct WalkNodeDef
    {
        /// <summary>The node's id.</summary>
        public NodeId Id { get; }

        /// <summary>Metres walked when entering this node.</summary>
        public uint LengthMetres { get; }

        /// <summary>Constructs the definition from its id and length.</summary>
        public WalkNodeDef(NodeId id, uint lengthMetres)
        {
            Id = id;
            LengthMetres = lengthMetres;
        }
    }
}
