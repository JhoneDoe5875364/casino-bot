using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace PragmaticBot.Models;

public class RoomBettingState
{
	public string TableId { get; set; } = string.Empty;

	public int CurrentStep { get; set; } = 1;

	public int StepVersion { get; set; }

	public string? LastBetGameId { get; set; }

	public string? LastBetType { get; set; }

	public decimal LastBetAmount { get; set; }

	public bool IsWaitingForResult { get; set; }

	public DateTime? LastBetTime { get; set; }

	public int LastProcessedHistoryCount { get; set; }

	public int BetVersion { get; set; }

	public bool IsRequestInFlight { get; set; }

	public DateTime? RequestStartTime { get; set; }

	public bool IsResetDeferred { get; set; }

	public bool IsDoubleUpPhase { get; set; }

	public decimal DoubleUpAmount { get; set; }

	public string? DoubleUpTarget { get; set; }

	public bool IsUnconfirmedBet { get; set; }

	public decimal PreBetBalance { get; set; }

	public bool IsLateConfirmed { get; set; }

	public bool IsShoeControlBlocked { get; set; }

	public bool IsGateOpen { get; set; }

	public DateTime? GateClosedAt { get; set; } = DateTime.Now;

	public DateTime? GateStaleWarnedAt { get; set; }

	public string? PbCrossTarget { get; set; }

	public bool PbCrossPhaseIsBp { get; set; }

	public int PbCrossLossStreak { get; set; }

	public bool IsIdleRound { get; set; }

	public decimal OscarSessionProfit { get; set; }

	public int OscarCurrentBetUnits { get; set; } = 1;

	public List<int> LabouchereSequence { get; set; } = new List<int>();

	public int ConsecutiveLossCount { get; set; }

	public decimal ChainStepLossAmount { get; set; }

	public bool IsDalmenzingRecoveryPhase { get; set; }

	public decimal DalmenzingDAlembertNetLoss { get; set; }

	public decimal DalmenzingRecoveryNetProfit { get; set; }

	public int DalmenzingRecoveryStep { get; set; } = 1;

	public bool DalmenzingRecoveryDoubleUpPhase { get; set; }

	public decimal DalmenzingRecoveryDoubleUpAmount { get; set; }

	public int MatrixCurrentRow { get; set; } = 1;

	public int MatrixCurrentCol { get; set; } = 1;

	public int MatrixRowWinCount { get; set; }

	public decimal MatrixCycleNet { get; set; }

	public decimal[] MatrixRowEntryNet { get; set; } = new decimal[15];
}
public class AutoBettingContext
{
	private readonly object _balanceLock = new object();

	private decimal _currentBalance;

	private decimal _peakBalance;

	private decimal _pendingBetAmount;

	private readonly object _keepAliveLock = new object();

	private decimal _keepAliveTotalAmount;

	private decimal _keepAliveReturnedAmount;

	private int _keepAliveBetCount;

	private int _keepAliveFailCount;

	private DateTime? _lastKeepAliveBetAt;

	private decimal _martinEntryBalance;

	private decimal _dAlembertBaseAmount;

	public static readonly int[] MatrixStepsPerRow = new int[15]
	{
		20, 20, 20, 20, 20, 20, 20, 20, 20, 20,
		20, 20, 20, 20, 20
	};

	public const int SwitchingMatrixBetAmountType = 13;

	public const int StairMatrixBetAmountType = 14;

	public bool IsRunning { get; set; }

	public int SelectedBetAmountType { get; set; }

	public ConcurrentDictionary<string, RoomBettingState> RoomStates { get; } = new ConcurrentDictionary<string, RoomBettingState>();

	public List<BettingPattern> Patterns { get; set; } = new List<BettingPattern>();

	public List<ExceptionPattern> ExceptionPatterns { get; set; } = new List<ExceptionPattern>();

	public bool IsPbCrossEnabled { get; set; }

	public bool IsBpCrossOnTwoLossEnabled { get; set; }

	public bool IsRoomGateEnabled { get; set; }

	public List<RoomGatePattern> RoomGateOnPatterns { get; set; } = new List<RoomGatePattern>();

	public List<RoomGatePattern> RoomGateOffPatterns { get; set; } = new List<RoomGatePattern>();

	public bool IsRoomGateActive
	{
		get
		{
			if (IsRoomGateEnabled)
			{
				if (RoomGateOnPatterns.Count <= 0)
				{
					return RoomGateOffPatterns.Count > 0;
				}
				return true;
			}
			return false;
		}
	}

