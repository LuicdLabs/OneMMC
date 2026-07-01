namespace OneMMC.Core.Features.PrintManagement.Models.PrintManagement;

/// <summary>
/// Represents information about a print form (paper size/type) on the system.
/// </summary>
public class PrintFormInfo
{
    /// <summary>Name of the form (e.g., "A4", "Letter")</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Type of the form (e.g., "Built-in", "Printer", "User-defined")</summary>
    public string FormType { get; set; } = string.Empty;

    /// <summary>Printable width in units of 0.1 mm</summary>
    public int PrintableWidth { get; set; }

    /// <summary>Printable height in units of 0.1 mm</summary>
    public int PrintableHeight { get; set; }
}


