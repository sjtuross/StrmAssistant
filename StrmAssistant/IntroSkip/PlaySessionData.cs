using System;

namespace StrmAssistant
{
    public class PlaySessionData
    {
        public long PlaybackStartTicks { get; set; } = 0;
        public long PreviousPositionTicks { get; set; } = 0;
        public DateTime PreviousEventTime { get; set; } = DateTime.MinValue;
        public long? FirstJumpPositionTicks { get; set; } = null;
        public long? LastJumpPositionTicks { get; set; } = null;
        public long MaxIntroDurationTicks { get; set; } =
            Plugin.Instance.GetPluginOptions().IntroSkipOptions.MaxIntroDurationSeconds * TimeSpan.TicksPerSecond;
        public long MaxCreditsDurationTicks { get; set; } =
            Plugin.Instance.GetPluginOptions().IntroSkipOptions.MaxCreditsDurationSeconds * TimeSpan.TicksPerSecond;
        public long MinOpeningPlotDurationTicks { get; set; } =
            Plugin.Instance.GetPluginOptions().IntroSkipOptions.MinOpeningPlotDurationSeconds * TimeSpan.TicksPerSecond;
        public DateTime? LastPauseEventTime { get; set; } = null;
        public DateTime? LastPlaybackRateChangeEventTime { get; set; } = null;
    }
}