	public Dictionary<int, decimal> BetAmounts { get; set; } = new Dictionary<int, decimal>();

	public Dictionary<int, int> StepBettingMethods { get; set; } = new Dictionary<int, int>();

	public Dictionary<string, int> TableIndexMap { get; set; } = new Dictionary<string, int>();

	public Dictionary<int, decimal> MartinBetAmounts { get; set; } = new Dictionary<int, decimal>();

	public Dictionary<int, decimal> HongKongBetAmounts { get; set; } = new Dictionary<int, decimal>();

	public Dictionary<int, int> HongKongWinTargetSteps { get; set; } = new Dictionary<int, int>();

	public decimal? UpperLimitPercent { get; set; }

	public bool IsUpperLimitEnabled { get; set; }

	public decimal? LowerLimitPercent { get; set; }

	public bool IsLowerLimitEnabled { get; set; }

	public int UpperLimitMode { get; set; }

	public decimal? TargetProfitAmount { get; set; }

	public bool IsTargetCycleEnabled { get; set; }

	public int? RestIntervalMinutes { get; set; }

	public int? SessionCount { get; set; }

	public decimal CycleStartBalance { get; set; }

	public int CompletedCycles { get; set; }

	public decimal SecondLogicCooldownBaseBalance { get; set; }

	public decimal StartBalance { get; set; }

	public decimal CurrentBalance
	{
		get
		{
			lock (_balanceLock)
			{
				return _currentBalance;
			}
		}
		set
		{
			lock (_balanceLock)
			{
				_currentBalance = value;
			}
		}
	}

	public decimal PeakBalance
	{
		get
		{
			lock (_balanceLock)
			{
				return _peakBalance;
			}
		}
		set
		{
			lock (_balanceLock)
			{
				_peakBalance = value;
			}
		}
	}

	public decimal TotalBetAmount { get; set; }

	public decimal PendingBetAmount
	{
		get
		{
			lock (_balanceLock)
			{
				return _pendingBetAmount;
			}
		}
		set
		{
			lock (_balanceLock)
			{
				_pendingBetAmount = value;
			}
		}
	}

	public decimal AvailableBalance
	{
		get
		{
			lock (_balanceLock)
			{
				return _currentBalance - _pendingBetAmount;
			}
		}
	}

	public string UserId { get; set; } = string.Empty;

	public string PpUserId { get; set; } = string.Empty;

	public bool IsPeakBalanceResetEnabled { get; set; }

	public bool IsVirtualMode { get; set; }

	public bool IsIgnorePendingBetEnabled { get; set; }

	public bool IsStepResetEnabled { get; set; }

	public int StepResetTargetStep { get; set; } = 1;

	public DateTime? LastGlobalBetTime { get; set; }

	public bool IsRefreshing { get; set; }

	public bool IsKeepAliveBetEnabled { get; set; } = true;

	public decimal KeepAliveBetAmount { get; set; } = 1000m;

	public int KeepAliveIntervalSeconds { get; set; } = 180;

	public string KeepAliveBetType { get; set; } = "Player";

	public decimal KeepAliveTotalAmount
	{
		get
		{
			lock (_keepAliveLock)
			{
				return _keepAliveTotalAmount;
			}
		}
	}

	public decimal KeepAliveReturnedAmount
	{
		get
		{
			lock (_keepAliveLock)
			{
				return _keepAliveReturnedAmount;
			}
		}
	}

	public decimal KeepAliveNetSpend
	{
		get
		{
			lock (_keepAliveLock)
			{
				return _keepAliveTotalAmount - _keepAliveReturnedAmount;
			}
		}
	}

	public int KeepAliveBetCount
	{
		get
		{
			lock (_keepAliveLock)
			{
				return _keepAliveBetCount;
			}
		}
	}

	public int KeepAliveFailCount
	{
		get
		{
			lock (_keepAliveLock)
			{
				return _keepAliveFailCount;
			}
		}
	}

	public DateTime? LastKeepAliveBetAt
	{
		get
		{
			lock (_keepAliveLock)
			{
				return _lastKeepAliveBetAt;
			}
		}
	}

	public bool IsDAlembertPhase { get; set; }

	public decimal MartinEntryBalance
	{
		get
		{
			lock (_balanceLock)
			{
				return _martinEntryBalance;
			}
		}
		set
		{
			lock (_balanceLock)
			{
				_martinEntryBalance = value;
			}
		}
	}

	public int MartinMaxStep { get; set; }

