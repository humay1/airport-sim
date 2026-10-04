using AirportSim.Sim.Core;

namespace AirportSim.Sim.Delay
{
    // Pooled, index-stable storage rows. Links are slot indices, -1 for none; the
    // free chains of each pool run through the same link fields.

    internal struct FlightRec
    {
        public ulong Flight;
        public MovementKind Kind;
        public bool HasRotation;
        public ulong Rotation;
        public int Root;
        public ulong Total;
        public int Checkpoints;
        public ulong Last;
        public bool Finalised;
        public ulong FinalisedAt;
        public int Missed;
        public bool HasMissedAt;
        public uint MissedAt;
        public bool Doomed;
        public int FirstLeaf;
        public int LastLeaf;
        public int FirstInterval;
        public int LastInterval;
        public int NextFree;
    }

    internal struct NodeRec
    {
        public ulong Id;
        public DelayNodeKind Kind;
        public ulong Subject;
        public ulong Parent;
        public DelayCategory Category;
        public ulong Ticks;
        public bool RootCause;
        public ulong Linked;
        public bool HasSource;
        public EventId Source;
        public DelaySource Src;
        public ulong A;
        public ulong B;
        public ulong CreatedAt;

        // Every node, ascending DelayEventId.
        public int Prev;
        public int Next;

        // One flight's leaves, ascending DelayEventId.
        public int LeafPrev;
        public int LeafNext;
    }

    internal struct IntervalRec
    {
        public EventId Opener;
        public ulong Flight;
        public int Family;
        public int Job;
        public DelayCategory Category;
        public ulong Start;
        public ulong End;
        public ulong A;
        public ulong B;

        // Every retained interval, ascending OpenerId.
        public int Prev;
        public int Next;

        // One flight's intervals, ascending OpenerId.
        public int FlightPrev;
        public int FlightNext;
    }
}
