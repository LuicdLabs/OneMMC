using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace OneMMC.Controls;

/// <summary>
/// Stacks every repeater element vertically after measuring its exact height.
/// </summary>
/// <remarks>
/// <para>
/// WinUI's <see cref="StackLayout"/> is a virtualizing layout. For variable-height content it must
/// estimate the size of unrealized elements and revise the scroll extent as more elements are measured.
/// A large correction near the estimated end can move the scroll thumb away from the pointer, repeatedly
/// correct the offset, or visibly jump the content. These behaviors are tracked by microsoft-ui-xaml issues
/// 9308 and 1829.
/// </para>
/// <para>
/// This layout removes that estimate by realizing and measuring every child. Use it only for a demonstrably
/// small, bounded collection. It is intentionally unsuitable for growing sources such as certificates,
/// devices, users, shares, or firewall rules because non-virtualizing those sources would increase initial
/// layout cost and retained XAML element count.
/// </para>
/// </remarks>
public sealed partial class StableStackLayout : NonVirtualizingLayout
{
    private static readonly Guid IWeakReferenceSourceIid = new("00000038-0000-0000-C000-000000000046");

    /// <summary>
    /// Initializes a new instance of the <see cref="StableStackLayout"/> class.
    /// </summary>
    public StableStackLayout()
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
    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        double desiredWidth = 0;
        double desiredHeight = 0;
        Size childAvailableSize = new(availableSize.Width, double.PositiveInfinity);

        foreach (var child in context.Children)
        {
            child.Measure(childAvailableSize);
            desiredWidth = Math.Max(desiredWidth, child.DesiredSize.Width);
            desiredHeight += child.DesiredSize.Height;
        }

        return new Size(desiredWidth, desiredHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        double offset = 0;

        foreach (var child in context.Children)
        {
            double height = child.DesiredSize.Height;
            child.Arrange(new Rect(0, offset, finalSize.Width, height));
            offset += height;
        }

        return finalSize;
    }
}
