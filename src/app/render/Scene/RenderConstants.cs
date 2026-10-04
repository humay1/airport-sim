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
    }
}
