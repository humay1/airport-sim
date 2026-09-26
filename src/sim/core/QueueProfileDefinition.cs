namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A queue profile definition. Spec: 08-interfaces-core.md §8.11, 09-interfaces-flow.md
    /// §9.4/§9.5, 10-events.md §10.6.
    /// </summary>
    public readonly struct QueueProfileDefinition : IContentDefinition
    {
        /// <summary>The definition's content id.</summary>
        public ContentId Id { get; }

        /// <summary>Service rate per open server, per sim-minute.</summary>
        public Fx ServiceRatePerServerPerMinute { get; }

        /// <summary>Standing capacity before the queue is considered full.</summary>
        public int CapacityStanding { get; }

        /// <summary>Wait, in sim-minutes, above which <c>QueueThresholdExceeded</c> fires.</summary>
        public Fx ThresholdWaitMinutes { get; }

        /// <summary>Hysteresis, in sim-minutes, below which the threshold clears.</summary>
        public Fx HysteresisMinutes { get; }

        /// <summary>Which delay category this queue's waits are attributed to.</summary>
        public DelayCategory Category { get; }

        /// <summary>The definition's content kind.</summary>
        public ContentKind Kind => ContentKind.QueueProfile;

        /// <summary>Constructs the definition from its members, in declared order.</summary>
        public QueueProfileDefinition(
            ContentId id,
            Fx serviceRatePerServerPerMinute,
            int capacityStanding,
            Fx thresholdWaitMinutes,
            Fx hysteresisMinutes,
            DelayCategory category)
        {
            Id = id;
            ServiceRatePerServerPerMinute = serviceRatePerServerPerMinute;
            CapacityStanding = capacityStanding;
            ThresholdWaitMinutes = thresholdWaitMinutes;
            HysteresisMinutes = hysteresisMinutes;
            Category = category;
        }
    }
}
