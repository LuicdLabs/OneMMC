using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Helpers;

/// <summary>
/// Drives the Basic "On an event" Log / Source combos shared by the New Trigger and Create Task dialogs.
/// The Log combo shows the friendly channel display names taskschd.msc uses (e.g. "Hardware Events"),
/// resolved on a background thread so the dialog never blocks; the controller maps the selected display
/// name back to the raw channel for the query, and narrows the Source list to the sources registered to
/// the selected log.
/// </summary>
internal sealed class EventLogPickerController
{
    private readonly ComboBox _logCombo;
    private readonly ComboBox _sourceCombo;
    private readonly DispatcherQueue _dispatcher;

    private List<string> _rawChannels = [];
    private List<string> _allProviders = [];
    private Dictionary<string, string> _rawByDisplay = new(StringComparer.OrdinalIgnoreCase);
    private bool _populated;

    // Set while the controller swaps the Log combo's ItemsSource (raw → display names) so the resulting
    // SelectionChanged does not clobber the Source list mid-swap.
    private bool _suppressSourceRefresh;

    public EventLogPickerController(ComboBox logCombo, ComboBox sourceCombo)
    {
        _logCombo = logCombo;
        _sourceCombo = sourceCombo;
        _dispatcher = logCombo.DispatcherQueue;
    }

    /// <summary>
    /// Populates both combos from the live system (raw channel names first), then resolves the friendly
    /// channel display names on a background thread and swaps them in. Safe to call repeatedly.
    /// </summary>
    public void EnsurePopulated()
    {
        if (_populated)
        {
            return;
        }
        _populated = true;

        _rawChannels = EventLogSources.GetChannels();
        _allProviders = EventLogSources.GetProviders();
        _logCombo.ItemsSource = _rawChannels;
        _sourceCombo.ItemsSource = _allProviders;

        _ = Task.Run(() =>
        {
            var map = EventLogSources.BuildChannelDisplayNameMap();
            _dispatcher.TryEnqueue(() => ApplyDisplayNames(map));
        });
    }

    /// <summary>Narrows the Source list to the sources of the selected log; call from the Log combo's SelectionChanged.</summary>
    public void RefreshSourcesForSelectedLog()
    {
        if (_suppressSourceRefresh)
        {
            return;
        }

        var raw = SelectedRawChannel();
        _sourceCombo.ItemsSource = string.IsNullOrWhiteSpace(raw)
            ? _allProviders
            : EventLogSources.GetSourcesForChannel(raw, _allProviders);
    }

    /// <summary>The raw channel name for the query, mapped back from the selected/typed display name.</summary>
    public string? SelectedRawChannel()
    {
        var text = EventLogSources.SelectedText(_logCombo);
        if (text is null)
        {
            return null;
        }
        return _rawByDisplay.TryGetValue(text, out var raw) ? raw : text;
    }

    /// <summary>The selected/typed source name, used as the provider in the query.</summary>
    public string? SelectedSource() => EventLogSources.SelectedText(_sourceCombo);

    /// <summary>
    /// Pre-selects a log channel and (optionally) a source, used to seed the Basic fields when editing an
    /// existing event trigger. The log is set by its raw channel name; if the display-name map has already
    /// been applied the matching display name is shown instead. The Source list is narrowed to the channel
    /// before the source is applied. Both combos are editable, so a value not present in the list is kept
    /// as typed text, and <see cref="ApplyDisplayNames"/> preserves the selection if it runs afterwards.
    /// </summary>
    public void Select(string? rawChannel, string? source)
    {
        if (!string.IsNullOrWhiteSpace(rawChannel))
        {
            // Prefer the display name when the reverse map is ready; otherwise the raw channel doubles as
            // the editable text and ApplyDisplayNames swaps it later.
            var display = _rawByDisplay.FirstOrDefault(kvp =>
                string.Equals(kvp.Value, rawChannel, StringComparison.OrdinalIgnoreCase)).Key ?? rawChannel;

            _suppressSourceRefresh = true;
            _logCombo.SelectedItem = display;
            if (_logCombo.SelectedItem is null)
            {
                _logCombo.Text = display;
            }
            _suppressSourceRefresh = false;

            RefreshSourcesForSelectedLog();
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            _sourceCombo.SelectedItem = source;
            if (_sourceCombo.SelectedItem is null)
            {
                _sourceCombo.Text = source;
            }
        }
    }

    // Swaps the Log combo from raw channel names to their localized display names while preserving the
    // user's current selection.
    private void ApplyDisplayNames(IReadOnlyDictionary<string, string> displayMap)
    {
        if (displayMap.Count == 0)
        {
            return;
        }

        var selectedRaw = SelectedRawChannel();
        var displayNames = new List<string>(_rawChannels.Count);
        var reverse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in _rawChannels)
        {
            var display = displayMap.TryGetValue(raw, out var d) && !string.IsNullOrWhiteSpace(d) ? d : raw;
            displayNames.Add(display);
            reverse.TryAdd(display, raw);
        }
        _rawByDisplay = reverse;

        _suppressSourceRefresh = true;
        _logCombo.ItemsSource = displayNames;
        if (selectedRaw is not null)
        {
            var display = displayMap.TryGetValue(selectedRaw, out var d) && !string.IsNullOrWhiteSpace(d) ? d : selectedRaw;
            _logCombo.SelectedItem = display;
            if (_logCombo.SelectedItem is null)
            {
                _logCombo.Text = display; // editable combo: restore a typed (non-listed) value
            }
        }
        _suppressSourceRefresh = false;
    }
}
