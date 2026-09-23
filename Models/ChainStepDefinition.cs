namespace PragmaticBot.Models;

public class ChainStepDefinition
{
	public int StrategyType { get; set; }

	public int? MaxStepTransition { get; set; }

	public int? ConsecutiveLossTransition { get; set; }

	public decimal? LossAmountTransition { get; set; }

	public bool StrategyCompleteTransition { get; set; }
}
