using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The one <see cref="IStateHasher"/> implementation: FNV-1a-64 over the fed byte
    /// stream, little-endian. Spec: 08-interfaces-core.md §8.9 ("Encoding, the concrete
    /// hasher and the core section", Q-017). A mutable struct, an exception to
    /// 07-conventions.md L10: systems use it directly; through the interface it would box.
    /// <c>new StateHasher()</c> and <c>default(StateHasher)</c> are both a fresh hasher.
    /// </summary>
    public struct StateHasher : IStateHasher
    {
        private const ulong OffsetBasis = 0xCBF29CE484222325UL;
        private const ulong Prime = 0x100000001B3UL;

        // Encoded so that the zero-initialised default struct is a fresh hasher:
        // _state == (true hash) XOR OffsetBasis, so _state == 0 means true hash == OffsetBasis.
        private ulong _state;

        public ulong Result => _state ^ OffsetBasis;

        public void Feed(ulong v)
        {
            for (int i = 0; i < 8; i++)
            {
                FeedByte((byte)(v & 0xFF));
                v >>= 8;
            }
        }

        public void Feed(long v) => Feed(unchecked((ulong)v));

        public void Feed(in Fx v) => Feed(v.Raw);

        public void Feed(bool v) => FeedByte(v ? (byte)1 : (byte)0);

        public void Feed(ReadOnlySpan<byte> v)
        {
            Feed((ulong)v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                FeedByte(v[i]);
            }
        }

        private void FeedByte(byte b)
        {
            ulong h = _state ^ OffsetBasis;
            h = (h ^ b) * Prime;
            _state = h ^ OffsetBasis;
        }
    }
}
