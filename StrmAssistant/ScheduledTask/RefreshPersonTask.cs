using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
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

            var serverPreferredMetadataLanguage = Plugin.MetadataApi.GetServerPreferredMetadataLanguage();
            _logger.Info("Server Preferred Metadata Language: " + serverPreferredMetadataLanguage);
            var isServerPreferZh = string.Equals(serverPreferredMetadataLanguage.Split('-')[0], "zh",
                StringComparison.OrdinalIgnoreCase);
            if (!isServerPreferZh)
            {
                progress.Report(100.0);
                _logger.Warn("Server Preferred Metadata Language is not set to Chinese.");
                _logger.Warn("RefreshPerson - Task Aborted");
                return;
            }

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
                        IncludeItemTypes = new[]
                            { nameof(Movie), nameof(Series), nameof(Episode), nameof(Video), nameof(Trailer) }
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

            var enhanceMovieDbPerson = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.EnhanceMovieDbPerson;

            if (remainingCount > 0)
            {
                if (enhanceMovieDbPerson) ChineseMovieDb.PatchCacheTime();
                IsRunning = true;
            }

            var refreshPersonMode = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.RefreshPersonMode;
            _logger.Info("Refresh Person Mode: " + refreshPersonMode);

            for (var startIndex = 0; startIndex < remainingCount; startIndex += batchSize)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info("RefreshPerson - Task Cancelled");
                    break;
                }

                personQuery.Limit = batchSize;
                personQuery.StartIndex = startIndex;
                personItems = _libraryManager.GetItemList(personQuery).Cast<Person>().ToList();
                
                if (personItems.Count == 0) break;

                foreach (var item in personItems)
                {
                    var taskItem = item;

                    var nameRefreshSkip = refreshPersonMode == RefreshPersonMode.Default && isServerPreferZh &&
                                          IsChinese(taskItem.Name) && IsChinese(taskItem.Overview) &&
                                          taskItem.DateLastSaved >= DateTimeOffset.UtcNow.AddDays(-30);
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

                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.Info("RefreshPerson - Task Cancelled");
                        break;
                    }

                    try
                    {
                        await QueueManager.SemaphoreLocal.WaitAsync(cancellationToken);
                    }
                    catch
                    {
                        break;
                    }

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                _logger.Info("RefreshPerson - Task Cancelled");
                                return;
                            }

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
                            _logger.Error("RefreshPerson - Item Failed: " + taskItem.Name);
                            _logger.Error(e.Message);
                            _logger.Debug(e.StackTrace);
                        }
                        finally
                        {
                            QueueManager.SemaphoreLocal.Release();

                            var currentCount = Interlocked.Increment(ref current);
                            progress.Report(currentCount / total * 100);
                            _logger.Info("RefreshPerson - Task " + currentCount + "/" + total + " - " + taskItem.Name);
                        }
                    }, cancellationToken);

                    tasks.Add(task);
                    Task.Delay(10).Wait();
                }
                await Task.WhenAll(tasks);
                tasks.Clear();
                personItems.Clear();
            }

            if (remainingCount > 0)
            {
                if (enhanceMovieDbPerson) ChineseMovieDb.UnpatchCacheTime();
                IsRunning = false;
            }

            progress.Report(100.0);
            _logger.Info("RefreshPerson - Task Complete");
        }

        public string Category => Plugin.Instance.Name;

        public string Key => "RefreshPersonTask";

        public string Description => "Refreshes and repairs Chinese actors";

        public string Name => "Refresh Chinese Actor";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }

        public static bool IsRunning { get; private set; }
    }
}
