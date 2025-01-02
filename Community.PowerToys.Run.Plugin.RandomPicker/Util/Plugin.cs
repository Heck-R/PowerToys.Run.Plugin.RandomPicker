
using System;
using System.Collections.Generic;
using System.Linq;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.RandomPicker.Util {

    public class PluginUtil {
        /// <summary>
        /// Changes the provided result into a simple navigation menu that auto completes on enter
        /// Overwrites the Action, but still calls it first (though its return value will be lost to keep the navigation menu functionality)
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        public static List<Result> NavigationMenu(PluginInitContext context, Query query, string[] path, List<Result> results) {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(results);

            // The queried path must be the same length as the provided one to make the result a submenu
            // +1 is a filter in the submenus
            if (query.Terms.Count > path.Length + 1) {
                return [];
            }

            // The base path must be the same as provided
            if (!path.SequenceEqual(query.Terms.Take(path.Length))) {
                return [];
            }

            // If the user started to type, then filter the options
            var filteredResults = path.Length < query.Terms.Count ?
                results.Where(result => result.QueryTextDisplay.Contains(query.Terms.Last())) :
                results;

            return filteredResults.Select(result => {
                var originalAction = result.Action;
                result.Action = actionContext => {
                    // Call original action
                    originalAction?.Invoke(actionContext);

                    // Change query on enter
                    context.API.ChangeQuery($"{string.Join(" ", path)} {result.QueryTextDisplay} ", true);
                    return false;
                };

                return result;
            }).ToList();
        }

        /// <summary>
        /// Assigns a score to the Results in a way that makes them appear in a fixed order
        /// This is done by adding a significant score difference, which should not be overtaken by normal automatic usage result tuning
        /// </summary>
        /// <param name="results"></param>
        /// <returns></returns>
        public static IEnumerable<Result> FixPositionAsScore(IEnumerable<Result> results) {
            for (int index = 0; index < results.Count(); index++) {
                results.ElementAt(index).Score = (results.Count() - index) * 10000;
                results.ElementAt(index).SelectedCount = 0;
            }
            return results;
        }
    }
}
