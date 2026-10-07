namespace AirportSim.App.Ui.UnityBackend
{
    /// <summary>The settings panel's localisation keys, exactly spec 17 section 17.4b's table.</summary>
    internal static class UiTextKeys
    {
        public static readonly LocalisedKey SettingsTitle = new LocalisedKey("ui.settings.title");
        public static readonly LocalisedKey PresetLow = new LocalisedKey("ui.settings.preset.low");
        public static readonly LocalisedKey PresetMedium = new LocalisedKey("ui.settings.preset.medium");
        public static readonly LocalisedKey PresetHigh = new LocalisedKey("ui.settings.preset.high");
        public static readonly LocalisedKey DrawAgents = new LocalisedKey("ui.settings.knob.draw_agents");
        public static readonly LocalisedKey MaxDrawnAgentsPerNode = new LocalisedKey("ui.settings.knob.max_drawn_agents_per_node");
        public static readonly LocalisedKey FrameRateCap = new LocalisedKey("ui.settings.knob.frame_rate_cap");
        public static readonly LocalisedKey ResolutionScalePercent = new LocalisedKey("ui.settings.knob.resolution_scale_percent");
        public static readonly LocalisedKey AntiAliasing = new LocalisedKey("ui.settings.knob.anti_aliasing");
        public static readonly LocalisedKey ValueOn = new LocalisedKey("ui.settings.value.on");
        public static readonly LocalisedKey ValueOff = new LocalisedKey("ui.settings.value.off");
        public static readonly LocalisedKey ValueUncapped = new LocalisedKey("ui.settings.value.uncapped");
    }
}
