namespace StrmAssistant.Mod
{
    public enum PatchApproach
    {
        None = 0,
        Reflection = 1,
        Harmony = 2,
    }

    public class PatchApproachTracker
    {
        public PatchApproach FallbackPatchApproach { get; set; } = PatchApproach.Harmony;
    }
}
