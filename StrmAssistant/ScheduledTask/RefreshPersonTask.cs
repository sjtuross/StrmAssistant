using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Mod;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.LanguageUtility;

namespace StrmAssistant
{
    public class RefreshPersonTask: IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;

        public RefreshPersonTask(ILibraryManager libraryManager)
        {
            _logger = Plugin.Instance.logger;
            _libraryManager = libraryManager;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("RefreshPerson - Task Execute");
            await Task.Yield();
            progress.Report(0);

            var personQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Person" }
            };
            var personItems = _libraryManager.GetItemList(personQuery).Cast<Person>().ToList();
            _logger.Info("RefreshPerson - Number of Persons Before: " + personItems.Count);

            var checkKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tmdb", "imdb", "tvdb" };

            var dupPersonItems = personItems.Where(item => item.ProviderIds != null)
                .SelectMany(item => item.ProviderIds
                    .Where(kvp => checkKeys.Contains(kvp.Key))
                    .Select(kvp => new { kvp.Key, kvp.Value, item }))
                .GroupBy(kvp => new { kvp.Key, kvp.Value })
                .Where(group => group.Count() > 1)
                .SelectMany(group => group.Select(g => g.item))
                .ToList();

            if (dupPersonItems.Count > 0)
            {
                foreach (var dupItem in dupPersonItems)
                {
                    _logger.Info($"RefreshPerson - Duplicate Person: {dupItem.Name}");
                    var relatedItems = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        PersonIds = new[] { dupItem.InternalId },
                        Recursive = true,
                        IncludeItemTypes = new[] { "Movie", "Series", "Episode", "Video", "Trailer" }
                    });
                    foreach (var relatedItem in relatedItems)
                    {
                        _logger.Info(
                            $"RefreshPerson - Deleting duplicate person {dupItem.Name} related to {relatedItem.Path}");
                    }
                }
                _libraryManager.DeleteItems(dupPersonItems.Select(i => i.InternalId).ToArray());
            }
            _logger.Info("RefreshPerson - Number of Duplicate Persons Deleted: " + dupPersonItems.Count);

            var skipCount = personItems
                .Count(item => item.ProviderIds != null &&
                               !item.ProviderIds.Keys.Any(key =>
                                   string.Equals(key, "tmdb", StringComparison.OrdinalIgnoreCase)));
            _logger.Info("RefreshPerson - Number of Persons without TmdbId Skipped: " + skipCount);

            var remainingCount = personItems.Count - dupPersonItems.Count - skipCount;
            _logger.Info("RefreshPerson - Number of Persons After: " + remainingCount);

            personItems.Clear();
            personItems.TrimExcess();

            personQuery.HasAnyProviderId = new[] { "tmdb" };

            double total = remainingCount;
            var current = 0;
            const int batchSize = 100;
            var tasks = new List<Task>();

            var chineseMovieDb = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.ChineseMovieDb;
            if (chineseMovieDb) ChineseMovieDb.PatchCacheTime();

            var refreshPersonMode = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.RefreshPersonMode;
            _logger.Info("Refresh Person Mode: " + refreshPersonMode);
            var serverPreferredMetadataLanguage = Plugin.MetadataApi.GetServerPreferredMetadataLanguage();
            _logger.Info("Server Preferred Metadata Language: " + serverPreferredMetadataLanguage);
            var isServerPreferZh = string.Equals(serverPreferredMetadataLanguage.Split('-')[0], "zh",
                StringComparison.OrdinalIgnoreCase);

            for (var startIndex = 0; startIndex < remainingCount; startIndex += batchSize)
            {
                personQuery.Limit = batchSize;
                personQuery.StartIndex = startIndex;
                personItems = _libraryManager.GetItemList(personQuery).Cast<Person>().ToList();
                
                if (personItems.Count == 0) break;

                foreach (var item in personItems)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.Info("RefreshPerson - Task Cancelled");
                        break;
                    }

                    var taskItem = item;

                    var nameRefreshSkip = refreshPersonMode == RefreshPersonMode.Default && isServerPreferZh &&
                                          IsChinese(taskItem.Name) && taskItem.DateLastSaved >=
                                          DateTimeOffset.UtcNow.AddDays(-30);
                    var imageRefreshSkip = taskItem.HasImage(ImageType.Primary) ||
                                           (refreshPersonMode == RefreshPersonMode.Default &&
                                            taskItem.DateLastRefreshed >=
                                            DateTimeOffset.UtcNow.AddDays(-30));

                    if (nameRefreshSkip && imageRefreshSkip)
                    {
                        var currentCount = Interlocked.Increment(ref current);
                        progress.Report(currentCount / total * 100);
                        _logger.Info("RefreshPerson - Task " + currentCount + "/" + total + " Skipped - " +
                                     taskItem.Name);
                        continue;
                    }

                    await QueueManager.SemaphoreMaster.WaitAsync(cancellationToken);

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            if (!nameRefreshSkip)
                            {
                                var result = await Plugin.MetadataApi
                                    .GetPersonMetadataFromMovieDb(taskItem, serverPreferredMetadataLanguage,
                                        cancellationToken)
                                    .ConfigureAwait(false);

                                if (result?.Item != null)
                                {
                                    var newName = result.Item.Name;
                                    if (!string.IsNullOrEmpty(newName))
                                    {
                                        taskItem.Name = Plugin.MetadataApi.ProcessPersonInfo(newName, true);
                                    }

                                    var newOverview = result.Item.Overview;
                                    if (!string.IsNullOrEmpty(newOverview))
                                    {
                                        taskItem.Overview = Plugin.MetadataApi.ProcessPersonInfo(newOverview, false);
                                    }

                                    _libraryManager.UpdateItem(taskItem, null, ItemUpdateType.MetadataEdit);
                                }
                            }

                            if (!imageRefreshSkip)
                            {
                                await taskItem.RefreshMetadata(MetadataApi.PersonRefreshOptions, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            _logger.Info("RefreshPerson - Item Cancelled: " + taskItem.Name);
                        }
                        catch (Exception e)
                        {
                            _logger.Info("RefreshPerson - Item Failed: " + taskItem.Name);
                            _logger.Debug(e.Message);
                            _logger.Debug(e.StackTrace);
                        }
                        finally
                        {
                            var currentCount = Interlocked.Increment(ref current);
                            progress.Report(currentCount / total * 100);
                            _logger.Info("RefreshPerson - Task " + currentCount + "/" + total + " - " + taskItem.Name);
                            QueueManager.SemaphoreMaster.Release();
                        }
                    }, cancellationToken);

                    tasks.Add(task);
                    Task.Delay(10).Wait();
                }
                await Task.WhenAll(tasks);
                tasks.Clear();
                personItems.Clear();
            }

            if (chineseMovieDb) ChineseMovieDb.UnpatchCacheTime();

            progress.Report(100.0);
            _logger.Info("RefreshPerson - Task Complete");
        }

        public string Category => Plugin.Instance.Name;

        public string Key => "RefreshPersonTask";

        public string Description => "Refreshes and repairs persons";

        public string Name => "Refresh Persons";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }
    }
}
