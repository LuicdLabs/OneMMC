using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

namespace OneMMC.Services
{
    /// <summary>
    /// Breadcrumb item record.
    /// Declared at namespace level so XAML compiled bindings (x:DataType/x:Bind) can reference it.
    /// </summary>
    public partial record Breadcrumb(string Label, Type Page, object? Parameter = null)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Breadcrumb navigation service - Manages breadcrumb navigation
    /// </summary>
    public class BreadcrumbNavigationService
    {
        #region Attached Properties
        public static readonly DependencyProperty IsHeaderVisibleProperty =
            DependencyProperty.RegisterAttached("IsHeaderVisible", typeof(bool), typeof(BreadcrumbNavigationService), new PropertyMetadata(true));

        public static void SetIsHeaderVisible(DependencyObject obj, bool value)
        {
            obj.SetValue(IsHeaderVisibleProperty, value);
        }

        public static bool GetIsHeaderVisible(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsHeaderVisibleProperty);
        }

        public static readonly DependencyProperty ClearNavigationProperty =
            DependencyProperty.RegisterAttached("ClearNavigation", typeof(bool), typeof(BreadcrumbNavigationService), new PropertyMetadata(true));

        public static void SetClearNavigation(DependencyObject obj, bool value)
        {
            obj.SetValue(ClearNavigationProperty, value);
        }

        public static bool GetClearNavigation(DependencyObject obj)
        {
            return (bool)obj.GetValue(ClearNavigationProperty);
        }

        public static readonly DependencyProperty PageTitleProperty =
            DependencyProperty.RegisterAttached("PageTitle", typeof(string), typeof(BreadcrumbNavigationService), new PropertyMetadata(string.Empty));

        public static void SetPageTitle(DependencyObject obj, string value)
        {
            obj.SetValue(PageTitleProperty, value);
        }

        public static string GetPageTitle(DependencyObject obj)
        {
            return (string)obj.GetValue(PageTitleProperty);
        }
        #endregion

        #region Enums
        public enum NavigateAnimationType
        {
            NoAnimation,
            Entrance,
            DrillIn,
            SlideFromLeft,
            SlideFromRight
        }

        /// <summary>
        /// Navigation state machine states
        /// </summary>
        public enum NavigationState
        {
            Idle,
            NavigatingFromBreadcrumb,
            RestoringState,
            NavigatingForward
        }
        #endregion

        #region Properties
        public static NavigationView? MainNavigation { get; private set; }
        public static BreadcrumbBar? MainBreadcrumb { get; private set; }
        public static Frame? MainFrame { get; private set; }
        public static ObservableCollection<Breadcrumb> BreadCrumbs { get; private set; } = new ObservableCollection<Breadcrumb>();

        // Breadcrumb history stack for breadcrumb click navigation (separate from main nav history)
        private static Stack<List<Breadcrumb>> _breadcrumbClickHistory = new Stack<List<Breadcrumb>>();

        // Main navigation history stack for switching between main nav items.
        // Each entry contains: (breadcrumbs, associated click history, associated back stack source types).
        private static Stack<(List<Breadcrumb> Breadcrumbs, Stack<List<Breadcrumb>> ClickHistory, Stack<bool> BackStackSourceTypes)> _mainNavHistory =
            new Stack<(List<Breadcrumb>, Stack<List<Breadcrumb>>, Stack<bool>)>();
        
        // Track whether each BackStack entry was created from breadcrumb click navigation
        // True = breadcrumb click navigation (restore from _breadcrumbClickHistory)
        // False = normal forward navigation (just remove last breadcrumb)
        private static Stack<bool> _backStackSourceType = new Stack<bool>();
        
        // Upper bound for the two history stacks below. They are pushed on every navigation but only
        // popped when the user walks all the way back, so forward-only browsing grew them for the whole
        // session. Ten is deeper than any realistic run of back-navigations.
        private const int MaximumHistoryDepth = 10;

        // State machine for navigation control
        public static NavigationState CurrentState { get; private set; } = NavigationState.Idle;
        
        // Convenience properties for backward compatibility
        public static bool IsNavigatingFromBreadcrumb => CurrentState == NavigationState.NavigatingFromBreadcrumb;
        public static bool IsRestoringBreadcrumbState => CurrentState == NavigationState.RestoringState;
        #endregion

