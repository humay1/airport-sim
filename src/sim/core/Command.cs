namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Spec: 01-architecture.md, 08-interfaces-core.md §8.7 ("Queue semantics", Q-020).
    /// Shape only at T-001; admission, ordering and dispatch are T-005's.
    /// </summary>
    public readonly struct Command
    {
        public ulong Tick { get; }
        public PlayerId Issuer { get; }
        public CommandKind Kind { get; }
        public byte[] Payload { get; }

        /// <summary>Assigned on admission by the queue, not by the caller. 0 means "not admitted".</summary>
        public uint Sequence { get; }

        /// <summary>A <c>null</c> payload is a programmer error; pass an empty array instead.</summary>
        public Command(ulong tick, PlayerId issuer, CommandKind kind, byte[] payload)
        {
            if (payload is null)
            {
                throw new System.ArgumentNullException(nameof(payload));
            }

            Tick = tick;
            Issuer = issuer;
            Kind = kind;
            Payload = payload;
            Sequence = 0;
        }

        /// <summary>Used by the admitting queue to stamp the assigned sequence.</summary>
        internal Command(ulong tick, PlayerId issuer, CommandKind kind, byte[] payload, uint sequence)
        {
            Tick = tick;
            Issuer = issuer;
            Kind = kind;
            Payload = payload;
            Sequence = sequence;
        }
    }
}
