using System;
using System.Collections.Generic;

namespace StrmAssistant.Provider
{
    public class EpisodeGroupResponse
    {
        public List<EpisodeGroup> groups { get; set; }
        public string id { get; set; }
    }

    public class EpisodeGroup
    {
        public string name { get; set; }
        public int order { get; set; }
        public List<GroupEpisode> episodes { get; set; }
    }

    public class GroupEpisode
    {
        public DateTimeOffset air_date { get; set; }
        public int episode_number { get; set; }
        public int season_number { get; set; }
        public int order { get; set; }
    }
}
