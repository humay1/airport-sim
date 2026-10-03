using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>12 §12.9: FreeStands() is in ascending StandId, whatever order the layout declares.</summary>
    public sealed class FreeStandsTests
    {
        [Fact]
        public void test_free_stands_ascending_stand_id_regardless_of_declaration_order()
        {
            var b = new LayoutBuilder()
                .Runway(1, 1, 15, 10)
                .Node(1, TaxiNodeKind.RunwayThreshold)
                .Node(2, TaxiNodeKind.Junction);
            foreach (ushort s in new ushort[] { 3, 1, 4, 2 })
            {
                ushort node = (ushort)(10 + s);
                b.Node(node, TaxiNodeKind.StandPosition)
                 .Edge((ushort)(20 + s), 2, node, 10)
                 .Stand(s, node, AirsideContent.Super, 900U + s);
            }

            b.Edge(1, 1, 2, 30);
            var rig = new HostRig(Csv.Of(Csv.Row("X1", "A", "12:00")), layout: b.Load());

            Assert.Equal(new List<ushort> { 1, 2, 3, 4 }, rig.Free());
            for (ushort s = 1; s <= 4; s++)
            {
                Assert.True(rig.Airside.TryGetStand(new StandId(s), out StandState st));
                Assert.Equal(s, st.Id.Value);
                Assert.False(st.Occupant.HasValue);
            }
        }
    }
}
