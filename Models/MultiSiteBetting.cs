namespace PragmaticBot.Models;

public class BettingSite
{
	public int SiteSlot { get; set; }

	public string Domain { get; set; } = string.Empty;

	public string BrowserType { get; set; } = "Chrome";

	public int DebugPort { get; set; } = 9222;

	public int WsServerPort { get; set; } = 29876;

	public string? SiteUrl { get; set; }

	public bool IsActive { get; set; } = true;

	public string? BalanceSelector { get; set; }
}
public class SiteBetSplit
{
	public int SiteSlot { get; set; }

	public string Domain { get; set; } = string.Empty;

	public decimal Amount { get; set; }
}
