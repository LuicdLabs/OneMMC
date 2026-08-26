using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Controls;

/// <summary>
/// Provides stable scrolling for a <see cref="SettingsExpander"/> with a small, bounded item collection.
/// </summary>
/// <remarks>
/// <para>
/// The CommunityToolkit <see cref="SettingsExpander"/> template hosts its items in an internal
/// <see cref="ItemsRepeater"/> named <c>PART_ItemsRepeater</c>, using WinUI's virtualizing
/// <see cref="StackLayout"/>. On a long page, especially with nested repeaters and expanders of very different
/// heights, unrealized item heights make the reported scroll extent an estimate. Realizing another group near
/// the end changes that estimate, so the scroll host clamps or shifts its offset and visibly jumps upward.
/// See microsoft-ui-xaml issues 9308 and 1829.
/// </para>
/// <para>
/// This subclass changes only the internal repeater's layout to <see cref="StableStackLayout"/>, making the
/// expanded height exact. It must be used only when the direct item count is fixed or tightly bounded.
/// Do not use it for large or growing <see cref="SettingsExpander.ItemsSource"/> collections because every item
/// will be realized.
/// </para>
/// <para>
/// <see cref="SettingsExpander.IsExpanded"/> is deliberately unrelated to this fix. Collapsing changes visual
/// presentation and may defer initial rendering, but it does not clear <see cref="SettingsExpander.Items"/>,
/// release the backing ItemsSource or view model, dispose resources, force collection of existing XAML peers,
/// reduce process RAM, change StackLayout's extent algorithm, or repair a layout cycle. Never use
/// <c>IsExpanded="False"</c> as a memory or layout-cycle fix.
/// </para>
/// </remarks>
public sealed partial class StableSettingsExpander : SettingsExpander
{
    private const string ItemsRepeaterPartName = "PART_ItemsRepeater";
    private static readonly Guid IWeakReferenceSourceIid = new("00000038-0000-0000-C000-000000000046");
    private readonly StableStackLayout _itemsLayout = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="StableSettingsExpander"/> class.
    /// </summary>
    public StableSettingsExpander()
    {
    }

    /// <inheritdoc />
    protected override bool IsOverridableInterface(Guid iid)
    {
        // WinUI's current projection sends this QI to the composed inner object. Marking it overridable
        // makes CsWinRT own the weak reference, matching microsoft/CsWinRT#2011.
        return iid == IWeakReferenceSourceIid || base.IsOverridableInterface(iid);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild(ItemsRepeaterPartName) is ItemsRepeater itemsRepeater)
        {
            // WinUI retains the composed native object, so its managed outer must live just as long.
            itemsRepeater.Layout = _itemsLayout;
        }
    }
}
