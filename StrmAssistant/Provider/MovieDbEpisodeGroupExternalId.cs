using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace StrmAssistant.Provider
{
    public class MovieDbEpisodeGroupExternalId : IExternalId
    {
        public string Name => "MovieDb Episode Group";

        public string Key => StaticName;

        public string UrlFormatString => null;

        public bool Supports(IHasProviderIds item) => item is Series;

        public static string StaticName => "TmdbEg";
    }
}
