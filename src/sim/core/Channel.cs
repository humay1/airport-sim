using System;
using System.Collections.Generic;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Per-event-type storage: published (envelope, payload) pairs this tick, and the
    /// subscribers of this type, sorted by registry order (ascending <see cref="SystemId"/>,
    /// which is already a total order since one handler is allowed per (subscriber, T)).
    /// </summary>
    internal sealed class Channel<T> : IChannel where T : struct, ISimEvent
    {
        private readonly List<EventEnvelope> _envelopes = new List<EventEnvelope>();
        private readonly List<T> _payloads = new List<T>();
        private readonly List<(ushort Owner, SimEventHandler<T> Handler)> _subscribers =
            new List<(ushort Owner, SimEventHandler<T> Handler)>();

        public int Append(in EventEnvelope envelope, in T payload)
        {
            _envelopes.Add(envelope);
            _payloads.Add(payload);
            return _envelopes.Count - 1;
        }

        public void Subscribe(SystemId subscriber, SimEventHandler<T> handler)
        {
            foreach (var existing in _subscribers)
            {
                if (existing.Owner == subscriber.Value)
                {
                    throw new ArgumentException("a subscriber may register at most one handler per event type", nameof(subscriber));
                }
            }

            _subscribers.Add((subscriber.Value, handler));
            _subscribers.Sort((a, b) => a.Owner.CompareTo(b.Owner));
        }

        public void Clear()
        {
            _envelopes.Clear();
            _payloads.Clear();
        }

        public void Dispatch(int index, EventBus bus, in TickContext ctx)
        {
            EventEnvelope envelope = _envelopes[index];
            T payload = _payloads[index];
            for (int i = 0; i < _subscribers.Count; i++)
            {
                bus.SetCurrentSource(new SystemId(_subscribers[i].Owner));
                _subscribers[i].Handler(in envelope, in payload, in ctx);
            }
        }
    }
}
