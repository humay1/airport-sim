using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;

namespace AirportSim.App.Ui
{
    // The production sink. Spec: 17 §17.5 step 4 (Q-010), 08 §8.7.
    internal sealed class LaneCommandSink : ILaneCommandSink
    {
        private readonly ISimHost _host;
        private readonly IFlowSystem _flow;
        private readonly Dictionary<uint, Pending> _pending = new Dictionary<uint, Pending>();

        // SetServersOpen payload (08 §8.7): NodeId.Value uint32 LE, count int32 LE.
        // Admission copies the payload, so one buffer serves every submit.
        private readonly byte[] _payload = new byte[8];

        internal LaneCommandSink(ISimHost host, IFlowSystem flow)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public void Request(NodeId node, int delta)
        {
            if (delta != 1 && delta != -1)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), "a lane request is +1 or -1");
            }

            if (!_flow.TryGetLaneState(node, out LaneState lanes))
            {
                return;
            }

            ulong now = _host.CurrentTick;
            int baseCount = lanes.ServersOpen;
            if (_pending.TryGetValue(node.Value, out Pending p) && now <= p.Tick)
            {
                baseCount = p.Count;
            }

            long raw = (long)baseCount + delta;
            int target = (int)(raw < 0 ? 0 : (raw > lanes.ServerCount ? lanes.ServerCount : raw));
            if (target == baseCount)
            {
                return;
            }

            ulong tick = now + SimConstants.COMMAND_MIN_LEAD_TICKS;
            uint id = node.Value;
            _payload[0] = (byte)id;
            _payload[1] = (byte)(id >> 8);
            _payload[2] = (byte)(id >> 16);
            _payload[3] = (byte)(id >> 24);
            _payload[4] = (byte)target;
            _payload[5] = (byte)(target >> 8);
            _payload[6] = (byte)(target >> 16);
            _payload[7] = (byte)(target >> 24);

            var cmd = new Command(tick, SimConstants.PLAYER_LOCAL, CommandKind.SetServersOpen, _payload);
            if (_host.TrySubmit(cmd, out _))
            {
                _pending[id] = new Pending(tick, target);
            }
        }

        private readonly struct Pending
        {
            public Pending(ulong tick, int count)
            {
                Tick = tick;
                Count = count;
            }

            public ulong Tick { get; }

            public int Count { get; }
        }
    }
}
