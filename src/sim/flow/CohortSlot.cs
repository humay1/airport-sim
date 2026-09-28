namespace AirportSim.Sim.Flow
{
    /// <summary>
    /// One pooled cohort storage slot. Index-stable: a slot's array index is
    /// never reused while the cohort is alive (09-interfaces-flow.md §9.10).
    /// </summary>
    internal struct CohortSlot
    {
        internal bool Alive;
        internal ulong Id;
        internal CohortKey Key;
        internal int NodeOrdinal;
        internal int Count;
        internal ulong EnteredNodeAt;
        internal ulong DueAt;

        /// <summary>True while this cohort is in an open blocking episode (§9.12).</summary>
        internal bool Blocked;

        /// <summary>Meaningful only while <see cref="Blocked"/>: the node ordinal refusing it.</summary>
        internal int BlockedByOrdinal;
    }
}
