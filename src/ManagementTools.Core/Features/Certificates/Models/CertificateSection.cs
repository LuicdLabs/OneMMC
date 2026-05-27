namespace ManagementTools.Core.Features.Certificates.Models;

/// <summary>
/// Represents one logical section within a certificate store card.
/// </summary>
public sealed class CertificateSection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateSection"/> class.
    /// </summary>
    /// <param name="kind">The entry kind represented by the section.</param>
    /// <param name="title">The section title.</param>
    /// <param name="emptyMessage">The empty-state message shown when the section has no entries.</param>
    /// <param name="entries">The entries contained in the section.</param>
    public CertificateSection(
        CertificateEntryKind kind,
        string title,
        string emptyMessage,
        IReadOnlyList<CertificateEntry> entries)
    {
        Kind = kind;
        Title = title;
        EmptyMessage = emptyMessage;
        Entries = entries;
    }

    /// <summary>
    /// Gets the section kind.
    /// </summary>
    public CertificateEntryKind Kind { get; }

    /// <summary>
    /// Gets the section title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the message displayed when the section is empty.
    /// </summary>
    public string EmptyMessage { get; }

    /// <summary>
    /// Gets the entries displayed in the section.
    /// </summary>
    public IReadOnlyList<CertificateEntry> Entries { get; }

    /// <summary>
    /// Gets the display title that includes the current item count.
    /// </summary>
    public string DisplayTitle => $"{Title} ({Entries.Count})";

    /// <summary>
    /// Gets a value indicating whether the section has at least one entry.
    /// </summary>
    public bool HasEntries => Entries.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the section is empty.
    /// </summary>
    public bool IsEmpty => !HasEntries;

    /// <summary>
    /// Creates a new section instance that reuses the current metadata with a different entry list.
    /// </summary>
    /// <param name="entries">The entries to display in the cloned section.</param>
    /// <returns>A new <see cref="CertificateSection"/> instance.</returns>
    public CertificateSection WithEntries(IReadOnlyList<CertificateEntry> entries) =>
        new(Kind, Title, EmptyMessage, entries);
}
