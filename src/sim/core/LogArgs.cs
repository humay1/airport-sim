namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Integer/id/<c>Fx</c>-only log parameters; no string formatting inside the sim.
    /// Spec: 08-interfaces-core.md §8.10 (Q-014). An exception to 07-conventions.md L10's
    /// single-constructor rule: one constructor per arity, 1 to 4.
    /// </summary>
    public readonly struct LogArgs
    {
        /// <summary>How many of A0..A3 are meaningful.</summary>
        public int Count { get; }

        /// <summary>The first argument, meaningful when Count &gt;= 1.</summary>
        public long A0 { get; }

        /// <summary>The second argument, meaningful when Count &gt;= 2.</summary>
        public long A1 { get; }

        /// <summary>The third argument, meaningful when Count &gt;= 3.</summary>
        public long A2 { get; }

        /// <summary>The fourth argument, meaningful when Count &gt;= 4.</summary>
        public long A3 { get; }

        /// <summary>Constructs a one-argument LogArgs.</summary>
        public LogArgs(long a0)
        {
            Count = 1;
            A0 = a0;
            A1 = 0;
            A2 = 0;
            A3 = 0;
        }

        /// <summary>Constructs a two-argument LogArgs.</summary>
        public LogArgs(long a0, long a1)
        {
            Count = 2;
            A0 = a0;
            A1 = a1;
            A2 = 0;
            A3 = 0;
        }

        /// <summary>Constructs a three-argument LogArgs.</summary>
        public LogArgs(long a0, long a1, long a2)
        {
            Count = 3;
            A0 = a0;
            A1 = a1;
            A2 = a2;
            A3 = 0;
        }

        /// <summary>Constructs a four-argument LogArgs.</summary>
        public LogArgs(long a0, long a1, long a2, long a3)
        {
            Count = 4;
            A0 = a0;
            A1 = a1;
            A2 = a2;
            A3 = a3;
        }
    }
}
