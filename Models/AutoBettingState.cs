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

	// 업치기(파레이): N단계 당첨을 모두 마치면 일정베팅 단계로 전환(true). 미당첨/합산수익 리셋 시 false.
	public bool IsUpchigiFlatPhase { get; set; }

	// 업치기 사이클 순손익(마지막 합산수익 리셋 이후 누적) — 합산수익 임계 비교용.
	public decimal UpchigiCycleNet { get; set; }

	// 업치기 누적실패 카운터 — 미당첨마다 +1, 누적실패 임계 도달 시 시작금액 배수 상승 후 0으로 리셋.
	public int UpchigiFailAccum { get; set; }

	// 업치기 시작금액에 곱하는 현재 배수 — 누적실패 임계를 넘을 때마다 설정 배수만큼 누적 상승(합산수익 리셋 시 1로 원복).
	public decimal UpchigiStartMultiplier { get; set; } = 1m;

	public bool IsUnconfirmedBet { get; set; }

	public decimal PreBetBalance { get; set; }

	public bool IsLateConfirmed { get; set; }

	// ── 진행 표기용 '회차'(= 누적 미당첨 기준 시도 번호). 1부터 시작, 미당첨마다 +1, 배수상승 때 1로 리셋, 합산수익 리셋 때는 유지.
	//    당첨·타이·완주로는 올리지 않는다. 진행 그리드에 "회차-단계"(예: 10-2)로 표시.
	public int CycleIndex { get; set; } = 1;

	// ── 2차 판정(사후 접수 보정)용: 전송했으나 확인 미수신으로 '미접수'된 베팅을 기억해 둔다.
	//    같은 gameId 의 win/결과(접수 증거)가 오면 이 정보로 승격해 정상 판정한다.
	public string? ProvisionalGameId { get; set; }

	public decimal ProvisionalAmount { get; set; }

	public string? ProvisionalTarget { get; set; }

	public DateTime? ProvisionalTime { get; set; }

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

	// 업치기(파레이) 설정: 단계수 N, 시작금액(1단계), 일정금액(N단계 완료 후), 합산수익 리셋 임계.
	// 단계별 금액 = 시작금액 × 2^(단계-1) (마틴식 2배). 당첨 시 자동 다음 단계(대상 동일),
	// 미당첨 시 1단계 초기화, N단계 완료 후 일정금액 일반 베팅, 사이클 순손익 > 임계면 1단계+수익 리셋.
	public int UpchigiStepCount { get; set; } = 1;

	public decimal UpchigiStartAmount { get; set; }

	public decimal UpchigiFlatAmount { get; set; }

	public decimal UpchigiProfitResetThreshold { get; set; }

	// 찢어베팅 '사이트당 한도금액'(원). 분배 시 한 사이트가 이 금액을 넘지 못한다. 0 이하면 기본 500만 사용.
	public decimal PerSiteLimit { get; set; } = 5000000m;

	// 찢어베팅 '시작 금액'(원). 베팅금이 이 값 이상일 때만 분할, 미만이면 1사이트 단독. 0 이하면 기본 4만 사용.
	public decimal SplitThreshold { get; set; } = 40000m;

	// 누적실패(미당첨 횟수) 임계 — 한 방의 누적 미당첨이 이 값에 도달하면 그 방의 시작금액 배수를 올린다. 0=미사용.
	public int UpchigiFailEscalateThreshold { get; set; }

	// 누적실패 임계 도달 시 시작금액에 곱할 배수(누적 상승). 예: 2 → 임계마다 ×2씩 (×2,×4,×8…). 1 이하=미사용.
	public decimal UpchigiFailEscalateMultiplier { get; set; } = 1m;

	// 누적 상승한 배수의 상한 — 이 값을 넘지 않게 제한한다. 0 또는 1 이하=상한 없음.
	public decimal UpchigiFailEscalateMaxMultiplier { get; set; }

	// 업치기 전체(모든 방 합산) 순손익 — 마지막 합산수익 리셋 이후 모든 방의 당첨/미당첨을 합산한다.
	// 이 값이 합산수익을 넘으면(= 전체 손실을 다 메우고 합산수익만큼 더 벌면) 모든 방을 1단계로 초기화한다.
	public decimal UpchigiGlobalNet { get; set; }

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

	/// <summary>진행 표기용 '시도 회차'(1단 climb 을 새로 시작할 때마다 +1). 일직선 전략의 "회차-단계" 앞 번호.</summary>
	public int CycleIndex { get; set; }

	/// <summary>업치기 시작금액 배수(누적실패 상승 레벨). 1 초과면 진행 표기에 "×N" 꼬리표로 보인다.</summary>
	public decimal StartMultiplier { get; set; } = 1m;

	/// <summary>업치기 '누적 미당첨 수'(이 배수 레벨에서). 진행 표기에 "(미당첨/임계)"로 붙여 임계 도달을 눈으로 확인.</summary>
	public int FailAccum { get; set; }

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

	/// <summary>
	/// 전송은 했지만 확인 메시지를 끝내 못 받아 '미접수=실패'로 처리된 경우(명시적 거절이 아님).
	/// 이 경우에만 2차 판정(win/결과 증거로 사후 접수 보정)의 대상이 된다. 거절(반려)은 false.
	/// </summary>
	public bool IsTimeoutUnconfirmed { get; set; }

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

	/// <summary>
	/// 전송은 됐으나 확인 메시지 미수신으로 미접수 처리된 실패. 2차 판정(사후 접수 보정) 대상.
	/// </summary>
	public static BetResult FailTimeoutUnconfirmed(string error)
	{
		return new BetResult
		{
			IsSuccess = false,
			IsTimeoutUnconfirmed = true,
			ErrorMessage = error
		};
	}
}
