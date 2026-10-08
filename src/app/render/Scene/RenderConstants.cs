namespace AirportSim.App.Render
{
    /// <summary>Presentation constants of the scene layer. Spec: 15-interfaces-render.md §15.2 (Q-099).</summary>
    public static class RenderConstants
    {
        /// <summary>View height, in world units, at or below which agents are drawn (§15.7). LOW CONFIDENCE.</summary>
        public const int AGENT_ZOOM_THRESHOLD = 120;

        /// <summary>Most agent dots drawn for one flow node box (§15.5).</summary>
        public const int MAX_DRAWN_AGENTS_PER_NODE = 256;

        /// <summary>Most lane pips drawn for one queue node (§15.5, Q-010).</summary>
        public const int MAX_DRAWN_LANES_PER_NODE = 32;

        /// <summary>Most ticks the pacer asks for in one frame (§15.8).</summary>
        public const uint MAX_CATCHUP_TICKS_PER_FRAME = 3;

        /// <summary>Real microseconds in one tick at 1x, TICK_MS × 1000 (§15.8).</summary>
        public const long REAL_MICROSECONDS_PER_TICK_1X = 100000;

        /// <summary>Ticks before STA that an approaching arrival appears (§15.19, Q-132). LOW CONFIDENCE.</summary>
        public const int APPROACH_TICKS = 15;

        /// <summary>Distance from touchdown, on the extended centreline, where an approach starts (§15.19).</summary>
        public const int APPROACH_ENTRY_M = 8000;

        /// <summary>Distance from touchdown of the final approach fix (§15.19).</summary>
        public const int FINAL_FIX_M = 2000;

        /// <summary>The side of the square holding pattern (§15.19).</summary>
        public const int HOLD_LEG_M = 1000;

        /// <summary>Ticks to fly one side of the hold (§15.19).</summary>
        public const int HOLD_LEG_TICKS = 3;

        /// <summary>How far past lift-off a departure climbs before it is removed (§15.19).</summary>
        public const int CLIMB_OUT_M = 4000;

        /// <summary>Approach elevation is distance to touchdown over this (§15.19).</summary>
        public const int GLIDE_RATIO = 20;

        /// <summary>Climb elevation is distance past lift-off over this (§15.19).</summary>
        public const int CLIMB_RATIO = 10;

        /// <summary>Ticks one walker takes to cross a jet bridge (§15.19).</summary>
        public const int BRIDGE_WALK_TICKS = 3;

        /// <summary>Most walkers per jet bridge (§15.19).</summary>
        public const int MAX_BRIDGE_WALKERS = 8;
    }
}
