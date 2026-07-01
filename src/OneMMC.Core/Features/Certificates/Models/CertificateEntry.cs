using System.Security.Cryptography.X509Certificates;

namespace OneMMC.Core.Features.Certificates.Models;

/// <summary>
/// Represents a certificate, CRL, or CTL entry displayed in the certificate pages.
/// </summary>
public sealed class CertificateEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateEntry"/> class.
    /// </summary>
    /// <param name="storeLocation">The logical store location.</param>
    /// <param name="storeName">The system store name.</param>
    /// <param name="kind">The entry kind.</param>
    /// <param name="identifier">The stable identifier used to resolve the native context again.</param>
    /// <param name="displayName">The primary label shown in the UI.</param>
    /// <param name="secondaryText">The secondary metadata line shown in the UI.</param>
    /// <param name="tertiaryText">The tertiary metadata line shown in the UI.</param>
    /// <param name="searchText">The flattened searchable text.</param>
    public CertificateEntry(
        StoreLocation storeLocation,
        string storeName,
        CertificateEntryKind kind,
        string identifier,
        string displayName,
        string secondaryText,
        string tertiaryText,
        string searchText)
    {
        StoreLocation = storeLocation;
        StoreName = storeName;
        Kind = kind;
        Identifier = identifier;
        DisplayName = displayName;
        SecondaryText = secondaryText;
        TertiaryText = tertiaryText;
        SearchText = searchText;
    }

    /// <summary>
    /// Gets the logical certificate store location.
    /// </summary>
    public StoreLocation StoreLocation { get; }

    /// <summary>
    /// Gets the system certificate store name.
    /// </summary>
    public string StoreName { get; }

    /// <summary>
    /// Gets the entry kind.
    /// </summary>
    public CertificateEntryKind Kind { get; }

    /// <summary>
    /// Gets the stable identifier used to re-resolve the native context.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// Gets the primary label shown in the UI.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the secondary metadata line shown in the UI.
    /// </summary>
    public string SecondaryText { get; }

    /// <summary>
    /// Gets the tertiary metadata line shown in the UI.
    /// </summary>
    public string TertiaryText { get; }

    /// <summary>
    /// Gets the flattened searchable text for the entry.
    /// </summary>
    public string SearchText { get; }

    /// <summary>
    /// Returns <see langword="true"/> when the entry matches the provided filter text.
    /// </summary>
    /// <param name="filterText">The filter text to evaluate.</param>
    /// <returns><see langword="true"/> when the entry matches the filter; otherwise <see langword="false"/>.</returns>
    public bool Matches(string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
        {
            return true;
        }

        return SearchText.Contains(filterText, StringComparison.CurrentCultureIgnoreCase);
    }
}
