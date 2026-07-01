using System.Threading.Tasks;

namespace OneMMC.Helpers;

/// <summary>
/// Implemented by editor pages that can hold unsaved edits, so the application shell can prompt the
/// user before leaving the page.
/// </summary>
/// <remarks>
/// A page's own <c>OnNavigatingFrom</c> can cancel and re-issue a back-navigation, but it runs too late
/// for the other ways a user leaves a page — clicking a breadcrumb item, switching navigation-pane
/// items, or closing the window — because those paths mutate shared navigation state (the breadcrumb
/// trail, the pane selection) <em>before</em> the frame navigation they trigger is cancelled, leaving
/// that state inconsistent. The shell therefore consults this interface up front, resolves the unsaved
/// changes once, and only then performs the navigation.
/// </remarks>
public interface IUnsavedChangesGuard
{
    /// <summary>Gets a value indicating whether the page has edits that leaving would discard.</summary>
    bool HasUnsavedChanges { get; }

    /// <summary>
    /// Shows the Save / Don't Save / Cancel prompt (saving when chosen) and reports whether the caller
    /// may proceed.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the edits were saved or discarded (or there was nothing to resolve);
    /// <see langword="false"/> when the user cancelled or the save failed, in which case the caller must
    /// stay on the page.
    /// </returns>
    Task<bool> ConfirmLeaveAsync();

    /// <summary>
    /// Tells the page to skip its next <c>OnNavigatingFrom</c> prompt because the shell already resolved
    /// the unsaved changes via <see cref="ConfirmLeaveAsync"/> and is about to drive the navigation.
    /// </summary>
    void SuppressNextNavigationGuard();
}