	public decimal DAlembertBaseAmount
	{
		get
		{
			lock (_balanceLock)
			{
				return _dAlembertBaseAmount;
			}
		}
		set
		{
			lock (_balanceLock)
			{
				_dAlembertBaseAmount = value;
			}
		}
	}

	public Dictionary<int, decimal> MartinBetAmountsBackup { get; set; } = new Dictionary<int, decimal>();

	public int DalmenzingMaxStep { get; set; }

	public int DalmenzingRecoveryMethod { get; set; }

	public decimal DalmenzingDAlembertBaseAmount { get; set; }

	public Dictionary<int, decimal> DalmenzingMartinAmounts { get; set; } = new Dictionary<int, decimal>();

	public Dictionary<int, decimal> DalmenzingHongKongAmounts { get; set; } = new Dictionary<int, decimal>();

	public bool IsMatrixStrategy => IsMatrixType(SelectedBetAmountType);

	public bool IsStairMatrixStrategy => SelectedBetAmountType == 14;

	public decimal[][] MatrixCells { get; set; } = Array.Empty<decimal[]>();

	public int[] MatrixDefinedMaxCols { get; set; } = new int[15];

	public Dictionary<int, (int WinCount, int TargetRow)> MatrixRowRules { get; set; } = new Dictionary<int, (int, int)>();

	public Dictionary<int, int> MatrixRowWinModes { get; set; } = new Dictionary<int, int>();

	public bool StairMatrixStepBackOnWin { get; set; }

	public Dictionary<int, int> MatrixRowRestMinutes { get; set; } = new Dictionary<int, int>();

	public decimal MatrixFirstRowCapAmount { get; set; }

	public decimal MatrixFirstRowCapBaseBalance { get; set; }

	public decimal BaseAmount1326 { get; set; }

	public decimal OscarBaseAmount { get; set; }

	public int OscarTargetUnits { get; set; } = 1;

	public decimal FlatBetAmount { get; set; }

	public decimal LabouchereMultiplier { get; set; }

	public List<int> LabouchereInitialSequence { get; set; } = new List<int>();

	public int? MaxActiveBettingRooms { get; set; }

	public bool IsChainMode { get; set; }

	public List<ChainStepDefinition> ChainSteps { get; set; } = new List<ChainStepDefinition>();

	public int CurrentChainStepIndex { get; set; }

	public int ChainCompletionAction { get; set; }

	public Dictionary<int, Dictionary<int, decimal>> AllChainBetAmounts { get; set; } = new Dictionary<int, Dictionary<int, decimal>>();

	public Dictionary<int, StrategyContextSnapshot> AllChainContexts { get; set; } = new Dictionary<int, StrategyContextSnapshot>();

	public decimal AddToCurrentBalance(decimal delta)
	{
		lock (_balanceLock)
		{
			_currentBalance += delta;
			return _currentBalance;
		}
	}

	public decimal SubtractFromCurrentBalance(decimal amount)
	{
		lock (_balanceLock)
		{
			_currentBalance -= amount;
			return _currentBalance;
		}
	}

	public void RecordKeepAliveBet(decimal amount, bool isSuccess)
	{
		lock (_keepAliveLock)
		{
			if (isSuccess)
			{
				_keepAliveTotalAmount += amount;
				_keepAliveBetCount++;
				_lastKeepAliveBetAt = DateTime.Now;
			}
			else
			{
				_keepAliveFailCount++;
			}
		}
	}

	public void RecordKeepAliveReturn(decimal amount)
	{
		if (amount <= 0m)
		{
			return;
		}
		lock (_keepAliveLock)
		{
			_keepAliveReturnedAmount += amount;
		}
	}

	public void ResetKeepAliveStats()
	{
		lock (_keepAliveLock)
		{
			_keepAliveTotalAmount = 0m;
			_keepAliveReturnedAmount = 0m;
			_keepAliveBetCount = 0;
			_keepAliveFailCount = 0;
			_lastKeepAliveBetAt = null;
		}
	}

	public static bool IsMatrixType(int betAmountType)
	{
		if ((uint)(betAmountType - 11) <= 3u)
		{
			return true;
		}
		return false;
	}

	public static bool UsesMatrixRowRules(int betAmountType)
	{
		if (IsMatrixType(betAmountType))
		{
			return betAmountType != 14;
		}
		return false;
	}

	public static bool SupportsMatrixRest(int betAmountType)
	{
		if ((uint)(betAmountType - 13) <= 1u)
		{
			return true;
		}
		return false;
	}