        #region Constructor
        public static void Init(NavigationView navigationView, BreadcrumbBar breadcrumbBar, Frame frame)
        {
            MainNavigation = navigationView;
            MainBreadcrumb = breadcrumbBar;
            MainFrame = frame;
            BreadCrumbs = new ObservableCollection<Breadcrumb>();
        }
        #endregion

        #region Private Functions
        private static void UpdateBreadcrumb()
        {
            if (MainBreadcrumb != null)
            {
                MainBreadcrumb.ItemsSource = BreadCrumbs;
            }
        }

        private static void LogDebug(string method, string message)
        {
            var breadcrumbStr = string.Join(" > ", BreadCrumbs.Select(b => b.Label));
            var clickHistoryStr = $"ClickHistory={_breadcrumbClickHistory.Count}";
            var mainNavHistoryStr = $"MainNavHistory={_mainNavHistory.Count}";
            var backStackStr = MainFrame != null ? $"BackStack={MainFrame.BackStack.Count}" : "BackStack=N/A";
            OneMMC.Services.Logging.UiLogger.LogDebug($"[Breadcrumb][{method}] {message} | Crumbs=[{breadcrumbStr}] | {clickHistoryStr} | {mainNavHistoryStr} | {backStackStr} | State={CurrentState}");
        }

        /// <summary>
        /// Get cached page metadata to avoid creating instances repeatedly
        /// </summary>
        private static (bool IsHeaderVisible, string PageTitle, bool ClearNavigation) GetPageMetadata(Type pageType)
        {
            // Removed Activator.CreateInstance to prevent memory leaks.
            // Attached properties are not used in XAML, so we return defaults.
            return (true, string.Empty, true);
        }

        private static NavigationTransitionInfo GetTransitionInfo(NavigateAnimationType animType, bool navigatingBack = false)
        {
            if (navigatingBack)
            {
                return new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromLeft };
            }

