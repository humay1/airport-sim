using System.Collections.Generic;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A passenger profile definition. Spec: 08-interfaces-core.md §8.11,
    /// 09-interfaces-flow.md §9.6, 11-interfaces-schedule.md §11.6.
    /// </summary>
    public readonly struct PaxProfileDefinition : IContentDefinition
    {
        /// <summary>The definition's content id.</summary>
        public ContentId Id { get; }

        /// <summary>Walking speed, in metres per second.</summary>
        public Fx WalkSpeedMps { get; }

        /// <summary>The show-up curve, one bucket per entry.</summary>
        public IReadOnlyList<ShowUpBucket> ShowUpCurve { get; }

        /// <summary>The definition's content kind.</summary>
        public ContentKind Kind => ContentKind.PaxProfile;

        /// <summary>Constructs the definition from its id, walk speed and show-up curve.</summary>
        public PaxProfileDefinition(ContentId id, Fx walkSpeedMps, IReadOnlyList<ShowUpBucket> showUpCurve)
        {
            Id = id;
            WalkSpeedMps = walkSpeedMps;
            ShowUpCurve = showUpCurve;
        }
    }
}
