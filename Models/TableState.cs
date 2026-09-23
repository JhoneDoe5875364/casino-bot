using System;
using System.Collections.Generic;

namespace PragmaticBot.Models;

public class CardInfo
{
	public string CardCode { get; set; } = string.Empty;

	public string Rank { get; set; } = string.Empty;

	public string Suit { get; set; } = string.Empty;

	public string Place { get; set; } = string.Empty;

	public string SuitDisplay => Suit switch
	{
		"C" => "♣", 
		"D" => "♦", 
		"H" => "♥", 
		"S" => "♠", 
		_ => Suit, 
	};

	public string DisplayText => Rank + SuitDisplay;

	/// <summary>
	/// Baccarat value of this card: ace 1, pips at face value, 10·J·Q·K zero.
	/// </summary>
	public int Points
	{
		get
		{
			string r = (Rank ?? string.Empty).Trim().ToUpperInvariant();
			if (r.Length == 0)
			{
				return 0;
			}
			switch (r)
			{
			case "A":
				return 1;
			case "10":
			case "T":
			case "J":
			case "Q":
			case "K":
			case "0":
				return 0;
			}
			return int.TryParse(r, out int n) && n >= 1 && n <= 9 ? n : 0;
		}
	}
}
public class LiveScoreState
{
	public List<CardInfo> PlayerCards { get; set; } = new List<CardInfo>();

	public List<CardInfo> BankerCards { get; set; } = new List<CardInfo>();

	public int PlayerScore { get; set; }

	public int BankerScore { get; set; }

	public int TotalCardCount { get; set; }

	/// <summary>
	/// Recomputes both totals from the cards on the table. Baccarat drops the tens digit, and the
	/// providers' own running-total field cannot be trusted — Pragmatic's produced values such as 43
	/// for a three-card hand, which is not a possible baccarat score.
	/// </summary>
	public void RecomputeScores()
	{
		PlayerScore = SumOf(PlayerCards);
		BankerScore = SumOf(BankerCards);
	}

	private static int SumOf(List<CardInfo> cards)
	{
		int total = 0;
		foreach (CardInfo c in cards)
		{
			total += c.Points;
		}
		return total % 10;
	}

	/// <summary>A score a provider sent is only believable if it is a real baccarat total.</summary>
	public static bool IsPlausibleScore(int score) => score >= 0 && score <= 9;

	public void Reset()
	{
		PlayerCards.Clear();
		BankerCards.Clear();
		PlayerScore = 0;
		BankerScore = 0;
		TotalCardCount = 0;
	}
}
public class TableState
{
	private string _gameId = string.Empty;

	public string TableId { get; set; } = string.Empty;

	public string GameId
	{
		get
		{
			return _gameId;
		}
		set
		{
			string next = value ?? string.Empty;
			if (!string.Equals(_gameId, next, StringComparison.Ordinal))
			{
				if (_gameId.Length > 0)
				{
					PreviousGameId = _gameId;
				}
				_gameId = next;
			}
		}
	}

	public string PreviousGameId { get; set; } = string.Empty;

	public string GameStartTime { get; set; } = string.Empty;

	public BettingPhase Phase { get; set; }

	public int TimerSeconds { get; set; }

	public DateTime? TimerReceivedAt { get; set; }

	public DateTime LastUpdated { get; set; } = DateTime.Now;

	public bool IsBettingOpen
	{
		get
		{
			if (Phase != BettingPhase.BetsOpen)
			{
				return Phase == BettingPhase.BetsClosingSoon;
			}
			return true;
		}
	}

	public double RemainingTimeMs
	{
		get
		{
			if (!TimerReceivedAt.HasValue || TimerSeconds <= 0)
			{
				return 0.0;
			}
			double elapsed = (DateTime.Now - TimerReceivedAt.Value).TotalMilliseconds;
			return Math.Max(0.0, (double)TimerSeconds * 1000.0 - elapsed);
		}
	}

	public string DisplayName { get; set; } = string.Empty;

	/// <summary>Which live-casino provider runs this table; decides the wire format of a bet.</summary>
	public string Provider { get; set; } = PragmaticBot.Services.GameProviderNames.PragmaticPlayKey;

	public LiveScoreState LiveScore { get; set; } = new LiveScoreState();

	public void ResetRoundTracking()
	{
		PreviousGameId = string.Empty;
	}
}
