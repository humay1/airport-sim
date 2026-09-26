namespace AirportSim.Sim.Core
{
    /// <summary>
    /// One bucket of a passenger profile's show-up curve. Spec: 08-interfaces-core.md §8.11,
    /// 11-interfaces-schedule.md §11.6.
    /// </summary>
    public readonly struct ShowUpBucket
    {
        /// <summary>Minutes before scheduled departure time this bucket represents.</summary>
        public uint MinutesBeforeStd { get; }

        /// <summary>The bucket's share of show-ups, in permille (parts per thousand).</summary>
        public uint SharePermille { get; }

        /// <summary>Constructs the bucket from its offset and share.</summary>
        public ShowUpBucket(uint minutesBeforeStd, uint sharePermille)
        {
            MinutesBeforeStd = minutesBeforeStd;
            SharePermille = sharePermille;
        }
    }
}
