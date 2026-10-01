using System;
using System.Diagnostics;
using AirportSim.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// One tick's measured sim.flow work (03 "How a budget is measured",
    /// "Measured" and "Timing a module's handlers", Q-064): the
    /// Stopwatch.GetTimestamp() differences around sim.flow's Tick
    /// (<see cref="TimedSystem"/>) and around every handler sim.flow
    /// registers through the <see cref="SystemServices"/> that
    /// <see cref="Shim"/> returns, summed into <see cref="Elapsed"/>. A test
    /// zeroes it before each Step(1) and records it after, so the sample
    /// covers phases 1 to 3 of exactly one tick. The sample array is
    /// allocated with the clock, before any measured loop, and nothing here
    /// allocates per call.
    ///
    /// In allocation mode the same shims meter each call with T-037's
    /// Allocation meter instead of timing it.
    /// </summary>
    internal sealed class FlowClock
    {
        public FlowClock(int capacity)
        {
            Samples = new long[capacity];
        }

        public long Elapsed;
        public readonly long[] Samples;
        public int Count;
        public bool Recording;

        public int EventHandlers;
        public int CommandHandlers;
        public long EventCalls;
        public long ApplyCalls;

        public bool MeterAllocation;
        public long Allocated;

        /// <summary>
        /// The services sim.flow is built with: Events and Commands forward
        /// to the real bus and registry, and wrap each handler sim.flow
        /// registers, when it registers it, during construction (Q-064).
        /// Ids and Content are the builder's own.
        /// </summary>
        public SystemServices Shim(in SystemServices real)
        {
            return new SystemServices(new TimingBus(real.Events, this), real.Ids, real.Content, new TimingRegistry(real.Commands, this));
        }

        /// <summary>Starts a window: samples and the call counts restart from zero.</summary>
        public void StartWindow()
        {
            Count = 0;
            EventCalls = 0;
            ApplyCalls = 0;
            Recording = true;
        }

        public void StopWindow()
        {
            Recording = false;
        }

        /// <summary>Starts a tick's sample. Called right before Step(1).</summary>
        public void Begin()
        {
            Elapsed = 0;
        }

        /// <summary>Ends a tick's sample. Called right after Step(1).</summary>
        public void End()
        {
            if (Recording)
            {
                Samples[Count++] = Elapsed;
            }
        }

        public string Handlers()
        {
            return "shimmed " + EventHandlers + " sim.flow event handler(s) and " + CommandHandlers + " command handler(s)";
        }

        private sealed class TimingBus : IEventBus
        {
            private readonly IEventBus _real;
            private readonly FlowClock _clock;

            public TimingBus(IEventBus real, FlowClock clock)
            {
                _real = real;
                _clock = clock;
            }

            public EventId Publish<T>(in T evt, in EventRef cause) where T : struct, ISimEvent
            {
                return _real.Publish(evt, cause);
            }

            public void Subscribe<T>(SystemId subscriber, SimEventHandler<T> handler) where T : struct, ISimEvent
            {
                FlowClock clock = _clock;
                clock.EventHandlers++;
                _real.Subscribe<T>(subscriber, (in EventEnvelope env, in T evt, in TickContext ctx) =>
                {
                    clock.EventCalls++;
                    if (clock.MeterAllocation)
                    {
                        long metered = Allocation.Start();
                        handler(env, evt, ctx);
                        clock.Allocated += Allocation.Since(metered);
                        return;
                    }

                    long start = Stopwatch.GetTimestamp();
                    handler(env, evt, ctx);
                    clock.Elapsed += Stopwatch.GetTimestamp() - start;
                });
            }
        }

        /// <summary>Apply is timed; Validate runs at admission, outside the tick (03, 08 §8.7).</summary>
        private sealed class TimingRegistry : ICommandHandlerRegistry
        {
            private readonly ICommandHandlerRegistry _real;
            private readonly FlowClock _clock;

            public TimingRegistry(ICommandHandlerRegistry real, FlowClock clock)
            {
                _real = real;
                _clock = clock;
            }

            public void Register(SystemId owner, ICommandHandler handler)
            {
                _clock.CommandHandlers++;
                _real.Register(owner, new TimedHandler(handler, _clock));
            }
        }

        private sealed class TimedHandler : ICommandHandler
        {
            private readonly ICommandHandler _inner;
            private readonly FlowClock _clock;

            public TimedHandler(ICommandHandler inner, FlowClock clock)
            {
                _inner = inner;
                _clock = clock;
            }

            public CommandKind Kind => _inner.Kind;

            public CommandRejection Validate(ReadOnlySpan<byte> payload)
            {
                return _inner.Validate(payload);
            }

            public void Apply(in Command cmd, in TickContext ctx)
            {
                _clock.ApplyCalls++;
                if (_clock.MeterAllocation)
                {
                    long metered = Allocation.Start();
                    _inner.Apply(cmd, ctx);
                    _clock.Allocated += Allocation.Since(metered);
                    return;
                }

                long start = Stopwatch.GetTimestamp();
                _inner.Apply(cmd, ctx);
                _clock.Elapsed += Stopwatch.GetTimestamp() - start;
            }
        }
    }

    /// <summary>
    /// Wraps sim.flow at its own registry position and adds the timestamp
    /// difference around its Tick to the <see cref="FlowClock"/>. The
    /// injector, other systems' handlers, the bus's dispatch and the
    /// checkpoint are all outside it (03 "Measured").
    /// </summary>
    internal sealed class TimedSystem : ISimSystem
    {
        private readonly ISimSystem _inner;
        private readonly FlowClock _clock;

        public TimedSystem(ISimSystem inner, FlowClock clock)
        {
            _inner = inner;
            _clock = clock;
        }

        public SystemId Id => _inner.Id;

        public string Name => _inner.Name;

        public void Tick(in TickContext ctx)
        {
            if (_clock.MeterAllocation)
            {
                // The meter's collection runs inside the tick, right before
                // sim.flow's Tick; it touches no sim state.
                long metered = Allocation.Start();
                _inner.Tick(ctx);
                _clock.Allocated += Allocation.Since(metered);
                return;
            }

            long start = Stopwatch.GetTimestamp();
            _inner.Tick(ctx);
            _clock.Elapsed += Stopwatch.GetTimestamp() - start;
        }

        public ulong ComputeStateHash()
        {
            return _inner.ComputeStateHash();
        }
    }

    /// <summary>
    /// 03 "Budget tests: window and arithmetic" (Q-044, Q-045), in long
    /// arithmetic only: no Int128, no floating point, no TimeSpan (07 L11).
    /// </summary>
    internal static class FlowBudget
    {
        public const long BudgetMicros = 2500;

        /// <summary>
        /// Judges exactly n = TICKS_PER_SIM_DAY consecutive raw samples
        /// against a budget of <paramref name="budgetMicros"/> whole
        /// microseconds. The reported mean and p99 go to the test output
        /// before either assertion, so every run reports them, pass or fail.
        /// </summary>
        public static void Assert(FlowClock clock, long budgetMicros, string what, ITestOutputHelper output)
        {
            Xunit.Assert.Equal((int)SimConstants.TICKS_PER_SIM_DAY, clock.Count);
            long n = clock.Count;
            long f = Stopwatch.Frequency;

            // Step 2: whole microseconds rounded up, capped at C = B x n + 1.
            long cap = budgetMicros * n + 1;
            long guard = (long.MaxValue - f + 1) / 1_000_000;
            long[] u = new long[n];
            long sum = 0;
            for (long i = 0; i < n; i++)
            {
                long d = clock.Samples[i];
                u[i] = d > guard ? cap : Math.Min((d * 1_000_000 + f - 1) / f, cap);
                sum += u[i];
            }

            // Step 4: nearest rank.
            Array.Sort(u);
            long p99 = u[(99 * n + 99) / 100 - 1];

            // Step 5: the reported mean is rounded up.
            string report = what + " over " + n + " ticks: mean " + ((sum + n - 1) / n) + " us (budget " + budgetMicros
                + "), p99 " + p99 + " us (budget " + (2 * budgetMicros) + "), max " + u[n - 1] + " us; "
                + clock.Handlers() + ", in the window " + clock.ApplyCalls + " Apply call(s) and " + clock.EventCalls + " event handler call(s); Stopwatch.Frequency " + f;
            output.WriteLine(report);

            // Step 3 and step 4's pass conditions.
            Xunit.Assert.True(sum <= budgetMicros * n, "mean over budget " + budgetMicros + " us. " + report);
            Xunit.Assert.True(p99 <= 2 * budgetMicros, "p99 over " + (2 * budgetMicros) + " us. " + report);
        }
    }
}
