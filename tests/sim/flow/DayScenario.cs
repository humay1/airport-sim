using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// A deterministic day on the Phase 0 fixture. A probe at sim.schedule's
    /// registry position injects departing cohorts (one flight per sim hour,
    /// from both sources) and absorbs each flight two hours after its first
    /// passengers, as sim.airside would. Lane commands are submitted between
    /// ticks, as the UI would. Every input is drawn from SplitMix64 with the
    /// caller's seed, so two runs with one seed see identical inputs.
    /// </summary>
    internal sealed class DayScenario
    {
        public long Injected;
        public long Boarded;
        public readonly List<string> Trace = new List<string>();

        public static IContentIndex Content()
        {
            return ContentIndexFactory.Create(new IContentDefinition[]
            {
                FlowKit.Pax(FlowKit.Walker, Fx.FromRatio(13, 10)),
                FlowKit.Queue(Landside.SecurityProfile, Fx.Parse("2.5"), 45, Fx.FromInt(12), Fx.FromInt(3)),
            });
        }

        /// <summary>
        /// Runs <paramref name="ticks"/> ticks. <paramref name="replay"/>, when
        /// given, replaces the drawn lane commands with a recorded command log.
        /// </summary>
        public void Run(Rig rig, ulong inputSeed, int ticks, bool promote = false, IReadOnlyList<Command>? replay = null, System.Action<ulong>? afterTick = null)
        {
            var rng = new SplitMix64(inputSeed);
            var injections = new Dictionary<ulong, (uint Source, int Count, ulong Flight)>();
            var absorbs = new Dictionary<ulong, ulong>();
            for (ulong t = 0; t < (ulong)ticks; t += 15)
            {
                ulong flight = 1 + t / SimConstants.TICKS_PER_SIM_HOUR;
                injections[t] = (rng.Range(0, 1) == 0 ? Landside.Kerb : Landside.RailBox, rng.Range(4, 30), flight);
            }

            for (ulong h = 2; h * SimConstants.TICKS_PER_SIM_HOUR < (ulong)ticks; h++)
            {
                absorbs[h * SimConstants.TICKS_PER_SIM_HOUR + 1] = h - 1;
            }

            var commands = new Dictionary<ulong, (uint Node, int Count)>();
            if (replay == null)
            {
                for (int k = 0; k < ticks / 400; k++)
                {
                    commands[(ulong)rng.Range(0, ticks - 2)] = (rng.Range(0, 1) == 0 ? Landside.SecurityA : Landside.SecurityB, rng.Range(0, 3));
                }
            }

            rig.Inject = (in TickContext ctx) =>
            {
                if (injections.TryGetValue(ctx.Tick, out var inj))
                {
                    rig.Flow.Inject(FlowKit.Key(inj.Flight), inj.Count, new NodeId(inj.Source));
                    Injected += inj.Count;
                }

                if (absorbs.TryGetValue(ctx.Tick, out ulong f))
                {
                    Boarded += rig.Flow.Absorb(new NodeId(Landside.Departed), new FlightId(f));
                }

                if (promote)
                {
                    rig.Flow.SetPromoted(new NodeId(ctx.Tick % 2 == 0 ? Landside.SecurityA : Landside.SecurityB), ctx.Tick % 3 != 0);
                }
            };

            int replayed = 0;
            for (int i = 0; i < ticks; i++)
            {
                ulong t = rig.Host.CurrentTick;
                if (replay != null)
                {
                    for (; replayed < replay.Count && replay[replayed].Tick <= t + 1; replayed++)
                    {
                        Command c = replay[replayed];
                        rig.Host.TrySubmit(new Command(c.Tick, c.Issuer, c.Kind, c.Payload), out _);
                    }
                }
                else if (commands.TryGetValue(t, out var cmd))
                {
                    rig.Submit(cmd.Node, cmd.Count, out _);
                }

                rig.Host.Step(1);
                afterTick?.Invoke(t);
            }

            rig.Inject = null;
        }
    }
}