            return animType switch
            {
                NavigateAnimationType.NoAnimation => new SuppressNavigationTransitionInfo(),
                NavigateAnimationType.Entrance => new EntranceNavigationTransitionInfo(),
                NavigateAnimationType.DrillIn => new DrillInNavigationTransitionInfo(),
                NavigateAnimationType.SlideFromRight => new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight },
                NavigateAnimationType.SlideFromLeft => new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromLeft },
                _ => new EntranceNavigationTransitionInfo(),
            };
        }

        /// <summary>
        /// Clear navigation session data
        /// </summary>
        private static void ClearNavigationSession()
        {
            BreadCrumbs.Clear();
            _breadcrumbClickHistory.Clear();
            _backStackSourceType.Clear();
        }

        /// <summary>
        /// Restore breadcrumbs from a list
        /// </summary>
        private static void RestoreBreadcrumbs(IEnumerable<Breadcrumb> breadcrumbs)
        {
            BreadCrumbs.Clear();
            foreach (var crumb in breadcrumbs)
            {
                BreadCrumbs.Add(crumb);
            }
            UpdateBreadcrumb();
        }

        private static List<Breadcrumb> ToLightweightBreadcrumbs(IEnumerable<Breadcrumb> breadcrumbs) =>
            breadcrumbs.Select(crumb => crumb with { Parameter = null }).ToList();

        private static Stack<List<Breadcrumb>> CloneLightweightBreadcrumbStack(Stack<List<Breadcrumb>> source) =>
            new(source.Reverse().Select(ToLightweightBreadcrumbs));

        private static Stack<bool> CloneBoolStack(Stack<bool> source) =>
            new(source.Reverse());

        /// <summary>
        /// Discards the oldest entries once a history stack exceeds <see cref="MaximumHistoryDepth"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="Stack{T}"/> cannot drop from the bottom, so the stack is rebuilt from the newest
        /// entries. Enumeration is newest-first, so the kept entries are reversed to oldest-first before
        /// being pushed back, which restores the original ordering.
        /// </remarks>
        private static void TrimHistory<T>(ref Stack<T> stack)
        {
            if (stack.Count <= MaximumHistoryDepth)
            {
                return;
            }

            stack = new Stack<T>(stack.Take(MaximumHistoryDepth).Reverse());
        }

        #endregion

        #region Public Functions
        /// <summary>
        /// Navigate to specified page
        /// </summary>
        public static void Navigate(Type targetPageType, NavigateAnimationType animType, object? parameter = null)
        {
            if (MainFrame == null) return;

            CurrentState = NavigationState.NavigatingForward;

            // Get cached page metadata
            var (isHeaderVisible, pageTitle, clearNavigation) = GetPageMetadata(targetPageType);

            // Prepare breadcrumbs
            if (clearNavigation)
            {
                SaveToMainNavHistory();
                ClearNavigationSession();
            }

            if (!string.IsNullOrEmpty(pageTitle))
            {
                BreadCrumbs.Add(new Breadcrumb(pageTitle, targetPageType, parameter));
            }
            UpdateBreadcrumb();

            // Prepare transition animation
            var info = GetTransitionInfo(animType);

            // Update header visibility
            if (MainNavigation != null)
            {
                MainNavigation.AlwaysShowHeader = isHeaderVisible;
            }
            ChangeBreadcrumbVisibility(isHeaderVisible);

            // Execute navigation
            MainFrame.Navigate(targetPageType, parameter, info);
            
            CurrentState = NavigationState.Idle;
        }

        /// <summary>
        /// Navigate from breadcrumb (return to specific level)
        /// </summary>
        public static void NavigateFromBreadcrumb(Type targetPageType, int breadcrumbIndex, object? parameter = null)
        {
            if (MainFrame == null) return;

            // Set state to prevent NavigationView_SelectionChanged from modifying breadcrumbs
            CurrentState = NavigationState.NavigatingFromBreadcrumb;
            
            LogDebug("NavigateFromBreadcrumb", $"START - targetPage={targetPageType.Name}, index={breadcrumbIndex}");

            // Get cached page metadata
            var (isHeaderVisible, _, _) = GetPageMetadata(targetPageType);

            // Update header visibility
            if (MainNavigation != null)
            {
                MainNavigation.AlwaysShowHeader = isHeaderVisible;
            }
            ChangeBreadcrumbVisibility(isHeaderVisible);

            // Save current breadcrumb state to CLICK HISTORY before removing items (for GoBack to restore)
            if (breadcrumbIndex < BreadCrumbs.Count - 1)
            {
                LogDebug("NavigateFromBreadcrumb", "Saving state before truncating");
                SaveToBreadcrumbClickHistory();
                
                // Mark this BackStack entry as created from breadcrumb click navigation
                _backStackSourceType.Push(true);
                TrimHistory(ref _backStackSourceType);

                int itemsToRemove = BreadCrumbs.Count - breadcrumbIndex - 1;
                for (int i = 0; i < itemsToRemove; i++)
                {
                    BreadCrumbs.RemoveAt(BreadCrumbs.Count - 1);
                }
                UpdateBreadcrumb();
                LogDebug("NavigateFromBreadcrumb", $"After truncating to index {breadcrumbIndex}");
            }

            // Use slide left animation
            var info = new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromLeft };
            
            // Navigate to target page (this adds current page to BackStack, enabling GoBack)
            MainFrame.Navigate(targetPageType, parameter, info);
            
            // Reset state after navigation completes
            CurrentState = NavigationState.Idle;
            LogDebug("NavigateFromBreadcrumb", "END - Navigation completed");
        }

        /// <summary>
        /// Directly add breadcrumb item and navigate
        /// </summary>
        public static void NavigateWithBreadcrumb(Type targetPageType, string pageTitle, NavigateAnimationType animType, bool clearNavigation = false, object? parameter = null)
        {
            if (MainFrame == null) return;

            // Prepare breadcrumbs
            if (clearNavigation)
            {
                SaveToMainNavHistory();
                ClearNavigationSession();
            }

            if (!string.IsNullOrEmpty(pageTitle))
            {
                BreadCrumbs.Add(new Breadcrumb(pageTitle, targetPageType, parameter));
            }
            UpdateBreadcrumb();

            // Execute navigation
            MainFrame.Navigate(targetPageType, parameter, GetTransitionInfo(animType));
        }

        /// <summary>
        /// Change breadcrumb visibility
        /// </summary>
        public static void ChangeBreadcrumbVisibility(bool isVisible)
        {
            if (MainBreadcrumb != null)
            {
                MainBreadcrumb.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Clear all breadcrumbs (called when switching main navigation items)
        /// </summary>
        public static void ClearBreadcrumbs()
        {
            LogDebug("ClearBreadcrumbs", "START");
            SaveToMainNavHistory();
            BreadCrumbs.Clear();
            UpdateBreadcrumb();
            LogDebug("ClearBreadcrumbs", "END");
        }

        /// <summary>
        /// Manually add breadcrumb
        /// </summary>
        public static void AddBreadcrumb(string label, Type pageType, object? parameter = null)
        {
            // Mark this upcoming BackStack entry as normal forward navigation (not breadcrumb click)
            // This is called before contentFrame.Navigate() which adds to BackStack
            _backStackSourceType.Push(false);
            TrimHistory(ref _backStackSourceType);

            BreadCrumbs.Add(new Breadcrumb(label, pageType, parameter));
            UpdateBreadcrumb();
            LogDebug("AddBreadcrumb", $"Added '{label}'");
        }

        /// <summary>
        /// Save current breadcrumb state to main navigation history (for switching between main nav items).
        /// Parameter values are intentionally dropped so old view models and large model graphs are not retained.
        /// </summary>
        private static void SaveToMainNavHistory()
        {
            if (BreadCrumbs.Count == 0) return;

            var currentState = ToLightweightBreadcrumbs(BreadCrumbs);
            var clickHistoryCopy = CloneLightweightBreadcrumbStack(_breadcrumbClickHistory);
            var backStackSourceTypeCopy = CloneBoolStack(_backStackSourceType);

            _mainNavHistory.Push((currentState, clickHistoryCopy, backStackSourceTypeCopy));
            TrimHistory(ref _mainNavHistory);
            LogDebug("SaveToMainNavHistory", $"Saved [{string.Join(" > ", currentState.Select(b => b.Label))}] with {clickHistoryCopy.Count} click history entries");

            _breadcrumbClickHistory.Clear();
            _backStackSourceType.Clear();
        }

        /// <summary>
        /// Save current breadcrumb state to breadcrumb click history (for breadcrumb click navigation)
        /// </summary>
        private static void SaveToBreadcrumbClickHistory()
        {
            if (BreadCrumbs.Count == 0) return;
            
            var currentState = ToLightweightBreadcrumbs(BreadCrumbs);
            _breadcrumbClickHistory.Push(currentState);
            TrimHistory(ref _breadcrumbClickHistory);
            LogDebug("SaveToBreadcrumbClickHistory", $"Saved [{string.Join(" > ", currentState.Select(b => b.Label))}]");
        }


        /// <summary>
        /// Restore previous breadcrumb state (for back navigation)
        /// </summary>
        public static void RestorePreviousBreadcrumbState()
        {
            // Set state to prevent NavigationView_SelectionChanged from modifying breadcrumbs
            CurrentState = NavigationState.RestoringState;
            
            LogDebug("RestorePreviousBreadcrumbState", "START");
            
            // Check the source type of the BackStack entry we're returning from
            bool wasFromBreadcrumbClick = false;
            if (_backStackSourceType.Count > 0)
            {
                wasFromBreadcrumbClick = _backStackSourceType.Pop();
            }
            
            if (wasFromBreadcrumbClick && _breadcrumbClickHistory.Count > 0)
            {
                // Restore from breadcrumb click history
                var previousState = _breadcrumbClickHistory.Pop();
                LogDebug("RestorePreviousBreadcrumbState", $"Restoring from click history: [{string.Join(" > ", previousState.Select(b => b.Label))}]");
                RestoreBreadcrumbs(previousState);
            }
            else if (BreadCrumbs.Count > 1)
            {
                // Normal back navigation: remove the last breadcrumb
                LogDebug("RestorePreviousBreadcrumbState", "Removing last item");
                BreadCrumbs.RemoveAt(BreadCrumbs.Count - 1);
                UpdateBreadcrumb();
            }
            else if (_mainNavHistory.Count > 0)
            {
                // Restore from main nav history
                var (previousBreadcrumbs, previousClickHistory, previousBackStackSourceTypes) = _mainNavHistory.Pop();
                LogDebug("RestorePreviousBreadcrumbState", $"Restoring from main nav history: [{string.Join(" > ", previousBreadcrumbs.Select(b => b.Label))}]");

                RestoreBreadcrumbs(previousBreadcrumbs);
                _breadcrumbClickHistory = previousClickHistory;
                _backStackSourceType = previousBackStackSourceTypes;
            }
            else
            {
                LogDebug("RestorePreviousBreadcrumbState", "Nothing to restore");
            }
            
            LogDebug("RestorePreviousBreadcrumbState", "END");
            
            // Reset state after restoration completes
            CurrentState = NavigationState.Idle;
        }

        /// <summary>
        /// Clear history stacks (for reset)
        /// </summary>
        public static void ClearHistory()
        {
            _breadcrumbClickHistory.Clear();
            _mainNavHistory.Clear();
            _backStackSourceType.Clear();
        }

        #endregion
    }
}

