using System;
using System.Collections.Generic;

namespace PragmaticBot.Services;

public class SecondLogicEngine
{
	public enum Outcome
	{
		Win,
		Lose,
		Tie
	}

	public readonly record struct Transition(bool SwitchRoom, bool StepReset, string Reason);

	private readonly int _missStreakToSwitchRoom;

	private readonly int _winStreakToSwitchRoom;

	private readonly bool _useStreakRiding;

	public string? ActiveRoom { get; private set; }

	public int Step { get; private set; } = 1;

	public int ConsecutiveLosses { get; private set; }

	public int ConsecutiveWins { get; private set; }

	public bool IsRiding
	{
		get
		{
			if (_useStreakRiding)
			{
				return ConsecutiveWins > 0;
			}
			return false;
		}
	}

	public SecondLogicEngine(int missStreakToSwitchRoom, int winStreakToSwitchRoom = 1, bool useStreakRiding = false)
	{
		_missStreakToSwitchRoom = Math.Max(1, missStreakToSwitchRoom);
		_winStreakToSwitchRoom = Math.Max(1, winStreakToSwitchRoom);
		_useStreakRiding = useStreakRiding;
	}

	public static bool TryDetectPattern(IReadOnlyList<string> nonTieResults, out string? target)
	{
		target = null;
		int n = nonTieResults.Count;
		if (n < 3)
		{
			return false;
		}
		string y = nonTieResults[n - 1];
		string x1 = nonTieResults[n - 2];
		string x2 = nonTieResults[n - 3];
		if (x1 != x2)
		{
			return false;
		}
		if (y == x1)
		{
			return false;
		}
		if (n >= 4 && nonTieResults[n - 4] == x1)
		{
			return false;
		}
		target = x1;
		return true;
	}

	public static int CountBends(IReadOnlyList<string> nonTieResults)
	{
		int count = 0;
		for (int i = 2; i < nonTieResults.Count; i++)
		{
			string y = nonTieResults[i];
			string x1 = nonTieResults[i - 1];
			string x2 = nonTieResults[i - 2];
			if (!(x1 != x2) && !(y == x1) && (i < 3 || !(nonTieResults[i - 3] == x1)))
			{
				count++;
			}
		}
		return count;
	}

	public void SetActiveRoom(string room)
	{
		ActiveRoom = room;
		ConsecutiveLosses = 0;
		ConsecutiveWins = 0;
	}

	public void RenameActiveRoom(string oldRoom, string newRoom)
	{
		if (ActiveRoom == oldRoom)
		{
			ActiveRoom = newRoom;
		}
	}

	public void Reset()
	{
		ActiveRoom = null;
		Step = 1;
		ConsecutiveLosses = 0;
		ConsecutiveWins = 0;
	}

	public void ResetCycleKeepingRoom()
	{
		Step = 1;
		ConsecutiveLosses = 0;
		ConsecutiveWins = 0;
	}

	public void ClearActiveRoom()
	{
		ActiveRoom = null;
		ConsecutiveLosses = 0;
		ConsecutiveWins = 0;
	}

	public int NotifyInterimWin()
	{
		int consecutiveLosses = ConsecutiveLosses;
		ConsecutiveLosses = 0;
		return consecutiveLosses;
	}

	public Transition ApplyResult(Outcome outcome)
	{
		switch (outcome)
		{
		case Outcome.Win:
			Step = 1;
			ConsecutiveLosses = 0;
			ConsecutiveWins++;
			if (_useStreakRiding)
			{
				return new Transition(SwitchRoom: false, StepReset: true, $"줄타기 당첨({ConsecutiveWins}연승)→같은 방·같은 타겟 유지(단계 리셋)");
			}
			if (ConsecutiveWins >= _winStreakToSwitchRoom)
			{
				ConsecutiveWins = 0;
				ActiveRoom = null;
				return new Transition(SwitchRoom: true, StepReset: true, (_winStreakToSwitchRoom == 1) ? "당첨→다른 방(단계 리셋)" : $"{_winStreakToSwitchRoom}연속 당첨→다른 방(단계 리셋)");
			}
			return new Transition(SwitchRoom: false, StepReset: true, $"당첨({ConsecutiveWins}/{_winStreakToSwitchRoom})→같은 방(단계 리셋)");
		case Outcome.Lose:
		{
			bool wasRiding = IsRiding;
			ConsecutiveWins = 0;
			ConsecutiveLosses++;
			if (ConsecutiveLosses >= _missStreakToSwitchRoom)
			{
				Step++;
				ActiveRoom = null;
				return new Transition(SwitchRoom: true, StepReset: false, $"{ConsecutiveLosses}연속 미당첨→다른 방(단계 유지)" + (wasRiding ? " [줄타기 종료]" : ""));
			}
			Step++;
			return new Transition(SwitchRoom: false, StepReset: false, wasRiding ? $"줄타기 미당첨→줄타기 종료, 같은 방({ConsecutiveLosses}/{_missStreakToSwitchRoom}, 단계+1)" : $"미당첨→같은 방({ConsecutiveLosses}/{_missStreakToSwitchRoom}, 단계+1)");
		}
		default:
			return new Transition(SwitchRoom: false, StepReset: false, "타이(무)→상태 유지");
		}
	}
}
