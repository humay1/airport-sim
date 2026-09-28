using AirportSim.Sim.Core;

namespace AirportSim.Sim.Flow
{
    /// <summary>
    /// The only authoritative passenger state (09-interfaces-flow.md §9.1, §9.2).
    /// </summary>
    public readonly struct PassengerCohort
    {
        /// <summary>The cohort's id.</summary>
        public CohortId Id { get; }

        /// <summary>The merge/split discriminator.</summary>
        public CohortKey Key { get; }

        /// <summary>The node the cohort is on.</summary>
        public NodeId Node { get; }

        /// <summary>The head count; always &gt; 0 for a live cohort.</summary>
        public int Count { get; }

        /// <summary>The tick the cohort arrived at <see cref="Node"/>.</summary>
        public ulong EnteredNodeAt { get; }

        /// <summary>The corridor release tick, or missed-flight gate close.</summary>
        public ulong DueAt { get; }

        /// <summary>Queue service credit; always <see cref="Fx.Zero"/> at Phase 0/1 (§9.12).</summary>
        public Fx ServiceCredit { get; }

        /// <summary>Constructs the cohort from its seven fields, in declared order.</summary>
        public PassengerCohort(CohortId id, CohortKey key, NodeId node, int count, ulong enteredNodeAt, ulong dueAt, Fx serviceCredit)
        {
            Id = id;
            Key = key;
            Node = node;
            Count = count;
            EnteredNodeAt = enteredNodeAt;
            DueAt = dueAt;
            ServiceCredit = serviceCredit;
        }
    }
}
