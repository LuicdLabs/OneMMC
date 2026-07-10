// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Portions adapted from CommunityToolkit.WinUI.Controls.GridSplitter
// (https://github.com/CommunityToolkit/Windows, MIT license).

using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Controls
{
    /// <summary>
    /// A drop-in replacement for <see cref="GridSplitter"/> that is safe under Native AOT.
    /// </summary>
    /// <remarks>
    /// The packaged <see cref="GridSplitter"/> detects star-sized rows/columns with
    /// <c>((GridLength)definition.GetValue(ColumnDefinition.WidthProperty)).IsStar</c>. Boxing a
    /// projected WinRT value type (<see cref="GridLength"/>) through <see cref="DependencyObject.GetValue"/>
    /// and unboxing it back crashes with <see cref="System.InvalidCastException"/> (RhUnbox2) once the
    /// CsWinRT/AOT compiler is in play — every drag delta raised an unhandled UI exception on the
    /// Event Viewer (and every other) page. Reading the strongly-typed <see cref="ColumnDefinition.Width"/> /
    /// <see cref="RowDefinition.Height"/> properties returns the struct directly with no boxing, so this
    /// subclass re-implements the drag pipeline against those typed getters.
    ///
    /// The base <see cref="GridSplitter"/> keeps its resize helpers and drag state <c>private</c>, so they
    /// cannot be reused from a subclass; this type carries its own copy of that logic. It inherits the base
    /// <c>DefaultStyleKey</c> (<c>typeof(GridSplitter)</c>), so the visual template shipped in the Sizers
    /// package still applies unchanged.
    /// </remarks>
    public partial class AotGridSplitter : GridSplitter
    {
        private GridResizeDirection _resizeDirection;
        private GridResizeBehavior _resizeBehavior;
        private double _currentSize;
        private double _siblingSize;

        // --- Star detection (the AOT-safe fix): read the typed property, never GetValue-then-unbox. ---

        private static bool IsStarColumn(ColumnDefinition definition) => definition.Width.IsStar;

        private static bool IsStarRow(RowDefinition definition) => definition.Height.IsStar;

        // --- Grid resolution (mirrors GridSplitter.cs). ParentLevel/ResizeDirection/ResizeBehavior are
        //     inherited public/base members; Orientation lives on SizerBase. ---

        private FrameworkElement? TargetControl
        {
            get
            {
                if (ParentLevel == 0)
                {
                    return this;
                }

                var parent = Parent;
                for (int i = 2; i < ParentLevel; i++)
                {
                    if (parent is FrameworkElement frameworkElement)
                    {
                        parent = frameworkElement.Parent;
                    }
                    else
                    {
                        break;
                    }
                }

                return parent as FrameworkElement;
            }
        }

        private Grid? Resizable => TargetControl?.Parent as Grid;

        private ColumnDefinition? CurrentColumn
        {
            get
            {
                if (Resizable == null)
                {
                    return null;
                }

                var index = GetTargetedColumn();
                if (index >= 0 && index < Resizable.ColumnDefinitions.Count)
                {
                    return Resizable.ColumnDefinitions[index];
                }

                return null;
            }
        }

        private ColumnDefinition? SiblingColumn
        {
            get
            {
                if (Resizable == null)
                {
                    return null;
                }

                var index = GetSiblingColumn();
                if (index >= 0 && index < Resizable.ColumnDefinitions.Count)
                {
                    return Resizable.ColumnDefinitions[index];
                }

                return null;
            }
        }

        private RowDefinition? CurrentRow
        {
            get
            {
                if (Resizable == null)
                {
                    return null;
                }

                var index = GetTargetedRow();
                if (index >= 0 && index < Resizable.RowDefinitions.Count)
                {
                    return Resizable.RowDefinitions[index];
                }

                return null;
            }
        }

        private RowDefinition? SiblingRow
        {
            get
            {
                if (Resizable == null)
                {
                    return null;
                }

                var index = GetSiblingRow();
                if (index >= 0 && index < Resizable.RowDefinitions.Count)
                {
                    return Resizable.RowDefinitions[index];
                }

                return null;
            }
        }

        // --- Index / direction / behavior helpers (mirror GridSplitter.Helpers.cs). ---

        private int GetTargetedColumn() => GetTargetIndex(Grid.GetColumn(TargetControl));

        private int GetTargetedRow() => GetTargetIndex(Grid.GetRow(TargetControl));

        private int GetSiblingColumn() => GetSiblingIndex(Grid.GetColumn(TargetControl));

        private int GetSiblingRow() => GetSiblingIndex(Grid.GetRow(TargetControl));

        private int GetTargetIndex(int currentIndex) => _resizeBehavior switch
        {
            GridResizeBehavior.CurrentAndNext => currentIndex,
            GridResizeBehavior.PreviousAndNext => currentIndex - 1,
            GridResizeBehavior.PreviousAndCurrent => currentIndex - 1,
            _ => -1,
        };

        private int GetSiblingIndex(int currentIndex) => _resizeBehavior switch
        {
            GridResizeBehavior.CurrentAndNext => currentIndex + 1,
            GridResizeBehavior.PreviousAndNext => currentIndex + 1,
            GridResizeBehavior.PreviousAndCurrent => currentIndex,
            _ => -1,
        };

        private GridResizeDirection GetResizeDirection()
        {
            GridResizeDirection direction = ResizeDirection;

            if (direction == GridResizeDirection.Auto)
            {
                if (HorizontalAlignment != HorizontalAlignment.Stretch)
                {
                    direction = GridResizeDirection.Columns;
                }
                else if (VerticalAlignment != VerticalAlignment.Stretch)
                {
                    direction = GridResizeDirection.Rows;
                }
                else if (ActualWidth <= ActualHeight)
                {
                    direction = GridResizeDirection.Columns;
                }
                else
                {
                    direction = GridResizeDirection.Rows;
                }
            }

            return direction;
        }

        private GridResizeBehavior GetResizeBehavior()
        {
            GridResizeBehavior resizeBehavior = ResizeBehavior;

            if (resizeBehavior == GridResizeBehavior.BasedOnAlignment)
            {
                if (_resizeDirection == GridResizeDirection.Columns)
                {
                    resizeBehavior = HorizontalAlignment switch
                    {
                        HorizontalAlignment.Left => GridResizeBehavior.PreviousAndCurrent,
                        HorizontalAlignment.Right => GridResizeBehavior.CurrentAndNext,
                        _ => GridResizeBehavior.PreviousAndNext,
                    };
                }
                else
                {
                    resizeBehavior = VerticalAlignment switch
                    {
                        VerticalAlignment.Top => GridResizeBehavior.PreviousAndCurrent,
                        VerticalAlignment.Bottom => GridResizeBehavior.CurrentAndNext,
                        _ => GridResizeBehavior.PreviousAndNext,
                    };
                }
            }

            return resizeBehavior;
        }

        // --- Size setters / validators (mirror GridSplitter.Helpers.cs). ---

        private bool SetColumnWidth(ColumnDefinition columnDefinition, double newWidth, GridUnitType unitType)
        {
            var minWidth = columnDefinition.MinWidth;
            if (!double.IsNaN(minWidth) && newWidth < minWidth)
            {
                newWidth = minWidth;
            }

            var maxWidth = columnDefinition.MaxWidth;
            if (!double.IsNaN(maxWidth) && newWidth > maxWidth)
            {
                newWidth = maxWidth;
            }

            if (newWidth > ActualWidth)
            {
                columnDefinition.Width = new GridLength(newWidth, unitType);
                return true;
            }

            return false;
        }

        private bool IsValidColumnWidth(ColumnDefinition columnDefinition, double newWidth)
        {
            var minWidth = columnDefinition.MinWidth;
            if (!double.IsNaN(minWidth) && newWidth < minWidth)
            {
                return false;
            }

            var maxWidth = columnDefinition.MaxWidth;
            if (!double.IsNaN(maxWidth) && newWidth > maxWidth)
            {
                return false;
            }

            if (newWidth <= ActualWidth)
            {
                return false;
            }

            return true;
        }

        private bool SetRowHeight(RowDefinition rowDefinition, double newHeight, GridUnitType unitType)
        {
            var minHeight = rowDefinition.MinHeight;
            if (!double.IsNaN(minHeight) && newHeight < minHeight)
            {
                newHeight = minHeight;
            }

            var maxHeight = rowDefinition.MaxHeight;
            if (!double.IsNaN(maxHeight) && newHeight > maxHeight)
            {
                newHeight = maxHeight;
            }

            if (newHeight > ActualHeight)
            {
                rowDefinition.Height = new GridLength(newHeight, unitType);
                return true;
            }

            return false;
        }

        private bool IsValidRowHeight(RowDefinition rowDefinition, double newHeight)
        {
            var minHeight = rowDefinition.MinHeight;
            if (!double.IsNaN(minHeight) && newHeight < minHeight)
            {
                return false;
            }

            var maxHeight = rowDefinition.MaxHeight;
            if (!double.IsNaN(maxHeight) && newHeight > maxHeight)
            {
                return false;
            }

            if (newHeight <= ActualHeight)
            {
                return false;
            }

            return true;
        }

        // --- Drag pipeline overrides (mirror GridSplitter.Events.cs). ---

        /// <inheritdoc/>
        protected override void OnLoaded(RoutedEventArgs e)
        {
            _resizeDirection = GetResizeDirection();
            Orientation = _resizeDirection == GridResizeDirection.Rows ?
                Orientation.Horizontal : Orientation.Vertical;
            _resizeBehavior = GetResizeBehavior();
        }

        /// <inheritdoc/>
        protected override void OnDragStarting()
        {
            _resizeDirection = GetResizeDirection();
            Orientation = _resizeDirection == GridResizeDirection.Rows ?
                Orientation.Horizontal : Orientation.Vertical;
            _resizeBehavior = GetResizeBehavior();

            if (Orientation == Orientation.Horizontal)
            {
                _currentSize = CurrentRow?.ActualHeight ?? -1;
                _siblingSize = SiblingRow?.ActualHeight ?? -1;
            }
            else
            {
                _currentSize = CurrentColumn?.ActualWidth ?? -1;
                _siblingSize = SiblingColumn?.ActualWidth ?? -1;
            }
        }

        /// <inheritdoc/>
        protected override bool OnDragVertical(double verticalChange)
        {
            if (CurrentRow == null || SiblingRow == null || Resizable == null)
            {
                return false;
            }

            var currentChange = _currentSize + verticalChange;
            var siblingChange = _siblingSize + (verticalChange * -1);

            if (!IsValidRowHeight(CurrentRow, currentChange) || !IsValidRowHeight(SiblingRow, siblingChange))
            {
                return false;
            }

            if (!IsStarRow(CurrentRow))
            {
                var changed = SetRowHeight(CurrentRow, currentChange, GridUnitType.Pixel);

                if (!IsStarRow(SiblingRow))
                {
                    changed = SetRowHeight(SiblingRow, siblingChange, GridUnitType.Pixel);
                }

                return changed;
            }
            else if (!IsStarRow(SiblingRow))
            {
                return SetRowHeight(SiblingRow, siblingChange, GridUnitType.Pixel);
            }
            else
            {
                if (!IsValidRowHeight(CurrentRow, currentChange) ||
                    !IsValidRowHeight(SiblingRow, siblingChange))
                {
                    return false;
                }

                foreach (var rowDefinition in Resizable.RowDefinitions)
                {
                    if (rowDefinition == CurrentRow)
                    {
                        SetRowHeight(CurrentRow, currentChange, GridUnitType.Star);
                    }
                    else if (rowDefinition == SiblingRow)
                    {
                        SetRowHeight(SiblingRow, siblingChange, GridUnitType.Star);
                    }
                    else if (IsStarRow(rowDefinition))
                    {
                        rowDefinition.Height = new GridLength(rowDefinition.ActualHeight, GridUnitType.Star);
                    }
                }

                return true;
            }
        }

        /// <inheritdoc/>
        protected override bool OnDragHorizontal(double horizontalChange)
        {
            if (CurrentColumn == null || SiblingColumn == null || Resizable == null)
            {
                return false;
            }

            var currentChange = _currentSize + horizontalChange;
            var siblingChange = _siblingSize + (horizontalChange * -1);

            if (!IsValidColumnWidth(CurrentColumn, currentChange) || !IsValidColumnWidth(SiblingColumn, siblingChange))
            {
                return false;
            }

            if (!IsStarColumn(CurrentColumn))
            {
                var changed = SetColumnWidth(CurrentColumn, currentChange, GridUnitType.Pixel);

                if (!IsStarColumn(SiblingColumn))
                {
                    changed = SetColumnWidth(SiblingColumn, siblingChange, GridUnitType.Pixel);
                }

                return changed;
            }
            else if (!IsStarColumn(SiblingColumn))
            {
                return SetColumnWidth(SiblingColumn, siblingChange, GridUnitType.Pixel);
            }
            else
            {
                if (!IsValidColumnWidth(CurrentColumn, currentChange) ||
                    !IsValidColumnWidth(SiblingColumn, siblingChange))
                {
                    return false;
                }

                foreach (var columnDefinition in Resizable.ColumnDefinitions)
                {
                    if (columnDefinition == CurrentColumn)
                    {
                        SetColumnWidth(CurrentColumn, currentChange, GridUnitType.Star);
                    }
                    else if (columnDefinition == SiblingColumn)
                    {
                        SetColumnWidth(SiblingColumn, siblingChange, GridUnitType.Star);
                    }
                    else if (IsStarColumn(columnDefinition))
                    {
                        columnDefinition.Width = new GridLength(columnDefinition.ActualWidth, GridUnitType.Star);
                    }
                }

                return true;
            }
        }
    }
}
