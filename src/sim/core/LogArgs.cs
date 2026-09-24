namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Integer/id/<c>Fx</c>-only log parameters; no string formatting inside the sim.
    /// Spec: 08-interfaces-core.md §8.10 (Q-014). An exception to 07-conventions.md L10's
    /// single-constructor rule: one constructor per arity, 1 to 4.
    /// </summary>
    public readonly struct LogArgs
    {
        public int Count { get; }
        public long A0 { get; }
        public long A1 { get; }
        public long A2 { get; }
        public long A3 { get; }

        public LogArgs(long a0)
        {
            Count = 1;
            A0 = a0;
            A1 = 0;
            A2 = 0;
            A3 = 0;
        }

        public LogArgs(long a0, long a1)
        {
            Count = 2;
            A0 = a0;
            A1 = a1;
            A2 = 0;
            A3 = 0;
        }

        public LogArgs(long a0, long a1, long a2)
        {
            Count = 3;
            A0 = a0;
            A1 = a1;
            A2 = a2;
            A3 = 0;
        }

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
