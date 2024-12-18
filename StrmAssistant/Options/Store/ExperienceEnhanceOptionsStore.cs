using Emby.Web.GenericEdit.PropertyDiff;
using MediaBrowser.Common;
using MediaBrowser.Model.Logging;
using StrmAssistant.Mod;
using StrmAssistant.Options.UIBaseClasses.Store;
using System.Collections.Generic;
using System.Linq;

namespace StrmAssistant.Options.Store
{
    public class ExperienceEnhanceOptionsStore : SimpleFileStore<ExperienceEnhanceOptions>
    {
        private readonly ILogger _logger;

        public ExperienceEnhanceOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
            : base(applicationHost, logger, pluginFullName)
        {
            _logger = logger;

            FileSaving += OnFileSaving;
            FileSaved += OnFileSaved;
        }
        
        public ExperienceEnhanceOptions ExperienceEnhanceOptions => GetOptions();

        private void OnFileSaving(object sender, FileSavingEventArgs e)
        {
            if (e.Options is ExperienceEnhanceOptions options)
            {
                var changes = PropertyChangeDetector.DetectObjectPropertyChanges(ExperienceEnhanceOptions, options);
                var changedProperties = new HashSet<string>(changes.Select(c => c.PropertyName));
                
                if (changedProperties.Contains(nameof(ExperienceEnhanceOptions.MergeMultiVersion)))
                {
                    if (options.MergeMultiVersion)
                    {
                        MergeMultiVersion.Patch();
                    }
                    else
                    {
                        MergeMultiVersion.Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(ExperienceEnhanceOptions.HidePersonNoImage)))
                {
                    if (options.HidePersonNoImage)
                    {
                        HidePersonNoImage.Patch();
                    }
                    else
                    {
                        HidePersonNoImage.Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(ExperienceEnhanceOptions.EnforceLibraryOrder)))
                {
                    if (options.EnforceLibraryOrder)
                    {
                        EnforceLibraryOrder.Patch();
                    }
                    else
                    {
                        EnforceLibraryOrder.Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(ExperienceEnhanceOptions.BeautifyMissingMetadata)))
                {
                    if (options.BeautifyMissingMetadata)
                    {
                        BeautifyMissingMetadata.Patch();
                    }
                    else
                    {
                        BeautifyMissingMetadata.Unpatch();
                    }
                }

                if (changedProperties.Contains(nameof(ExperienceEnhanceOptions.EnhanceMissingEpisodes)))
                {
                    if (options.EnhanceMissingEpisodes)
                    {
                        EnhanceMissingEpisodes.Patch();
                    }
                    else
                    {
                        EnhanceMissingEpisodes.Unpatch();
                    }
                }
            }
        }

        private void OnFileSaved(object sender, FileSavedEventArgs e)
        {
            if (e.Options is ExperienceEnhanceOptions options)
            {
                _logger.Info("MergeMultiVersion is set to {0}", options.MergeMultiVersion);
                _logger.Info("HidePersonNoImage is set to {0}", options.HidePersonNoImage);
                _logger.Info("EnforceLibraryOrder is set to {0}", options.EnforceLibraryOrder);
                _logger.Info("BeautifyMissingMetadata is set to {0}", options.BeautifyMissingMetadata);
                _logger.Info("EnhanceMissingEpisodes is set to {0}", options.EnhanceMissingEpisodes);
            }
        }
    }
}
