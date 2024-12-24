using Emby.Media.Common.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using static StrmAssistant.GeneralOptions;

namespace StrmAssistant.Options
{
    public static class Utility
    {
        private static HashSet<string> _selectedCatchupTasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void UpdateCatchupScope()
        {
            var catchupTaskScope = Plugin.Instance.GetPluginOptions().GeneralOptions.CatchupTaskScope;

            _selectedCatchupTasks = new HashSet<string>(
                catchupTaskScope?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ??
                Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsCatchupTaskSelected(params CatchupTask[] tasksToCheck)
        {
            return tasksToCheck.Any(f => _selectedCatchupTasks.Contains(f.ToString()));
        }

        public static string GetSelectedCatchupTaskDescription()
        {
            return string.Join(", ",
                _selectedCatchupTasks.Select(task =>
                    Enum.TryParse(task.Trim(), true, out CatchupTask type) ? type.GetDescription() : null));
        }
    }
}
