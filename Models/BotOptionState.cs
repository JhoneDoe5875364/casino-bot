namespace PragmaticBot.Models;

public sealed class BotOptionState
{
	public bool PeakReset { get; set; }

	public bool StepReset { get; set; }

	public int StepResetIndex { get; set; } = -1;

	public bool RoomGate { get; set; }

	public bool VirtualBet { get; set; }

	public bool IgnorePendingBet { get; set; }

	public bool ShoeControl { get; set; }

	public int ShoeControlIndex { get; set; } = -1;

	public bool StepAlarm { get; set; }

	public int StepAlarmIndex { get; set; } = -1;

	public int SplitCount { get; set; } = 1;

	public BotOptionState Clone()
	{
		return (BotOptionState)MemberwiseClone();
	}
}
