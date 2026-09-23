using System.Collections.Generic;

namespace PragmaticBot.Models;

public class BettingPattern
{
	public string RawText { get; set; } = string.Empty;

	public List<string> Sequence { get; set; } = new List<string>();

	public string BetTarget { get; set; } = string.Empty;

	public bool IsValid { get; set; }

	public string? ErrorMessage { get; set; }

	public int Number { get; set; }
}
