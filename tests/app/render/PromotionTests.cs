using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>15 §15.7: the promotion controller, the one caller of SetPromoted.</summary>
    public sealed class PromotionTests
    {
        private static string Show(IEnumerable<(uint Node, bool Promoted)> calls)
        {
            return "[" + string.Join(", ", calls.Select(c => "(" + c.Node + "," + (c.Promoted ? "T" : "F") + ")")) + "]";
        }

        private static void AssertCalls(SmallScene s, string what, params (uint Node, bool Promoted)[] expected)
        {
            List<(uint Node, bool Promoted)> got = s.Flow!.SetPromotedCalls;
            Assert.True(got.SequenceEqual(expected), what + ": SetPromoted calls " + Show(got) + ", expected " + Show(expected));
            got.Clear();
        }

        /// <summary>A small scene whose flow also knows node 9, which the layout has no box for.</summary>
        private static SmallScene Scene(bool reversed = false)
        {
            var s = new SmallScene(reversedLayoutLists: reversed);
            s.Flow!.Node(9);
            return s;
        }

        [Fact]
        public void test_promotion_first_update_sets_every_layout_node()
        {
            // The layout lists its boxes 6, 5, 3: the calls are still ascending NodeId.
            var s = Scene(reversed: true);
            IPromotionController c = s.Controller();
            c.Update(Cam.On(SmallScene.Queue5Box, 60f), Gfx.High());
            AssertCalls(s, "first Update, queue 5 in view", (3, false), (5, true), (6, false));

            // Nothing in view: the first Update still names every layout node.
            var t = Scene();
            t.Controller().Update(Cam.Away(), Gfx.High());
            AssertCalls(t, "first Update, nothing in view", (3, false), (5, false), (6, false));

            // Above the threshold, with every box in view.
            var u = Scene();
            u.Controller().Update(SmallScene.Overview, Gfx.High());
            AssertCalls(u, "first Update, overview", (3, false), (5, false), (6, false));
        }

        [Fact]
        public void test_promotion_calls_only_on_change_in_ascending_node_id()
        {
            var s = Scene();
            IPromotionController c = s.Controller();
            c.Update(SmallScene.Overview, Gfx.High());
            s.Flow!.SetPromotedCalls.Clear();

            c.Update(SmallScene.Overview, Gfx.High());
            AssertCalls(s, "unchanged view");

            // View [170,530] × [170,290]: queues 5 and 6, not the hall.
            CameraView queues = Cam.At(350f, 230f, 120f, 3f);
            c.Update(queues, Gfx.High());
            AssertCalls(s, "zoom onto both queues", (5, true), (6, true));
            c.Update(queues, Gfx.High());
            AssertCalls(s, "same view again");

            // View [20,80] × [190,250]: the hall only. Promotions and demotions interleave in NodeId order.
            CameraView hall = Cam.On(SmallScene.HallBox, 60f);
            c.Update(hall, Gfx.High());
            AssertCalls(s, "pan to the hall", (3, true), (5, false), (6, false));

            c.Update(Cam.Away(), Gfx.High());
            AssertCalls(s, "pan away", (3, false));
            c.Update(Cam.Away(30f), Gfx.High());
            AssertCalls(s, "zoom while nothing is in view");

            Assert.False(s.Flow.IsPromoted(9), "node 9 has no box and is never promoted");
            Assert.Empty(s.Guard.Violations);
        }

        [Fact]
        public void test_promotion_zoom_threshold_is_inclusive()
        {
            var s = Scene();
            IPromotionController c = s.Controller();

            // Exactly AGENT_ZOOM_THRESHOLD promotes, from the first Update on.
            c.Update(Cam.On(SmallScene.Queue5Box, RenderConst.AgentZoomThreshold), Gfx.High());
            AssertCalls(s, "first Update at ViewHeight 120", (3, false), (5, true), (6, false));

            c.Update(Cam.On(SmallScene.Queue5Box, 121f), Gfx.High());
            AssertCalls(s, "ViewHeight 121", (5, false));

            c.Update(Cam.On(SmallScene.Queue5Box, 120f), Gfx.High());
            AssertCalls(s, "back to ViewHeight 120", (5, true));

            c.Update(Cam.On(SmallScene.Queue5Box, 119f), Gfx.High());
            AssertCalls(s, "ViewHeight 119");
        }

        [Fact]
        public void test_promotion_box_touching_view_edge_is_visible()
        {
            var s = Scene();
            IPromotionController c = s.Controller();

            // View [140,200] × [200,260]: its right edge touches queue 5's MinX = 200.
            c.Update(Cam.At(170f, 230f, 60f), Gfx.High());
            AssertCalls(s, "right edge touches queue 5", (3, false), (5, true), (6, false));

            // View [139,199]: one unit short.
            c.Update(Cam.At(169f, 230f, 60f), Gfx.High());
            AssertCalls(s, "one unit short of queue 5", (5, false));

            // View [200,260] × [260,320]: its bottom-left corner touches queue 5's max-Y edge.
            c.Update(Cam.At(230f, 290f, 60f), Gfx.High());
            AssertCalls(s, "bottom edge touches queue 5's top", (5, true));
        }

        [Fact]
        public void test_promotion_draw_agents_off_demotes_every_promoted_node()
        {
            var s = Scene();
            s.Flow!.SetPopulation(SmallScene.Queue5, 20);
            s.Flow.SetPopulation(SmallScene.Queue6, 20);
            IPromotionController c = s.Controller();
            ISceneBuilder b = s.Builder();
            CameraView queues = Cam.At(350f, 230f, 100f, 3.6f);

            c.Update(queues, Gfx.High());
            AssertCalls(s, "both queues in view", (3, false), (5, true), (6, true));
            Assert.Equal(40, Prims.InLayer(Prims.Copy(b.Build(queues, Gfx.High())), DrawLayer.Agent).Count);

            GraphicsSettings off = Gfx.Custom(false, 256);
            c.Update(queues, off);
            AssertCalls(s, "DrawAgents switched off", (5, false), (6, false));
            Assert.Empty(Prims.InLayer(Prims.Copy(b.Build(queues, off)), DrawLayer.Agent));

            c.Update(queues, off);
            AssertCalls(s, "still off");
            c.Update(Cam.On(SmallScene.HallBox, 60f), off);
            AssertCalls(s, "a pan with DrawAgents off promotes nothing");

            c.Update(queues, Gfx.High());
            AssertCalls(s, "DrawAgents back on", (5, true), (6, true));

            // The Low preset turns agents off (15 §15.14).
            c.Update(queues, RenderFactory.GraphicsForPreset(GraphicsPreset.Low));
            AssertCalls(s, "Low preset", (5, false), (6, false));
            Assert.Empty(s.Guard.Violations);
        }
    }
}
