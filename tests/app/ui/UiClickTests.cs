using AirportSim.App.Render;
using Xunit;

namespace AirportSim.App.Ui.Tests
{
    /// <summary>
    /// The click hit test (17 §17.5 step 1) on the 15 §15.12 fixture layout,
    /// through Screen.Identity (world = screen). Fixture boxes: node k at
    /// ((k-1)·100, 200)-((k-1)·100 + 80, 260), 20 units apart.
    /// </summary>
    public sealed class UiClickTests
    {
        private static IUiController Make(out RecordingSink sink)
        {
            sink = new RecordingSink();
            return UiFactory.CreateController(Layouts.Fixture(), sink, Gfx.Of(GraphicsPreset.High));
        }

        [Fact]
        public void test_ui_click_hits_box_inclusive_of_edges()
        {
            FlowNodeBox box = Layouts.Box(Layouts.Fixture(), 2);
            Assert.True(box.MinX == 100 && box.MinY == 200 && box.MaxX == 180 && box.MaxY == 260, "the fixture's box 2 moved");

            IUiController ui = Make(out RecordingSink sink);
            (float X, float Y)[] inside =
            {
                (100f, 200f), (180f, 200f), (100f, 260f), (180f, 260f),   // corners
                (100f, 230f), (180f, 230f), (140f, 200f), (140f, 260f),   // edges
                (140f, 230f),                                             // interior
            };
            foreach ((float x, float y) in inside)
            {
                int before = sink.Requests.Count;
                Screen.Update(ui, In.Click(x, y));
                Assert.True(sink.Requests.Count == before + 1 && sink.Requests[before] == (2U, 1), "click at (" + x + "," + y + ") on box 2's closed rectangle: " + sink.Show());
            }

            (float X, float Y)[] outside = { (99.5f, 230f), (180.5f, 230f), (140f, 199.5f), (140f, 260.5f), (99.5f, 199.5f) };
            foreach ((float x, float y) in outside)
            {
                int before = sink.Requests.Count;
                Screen.Update(ui, In.Click(x, y));
                Assert.True(sink.Requests.Count == before, "click at (" + x + "," + y + "), half a unit outside box 2, requested: " + sink.Show());
            }
        }

        [Fact]
        public void test_ui_click_outside_every_box_requests_nothing()
        {
            IUiController ui = Make(out RecordingSink sink);
            Screen.Update(
                ui,
                In.Click(50f, 100f),
                In.Click(90f, 230f),
                In.RightClick(890f, 230f),
                In.Click(1000f, 230f),
                In.Click(440f, 261f),
                In.RightClick(440f, 199f),
                In.Click(0f, 0f),
                In.Click(1024f, 512f));
            Assert.True(sink.Requests.Count == 0, "clicks outside every box requested " + sink.Show());
            Pace.AssertBoth(false, GameSpeed.X1, ui, "misses leave pacing alone");

            Screen.Update(ui, In.Click(90f, 230f), In.Click(440f, 230f), In.Click(890f, 230f));
            Assert.True(sink.Requests.Count == 1 && sink.Requests[0] == (5U, 1), "only the hit counts: " + sink.Show());
        }

        [Fact]
        public void test_ui_click_while_paused_still_requests()
        {
            // 17 §17.5: "While paused, a click is still submitted."
            IUiController ui = Make(out RecordingSink sink);
            Screen.Update(ui, In.Pause(), In.Click(440f, 230f));
            Screen.Update(ui, In.RightClick(540f, 230f));
            Assert.True(sink.Show() == "[(5,+1) (6,-1)]", "clicks while paused: " + sink.Show());
            Pace.AssertBoth(true, GameSpeed.X1, ui, "still paused");
        }
    }
}
