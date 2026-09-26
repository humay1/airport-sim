using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// Interface conformance (11 §11.3, §11.4, §11.7, §11.9a under 07 L5/L6/L10)
    /// and the headless day (07 "Testing").
    /// </summary>
    public sealed class ScheduleTests
    {
        [Fact]
        public void test_schedule_system_is_registry_position_2_named_sim_schedule()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            Assert.Equal(SchedConst.ScheduleSystemId, rig.Schedule.Id.Value);
            Assert.Equal("sim.schedule", rig.Schedule.Name);
            Assert.IsAssignableFrom<ISimSystem>(rig.Schedule);
            rig.RunTo(1);
            Assert.Equal(200, rig.Schedule.PublishedFlights().Count);            Assert.Equal(rig.Schedule.ComputeStateHash(), rig.Sink.Recorded[0].SystemHashes[0]);
        }

        [Fact]
        public void test_schedule_interface_has_only_the_spec_queries()
        {
            Type t = typeof(IScheduleSystem);
            Assert.True(t.IsInterface && t.IsPublic);
            Assert.Contains(typeof(ISimSystem), t.GetInterfaces());
            var names = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.Equal(new List<string> { "MovementsBetween", "PendingInjectionCount", "PublishedFlights", "TryGetFlight", "TryGetRotation" }, names);
            Assert.Empty(t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

            Assert.Equal(typeof(bool), t.GetMethod("TryGetFlight")!.ReturnType);
            Assert.Equal(new[] { typeof(FlightId), typeof(FlightRecord).MakeByRefType() }, t.GetMethod("TryGetFlight")!.GetParameters().Select(p => p.ParameterType).ToArray());
            Assert.True(t.GetMethod("TryGetFlight")!.GetParameters()[1].IsOut);
            Assert.Equal(typeof(IReadOnlyList<FlightId>), t.GetMethod("PublishedFlights")!.ReturnType);
            Assert.Equal(typeof(IReadOnlyList<FlightId>), t.GetMethod("MovementsBetween")!.ReturnType);
            Assert.Equal(new[] { typeof(ulong), typeof(ulong), typeof(MovementKind) }, t.GetMethod("MovementsBetween")!.GetParameters().Select(p => p.ParameterType).ToArray());
            Assert.Equal(new[] { typeof(FlightId), typeof(FlightId).MakeByRefType() }, t.GetMethod("TryGetRotation")!.GetParameters().Select(p => p.ParameterType).ToArray());
            Assert.Equal(typeof(int), t.GetMethod("PendingInjectionCount")!.ReturnType);
        }

        [Fact]
        public void test_schedule_flight_record_matches_spec_shape()
        {
            Type t = typeof(FlightRecord);
            Assert.True(t.IsValueType && t.IsPublic);
            Assert.NotNull(t.GetCustomAttribute<IsReadOnlyAttribute>());
            (string Name, Type Type)[] members =
            {
                ("Id", typeof(FlightId)), ("Airline", typeof(AirlineId)), ("AircraftType", typeof(ContentId)),
                ("Kind", typeof(MovementKind)), ("DayIndex", typeof(uint)), ("ScheduledTick", typeof(ulong)),
                ("PublishTick", typeof(ulong)), ("Rotation", typeof(FlightId)), ("HasRotation", typeof(bool)),
                ("MinTurnaround", typeof(Fx)), ("PaxProfile", typeof(ContentId)), ("PaxCount", typeof(int)),
                ("HoldBagPermille", typeof(int)), ("AssistPermille", typeof(int)), ("EntryNode", typeof(NodeId)),
            };
            foreach ((string name, Type type) in members)
            {
                PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                Assert.True(p != null, "missing FlightRecord." + name);
                Assert.Equal(type, p!.PropertyType);
                Assert.Null(p.SetMethod);
            }

            ConstructorInfo[] ctors = t.GetConstructors();
            Assert.Single(ctors);
            Assert.Equal(members.Select(m => m.Type).ToArray(), ctors[0].GetParameters().Select(p => p.ParameterType).ToArray());
        }

        [Fact]
        public void test_schedule_table_loader_and_factory_match_spec_shape()
        {
            Type table = typeof(ScheduleTable);
            Assert.True(table.IsValueType && table.IsPublic);
            Assert.Equal(typeof(IReadOnlyList<FlightTemplate>), table.GetProperty("Rows")!.PropertyType);
            Assert.Equal(typeof(ulong), table.GetProperty("FixtureHash")!.PropertyType);

            MethodInfo load = typeof(IScheduleLoader).GetMethod("Load")!;
            Assert.Equal(typeof(ScheduleTable), load.ReturnType);
            Assert.Equal(new[] { typeof(ReadOnlySpan<byte>), typeof(string) }, load.GetParameters().Select(p => p.ParameterType).ToArray());

            Type f = typeof(ScheduleFactory);
            Assert.True(f.IsAbstract && f.IsSealed, "ScheduleFactory is a static class");
            Assert.Equal(typeof(IScheduleLoader), f.GetMethod("CreateLoader")!.ReturnType);
            MethodInfo create = f.GetMethod("CreateSystem")!;
            Assert.Equal(typeof(IScheduleSystem), create.ReturnType);
            ParameterInfo[] ps = create.GetParameters();
            Assert.Equal(new[] { typeof(SystemServices).MakeByRefType(), typeof(ScheduleTable).MakeByRefType(), typeof(IFlowSystem) }, ps.Select(p => p.ParameterType).ToArray());
            Assert.True(ps[0].IsIn && ps[1].IsIn);

            foreach (Type pub in new[] { typeof(FlightRecord), typeof(ScheduleTable), typeof(FlightTemplate), typeof(IScheduleLoader), typeof(IScheduleSystem), typeof(ScheduleFactory) })
            {
                Assert.Equal("AirportSim.Sim.Schedule", pub.Namespace);
            }
        }

        [Fact]
        public void test_schedule_headless_day_meets_invariants()
        {
            var rig = new HostRig(Fixture.Bytes(), withFlow: true);
            var oracle = new ScheduleOracle(Fixture.Text(), 2);
            var lastPending = new Dictionary<ulong, int>();
            for (ulong t = 0; t < SchedConst.TicksPerDay; t += 60UL)
            {
                rig.RunTo(t + 60UL);
                foreach (FlightId id in rig.Schedule.PublishedFlights())
                {
                    int pending = rig.Schedule.PendingInjectionCount(id);
                    FlightRecord r = rig.Flight(id.Value);
                    Assert.InRange(pending, 0, r.PaxCount);
                    if (lastPending.TryGetValue(id.Value, out int prev))
                    {
                        Assert.True(pending <= prev, "pending grew for flight " + id.Value);
                    }

                    lastPending[id.Value] = pending;
                }
            }

            Assert.Equal(SchedConst.TicksPerDay, rig.Host.CurrentTick);
            Assert.Equal(24, rig.Sink.Recorded.Count);
            Assert.Equal(400, rig.Schedule.PublishedFlights().Count);
            Assert.Equal(800, rig.Events!.Events.Count);

            long day0Pax = 0;
            foreach (OracleFlight f in oracle.Flights)
            {
                if (f.Day == 0)
                {
                    day0Pax += f.Row.Pax;
                    Assert.Equal(0, rig.Schedule.PendingInjectionCount(new FlightId(f.Id)));
                }
            }

            long day0Injected = 0;
            foreach (Injection inj in rig.Flow!.Injections)
            {
                day0Injected += inj.Key.Flight.Value <= 200UL ? inj.Count : 0;
                Assert.True(inj.Tick < SchedConst.TicksPerDay);
            }

            Assert.True(day0Pax > 0);
            Assert.Equal(day0Pax, day0Injected);
            Assert.Equal(0, rig.Flow.BadCalls);
        }

        [Fact]
        public void test_schedule_headless_day_without_flow_drains_all_day_zero_demand()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(SchedConst.TicksPerDay);
            for (ulong id = 1; id <= 200; id++)
            {
                Assert.Equal(0, rig.Schedule.PendingInjectionCount(new FlightId(id)));
            }

            int pendingDay1 = 0;
            for (ulong id = 1; id <= 200; id++)
            {
                pendingDay1 += rig.Schedule.PendingInjectionCount(new FlightId(SchedConst.DayStride + id));
            }

            Assert.True(pendingDay1 > 0, "day-1 demand is queued once day-1 flights publish");
        }
    }
}
