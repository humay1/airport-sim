using AirportSim.Sim.Core;

namespace AirportSim.Sim.Flow
{
    /// <summary>
    /// A derived, not drawn, individual passenger identity. Spec:
    /// 09-interfaces-flow.md §9.1, §9.7.
    /// </summary>
    public readonly struct PassengerRef
    {
        /// <summary>The passenger's cohort.</summary>
        public CohortId Cohort { get; }

        /// <summary>The passenger's index within the cohort.</summary>
        public int Index { get; }

        /// <summary>Constructs the reference from its cohort and index.</summary>
        public PassengerRef(CohortId cohort, int index)
        {
            Cohort = cohort;
            Index = index;
        }
    }
}
