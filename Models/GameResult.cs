using System;

namespace PragmaticBot.Models;

public class GameResult
{
	public string GameId { get; set; } = string.Empty;

	public string Winner { get; set; } = string.Empty;

	public string Score { get; set; } = string.Empty;

	public bool IsNatural { get; set; }

	public bool PlayerPair { get; set; }

	public bool BankerPair { get; set; }

	public string TableId { get; set; } = string.Empty;

	public DateTime ReceivedAt { get; set; } = DateTime.Now;
}
