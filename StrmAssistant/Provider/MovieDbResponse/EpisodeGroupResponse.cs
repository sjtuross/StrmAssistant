using System.Collections.Generic;

namespace StrmAssistant.Provider
{
    internal class EpisodeGroupResponse
    {
        public List<EpisodeGroup> groups { get; set; }
        public string id { get; set; }
    }

    internal class EpisodeGroup
    {
        public string name { get; set; }
        public int order { get; set; }
        public List<GroupEpisode> episodes { get; set; }
    }

    internal class GroupEpisode
    {
        public int episode_number { get; set; }
        public int season_number { get; set; }
        public int order { get; set; }
    }
}
