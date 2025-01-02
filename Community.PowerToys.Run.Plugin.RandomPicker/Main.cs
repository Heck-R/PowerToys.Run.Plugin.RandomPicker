using Community.PowerToys.Run.Plugin.RandomPicker.Util;
using ManagedCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Wox.Infrastructure.Storage;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.RandomPicker {

    static class Menu {
        public static string PICK = "pick";
        public static string FAVORITES = "favorites";
        public static string HISTORY = "history";
    }

    class Storage {
        /// <summary>
        /// Favorites can be added from the history
        /// By default new ones are added to the end
        /// The order can be adjusted
        /// </summary>
        public List<string> Favorites { get; set; } = [];

        /// <summary>
        /// The latest item is the first
        /// The history has no repetition, as re-picking would cause a spamming of the history
        /// => the older occurrence of the item is removed, and only the latest is kept
        /// </summary>
        public LinkedList<string> History { get; set; } = [];
    }

    class RandomDefinitionContextData {
        public string RandomDefinition { get; set; }
    }

    class HistoryContextData {
        public string RandomDefinition { get; set; }
    }

    class FavoriteContextData {
        public string RandomDefinition { get; set; }
        public string RawQuery { get; set; }
    }

    /// <summary>
    /// Main class of this plugin that implement all used interfaces.
    /// </summary>
    public class Main : IPlugin, IContextMenu, IDisposable, ISavable {

        /// <summary>
        /// Saved Plugin context for utils
        /// </summary>
        private PluginInitContext Context { get; set; }

        /// <summary>
        /// Path to the icon of the right theme
        /// </summary>
        private readonly IconLoader IconLoader = new();

        /// <summary>
        /// API for saving and loading the Storage variable
        /// </summary>
        private readonly PluginJsonStorage<Storage> StorageApi = new();

        /// <summary>
        /// Storage that is persisted through sessions using the StorageApi
        /// </summary>
        private Storage Storage = new();

        /// <summary>
        /// Flag for on-demand random generation
        /// It is meant to be true when initiating the random picking, and be true for only that one manual query
        /// </summary>
        private bool GenerateInNextQuery = false;

        /// <summary>
        /// A simple counter for the amount of generations initiated
        /// Used as a visual feedback for the user in case the new random result is the same as the one before
        /// </summary>
        private int GenerationCounter = 0;


        #region IDisposable

        private bool Disposed { get; set; }

        /// <summary>
        /// Initialize the plugin with the given <see cref="PluginInitContext"/>.
        /// </summary>
        /// <param name="context">The <see cref="PluginInitContext"/> for this plugin.</param>
        public void Init(PluginInitContext context) {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Context.API.ThemeChanged += OnThemeChanged;
            UpdateTheme(Context.API.GetCurrentTheme());

            this.Storage = this.StorageApi.Load();
        }

        /// <inheritdoc/>
        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Wrapper method for <see cref="Dispose()"/> that dispose additional objects and events form the plugin itself.
        /// </summary>
        /// <param name="disposing">Indicate that the plugin is disposed.</param>
        protected virtual void Dispose(bool disposing) {
            if (Disposed || !disposing) {
                return;
            }

            if (Context?.API != null) {
                Context.API.ThemeChanged -= OnThemeChanged;
            }

            Disposed = true;
        }

        private void UpdateTheme(Theme theme) => this.IconLoader.ThemeName = theme == Theme.Light || theme == Theme.HighContrastWhite ? "light" : "dark";

        private void OnThemeChanged(Theme currentTheme, Theme newTheme) => UpdateTheme(newTheme);

        #endregion

        #region ISavable

        /// <summary>
        /// Saves any unsaved storage state
        /// </summary>
        public void Save() {
            this.StorageApi.Save();
        }

        #endregion

        #region IContextMenu

        /// <summary>
        /// Return a list context menu entries for a given <see cref="Result"/> (shown at the right side of the result).
        /// </summary>
        /// <param name="selectedResult">The <see cref="Result"/> for the list with context menu entries.</param>
        /// <returns>A list context menu entries.</returns>
        public List<ContextMenuResult> LoadContextMenus(Result selectedResult) {
            if (selectedResult.ContextData is HistoryContextData historyContext) {
                return
                [
                    new ContextMenuResult {
                        PluginName = this.Name,
                        Title = "Add to Favorites (Ctrl+F)",
                        Glyph = "\u2605", // Star
                        FontFamily = "Consolas, \"Courier New\", monospace",
                        AcceleratorModifiers = ModifierKeys.Control,
                        AcceleratorKey = Key.F,
                        Action = _ =>
                        {
                            if (this.Storage.Favorites.Contains(historyContext.RandomDefinition)) {
                                // Skip adding already favorite items
                                return false;
                            }

                            this.Storage.Favorites.Add(historyContext.RandomDefinition);

                            // Saving here is done to avoid loss when the process is killed
                            this.StorageApi.Save();
                            return false;
                        },
                    }
                ];
            }
            if (selectedResult.ContextData is FavoriteContextData favoriteContext) {
                return
                [
                    new ContextMenuResult {
                        PluginName = this.Name,
                        Title = "Delete from Favorites (Ctrl+D)",
                        Glyph = "\U0001F5D1", // Trash
                        FontFamily = "Consolas, \"Courier New\", monospace",
                        AcceleratorModifiers = ModifierKeys.Control,
                        AcceleratorKey = Key.D,
                        Action = _ =>
                        {
                            this.Storage.Favorites.Remove(favoriteContext.RandomDefinition);
                            
                            // Saving here is done to avoid loss when the process is killed
                            this.StorageApi.Save();

                            this.Context.API.ChangeQuery(favoriteContext.RawQuery, true);
                            return false;
                        },
                    },
                ];
            }

            // The context data is not of a handled type
            return [];
        }

        #endregion

        #region IPlugin

        /// <summary>
        /// ID of the plugin.
        /// This joke is not documented, but is required for the plugin to work
        /// </summary>
        public static string PluginID => "58698CC74ADD4BADA331759CB1D7168A";

        /// <summary>
        /// Name of the plugin.
        /// </summary>
        public string Name => "RandomPicker";

        /// <summary>
        /// Description of the plugin.
        /// </summary>
        public string Description => "Pick randomly from a predefined list, optionally using weights";

        /// <summary>
        /// Main Plugin interface for generating items on the UI
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public List<Result> Query(Query query) {
            try {
                if (query.Terms.ElementAtOrDefault(0) == Menu.PICK) {
                    return this.Pick(query);
                }

                if (query.Terms.ElementAtOrDefault(0) == Menu.HISTORY) {
                    return this.History(query);
                }

                if (query.Terms.ElementAtOrDefault(0) == Menu.FAVORITES) {
                    return this.Favorites(query);
                }

                var navigationMenus = PluginUtil.NavigationMenu(this.Context, query, [], [
                    new Result {
                        QueryTextDisplay = Menu.PICK,
                        IcoPath = this.IconLoader.MAIN,
                        Title = Menu.PICK,
                        SubTitle = "Provide a random definition",
                    },
                    new Result {
                        QueryTextDisplay = Menu.FAVORITES,
                        IcoPath = this.IconLoader.MAIN,
                        Title = Menu.FAVORITES,
                        SubTitle = "Select a saved random definition",
                    },
                    new Result {
                        QueryTextDisplay = Menu.HISTORY,
                        IcoPath = this.IconLoader.MAIN,
                        Title = Menu.HISTORY,
                        SubTitle = "Select a previously used random definition",
                    },
                ]);

                return navigationMenus.Count > 0 ? navigationMenus : [
                    new Result {
                        QueryTextDisplay = "",
                        IcoPath = this.IconLoader.WARNING,
                        Title = "No result",
                        SubTitle = "",
                        Action = actionContext => false,
                    }
                ];
            } catch (Exception ex) {
                return [
                    new Result {
                        QueryTextDisplay = "",
                        IcoPath = this.IconLoader.WARNING,
                        Title = "Error",
                        SubTitle = ex.Message,
                        Action = actionContext => false,
                    }
                ];
            }
        }

        /// <summary>
        /// Generates results for the item picking menu
        /// User input format: <RandomDefinition>[ <ResultCount>[ <MaxRepCount>]]
        /// Where <RandomDefinition> is: <item>[:weight][;<item>[:weight][...]]
        /// 
        /// Example user input (excluding parent menus): item1;item2:2;item3:8 2 2
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        private List<Result> Pick(Query query) {
            var generateInNextQuery = this.GenerateInNextQuery;
            this.GenerateInNextQuery = false;

            string definitionInput = string.Join(" ", query.Terms.ToArray(), 1, query.Terms.Count - 1);

            var match = Regex.Match(definitionInput, $@"^(?<RandomDefinition>.*?\S)(\s(?<ResultCount>\d+))?(\s(?<MaxRepCount>\d+))?$");
            if (!match.Success) {
                return [
                    new Result {
                    QueryTextDisplay = "",
                    IcoPath = this.IconLoader.WARNING,
                    Title = $"Bad format: The given input does not match the required format",
                    SubTitle = "Right format: <RandomDefinition>[ <ResultCount>[ <MaxRepCount>]]\nWhere <RandomDefinition> is: <item>[:weight][;<item>[:weight][...]]",
                    Action = actionContext => false,
                }
                ];
            }
            string randomDefinition = match.Groups["RandomDefinition"].Value;

            if (generateInNextQuery) {
                // Only increase the generation counter when actual generation takes place
                this.GenerationCounter++;
            }

            List<Result> results = [
                new Result {
                    QueryTextDisplay = "",
                    IcoPath = this.IconLoader.MAIN,
                    Title = "Generate Random...",
                    SubTitle = $"Generation counter: {this.GenerationCounter}",
                    Action = actionContext => {
                        this.AddItemToHistory(randomDefinition);

                        this.Context.API.ChangeQuery(query.RawQuery, true);
                        this.GenerateInNextQuery = true;
                        return false;
                    },
                    ContextData = new RandomDefinitionContextData {
                        RandomDefinition = match.Groups["RandomDefinition"].Value,
                    }
                }
            ];

            if (!generateInNextQuery) {
                // Skip picking, on random definition user input
                return results;
            }

            long resultCount = match.Groups["ResultCount"].Success ? long.Parse(match.Groups["ResultCount"].Value) : 1;
            long maxRepCount = match.Groups["MaxRepCount"].Success ? long.Parse(match.Groups["MaxRepCount"].Value) : -1;

            List<string> randomItems = [];

            var items = RandomParser.Parse(randomDefinition);
            // Repeat items if max count defined to remove later enforcing the max count
            for (long count = 1; count < maxRepCount; count++) {
                items.AddRange(items);
            }

            for (var i = 0; i < resultCount; i++) {
                if (items.Count == 0) {
                    // When max rep count is set, the items may run out after a certain number of removal
                    break;
                }

                var selectedIndex = RandomSelector.Select(items);
                randomItems.Add(items[selectedIndex].Value);

                if (maxRepCount > 0) {
                    items.RemoveAt(selectedIndex);
                }
            }

            results.AddRange(randomItems.Select(randomItem => new Result {
                QueryTextDisplay = "",
                IcoPath = this.IconLoader.MAIN,
                Title = randomItem,
                Action = actionContext => {
                    this.Context.API.ChangeQuery(query.RawQuery, true);
                    Clipboard.SetDataObject(randomItem);
                    return false;
                }
            }));

            /// For debugging - to check the order being honored
            // results.Add(new Result {
            //     QueryTextDisplay = "",
            //     IcoPath = this.IconLoader.WARNING,
            //     Title = string.Join("#", results.Select(result => result.Title)),
            //     SubTitle = "",
            //     Action = actionContext => {
            //         this.Context.API.ChangeQuery(query.RawQuery, true);
            //         Clipboard.SetDataObject("randomItem");
            //         return false;
            //     },
            // });

            return PluginUtil.FixPositionAsScore(results).ToList();
        }

        private void AddItemToHistory(string randomDefinition) {
            if (this.Storage.History.First?.Value == randomDefinition) {
                // There is no point in removing and re-adding the first element
                return;
            }

            this.Storage.History.Remove(randomDefinition);
            this.Storage.History.AddFirst(randomDefinition);
            while (this.Storage.History.Count > 100) {
                // There should never be more than 1 item excess in normal usage, but this implicitly fixes the size on a max size change
                this.Storage.History.RemoveLast();
            }

            // Saving here is done to avoid loss when the process is killed
            this.StorageApi.Save();
        }

        private List<Result> Favorites(Query query) {
            var favoriteResults = this.Storage.Favorites.Select(randomDefinition => new Result {
                QueryTextDisplay = query.Search,
                IcoPath = this.IconLoader.MAIN,
                Title = randomDefinition,
                Action = actionContext => {
                    this.Context.API.ChangeQuery($"{query.ActionKeyword} {Menu.PICK} {randomDefinition}", true);
                    return false;
                },
                ContextData = new FavoriteContextData {
                    RandomDefinition = randomDefinition,
                    RawQuery = query.RawQuery,
                },
            }).ToList();

            var searchMatch = Regex.Match(query.RawUserQuery, $@"^.*?{Menu.FAVORITES}\s?(?<Search>.*)$");
            if (searchMatch.Groups["Search"].Success) {
                favoriteResults = favoriteResults.Where(result =>
                    // If a result does not have the right type of Context, then it's not searchable
                    result.ContextData is FavoriteContextData historyContext &&
                    historyContext.RandomDefinition.Contains(searchMatch.Groups["Search"].Value)
                ).ToList();
            }

            return favoriteResults.Count > 0 ? PluginUtil.FixPositionAsScore(favoriteResults).ToList() : [
                new Result {
                    QueryTextDisplay = query.Search,
                    IcoPath = this.IconLoader.WARNING,
                    Title = "No favorite result",
                }
            ];
        }

        private List<Result> History(Query query) {
            int index = 0;

            var historyResults = this.Storage.History.Select(randomDefinition => new Result {
                QueryTextDisplay = query.Search,
                IcoPath = this.IconLoader.MAIN,
                Title = $"{index++}: {randomDefinition}",
                Action = actionContext => {
                    this.Context.API.ChangeQuery($"{query.ActionKeyword} {Menu.PICK} {randomDefinition}", true);
                    return false;
                },
                ContextData = new HistoryContextData {
                    RandomDefinition = randomDefinition
                },
            }).ToList();

            var searchMatch = Regex.Match(query.RawUserQuery, $@"^.*?{Menu.HISTORY}\s?(?<Search>.*)$");
            if (searchMatch.Groups["Search"].Success) {
                historyResults = historyResults.Where(result =>
                    // If a result does not have the right type of Context, then it's not searchable
                    result.ContextData is HistoryContextData historyContext &&
                    historyContext.RandomDefinition.Contains(searchMatch.Groups["Search"].Value)
                ).ToList();
            }

            return historyResults.Count > 0 ? PluginUtil.FixPositionAsScore(historyResults).ToList() : [
                new Result {
                    QueryTextDisplay = query.Search,
                    IcoPath = this.IconLoader.WARNING,
                    Title = "No history result",
                }
            ];
        }

        #endregion
    }
}
