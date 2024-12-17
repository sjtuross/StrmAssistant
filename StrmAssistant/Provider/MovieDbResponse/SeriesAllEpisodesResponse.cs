using System;
using System.Collections.Generic;

namespace StrmAssistant.Provider
{
    internal class SeriesResponseInfo
    {
        public List<SeasonResponseInfo> seasons { get; set; }
    }

    internal class SeasonResponseInfo
    {
        public int id { get; set; }

        public int season_number { get; set; }

        public List<EpisodeResponseInfo> episodes { get; set; }
    }

    internal class EpisodeResponseInfo
    {
        public DateTimeOffset air_date { get; set; }

        public int episode_number { get; set; }

        public string name { get; set; }

        public string overview { get; set; }

        public int id { get; set; }

        public int season_number { get; set; }
    }
}
