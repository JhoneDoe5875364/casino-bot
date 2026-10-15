using System;
using System.ComponentModel;

namespace PragmaticBot.Models;

/// <summary>
/// One line of the 베팅 이력 grid on the main form.
/// <para>
/// A row is created when a bet goes out (status 베팅중) and is filled in again when the round is
/// judged, so the grid keeps the whole session rather than the single line the 비고 label shows.
/// </para>
/// </summary>
public class BetHistoryRow : INotifyPropertyChanged
{
	private string _status = "베팅중";

	private string _gameResult = "";

	private decimal _profit;

	public event PropertyChangedEventHandler? PropertyChanged;

	public DateTime Time { get; set; } = DateTime.Now;

	/// <summary>PP table id — kept for matching a settlement back to its bet, not displayed.</summary>
	public string TableId { get; set; } = string.Empty;

	/// <summary>PP game/round id — the other half of the match key.</summary>
	public string GameId { get; set; } = string.Empty;

	public string RoomName { get; set; } = string.Empty;

	public int Step { get; set; }

	/// <summary>그리드 "단계" 칸 표시용. 일직선 전략은 "회차-단계"(예: 102-2), 매트릭스는 "행-열". 비면 Step 숫자.</summary>
	public string StepDisplay { get; set; } = string.Empty;

	public string BetType { get; set; } = string.Empty;

	public decimal BetAmount { get; set; }

	/// <summary>True once the round has been judged; a pending row is the one a result may settle.</summary>
	public bool IsSettled { get; private set; }

	public string Status
	{
		get => _status;
		set => Set(ref _status, value, nameof(Status));
	}

	public string GameResult
	{
		get => _gameResult;
		set => Set(ref _gameResult, value, nameof(GameResult));
	}

	public decimal Profit
	{
		get => _profit;
		set => Set(ref _profit, value, nameof(Profit));
	}

	public void Settle(string status, string gameResult, decimal profit)
	{
		IsSettled = true;
		Status = status;
		GameResult = gameResult;
		Profit = profit;
	}

	private void Set<T>(ref T field, T value, string name)
	{
		if (!Equals(field, value))
		{
			field = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		}
	}
}
