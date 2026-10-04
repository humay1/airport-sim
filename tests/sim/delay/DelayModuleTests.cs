using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>
    /// 06 delay_module_never_writes and 14 §14.1/§14.10–§14.13a: the module's
    /// public shape, its references, what it subscribes to and publishes, and
    /// what it may not touch. The static checks read the compiled assembly's
    /// metadata and IL only (07 L5: the public surface is all tests compile
    /// against).
    /// </summary>
    public sealed class DelayModuleTests
    {
        private static readonly Assembly DelayAssembly = typeof(IDelaySystem).Assembly;

        private static readonly Type[] Consumed =
        {
            typeof(FlightPlanPublished), typeof(FlightMilestoneReached),
            typeof(AircraftHeldForRunway), typeof(AircraftHeldForRunwayReleased),
            typeof(AircraftHeldOnTaxiway), typeof(AircraftHeldOnTaxiwayReleased),
            typeof(StandUnavailable), typeof(StandAssigned),
            typeof(TurnaroundJobBlocked), typeof(TurnaroundJobUnblocked),
            typeof(DepartureHeldForPassengers), typeof(DepartureHeldForPassengersReleased),
            typeof(PassengersMissedFlight),
        };

        private static readonly Dictionary<short, OpCode> OpCodeMap = OpCodesByValue();

        private static Dictionary<short, OpCode> OpCodesByValue()
        {
            var map = new Dictionary<short, OpCode>();
            foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var op = (OpCode)f.GetValue(null)!;
                map[op.Value] = op;
            }

            return map;
        }

        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>Every method and constructor body in the assembly, compiler-generated ones (lambdas, iterators) included.</summary>
        private static IEnumerable<MethodBase> Bodies()
        {
            foreach (Type t in DelayAssembly.GetTypes())
            {
                foreach (MethodInfo m in t.GetMethods(All))
                {
                    yield return m;
                }

                foreach (ConstructorInfo c in t.GetConstructors(All))
                {
                    yield return c;
                }
            }
        }

        /// <summary>Every method a body calls (call, callvirt, newobj, ldftn, ldvirtftn), resolved in its generic context.</summary>
        private static List<MethodBase> Calls(MethodBase body, List<string> unresolved)
        {
            var result = new List<MethodBase>();
            MethodBody? mb;
            try
            {
                mb = body.GetMethodBody();
            }
            catch (Exception)
            {
                return result;
            }

            if (mb == null)
            {
                return result;
            }

            byte[] il = mb.GetILAsByteArray() ?? Array.Empty<byte>();
            Dictionary<short, OpCode> ops = OpCodeMap;
            int i = 0;
            while (i < il.Length)
            {
                short value = il[i++];
                if (value == 0xFE)
                {
                    value = unchecked((short)(0xFE00 | il[i++]));
                }

                Assert.True(ops.TryGetValue(value, out OpCode op), "unknown IL opcode in " + body.DeclaringType + "." + body.Name);
                int size;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: size = 0; break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: size = 1; break;
                    case OperandType.InlineVar: size = 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: size = 8; break;
                    case OperandType.InlineSwitch: size = 4 + (4 * BitConverter.ToInt32(il, i)); break;
                    default: size = 4; break;
                }

                if (op.OperandType == OperandType.InlineMethod)
                {
                    int token = BitConverter.ToInt32(il, i);
                    try
                    {
                        Type[]? typeArgs = body.DeclaringType != null && body.DeclaringType.IsGenericType ? body.DeclaringType.GetGenericArguments() : null;
                        Type[]? methodArgs = body.IsGenericMethod ? body.GetGenericArguments() : null;
                        MethodBase? target = body.Module.ResolveMethod(token, typeArgs, methodArgs);
                        if (target != null)
                        {
                            result.Add(target);
                        }
                    }
                    catch (ArgumentException ex)
                    {
                        unresolved.Add(body.DeclaringType + "." + body.Name + ": " + ex.Message);
                    }
                }

                i += size;
            }

            return result;
        }

        private static bool IsPublish(MethodBase m)
        {
            return m.Name == "Publish" && m.IsGenericMethod && m.DeclaringType != null && typeof(IEventPublisher).IsAssignableFrom(m.DeclaringType);
        }

        /// <summary>The type and every type it is built from (element types, generic arguments).</summary>
        private static IEnumerable<Type> Constituents(Type t)
        {
            yield return t;
            if (t.HasElementType)
            {
                foreach (Type e in Constituents(t.GetElementType()!))
                {
                    yield return e;
                }
            }

            if (t.IsGenericType)
            {
                foreach (Type a in t.GetGenericArguments())
                {
                    foreach (Type e in Constituents(a))
                    {
                        yield return e;
                    }
                }
            }
        }

        [Fact]
        public void test_delay_module_never_writes()
        {
            // 14 §14.14: static — src/sim/delay references no type outside
            // sim.core, holds no reference to any other ISimSystem, and publishes
            // no event type other than DelayEvent.
            var problems = new List<string>();

            // 1. Assembly references: no AirportSim module but sim.core (07 L2:
            //    AirportSim.Sim.Delay's only ProjectReference is Core).
            foreach (AssemblyName r in DelayAssembly.GetReferencedAssemblies())
            {
                if (r.Name != null && r.Name.StartsWith("AirportSim.", StringComparison.Ordinal) && r.Name != "AirportSim.Sim.Core")
                {
                    problems.Add("references assembly " + r.Name);
                }
            }

            // 2. No field of any type, static or instance, holds an ISimSystem
            //    from outside the module (directly, in an array or collection).
            foreach (Type t in DelayAssembly.GetTypes())
            {
                foreach (FieldInfo f in t.GetFields(All))
                {
                    foreach (Type c in Constituents(f.FieldType))
                    {
                        if (typeof(ISimSystem).IsAssignableFrom(c) && c.Assembly != DelayAssembly)
                        {
                            problems.Add(t.FullName + "." + f.Name + " holds " + c.FullName);
                        }
                    }
                }
            }

            // 3. Every Publish<T> call site publishes DelayEvent.
            var unresolved = new List<string>();
            int publishSites = 0;
            foreach (MethodBase body in Bodies())
            {
                foreach (MethodBase target in Calls(body, unresolved))
                {
                    if (IsPublish(target))
                    {
                        publishSites++;
                        Type evt = target.GetGenericArguments()[0];
                        if (evt != typeof(DelayEvent))
                        {
                            problems.Add(body.DeclaringType + "." + body.Name + " publishes " + evt.FullName);
                        }
                    }
                }
            }

            Assert.True(problems.Count == 0, string.Join("\n", problems));
            Assert.True(unresolved.Count == 0, "unresolved call tokens:\n" + string.Join("\n", unresolved));
            Assert.True(publishSites > 0, "no Publish<DelayEvent> call site at all: §14.8 publication is missing");

            // 4. And at run time, over generated days: every event published with
            //    sim.delay's Source is a DelayEvent, and there are some.
            var run = new GeneratedRun(0x0024_1A11UL, Generator.SyntheticDay, model: false);
            run.RunDays(1);
            Assert.True(run.Rig.R.FromDelay.TryGetValue(typeof(DelayEvent), out int n) && n > 50, "sim.delay published too few DelayEvents");
            Assert.Equal(new[] { typeof(DelayEvent) }, run.Rig.R.FromDelay.Keys.ToArray());
        }

        [Fact]
        public void test_delay_module_reads_no_content_and_no_rng()
        {
            // 14 §14.1: no TickContext.Content (nor SystemServices.Content); 14
            // §14.13: no RNG, no stream declared speculatively.
            var unresolved = new List<string>();
            var problems = new List<string>();
            int subscribes = 0;
            foreach (MethodBase body in Bodies())
            {
                foreach (MethodBase target in Calls(body, unresolved))
                {
                    Type? owner = target.DeclaringType;
                    if (target.Name == "Subscribe" && owner == typeof(IEventBus))
                    {
                        subscribes++;
                    }

                    bool content = target.Name == "get_Content" && (owner == typeof(TickContext) || owner == typeof(SystemServices));
                    bool rng = (target.Name == "get_Rng" && owner == typeof(TickContext))
                        || (owner != null && (typeof(IRandomService).IsAssignableFrom(owner) || typeof(IRandomStream).IsAssignableFrom(owner)))
                        || owner == typeof(RngStreamName);
                    if (content || rng)
                    {
                        problems.Add(body.DeclaringType + "." + body.Name + " calls " + owner + "." + target.Name);
                    }
                }
            }

            // The scan saw the real module: §14.13a subscribes through services.Events.
            Assert.True(subscribes > 0, "no IEventBus.Subscribe call found in sim.delay's IL");
            Assert.True(problems.Count == 0, string.Join("\n", problems));
            Assert.True(unresolved.Count == 0, "unresolved call tokens:\n" + string.Join("\n", unresolved));
        }

        [Fact]
        public void test_delay_module_public_shape_matches_spec()
        {
            // 07 L1/L6/L10, 14 §14.3, §14.10, §14.13a.
            Assert.Equal("AirportSim.Sim.Delay", DelayAssembly.GetName().Name);

            Type sys = typeof(IDelaySystem);
            Assert.True(sys.IsInterface && sys.IsPublic && sys.Namespace == "AirportSim.Sim.Delay");
            Assert.Contains(typeof(ISimSystem), sys.GetInterfaces());
            Assert.Empty(sys.GetProperties());
            MethodInfo[] methods = sys.GetMethods();
            Assert.Equal(
                new[] { "LeavesOf", "RetainedFlights", "TryGetFlightDelay", "TryGetNode" },
                methods.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());

            MethodInfo get = sys.GetMethod("TryGetFlightDelay")!;
            Assert.Equal(typeof(bool), get.ReturnType);
            ParameterInfo[] gp = get.GetParameters();
            Assert.Equal(2, gp.Length);
            Assert.Equal(typeof(FlightId), gp[0].ParameterType);
            Assert.True(gp[1].IsOut && gp[1].ParameterType == typeof(FlightDelay).MakeByRefType());

            MethodInfo retained = sys.GetMethod("RetainedFlights")!;
            Assert.Equal(typeof(IReadOnlyList<FlightId>), retained.ReturnType);
            Assert.Empty(retained.GetParameters());

            MethodInfo leaves = sys.GetMethod("LeavesOf")!;
            Assert.Equal(typeof(IReadOnlyList<DelayEventId>), leaves.ReturnType);
            Assert.Equal(new[] { typeof(FlightId) }, leaves.GetParameters().Select(p => p.ParameterType).ToArray());

            MethodInfo node = sys.GetMethod("TryGetNode")!;
            Assert.Equal(typeof(bool), node.ReturnType);
            ParameterInfo[] np = node.GetParameters();
            Assert.Equal(2, np.Length);
            Assert.Equal(typeof(DelayEventId), np[0].ParameterType);
            Assert.True(np[1].IsOut && np[1].ParameterType == typeof(DelayNode).MakeByRefType());

            // DelayFactory.CreateSystem(in SystemServices) -> IDelaySystem, its only public method.
            Type factory = typeof(DelayFactory);
            Assert.True(factory.IsPublic && factory.IsAbstract && factory.IsSealed, "DelayFactory must be a public static class");
            MethodInfo[] fm = factory.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Assert.Single(fm);
            Assert.Equal("CreateSystem", fm[0].Name);
            Assert.Equal(typeof(IDelaySystem), fm[0].ReturnType);
            ParameterInfo[] cp = fm[0].GetParameters();
            Assert.Single(cp);
            Assert.True(cp[0].IsIn && cp[0].ParameterType == typeof(SystemServices).MakeByRefType(), "CreateSystem takes in SystemServices");

            // FlightDelay: a public readonly struct, the §14.3 members in order,
            // each a get-only property, and one constructor taking them in order.
            Type fd = typeof(FlightDelay);
            Assert.True(fd.IsValueType && fd.IsPublic && fd.Namespace == "AirportSim.Sim.Delay");
            Assert.Contains(fd.GetCustomAttributes(false), a => a.GetType().FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
            var members = new (string Name, Type Type)[]
            {
                ("Flight", typeof(FlightId)), ("Kind", typeof(MovementKind)), ("HasRotation", typeof(bool)), ("Rotation", typeof(FlightId)),
                ("Root", typeof(DelayEventId)), ("TotalTicks", typeof(ulong)), ("TotalMinutes", typeof(Fx)), ("CheckpointsReached", typeof(int)),
                ("LastCheckpointActual", typeof(ulong)), ("Finalised", typeof(bool)), ("FinalisedAt", typeof(ulong)), ("MissedPassengers", typeof(int)),
                ("MissedLastBlockedAt", typeof(NodeId?)),
            };
            PropertyInfo[] props = fd.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.MetadataToken).ToArray();
            Assert.Equal(members.Select(m => m.Name + ":" + m.Type.FullName).ToArray(), props.Select(p => p.Name + ":" + p.PropertyType.FullName).ToArray());
            Assert.All(props, p => Assert.True(p.CanRead && p.SetMethod == null, p.Name + " must be get-only"));
            Assert.NotNull(fd.GetConstructor(members.Select(m => m.Type).ToArray()));

            // Id 7 and Name "sim.delay" (08 §8.5).
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(1UL, ContentIndexFactory.Create(new List<IContentDefinition>()), new RecordingCheckpointSink(), new NullLog()));
            IDelaySystem d = DelayFactory.CreateSystem(b.Services);
            Assert.Equal(DConst.DelayPos, d.Id.Value);
            Assert.Equal("sim.delay", d.Name);
        }

        [Fact]
        public void test_delay_module_subscribes_to_exactly_the_consumed_events_and_registers_no_command()
        {
            // 14 §14.12: it subscribes, as SystemId 7, to the consumed table and
            // to nothing else; §14.11: no command handler.
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(1UL, ContentIndexFactory.Create(new List<IContentDefinition>()), new RecordingCheckpointSink(), new NullLog()));
            var shim = new ShimBus(b.Services.Events, null, null);
            var commands = new CountingRegistry(b.Services.Commands);
            DelayFactory.CreateSystem(new SystemServices(shim, b.Services.Ids, b.Services.Content, commands));

            Assert.All(shim.Subscribed, s => Assert.Equal(DConst.DelayPos, s.Subscriber));
            Assert.Equal(
                Consumed.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                shim.Subscribed.Select(s => s.Type.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            Assert.Equal(0, commands.Registered);
        }

        private sealed class CountingRegistry : ICommandHandlerRegistry
        {
            private readonly ICommandHandlerRegistry _real;

            public CountingRegistry(ICommandHandlerRegistry real)
            {
                _real = real;
            }

            public int Registered { get; private set; }

            public void Register(SystemId owner, ICommandHandler handler)
            {
                Registered++;
                _real.Register(owner, handler);
            }
        }
    }
}
