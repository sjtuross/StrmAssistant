using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

            var personItems = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Person" }
            });
            _logger.Info("RefreshPerson - Number of Persons Before: " + personItems.Length);

            var checkKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tmdb", "imdb", "tvdb" };

            var dupPersonItems = personItems.Where(item => item.ProviderIds != null)
                .SelectMany(item => item.ProviderIds
                    .Where(kvp => checkKeys.Contains(kvp.Key))
                    .Select(kvp => new { kvp.Key, kvp.Value, item }))
                .GroupBy(kvp => new { kvp.Key, kvp.Value })
                .Where(group => group.Count() > 1)
                .SelectMany(group => group.Select(g => g.item))
                .ToArray();

            if (dupPersonItems.Length > 0)
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

            _logger.Info("RefreshPerson - Number of Duplicate Persons Deleted: " + dupPersonItems.Length);

            var dupItemIds = new HashSet<long>(dupPersonItems.Select(d => d.InternalId));
            var remainingPersonItems = personItems.Where(i => !dupItemIds.Contains(i.InternalId))
                .OrderByDescending(i => i.DateCreated).ToList();
            _logger.Info("RefreshPerson - Number of Persons After: " + remainingPersonItems.Count);

            double total = remainingPersonItems.Count;
            var current = 0;
            var tasks = new List<Task>();

            foreach (var item in remainingPersonItems)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info("RefreshPerson - Task Cancelled");
                    break;
                }

                await QueueManager.SemaphoreMaster.WaitAsync(cancellationToken);

                var taskItem = item;
                var task = Task.Run(async () =>
                {
                    try
                    {
                        var result = await Plugin.MetadataApi
                            .GetPersonMetadataFromMovieDb(taskItem as Person, cancellationToken)
                            .ConfigureAwait(false);

                        if (result?.Item != null)
                        {
                            var newName = result.Item.Name;
                            if (!string.IsNullOrEmpty(newName))
                            {
                                var updateResult = Plugin.MetadataApi.UpdateAsNeeded(taskItem, newName);
                                if (updateResult.Item2) taskItem.Name = taskItem.SortName = updateResult.Item1;
                            }

                            var newOverview = result.Item.Overview;
                            if (!string.IsNullOrEmpty(newOverview))
                            {
                                var updateResult = Plugin.MetadataApi.UpdateAsNeeded(taskItem, newOverview);
                                if (updateResult.Item2) taskItem.Overview = updateResult.Item1;
                            }

                            _libraryManager.UpdateItem(taskItem, null, ItemUpdateType.MetadataEdit);
                        }

                        if (!taskItem.HasImage(ImageType.Primary))
                        {
                            await taskItem.RefreshMetadata(MetadataApi.PersonRefreshOptions, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.Info("RefreshPerson - Task Cancelled: " + taskItem.Name);
                    }
                    catch (Exception e)
                    {
                        _logger.Info("RefreshPerson - Task Failed: " + taskItem.Name);
                        _logger.Debug(e.Message);
                        _logger.Debug(e.StackTrace);
                    }
                    finally
                    {
                        Interlocked.Increment(ref current);
                        progress.Report(current / total * 100);
                        _logger.Info("RefreshPerson - Task " + current + "/" + total + " - " + taskItem.Name);
                        QueueManager.SemaphoreMaster.Release();
                    }
                }, cancellationToken);
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);

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
