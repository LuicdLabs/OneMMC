using System;

namespace OneMMC.Core.Features.SystemManagement.Models.ComExp;

public sealed class ComPlusApplicationInfo
{
	public string Name { get; set; } = string.Empty;
	public string? Id { get; set; }
	public string? Description { get; set; }
	public string? Activation { get; set; }
	public string? AuthenticationLevel { get; set; }
	public string? AccessChecksLevel { get; set; }
	public string? Identity { get; set; }
	public string Summary { get; set; } = string.Empty;
}


