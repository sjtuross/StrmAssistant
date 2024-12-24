using Emby.Media.Common.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StrmAssistant.Options
{
    public static class Utility
    {
        private static HashSet<string> _selectedExclusiveFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> _selectedCatchupTasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void UpdateExclusiveControlFeatures(string currentScope)
        {
            _selectedExclusiveFeatures = new HashSet<string>(
                currentScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(f => !(f == MediaInfoExtractOptions.ExclusiveControl.CatchAllAllow.ToString() &&
                                  currentScope.Contains(MediaInfoExtractOptions.ExclusiveControl.CatchAllBlock.ToString()))) ??
                Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsExclusiveFeatureSelected(params MediaInfoExtractOptions.ExclusiveControl[] featuresToCheck)
        {
            return featuresToCheck.Any(f => _selectedExclusiveFeatures.Contains(f.ToString()));
        }

        public static string GetSelectedExclusiveFeatureDescription()
        {
            return string.Join(", ",
                _selectedExclusiveFeatures.Select(feature =>
                    Enum.TryParse(feature.Trim(), true, out GeneralOptions.CatchupTask type) ? type.GetDescription() : null));
        }

        public static void UpdateCatchupScope(string currentScope)
        {
            _selectedCatchupTasks = new HashSet<string>(
                currentScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ??
                Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsCatchupTaskSelected(params GeneralOptions.CatchupTask[] tasksToCheck)
        {
            return tasksToCheck.Any(f => _selectedCatchupTasks.Contains(f.ToString()));
        }

        public static string GetSelectedCatchupTaskDescription()
        {
            return string.Join(", ",
                _selectedCatchupTasks.Select(task =>
                    Enum.TryParse(task.Trim(), true, out GeneralOptions.CatchupTask type) ? type.GetDescription() : null));
        }
    }
}
