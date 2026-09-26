using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// The event catalogue of 10 §10.9 (Q-018), declared in sim.core by
    /// T-026. Each struct holds payload fields only (08 §8.6) and follows 07
    /// L10. Also checks the non-event payload structs (DelayNode,
    /// DelayExplanation, ShowUpBucket) and the content definitions, which
    /// follow the same rule.
    /// </summary>
    public sealed class EventCatalogueTests
    {
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        private static List<string> ShapeViolations(PayloadShapes.Shape shape, bool allowKind)
        {
            var v = new List<string>();
            Type t = shape.Type;
            string n = t.Name;

            if (!t.IsPublic)
            {
                v.Add($"{n}: not public");
            }

            if (!PayloadShapes.IsReadOnlyStruct(t))
            {
                v.Add($"{n}: not a readonly struct (07 L10)");
            }

            ConstructorInfo[] ctors = t.GetConstructors(PublicInstance);
            if (ctors.Length != 1)
            {
                v.Add($"{n}: {ctors.Length} public constructors, expected exactly 1 (07 L10)");
            }
            else
            {
                Type[] actual = ctors[0].GetParameters().Select(p => p.ParameterType).ToArray();
                Type[] expected = shape.Members.Select(m => m.Type).ToArray();
                if (!actual.SequenceEqual(expected))
                {
                    v.Add($"{n}: constructor ({string.Join(", ", actual.Select(x => x.Name))}), expected ({string.Join(", ", expected.Select(x => x.Name))})");
                }
            }

            var expectedNames = new HashSet<string>(shape.Members.Select(m => m.Name));
            foreach ((string name, Type type) in shape.Members)
            {
                PropertyInfo? p = t.GetProperty(name, PublicInstance);
                if (p == null)
                {
                    v.Add($"{n}.{name}: missing");
                    continue;
                }

                if (p.PropertyType != type)
                {
                    v.Add($"{n}.{name}: {p.PropertyType.Name}, expected {type.Name}");
                }

                if (p.SetMethod != null)
                {
                    v.Add($"{n}.{name}: has a setter or init accessor, expected get-only (07 L10)");
                }
            }

            foreach (PropertyInfo p in t.GetProperties(PublicInstance))
            {
                if (!expectedNames.Contains(p.Name) && !(allowKind && p.Name == "Kind"))
                {
                    v.Add($"{n}.{p.Name}: not a member in the spec");
                }
            }

            FieldInfo[] publicFields = t.GetFields(PublicInstance);
            if (publicFields.Length != 0)
            {
                v.Add($"{n}: public fields {string.Join(", ", publicFields.Select(f => f.Name))}; members are properties (07 L10)");
            }

            return v;
        }

        private static List<string> RoundTripViolations(PayloadShapes.Shape shape)
        {
            var v = new List<string>();
            ConstructorInfo ctor = shape.Type.GetConstructors(PublicInstance).Single();
            var args = new object?[shape.Members.Length];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = PayloadShapes.MakeValue(shape.Members[i].Type, i + 1);
            }

            object instance = ctor.Invoke(args);
            for (int i = 0; i < args.Length; i++)
            {
                string name = shape.Members[i].Name;
                object? got = shape.Type.GetProperty(name, PublicInstance)!.GetValue(instance);
                if (!PayloadShapes.SameValue(args[i], got))
                {
                    v.Add($"{shape.Type.Name}.{name}: constructor argument {i} was {args[i]}, property returned {got}");
                }
            }

            return v;
        }

        [Fact]
        public void test_event_catalogue_structs_match_10_9_field_for_field()
        {
            var violations = PayloadShapes.Events.SelectMany(s => ShapeViolations(s, allowKind: false)).ToList();
            Assert.True(violations.Count == 0, "10 §10.9 / 07 L10:\n" + string.Join("\n", violations));
            Assert.Equal(21, PayloadShapes.Events.Length);
        }

        [Fact]
        public void test_event_catalogue_constructor_arguments_land_in_the_named_members()
        {
            // Types alone cannot tell same-typed members apart (Flight vs
            // Rotation, Held vs BlockedBy), so each argument gets a distinct
            // value and must come back from the member of its position.
            var violations = PayloadShapes.Events.SelectMany(RoundTripViolations).ToList();
            Assert.True(violations.Count == 0, string.Join("\n", violations));
        }

        [Fact]
        public void test_event_catalogue_structs_implement_sim_event()
        {
            var missing = PayloadShapes.Events.Where(s => !typeof(ISimEvent).IsAssignableFrom(s.Type)).Select(s => s.Type.Name).ToList();
            Assert.True(missing.Count == 0, "not ISimEvent (08 §8.6): " + string.Join(", ", missing));
        }

        [Fact]
        public void test_event_catalogue_only_events_implement_sim_event()
        {
            // T-001's EventEnvelope/EventRef and the payload structs are not
            // events, and no undeclared event (FlightPlanRevised,
            // FlightCancelled, Phase 2 rows) exists yet (10 §10.9).
            var declared = new HashSet<Type>(PayloadShapes.Events.Select(s => s.Type));
            var extra = typeof(SimConstants).Assembly.GetExportedTypes()
                .Where(t => t.IsValueType && typeof(ISimEvent).IsAssignableFrom(t) && !declared.Contains(t))
                .Select(t => t.Name)
                .ToList();
            Assert.True(extra.Count == 0, "event structs not in 10 §10.9: " + string.Join(", ", extra));
            Assert.Equal(21, typeof(SimConstants).Assembly.GetExportedTypes().Count(t => t.IsValueType && typeof(ISimEvent).IsAssignableFrom(t)));
        }

        [Fact]
        public void test_event_catalogue_payload_structs_match_spec_field_for_field()
        {
            var violations = PayloadShapes.Payloads.SelectMany(s => ShapeViolations(s, allowKind: false))
                .Concat(PayloadShapes.Payloads.SelectMany(RoundTripViolations))
                .ToList();
            Assert.True(violations.Count == 0, "14 §14.3 / 08 §8.11 / 07 L10:\n" + string.Join("\n", violations));
        }

        [Fact]
        public void test_event_catalogue_nullable_members_accept_and_return_null()
        {
            var violations = new List<string>();
            foreach (PayloadShapes.Shape shape in PayloadShapes.Events.Concat(PayloadShapes.Payloads))
            {
                if (!shape.Members.Any(m => Nullable.GetUnderlyingType(m.Type) != null))
                {
                    continue;
                }

                var args = new object?[shape.Members.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    args[i] = Nullable.GetUnderlyingType(shape.Members[i].Type) != null
                        ? null
                        : PayloadShapes.MakeValue(shape.Members[i].Type, i + 1);
                }

                object instance = shape.Type.GetConstructors(PublicInstance).Single().Invoke(args);
                foreach ((string name, Type type) in shape.Members.Where(m => Nullable.GetUnderlyingType(m.Type) != null))
                {
                    object? got = shape.Type.GetProperty(name, PublicInstance)!.GetValue(instance);
                    if (got != null)
                    {
                        violations.Add($"{shape.Type.Name}.{name}: null in, {got} out");
                    }
                }
            }

            Assert.True(violations.Count == 0, string.Join("\n", violations));
        }

        [Fact]
        public void test_event_catalogue_flight_plan_published_binds_by_name()
        {
            var flight = new FlightId(11UL);
            var rotation = new FlightId(12UL);
            var e = new FlightPlanPublished(
                flight, MovementKind.Departure, rotation, true, new AirlineId(7U), new ContentId("a320"),
                600UL, 1800UL, Fx.FromRaw(45L << 32));

            Assert.Equal(flight, e.Flight);
            Assert.Equal(MovementKind.Departure, e.Kind);
            Assert.Equal(rotation, e.Rotation);
            Assert.True(e.HasRotation);
            Assert.Equal(new AirlineId(7U), e.Airline);
            Assert.Equal(new ContentId("a320"), e.AircraftType);
            Assert.Equal(600UL, e.SchedArr);
            Assert.Equal(1800UL, e.SchedDep);
            Assert.Equal(45L << 32, e.MinTurnaround.Raw);
        }

        [Fact]
        public void test_event_catalogue_released_hold_carries_null_held_at()
        {
            var open = new DepartureHeldForPassengers(new FlightId(3UL), 17, new NodeId(42U));
            var closed = new DepartureHeldForPassengersReleased(new FlightId(3UL), 0, null);

            Assert.Equal(new NodeId(42U), open.HeldAt);
            Assert.Equal(17, open.Outstanding);
            Assert.Null(closed.HeldAt);
            Assert.Equal(0, closed.Outstanding);
        }

        [Fact]
        public void test_event_catalogue_delay_event_carries_full_delay_node()
        {
            var node = new DelayNode(
                new DelayEventId(9UL), DelayNodeKind.Allocation, new FlightId(5UL), new DelayEventId(1UL),
                DelayCategory.StandUnavailable, 30UL, Fx.FromRaw(3L << 32), true, new FlightId(0UL),
                new EventRef(new EventId(100UL, 2U), true),
                new DelayExplanation(DelaySource.StandUnavailable, 4UL, 5UL),
                120UL);
            var e = new DelayEvent(node);

            Assert.Equal(new DelayEventId(9UL), e.Node.Id);
            Assert.Equal(DelayNodeKind.Allocation, e.Node.Kind);
            Assert.Equal(new FlightId(5UL), e.Node.Subject);
            Assert.Equal(new DelayEventId(1UL), e.Node.Parent);
            Assert.Equal(DelayCategory.StandUnavailable, e.Node.Category);
            Assert.Equal(30UL, e.Node.Ticks);
            Assert.Equal(3L << 32, e.Node.Minutes.Raw);
            Assert.True(e.Node.RootCause);
            Assert.Equal(new FlightId(0UL), e.Node.LinkedFlight);
            Assert.Equal(new EventId(100UL, 2U), e.Node.SourceEvent.Id);
            Assert.True(e.Node.SourceEvent.HasValue);
            Assert.Equal(DelaySource.StandUnavailable, e.Node.Explanation.Source);
            Assert.Equal(4UL, e.Node.Explanation.A);
            Assert.Equal(5UL, e.Node.Explanation.B);
            Assert.Equal(120UL, e.Node.CreatedAt);

            var total = new DelayNode(
                new DelayEventId(1UL), DelayNodeKind.FlightTotal, new FlightId(5UL), default, null, 30UL,
                Fx.FromRaw(3L << 32), false, default, EventRef.None, default, 120UL);
            Assert.Null(total.Category);
        }

        [Fact]
        public void test_event_catalogue_payload_survives_the_bus_unchanged()
        {
            // The bus copies the payload (08 §8.6); a catalogue struct must
            // travel through T-001's transport and arrive equal.
            var sent = new TurnaroundJobBlocked(
                new FlightId(77UL), JobKind.Fuel, ResourceKind.Vehicle, new EntityId(0x0005_0000_0000_0003UL), DelayCategory.Fuel);
            var received = new List<TurnaroundJobBlocked>();
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Services.Events.Subscribe<TurnaroundJobBlocked>(new SystemId(7), (in EventEnvelope env, in TurnaroundJobBlocked evt, in TickContext ctx) => received.Add(evt));
            b.Register(new ProbeSystem(5)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) => ctx.Events.Publish(sent, EventRef.None),
            });
            b.Register(new ProbeSystem(7));
            b.Build().Step(1);

            TurnaroundJobBlocked got = Assert.Single(received);
            Assert.Equal(sent.Flight, got.Flight);
            Assert.Equal(sent.Job, got.Job);
            Assert.Equal(sent.WaitingOn, got.WaitingOn);
            Assert.Equal(sent.Resource, got.Resource);
            Assert.Equal(sent.Category, got.Category);
        }
    }
}
