using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The one <see cref="ISimHost"/> implementation: the fixed per-tick phase loop.
    /// Spec: 08-interfaces-core.md §8.5 ("Fixed phase order"), §8.5a, §8.7 (Q-020), §8.9 (Q-017).
    /// </summary>
    internal sealed class SimHost : ISimHost
    {
        private readonly ISimSystem[] _systems;
        private readonly EventBus _eventBus;
        private readonly IRandomService _rng;
        private readonly IContentIndex _content;
        private readonly ISimLog _log;
        private readonly ICheckpointSink _checkpoints;
        private readonly IdAllocator _idAllocator;
        private readonly SimClock _clock;

        private ulong _ticksExecuted;
        private bool _broken;
        private bool _inStep;

        internal SimHost(
            ISimSystem[] systems,
            EventBus eventBus,
            IRandomService rng,
            IContentIndex content,
            ISimLog log,
            ICheckpointSink checkpoints,
            IdAllocator idAllocator)
        {
            _systems = systems;
            _eventBus = eventBus;
            _rng = rng;
            _content = content;
            _log = log;
            _checkpoints = checkpoints;
            _idAllocator = idAllocator;
            _clock = new SimClock(this);
        }

        public ulong CurrentTick => _ticksExecuted;

        public void Step(uint ticks)
        {
            EnsureNotBroken();
            _inStep = true;
            try
            {
                for (uint i = 0; i < ticks; i++)
                {
                    RunOneTick();
                }
            }
            finally
            {
                _inStep = false;
            }
        }

        public ulong WorldStateHash()
        {
            EnsureNotBroken();
            return ComputeWorldHash();
        }

        public bool TrySubmit(in Command cmd, out CommandRejection reason)
        {
            EnsureNotBroken();
            if (_inStep)
            {
                throw new InvalidOperationException("TrySubmit cannot be called during Step (08-interfaces-core.md §8.7, Q-020)");
            }

            // T-001 ships the command family as shape only (Q-014): nothing is ever admitted.
            // T-005 gives ICommandHandlerRegistry/ICommandQueue their real behaviour.
            reason = CommandRejection.UnknownKind;
            return false;
        }

        public System.Collections.Generic.IReadOnlyList<Command> CommandLogSince(ulong tick)
        {
            EnsureNotBroken();
            // No command is ever admitted at T-001 (see TrySubmit above), so the log is
            // always empty.
            return Array.Empty<Command>();
        }

        private void RunOneTick()
        {
            ulong t = _ticksExecuted;
            try
            {
                _eventBus.BeginTick(t);

                // Phase 1: command application. Nothing is ever pending at T-001 (see TrySubmit).
                _eventBus.SetPhase(1);

                // Phase 2: system update, in registry order.
                _eventBus.SetPhase(2);
                var ctx = new TickContext(t, _clock, _eventBus, _rng, _content, _log);
                for (int i = 0; i < _systems.Length; i++)
                {
                    _eventBus.SetCurrentSource(_systems[i].Id);
                    _systems[i].Tick(in ctx);
                }

                // Phase 3: event dispatch.
                _eventBus.Dispatch(in ctx);

                // The tick is complete: ticks-executed now counts this one.
                _ticksExecuted = t + 1;

                // Phase 4: checkpoint, if due.
                if (t % SimConstants.HASH_CHECKPOINT_TICKS == 0)
                {
                    RecordCheckpoint(t);
                }
            }
            catch (Exception ex)
            {
                WrapAndBreak(t, ex);
            }
        }

        private void RecordCheckpoint(ulong t)
        {
            ulong coreHash = ComputeCoreHash();
            var systemHashes = new ulong[_systems.Length];
            for (int i = 0; i < _systems.Length; i++)
            {
                systemHashes[i] = _systems[i].ComputeStateHash();
            }

            var hasher = new StateHasher();
            hasher.Feed(_ticksExecuted);
            hasher.Feed(coreHash);
            for (int i = 0; i < systemHashes.Length; i++)
            {
                hasher.Feed(systemHashes[i]);
            }

            _checkpoints.Record(new Checkpoint(t, hasher.Result, coreHash, systemHashes));
        }

        private ulong ComputeWorldHash()
        {
            var hasher = new StateHasher();
            hasher.Feed(_ticksExecuted);
            hasher.Feed(ComputeCoreHash());
            for (int i = 0; i < _systems.Length; i++)
            {
                hasher.Feed(_systems[i].ComputeStateHash());
            }
            return hasher.Result;
        }

        private ulong ComputeCoreHash()
        {
            var hasher = new StateHasher();

            // Next command Sequence to assign; always 1, since nothing is ever admitted at
            // T-001 (Q-020: Sequence is a global counter starting at 1).
            hasher.Feed(1UL);

            // Number of pending (admitted, not yet applied) commands; always 0 at T-001.
            hasher.Feed(0UL);

            _idAllocator.FeedCounters(ref hasher);

            return hasher.Result;
        }

        private void WrapAndBreak(ulong tick, Exception ex)
        {
            _broken = true;
            ulong worldHash = 0;
            bool hasWorldHash = true;
            try
            {
                worldHash = ComputeWorldHash();
            }
            catch
            {
                hasWorldHash = false;
            }

            throw new SimInvariantException(
                "a broken invariant escaped tick " + tick.ToString(System.Globalization.CultureInfo.InvariantCulture),
                tick,
                ex,
                worldHash,
                hasWorldHash);
        }

        private void EnsureNotBroken()
        {
            if (_broken)
            {
                throw new InvalidOperationException("the host is unusable after a broken invariant (08-interfaces-core.md §8.5a)");
            }
        }
    }
}
