namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The mechanical explanation attached to a delay node. Spec: 06-delay-attribution.md,
    /// 14-interfaces-delay.md §14.3. <see cref="A"/> and <see cref="B"/>'s meaning depends on
    /// <see cref="Source"/>, defined by sim.delay.
    /// </summary>
    public readonly struct DelayExplanation
    {
        /// <summary>The mechanical source.</summary>
        public DelaySource Source { get; }

        /// <summary>The first source-specific value.</summary>
        public ulong A { get; }

        /// <summary>The second source-specific value.</summary>
        public ulong B { get; }

        /// <summary>Constructs the explanation from its source and two source-specific values.</summary>
        public DelayExplanation(DelaySource source, ulong a, ulong b)
        {
            Source = source;
            A = a;
            B = b;
        }
    }
}
