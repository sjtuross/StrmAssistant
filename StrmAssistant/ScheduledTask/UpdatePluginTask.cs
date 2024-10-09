using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant
{
    public class UpdatePluginTask : IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly IApplicationHost _applicationHost;
        private readonly IApplicationPaths _applicationPaths;
        private readonly IHttpClient _httpClient;
        private readonly IActivityManager _activityManager;
        private readonly IServerApplicationHost _serverApplicationHost;

        public UpdatePluginTask(IApplicationHost applicationHost, IApplicationPaths applicationPaths,
            IHttpClient httpClient, IActivityManager activityManager, IServerApplicationHost serverApplicationHost)
        {
            _logger = Plugin.Instance.logger;
            _applicationHost = applicationHost;
            _applicationPaths = applicationPaths;
            _httpClient = httpClient;
            _activityManager = activityManager;
            _serverApplicationHost = serverApplicationHost;
        }

        private static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        private static string PluginAssemblyFilename => Assembly.GetExecutingAssembly().GetName().Name + ".dll";
        private static string RepoReleaseUrl => "https://api.github.com/repos/sjtuross/StrmAssistant/releases/latest";

        public string Key => "UpdatePluginTask";

        public string Name => "Update Plugin";

        public string Description => $"Updates {Plugin.Instance.Name} plugin to the latest version";

        public string Category => Plugin.Instance.Name;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerDaily,
                TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
            };
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            await Task.Yield();
            progress.Report(0);

            try
            {
                var stream = await _httpClient.Get(new HttpRequestOptions
                    {
                        Url = RepoReleaseUrl,
                        CancellationToken = cancellationToken,
                        AcceptHeader = "application/json",
                        EnableDefaultUserAgent = true
                    })
                    .ConfigureAwait(false);

                var apiResult = await JsonSerializer.DeserializeAsync<ApiResponseInfo>(stream, null, cancellationToken);
                
                var currentVersion = ParseVersion(CurrentVersion);
                var remoteVersion = ParseVersion(apiResult?.TagName);

                if (currentVersion.CompareTo(remoteVersion) < 0)
                {
                    _logger.Info("Found new plugin version: {0}", remoteVersion);

                    var url = (apiResult?.Assets ?? Array.Empty<ApiAssetInfo>())
                        .FirstOrDefault(asset => asset.Name == PluginAssemblyFilename)
                        ?.BrowserDownloadUrl;
                    if (!Uri.IsWellFormedUriString(url, UriKind.Absolute)) throw new Exception("Invalid download url");

                    using (var responseStream = await _httpClient.Get(new HttpRequestOptions
                               {
                                   Url = url,
                                   CancellationToken = cancellationToken,
                                   EnableDefaultUserAgent = true,
                                   Progress = progress
                               })
                               .ConfigureAwait(false))
                    {
                        var dllFilePath = Path.Combine(_applicationPaths.PluginsPath, PluginAssemblyFilename);
                        using (var fileStream = new FileStream(dllFilePath, FileMode.Create, FileAccess.Write))
                        {
                            await responseStream.CopyToAsync(fileStream, 81920, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }

                    _logger.Info("Plugin update complete");

                    _activityManager.Create(new ActivityLogEntry
                    {
                        Name = Plugin.Instance.Name + " Updated to " + remoteVersion + " on " +
                               _serverApplicationHost.FriendlyName,
                        Type = "PluginUpdateInstalled",
                        Severity = LogSeverity.Info
                    });

                    _applicationHost.NotifyPendingRestart();
                }
                else
                {
                    _logger.Info("No need to update");
                }
            }
            catch (Exception e)
            {
                _activityManager.Create(new ActivityLogEntry
                {
                    Name = Plugin.Instance.Name + " update failed on " + _serverApplicationHost.FriendlyName,
                    Type = "PluginUpdateFailed",
                    Overview = e.Message,
                    Severity = LogSeverity.Error
                });

                _logger.Error("Update error: {0}", e.Message);
                _logger.Debug(e.StackTrace);
            }

            progress.Report(100);
        }

        private static Version ParseVersion(string v)
        {
            return new Version(v.StartsWith("v") ? v.Substring(1) : v);
        }

        private class ApiResponseInfo
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; }

            [JsonPropertyName("assets")]
            public ApiAssetInfo[] Assets { get; set; }
        }

        private class ApiAssetInfo
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; }
        }
    }
}
