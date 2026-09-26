using System;
using System.Collections.Generic;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The one <see cref="IEventBus"/> implementation. Spec: 08-interfaces-core.md §8.6
    /// ("Envelope and publication", Q-014). Owned and driven by <see cref="SimHost"/>, one
    /// per host.
    /// </summary>
    internal sealed class EventBus : IEventBus
    {
        private readonly Dictionary<Type, IChannel> _channels = new Dictionary<Type, IChannel>();
        private readonly List<(Type EventType, int Index)> _order = new List<(Type, int)>();
        private readonly SortedSet<ushort> _subscriberIds = new SortedSet<ushort>();

        private ulong _tick;
        private uint _nextSequence;
        private int _phase; // 0 = outside a tick; 1, 2 or 3 = the current phase
        private int _currentPass; // > 0 only while draining phase 3
        private SystemId _currentSource;
        private bool _built;

        internal void MarkBuilt()
        {
            _built = true;
        }

        internal void BeginTick(ulong tick)
        {
            _tick = tick;
            _nextSequence = 0;
            _order.Clear();
            foreach (var channel in _channels.Values)
            {
                channel.Clear();
            }
        }

        internal void SetPhase(int phase)
        {
            _phase = phase;
        }

        internal void SetCurrentSource(SystemId source)
        {
            _currentSource = source;
        }

        public EventId Publish<T>(in T evt, in EventRef cause) where T : struct, ISimEvent
        {
            if (_phase < 1 || _phase > 3)
            {
                throw new InvalidOperationException("Publish is only callable during phases 1-3 of a tick");
            }

            if (_phase == 3 && _currentPass >= SimConstants.MAX_EVENT_CASCADE_PASSES)
            {
                throw new SimInvariantException("event published during the final cascade pass", _tick);
            }

            if (_nextSequence >= SimConstants.MAX_EVENTS_PER_TICK)
            {
                throw new SimInvariantException("MAX_EVENTS_PER_TICK exceeded", _tick);
            }

            var id = new EventId(_tick, _nextSequence);
            _nextSequence++;

            var envelope = new EventEnvelope(id, _tick, _currentSource, cause);
            var channel = GetOrCreateChannel<T>();
            int index = channel.Append(in envelope, in evt);
            _order.Add((typeof(T), index));
            return id;
        }

        public void Subscribe<T>(SystemId subscriber, SimEventHandler<T> handler) where T : struct, ISimEvent
        {
            if (_built)
            {
                throw new InvalidOperationException("Subscribe is construction-time only");
            }

            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (subscriber.Value == 0 || subscriber.Value == 8 || subscriber.Value > 14)
            {
                throw new ArgumentException("subscriber must be a registered system id, 1-14 excluding 8", nameof(subscriber));
            }

            GetOrCreateChannel<T>().Subscribe(subscriber, handler);
            _subscriberIds.Add(subscriber.Value);
        }

        /// <summary>Every distinct subscriber id registered so far, ascending. Used by
        /// <see cref="SimHostBuilder.Build"/> to check every subscriber is a registered system.</summary>
        internal IReadOnlyCollection<ushort> SubscriberIds => _subscriberIds;

        /// <summary>Drains the tick's event queue, in cascading passes. Spec: §8.6 rules 1-4.</summary>
        internal void Dispatch(in TickContext ctx)
        {
            SetPhase(3);
            int passStart = 0;
            _currentPass = 0;
            while (passStart < _order.Count)
            {
                _currentPass++;
                int passEnd = _order.Count;
                for (int i = passStart; i < passEnd; i++)
                {
                    var (eventType, index) = _order[i];
                    _channels[eventType].Dispatch(index, this, in ctx);
                }
                passStart = passEnd;
            }
            _currentPass = 0;
            SetPhase(0);
        }

        private Channel<T> GetOrCreateChannel<T>() where T : struct, ISimEvent
        {
            Type type = typeof(T);
            if (_channels.TryGetValue(type, out IChannel? existing))
            {
                return (Channel<T>)existing;
            }

            var created = new Channel<T>();
            _channels[type] = created;
            return created;
        }
    }
}
