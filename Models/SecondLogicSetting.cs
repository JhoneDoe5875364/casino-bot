namespace PragmaticBot.Models;

public class SecondLogicSetting
{
	public bool IsEnabled { get; set; }

	public int MissStreakToSwitchRoom { get; set; } = 5;

	public int WinStreakToSwitchRoom { get; set; } = 1;

	public int BendCountToBet { get; set; } = 1;

	public int BetAmountType { get; set; }

	public int DAlembertMaxStep { get; set; }

	public bool UseMartinDalembertCombo { get; set; }

	public bool UseStreakRiding { get; set; }

	public bool UseProfitCooldown { get; set; }

	public decimal CooldownProfitAmount { get; set; } = 1000000m;

	public int CooldownMinutes { get; set; } = 10;

	public static SecondLogicSetting Default()
	{
		return new SecondLogicSetting();
	}
}
