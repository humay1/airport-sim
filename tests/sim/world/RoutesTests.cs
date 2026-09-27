using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.World.Tests
{
    /// <summary>
    /// 18 §18.2/§18.3: the routes do not depend on edge-list or file order.
    /// </summary>
    public sealed class RoutesTests
    {
        [Fact]
        public void test_routes_independent_of_file_order()
        {
            // 18 §18.2/§18.3: arrays and keys may come in any order, and nothing
            // downstream sees file order. Each scrambled file must load to the
            // same ascending lists and answer every query identically, while
            // FixtureHash follows the exact bytes.
            const ulong seed = 0x0F11_E0DEUL;
            var rng = new SplitMix64(seed);
            for (int iteration = 0; iteration < 60; iteration++)
            {
                WalkGraph reference = WorldKit.RandomGraph(rng, 7, 3, 40);
                string plain = GraphJson.Write(reference);
                string scrambled = GraphJson.Write(reference, rng);
                string where = "seed 0x" + seed.ToString("X") + ", iteration " + iteration;

                WalkGraph a = WorldKit.Load(plain);
                WalkGraph b = WorldKit.Load(scrambled);
                Assert.True(WorldKit.Content(reference) == WorldKit.Content(a), where + ": plain file");
                Assert.True(WorldKit.Content(reference) == WorldKit.Content(b), where + ": scrambled file " + scrambled);
                Assert.True(a.FixtureHash == WorldKit.Fnv1a64(WorldKit.Utf8(plain)), where);
                Assert.True(b.FixtureHash == WorldKit.Fnv1a64(WorldKit.Utf8(scrambled)), where);

                string expected = WorldSystemTests.Snapshot(WorldKit.Create(reference));
                Assert.True(expected == WorldSystemTests.Snapshot(WorldKit.Create(a)), where + ": routes from plain file");
                Assert.True(expected == WorldSystemTests.Snapshot(WorldKit.Create(b)), where + ": routes from scrambled file");
            }
        }
    }
}
