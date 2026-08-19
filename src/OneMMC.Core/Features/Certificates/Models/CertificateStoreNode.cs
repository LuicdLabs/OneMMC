using CommunityToolkit.Mvvm.ComponentModel;
using System.Security.Cryptography.X509Certificates;

namespace OneMMC.Core.Features.Certificates.Models;

/// <summary>
/// Represents one logical certificate store card in the UI.
/// </summary>
public sealed partial class CertificateStoreNode : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateStoreNode"/> class.
    /// </summary>
    /// <param name="storeLocation">The store location.</param>
    /// <param name="storeName">The system store name.</param>
    /// <param name="displayName">The display name shown in the UI.</param>
    /// <param name="sections">The fixed logical sections for the store.</param>
    public CertificateStoreNode(
        StoreLocation storeLocation,
        string storeName,
        string displayName,
        IReadOnlyList<CertificateSection> sections)
    {
        StoreLocation = storeLocation;
        StoreName = storeName;
        DisplayName = displayName;
        Sections = sections;
    }

    /// <summary>
    /// Gets the logical store location.
    /// </summary>
    public StoreLocation StoreLocation { get; }

    /// <summary>
    /// Gets the system store name.
    /// </summary>
    public string StoreName { get; }

    /// <summary>
    /// Gets the display name shown in the UI.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the fixed logical sections in the store.
    /// </summary>
    public IReadOnlyList<CertificateSection> Sections { get; }

    /// <summary>
    /// Gets the visible section headings and entries rendered within the expander.
    /// </summary>
    /// <remarks>
    /// Built on first access rather than in the constructor. Flattening every section and entry up front
    /// meant loading the page paid for every row in every store even though most stores stay collapsed;
    /// a machine store like "Trusted Root Certification Authorities" alone contributes hundreds of rows.
    /// </remarks>
    public IReadOnlyList<CertificateDisplayRow> Rows => _rows ??= CreateRows(Sections);

    private IReadOnlyList<CertificateDisplayRow>? _rows;

    /// <summary>
    /// Gets or sets a value indicating whether the card is expanded.
    /// </summary>
    /// <remarks>
    /// Collapsed by default, matching certlm.msc / certmgr.msc. Expanding a store is what materializes
    /// its rows, so defaulting to expanded realized the whole certificate list on every visit.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>
    /// Creates a new store node with the same metadata and different sections.
    /// </summary>
    /// <param name="sections">The sections to place in the cloned node.</param>
    /// <returns>A cloned <see cref="CertificateStoreNode"/> with the provided sections.</returns>
    public CertificateStoreNode WithSections(IReadOnlyList<CertificateSection> sections) =>
        new(StoreLocation, StoreName, DisplayName, sections)
        {
            IsExpanded = IsExpanded
        };

    private static IReadOnlyList<CertificateDisplayRow> CreateRows(IReadOnlyList<CertificateSection> sections)
    {
        var rows = new List<CertificateDisplayRow>();

        foreach (CertificateSection section in sections.Where(section => section.HasEntries))
        {
            rows.Add(new CertificateDisplayRow(section.Title));
            rows.AddRange(section.Entries.Select(entry => new CertificateDisplayRow(entry)));
        }

        return rows;
    }
}
