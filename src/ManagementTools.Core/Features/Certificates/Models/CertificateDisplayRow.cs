namespace ManagementTools.Core.Features.Certificates.Models;

/// <summary>
/// Represents a section heading or an actionable item rendered within a certificate store card.
/// </summary>
public sealed class CertificateDisplayRow
{
    /// <summary>
    /// Initializes a section heading row.
    /// </summary>
    /// <param name="sectionTitle">The localized section title.</param>
    public CertificateDisplayRow(string sectionTitle)
    {
        PrimaryText = sectionTitle;
        SecondaryText = string.Empty;
    }

    /// <summary>
    /// Initializes an actionable entry row.
    /// </summary>
    /// <param name="entry">The entry represented by the row.</param>
    public CertificateDisplayRow(CertificateEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Entry = entry;
        PrimaryText = entry.DisplayName;
        SecondaryText = entry.SecondaryText;
    }

    /// <summary>
    /// Gets the primary row label.
    /// </summary>
    public string PrimaryText { get; }

    /// <summary>
    /// Gets the secondary metadata text for an entry row.
    /// </summary>
    public string SecondaryText { get; }

    /// <summary>
    /// Gets the entry represented by this row, or <see langword="null"/> for section headings.
    /// </summary>
    public CertificateEntry? Entry { get; }

    /// <summary>
    /// Gets a value indicating whether this row is a section heading.
    /// </summary>
    public bool IsSection => Entry is null;

    /// <summary>
    /// Gets a value indicating whether this row represents an actionable entry.
    /// </summary>
    public bool IsEntry => Entry is not null;
}
