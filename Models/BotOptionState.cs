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

	/// <summary>찢어베팅 '사이트당 한도금액'(원). 한 사이트에 이 이상 못 걸고, 넘으면 다음 사이트로 분할한다.</summary>
	public decimal PerSiteLimit { get; set; } = 5000000m;

	/// <summary>찢어베팅 '시작 금액'(원). 베팅금이 이 값 이상일 때만 분할하고, 미만이면 1사이트 단독으로 건다.</summary>
	public decimal SplitThreshold { get; set; } = 40000m;

	public BotOptionState Clone()
	{
		return (BotOptionState)MemberwiseClone();
	}
}
