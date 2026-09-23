namespace PragmaticBot.Models;

public enum BettingPhase
{
	Idle,
	GameStarted,
	BetsOpen,
	BetsClosingSoon,
	BetsClosed,
	Dealing,
	Result
}
