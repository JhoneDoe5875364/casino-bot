using System.Collections.Generic;

namespace PragmaticBot.Models;

public class RoomGatePattern
{
	public string RawText { get; set; } = string.Empty;

	public List<string> Sequence { get; set; } = new List<string>();

	public bool IsOn { get; set; }

	public bool IsValid { get; set; }

	public int Number { get; set; }

	public string? ErrorMessage { get; set; }

	public string DisplayName => $"{(IsOn ? "On" : "Off")}#{Number} '{RawText}'";
}
