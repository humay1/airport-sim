using System.Collections.Generic;
using AirportSim.App.Render;
using Xunit;

namespace AirportSim.App.Ui.Tests
{
    /// <summary>
    /// Pause and speed (17 §17.4): ignored while the settings panel is open
    /// (§17.4a, Q-034), and outcome-neutral through 16 §16.6's frame order
    /// (§17.10's integration test).
    /// </summary>
    public sealed class UiPauseAndSpeedTests
    {
        private static readonly GameSpeed[] Speeds = { GameSpeed.X1, GameSpeed.X2, GameSpeed.X4 };

        [Fact]
        public void test_ui_pause_and_speed_ignored_while_settings_open()
        {
            IUiController ui = UiFactory.CreateController(Layouts.Fixture(), new RecordingSink(), Gfx.Of(GraphicsPreset.Medium));

            // Unpaused at X2, open, try to pause and change speed, close.
            Screen.Update(ui, In.Speed(GameSpeed.X2));
            Screen.Update(ui, In.Settings());
            Screen.Update(ui, In.Pause());
            Pace.AssertBoth(true, GameSpeed.X2, ui, "TogglePause while open (the panel still pauses)");
            Screen.Update(ui, In.Speed(GameSpeed.X4), In.Pause(), In.Pause(), In.Pause());
            Pace.AssertBoth(true, GameSpeed.X2, ui, "SetSpeed and TogglePause while open");
            Screen.Update(ui, In.Settings());
            Pace.AssertBoth(false, GameSpeed.X2, ui, "closed: the player's own pause and speed are unchanged");

            // Paused at X2, open, try to unpause and change speed, close.
            Screen.Update(ui, In.Pause());
            Screen.Update(ui, In.Settings(), In.Pause(), In.Speed(GameSpeed.X1));
            Pace.AssertBoth(true, GameSpeed.X2, ui, "TogglePause and SetSpeed after opening in the same frame");
            Screen.Update(ui, In.Settings());
            Pace.AssertBoth(true, GameSpeed.X2, ui, "closed: still the player's own pause");
            Screen.Update(ui, In.Pause(), In.Speed(GameSpeed.X4));
            Pace.AssertBoth(false, GameSpeed.X4, ui, "the controls work again after closing");
        }

        [Fact]
        public void test_ui_pause_and_speed_do_not_change_outcome()
        {
            const ulong seed = 0x9A5E_0029UL;
            const int maxFrames = 200000;
            var loop = new FrameLoop(Gfx.Of(GraphicsPreset.Medium));
            var model = new UiModel(loop.Layout, Gfx.Of(GraphicsPreset.Medium));
            var rng = new SplitMix64(seed);
            var speedFrames = new Dictionary<GameSpeed, int> { { GameSpeed.X1, 0 }, { GameSpeed.X2, 0 }, { GameSpeed.X4, 0 } };
            int multiInputFrames = 0;
            int pausedFrames = 0;

            while (loop.Sim.Host.CurrentTick < UiConst.TicksPerDay)
            {
                Assert.True(loop.Frames < maxFrames, "seed " + seed + ": no sim-day after " + maxFrames + " frames");

                // Scripted pause and speed inputs, sometimes several in one frame; no clicks.
                var inputs = new List<UiInput>();
                if (model.PlayerPaused ? rng.Range(0, 11) == 0 : rng.Range(0, 199) == 0)
                {
                    inputs.Add(In.Pause());
                }

                if (rng.Range(0, 59) == 0)
                {
                    inputs.Add(In.Speed(Speeds[rng.Range(0, Speeds.Length - 1)]));
                }

                if (rng.Range(0, 399) == 0)
                {
                    inputs.Add(In.Pause());
                    inputs.Add(In.Speed(Speeds[rng.Range(0, Speeds.Length - 1)]));
                    inputs.Add(In.Pause());
                }

                multiInputFrames += inputs.Count > 1 ? 1 : 0;
                model.Update(inputs, Screen.Identity, Screen.W, Screen.H);
                (uint _, UiFrame frame) = loop.Run(inputs, Screen.Identity, rng.Range(4000, 250000));

                Pace.AssertIs(model.Paused, model.Speed, frame.Pacing, "seed " + seed + ", frame " + (loop.Frames - 1));
                speedFrames[model.Speed]++;
                pausedFrames += model.Paused ? 1 : 0;
            }

            foreach (KeyValuePair<GameSpeed, int> kv in speedFrames)
            {
                Assert.True(kv.Value > 0, "the day never ran at " + kv.Key);
            }

            Assert.True(pausedFrames > 0, "the day was never paused");
            Assert.True(loop.PausedFrames == pausedFrames, "the frame loop saw " + loop.PausedFrames + " paused frames, the script " + pausedFrames);
            Assert.True(multiInputFrames > 0, "no frame carried several inputs");
            Assert.True(loop.UiHost.Submits == 0, "a run with no clicks submitted " + loop.UiHost.Submits + " commands");
            loop.AssertNeutral("pause and speed");
        }
    }
}
