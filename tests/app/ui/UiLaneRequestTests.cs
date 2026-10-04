using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.App.Ui.Tests
{
    /// <summary>
    /// The production ILaneCommandSink (17 §17.5 step 4, Q-010) against a
    /// fake host and a fake flow (17 §17.7): the command it submits (08
    /// §8.7), its payload layout, the non-queue and clamp rules, and the
    /// §17.9 allocation rule for the work it does inside Update.
    /// </summary>
    public sealed class UiLaneRequestTests
    {
        private static (FakeHost Host, FakeFlow Flow, ILaneCommandSink Sink, CallGuard Guard) Make(ulong tick)
        {
            var guard = new CallGuard();
            var host = new FakeHost(guard, tick);
            var flow = new FakeFlow(guard);
            return (host, flow, UiFactory.CreateLaneCommandSink(host, flow), guard);
        }

        [Fact]
        public void test_ui_lane_request_submits_set_servers_open_for_next_tick()
        {
            (FakeHost host, FakeFlow flow, ILaneCommandSink sink, CallGuard guard) = Make(100UL);
            flow.Lanes(5, 4, 2);
            sink.Request(new NodeId(5), 1);

            Assert.True(host.Submits.Count == 1, "one request, one submit: " + host.Show());
            Submitted s = host.Submits[0];
            Assert.Equal(100UL + SimConstants.COMMAND_MIN_LEAD_TICKS, s.Tick);
            Assert.Equal(SimConstants.PLAYER_LOCAL.Value, s.Issuer);
            Assert.Equal(CommandKind.SetServersOpen, s.Kind);
            Assert.True(s.Payload != null && s.Payload.Length == 8, "payload " + s);
            Assert.Equal(5U, s.Node);
            Assert.Equal(3, s.Count);

            (FakeHost host2, FakeFlow flow2, ILaneCommandSink sink2, CallGuard guard2) = Make(7777UL);
            flow2.Lanes(6, 3, 3);
            sink2.Request(new NodeId(6), -1);
            Assert.True(host2.Submits.Count == 1, "one request, one submit: " + host2.Show());
            Submitted d = host2.Submits[0];
            Assert.True(d.Tick == 7778UL && d.Issuer == 0 && d.Kind == CommandKind.SetServersOpen && d.Node == 6U && d.Count == 2, "a −1 request: " + d);

            Assert.Empty(guard.Violations);
            Assert.Empty(guard2.Violations);
        }

        [Fact]
        public void test_ui_lane_request_encodes_payload_per_core_layout()
        {
            // 08 §8.7: NodeId.Value uint32 then count int32, little-endian, no padding, no prefix.
            (FakeHost host, FakeFlow flow, ILaneCommandSink sink, CallGuard guard) = Make(9UL);
            flow.Lanes(0xA1B2C3D4U, 70000, 0x00010201);
            sink.Request(new NodeId(0xA1B2C3D4U), 1);
            Assert.True(host.Submits.Count == 1, host.Show());
            Assert.Equal(new byte[] { 0xD4, 0xC3, 0xB2, 0xA1, 0x02, 0x02, 0x01, 0x00 }, host.Submits[0].Payload);

            (FakeHost host2, FakeFlow flow2, ILaneCommandSink sink2, CallGuard guard2) = Make(9UL);
            flow2.Lanes(0x00000107U, 0x01000000, 0x01000000);
            sink2.Request(new NodeId(0x00000107U), -1);
            Assert.True(host2.Submits.Count == 1, host2.Show());
            Assert.Equal(new byte[] { 0x07, 0x01, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00 }, host2.Submits[0].Payload);

            Assert.Empty(guard.Violations);
            Assert.Empty(guard2.Violations);
        }

        [Fact]
        public void test_ui_lane_request_ignored_for_non_queue_node()
        {
            (FakeHost host, FakeFlow flow, ILaneCommandSink sink, CallGuard guard) = Make(20UL);
            flow.Lanes(5, 4, 2);

            // Node 4 exists in no lane state, and neither does 12345: TryGetLaneState answers false.
            sink.Request(new NodeId(4), 1);
            sink.Request(new NodeId(4), -1);
            sink.Request(new NodeId(12345), 1);
            Assert.True(host.Submits.Count == 0, "a request for a non-Queue node was submitted: " + host.Show());
            Assert.True(flow.LaneCalls >= 3, "the sink did not ask TryGetLaneState about the non-Queue nodes");

            sink.Request(new NodeId(5), 1);
            Assert.True(host.Submits.Count == 1 && host.Submits[0].Node == 5U && host.Submits[0].Count == 3, "the Queue node's request: " + host.Show());
            Assert.Empty(guard.Violations);
        }

        [Fact]
        public void test_ui_lane_request_clamped_and_no_submit_when_unchanged()
        {
            (FakeHost host, FakeFlow flow, ILaneCommandSink sink, CallGuard guard) = Make(300UL);
            flow.Lanes(5, 3, 3).Lanes(6, 3, 0).Lanes(7, 0, 0).Lanes(8, 2, 1);

            // target = clamp(base + delta, 0, ServerCount) == base: nothing.
            sink.Request(new NodeId(5), 1);
            sink.Request(new NodeId(6), -1);
            sink.Request(new NodeId(7), 1);
            sink.Request(new NodeId(7), -1);
            Assert.True(host.Submits.Count == 0, "a request at a bound was submitted: " + host.Show());

            // The other direction moves.
            sink.Request(new NodeId(5), -1);
            sink.Request(new NodeId(6), 1);

            // The clamp applies to the pending target too: 1 → 2 is submitted, 2 → 2 is not.
            sink.Request(new NodeId(8), 1);
            sink.Request(new NodeId(8), 1);

            // Every submit is checked field by field: node, count, tick.
            (uint Node, int Count)[] expected = { (5U, 2), (6U, 1), (8U, 2) };
            Assert.True(host.Submits.Count == expected.Length, "expected " + expected.Length + " submits, got " + host.Show());
            for (int i = 0; i < expected.Length; i++)
            {
                Submitted s = host.Submits[i];
                Assert.True(s.Node == expected[i].Node, "submit " + i + ": node " + s.Node + ", expected " + expected[i].Node + ": " + host.Show());
                Assert.True(s.Count == expected[i].Count, "submit " + i + ": count " + s.Count + ", expected " + expected[i].Count + ": " + host.Show());
                Assert.True(s.Tick == 301UL, "submit " + i + ": tick " + s.Tick + ", expected 301: " + host.Show());
            }

            Assert.Empty(guard.Violations);
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_ui_lane_request_allocates_nothing_after_warm_up()
        {
            // 17 §17.9 (Q-105): Request allocates nothing for a node this sink
            // has handled before. The warm-up hands it every node the metered
            // window uses, the non-lane node 4 included, so each later call is
            // steady state. CountingHost's TrySubmit allocates nothing, and no
            // call in the window throws.
            var guard = new CallGuard();
            var host = new CountingHost(guard, 1000UL);
            var flow = new FakeFlow(guard).Lanes(5, 6, 3).Lanes(6, 4, 0).Lanes(7, 2, 2);
            ILaneCommandSink sink = UiFactory.CreateLaneCommandSink(host, flow);
            NodeId[] nodes = { new NodeId(5), new NodeId(6), new NodeId(7), new NodeId(4) };

            for (int i = 0; i < 64; i++)
            {
                host.Answer = i % 5 == 4 ? CommandRejection.TooLate : CommandRejection.None;
                sink.Request(nodes[i % nodes.Length], i % 3 == 0 ? -1 : 1);
                host.Tick += (ulong)(i % 2);
            }

            long before = host.Count;
            long start = Allocation.Start();
            for (int i = 0; i < 2000; i++)
            {
                host.Answer = i % 5 == 4 ? CommandRejection.TooLate : CommandRejection.None;
                sink.Request(nodes[i % nodes.Length], i % 3 == 0 ? -1 : 1);
                host.Tick += (ulong)(i % 2);
            }

            long allocated = Allocation.Since(start);
            Assert.True(allocated == 0, "2000 lane requests allocated " + allocated + " bytes after warm-up (17 §17.9)");
            Assert.True(host.Count - before > 500, "the metered requests made only " + (host.Count - before) + " submits");
            Assert.True(host.BadShape == 0, host.BadShape + " submits were not SetServersOpen for the next tick with an 8-byte payload");
            Assert.Empty(guard.Violations);
        }
    }
}
