using System;

namespace PragmaticBot.Models;

public class RoomCooldownState
{
	public string TableId { get; set; } = string.Empty;

	public int ConsecutiveLossCount { get; set; }

	public DateTime? CooldownUntilUtc { get; set; }

	public int CooldownHitCount { get; set; }

	public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}
public class CooldownCheck
{
	public bool IsBlocked { get; set; }

	public int RemainingSeconds { get; set; }

	public int ConsecutiveLossCount { get; set; }
}
public class RoomCooldownSettings
{
	public bool IsEnabled { get; set; }

	public int LossThreshold { get; set; } = 5;

	public int CooldownMinutes { get; set; } = 3;

	public static RoomCooldownSettings Disabled()
	{
		return new RoomCooldownSettings
		{
			IsEnabled = false,
			LossThreshold = 5,
			CooldownMinutes = 3
		};
	}
}
public class RoomCooldownChangedEventArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public int ConsecutiveLossCount { get; set; }

	public bool IsInCooldown { get; set; }

	public int RemainingSeconds { get; set; }
}
