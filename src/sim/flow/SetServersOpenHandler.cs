using System;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Flow
{
    /// <summary>
    /// Admits and applies <c>SetServersOpen</c>. Spec: 09-interfaces-flow.md §9.8,
    /// 08-interfaces-core.md §8.7 (byte layout: <c>NodeId.Value</c> then <c>count</c>,
    /// little-endian, 8 bytes).
    /// </summary>
    internal sealed class SetServersOpenHandler : ICommandHandler
    {
        private readonly FlowSystem _system;

        internal SetServersOpenHandler(FlowSystem system)
        {
            _system = system;
        }

        public CommandKind Kind => CommandKind.SetServersOpen;

        public CommandRejection Validate(ReadOnlySpan<byte> payload)
        {
            if (payload.Length != 8)
            {
                return CommandRejection.MalformedPayload;
            }

            uint nodeValue = Decode(payload, 0);
            if (!_system.TryOrdinalOf(new NodeId(nodeValue), out int ordinal))
            {
                return CommandRejection.MalformedPayload;
            }

            if (_system.KindOf(ordinal) != NodeKind.Queue)
            {
                return CommandRejection.NotPermitted;
            }

            return CommandRejection.None;
        }

        public void Apply(in Command cmd, in TickContext ctx)
        {
            uint nodeValue = Decode(cmd.Payload, 0);
            int count = unchecked((int)Decode(cmd.Payload, 4));
            _system.TryOrdinalOf(new NodeId(nodeValue), out int ordinal);
            _system.ApplySetServersOpen(ordinal, count);
        }

        /// <summary>Little-endian uint32, independent of machine endianness (08 §8.7).</summary>
        private static uint Decode(ReadOnlySpan<byte> payload, int offset)
        {
            return (uint)payload[offset]
                | ((uint)payload[offset + 1] << 8)
                | ((uint)payload[offset + 2] << 16)
                | ((uint)payload[offset + 3] << 24);
        }
    }
}
