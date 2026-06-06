using ManagementTools.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.UserSecurity.SecPol.IPSecurity.Editors;

/// <summary>
/// Identifies whether an IPsec editor is creating a new object or editing an existing object.
/// </summary>
public enum IPSecurityEditorMode
{
    /// <summary>Create a new object.</summary>
    Create,

    /// <summary>Edit an existing object.</summary>
    Edit
}

/// <summary>
/// Represents the validated result of editing a filter list and its in-memory filters.
/// </summary>
public sealed class IPSecurityFilterListEditorResult
{
    /// <summary>Gets the options that create or update the filter list.</summary>
    public required IPSecurityFilterListCommandOptions Options { get; init; }

    /// <summary>Gets the filters that existed when the editor opened.</summary>
    public IReadOnlyList<IPSecurityFilterCommandOptions> OriginalFilters { get; init; } = [];

    /// <summary>Gets the filters currently contained by the edited list.</summary>
    public IReadOnlyList<IPSecurityFilterCommandOptions> Filters { get; init; } = [];
}

internal static class IPSecurityEditorValidation
{
    public static bool TryValidate(Action validation, InfoBar infoBar, string errorMessage)
    {
        try
        {
            validation();
            infoBar.IsOpen = false;
            return true;
        }
        catch (ArgumentException)
        {
            infoBar.Message = errorMessage;
            infoBar.IsOpen = true;
            return false;
        }
    }

    public static string? OptionalText(string text)
    {
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public static string? RenamedValue(IPSecurityEditorMode mode, string originalName, string currentName)
    {
        return mode == IPSecurityEditorMode.Edit
            && !string.Equals(originalName, currentName, StringComparison.Ordinal)
                ? currentName
                : null;
    }

    public static int? GetInteger(NumberBox numberBox)
    {
        return double.IsNaN(numberBox.Value)
            ? null
            : checked((int)numberBox.Value);
    }

    public static int? GetOptionalPort(NumberBox numberBox)
    {
        int? value = GetInteger(numberBox);
        return value is null or 0 ? null : value;
    }

    public static IPSecurityFilterCommandOptions ToFilterOptions(IPSecurityFilterDefinition filter)
    {
        return new IPSecurityFilterCommandOptions
        {
            FilterListName = filter.FilterListName,
            SourceAddress = filter.SourceAddress,
            DestinationAddress = filter.DestinationAddress,
            Description = OptionalText(filter.Description),
            Protocol = OptionalText(filter.Protocol),
            IsMirrored = filter.IsMirrored,
            SourceMask = OptionalText(filter.SourceMask),
            DestinationMask = OptionalText(filter.DestinationMask),
            SourcePort = filter.SourcePort == 0 ? null : filter.SourcePort,
            DestinationPort = filter.DestinationPort == 0 ? null : filter.DestinationPort
        };
    }

    public static IPSecurityFilterCommandOptions WithFilterListName(
        IPSecurityFilterCommandOptions filter,
        string filterListName)
    {
        return new IPSecurityFilterCommandOptions
        {
            FilterListName = filterListName,
            SourceAddress = filter.SourceAddress,
            DestinationAddress = filter.DestinationAddress,
            Description = filter.Description,
            Protocol = filter.Protocol,
            IsMirrored = filter.IsMirrored,
            SourceMask = filter.SourceMask,
            DestinationMask = filter.DestinationMask,
            SourcePort = filter.SourcePort,
            DestinationPort = filter.DestinationPort
        };
    }
}

internal sealed class IPSecurityFilterEditorItem
{
    public required IPSecurityFilterCommandOptions Options { get; init; }

    public required string Summary { get; init; }
}

internal sealed class IPSecurityAuthenticationEditorItem
{
    public required IPSecurityAuthenticationMethodKind Kind { get; init; }

    public required string DisplayName { get; init; }

    public string Detail { get; init; } = string.Empty;

    public bool EnableCertificateToAccountMapping { get; init; }

    public bool ExcludeCertificateAuthorityName { get; init; }

    public string PreSharedKey { get; init; } = string.Empty;

    public bool RequiresPreSharedKeyReentry { get; init; }
}
