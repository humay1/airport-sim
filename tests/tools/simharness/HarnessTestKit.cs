using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AirportSim.Sim.Core;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// Test-only helpers for the T-006 harness tests, written from
    /// 19-interfaces-harness.md (Q-025, Q-026, Q-027) and 08 §8.9, never from
    /// an implementation.
    /// </summary>
    internal static class HarnessTestKit
    {
        internal const uint TicksPerDay = (uint)SimConstants.TICKS_PER_SIM_DAY;

        // ------------------------------------------------------------ oracles

        /// <summary>FNV-1a-64 over values fed as 8 little-endian bytes each (08 §8.9).</summary>
        internal static ulong Fnv(params ulong[] values)
        {
            ulong h = 0xCBF29CE484222325UL;
            unchecked
            {
                foreach (ulong v in values)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        h = (h ^ (byte)(v >> (8 * i))) * 0x100000001B3UL;
                    }
                }
            }
            return h;
        }

        /// <summary>
        /// §19.2 script: one NoOp for each 1 ≤ t &lt; ticks with t % 100 == 0.
        /// </summary>
        internal static uint ScriptLength(uint ticks)
        {
            return ticks == 0 ? 0u : (ticks - 1) / 100;
        }

        /// <summary>Checkpoints in one run: ticks t in [0, ticks) with t % 600 == 0 (08 §8.9).</summary>
        internal static uint CheckpointCount(uint ticks)
        {
            return (ticks - 1) / (uint)SimConstants.HASH_CHECKPOINT_TICKS + 1;
        }

        /// <summary>
        /// Final WorldStateHash of one run with no systems registered: the ticks
        /// executed, then the core section after every scripted NoOp has applied
        /// (next Sequence = script length + 1, no pending, no id counters).
        /// </summary>
        internal static ulong EmptyCompositionFinalHash(uint ticks)
        {
            ulong core = Fnv(ScriptLength(ticks) + 1UL, 0UL, 0UL);
            return Fnv(ticks, core);
        }

        internal static string Hex16(ulong v)
        {
            return v.ToString("x16", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------ doubles

        internal sealed class EmptyContent : IContentIndex
        {
            public bool TryGet<T>(ContentId id, out T definition) where T : IContentDefinition
            {
                definition = default!;
                return false;
            }

            public IReadOnlyList<ContentId> AllOf(ContentKind kind)
            {
                return Array.Empty<ContentId>();
            }
        }

        /// <summary>
        /// A probe system. Its state is the number of ticks it has seen plus a
        /// "drift" flag that turns on at <c>driftFromTick</c> when <c>drifts</c> is
        /// set, so a composer can make one run differ from another from a chosen
        /// tick onward. It also records what the host handed it.
        /// </summary>
        internal sealed class Probe : ISimSystem
        {
            private readonly ushort _position;
            private readonly bool _drifts;
            private readonly ulong _driftFromTick;
            private readonly ulong _salt;
            private ulong _ticks;
            private bool _drifted;

            public Probe(ushort position, bool drifts = false, ulong driftFromTick = 0, ulong salt = 0)
            {
                _position = position;
                _drifts = drifts;
                _driftFromTick = driftFromTick;
                _salt = salt;
            }

            public SystemId Id => new SystemId(_position);

            public string Name => "probe.harness";

            public ulong TicksSeen => _ticks;

            public ulong? ObservedMasterSeed { get; private set; }

            public IContentIndex? ObservedContent { get; private set; }

            public bool FeedMasterSeed { get; set; }

            public Action<TickContext>? OnTick { get; set; }

            public void Tick(in TickContext ctx)
            {
                ObservedMasterSeed = ctx.Rng.MasterSeed;
                ObservedContent = ctx.Content;
                _ticks++;
                if (_drifts && ctx.Tick >= _driftFromTick)
                {
                    _drifted = true;
                }
                OnTick?.Invoke(ctx);
            }

            public ulong ComputeStateHash()
            {
                var h = new StateHasher();
                h.Feed(_ticks);
                h.Feed(_drifted);
                h.Feed(_salt);
                if (FeedMasterSeed && ObservedMasterSeed.HasValue)
                {
                    h.Feed(ObservedMasterSeed.Value);
                }
                return h.Result;
            }
        }

        /// <summary>A composer that counts its calls and remembers every builder it was given.</summary>
        internal sealed class CountingComposer
        {
            private readonly Action<ISimHostBuilder, int> _register;

            public CountingComposer(Action<ISimHostBuilder, int> register)
            {
                _register = register;
            }

            public int Calls { get; private set; }

            public readonly List<ISimHostBuilder> Builders = new List<ISimHostBuilder>();

            public void Compose(ISimHostBuilder builder)
            {
                Calls++;
                Builders.Add(builder);
                _register(builder, Calls);
            }
        }

        internal static void NoSystems(ISimHostBuilder builder)
        {
        }

        // ------------------------------------------------------------ CLI

        internal sealed class CliResult
        {
            public CliResult(int exit, string stdout, string stderr)
            {
                Exit = exit;
                Stdout = stdout;
                Stderr = stderr;
            }

            public int Exit { get; }
            public string Stdout { get; }
            public string Stderr { get; }
        }

        internal static CliResult Cli(params string[] args)
        {
            var stdout = new StringWriter(CultureInfo.InvariantCulture);
            var stderr = new StringWriter(CultureInfo.InvariantCulture);
            int exit = HarnessCli.Run(args, stdout, stderr);
            return new CliResult(exit, stdout.ToString(), stderr.ToString());
        }
    }
}
