using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>Interface conformance for the promotion surface (09 §9.7 under 07 L10).</summary>
    public sealed class PromotionShapeTests
    {
        private static void AssertStruct(Type t, params (string Name, Type Type)[] members)
        {
            Assert.True(t.IsValueType && t.IsPublic, t.Name + " must be a public struct");
            Assert.Equal("AirportSim.Sim.Flow", t.Namespace);
            Assert.NotNull(t.GetCustomAttribute<IsReadOnlyAttribute>());
            foreach ((string name, Type type) in members)
            {
                PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                Assert.True(p != null, "missing " + t.Name + "." + name);
                Assert.Equal(type, p!.PropertyType);
                Assert.Null(p.SetMethod);
            }

            ConstructorInfo[] ctors = t.GetConstructors();
            Assert.Single(ctors);
            Assert.Equal(members.Select(m => m.Type).ToArray(), ctors[0].GetParameters().Select(p => p.ParameterType).ToArray());
        }

        [Fact]
        public void test_promotion_shape_matches_spec_and_views_are_live()
        {
            AssertStruct(typeof(PassengerRef), ("Cohort", typeof(CohortId)), ("Index", typeof(int)));
            AssertStruct(typeof(AgentView), ("Ref", typeof(PassengerRef)), ("Node", typeof(NodeId)), ("ProgressAlongEdge", typeof(Fx)));

            MethodInfo set = typeof(IFlowSystem).GetMethod("SetPromoted")!;
            Assert.Equal(typeof(void), set.ReturnType);
            Assert.Equal(new[] { typeof(NodeId), typeof(bool) }, set.GetParameters().Select(p => p.ParameterType).ToArray());
            MethodInfo agents = typeof(IFlowSystem).GetMethod("AgentsAt")!;
            Assert.Equal(typeof(IReadOnlyList<AgentView>), agents.ReturnType);
            Assert.Equal(new[] { typeof(NodeId) }, agents.GetParameters().Select(p => p.ParameterType).ToArray());

            // Behavioural half, so the test cannot pass on a shape-only stub.
            var rig = new PromoRig(PromoPlan.Standard(), record: false);
            rig.Host.Step(6000);
            rig.SetAll(true);
            int shown = 0;
            foreach (uint n in PromoConst.AllNodes)
            {
                shown += rig.Flow.AgentsAt(new NodeId(n)).Count;
            }

            Assert.Equal(rig.TotalPopulation(), shown);
            Assert.True(shown > 0);
        }
    }
}