	public int GetMatrixRestMinutes(int row)
	{
		if (!SupportsMatrixRest(SelectedBetAmountType))
		{
			return 0;
		}
		return MatrixRowRestMinutes.GetValueOrDefault(row);
	}

	public bool ShouldStepBackOnWin(int row)
	{
		return SelectedBetAmountType switch
		{
			12 => true, 
			13 => MatrixRowWinModes.GetValueOrDefault(row) == 1, 
			14 => StairMatrixStepBackOnWin, 
			_ => false, 
		};
	}
}
public class AutoBettingStateChangedEventArgs : EventArgs
{
	public bool IsRunning { get; set; }

	public string Message { get; set; } = string.Empty;

	public string? StopReason { get; set; }
}
public class AutoBettingExecutedEventArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public string GameId { get; set; } = string.Empty;

	public string BetType { get; set; } = string.Empty;

	public decimal Amount { get; set; }

	public int Step { get; set; }

	public int StepVersion { get; set; }

	public string MatchedPattern { get; set; } = string.Empty;

	public bool IsSuccess { get; set; }

	public string? ErrorMessage { get; set; }

	public bool IsIdleRound { get; set; }

	public int MatrixRow { get; set; }

	public int MatrixCol { get; set; }

	public string? SiteSplitRemark { get; set; }
}
public class AutoBettingResultEventArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public string GameId { get; set; } = string.Empty;

	public bool IsWin { get; set; }

	public string BetType { get; set; } = string.Empty;

	public string GameResult { get; set; } = string.Empty;

	public decimal BetAmount { get; set; }

	public bool IsIdleRound { get; set; }

	public int NextStep { get; set; }

	public int StepVersion { get; set; }

	public int NextMatrixRow { get; set; }

	public int NextMatrixCol { get; set; }

	public bool IsSecondLogicRoomSwitched { get; set; }

	public bool IsPeakLoweredSilently { get; set; }
}
public class PeakResetEventArgs : EventArgs
{
	public decimal NewPeakBalance { get; set; }

	public int PauseDurationSeconds { get; set; }

	public int GlobalStepVersion { get; set; }
}
public class ProfitCooldownEventArgs : EventArgs
{
	public decimal ProfitAmount { get; set; }

	public decimal BaseBalance { get; set; }

	public int CooldownMinutes { get; set; }

	public int GlobalStepVersion { get; set; }
}
public class ProfitCooldownEndedEventArgs : EventArgs
{
	public decimal NewBaseBalance { get; set; }
}
public class InactivityRefreshEventArgs : EventArgs
{
	public TimeSpan InactiveDuration { get; set; }

	public int RefreshWaitSeconds { get; set; }
}
public class KeepAliveBetEventArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public string GameId { get; set; } = string.Empty;

	public string BetType { get; set; } = string.Empty;

	public decimal Amount { get; set; }

	public bool IsSuccess { get; set; }

	public bool IsUnconfirmed { get; set; }

	public string? ErrorMessage { get; set; }

	public int TotalCount { get; set; }

	public decimal TotalAmount { get; set; }

	public decimal NetSpend { get; set; }
}
public class RoomGateChangedEventArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public bool IsOpen { get; set; }

	public string Reason { get; set; } = string.Empty;
}
public class BetResult
{
	public bool IsSuccess { get; set; }

	public bool IsUnconfirmed { get; set; }

	public string? ErrorMessage { get; set; }

	public string? SentMessage { get; set; }

	/// <summary>
	/// 실제로 페이지에 걸린 금액(스팟에서 읽은 값). null 이면 "모름 → 의도액 그대로 사용".
	/// DOM 베팅에서 클릭 일부가 누락되면 의도액과 실제 걸린 금액이 달라지는데(예: 80,000 의도 → 70,000 실제),
	/// 이 값이 있으면 상위 회계(단계금액/누적/손익)를 실제 금액 기준으로 보정한다.
	/// </summary>
	public decimal? ActualAmount { get; set; }

	public static BetResult Ok(string sentMessage, decimal? actualAmount = null)
	{
		return new BetResult
		{
			IsSuccess = true,
			SentMessage = sentMessage,
			ActualAmount = actualAmount
		};
	}

	public static BetResult Unconfirmed(string sentMessage)
	{
		return new BetResult
		{
			IsSuccess = true,
			IsUnconfirmed = true,
			SentMessage = sentMessage
		};
	}

	public static BetResult Fail(string error)
	{
		return new BetResult
		{
			IsSuccess = false,
			ErrorMessage = error
		};
	}
}
