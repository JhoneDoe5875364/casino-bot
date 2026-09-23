using System.Collections.Generic;

namespace PragmaticBot.Models;

public class StrategyContextSnapshot
{
	public decimal BaseAmount1326 { get; set; }

	public decimal OscarBaseAmount { get; set; }

	public int OscarTargetUnits { get; set; } = 1;

	public decimal FlatBetAmount { get; set; }

	public decimal LabouchereMultiplier { get; set; }

	public List<int> LabouchereInitialSequence { get; set; } = new List<int>();

	public decimal DAlembertBaseAmount { get; set; }

	public Dictionary<int, int> StepBettingMethods { get; set; } = new Dictionary<int, int>();
}
