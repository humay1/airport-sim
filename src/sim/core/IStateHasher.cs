using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Feeds a system's serialisable state, in declared order, into an FNV-1a-64 hash.
    /// Spec: 08-interfaces-core.md §8.9 ("Encoding, the concrete hasher and the core
    /// section", Q-017). <see cref="StateHasher"/> is the one implementation; construct
    /// it with <c>new StateHasher()</c> or <c>default</c>.
    /// </summary>
    public interface IStateHasher
    {
        /// <summary>Feeds 8 bytes, little-endian.</summary>
        void Feed(ulong v);

        /// <summary>Feeds 8 bytes, little-endian, reinterpreting the bit pattern as unsigned.</summary>
        void Feed(long v);

        /// <summary>Feeds the fixed-point value's raw bits, little-endian.</summary>
        void Feed(in Fx v);

        /// <summary>Feeds one byte: 1 for true, 0 for false.</summary>
        void Feed(bool v);

        /// <summary>Feeds a length-prefixed (uint64, little-endian) byte span.</summary>
        void Feed(ReadOnlySpan<byte> v);

        /// <summary>The hash of everything fed so far. Readable at any time.</summary>
        ulong Result { get; }
    }
}
