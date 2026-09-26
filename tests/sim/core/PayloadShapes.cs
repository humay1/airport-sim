using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// The T-026 shapes, transcribed from 10 §10.9 (event structs), 14 §14.3
    /// (DelayNode, DelayExplanation), 08 §8.11 (content definitions) and the
    /// T-026 interface block, mapped to C# by 07 L10: a public readonly
    /// struct, one public get-only property per IDL member, and one public
    /// constructor taking the members in declared order. X? of a struct is
    /// System.Nullable&lt;X&gt; (Q-018).
    /// </summary>
    internal static class PayloadShapes
    {
        internal sealed class Shape
        {
            public Shape(Type type, params (string Name, Type Type)[] members)
            {
                Type = type;
                Members = members;
            }

            public Type Type { get; }

            public (string Name, Type Type)[] Members { get; }
        }

        private static Shape S(Type t, params (string, Type)[] m) => new Shape(t, m);

        /// <summary>Every event struct of 10 §10.9, Phase 0 and Phase 1, in catalogue order.</summary>
        public static readonly Shape[] Events =
        {
            S(typeof(FlightPlanPublished),
                ("Flight", typeof(FlightId)), ("Kind", typeof(MovementKind)), ("Rotation", typeof(FlightId)),
                ("HasRotation", typeof(bool)), ("Airline", typeof(AirlineId)), ("AircraftType", typeof(ContentId)),
                ("SchedArr", typeof(ulong)), ("SchedDep", typeof(ulong)), ("MinTurnaround", typeof(Fx))),
            S(typeof(FlightMilestoneReached),
                ("Flight", typeof(FlightId)), ("Milestone", typeof(FlightMilestone)), ("PlannedTick", typeof(ulong)), ("ActualTick", typeof(ulong))),
            S(typeof(QueueThresholdExceeded),
                ("Node", typeof(NodeId)), ("WaitMinutes", typeof(Fx)), ("ServersOpen", typeof(int)), ("ServerCount", typeof(int))),
            S(typeof(QueueThresholdCleared),
                ("Node", typeof(NodeId)), ("WaitMinutes", typeof(Fx)), ("ServersOpen", typeof(int)), ("ServerCount", typeof(int))),
            S(typeof(FlowBlocked),
                ("Cohort", typeof(CohortId)), ("Held", typeof(NodeId)), ("BlockedBy", typeof(NodeId))),
            S(typeof(FlowUnblocked),
                ("Cohort", typeof(CohortId)), ("Held", typeof(NodeId)), ("BlockedBy", typeof(NodeId))),

            S(typeof(AircraftHeldForRunway),
                ("Flight", typeof(FlightId)), ("Runway", typeof(RunwayId)), ("QueuePosition", typeof(int))),
            S(typeof(AircraftHeldForRunwayReleased),
                ("Flight", typeof(FlightId)), ("Runway", typeof(RunwayId)), ("QueuePosition", typeof(int))),
            S(typeof(AircraftHeldOnTaxiway),
                ("Flight", typeof(FlightId)), ("Edge", typeof(TaxiEdgeId)), ("Blocking", typeof(FlightId?))),
            S(typeof(AircraftHeldOnTaxiwayReleased),
                ("Flight", typeof(FlightId)), ("Edge", typeof(TaxiEdgeId)), ("Blocking", typeof(FlightId?))),
            S(typeof(StandUnavailable),
                ("Flight", typeof(FlightId)), ("Stand", typeof(StandId?)), ("Occupying", typeof(FlightId?))),
            S(typeof(StandAssigned),
                ("Flight", typeof(FlightId)), ("Stand", typeof(StandId?)), ("Occupying", typeof(FlightId?))),
            S(typeof(DepartureHeldForPassengers),
                ("Flight", typeof(FlightId)), ("Outstanding", typeof(int)), ("HeldAt", typeof(NodeId?))),
            S(typeof(DepartureHeldForPassengersReleased),
                ("Flight", typeof(FlightId)), ("Outstanding", typeof(int)), ("HeldAt", typeof(NodeId?))),
            S(typeof(PassengersArrivedAtGate),
                ("Flight", typeof(FlightId)), ("Count", typeof(int))),
            S(typeof(PassengersMissedFlight),
                ("Flight", typeof(FlightId)), ("Count", typeof(int)), ("LastBlockedAt", typeof(NodeId))),
            S(typeof(TurnaroundJobStarted),
                ("Flight", typeof(FlightId)), ("Job", typeof(JobKind)), ("PlannedStart", typeof(ulong))),
            S(typeof(TurnaroundJobCompleted),
                ("Flight", typeof(FlightId)), ("Job", typeof(JobKind)), ("PlannedStart", typeof(ulong))),
            S(typeof(TurnaroundJobBlocked),
                ("Flight", typeof(FlightId)), ("Job", typeof(JobKind)), ("WaitingOn", typeof(ResourceKind)),
                ("Resource", typeof(EntityId?)), ("Category", typeof(DelayCategory))),
            S(typeof(TurnaroundJobUnblocked),
                ("Flight", typeof(FlightId)), ("Job", typeof(JobKind)), ("WaitingOn", typeof(ResourceKind)),
                ("Resource", typeof(EntityId?)), ("Category", typeof(DelayCategory))),
            S(typeof(DelayEvent),
                ("Node", typeof(DelayNode))),
        };

        /// <summary>Non-event payload structs (14 §14.3, 08 §8.11).</summary>
        public static readonly Shape[] Payloads =
        {
            S(typeof(DelayExplanation),
                ("Source", typeof(DelaySource)), ("A", typeof(ulong)), ("B", typeof(ulong))),
            S(typeof(DelayNode),
                ("Id", typeof(DelayEventId)), ("Kind", typeof(DelayNodeKind)), ("Subject", typeof(FlightId)),
                ("Parent", typeof(DelayEventId)), ("Category", typeof(DelayCategory?)), ("Ticks", typeof(ulong)),
                ("Minutes", typeof(Fx)), ("RootCause", typeof(bool)), ("LinkedFlight", typeof(FlightId)),
                ("SourceEvent", typeof(EventRef)), ("Explanation", typeof(DelayExplanation)), ("CreatedAt", typeof(ulong))),
            S(typeof(ShowUpBucket),
                ("MinutesBeforeStd", typeof(uint)), ("SharePermille", typeof(uint))),
        };

        /// <summary>
        /// Content definitions (08 §8.11). Kind comes from IContentDefinition
        /// and is fixed by the type, so it is not a constructor member.
        /// </summary>
        public static readonly (Shape Shape, ContentKind Kind)[] Definitions =
        {
            (S(typeof(SizeCategoryDefinition), ("Id", typeof(ContentId)), ("Ordinal", typeof(int))), ContentKind.SizeCategory),
            (S(typeof(AircraftDefinition), ("Id", typeof(ContentId)), ("SizeCategory", typeof(ContentId))), ContentKind.Aircraft),
            (S(typeof(PaxProfileDefinition),
                ("Id", typeof(ContentId)), ("WalkSpeedMps", typeof(Fx)), ("ShowUpCurve", typeof(IReadOnlyList<ShowUpBucket>))), ContentKind.PaxProfile),
            (S(typeof(QueueProfileDefinition),
                ("Id", typeof(ContentId)), ("ServiceRatePerServerPerMinute", typeof(Fx)), ("CapacityStanding", typeof(int)),
                ("ThresholdWaitMinutes", typeof(Fx)), ("HysteresisMinutes", typeof(Fx)), ("Category", typeof(DelayCategory))), ContentKind.QueueProfile),
        };

        /// <summary>Single-Value id structs of the T-026 block, with their Value type.</summary>
        public static readonly (Type Id, Type Value)[] Ids =
        {
            (typeof(AirlineId), typeof(uint)),
            (typeof(RunwayId), typeof(ushort)),
            (typeof(StandId), typeof(ushort)),
            (typeof(TaxiNodeId), typeof(ushort)),
            (typeof(TaxiEdgeId), typeof(ushort)),
            (typeof(VehicleId), typeof(ushort)),
            (typeof(JobId), typeof(ulong)),
            (typeof(DelayEventId), typeof(ulong)),
            (typeof(NodeId), typeof(uint)),
            (typeof(EdgeId), typeof(uint)),
            (typeof(CohortId), typeof(ulong)),
        };

        /// <summary>Enums of the T-026 block with their members in declared order.</summary>
        public static readonly (Type Enum, string[] Names)[] Enums =
        {
            (typeof(MovementKind), new[] { "Arrival", "Departure" }),
            (typeof(JobKind), new[] { "Deboard", "BaggageUnload", "CabinClean", "Catering", "Fuel", "BaggageLoad", "PushbackPrep", "Boarding" }),
            (typeof(VehicleKind), new[] { "CleaningCrew", "CateringTruck", "FuelTruck", "BaggageTractor", "PushbackTug" }),
            (typeof(JobStatus), new[] { "Blocked", "Active", "Completed" }),
            (typeof(ResourceKind), new[] { "Vehicle", "JobDependency", "Crew" }),
            (typeof(DelayCategory), DelayCategoryNames),
            (typeof(DelaySource), new[] { "FlightTotal", "InboundAircraft", "RunwayHold", "TaxiwayHold", "StandUnavailable", "TurnaroundJobWait", "Unexplained", "PassengerHold" }),
            (typeof(DelayNodeKind), new[] { "FlightTotal", "Allocation" }),
            (typeof(FlightMilestone), FlightMilestoneNames),
            (typeof(ContentKind), new[] { "SizeCategory", "Aircraft", "PaxProfile", "QueueProfile" }),
        };

        /// <summary>06-delay-attribution.md, verbatim IDL names (snake_case), in order.</summary>
        public static string[] DelayCategoryNames => new[]
        {
            "late_inbound", "runway_congestion", "taxi_congestion", "stand_unavailable",
            "ground_handling", "fuel", "catering", "cleaning", "loading", "pushback",
            "crew", "passenger_late", "security_queue", "immigration_queue", "baggage",
            "weather", "deicing", "atc_flow", "incident", "policy_constraint", "propagated",
        };

        /// <summary>10 §10.4, in order.</summary>
        public static string[] FlightMilestoneNames => new[]
        {
            "PlanPublished", "InboundAirborne", "Landed", "OffRunway", "OnStand", "DoorsOpen",
            "DeboardComplete", "ReadyToBoard", "BoardingComplete", "DoorsClosed", "Pushback",
            "TakeoffRoll", "Airborne",
        };

        public static IEnumerable<Shape> AllStructShapes()
        {
            return Events.Concat(Payloads).Concat(Definitions.Select(d => d.Shape));
        }

        public static bool IsReadOnlyStruct(Type t)
        {
            return t.IsValueType
                && t.GetCustomAttributes(false).Any(a => a.GetType().FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        }

        /// <summary>
        /// Builds a value of <paramref name="t"/> that differs for different
        /// seeds, so two members of the same type get distinct values. Structs
        /// are built through their single public constructor, recursively.
        /// </summary>
        public static object MakeValue(Type t, int seed)
        {
            Type? nullableOf = Nullable.GetUnderlyingType(t);
            if (nullableOf != null)
            {
                return MakeValue(nullableOf, seed);
            }

            if (t == typeof(bool))
            {
                return seed % 2 == 1;
            }

            if (t == typeof(ulong))
            {
                return (ulong)(1_000_000_000_000UL + (ulong)seed);
            }

            if (t == typeof(long))
            {
                return -1_000_000_000_000L - seed;
            }

            if (t == typeof(uint))
            {
                return 3_000_000_000U + (uint)seed;
            }

            if (t == typeof(int))
            {
                return -1000 - seed;
            }

            if (t == typeof(ushort))
            {
                return (ushort)(40000 + seed);
            }

            if (t == typeof(string))
            {
                return "id." + seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (t == typeof(Fx))
            {
                return Fx.FromRaw(((long)seed + 1L) * 0x1_2345_6789L);
            }

            if (t.IsEnum)
            {
                int n = Enum.GetValues(t).Length;
                int ordinal = n > 1 ? 1 + seed % (n - 1) : 0;
                return Enum.ToObject(t, ordinal);
            }

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                Type e = t.GetGenericArguments()[0];
                Array arr = Array.CreateInstance(e, 3);
                for (int i = 0; i < 3; i++)
                {
                    arr.SetValue(MakeValue(e, seed * 7 + i + 1), i);
                }

                return arr;
            }

            if (t.IsValueType)
            {
                ConstructorInfo[] ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                if (ctors.Length == 1)
                {
                    ParameterInfo[] ps = ctors[0].GetParameters();
                    var args = new object?[ps.Length];
                    for (int i = 0; i < ps.Length; i++)
                    {
                        args[i] = MakeValue(ps[i].ParameterType, seed * 31 + i + 1);
                    }

                    return ctors[0].Invoke(args);
                }
            }

            throw new InvalidOperationException("no test value generator for " + t.FullName);
        }

        /// <summary>Equality for values read back from a property: sequences by content, the rest by Equals.</summary>
        public static bool SameValue(object? expected, object? actual)
        {
            if (expected is IEnumerable e && actual is IEnumerable a && !(expected is string))
            {
                return e.Cast<object?>().SequenceEqual(a.Cast<object?>());
            }

            return Equals(expected, actual);
        }
    }
}
