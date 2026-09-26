using System;
using System.Collections.Generic;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The one <see cref="ICommandQueue"/> implementation. Spec: 08-interfaces-core.md §8.7
    /// ("Queue semantics", Q-020; "Issuer, kinds and payloads" and "Dispatch", Q-010) and
    /// §8.9 ("the core section", Q-017).
    ///
    /// <para>Every admitted command is kept forever in <see cref="_commandLog"/>, sorted by
    /// (Tick, Issuer, Sequence). <see cref="_pendingStart"/> is the index of the first
    /// not-yet-applied command: since admission only ever accepts a tick strictly in the
    /// future (§8.7 "Admission"), every newly inserted command sorts at or after this index,
    /// so <see cref="ApplyDue"/> only ever advances the pointer forward and never rescans
    /// applied entries. This keeps <see cref="ApplyDue"/> allocation-free.</para>
    /// </summary>
    internal sealed class CommandQueue : ICommandQueue
    {
        private readonly CommandHandlerRegistry _registry;
        private readonly EventBus _eventBus;
        private readonly ISimClock _clock;
        private readonly IRandomService _rng;
        private readonly IContentIndex _content;
        private readonly ISimLog _logSink;

        // Sorted ascending by (Tick, Issuer.Value, Sequence). Never trimmed at T-005: trimming
        // is sim.save's call (§8.7).
        private readonly List<Command> _commandLog = new List<Command>();
        private int _pendingStart;
        private uint _nextSequence = 1;
        private ulong _currentTick;

        internal CommandQueue(
            CommandHandlerRegistry registry,
            EventBus eventBus,
            ISimClock clock,
            IRandomService rng,
            IContentIndex content,
            ISimLog logSink)
        {
            _registry = registry;
            _eventBus = eventBus;
            _clock = clock;
            _rng = rng;
            _content = content;
            _logSink = logSink;
        }

        /// <summary>Kept in step with <c>ISimHost.CurrentTick</c> by the host, since TrySubmit
        /// is only ever callable outside Step (§8.7).</summary>
        internal void SetCurrentTick(ulong tick)
        {
            _currentTick = tick;
        }

        public bool TrySubmit(in Command cmd, out CommandRejection reason)
        {
            if (cmd.Tick < _currentTick + SimConstants.COMMAND_MIN_LEAD_TICKS)
            {
                reason = CommandRejection.TooLate;
                return false;
            }

            if (cmd.Issuer != SimConstants.PLAYER_LOCAL)
            {
                reason = CommandRejection.NotPermitted;
                return false;
            }

            CommandRejection kindCheck;
            if (cmd.Kind == CommandKind.NoOp)
            {
                kindCheck = cmd.Payload.Length == 0 ? CommandRejection.None : CommandRejection.MalformedPayload;
            }
            else if (_registry.TryGetHandler(cmd.Kind, out ICommandHandler? handler, out _))
            {
                kindCheck = handler!.Validate(new ReadOnlySpan<byte>(cmd.Payload));
            }
            else
            {
                reason = CommandRejection.UnknownKind;
                return false;
            }

            if (kindCheck != CommandRejection.None)
            {
                reason = kindCheck;
                return false;
            }

            uint sequence = _nextSequence;
            _nextSequence++;
            byte[] payloadCopy = (byte[])cmd.Payload.Clone();
            Insert(new Command(cmd.Tick, cmd.Issuer, cmd.Kind, payloadCopy, sequence));

            reason = CommandRejection.None;
            return true;
        }

        public void ApplyDue(ulong tick)
        {
            var ctx = new TickContext(tick, _clock, _eventBus, _rng, _content, _logSink);
            while (_pendingStart < _commandLog.Count && _commandLog[_pendingStart].Tick == tick)
            {
                Command cmd = _commandLog[_pendingStart];
                _pendingStart++;

                if (cmd.Kind == CommandKind.NoOp)
                {
                    continue;
                }

                // Admission already proved a handler exists for this kind (UnknownKind would
                // have rejected it otherwise), and no API removes one afterwards.
                if (_registry.TryGetHandler(cmd.Kind, out ICommandHandler? handler, out SystemId owner))
                {
                    _eventBus.SetCurrentSource(owner);
                    handler!.Apply(in cmd, in ctx);
                }
            }
        }

        public IReadOnlyList<Command> LogSince(ulong tick)
        {
            int start = LowerBound(tick);
            var result = new List<Command>(_commandLog.Count - start);
            for (int i = start; i < _commandLog.Count; i++)
            {
                Command c = _commandLog[i];
                result.Add(new Command(c.Tick, c.Issuer, c.Kind, (byte[])c.Payload.Clone(), c.Sequence));
            }
            return result;
        }

        /// <summary>Spec §8.9 core section, in order: the next Sequence, the pending count,
        /// then each pending command in (Tick, Issuer, Sequence) order.</summary>
        internal void FeedCoreHash(ref StateHasher hasher)
        {
            hasher.Feed((ulong)_nextSequence);
            hasher.Feed((ulong)(_commandLog.Count - _pendingStart));
            for (int i = _pendingStart; i < _commandLog.Count; i++)
            {
                Command c = _commandLog[i];
                hasher.Feed(c.Tick);
                hasher.Feed((ulong)c.Issuer.Value);
                hasher.Feed((ulong)c.Kind);
                hasher.Feed((ulong)c.Sequence);
                hasher.Feed(new ReadOnlySpan<byte>(c.Payload));
            }
        }

        private void Insert(Command cmd)
        {
            // Every admitted command's Tick is strictly greater than _currentTick, and no
            // command with Tick <= _currentTick remains at or after _pendingStart, so the
            // insertion point is always within [_pendingStart, Count].
            int lo = _pendingStart;
            int hi = _commandLog.Count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (IsBefore(_commandLog[mid], cmd))
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }
            _commandLog.Insert(lo, cmd);
        }

        private int LowerBound(ulong tick)
        {
            int lo = 0;
            int hi = _commandLog.Count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (_commandLog[mid].Tick < tick)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }
            return lo;
        }

        private static bool IsBefore(Command a, Command b)
        {
            if (a.Tick != b.Tick)
            {
                return a.Tick < b.Tick;
            }
            if (a.Issuer.Value != b.Issuer.Value)
            {
                return a.Issuer.Value < b.Issuer.Value;
            }
            return a.Sequence < b.Sequence;
        }
    }
}
