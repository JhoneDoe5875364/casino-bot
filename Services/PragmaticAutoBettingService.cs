using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using PragmaticBot.Models;
using PragmaticBot.Models.Api;

namespace PragmaticBot.Services;

public class PragmaticAutoBettingService
{
	private readonly record struct MatrixCarry(int Row, int Col, int RowWinCount, decimal CycleNet, decimal[] RowEntryNet);

	private sealed record KeepAlivePendingBet(string TableId, string GameId, string BetType, decimal Amount, DateTime SentAt);

	private sealed class SplitBetOutcome
	{
		public bool AnySuccess { get; init; }

		public bool AllUnconfirmed { get; init; }

		public decimal ActualAmount { get; init; }

		public string? SiteSplitRemark { get; init; }

		public bool StopRequested { get; init; }

		public string? StopReason { get; init; }
	}

	private readonly PragmaticBettingService _bettingService;

	private readonly PatternParsingService _patternParser;

	private readonly RoomCooldownService _cooldownService;

	private readonly object _lock = new object();

	private ConcurrentDictionary<string, TableState>? _tableStates;

	private ConcurrentDictionary<string, List<GameResult>>? _gameHistories;

	private volatile HashSet<string> _selectedTables = new HashSet<string>();

	private Func<string, string?>? _resolveTableKey;

	private readonly ConcurrentDictionary<string, byte> _disabledRooms = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

	private string? _slPendingReleaseRoom;

	private readonly BetAllocationService _betAllocationService = new BetAllocationService();

	private IReadOnlyList<BettingSite>? _multiSites;

	private IReadOnlyDictionary<int, PragmaticBettingService>? _bettingServicesBySlot;

	private IReadOnlyDictionary<int, ConcurrentDictionary<string, TableState>>? _tableStatesBySlot;

	private IReadOnlyDictionary<int, Func<decimal?>>? _balanceGettersBySlot;

	private IReadOnlyDictionary<int, Func<string>>? _ppUserIdGettersBySlot;

	private Func<decimal>? _getMasterBalance;

	private Func<int>? _getSplitCount;

	private Action<int, string>? _requestRoomRecovery;

	private SecondLogicEngine? _secondLogic;

	private string? _slLastRoom;

	private int? _slCarryStep;

	private MatrixCarry? _slCarryMatrix;

	private string? _slCarryPbCross;

	private int _slCarryPbCrossLossStreak;

	private bool _slCarryPbCrossPhaseIsBp;

	private const int BpCrossLossStreakThreshold = 2;

	private bool _slUseCombo;

	private int _slDAlembertMaxStep;

	private decimal _slComboUnit;

	private bool _slUseStreakRiding;

	private string? _slRidingTarget;

	private int _slSplitYieldCount;

	private bool _slUseProfitCooldown;

	private decimal _slCooldownProfitAmount;

	private int _slCooldownMinutes;

	private volatile bool _isProfitCoolingDown;

	private CancellationTokenSource? _profitCooldownCts;

	private const int SecondLogicSplitYieldLimit = 3;

	private const int RoomStaleSeconds = 120;

	private volatile bool _isPaused;

	private long _pauseStartTimeTicks;

	private const int PeakResetPauseSeconds = 15;

	private const int MaxPauseDurationSeconds = 45;

	private volatile bool _isCycleResting;

	private CancellationTokenSource? _cycleRestCts;

	private volatile bool _isMatrixResting;

	private CancellationTokenSource? _matrixRestCts;

	private System.Timers.Timer? _inactivityTimer;

	private int _inactivityTimerRunning;

	// [진단] 무베팅이 길어지는데 복구 체크가 쉬는-플래그 때문에 스킵되는 상황을 드러내는 로그의 스로틀.
	private long _lastStallDiagTicks;

	private const int InactivityCheckIntervalMs = 30000;

	private const int InactivityTimeoutSeconds = 300;

	private const int RefreshWaitSeconds = 10;

	private System.Timers.Timer? _keepAliveTimer;

	private int _keepAliveTickRunning;

	private int _keepAliveRotationIndex;

	private DateTime _keepAliveNextDueAt = DateTime.MinValue;

	private KeepAlivePendingBet? _keepAlivePending;

	private int _keepAliveConsecutiveFails;

	private long _lastRealBetSentTicks;

	private const int KeepAliveTickIntervalMs = 3000;

	private const int KeepAliveRetryDelaySeconds = 20;

	private const int KeepAliveMaxRetryDelaySeconds = 120;

	private const int KeepAliveMinRemainingMs = 4000;

	private const int KeepAlivePendingExpiryMinutes = 5;

	private const int KeepAliveBusyDelaySeconds = 5;

	// 마감 몇 ms 전에 베팅 처리를 시작할지. 여러 방이 겹칠 때 순서대로 처리할 여유를 준다.
	private const int BettingTargetMs = 10000;

	// 패턴 적중 시 '즉시' 베팅: 창이 열리면 직전 결과 반영 여유(이 버퍼)만 두고 바로 베팅한다.
	// 예전엔 마감 BettingTargetMs(10초) 전까지 기다려 칩이 마감 직전에 올라가고 실패가 잦았다.
	// 이제 창 초반에 베팅하므로 전송·확인에 쓸 여유가 최대가 된다. (직전 결과 동기화는 ExecuteTimer의 방게이트 대기가 보강)
	private const int BettingOpenSettleMs = 600;

	private const int HistorySyncDeadlineMs = 3000;

	private const int HistorySyncPollMs = 200;

	private const int HistorySyncLookback = 5;

	private const int RequestInFlightTimeoutSeconds = 15;

	private const int StaleBetResultMinutes = 3;

	private readonly ConcurrentDictionary<string, CancellationTokenSource> _bettingCts = new ConcurrentDictionary<string, CancellationTokenSource>();

	private readonly ConcurrentDictionary<string, string> _resetBlockedRounds = new ConcurrentDictionary<string, string>();

	public const int RoomGateStaleMinutes = 45;

	private const int RoomGateLoadAttempts = 3;

	private const int RoomGateLoadRetryDelayMs = 700;

	private string? _roomGateLoadError;

	private bool _roomGateFormatsLoaded;

	private bool IsStreakRidingTargetActive
	{
		get
		{
			if (_slUseStreakRiding)
			{
				return !Context.IsPbCrossEnabled;
			}
			return false;
		}
	}

	private bool IsSecondLogic => _secondLogic != null;

	private bool IsSecondLogicMatrix
	{
		get
		{
			if (IsSecondLogic)
			{
				return Context.IsMatrixStrategy;
			}
			return false;
		}
	}

	private bool IsSecondLogicHongKong
	{
		get
		{
			if (IsSecondLogic)
			{
				return Context.SelectedBetAmountType == 1;
			}
			return false;
		}
	}

	private bool IsSecondLogicDAlembert
	{
		get
		{
			if (IsSecondLogic)
			{
				return Context.SelectedBetAmountType == 4;
			}
			return false;
		}
	}

	private int SecondLogicDAlembertMaxStep
	{
		get
		{
			if (!IsSecondLogicDAlembert || _slDAlembertMaxStep <= 0)
			{
				return 0;
			}
			return _slDAlembertMaxStep;
		}
	}

	public bool IsRunning => Context.IsRunning;

	public bool IsPaused => _isPaused;

	public bool IsProfitCoolingDown => _isProfitCoolingDown;

	public bool IsMatrixResting => _isMatrixResting;

	public AutoBettingContext Context { get; } = new AutoBettingContext();

	public RoomCooldownService CooldownService => _cooldownService;

	private bool IsMultiSiteConfigured
	{
		get
		{
			IReadOnlyList<BettingSite> multiSites = _multiSites;
			if (multiSites != null && multiSites.Count > 0)
			{
				IReadOnlyDictionary<int, PragmaticBettingService> bettingServicesBySlot = _bettingServicesBySlot;
				if (bettingServicesBySlot != null && bettingServicesBySlot.Count > 0)
				{
					return (_getSplitCount?.Invoke() ?? 1) > 1;
				}
			}
			return false;
		}
	}

	public int DisabledRoomCount => _disabledRooms.Count;

	public string? RoomGateUnavailableReason
	{
		get
		{
			if (Context.IsRoomGateEnabled && _roomGateFormatsLoaded)
			{
				if (_roomGateLoadError == null)
				{
					if (Context.IsRoomGateActive)
					{
						return null;
					}
					return "유효한 서식이 하나도 없습니다 — 방On/Off서식에서 p/b로 입력해 주세요.";
				}
				return "서식을 불러오지 못했습니다 (" + _roomGateLoadError + ")";
			}
			return null;
		}
	}

	public event EventHandler<AutoBettingStateChangedEventArgs>? StateChanged;

	public event EventHandler<AutoBettingExecutedEventArgs>? BetExecuted;

	public event EventHandler<AutoBettingResultEventArgs>? ResultJudged;

	public event EventHandler<PeakResetEventArgs>? PeakBalanceReset;

	public event EventHandler<ProfitCooldownEventArgs>? ProfitCooldownStarted;

	public event EventHandler<ProfitCooldownEndedEventArgs>? ProfitCooldownEnded;

	public event EventHandler<InactivityRefreshEventArgs>? InactivityRefreshStarted;

	public event EventHandler? RefreshCompleted;

	public event Action<decimal>? PendingBetAmountChanged;

	public event EventHandler<KeepAliveBetEventArgs>? KeepAliveBetExecuted;

	public event EventHandler<RoomGateChangedEventArgs>? RoomGateChanged;

	private static decimal ComboMultiplier(int step)
	{
		if (step <= 4)
		{
			return (decimal)Math.Pow(2.0, step - 1);
		}
		return (decimal)Math.Pow(2.0, 3.0) + (decimal)(step - 4);
	}

	public PragmaticAutoBettingService(PragmaticBettingService bettingService)
	{
		_bettingService = bettingService;
		_patternParser = new PatternParsingService();
		_cooldownService = new RoomCooldownService();
	}

	private static ApiClient GetApi()
	{
		return AuthService.Api ?? throw new InvalidOperationException("로그인이 필요합니다.");
	}

	public void SetDataReferences(ConcurrentDictionary<string, TableState> tableStates, ConcurrentDictionary<string, List<GameResult>> gameHistories)
	{
		_tableStates = tableStates;
		_gameHistories = gameHistories;
	}

	public void SetMultiSiteReferences(IReadOnlyDictionary<int, PragmaticBettingService> bettingServicesBySlot, IReadOnlyDictionary<int, ConcurrentDictionary<string, TableState>> tableStatesBySlot, IReadOnlyDictionary<int, Func<decimal?>> balanceGettersBySlot, IReadOnlyDictionary<int, Func<string>> ppUserIdGettersBySlot, Func<decimal> getMasterBalance, Func<int> getSplitCount, IReadOnlyList<BettingSite> sites, Action<int, string>? requestRoomRecovery = null)
	{
		_requestRoomRecovery = requestRoomRecovery;
		_bettingServicesBySlot = bettingServicesBySlot;
		_tableStatesBySlot = tableStatesBySlot;
		_balanceGettersBySlot = balanceGettersBySlot;
		_ppUserIdGettersBySlot = ppUserIdGettersBySlot;
		_getMasterBalance = getMasterBalance;
		_getSplitCount = getSplitCount;
		_multiSites = sites;
		FileLogger.Log($"[AutoBetting] 멀티 사이트 참조 설정됨: {sites.Count}개 슬롯 ({string.Join(", ", sites.Select((BettingSite s) => $"슬롯{s.SiteSlot}/{(string.IsNullOrEmpty(s.Domain) ? "미설정" : s.Domain)}"))})", "Info", "MultiSite");
	}

	public void SetSelectedTables(HashSet<string> selectedTableIds)
	{
		_selectedTables = selectedTableIds;
		FileLogger.Log($"[AutoBetting] 선택 테이블 설정: {selectedTableIds.Count}개 - {string.Join(", ", selectedTableIds)}");
	}

	public void SetTableKeyResolver(Func<string, string?> resolver)
	{
		_resolveTableKey = resolver;
	}

	private bool IsSelectedTable(string tableId)
	{
		if (_selectedTables.Contains(tableId))
		{
			return true;
		}
		string resolved = _resolveTableKey?.Invoke(tableId);
		if (resolved != null)
		{
			return _selectedTables.Contains(resolved);
		}
		return false;
	}

	public bool IsRoomBettingEnabled(string tableId)
	{
		if (_disabledRooms.IsEmpty)
		{
			return true;
		}
		if (_disabledRooms.ContainsKey(tableId))
		{
			return false;
		}
		string resolved = _resolveTableKey?.Invoke(tableId);
		if (resolved != null)
		{
			return !_disabledRooms.ContainsKey(resolved);
		}
		return true;
	}

	public void SetRoomBettingEnabled(string tableId, bool enabled)
	{
		if (string.IsNullOrEmpty(tableId))
		{
			return;
		}
		if (enabled)
		{
			if (!_disabledRooms.TryRemove(tableId, out var _))
			{
				return;
			}
			lock (_lock)
			{
				if (_slPendingReleaseRoom == tableId)
				{
					_slPendingReleaseRoom = null;
				}
			}
			int step = ((!Context.RoomStates.TryGetValue(tableId, out RoomBettingState rs)) ? 1 : rs.CurrentStep);
			FileLogger.Log($"[방스위치] {tableId}: ON — 베팅 재개 (단계={step} 유지)", "Info", "RoomSwitch");
		}
		else if (_disabledRooms.TryAdd(tableId, 0))
		{
			FileLogger.Log("[방스위치] " + tableId + ": OFF — 신규 진입 중단 (진행 중 베팅은 정산까지 유지)", "Info", "RoomSwitch");
			ReleaseSecondLogicRoomIfActive(tableId);
		}
	}

	private void ReleaseSecondLogicRoomIfActive(string tableId)
	{
		lock (_lock)
		{
			if (_secondLogic?.ActiveRoom != tableId)
			{
				return;
			}
			Context.RoomStates.TryGetValue(tableId, out RoomBettingState roomState);
			if (roomState != null)
			{
				RoomBettingState rs = roomState;
				if (rs.IsWaitingForResult || rs.IsRequestInFlight)
				{
					_slPendingReleaseRoom = tableId;
					FileLogger.Log("[방스위치][세컨로직] " + tableId + ": 진행 중 베팅 있음 → 결과 판정 후 방 점유 해제 예약", "Info", "RoomSwitch");
					return;
				}
			}
			ReleaseSecondLogicRoomCore(tableId, roomState);
		}
	}

	private void ReleaseSecondLogicRoomCore(string tableId, RoomBettingState? roomState)
	{
		_slPendingReleaseRoom = null;
		if (_secondLogic == null)
		{
			return;
		}
		if (roomState != null)
		{
			if (IsSecondLogicMatrix)
			{
				_slCarryMatrix = CaptureMatrixCarry(roomState);
			}
			else
			{
				_slCarryStep = roomState.CurrentStep;
			}
			_slCarryPbCross = roomState.PbCrossTarget;
			_slCarryPbCrossLossStreak = roomState.PbCrossLossStreak;
			_slCarryPbCrossPhaseIsBp = roomState.PbCrossPhaseIsBp;
		}
		_secondLogic.ClearActiveRoom();
		_slLastRoom = null;
		_slSplitYieldCount = 0;
		_slRidingTarget = null;
		string carried = (IsSecondLogicMatrix ? $"{_slCarryMatrix?.Row}차-{_slCarryMatrix?.Col}단계" : $"단계={_slCarryStep}");
		FileLogger.Log($"[방스위치][세컨로직] {tableId}: 활성 방 OFF → 방 점유 해제, 다음 방으로 이월 ({carried})", "Warning", "RoomSwitch");
	}

	private bool IsHongKongDoubleUpPending(string tableId, RoomBettingState roomState)
	{
		if (!roomState.IsDoubleUpPhase || string.IsNullOrEmpty(roomState.DoubleUpTarget))
		{
			return false;
		}
		if (Context.SelectedBetAmountType != 1)
		{
			if (Context.SelectedBetAmountType == 3)
			{
				return Context.TableIndexMap.GetValueOrDefault(tableId, 0) % 2 == 1;
			}
			return false;
		}
		return true;
	}

	public async Task ReloadRoomGateFormatsAsync()
	{
		if (!string.IsNullOrEmpty(Context.UserId))
		{
			await LoadRoomGateFormatSettingsAsync(Context.UserId);
			if (Context.IsRoomGateActive)
			{
				ResetAllRoomGates("방 On/Off 서식 변경");
			}
		}
	}

	public bool IsRoomGateOpen(string tableId)
	{
		if (!Context.IsRoomGateActive)
		{
			return true;
		}
		if (Context.RoomStates.TryGetValue(tableId, out RoomBettingState roomState))
		{
			return roomState.IsGateOpen;
		}
		return false;
	}

	public RoomGateDisplay GetRoomGateDisplay(string tableId)
	{
		if (!Context.IsRoomGateActive)
		{
			return RoomGateDisplay.None;
		}
		if (!Context.RoomStates.TryGetValue(tableId, out RoomBettingState roomState))
		{
			return RoomGateDisplay.Closed;
		}
		if (roomState.IsGateOpen)
		{
			return RoomGateDisplay.Open;
		}
		if (!((roomState.GateClosedAt.HasValue ? (DateTime.Now - roomState.GateClosedAt.Value) : TimeSpan.Zero).TotalMinutes >= 45.0))
		{
			return RoomGateDisplay.Closed;
		}
		return RoomGateDisplay.StaleClosed;
	}

	private void EvaluateRoomGate(string tableId, List<GameResult> history)
	{
		if (!Context.IsRoomGateActive)
		{
			return;
		}
		RoomBettingState roomState = Context.RoomStates.GetOrAdd(tableId, (string _) => new RoomBettingState
		{
			TableId = tableId
		});
		RoomGatePattern offMatch = _patternParser.FindMatchingRoomGatePattern(history, Context.RoomGateOffPatterns);
		RoomGatePattern onMatch = _patternParser.FindMatchingRoomGatePattern(history, Context.RoomGateOnPatterns);
		if (offMatch == null && onMatch == null)
		{
			return;
		}
		int num;
		RoomGatePattern roomGatePattern;
		if (offMatch != null)
		{
			if (onMatch != null)
			{
				num = ((offMatch.Sequence.Count >= onMatch.Sequence.Count) ? 1 : 0);
				if (num == 0)
				{
					goto IL_009f;
				}
			}
			else
			{
				num = 1;
			}
			roomGatePattern = offMatch;
			goto IL_00a3;
		}
		num = 0;
		goto IL_009f;
		IL_009f:
		roomGatePattern = onMatch;
		goto IL_00a3;
		IL_00a3:
		RoomGatePattern winner = roomGatePattern;
		RoomGatePattern loser = ((num != 0) ? onMatch : offMatch);
		string reason = ((loser == null) ? (winner.DisplayName + " 매칭") : $"{winner.DisplayName} 매칭 ({loser.DisplayName}과 동시 매칭 — {((winner.Sequence.Count == loser.Sequence.Count) ? "동률이라 Off 우선" : "더 길어 우선")})");
		if (num != 0)
		{
			CloseRoomGate(tableId, roomState, reason);
		}
		else
		{
			OpenRoomGate(tableId, roomState, reason);
		}
	}

	private void OpenRoomGate(string tableId, RoomBettingState roomState, string reason)
	{
		if (!roomState.IsGateOpen)
		{
			roomState.IsGateOpen = true;
			roomState.GateClosedAt = null;
			roomState.GateStaleWarnedAt = null;
			string stepDesc = (Context.IsMatrixStrategy ? $"{roomState.MatrixCurrentRow}차-{roomState.MatrixCurrentCol}단계" : $"단계={roomState.CurrentStep}");
			FileLogger.Log($"[방게이트] {tableId}: 열림 — {reason} (진행 상태 유지: {stepDesc})", "Info", "RoomGate");
			RoomGateChanged?.Invoke(this, new RoomGateChangedEventArgs
			{
				TableId = tableId,
				IsOpen = true,
				Reason = reason
			});
		}
	}

	private void CloseRoomGate(string tableId, RoomBettingState roomState, string reason)
	{
		if (!roomState.IsGateOpen)
		{
			DateTime? gateClosedAt = roomState.GateClosedAt;
			DateTime valueOrDefault = gateClosedAt.GetValueOrDefault();
			if (!gateClosedAt.HasValue)
			{
				valueOrDefault = DateTime.Now;
				DateTime? gateClosedAt2 = valueOrDefault;
				roomState.GateClosedAt = gateClosedAt2;
			}
			return;
		}
		roomState.IsGateOpen = false;
		roomState.GateClosedAt = DateTime.Now;
		roomState.GateStaleWarnedAt = null;
		FileLogger.Log($"[방게이트] {tableId}: 닫힘 — {reason} (신규 진입 중단, 진행 중 베팅·단계는 유지)", "Info", "RoomGate");
		RoomGateChanged?.Invoke(this, new RoomGateChangedEventArgs
		{
			TableId = tableId,
			IsOpen = false,
			Reason = reason
		});
		ReleaseSecondLogicRoomIfActive(tableId);
	}

	public void ResetRoomGate(string tableId, string reason)
	{
		if (Context.RoomStates.TryGetValue(tableId, out RoomBettingState roomState))
		{
			if (roomState.IsGateOpen)
			{
				CloseRoomGate(tableId, roomState, reason);
				return;
			}
			roomState.GateClosedAt = DateTime.Now;
			roomState.GateStaleWarnedAt = null;
		}
	}

	public void ResetAllRoomGates(string reason)
	{
		foreach (var (tableId, roomState) in Context.RoomStates)
		{
			if (roomState.IsGateOpen)
			{
				CloseRoomGate(tableId, roomState, reason);
				continue;
			}
			roomState.GateClosedAt = DateTime.Now;
			roomState.GateStaleWarnedAt = null;
		}
		FileLogger.Log("[방게이트] 전체 방 닫힘으로 초기화 — " + reason, "Info", "RoomGate");
	}

	public void SetPbCrossEnabled(bool enabled, bool bpCrossOnTwoLoss)
	{
		bool crossChanged;
		bool twoLossChanged;
		lock (_lock)
		{
			crossChanged = Context.IsPbCrossEnabled != enabled;
			twoLossChanged = Context.IsBpCrossOnTwoLossEnabled != bpCrossOnTwoLoss;
			if (!crossChanged && !twoLossChanged)
			{
				return;
			}
			Context.IsPbCrossEnabled = enabled;
			Context.IsBpCrossOnTwoLossEnabled = bpCrossOnTwoLoss;
			foreach (RoomBettingState roomState in Context.RoomStates.Values)
			{
				roomState.PbCrossLossStreak = 0;
				roomState.PbCrossPhaseIsBp = false;
				if (crossChanged)
				{
					roomState.PbCrossTarget = null;
				}
			}
			_slCarryPbCrossLossStreak = 0;
			_slCarryPbCrossPhaseIsBp = false;
			if (crossChanged)
			{
				_slCarryPbCross = null;
				_slRidingTarget = null;
			}
		}
		if (crossChanged)
		{
			FileLogger.Log(enabled ? ("[PB교차] 기능 켜짐 — 서식 매칭 없이 매 라운드 베팅하며 직전에 벤 타겟의 반대쪽을 겁니다. 전체 방 위상 초기화 (첫 타겟=Player)" + (_slUseStreakRiding ? " / 세컨로직 줄타기의 타겟 고정은 교차에 양보합니다(방 유지 규칙은 유지)" : "")) : "[PB교차] 기능 꺼짐 — 다시 서식 매칭으로 진입·타겟을 판정합니다.", "Info", "PatternMatch");
		}
		if (twoLossChanged)
		{
			FileLogger.Log(bpCrossOnTwoLoss ? ($"[PB교차][2연패] 기능 켜짐 — 방별로 {2}연패마다 위상을 토글합니다(PB↔BP). " + "전체 방 연패 카운터·위상 초기화 (PB부터 시작)") : "[PB교차][2연패] 기능 꺼짐 — 위상 전환 없이 평소의 교차만 돕니다.", "Info", "PatternMatch");
		}
	}

	public IReadOnlyList<string> CheckRoomGateWatchdog()
	{
		if (!IsRunning || !Context.IsRoomGateActive)
		{
			return Array.Empty<string>();
		}
		DateTime now = DateTime.Now;
		TimeSpan threshold = TimeSpan.FromMinutes(45L);
		List<KeyValuePair<string, RoomBettingState>> stale = Context.RoomStates.Where<KeyValuePair<string, RoomBettingState>>((KeyValuePair<string, RoomBettingState> kv) => !kv.Value.IsGateOpen && IsSelectedTable(kv.Key) && IsRoomBettingEnabled(kv.Key) && kv.Value.GateClosedAt.HasValue && now - kv.Value.GateClosedAt.Value >= threshold && (!kv.Value.GateStaleWarnedAt.HasValue || now - kv.Value.GateStaleWarnedAt.Value >= threshold)).ToList();
		foreach (var (tableId, roomState) in stale)
		{
			TimeSpan closedFor = now - roomState.GateClosedAt.Value;
			roomState.GateStaleWarnedAt = now;
			FileLogger.Log($"[방게이트] ⚠ {tableId}: {closedFor.TotalMinutes:F0}분째 닫힘 — On 서식이 걸리지 않았거나 슈 교체를 놓쳤을 수 있습니다 (수동 확인 필요)", "Warning", "RoomGate");
		}
		return stale.Select((KeyValuePair<string, RoomBettingState> kv) => kv.Key).ToList();
	}

	public void RenameRoomKey(string oldKey, string newKey)
	{
		if (string.IsNullOrEmpty(oldKey) || string.IsNullOrEmpty(newKey) || oldKey == newKey)
		{
			return;
		}
		if (_disabledRooms.TryRemove(oldKey, out var _))
		{
			_disabledRooms[newKey] = 0;
			FileLogger.Log($"[방스위치] 키 이관: {oldKey} → {newKey} (OFF 상태 유지)", "Info", "RoomSwitch");
		}
		CancelBettingTimer(oldKey);
		if (_resetBlockedRounds.TryRemove(oldKey, out string blockedRound))
		{
			_resetBlockedRounds.TryAdd(newKey, blockedRound);
		}
		if (Context.RoomStates.TryRemove(oldKey, out RoomBettingState roomState))
		{
			roomState.TableId = newKey;
			if (Context.RoomStates.TryAdd(newKey, roomState))
			{
				string stepDesc = (Context.IsMatrixStrategy ? $"{roomState.MatrixCurrentRow}차-{roomState.MatrixCurrentCol}단계" : $"단계={roomState.CurrentStep}");
				FileLogger.Log($"[방스위치] 방 상태 이관: {oldKey} → {newKey} (게이트={(roomState.IsGateOpen ? "열림" : "닫힘")}, {stepDesc})", "Info", "RoomSwitch");
			}
			else
			{
				FileLogger.Log($"[방스위치] 방 상태 이관 생략: {newKey}에 이미 진행 중인 상태가 있어 옛 키({oldKey}) 상태는 버립니다.", "Info", "RoomSwitch");
			}
		}
		lock (_lock)
		{
			if (_slLastRoom == oldKey)
			{
				_slLastRoom = newKey;
			}
			if (_slPendingReleaseRoom == oldKey)
			{
				_slPendingReleaseRoom = newKey;
			}
			_secondLogic?.RenameActiveRoom(oldKey, newKey);
		}
	}

	public async Task StartAsync(string userId, int betAmountType = 0)
	{
		if (IsRunning)
		{
			FileLogger.Log("[AutoBetting] 이미 실행 중입니다.");
			return;
		}
		try
		{
			Context.UserId = userId;
			_startBetAmountCache = new Dictionary<int, List<BetAmountSettingDto>>();
			SecondLogicSetting slSetting = new SecondLogicSettingService().Get(userId);
			if (slSetting.IsEnabled)
			{
				_slUseStreakRiding = slSetting.UseStreakRiding;
				_slRidingTarget = null;
				_secondLogic = new SecondLogicEngine(slSetting.MissStreakToSwitchRoom, slSetting.WinStreakToSwitchRoom, _slUseStreakRiding);
				_slLastRoom = null;
				_slPendingReleaseRoom = null;
				_slCarryStep = null;
				_slCarryMatrix = null;
				_slCarryPbCross = null;
				_slCarryPbCrossLossStreak = 0;
				_slCarryPbCrossPhaseIsBp = false;
				_slSplitYieldCount = 0;
				_slUseCombo = slSetting.UseMartinDalembertCombo;
				_slDAlembertMaxStep = Math.Max(0, slSetting.DAlembertMaxStep);
				_slCooldownProfitAmount = Math.Max(0m, slSetting.CooldownProfitAmount);
				_slCooldownMinutes = Math.Max(0, slSetting.CooldownMinutes);
				_slUseProfitCooldown = slSetting.UseProfitCooldown && _slCooldownProfitAmount > 0m && _slCooldownMinutes > 0;
				betAmountType = ((!_slUseCombo) ? NormalizeSecondLogicBetAmountType(slSetting.BetAmountType) : 0);
				int winStreak = Math.Max(1, slSetting.WinStreakToSwitchRoom);
				FileLogger.Log($"[세컨로직] 활성 — 진입·타겟은 외부 서식/예외 서식 설정을 따름, {slSetting.MissStreakToSwitchRoom}연속 미당첨 시 방이동, " + (_slUseStreakRiding ? ($"줄타기 ON(당첨 시 틀릴 때까지 같은 방·같은 타겟 반복 — 연속 당첨 방이동 설정만 무시한다. 미당첨은 줄타기만 끝내고, 방이동은 위 {slSetting.MissStreakToSwitchRoom}연속 미당첨 규칙을 그대로 따른다)" + (Context.IsPbCrossEnabled ? " ※PB교차 ON이라 타겟 고정은 하지 않고 방 유지 규칙만 적용" : "") + ", ") : (((winStreak == 1) ? "당첨 즉시" : $"{winStreak}연속 당첨 시") + " 방이동, ")) + (_slUseCombo ? "베팅금=마틴4→달랑 조합(단위×배수)" : ("베팅금타입=" + GetBetAmountTypeName(betAmountType))) + ((!_slUseCombo && betAmountType == 4 && _slDAlembertMaxStep > 0) ? $", 달랑베르 최대단계={_slDAlembertMaxStep} (도달 후 미당첨 시 1단계 재시작)" : "") + (_slUseProfitCooldown ? $", 수익 쿨다운 ON(기준 대비 +{_slCooldownProfitAmount:N0}원 달성 시 {_slCooldownMinutes}분 정지 — 정지 시 모도 초기화, 해제 시 기준 잔액 재설정)" : ""));
			}
			else
			{
				_secondLogic = null;
				_slDAlembertMaxStep = 0;
				_slUseStreakRiding = false;
				_slRidingTarget = null;
				_slUseProfitCooldown = false;
				_slCooldownProfitAmount = 0m;
				_slCooldownMinutes = 0;
			}
			Context.IsChainMode = betAmountType == -1;
			// [성능] 서로 독립적인 설정들(각기 다른 Context 필드만 갱신)을 동시에 로드한다.
			//        원격 VPS에서 한 건당 ~0.3초 걸리던 순차 왕복을 겹쳐 처리해 게임 시작을 크게 단축한다.
			Task formatTask = LoadFormatSettingsAsync(userId);
			Task limitTask = LoadLimitSettingsAsync(userId);
			Task exceptionTask = LoadExceptionFormatSettingsAsync(userId);
			Task roomGateTask = LoadRoomGateFormatSettingsAsync(userId);
			Task<RoomCooldownSettings> cooldownTask = LoadRoomCooldownSettingsAsync();
			await formatTask;
			if (Context.Patterns.Count == 0 && !Context.IsPbCrossEnabled)
			{
				RaiseStateChanged(isRunning: false, "서식 설정이 없습니다.");
				return;
			}
			if (Context.IsChainMode)
			{
				if (!(await LoadStrategyChainAsync(userId)))
				{
					return;
				}
			}
			else
			{
				Context.SelectedBetAmountType = betAmountType;
				await LoadBetAmountSettingsAsync(userId, betAmountType);
				if (!((betAmountType != 3) ? (Context.BetAmounts.Count > 0) : (Context.MartinBetAmounts.Count > 0 || Context.HongKongBetAmounts.Count > 0)))
				{
					RaiseStateChanged(isRunning: false, (betAmountType == 4) ? "달랑베르 기본금액이 설정되지 않았습니다. 베팅금 설정의 '달랑베르' 탭에서 기본 금액을 입력해 주세요." : "베팅금 설정이 없습니다.");
					return;
				}
				if (betAmountType == 3)
				{
					await InitializeMixedModeTableIndexMapAsync(userId);
				}
				if (_slUseCombo)
				{
					_slComboUnit = ((Context.BetAmounts.Count > 0) ? Context.BetAmounts[Context.BetAmounts.Keys.Min()] : 0m);
					if (_slComboUnit <= 0m)
					{
						RaiseStateChanged(isRunning: false, "세컨로직 조합 베팅: 마틴 1단계 금액이 설정되지 않았습니다.");
						return;
					}
					FileLogger.Log($"[세컨로직] 조합 단위(마틴 1단계) = {_slComboUnit:N0}원");
				}
			}
			await Task.WhenAll(limitTask, exceptionTask, roomGateTask);
			Context.PendingBetAmount = 0m;
			if (Context.CurrentBalance <= 0m)
			{
				RaiseStateChanged(isRunning: false, "잔액이 감지되지 않았습니다. 게임 화면에서 잔액이 표시되는지 확인 후 다시 시도해 주세요.");
				FileLogger.Log("[AutoBetting] 시작 차단: 잔액 미감지 (CurrentBalance=0)");
				return;
			}
			Context.StartBalance = Context.CurrentBalance;
			Context.PeakBalance = Context.CurrentBalance;
			Context.CycleStartBalance = Context.CurrentBalance;
			Context.CompletedCycles = 0;
			_isCycleResting = false;
			Context.MatrixFirstRowCapBaseBalance = Context.CurrentBalance;
			Context.SecondLogicCooldownBaseBalance = Context.CurrentBalance;
			_profitCooldownCts?.Cancel();
			_isProfitCoolingDown = false;
			_matrixRestCts?.Cancel();
			_isMatrixResting = false;
			if (Context.SelectedBetAmountType == 5 && !Context.IsChainMode)
			{
				Context.MartinEntryBalance = Context.CurrentBalance;
			}
			FileLogger.Log($"[AutoBetting] 시작잔액: {Context.StartBalance:N0}원" + (Context.IsUpperLimitEnabled ? $", 상한: +{Context.UpperLimitPercent}%" : "") + (Context.IsLowerLimitEnabled ? $", 하한: -{Context.LowerLimitPercent}%" : "") + (Context.IsChainMode ? $", 체인모드: {Context.ChainSteps.Count}단계" : ""));
			if (string.IsNullOrEmpty(Context.PpUserId))
			{
				FileLogger.Log("[AutoBetting] PP UserId 미설정 - seat 메시지 수신 대기 중 (베팅 시 자동 스킵됨)");
			}
			Context.IsRunning = true;
			Context.RoomStates.Clear();
			Context.UpchigiGlobalNet = 0m;
			Context.TotalBetAmount = 0m;
			Context.PendingBetAmount = 0m;
			Context.LastGlobalBetTime = null;
			Context.IsRefreshing = false;
			_isPaused = false;
			_resetBlockedRounds.Clear();
			StartInactivityTimer();
			Context.ResetKeepAliveStats();
			StartKeepAliveTimer();
			RoomCooldownSettings cooldownSettings = await cooldownTask;
			_cooldownService.Start(userId, cooldownSettings);
			RaiseStateChanged(isRunning: true, "자동 베팅 시작됨" + (Context.IsChainMode ? $" (체인 {Context.ChainSteps.Count}단계)" : ""));
			FileLogger.Log($"[AutoBetting] 자동 베팅 시작 - 패턴 {Context.Patterns.Count}개, 베팅금 {Context.BetAmounts.Count}단계" + (Context.IsChainMode ? $", 체인={Context.ChainSteps.Count}단계" : "") + (Context.IsPbCrossEnabled ? ", PB교차베팅 ON(서식 매칭 없이 매 라운드 P/B 교차)" : "") + ((Context.IsPbCrossEnabled && Context.IsBpCrossOnTwoLossEnabled) ? $", 2연패시BP교차 ON({2}연패마다 PB↔BP 위상 전환)" : ""));
		}
		catch (Exception ex)
		{
			FileLogger.Log("[AutoBetting] 시작 실패: " + ex.Message, "Error", "AutoBetting", ex.StackTrace);
			RaiseStateChanged(isRunning: false, "시작 실패: " + ex.Message);
		}
		finally
		{
			_startBetAmountCache = null;
		}
	}

	/// <summary>
	/// Rooms whose bet was already on the table when the bot stopped. Their results are still accepted
	/// so the round can be settled; everything else stays shut off.
	/// </summary>
	private readonly ConcurrentDictionary<string, byte> _settlementDrainRooms = new(StringComparer.Ordinal);

	private const int SettlementDrainTimeoutSeconds = 90;

	/// <summary>True while the bot is stopped but still collecting results for bets already placed.</summary>
	public bool IsDrainingSettlement => !_settlementDrainRooms.IsEmpty;

	private bool IsDrainingRoom(string tableId)
	{
		return !string.IsNullOrEmpty(tableId) && _settlementDrainRooms.ContainsKey(tableId);
	}

	/// <summary>
	/// Waits for the rounds that were in flight at stop time to be judged.
	/// <para>
	/// Without this the bot dropped them: <c>IsRunning</c> went false the instant a limit was hit and
	/// every result handler bails on that flag, so the last bet was never settled — it stayed Pending
	/// on the server, showed 베팅중 forever in the history, and its money never reached the ledger.
	/// </para>
	/// </summary>
	private async Task DrainSettlementAsync()
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(SettlementDrainTimeoutSeconds);
		while (!_settlementDrainRooms.IsEmpty && DateTime.UtcNow < deadline)
		{
			foreach (string tableId in _settlementDrainRooms.Keys.ToList())
			{
				// a room is done as soon as its bet is no longer awaiting a result
				if (!Context.RoomStates.TryGetValue(tableId, out RoomBettingState room)
					|| (!room.IsWaitingForResult && !room.IsRequestInFlight))
				{
					if (_settlementDrainRooms.TryRemove(tableId, out _))
					{
						FileLogger.Log("[AutoBetting][정산대기] " + tableId + ": 정산 완료", "Info", "AutoBetting");
					}
				}
			}
			if (_settlementDrainRooms.IsEmpty)
			{
				break;
			}
			await Task.Delay(500).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (!_settlementDrainRooms.IsEmpty)
		{
			string stuck = string.Join(", ", _settlementDrainRooms.Keys);
			FileLogger.Log($"[AutoBetting][정산대기] ⚠ {SettlementDrainTimeoutSeconds}초 내 결과 미수신 — 미정산으로 남습니다: {stuck}", "Warning", "AutoBetting");
			_settlementDrainRooms.Clear();
		}
	}

	public async Task StopAsync(string? reason = null)
	{
		if (!IsRunning)
		{
			return;
		}
		// bets already on the table still need their result — remember those rooms before the
		// IsRunning flag shuts every result handler down
		_settlementDrainRooms.Clear();
		foreach (KeyValuePair<string, RoomBettingState> kv in Context.RoomStates)
		{
			if (kv.Value.IsWaitingForResult || kv.Value.IsRequestInFlight)
			{
				_settlementDrainRooms[kv.Key] = 0;
			}
		}
		Context.IsRunning = false;
		_isPaused = false;
		_resetBlockedRounds.Clear();
		_secondLogic = null;
		_slLastRoom = null;
		_slPendingReleaseRoom = null;
		_slCarryStep = null;
		_slCarryMatrix = null;
		_slCarryPbCross = null;
		_slCarryPbCrossLossStreak = 0;
		_slCarryPbCrossPhaseIsBp = false;
		_slUseCombo = false;
		_slComboUnit = 0m;
		_slDAlembertMaxStep = 0;
		_slUseStreakRiding = false;
		_slRidingTarget = null;
		_slUseProfitCooldown = false;
		_slCooldownProfitAmount = 0m;
		_slCooldownMinutes = 0;
		_cycleRestCts?.Cancel();
		_isCycleResting = false;
		_profitCooldownCts?.Cancel();
		_isProfitCoolingDown = false;
		_matrixRestCts?.Cancel();
		_isMatrixResting = false;
		StopInactivityTimer();
		StopKeepAliveTimer();
		_cooldownService.Stop();
		foreach (KeyValuePair<string, CancellationTokenSource> kvp in _bettingCts)
		{
			if (_bettingCts.TryRemove(kvp.Key, out CancellationTokenSource cts))
			{
				cts.Cancel();
			}
		}
		string msgEarly = reason ?? "사용자가 중지함";
		if (!_settlementDrainRooms.IsEmpty)
		{
			// new bets are already blocked; let the screen show that while the last rounds settle.
			// No StopReason here so the operator does not get the stop dialog twice.
			FileLogger.Log($"[AutoBetting][정산대기] 중지 요청({msgEarly}) — 진행 중이던 {_settlementDrainRooms.Count}개 방의 결과를 최대 {SettlementDrainTimeoutSeconds}초간 기다립니다", "Info", "AutoBetting");
			RaiseStateChanged(isRunning: false, msgEarly + " — 마지막 베팅 정산 대기 중...");
			await DrainSettlementAsync();
		}

		// saved after the drain so the last round's money is inside these totals
		try
		{
			if (!Context.IsVirtualMode)  // 가상베팅은 로컬 검증용 — 서버에 세션/이력을 남기지 않는다.
			{
				await GetApi().SaveBettingSessionAsync(Context.StartBalance, Context.AvailableBalance, Context.TotalBetAmount, Context.IsVirtualMode);
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[AutoBetting] 세션 저장 실패: " + ex.Message, "Warning", "AutoBetting");
		}
		if (Context.KeepAliveBetCount > 0 || Context.KeepAliveFailCount > 0)
		{
			FileLogger.Log($"[KeepAlive] 세션 결산 - 전송 {Context.KeepAliveBetCount}건 / 실패 {Context.KeepAliveFailCount}건, 지출 {Context.KeepAliveTotalAmount:N0}원, 환급 {Context.KeepAliveReturnedAmount:N0}원, 순지출 {Context.KeepAliveNetSpend:N0}원 (봇 베팅 회계와 분리된 실제 금액)", "Info", "AutoBetting");
		}
		string msg = reason ?? "사용자가 중지함";
		RaiseStateChanged(isRunning: false, msg, reason);
		FileLogger.Log("[AutoBetting] 자동 베팅 중지 - 사유: " + msg, "Info", DbLogService.SessionCategory);
	}

	public void OnGameStarted(string tableId, string gameId)
	{
		if (IsRunning && _tableStates != null && (_selectedTables.Count <= 0 || IsSelectedTable(tableId)) && _tableStates.TryGetValue(tableId, out TableState state))
		{
			state.GameId = gameId;
			state.Phase = BettingPhase.GameStarted;
			state.LastUpdated = DateTime.Now;
		}
	}

	public void OnTimerReceived(string tableId, int seconds)
	{
		if (IsRunning && _tableStates != null && (_selectedTables.Count <= 0 || IsSelectedTable(tableId)) && _tableStates.TryGetValue(tableId, out TableState state))
		{
			state.TimerSeconds = seconds;
			state.TimerReceivedAt = DateTime.Now;
			state.LastUpdated = DateTime.Now;
			if (state.Phase == BettingPhase.BetsOpen && !_bettingCts.ContainsKey(tableId))
			{
				ScheduleBettingTimer(tableId, state);
			}
		}
	}

	public void OnBetsOpened(string tableId, string gameId)
	{
		if (IsRunning && _tableStates != null && (_selectedTables.Count <= 0 || IsSelectedTable(tableId)) && _tableStates.TryGetValue(tableId, out TableState state))
		{
			state.Phase = BettingPhase.BetsOpen;
			if (!string.IsNullOrEmpty(gameId))
			{
				state.GameId = gameId;
			}
			state.LastUpdated = DateTime.Now;
			ClearResetBlockIfNewRound(tableId, state.GameId);
			ScheduleBettingTimer(tableId, state);
		}
	}

	public void OnBetsClosingSoon(string tableId)
	{
		if (IsRunning && _tableStates != null && (_selectedTables.Count <= 0 || IsSelectedTable(tableId)) && _tableStates.TryGetValue(tableId, out TableState state))
		{
			state.Phase = BettingPhase.BetsClosingSoon;
			state.LastUpdated = DateTime.Now;
		}
	}

	public void OnBetsClosed(string tableId)
	{
		if (IsRunning && _tableStates != null && (_selectedTables.Count <= 0 || IsSelectedTable(tableId)))
		{
			CancelBettingTimer(tableId);
			if (_tableStates.TryGetValue(tableId, out TableState state))
			{
				state.Phase = BettingPhase.BetsClosed;
				state.LastUpdated = DateTime.Now;
			}
		}
	}

	public void OnDealingStarted(string tableId)
	{
		if (IsRunning && _tableStates != null && (_selectedTables.Count <= 0 || IsSelectedTable(tableId)) && _tableStates.TryGetValue(tableId, out TableState state))
		{
			state.Phase = BettingPhase.Dealing;
			state.LastUpdated = DateTime.Now;
		}
	}

	public void OnGameResultReceived(string tableId, GameResult result)
	{
		// a stopped bot still accepts the result for a bet it had already placed in this room
		if ((!IsRunning && !IsDrainingRoom(tableId)) || _gameHistories == null || (_selectedTables.Count > 0 && !IsSelectedTable(tableId)) || !_gameHistories.TryGetValue(tableId, out List<GameResult> history))
		{
			return;
		}
		int newHistoryCount;
		lock (history)
		{
			newHistoryCount = history.Count;
		}
		ConcurrentDictionary<string, TableState>? tableStates = _tableStates;
		if (tableStates != null && tableStates.TryGetValue(tableId, out TableState state))
		{
			state.Phase = BettingPhase.Result;
			state.LastUpdated = DateTime.Now;
		}
		if (Context.IsRoomGateActive)
		{
			List<GameResult> gateHistory;
			lock (history)
			{
				gateHistory = history.ToList();
			}
			EvaluateRoomGate(tableId, gateHistory);
		}
		SettleKeepAliveBet(tableId, result);
		JudgeResult(tableId, newHistoryCount, result);
	}

	public decimal GetBetAmountForRound(string tableId, string gameId)
	{
		if (string.IsNullOrEmpty(gameId))
		{
			return 0m;
		}
		if (!Context.RoomStates.TryGetValue(tableId, out RoomBettingState roomState))
		{
			return 0m;
		}
		if (!string.Equals(roomState.LastBetGameId, gameId, StringComparison.Ordinal))
		{
			return 0m;
		}
		return roomState.LastBetAmount;
	}

	public void OnWinReceived(string tableId, string gameId, decimal netWinBalance, decimal winAmount)
	{
		// the payout for a bet placed before the stop still belongs in the ledger
		if (!IsRunning && !IsDrainingRoom(tableId))
		{
			return;
		}
		// 2차 판정: 확인 미수신으로 '미접수' 처리됐던 베팅이, 이 win 증거로 접수 확인되면 승격·정산한다.
		TryReconcileUnconfirmedBet(tableId, gameId, netWinBalance, winAmount);
		FileLogger.Log($"[AutoBetting] {tableId}: win nwb={netWinBalance:+#,##0;-#,##0}, payout={winAmount:N0}");
		if (!Context.RoomStates.TryGetValue(tableId, out RoomBettingState roomState) || !roomState.LastBetTime.HasValue)
		{
			return;
		}
		double elapsed = (DateTime.Now - roomState.LastBetTime.Value).TotalSeconds;
		if (elapsed < 120.0)
		{
			FileLogger.Log($"[Win진단] {tableId}: 베팅→win 소요시간={elapsed:F1}초, nwb={netWinBalance:+#,##0;-#,##0}, win={winAmount:N0}, 단계={roomState.CurrentStep}, 베팅={roomState.LastBetAmount:N0}", "Info", "AutoBetting");
		}
		if (!roomState.IsUnconfirmedBet || roomState.IsLateConfirmed || !(elapsed < 120.0))
		{
			return;
		}
		decimal balanceDiff = Math.Abs(Context.AvailableBalance - roomState.PreBetBalance);
		decimal threshold = roomState.LastBetAmount * 0.1m;
		if (netWinBalance == 0m && winAmount == 0m && balanceDiff < threshold)
		{
			FileLogger.Log($"[AutoBetting] {tableId}: ✗ 미확인 베팅 미접수 판단! (nwb=0, win=0, balDiff={balanceDiff:N0} < {threshold:N0})", "Error", "AutoBetting");
			Task.Run(async delegate
			{
				try
				{
					await GetApi().CancelBettingHistoryAsync(roomState.LastBetType ?? "", roomState.LastBetAmount, tableId, roomState.LastBetGameId);
					FileLogger.Log("[AutoBetting] " + tableId + ": 미접수 베팅 내역 DB 취소 완료");
				}
				catch (Exception ex)
				{
					FileLogger.Log("[AutoBetting] " + tableId + ": 베팅 내역 취소 실패: " + ex.Message, "Warning", "AutoBetting");
				}
			});
			roomState.IsUnconfirmedBet = false;
		}
		else
		{
			FileLogger.Log($"[AutoBetting] {tableId}: 미확인 베팅 사후 확인됨 (nwb={netWinBalance:+#,##0;-#,##0}, balDiff={balanceDiff:N0})");
			roomState.IsUnconfirmedBet = false;
		}
	}

	/// <summary>
	/// 2차 판정(사후 접수 보정). 확인 메시지를 못 받아 '미접수=실패'로 처리됐던 베팅이,
	/// 같은 gameId 의 win 메시지(nwb=-스테이크/배당 = 접수 증거)로 "사이트가 받았다"가 확인되면
	/// 그 베팅을 '확인된 베팅'으로 승격해 정상 판정(단계·회계)한다.
	/// 가드: ① 전송 gameId 정확 일치 ② 거절/반려는 제외(provisional 미기록) ③ 접수 증거 필수·승패는 실제 결과로
	///       ④ 성공 경로와 동일 회계 ⑤ 라운드당 1회(소비+IsWaitingForResult 공유 마커).
	/// </summary>
	private void TryReconcileUnconfirmedBet(string tableId, string gameId, decimal netWinBalance, decimal winAmount)
	{
		if (string.IsNullOrEmpty(gameId) || _gameHistories == null)
		{
			return;
		}
		// 가드 ③(접수 증거): 이 라운드에 우리 스테이크가 실제로 움직였는가. 둘 다 0이면 안 받은 것 → 승격 안 함.
		if (netWinBalance == 0m && winAmount <= 0m)
		{
			return;
		}
		// 승패 판정용 결과는 _lock 밖에서 먼저 조회(락 중첩 회피). 금액 추론이 아니라 실제 winner 로 판정한다.
		GameResult? gr = null;
		int historyCount = 0;
		if (_gameHistories.TryGetValue(tableId, out List<GameResult> history))
		{
			lock (history)
			{
				historyCount = history.Count;
				for (int i = history.Count - 1; i >= 0; i--)
				{
					if (string.Equals(history[i].GameId, gameId, StringComparison.Ordinal))
					{
						gr = history[i];
						break;
					}
				}
			}
		}
		lock (_lock)
		{
			if (!Context.RoomStates.TryGetValue(tableId, out RoomBettingState roomState))
			{
				return;
			}
			// 가드 ①: 정확히 이 미접수 베팅의 gameId 여야 한다. (거절건은 provisional 미기록 → 자동 제외: 가드 ②)
			if (string.IsNullOrEmpty(roomState.ProvisionalGameId)
				|| !string.Equals(roomState.ProvisionalGameId, gameId, StringComparison.Ordinal))
			{
				return;
			}
			decimal amt = roomState.ProvisionalAmount;
			string target = roomState.ProvisionalTarget ?? string.Empty;
			// 가드 ⑤: 한 번만 소비(재진입 차단). 가드 ④: 성공 경로와 동일하게 회계를 맞춘다.
			roomState.ProvisionalGameId = null;
			roomState.LastBetGameId = gameId;
			roomState.LastBetAmount = amt;
			roomState.LastBetType = target;
			roomState.LastBetTime = roomState.ProvisionalTime ?? DateTime.Now;
			roomState.IsWaitingForResult = true;
			roomState.IsUnconfirmedBet = false;
			Context.TotalBetAmount += amt;      // 접수 증명됨 → 이제 집계(성공 경로와 동일)
			Context.PendingBetAmount += amt;    // 실패 때 뺐던 것을 재가산 → JudgeResult 가 다시 빼서 상쇄
			FileLogger.Log($"[AutoBetting] {tableId}: ★ 미접수 베팅 사후 접수확인(win 증거: nwb={netWinBalance:+#,##0;-#,##0}, win={winAmount:N0}) → 승격 (game={gameId}, 금액={amt:N0}, 타겟={target})", "Info", "AutoBetting");
			if (gr != null)
			{
				// 결과가 이미 있으면 즉시 정상 판정 경로로 정산(단계·이벤트·합산수익 전부 성공 경로와 동일).
				JudgeResult(tableId, historyCount, gr);
			}
			else
			{
				// 결과가 아직 → IsWaitingForResult=true, LastBetGameId=gameId 로 해놨으니 결과 수신 시 JudgeResult 가 roundMatched 로 정산.
				FileLogger.Log($"[AutoBetting] {tableId}: 승격 후 결과 대기 (game={gameId}) — 결과 수신 시 정산", "Info", "AutoBetting");
			}
		}
	}

	public void OnLateConfirmationReceived()
	{
		foreach (RoomBettingState room in Context.RoomStates.Values.Where((RoomBettingState r) => r.IsUnconfirmedBet && !r.IsLateConfirmed).ToList())
		{
			room.IsLateConfirmed = true;
			FileLogger.Log("[AutoBetting] " + room.TableId + ": ★ 비동기 후속 확인으로 Confirmed 보정 완료", "Info", "AutoBetting");
		}
	}

	public void CheckBalanceAfterDomUpdate()
	{
		if (IsRunning)
		{
			CheckPeakBalance();
			CheckLimits();
		}
	}

	public void ScheduleBettingTimer(string tableId, TableState state)
	{
		if (!IsRunning || _isPaused || _isCycleResting || _isProfitCoolingDown || _isMatrixResting || (_selectedTables.Count > 0 && !IsSelectedTable(tableId)))
		{
			return;
		}
		CancelBettingTimer(tableId);
		double remainingMs = state.RemainingTimeMs;
		// 창이 열리면 작은 버퍼(직전 결과 반영)만 두고 '즉시' 베팅한다 — 처리 여유를 최대로 확보해
		// 칩이 마감 직전에 올라가며 실패하던 문제를 없앤다. (예전: remainingMs - BettingTargetMs 로 마감 10초 전까지 대기)
		double delayMs = BettingOpenSettleMs;
		if (delayMs > remainingMs)
		{
			delayMs = 0.0;
		}
		if (remainingMs < 1000.0)
		{
			FileLogger.Log($"[AutoBetting] {tableId}: 타이머 스킵 (remaining={remainingMs:F0}ms, TimerSeconds={state.TimerSeconds}, TimerReceivedAt={state.TimerReceivedAt})");
			return;
		}
		FileLogger.Log($"[AutoBetting] {tableId}: 타이머 예약 (remaining={remainingMs:F0}ms, delay={delayMs:F0}ms)");
		CancellationTokenSource cts = new CancellationTokenSource();
		_bettingCts[tableId] = cts;
		Task.Run(async delegate
		{
			_ = 1;
			try
			{
				if (delayMs > 0.0)
				{
					await Task.Delay((int)delayMs, cts.Token);
				}
				if (!cts.Token.IsCancellationRequested && IsRunning && !_isPaused && !_isCycleResting && !_isProfitCoolingDown && !_isMatrixResting)
				{
					await ExecuteTimerBetAsync(tableId, cts.Token);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex2)
			{
				FileLogger.Log("[AutoBetting] " + tableId + ": 타이머 베팅 오류: " + ex2.Message, "Error", "AutoBetting");
			}
		});
	}

	private void CancelBettingTimer(string tableId)
	{
		if (_bettingCts.TryRemove(tableId, out CancellationTokenSource cts))
		{
			cts.Cancel();
			cts.Dispose();
		}
	}

	private bool IsRoundHistorySynced(string tableId, TableState state)
	{
		string previousGameId = state.PreviousGameId;
		if (string.IsNullOrEmpty(previousGameId))
		{
			return true;
		}
		if (_gameHistories == null || !_gameHistories.TryGetValue(tableId, out List<GameResult> history))
		{
			return false;
		}
		lock (history)
		{
			return history.TakeLast(5).Any((GameResult r) => string.Equals(r.GameId, previousGameId, StringComparison.Ordinal));
		}
	}

	private async Task<bool> WaitForRoundHistorySyncAsync(string tableId, TableState state, CancellationToken token)
	{
		if (IsRoundHistorySynced(tableId, state))
		{
			return true;
		}
		DateTime startedAt = DateTime.Now;
		double waitedMs = 0.0;
		try
		{
			while (state.RemainingTimeMs > 3000.0)
			{
				await Task.Delay(200, token);
				waitedMs = (DateTime.Now - startedAt).TotalMilliseconds;
				if (IsRoundHistorySynced(tableId, state))
				{
					FileLogger.Log($"[방게이트] {tableId}: 직전 라운드({state.PreviousGameId}) 반영 확인 — {waitedMs:F0}ms 대기 후 베팅 진행 (마감까지 {state.RemainingTimeMs:F0}ms)", "Info", "RoomGate");
					return true;
				}
			}
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
		FileLogger.Log($"[방게이트] {tableId}: 직전 라운드({state.PreviousGameId}) 결과가 마감 {3000}ms 전까지 오지 않아 이 라운드는 베팅하지 않습니다 (대기 {waitedMs:F0}ms) — 한 라운드 뒤쳐진 빅로드로 방을 고르지 않기 위함", "Warning", "RoomGate");
		return false;
	}

	private void RefreshRoomGate(string tableId)
	{
		if (Context.IsRoomGateActive && _gameHistories != null && _gameHistories.TryGetValue(tableId, out List<GameResult> history))
		{
			List<GameResult> snapshot;
			lock (history)
			{
				snapshot = history.ToList();
			}
			EvaluateRoomGate(tableId, snapshot);
		}
	}

	private async Task ExecuteTimerBetAsync(string tableId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!IsRunning || _tableStates == null || _gameHistories == null)
		{
			FileLogger.Log($"[AutoBetting] {tableId}: ExecuteTimer 스킵 (Running={IsRunning}, States={_tableStates != null}, Histories={_gameHistories != null})");
			return;
		}
		if (_isCycleResting)
		{
			FileLogger.Log("[AutoBetting] " + tableId + ": 타이머 발동했으나 사이클 휴식 중 - 베팅 스킵");
			return;
		}
		if (_isProfitCoolingDown)
		{
			FileLogger.Log("[AutoBetting] " + tableId + ": 타이머 발동했으나 수익 쿨다운 중 - 베팅 스킵");
			return;
		}
		if (_isMatrixResting)
		{
			FileLogger.Log("[AutoBetting] " + tableId + ": 타이머 발동했으나 차별 휴식 중 - 베팅 스킵");
			return;
		}
		if (_isPaused)
		{
			long pauseStartTicks = Interlocked.Read(in _pauseStartTimeTicks);
			if (pauseStartTicks <= 0 || !((DateTime.Now - new DateTime(pauseStartTicks)).TotalSeconds > 45.0))
			{
				FileLogger.Log("[AutoBetting] " + tableId + ": ExecuteTimer 스킵 (일시정지 중)");
				return;
			}
			_isPaused = false;
			Interlocked.Exchange(ref _pauseStartTimeTicks, 0L);
			FileLogger.Log($"[AutoBetting] {tableId}: 일시정지 {45}초 초과, 강제 해제", "Error", "AutoBetting");
		}
		if (_selectedTables.Count > 0 && !IsSelectedTable(tableId))
		{
			FileLogger.Log("[AutoBetting] " + tableId + ": ExecuteTimer 스킵 (미선택 테이블)");
			return;
		}
		if (!IsRoomBettingEnabled(tableId))
		{
			FileLogger.Log("[AutoBetting] " + tableId + ": ExecuteTimer 스킵 (방 OFF)");
			return;
		}
		if (!_tableStates.TryGetValue(tableId, out TableState state))
		{
			FileLogger.Log("[AutoBetting] " + tableId + ": ExecuteTimer 스킵 (TableState 없음)");
			return;
		}
		if (!state.IsBettingOpen)
		{
			FileLogger.Log($"[AutoBetting] {tableId}: ExecuteTimer 스킵 (Phase={state.Phase})");
			return;
		}
		if (IsRoundBlockedByReset(tableId, state.GameId))
		{
			FileLogger.Log($"[AutoBetting] {tableId}: ExecuteTimer 스킵 (모도 초기화로 봉인된 라운드 {state.GameId})");
			return;
		}
		// Pragmatic stamps the account id into every bet frame and learns it from a seat message.
		// Evolution sends no such message — its socket is already bound to the session — so requiring
		// the id there would block every bet.
		// 판정은 계정 권한이 아니라 "이 테이블의 provider"로 해야 한다. 두 권한을 가진 계정이
		// 에볼루션을 쓰다 프라그마틱으로 오면, 권한 기준일 때 PP 테이블인데도 PpUserId 검사가
		// 건너뛰어져 빈 id로 베팅이 나가 서버가 무시한다(= 프라그마틱 베팅 미접수).
		bool tableIsEvolution = PragmaticMessageParser.IsEvolutionTable(tableId)
			|| string.Equals(state.Provider, GameProviderNames.EvolutionKey, StringComparison.OrdinalIgnoreCase);
		if (!Context.IsVirtualMode && string.IsNullOrEmpty(Context.PpUserId) && !tableIsEvolution)
		{
			FileLogger.Log("[AutoBetting] " + tableId + ": PP UserId 미설정 → 베팅 스킵 (seat 메시지 대기 중)");
			return;
		}
		RoomBettingState roomState = Context.RoomStates.GetOrAdd(tableId, (string _) => new RoomBettingState
		{
			TableId = tableId
		});
		if (roomState.IsRequestInFlight)
		{
			if (!roomState.RequestStartTime.HasValue || !((DateTime.Now - roomState.RequestStartTime.Value).TotalSeconds > 15.0))
			{
				FileLogger.Log($"[AutoBetting] {tableId}: ExecuteTimer 스킵 (RequestInFlight, 경과={(DateTime.Now - roomState.RequestStartTime.Value).TotalSeconds:F1}초)");
				return;
			}
			FileLogger.Log($"[AutoBetting] {tableId}: IsRequestInFlight 타임아웃 ({15}초), 강제 해제");
			roomState.IsRequestInFlight = false;
		}
		if (roomState.IsWaitingForResult)
		{
			// [양방향] 창이 열렸는데 직전 라운드 결과가 아직 판정되지 않았다. 즉시 스킵하면 그 라운드를 건너뛰어
			// 단계가 빅로드보다 하나 뒤처진다(= '단계 하나 빠짐'). 그래서 이 열린 창 안에서 결과가 판정될 때까지
			// (= IsWaitingForResult 해제) 잠깐 기다렸다가, 올라간 단계로 '이 라운드에' 베팅한다.
			// 베팅 시간 확보를 위해 마감 ~3.5초 전까지만 대기한다.
			DateTime waitStart = DateTime.Now;
			while (roomState.IsWaitingForResult && !cancellationToken.IsCancellationRequested
				&& IsRunning && state.IsBettingOpen && state.RemainingTimeMs > 3500.0)
			{
				try { await Task.Delay(120, cancellationToken); }
				catch (OperationCanceledException) { return; }
			}
			if (roomState.IsWaitingForResult)
			{
				FileLogger.Log($"[AutoBetting] {tableId}: ExecuteTimer 스킵 (결과 대기 {(DateTime.Now - waitStart).TotalMilliseconds:F0}ms 후에도 미판정)");
				return;
			}
			FileLogger.Log($"[AutoBetting] {tableId}: 결과 대기 {(DateTime.Now - waitStart).TotalMilliseconds:F0}ms 후 판정 완료 → 같은 라운드에 단계={roomState.CurrentStep} 진행");
			if (!IsRunning || cancellationToken.IsCancellationRequested || _isPaused || _isCycleResting
				|| _isProfitCoolingDown || _isMatrixResting || !state.IsBettingOpen || roomState.IsRequestInFlight)
			{
				return;
			}
		}
		int? maxActiveBettingRooms = Context.MaxActiveBettingRooms;
		if (maxActiveBettingRooms.HasValue && maxActiveBettingRooms.GetValueOrDefault() > 0 && roomState.CurrentStep == 1 && !roomState.IsWaitingForResult)
		{
			int activeCount = Context.RoomStates.Values.Count((RoomBettingState r) => r.CurrentStep > 1 || r.IsWaitingForResult);
			if (activeCount >= Context.MaxActiveBettingRooms.Value)
			{
				FileLogger.Log($"[AutoBetting] {tableId}: 활성방 제한 ({activeCount}/{Context.MaxActiveBettingRooms.Value}), 스킵");
				return;
			}
		}
		if (roomState.IsShoeControlBlocked)
		{
			return;
		}
		if (Context.IsRoomGateActive)
		{
			if (!(await WaitForRoundHistorySyncAsync(tableId, state, cancellationToken)))
			{
				return;
			}
			if (!IsRunning || cancellationToken.IsCancellationRequested || _isPaused || _isCycleResting || _isProfitCoolingDown || _isMatrixResting || !state.IsBettingOpen || roomState.IsWaitingForResult || roomState.IsRequestInFlight)
			{
				FileLogger.Log("[AutoBetting] " + tableId + ": ExecuteTimer 스킵 (빅로드 동기화 대기 후 상태 변경)");
				return;
			}
			RefreshRoomGate(tableId);
		}
		if (Context.IsRoomGateActive && !roomState.IsGateOpen)
		{
			if (IsSecondLogic || !IsHongKongDoubleUpPending(tableId, roomState))
			{
				return;
			}
			FileLogger.Log($"[방게이트] {tableId}: 닫힌 방이지만 홍콩크루즈 2판은 통과 (타겟={roomState.DoubleUpTarget}, 사이클 마무리 후 다음 라운드부터 차단)", "Info", "RoomGate");
		}
		if (!_gameHistories.TryGetValue(tableId, out List<GameResult> history))
		{
			FileLogger.Log("[AutoBetting] " + tableId + ": ExecuteTimer 스킵 (히스토리 없음)");
			return;
		}
		List<GameResult> historyCopy;
		lock (history)
		{
			historyCopy = new List<GameResult>(history);
		}
		string ridingTarget = null;
		bool isSecondLogicActiveRoom = false;
		if (IsSecondLogic)
		{
			lock (_lock)
			{
				isSecondLogicActiveRoom = _secondLogic.ActiveRoom == tableId;
				if (isSecondLogicActiveRoom && IsStreakRidingTargetActive)
				{
					ridingTarget = _slRidingTarget;
				}
			}
		}
		bool isDoubleUpRound = IsHongKongDoubleUpPending(tableId, roomState) && (!IsSecondLogic | isSecondLogicActiveRoom);
		// 슈 교체 직후 히스토리가 비어도, 서식 없이 직전 타겟으로 베팅하는 '줄타기/업치기 연속' 상황이면 그 한 라운드도 진행한다.
		// (예전엔 히스토리가 비면 무조건 스킵해 슈가 바뀌면 줄타기가 한 판 쉬었다 — 그 결함 수정)
		bool ridesWithoutFormat = ridingTarget != null || isDoubleUpRound
			|| (Context.SelectedBetAmountType == 15 && !string.IsNullOrEmpty(roomState.LastBetType)
				&& (roomState.IsUpchigiFlatPhase || roomState.CurrentStep > 1))
			|| (Context.IsPbCrossEnabled && !string.IsNullOrEmpty(roomState.PbCrossTarget));
		if (historyCopy.Count == 0 && !ridesWithoutFormat)
		{
			FileLogger.Log("[AutoBetting] " + tableId + ": ExecuteTimer 스킵 (히스토리 비어있음)");
			return;
		}
		if (ridingTarget == null && !isDoubleUpRound && Context.ExceptionPatterns.Count > 0)
		{
			ExceptionPattern exMatch = _patternParser.FindMatchingExceptionPattern(historyCopy, Context.ExceptionPatterns);
			if (exMatch != null)
			{
				string tailWithTie = string.Concat(from r in historyCopy.TakeLast(15)
					select (r.Winner.Length <= 0) ? '?' : char.ToUpper(r.Winner[0]));
				string tailNoTie = string.Concat(from r in historyCopy.Where((GameResult r) => !r.Winner.Equals("Tie", StringComparison.OrdinalIgnoreCase)).TakeLast(15)
					select char.ToUpper(r.Winner[0]));
				FileLogger.Log($"[패턴진단] {tableId}: 예외 매칭 #{exMatch.Number} '{exMatch.RawText}' → 스킵 | 히스토리={historyCopy.Count}, 최근(Tie포함)={tailWithTie}, 최근(P/B만)={tailNoTie}", "Info", "PatternMatch");
				return;
			}
		}
		BettingPattern matchedPattern = null;
		bool isDoubleUpForced = false;
		if (IsHongKongDoubleUpPending(tableId, roomState))
		{
			matchedPattern = new BettingPattern
			{
				Number = 0,
				RawText = "[HK_DoubleUp]",
				BetTarget = roomState.DoubleUpTarget,
				Sequence = new List<string>(),
				IsValid = true
			};
			isDoubleUpForced = true;
			FileLogger.Log("[AutoBetting] " + tableId + ": ★ 홍콩크루즈 DoubleUp 2판 - 서식 매칭 우회, 타겟=" + roomState.DoubleUpTarget);
		}
		bool isRidingForced = false;
		if (ridingTarget != null && !isDoubleUpForced)
		{
			matchedPattern = new BettingPattern
			{
				Number = 0,
				RawText = "[줄타기]",
				BetTarget = ridingTarget,
				Sequence = new List<string>(),
				IsValid = true
			};
			isRidingForced = true;
			FileLogger.Log($"[세컨로직][줄타기] {tableId}: 서식 매칭 우회 - 고정 타겟={ridingTarget} ({_secondLogic.ConsecutiveWins}연승 중, 미당첨 시 줄타기 종료 — 방이동은 연속 미당첨 규칙을 따름)");
		}
		bool isPbCrossForced = false;
		if (!isDoubleUpForced && !isRidingForced && Context.IsPbCrossEnabled)
		{
			string crossTarget = roomState.PbCrossTarget ?? "Player";
			matchedPattern = new BettingPattern
			{
				Number = 0,
				RawText = "[PB교차]",
				BetTarget = crossTarget,
				Sequence = new List<string>(),
				IsValid = true
			};
			isPbCrossForced = true;
			FileLogger.Log($"[PB교차] {tableId}: 서식 매칭 우회 - 타겟={crossTarget} (직전 베팅={roomState.LastBetType ?? "없음"}, 단계={roomState.CurrentStep})", "Info", "PatternMatch");
		}
		if (!isDoubleUpForced && !isRidingForced && !isPbCrossForced
			&& Context.SelectedBetAmountType == 15 && !string.IsNullOrEmpty(roomState.LastBetType)
			&& (roomState.IsUpchigiFlatPhase || roomState.CurrentStep > 1))
		{
			// 업치기 자동 연속: 당첨로 2단계 이상 진입했으면 서식 재매칭 없이 직전 타겟으로 자동 베팅(대상 동일).
			matchedPattern = new BettingPattern
			{
				Number = 0,
				RawText = roomState.IsUpchigiFlatPhase ? "[일정베팅]" : "[업치기]",
				BetTarget = roomState.LastBetType,
				Sequence = new List<string>(),
				IsValid = true
			};
			FileLogger.Log(roomState.IsUpchigiFlatPhase
				? $"[업치기] {tableId}: 서식 매칭 우회 - 일정베팅 자동 연속, 타겟={roomState.LastBetType}"
				: $"[업치기] {tableId}: 서식 매칭 우회 - 자동 업치기 {roomState.CurrentStep}단계, 타겟={roomState.LastBetType}");
		}
		else if (!isDoubleUpForced && !isRidingForced && !isPbCrossForced)
		{
			matchedPattern = _patternParser.FindMatchingPattern(historyCopy, Context.Patterns);
			if (matchedPattern == null)
			{
				string tailWithTie2 = string.Concat(from r in historyCopy.TakeLast(15)
					select (r.Winner.Length <= 0) ? '?' : char.ToUpper(r.Winner[0]));
				string tailNoTie2 = string.Concat(from r in historyCopy.Where((GameResult r) => !r.Winner.Equals("Tie", StringComparison.OrdinalIgnoreCase)).TakeLast(15)
					select char.ToUpper(r.Winner[0]));
				int maxSeq = ((Context.Patterns.Count > 0) ? Context.Patterns.Max((BettingPattern p) => p.Sequence.Count) : 0);
				string patternsBrief = string.Join(",", from p in Context.Patterns.Take(8)
					select $"#{p.Number}:{p.RawText}");
				if (Context.Patterns.Count > 8)
				{
					patternsBrief += $",...(+{Context.Patterns.Count - 8})";
				}
				FileLogger.Log($"[패턴진단] {tableId}: 미매칭 → 스킵 | 히스토리={historyCopy.Count}(P/B만={tailNoTie2.Length}), 최근(Tie포함)={tailWithTie2}, 최근(P/B만)={tailNoTie2}, 최대패턴길이={maxSeq}, 패턴={Context.Patterns.Count}개[{patternsBrief}]", "Info", "PatternMatch");
				return;
			}
			string matchedTail = string.Concat(from r in historyCopy.Where((GameResult r) => !r.Winner.Equals("Tie", StringComparison.OrdinalIgnoreCase)).TakeLast(matchedPattern.Sequence.Count)
				select char.ToUpper(r.Winner[0]));
			FileLogger.Log($"[패턴진단] {tableId}: ✓ 매칭 #{matchedPattern.Number} '{matchedPattern.RawText}' (시퀀스={matchedTail}, 타겟={matchedPattern.BetTarget})", "Info", "PatternMatch");
		}
		if (IsSecondLogic)
		{
			bool slProceed;
			lock (_lock)
			{
				if (_secondLogic.ActiveRoom == null)
				{
					if (tableId == _slLastRoom)
					{
						slProceed = false;
					}
					else if (ShouldYieldRoomForSplit(tableId))
					{
						slProceed = false;
					}
					else
					{
						_secondLogic.SetActiveRoom(tableId);
						_slLastRoom = null;
						_slSplitYieldCount = 0;
						_slRidingTarget = null;
						roomState.IsDoubleUpPhase = false;
						roomState.DoubleUpAmount = 0m;
						roomState.DoubleUpTarget = null;
						roomState.PbCrossTarget = _slCarryPbCross;
						roomState.PbCrossLossStreak = _slCarryPbCrossLossStreak;
						roomState.PbCrossPhaseIsBp = _slCarryPbCrossPhaseIsBp;
						_slCarryPbCross = null;
						_slCarryPbCrossLossStreak = 0;
						_slCarryPbCrossPhaseIsBp = false;
						if (IsSecondLogicMatrix)
						{
							MatrixCarry carryMatrix = _slCarryMatrix ?? CreateEmptyMatrixCarry();
							roomState.MatrixCurrentRow = carryMatrix.Row;
							roomState.MatrixCurrentCol = carryMatrix.Col;
							roomState.MatrixRowWinCount = carryMatrix.RowWinCount;
							roomState.MatrixCycleNet = carryMatrix.CycleNet;
							roomState.MatrixRowEntryNet = (decimal[])carryMatrix.RowEntryNet.Clone();
							_slCarryMatrix = null;
							FileLogger.Log($"[세컨로직][행렬] {tableId}: 새 방 시작 좌표 → {carryMatrix.Row}차-{carryMatrix.Col}단계 (누적당첨={carryMatrix.RowWinCount}, 사이클순손익={carryMatrix.CycleNet:+#,##0;-#,##0;0})");
						}
						else
						{
							maxActiveBettingRooms = _slCarryStep;
							if (maxActiveBettingRooms.HasValue)
							{
								int carry = (roomState.CurrentStep = maxActiveBettingRooms.GetValueOrDefault());
								_slCarryStep = null;
								FileLogger.Log($"[세컨로직] {tableId}: 새 방 시작 단계 → {carry}");
							}
						}
						slProceed = true;
					}
				}
				else
				{
					slProceed = _secondLogic.ActiveRoom == tableId;
				}
			}
			if (!slProceed)
			{
				return;
			}
			FileLogger.Log($"[세컨로직] {tableId}: 베팅 결정 서식=#{matchedPattern.Number} '{matchedPattern.RawText}', 타겟={matchedPattern.BetTarget}, 단계={roomState.CurrentStep}");
		}
		if (!IsSecondLogic)
		{
			CooldownCheck cooldownCheck = _cooldownService.Check(tableId);
			if (cooldownCheck.IsBlocked)
			{
				int mm = cooldownCheck.RemainingSeconds / 60;
				int ss = cooldownCheck.RemainingSeconds % 60;
				FileLogger.Log($"[AutoBetting] {tableId}: ★ 연패 쿨다운 중 (남은 {mm:D2}:{ss:D2}, {cooldownCheck.ConsecutiveLossCount}연패) → 베팅 스킵", "Info", "RoomCooldown");
				return;
			}
		}
		int step = roomState.CurrentStep;
		bool isIdleRound = false;
		decimal betAmount;
		if (_slUseCombo)
		{
			decimal mult = ComboMultiplier(step);
			betAmount = _slComboUnit * mult;
			FileLogger.Log($"[세컨로직][조합] {tableId}: 단위 {_slComboUnit:N0} × 배수 {mult} = {betAmount:N0} (단계 {step})");
		}
		else if ((Context.SelectedBetAmountType == 1 || (Context.SelectedBetAmountType == 3 && Context.TableIndexMap.GetValueOrDefault(tableId, 0) % 2 == 1)) && roomState.IsDoubleUpPhase && roomState.DoubleUpAmount > 0m)
		{
			betAmount = roomState.DoubleUpAmount;
			FileLogger.Log($"[AutoBetting] {tableId}: HK DoubleUp 2판 - 타겟={matchedPattern.BetTarget}, 금액={betAmount:N0}");
		}
		else if (Context.SelectedBetAmountType == 7)
		{
			betAmount = Context.OscarBaseAmount * (decimal)roomState.OscarCurrentBetUnits;
			if (betAmount <= 0m)
			{
				FileLogger.Log("[진단][오스카] " + tableId + ": 베팅금 0 → 스킵", "Warning", "AutoBetting");
				return;
			}
		}
		else if (Context.SelectedBetAmountType == 8)
		{
			betAmount = Context.FlatBetAmount;
			if (betAmount <= 0m)
			{
				FileLogger.Log("[진단][플랫] " + tableId + ": 베팅금 0 → 스킵", "Warning", "AutoBetting");
				return;
			}
		}
		else if (Context.SelectedBetAmountType == 15)
		{
			// 업치기(파레이): 업치기 단계면 시작금×2^(단계-1), N단계 완료 후 일정베팅 단계면 일정금.
			if (roomState.IsUpchigiFlatPhase)
			{
				// 일정금액도 시작금액과 같은 배수(누적실패 임계로 올라간 UpchigiStartMultiplier)로 올린다.
				betAmount = Context.UpchigiFlatAmount * Math.Max(1m, roomState.UpchigiStartMultiplier);
			}
			else
			{
				int expo = Math.Max(0, Math.Min(30, step - 1));
				decimal effectiveStart = Context.UpchigiStartAmount * Math.Max(1m, roomState.UpchigiStartMultiplier);
				betAmount = effectiveStart * (decimal)(1L << expo);
			}
			if (betAmount <= 0m)
			{
				FileLogger.Log($"[진단][업치기] {tableId}: 베팅금 0 → 스킵 (단계={step})", "Warning", "AutoBetting");
				return;
			}
			string upPhaseText = (roomState.IsUpchigiFlatPhase ? "일정베팅" : ("업치기 " + step + "단계"));
			string upMultText = (roomState.UpchigiStartMultiplier > 1m ? $", 시작배수 ×{roomState.UpchigiStartMultiplier:0.##}" : "");
				FileLogger.Log($"[진단][업치기] {tableId}: {upPhaseText}, 금액={betAmount:N0}{upMultText}", "Debug", "AutoBetting");
		}
		else if (Context.SelectedBetAmountType == 9)
		{
			if (roomState.LabouchereSequence.Count == 0)
			{
				roomState.LabouchereSequence = Context.LabouchereInitialSequence.ToList();
			}
			List<int> seq = roomState.LabouchereSequence;
			int num;
			if (seq.Count < 2)
			{
				num = ((seq.Count == 1) ? seq[0] : 0);
			}
			else
			{
				num = seq[0] + seq[seq.Count - 1];
			}
			int betUnits = num;
			betAmount = Context.LabouchereMultiplier * (decimal)betUnits;
			if (betAmount <= 0m)
			{
				FileLogger.Log("[진단][라부셰르] " + tableId + ": 베팅금 0 → 스킵", "Warning", "AutoBetting");
				return;
			}
			FileLogger.Log($"[진단][라부셰르] {tableId}: 수열=[{string.Join(",", seq)}], 단위={betUnits}, 금액={betAmount:N0}", "Debug", "AutoBetting");
		}
		else if (Context.IsMatrixStrategy)
		{
			int row = roomState.MatrixCurrentRow;
			int col = roomState.MatrixCurrentCol;
			if (row < 1 || row > AutoBettingContext.MatrixStepsPerRow.Length)
			{
				FileLogger.Log($"[진단][행렬계열] {tableId}: 잘못된 차={row} → 1차로 강제 복귀", "Warning", "AutoBetting");
				ResetMatrixToFirstRow(roomState);
				row = roomState.MatrixCurrentRow;
				col = roomState.MatrixCurrentCol;
			}
			int maxCol = GetEffectiveMaxCol(row);
			if (maxCol == 0)
			{
				FileLogger.Log($"[진단][행렬계열] {tableId}: {row}차에 입력된 단계가 없음 → 스킵", "Warning", "AutoBetting");
				return;
			}
			if (col < 1)
			{
				int num2 = (roomState.MatrixCurrentCol = 1);
				col = num2;
			}
			else if (col > maxCol)
			{
				if (row >= AutoBettingContext.MatrixStepsPerRow.Length)
				{
					FileLogger.Log($"[진단][행렬계열] {tableId}: {row}차-{col}단계 (입력 범위 초과) → 1차-1단계 강제 복귀", "Warning", "AutoBetting");
					ResetMatrixToFirstRow(roomState);
				}
				else
				{
					roomState.MatrixCurrentRow = row + 1;
					roomState.MatrixCurrentCol = 1;
					roomState.MatrixRowWinCount = 0;
					FileLogger.Log($"[진단][행렬계열] {tableId}: {row}차-{col}단계 (입력 범위 초과) → {roomState.MatrixCurrentRow}차-1단계 자동 전진 (누적당첨 리셋)", "Warning", "AutoBetting");
				}
				return;
			}
			betAmount = Context.MatrixCells[row - 1][col - 1];
			if (betAmount <= 0m)
			{
				isIdleRound = true;
				betAmount = 0m;
				FileLogger.Log($"[AutoBetting][행렬] {tableId}: {row}차-{col}단계 0원 → 관망 라운드 (베팅 없이 단계 진행)");
			}
			(int, int)? diagRule = GetMatrixRowRule(row);
			string diagRuleText = (diagRule.HasValue ? $"{diagRule.Value.Item1}승→{diagRule.Value.Item2}차" : ((row >= 2) ? "규칙없음(회수판정)" : "규칙없음"));
			FileLogger.Log($"[진단][행렬계열] {tableId}: {row}차-{col}단계 (실효 max={maxCol}), 금액={betAmount:N0}, 누적당첨={roomState.MatrixRowWinCount}, 이동규칙={diagRuleText}, 사이클순손익={roomState.MatrixCycleNet:+#,##0;-#,##0;0}", "Debug", "AutoBetting");
		}
		else if (Context.SelectedBetAmountType == 10)
		{
			if (!roomState.IsDalmenzingRecoveryPhase)
			{
				betAmount = Context.DalmenzingDAlembertBaseAmount * (decimal)step;
			}
			else
			{
				int recoveryStep = roomState.DalmenzingRecoveryStep;
				betAmount = ((Context.DalmenzingRecoveryMethod != 1) ? Context.DalmenzingMartinAmounts.GetValueOrDefault(recoveryStep, 0m) : ((!roomState.DalmenzingRecoveryDoubleUpPhase || !(roomState.DalmenzingRecoveryDoubleUpAmount > 0m)) ? Context.DalmenzingHongKongAmounts.GetValueOrDefault(recoveryStep, 0m) : roomState.DalmenzingRecoveryDoubleUpAmount));
			}
			if (betAmount <= 0m)
			{
				Dictionary<int, decimal> recoveryTable = ((Context.DalmenzingRecoveryMethod == 1) ? Context.DalmenzingHongKongAmounts : Context.DalmenzingMartinAmounts);
				if (!roomState.IsDalmenzingRecoveryPhase || !recoveryTable.ContainsKey(roomState.DalmenzingRecoveryStep))
				{
					FileLogger.Log($"[진단][달랑멘징] {tableId}: 베팅금 0 → 스킵 (페이즈={(roomState.IsDalmenzingRecoveryPhase ? "복구" : "달랑")}, 달랑단계={step}, 복구단계={roomState.DalmenzingRecoveryStep})", "Warning", "AutoBetting");
					return;
				}
				isIdleRound = true;
				betAmount = 0m;
				FileLogger.Log($"[AutoBetting][달랑멘징] {tableId}: 복구 {roomState.DalmenzingRecoveryStep}단계 0원 → 관망 라운드 (베팅 없이 단계 진행)");
			}
			FileLogger.Log($"[진단][달랑멘징] {tableId}: 페이즈={(roomState.IsDalmenzingRecoveryPhase ? "복구" : "달랑")}, 금액={betAmount:N0}", "Debug", "AutoBetting");
		}
		else
		{
			betAmount = GetBetAmountForTable(tableId, step);
			if (betAmount <= 0m)
			{
				if (!HasBetAmountForTable(tableId, step))
				{
					if (Context.SelectedBetAmountType == 3)
					{
						int idx = Context.TableIndexMap.GetValueOrDefault(tableId, 0);
						string methodName = ((idx % 2 == 0) ? "마틴" : "홍콩크루즈");
						FileLogger.Log($"[AutoBetting][혼합] {tableId}: 방{idx + 1}({methodName}) 단계 {step} 금액 0 → 베팅 skip (휴업)");
					}
					else
					{
						FileLogger.Log($"[AutoBetting] {tableId}: {step}단계 베팅금 없음");
					}
					return;
				}
				isIdleRound = true;
				betAmount = 0m;
				FileLogger.Log($"[AutoBetting] {tableId}: {step}단계 0원 → 관망 라운드 (베팅 없이 단계 진행)");
			}
		}
		if (!CheckLimits())
		{
			decimal effectiveBal = Context.CurrentBalance - Context.PendingBetAmount;
			string blockReason = (_isProfitCoolingDown ? "수익 쿨다운 중" : (_isCycleResting ? "사이클 휴식 중" : (_isMatrixResting ? "차별 휴식 중" : "상한/하한 초과")));
			FileLogger.Log($"[AutoBetting] {tableId}: 패턴 {matchedPattern.RawText} 매칭, BUT {blockReason} (유효잔액={effectiveBal:N0}, 시작={Context.StartBalance:N0}), 스킵", "Warning", "AutoBetting");
			return;
		}
		if (Context.AvailableBalance < betAmount)
		{
			FileLogger.Log($"[AutoBetting] {tableId}: 잔액 부족으로 스킵 (필요={betAmount:N0}, 가용={Context.AvailableBalance:N0})", "Warning", "AutoBetting");
			return;
		}
		roomState.IsRequestInFlight = true;
		roomState.RequestStartTime = DateTime.Now;
		string betType = matchedPattern.BetTarget;
		FileLogger.Log($"[AutoBetting] ★ {tableId}: {(isIdleRound ? "관망" : "베팅")} 시도 패턴={matchedPattern.RawText}, 타겟={betType}, 단계={step}, 금액={betAmount:N0}");
		try
		{
			decimal preBetBalance = (roomState.PreBetBalance = Context.AvailableBalance);
			Context.PendingBetAmount += betAmount;
			FileLogger.Log($"[AutoBetting] {tableId}: PendingBet +{betAmount:N0} → 합계={Context.PendingBetAmount:N0}");
			PendingBetAmountChanged?.Invoke(Context.PendingBetAmount);
			string siteSplitRemark = null;
			BetResult result;
			if (isIdleRound)
			{
				result = BetResult.Ok("IDLE");
				FileLogger.Log($"[AutoBetting][관망] {tableId}: {betType} 0원 - 베팅 전송 없이 결과만 대기 (단계 {step})");
			}
			else if (Context.IsVirtualMode)
			{
				result = BetResult.Ok("VIRTUAL");
				FileLogger.Log($"[AutoBetting][가상] {tableId}: 가상 베팅 - {betType} {betAmount:N0}");
			}
			else if (IsMultiSiteConfigured)
			{
				SplitBetOutcome outcome = await PlaceSplitBetAsync(tableId, state, betType, betAmount);
				if (outcome.StopRequested)
				{
					FileLogger.Log("[AutoBetting] " + tableId + ": ★ 분할 베팅 정지 신호 - " + outcome.StopReason, "Error", "MultiSite");
					result = BetResult.Fail(outcome.StopReason ?? "사이트 잔액 부족");
					Task.Run(async delegate
					{
						await StopAsync(outcome.StopReason);
					});
				}
				else if (!outcome.AnySuccess)
				{
					result = BetResult.Fail("모든 사이트 베팅 전송 실패");
				}
				else
				{
					result = (outcome.AllUnconfirmed ? BetResult.Unconfirmed("MULTI-SITE") : BetResult.Ok("MULTI-SITE"));
					siteSplitRemark = outcome.SiteSplitRemark;
					decimal unsent = betAmount - outcome.ActualAmount;
					if (unsent > 0m)
					{
						Context.PendingBetAmount -= unsent;
						if (Context.PendingBetAmount < 0m)
						{
							Context.PendingBetAmount = 0m;
						}
						FileLogger.Log($"[AutoBetting] {tableId}: 분할 일부 실패 → PendingBet -{unsent:N0} (단계금액 {betAmount:N0} → 실제 전송 {outcome.ActualAmount:N0})", "Warning", "MultiSite");
						PendingBetAmountChanged?.Invoke(Context.PendingBetAmount);
					}
					betAmount = outcome.ActualAmount;
				}
			}
			else
			{
				double remainAtCall = state.RemainingTimeMs;
				var swBet = System.Diagnostics.Stopwatch.StartNew();
				result = await _bettingService.PlaceBetAsync(state, betType, betAmount, Context.PpUserId);
				// 회원님 가설 검증용: 베팅 호출 시점의 남은시간 + 처리에 걸린 시간 + 남은시간(호출 후 추정).
				FileLogger.Log($"[베팅시간] {tableId}: 호출시 남은={remainAtCall:F0}ms, 처리={swBet.ElapsedMilliseconds}ms, 결과={(result.IsSuccess ? "성공" : "실패:" + result.ErrorMessage)}", "Info", "Betting");
			}
			// Evolution acknowledges every accepted bet with baccarat.playerBettingState. When that
			// never arrives the bet was not taken — typically because the browser is sitting in the
			// lobby, whose socket streams table state but accepts no wagers. Booking it anyway is
			// what made the bot report stakes and winnings that never existed.
			if (result.IsSuccess && result.IsUnconfirmed
				&& string.Equals(state.Provider, GameProviderNames.EvolutionKey, StringComparison.OrdinalIgnoreCase))
			{
				FileLogger.Log($"[AutoBetting] {tableId}: ✗ 에볼루션 확인 응답 없음 → 미접수 처리 (금액={betAmount:N0}) — 테이블에 입장했는지 확인하세요", "Error", "AutoBetting");
				result = BetResult.Fail("에볼루션이 베팅을 확인하지 않았습니다 (테이블 미입장으로 추정)");
			}
			// 실제 페이지에 걸린 금액이 의도액과 다르면(DOM 클릭 누락 등, 예 80,000 의도 → 70,000 실제)
			// 실제 금액으로 회계를 맞춘다. PendingBet 도 차액만큼 보정 → 이후 LastBetAmount/TotalBet/
			// 손익/서버기록이 전부 실제 금액 기준이 된다. (분할 경로가 outcome.ActualAmount 로 하는 것과 동일)
			if (result.IsSuccess && result.ActualAmount is decimal actualBet && actualBet > 0m && actualBet != betAmount)
			{
				decimal diff = betAmount - actualBet;   // >0: 덜 걸림(과예약), <0: 더 걸림
				Context.PendingBetAmount -= diff;
				if (Context.PendingBetAmount < 0m) { Context.PendingBetAmount = 0m; }
				PendingBetAmountChanged?.Invoke(Context.PendingBetAmount);
				FileLogger.Log($"[AutoBetting] {tableId}: 실제 베팅액 보정 {betAmount:N0} → {actualBet:N0} (실제 페이지 금액 기준, PendingBet {(diff >= 0m ? "-" : "+")}{Math.Abs(diff):N0})", "Warning", "Betting");
				betAmount = actualBet;
			}
			if (result.IsSuccess)
			{
				// 새 베팅이 정상 접수됐으니, 아직 미해소된 이전 provisional(미접수 추적)은 폐기한다.
				roomState.ProvisionalGameId = null;
				roomState.LastBetGameId = state.GameId;
				roomState.LastBetType = betType;
				roomState.LastBetAmount = betAmount;
				roomState.IsWaitingForResult = true;
				roomState.LastBetTime = DateTime.Now;
				roomState.LastProcessedHistoryCount = historyCopy.Count;
				roomState.BetVersion++;
				roomState.IsIdleRound = isIdleRound;
				Context.TotalBetAmount += betAmount;
				Context.LastGlobalBetTime = DateTime.Now;
				if (!Context.IsVirtualMode && !isIdleRound)
				{
					Volatile.Write(ref _lastRealBetSentTicks, DateTime.Now.Ticks);
				}
				if (result.IsUnconfirmed)
				{
					roomState.IsUnconfirmedBet = true;
					roomState.IsLateConfirmed = false;
					FileLogger.Log($"[AutoBetting] {tableId}: 베팅 미확인(PP 타임아웃) → 사후검증 추적 시작 (preBal={preBetBalance:N0}, betAmt={betAmount:N0})", "Warning", "AutoBetting");
				}
				else
				{
					roomState.IsUnconfirmedBet = false;
				}
				if (isIdleRound)
				{
					FileLogger.Log($"[AutoBetting][관망] {tableId}: 결과 대기 시작 (TotalBet 변동 없음, 합계={Context.TotalBetAmount:N0})");
				}
				else
				{
					FileLogger.Log($"[AutoBetting] {tableId}: 베팅 성공 → TotalBet +{betAmount:N0} → 합계={Context.TotalBetAmount:N0}");
				}
				if (!isIdleRound && siteSplitRemark == null && !Context.IsVirtualMode)
				{
					try
					{
						await GetApi().SaveBettingHistoryAsync(betType, betAmount, "Pending", Context.CurrentBalance, Context.CurrentBalance - betAmount, Context.IsVirtualMode, tableId, state.GameId, Context.IsMatrixStrategy ? new int?(Context.SelectedBetAmountType) : ((int?)null), Context.IsMatrixStrategy ? new int?(roomState.MatrixCurrentRow) : ((int?)null), Context.IsMatrixStrategy ? new int?(roomState.MatrixCurrentCol) : ((int?)null));
					}
					catch (Exception ex)
					{
						FileLogger.Log("[AutoBetting] " + tableId + ": 베팅 내역 저장 실패: " + ex.Message, "Warning", "AutoBetting");
					}
				}
				BetExecuted?.Invoke(this, new AutoBettingExecutedEventArgs
				{
					TableId = tableId,
					GameId = state.GameId,
					BetType = betType,
					Amount = betAmount,
					Step = step,
					StepVersion = roomState.StepVersion,
					MatchedPattern = matchedPattern.RawText,
					IsSuccess = true,
					IsIdleRound = isIdleRound,
					MatrixRow = (Context.IsMatrixStrategy ? roomState.MatrixCurrentRow : 0),
					MatrixCol = (Context.IsMatrixStrategy ? roomState.MatrixCurrentCol : 0),
					CycleIndex = roomState.CycleIndex,
					StartMultiplier = roomState.UpchigiStartMultiplier,
					FailAccum = roomState.UpchigiFailAccum,
					SiteSplitRemark = siteSplitRemark
				});
			}
			else
			{
				Context.PendingBetAmount -= betAmount;
				if (Context.PendingBetAmount < 0m)
				{
					Context.PendingBetAmount = 0m;
				}
				// 2차 판정 준비: '확인 미수신(미접수)'만 provisional 로 기억한다(거절/반려는 제외 — 가드 ②).
				// 같은 gameId 의 win/결과(접수 증거)가 오면 TryReconcileUnconfirmedBet 에서 승격·정산한다.
				if (result.IsTimeoutUnconfirmed && !isIdleRound && !string.IsNullOrEmpty(state.GameId))
				{
					roomState.ProvisionalGameId = state.GameId;
					roomState.ProvisionalAmount = betAmount;
					roomState.ProvisionalTarget = betType;
					roomState.ProvisionalTime = DateTime.Now;
					FileLogger.Log($"[AutoBetting] {tableId}: 미접수 베팅 provisional 기록 (game={state.GameId}, 금액={betAmount:N0}, 타겟={betType}) — win/결과 증거 대기", "Warning", "AutoBetting");
				}
				else
				{
					roomState.ProvisionalGameId = null;   // 거절 등은 사후 보정 대상 아님
				}
				FileLogger.Log($"[AutoBetting] {tableId}: 베팅 실패 → PendingBet -{betAmount:N0} → 합계={Context.PendingBetAmount:N0}, 사유={result.ErrorMessage}");
				PendingBetAmountChanged?.Invoke(Context.PendingBetAmount);
				BetExecuted?.Invoke(this, new AutoBettingExecutedEventArgs
				{
					TableId = tableId,
					GameId = state.GameId,
					BetType = betType,
					Amount = betAmount,
					Step = step,
					StepVersion = roomState.StepVersion,
					MatchedPattern = matchedPattern.RawText,
					IsSuccess = false,
					ErrorMessage = result.ErrorMessage,
					IsIdleRound = isIdleRound,
					MatrixRow = (Context.IsMatrixStrategy ? roomState.MatrixCurrentRow : 0),
					MatrixCol = (Context.IsMatrixStrategy ? roomState.MatrixCurrentCol : 0),
					CycleIndex = roomState.CycleIndex,
					StartMultiplier = roomState.UpchigiStartMultiplier,
					FailAccum = roomState.UpchigiFailAccum,
					SiteSplitRemark = siteSplitRemark
				});
			}
		}
		catch (Exception ex2)
		{
			Context.PendingBetAmount -= betAmount;
			if (Context.PendingBetAmount < 0m)
			{
				Context.PendingBetAmount = 0m;
			}
			PendingBetAmountChanged?.Invoke(Context.PendingBetAmount);
			FileLogger.Log("[AutoBetting] " + tableId + ": 베팅 예외 - " + ex2.Message, "Error", "AutoBetting");
		}
		finally
		{
			roomState.IsRequestInFlight = false;
			roomState.RequestStartTime = null;
			if (roomState.IsResetDeferred && !roomState.IsWaitingForResult)
			{
				lock (_lock)
				{
					if (roomState.IsResetDeferred && !roomState.IsWaitingForResult)
					{
						ApplyDeferredResetIfNeeded(tableId, roomState);
					}
				}
			}
		}
	}

	private async Task<SplitBetOutcome> PlaceSplitBetAsync(string tableId, TableState masterState, string betType, decimal amount)
	{
		RequestRoomRecoveryForStaleSites(tableId);
		List<BettingSite> sites = (_multiSites ?? Array.Empty<BettingSite>()).Where((BettingSite s) => s.IsActive && IsSiteReadyForBet(s.SiteSlot, tableId)).ToList();
		int maxSites = _getSplitCount?.Invoke() ?? 1;
		decimal perSiteLimit = (Context.PerSiteLimit >= 10000m ? Context.PerSiteLimit : 5000000m);
		decimal splitThreshold = (Context.SplitThreshold >= 10000m ? Context.SplitThreshold : 40000m);
		BetAllocationService.AllocationResult allocation = _betAllocationService.Allocate(amount, sites, maxSites, perSiteLimit, splitThreshold);
		if (allocation.ExceedsPerSiteLimit)
		{
			return new SplitBetOutcome
			{
				StopRequested = true,
				StopReason = $"분할해도 사이트당 {perSiteLimit:N0}원 초과 (베팅금 {amount:N0}, 최대 {maxSites}곳)"
			};
		}
		IReadOnlyList<SiteBetSplit> splits = allocation.Splits;
		if (splits.Count == 0)
		{
			FileLogger.Log($"[AutoBetting] {tableId}: 분할 결과 없음 - 베팅 준비된 사이트가 없음 (준비 사이트 {sites.Count}곳)", "Warning", "MultiSite");
			return new SplitBetOutcome
			{
				AnySuccess = false,
				ActualAmount = 0m
			};
		}
		foreach (SiteBetSplit split in splits)
		{
			decimal available = GetSiteAvailableBalance(split.SiteSlot);
			if (split.SiteSlot == 0)
			{
				available += amount;
			}
			if (available < split.Amount)
			{
				return new SplitBetOutcome
				{
					StopRequested = true,
					StopReason = $"사이트{split.SiteSlot + 1} 잔액 부족 (가용 {available:N0} < 필요 {split.Amount:N0})"
				};
			}
		}
		(SiteBetSplit, BetResult)[] obj = await Task.WhenAll(splits.Select(async delegate(SiteBetSplit siteBetSplit)
		{
			var (svc, siteState, ppUserId) = ResolveSite(siteBetSplit.SiteSlot, tableId, masterState);
			BetResult r2;
			if (svc == null || siteState == null || string.IsNullOrEmpty(ppUserId))
			{
				r2 = BetResult.Fail($"사이트{siteBetSplit.SiteSlot + 1} 세션/테이블 상태 없음");
				FileLogger.Log($"[AutoBetting] {tableId}: 사이트{siteBetSplit.SiteSlot + 1} 베팅 불가 - 세션/테이블/PpUserId 미확보", "Warning", "MultiSite");
			}
			else
			{
				r2 = await svc.PlaceBetAsync(siteState, betType, siteBetSplit.Amount, ppUserId);
			}
			FileLogger.Log($"[AutoBetting] {tableId}: 사이트{siteBetSplit.SiteSlot + 1}({siteBetSplit.Domain}) 분할 {siteBetSplit.Amount:N0} → {((!r2.IsSuccess) ? ("실패: " + r2.ErrorMessage) : (r2.IsUnconfirmed ? "미확인" : "성공"))}", "Info", "MultiSite");
			return (split: siteBetSplit, r: r2);
		}).ToList());
		decimal actualTotal = 0m;
		bool anySuccess = false;
		bool anyConfirmed = false;
		List<BettingAllocationDto> allocations = new List<BettingAllocationDto>();
		(SiteBetSplit, BetResult)[] array = obj;
		for (int num = 0; num < array.Length; num++)
		{
			var (split2, r) = array[num];
			if (r.IsSuccess)
			{
				anySuccess = true;
				actualTotal += split2.Amount;
				if (!r.IsUnconfirmed)
				{
					anyConfirmed = true;
				}
			}
			allocations.Add(new BettingAllocationDto(split2.SiteSlot, split2.Domain, split2.Amount, r.IsSuccess, r.IsSuccess ? (r.IsUnconfirmed ? "Unconfirmed" : "Sent") : "Failed"));
		}
		if (anySuccess)
		{
			await SaveSplitBettingHistoryAsync(betType, actualTotal, allocations, tableId, masterState.GameId);
		}
		string siteSplitRemark = string.Join(" / ", allocations.OrderBy((BettingAllocationDto a) => a.SiteSlot).Select((BettingAllocationDto a, int i) => $"{i + 1}번 {a.AllocatedAmount:N0}" + (a.IsSent ? "" : "(실패)")));
		return new SplitBetOutcome
		{
			AnySuccess = anySuccess,
			AllUnconfirmed = (anySuccess && !anyConfirmed),
			ActualAmount = actualTotal,
			SiteSplitRemark = siteSplitRemark
		};
	}

	private bool ShouldYieldRoomForSplit(string tableId)
	{
		if (!IsMultiSiteConfigured)
		{
			return false;
		}
		int readyCount = (_multiSites ?? Array.Empty<BettingSite>()).Count((BettingSite s) => s.IsActive && IsSiteReadyForBet(s.SiteSlot, tableId));
		if (readyCount >= 2)
		{
			return false;
		}
		if (_slSplitYieldCount >= 3)
		{
			FileLogger.Log($"[세컨로직] {tableId}: 분할 가능한 방을 {3}회 찾지 못해 준비된 {readyCount}곳으로 진행합니다 (보조 사이트 상태를 확인하세요).", "Warning", "MultiSite");
			return false;
		}
		_slSplitYieldCount++;
		FileLogger.Log($"[세컨로직] {tableId}: 분할 불가(준비 {readyCount}곳) → 활성 방 선택 양보 ({_slSplitYieldCount}/{3})", "Info", "MultiSite");
		return true;
	}

	private void RequestRoomRecoveryForStaleSites(string tableId)
	{
		if (_requestRoomRecovery == null || _multiSites == null)
		{
			return;
		}
		foreach (BettingSite item in _multiSites.Where((BettingSite s) => s.IsActive && s.SiteSlot > 0))
		{
			int slot = item.SiteSlot;
			if (_balanceGettersBySlot != null && _balanceGettersBySlot.TryGetValue(slot, out Func<decimal?> balanceGetter) && balanceGetter().HasValue && _ppUserIdGettersBySlot != null && _ppUserIdGettersBySlot.TryGetValue(slot, out Func<string> userIdGetter) && !string.IsNullOrEmpty(userIdGetter()) && IsRoomStaleOnSite(slot, tableId))
			{
				_requestRoomRecovery(slot, tableId);
			}
		}
	}

	private bool IsRoomStaleOnSite(int slot, string tableId)
	{
		if (_tableStatesBySlot == null || !_tableStatesBySlot.TryGetValue(slot, out ConcurrentDictionary<string, TableState> states) || !states.TryGetValue(tableId, out var state))
		{
			return true;
		}
		return (DateTime.Now - state.LastUpdated).TotalSeconds > 120.0;
	}

	private bool IsSiteReadyForBet(int siteSlot, string tableId)
	{
		if (siteSlot == 0)
		{
			return !string.IsNullOrEmpty(Context.PpUserId);
		}
		if (_balanceGettersBySlot == null || !_balanceGettersBySlot.TryGetValue(siteSlot, out Func<decimal?> balanceGetter) || !balanceGetter().HasValue)
		{
			return false;
		}
		if (_ppUserIdGettersBySlot == null || !_ppUserIdGettersBySlot.TryGetValue(siteSlot, out Func<string> userIdGetter) || string.IsNullOrEmpty(userIdGetter()))
		{
			return false;
		}
		return IsRoomBettableOnSite(siteSlot, tableId);
	}

	private bool IsRoomBettableOnSite(int siteSlot, string tableId)
	{
		if (_tableStatesBySlot == null || !_tableStatesBySlot.TryGetValue(siteSlot, out ConcurrentDictionary<string, TableState> states) || !states.TryGetValue(tableId, out var state))
		{
			return false;
		}
		if (state.IsBettingOpen)
		{
			return !string.IsNullOrEmpty(state.GameId);
		}
		return false;
	}

	private decimal GetSiteAvailableBalance(int siteSlot)
	{
		if (siteSlot == 0)
		{
			return _getMasterBalance?.Invoke() ?? Context.AvailableBalance;
		}
		if (_balanceGettersBySlot == null || !_balanceGettersBySlot.TryGetValue(siteSlot, out Func<decimal?> getter))
		{
			return 0m;
		}
		return getter().GetValueOrDefault();
	}

	private (PragmaticBettingService? Service, TableState? State, string PpUserId) ResolveSite(int siteSlot, string tableId, TableState masterState)
	{
		if (siteSlot == 0)
		{
			return (Service: _bettingService, State: masterState, PpUserId: Context.PpUserId);
		}
		PragmaticBettingService svc = null;
		_bettingServicesBySlot?.TryGetValue(siteSlot, out svc);
		TableState state = null;
		if (_tableStatesBySlot != null && _tableStatesBySlot.TryGetValue(siteSlot, out ConcurrentDictionary<string, TableState> states))
		{
			states.TryGetValue(tableId, out state);
		}
		string ppUserId = ((_ppUserIdGettersBySlot != null && _ppUserIdGettersBySlot.TryGetValue(siteSlot, out Func<string> getter)) ? getter() : string.Empty);
		return (Service: svc, State: state, PpUserId: ppUserId);
	}

	private async Task SaveSplitBettingHistoryAsync(string betType, decimal actualTotal, List<BettingAllocationDto> allocations, string tableId, string gameId)
	{
		try
		{
			decimal balanceBefore = Context.CurrentBalance;
			int? matrixRow = null;
			int? matrixCol = null;
			int? betAmountType = null;
			if (Context.IsMatrixStrategy && Context.RoomStates.TryGetValue(tableId, out RoomBettingState rs))
			{
				betAmountType = Context.SelectedBetAmountType;
				matrixRow = rs.MatrixCurrentRow;
				matrixCol = rs.MatrixCurrentCol;
			}
			if (!Context.IsVirtualMode)  // 가상베팅은 서버 미기록(로컬 검증용)
			{
				await GetApi().SaveBettingHistoryWithAllocationsAsync(new BettingHistoryWithAllocationsDto(betType, actualTotal, "Pending", balanceBefore, balanceBefore - actualTotal, allocations, tableId, gameId, betAmountType, matrixRow, matrixCol, Context.IsVirtualMode));
			}
			FileLogger.Log($"[AutoBetting] 분할 베팅 내역 저장: {betType} {actualTotal:N0} ({allocations.Count}개 사이트)", "Info", "MultiSite");
		}
		catch (Exception ex)
		{
			FileLogger.Log("[AutoBetting] 분할 베팅 내역 저장 실패: " + ex.Message, "Warning", "MultiSite");
		}
	}

	private void JudgeResult(string tableId, int newHistoryCount, GameResult lastResult)
	{
		int nextStep = 0;
		bool shouldFireStepReset = false;
		bool shouldFireLastStepReset = false;
		bool shouldLowerPeakSilently = false;
		int stepVersion = 0;
		int globalStepVersion = 0;
		bool shouldStopForChain = false;
		int nextMatrixRow = 0;
		int nextMatrixCol = 0;
		int matrixRestMinutes = 0;
		bool slRoomSwitched = false;
		bool isTie;
		bool isWin;
		string lastBetType;
		string lastBetGameId;
		decimal lastBetAmount;
		bool isIdleRound;
		lock (_lock)
		{
			if (!Context.RoomStates.TryGetValue(tableId, out RoomBettingState roomState) || !roomState.IsWaitingForResult)
			{
				return;
			}
			// 원래는 '히스토리 개수 증가(newHistoryCount > LastProcessedHistoryCount)'로만 새 결과를 감지했다.
			// 그런데 Evolution 로드맵이 history_v2 로 재구성되며 개수가 줄면(예 72→66) 유효한 결과가
			// '68<=72' 로 스킵되어, 결과가 왔는데도 베팅이 영영 미정산(베팅중)으로 남았다.
			// → 결과의 gameId 가 이 베팅의 라운드(LastBetGameId)와 일치하면 그 라운드가 끝났다는 확정
			//   신호이므로, 히스토리 개수와 무관하게 정산한다. (정산 후 IsWaitingForResult=false 라 재정산 없음)
			bool countAdvanced = newHistoryCount > roomState.LastProcessedHistoryCount;
			bool roundMatched = !string.IsNullOrEmpty(lastResult.GameId)
				&& string.Equals(lastResult.GameId, roomState.LastBetGameId, StringComparison.OrdinalIgnoreCase);
			if (!countAdvanced && !roundMatched)
			{
				return;
			}
			int stepBefore = roomState.CurrentStep;
			isTie = string.Equals(lastResult.Winner, "Tie", StringComparison.OrdinalIgnoreCase);
			isWin = !isTie && string.Equals(roomState.LastBetType, lastResult.Winner, StringComparison.OrdinalIgnoreCase);
			lastBetType = roomState.LastBetType;
			lastBetGameId = roomState.LastBetGameId;
			lastBetAmount = roomState.LastBetAmount;
			isIdleRound = roomState.IsIdleRound;
			roomState.IsIdleRound = false;
			if (Context.IsPbCrossEnabled && !isTie && !string.IsNullOrEmpty(roomState.LastBetType))
			{
				string nextCrossTarget = (string.Equals(roomState.LastBetType, "Player", StringComparison.OrdinalIgnoreCase) ? "Banker" : "Player");
				if (Context.IsBpCrossOnTwoLossEnabled && !isIdleRound)
				{
					if (isWin)
					{
						roomState.PbCrossLossStreak = 0;
					}
					else if (++roomState.PbCrossLossStreak >= 2)
					{
						roomState.PbCrossLossStreak = 0;
						roomState.PbCrossPhaseIsBp = !roomState.PbCrossPhaseIsBp;
						string forcedTarget = (roomState.PbCrossPhaseIsBp ? "Banker" : "Player");
						FileLogger.Log($"[PB교차][2연패] {tableId}: {2}연패 → 위상 {(roomState.PbCrossPhaseIsBp ? "PB→BP" : "BP→PB")} 전환, 다음 타겟={forcedTarget} (교차만 돌았다면 {nextCrossTarget})", "Info", "PatternMatch");
						nextCrossTarget = forcedTarget;
					}
				}
				roomState.PbCrossTarget = nextCrossTarget;
			}
			FileLogger.Log($"[AutoBetting] {tableId}: 판정{(isIdleRound ? "(관망)" : "")} - Winner='{lastResult.Winner}', 베팅='{lastBetType}', isTie={isTie}, isWin={isWin}");
			int effectiveMethod;
			if (Context.SelectedBetAmountType == 3)
			{
				int tableIndex = Context.TableIndexMap.GetValueOrDefault(tableId, 0);
				effectiveMethod = ((tableIndex % 2 != 0) ? 1 : 0);
				FileLogger.Log($"[AutoBetting][혼합] {tableId}: 방{tableIndex + 1} 방식={GetBetAmountTypeName(effectiveMethod)}");
			}
			else if (Context.SelectedBetAmountType == 5)
			{
				effectiveMethod = (Context.IsDAlembertPhase ? 4 : 0);
				FileLogger.Log($"[AutoBetting][마틴+달랑] {tableId}: 페이즈={(Context.IsDAlembertPhase ? "달랑베르" : "마틴")}, 단계={roomState.CurrentStep}");
			}
			else
			{
				effectiveMethod = Context.SelectedBetAmountType;
			}
			switch (effectiveMethod)
			{
			case 1:
				HandleHongKongCruiseResult(tableId, roomState, isTie, isWin, stepBefore, ref shouldFireLastStepReset, ref shouldFireStepReset);
				break;
			case 2:
				HandleFibonacciResult(tableId, roomState, isTie, isWin, stepBefore, ref shouldFireLastStepReset, ref shouldFireStepReset);
				break;
			case 4:
				HandleDAlembertResult(tableId, roomState, isTie, isWin, stepBefore, ref shouldFireLastStepReset, ref shouldFireStepReset, ref shouldLowerPeakSilently);
				break;
			case 6:
				Handle1326Result(tableId, roomState, isTie, isWin, stepBefore, ref nextStep, ref shouldFireLastStepReset, ref shouldFireStepReset);
				break;
			case 7:
				HandleOscarGrindResult(tableId, roomState, isTie, isWin, stepBefore, ref nextStep, ref shouldFireLastStepReset, ref shouldFireStepReset);
				break;
			case 8:
				HandleFlatBettingResult(tableId, roomState, isTie, isWin, stepBefore, ref nextStep, ref shouldFireLastStepReset, ref shouldFireStepReset);
				break;
			case 9:
				HandleLabouchereResult(tableId, roomState, isTie, isWin, stepBefore, ref nextStep, ref shouldFireLastStepReset, ref shouldFireStepReset);
				break;
			case 10:
				HandleDalmenzingResult(tableId, roomState, isTie, isWin, stepBefore, ref shouldFireLastStepReset, ref shouldFireStepReset);
				break;
			case 15:
				HandleUpchigiResult(tableId, roomState, isTie, isWin, stepBefore, ref shouldFireLastStepReset, ref shouldFireStepReset);
				break;
			default:
				if (AutoBettingContext.IsMatrixType(effectiveMethod))
				{
					HandleMatrixResult(tableId, roomState, isTie, isWin, ref shouldFireLastStepReset, ref shouldFireStepReset, ref matrixRestMinutes);
				}
				else
				{
					HandleMartinResult(tableId, roomState, isTie, isWin, stepBefore, ref shouldFireLastStepReset, ref shouldFireStepReset);
				}
				break;
			}
			bool slDeferForDoubleUp = IsSecondLogicHongKong && roomState.IsDoubleUpPhase;
			if (slDeferForDoubleUp)
			{
				int slLossesCleared = ((isWin && _secondLogic.ActiveRoom == tableId) ? _secondLogic.NotifyInterimWin() : 0);
				FileLogger.Log($"[세컨로직][홍콩] {tableId}: {(isTie ? "타이(무)" : (isWin ? "1판 승리" : "미당첨"))} → 2판 대기 (방 이동 판정 보류, 2판 금액={roomState.DoubleUpAmount:N0}, 타겟={roomState.DoubleUpTarget}" + ((slLossesCleared > 0) ? $", 연속미당첨 {slLossesCleared}→0 리셋" : "") + ")");
			}
			if (IsSecondLogic && !slDeferForDoubleUp && _secondLogic.ActiveRoom == tableId)
			{
				SecondLogicEngine.Outcome slOutcome = (isTie ? SecondLogicEngine.Outcome.Tie : ((!isWin) ? SecondLogicEngine.Outcome.Lose : SecondLogicEngine.Outcome.Win));
				SecondLogicEngine.Transition slTr = _secondLogic.ApplyResult(slOutcome);
				if (slTr.SwitchRoom)
				{
					_slLastRoom = tableId;
					_slSplitYieldCount = 0;
					slRoomSwitched = true;
					if (IsSecondLogicMatrix)
					{
						_slCarryMatrix = CaptureMatrixCarry(roomState);
					}
					else if (IsSecondLogicDAlembert)
					{
						_slCarryStep = roomState.CurrentStep;
					}
					else if (IsSecondLogicHongKong)
					{
						_slCarryStep = roomState.CurrentStep;
					}
					else
					{
						_slCarryStep = (slTr.StepReset ? 1 : roomState.CurrentStep);
					}
					_slCarryPbCross = roomState.PbCrossTarget;
					_slCarryPbCrossLossStreak = roomState.PbCrossLossStreak;
					_slCarryPbCrossPhaseIsBp = roomState.PbCrossPhaseIsBp;
				}
				if (IsStreakRidingTargetActive && !isTie)
				{
					_slRidingTarget = ((isWin && !slTr.SwitchRoom) ? lastBetType : null);
				}
				string slProgress = (IsSecondLogicMatrix ? $"{roomState.MatrixCurrentRow}차-{roomState.MatrixCurrentCol}단계" : $"단계={roomState.CurrentStep}");
				FileLogger.Log($"[세컨로직] {tableId}: {slTr.Reason} ({slProgress}, 연속당첨={_secondLogic.ConsecutiveWins}, 연속미당첨={_secondLogic.ConsecutiveLosses}" + (IsStreakRidingTargetActive ? (", 줄타기타겟=" + (_slRidingTarget ?? "없음")) : "") + ")");
			}
			if (IsSecondLogic && _slPendingReleaseRoom == tableId)
			{
				lock (_lock)
				{
					if (_slPendingReleaseRoom == tableId)
					{
						if (_secondLogic.ActiveRoom == tableId)
						{
							ReleaseSecondLogicRoomCore(tableId, roomState);
						}
						else
						{
							_slPendingReleaseRoom = null;
						}
					}
				}
			}
			if (Context.SelectedBetAmountType == 3 && roomState.IsDoubleUpPhase && !(Context.TableIndexMap.GetValueOrDefault(tableId, 0) % 2 != 0))
			{
				roomState.IsDoubleUpPhase = false;
				roomState.DoubleUpAmount = 0m;
				roomState.DoubleUpTarget = null;
				FileLogger.Log($"[AutoBetting][혼합] {tableId}: 방식 전환으로 DoubleUp 해제, 단계={roomState.CurrentStep}");
			}
			if (Context.SelectedBetAmountType == 5 && !isTie && !Context.IsChainMode)
			{
				if (!Context.IsDAlembertPhase && !isWin && Context.MartinMaxStep > 0 && stepBefore >= Context.MartinMaxStep)
				{
					SwitchToDAlembertPhase(tableId);
				}
				else if (Context.IsDAlembertPhase && Context.AvailableBalance >= Context.MartinEntryBalance)
				{
					SwitchToMartinPhase(tableId);
				}
			}
			if (Context.IsChainMode && !isTie)
			{
				if (!isWin)
				{
					roomState.ConsecutiveLossCount++;
					roomState.ChainStepLossAmount += lastBetAmount;
				}
				else
				{
					roomState.ConsecutiveLossCount = 0;
				}
				if (EvaluateChainTransition(roomState, isWin, stepBefore))
				{
					if (!AdvanceChainStep(tableId))
					{
						shouldStopForChain = true;
					}
					nextStep = 1;
				}
			}
			nextStep = ((Context.IsChainMode & shouldStopForChain) ? 1 : ((nextStep > 0) ? nextStep : roomState.CurrentStep));
			roomState.IsWaitingForResult = false;
			roomState.LastProcessedHistoryCount = newHistoryCount;
			Context.PendingBetAmount -= lastBetAmount;
			if (Context.PendingBetAmount < 0m)
			{
				Context.PendingBetAmount = 0m;
			}
			if (ApplyDeferredResetIfNeeded(tableId, roomState))
			{
				nextStep = 1;
			}
			stepVersion = roomState.StepVersion;
			if (Context.IsMatrixStrategy)
			{
				nextMatrixRow = roomState.MatrixCurrentRow;
				nextMatrixCol = roomState.MatrixCurrentCol;
			}
			if (shouldFireLastStepReset | shouldLowerPeakSilently)
			{
				Context.PeakBalance = Context.AvailableBalance;
				FileLogger.Log($"[AutoBetting] 선현재금액 → 현재금액({Context.AvailableBalance:N0})으로 하향 (마지막 단계 미당첨{(shouldLowerPeakSilently ? ", 세컨로직 달랑베르 상한" : "")})");
			}
			if (shouldFireStepReset | shouldFireLastStepReset)
			{
				globalStepVersion = Context.RoomStates.Values.DefaultIfEmpty().Max((RoomBettingState r) => r?.StepVersion ?? 0);
				if (shouldFireStepReset)
				{
					ResetSecondLogicCycle(clearActiveRoom: false, tableId + " 미당첨 → 모든 방 초기화");
				}
				if (Context.IsPeakBalanceResetEnabled && !_isPaused)
				{
					_isPaused = true;
					Interlocked.Exchange(ref _pauseStartTimeTicks, DateTime.Now.Ticks);
					FileLogger.Log($"[AutoBetting] 모도 초기화 — {15}초 일시정지 시작");
				}
			}
		}
		if (Context.IsVirtualMode && !isIdleRound)
		{
			decimal virtualCredit = (isTie ? lastBetAmount : ((!isWin) ? 0m : (string.Equals(lastBetType, "Banker", StringComparison.OrdinalIgnoreCase) ? (lastBetAmount * 1.95m) : (lastBetAmount * 2m))));
			decimal virtualNet = virtualCredit - lastBetAmount;
			decimal afterVirtualBalance = Context.AddToCurrentBalance(virtualNet);
			FileLogger.Log($"[AutoBetting][가상] {tableId}: 가상 정산 - {(isWin ? "당첨" : (isTie ? "타이" : "미당첨"))} 환급={virtualCredit:N0}, 순손익={virtualNet:+#,##0;-#,##0;0}, 잔액={afterVirtualBalance:N0}");
			CheckBalanceAfterDomUpdate();
		}
		PendingBetAmountChanged?.Invoke(Context.PendingBetAmount);
		try
		{
			_cooldownService.OnBetResult(tableId, isWin, isTie);
		}
		catch (Exception ex)
		{
			FileLogger.Log("[RoomCooldown] 판정 처리 오류: " + ex.Message, "Warning", "RoomCooldown");
		}
		ResultJudged?.Invoke(this, new AutoBettingResultEventArgs
		{
			TableId = tableId,
			GameId = (lastBetGameId ?? ""),
			IsWin = isWin,
			BetType = (lastBetType ?? ""),
			GameResult = lastResult.Winner,
			BetAmount = lastBetAmount,
			IsIdleRound = isIdleRound,
			NextStep = nextStep,
			StepVersion = stepVersion,
			NextMatrixRow = nextMatrixRow,
			NextMatrixCol = nextMatrixCol,
			IsSecondLogicRoomSwitched = slRoomSwitched,
			IsPeakLoweredSilently = ((shouldFireLastStepReset | shouldLowerPeakSilently) && !shouldFireStepReset && (!shouldFireLastStepReset || !Context.IsPeakBalanceResetEnabled))
		});
		if (!isIdleRound)
		{
			SettleBettingHistoryInDbAsync(lastBetType ?? "", lastBetAmount, isWin, isTie, tableId, lastBetGameId ?? "");
		}
		if (shouldFireStepReset || (shouldFireLastStepReset && Context.IsPeakBalanceResetEnabled))
		{
			PeakBalanceReset?.Invoke(this, new PeakResetEventArgs
			{
				NewPeakBalance = Context.PeakBalance,
				PauseDurationSeconds = 15,
				GlobalStepVersion = globalStepVersion
			});
			ResumeAfterPauseAsync();
		}
		if (matrixRestMinutes > 0)
		{
			EnterMatrixRest(matrixRestMinutes);
		}
		if (shouldStopForChain)
		{
			Task.Run(async delegate
			{
				await StopAsync();
				RaiseStateChanged(isRunning: false, "전략 체인이 완료되어 자동 베팅을 정지합니다.");
			});
		}
	}

	private void HandleMartinResult(string tableId, RoomBettingState roomState, bool isTie, bool isWin, int stepBefore, ref bool shouldFireLastStepReset, ref bool shouldFireStepReset)
	{
		if (isTie)
		{
			FileLogger.Log($"[AutoBetting] {tableId}: Tie - 단계 유지 ({stepBefore})");
			return;
		}
		if (isWin)
		{
			roomState.CurrentStep = 1;
			roomState.StepVersion++;
			roomState.IsDoubleUpPhase = false;
			FileLogger.Log("[AutoBetting] " + tableId + ": 당첨 - 1단계로 초기화");
			return;
		}
		bool isMixed = Context.SelectedBetAmountType == 3;
		int maxStep = ((!isMixed) ? ((Context.BetAmounts.Count > 0) ? Context.BetAmounts.Keys.Max() : 50) : ((Context.MartinBetAmounts.Count > 0) ? Context.MartinBetAmounts.Keys.Max() : 50));
		int nextStep = roomState.CurrentStep + 1;
		if (nextStep > maxStep)
		{
			if (Context.SelectedBetAmountType == 5 && !Context.IsDAlembertPhase)
			{
				roomState.CurrentStep = 1;
				roomState.StepVersion++;
				FileLogger.Log($"[AutoBetting][마틴+달랑] {tableId}: 마틴 마지막 단계({stepBefore}) 미당첨 → 달랑베르 전환 대기");
				return;
			}
			if (isMixed)
			{
				int idx = Context.TableIndexMap.GetValueOrDefault(tableId, 0);
				roomState.CurrentStep = 1;
				roomState.StepVersion++;
				FileLogger.Log($"[AutoBetting][혼합] {tableId}: 방{idx + 1}(마틴) 마지막 단계({stepBefore}) 미당첨 → 해당 방만 1단계 리셋");
				return;
			}
			roomState.CurrentStep = 1;
			roomState.StepVersion++;
			shouldFireLastStepReset = true;
			FileLogger.Log($"[AutoBetting] {tableId}: 마지막 단계({stepBefore}) 미당첨 → 1단계 초기화");
			if (Context.IsStepResetEnabled)
			{
				ResetAllRoomSteps(tableId);
				shouldFireStepReset = true;
			}
		}
		else
		{
			roomState.CurrentStep = nextStep;
			roomState.StepVersion++;
			FileLogger.Log($"[AutoBetting] {tableId}: 미당첨 - {stepBefore}→{nextStep}단계");
			if (!isMixed && Context.IsStepResetEnabled && roomState.CurrentStep >= Context.StepResetTargetStep)
			{
				ResetAllRoomSteps(tableId);
				shouldFireStepReset = true;
				FileLogger.Log($"[AutoBetting] {tableId}: {Context.StepResetTargetStep}단계 도달 → 모든방 초기화");
			}
		}
	}

	private void HandleHongKongCruiseResult(string tableId, RoomBettingState roomState, bool isTie, bool isWin, int stepBefore, ref bool shouldFireLastStepReset, ref bool shouldFireStepReset)
	{
		if (isTie)
		{
			FileLogger.Log($"[AutoBetting] {tableId}: HK Tie - 단계 유지 ({stepBefore})");
			return;
		}
		bool isMixed = Context.SelectedBetAmountType == 3;
		int maxStep = ((!isMixed) ? ((Context.BetAmounts.Count > 0) ? Context.BetAmounts.Keys.Max() : 50) : ((Context.HongKongBetAmounts.Count > 0) ? Context.HongKongBetAmounts.Keys.Max() : 50));
		if (roomState.IsDoubleUpPhase)
		{
			roomState.IsDoubleUpPhase = false;
			roomState.DoubleUpAmount = 0m;
			roomState.DoubleUpTarget = null;
			if (isWin)
			{
				int targetStep = (roomState.CurrentStep = GetHongKongWinTargetStep(stepBefore, maxStep));
				roomState.StepVersion++;
				FileLogger.Log((targetStep == 1) ? ("[AutoBetting] " + tableId + ": HK 2연승 → 1단계 초기화") : $"[AutoBetting] {tableId}: ★ HK 2연승 ({stepBefore}단계) → 이동 규칙 적용 → {targetStep}단계");
				return;
			}
			int nextStep = roomState.CurrentStep + 1;
			if (nextStep > maxStep)
			{
				roomState.CurrentStep = 1;
				roomState.StepVersion++;
				if (!isMixed)
				{
					shouldFireLastStepReset = true;
				}
				if (isMixed)
				{
					int idx = Context.TableIndexMap.GetValueOrDefault(tableId, 0);
					FileLogger.Log($"[AutoBetting][혼합] {tableId}: 방{idx + 1}(홍콩) 2판 마지막 단계({stepBefore}) 미당첨 → 해당 방만 1단계 리셋");
				}
			}
			else
			{
				roomState.CurrentStep = nextStep;
				roomState.StepVersion++;
			}
			FileLogger.Log($"[AutoBetting] {tableId}: HK 2판 미당첨 → {roomState.CurrentStep}단계");
			return;
		}
		if (isWin)
		{
			roomState.IsDoubleUpPhase = true;
			roomState.DoubleUpAmount = roomState.LastBetAmount * 2m;
			roomState.DoubleUpTarget = roomState.LastBetType;
			FileLogger.Log($"[AutoBetting] {tableId}: HK 1판 승리 → 2판 도전 (금액={roomState.DoubleUpAmount:N0}, 타겟={roomState.DoubleUpTarget})");
			return;
		}
		int nextStep2 = roomState.CurrentStep + 1;
		if (nextStep2 > maxStep)
		{
			roomState.CurrentStep = 1;
			roomState.StepVersion++;
			if (!isMixed)
			{
				shouldFireLastStepReset = true;
			}
			if (isMixed)
			{
				int idx2 = Context.TableIndexMap.GetValueOrDefault(tableId, 0);
				FileLogger.Log($"[AutoBetting][혼합] {tableId}: 방{idx2 + 1}(홍콩) 마지막 단계({stepBefore}) 미당첨 → 해당 방만 1단계 리셋");
			}
		}
		else
		{
			roomState.CurrentStep = nextStep2;
			roomState.StepVersion++;
		}
		FileLogger.Log($"[AutoBetting] {tableId}: HK 미당첨 → {roomState.CurrentStep}단계");
	}

	private void HandleFibonacciResult(string tableId, RoomBettingState roomState, bool isTie, bool isWin, int stepBefore, ref bool shouldFireLastStepReset, ref bool shouldFireStepReset)
	{
		if (isTie)
		{
			FileLogger.Log($"[AutoBetting] {tableId}: Fib Tie - 단계 유지 ({stepBefore})");
			return;
		}
		if (isWin)
		{
			int newStep = (roomState.CurrentStep = Math.Max(1, roomState.CurrentStep - 2));
			roomState.StepVersion++;
			FileLogger.Log($"[AutoBetting] {tableId}: Fib 승리 → {newStep}단계 (2단계 후퇴)");
			return;
		}
		int maxStep = ((Context.BetAmounts.Count > 0) ? Context.BetAmounts.Keys.Max() : 50);
		int nextStep = roomState.CurrentStep + 1;
		if (nextStep > maxStep)
		{
			roomState.CurrentStep = 1;
			roomState.StepVersion++;
			shouldFireLastStepReset = true;
			FileLogger.Log("[AutoBetting] " + tableId + ": Fib 마지막 단계 미당첨 → 1단계 초기화");
			return;
		}
		roomState.CurrentStep = nextStep;
		roomState.StepVersion++;
		FileLogger.Log($"[AutoBetting] {tableId}: Fib 미당첨 → {nextStep}단계");
	}

	private void HandleDalmenzingResult(string tableId, RoomBettingState roomState, bool isTie, bool isWin, int stepBefore, ref bool shouldFireLastStepReset, ref bool shouldFireStepReset)
	{
		if (isTie)
		{
			FileLogger.Log($"[AutoBetting][달랑멘징] {tableId}: Tie - 단계 유지 ({stepBefore}), 페이즈={(roomState.IsDalmenzingRecoveryPhase ? "복구" : "달랑")}");
			return;
		}
		decimal lastBetAmt = roomState.LastBetAmount;
		if (!roomState.IsDalmenzingRecoveryPhase)
		{
			if (isWin)
			{
				roomState.DalmenzingDAlembertNetLoss -= lastBetAmt;
				roomState.CurrentStep = Math.Max(1, roomState.CurrentStep - 1);
				roomState.StepVersion++;
				FileLogger.Log($"[AutoBetting][달랑멘징][달랑] {tableId}: 당첨 {stepBefore}→{roomState.CurrentStep}단계, 누적순손실={roomState.DalmenzingDAlembertNetLoss:N0}");
				return;
			}
			roomState.DalmenzingDAlembertNetLoss += lastBetAmt;
			int maxStep = ((Context.DalmenzingMaxStep > 0) ? Context.DalmenzingMaxStep : 50);
			if (stepBefore >= maxStep)
			{
				roomState.IsDalmenzingRecoveryPhase = true;
				roomState.DalmenzingRecoveryStep = 1;
				roomState.DalmenzingRecoveryDoubleUpPhase = false;
				roomState.DalmenzingRecoveryDoubleUpAmount = 0m;
				roomState.DalmenzingRecoveryNetProfit = 0m;
				roomState.CurrentStep = 1;
				roomState.StepVersion++;
				string methodName = ((Context.DalmenzingRecoveryMethod == 1) ? "홍콩크루즈" : "마틴");
				FileLogger.Log($"[AutoBetting][달랑멘징] {tableId}: ★★ N({maxStep})단계 패배 → 복구({methodName}) 진입! 복구목표={roomState.DalmenzingDAlembertNetLoss:N0}");
			}
			else
			{
				roomState.CurrentStep++;
				roomState.StepVersion++;
				FileLogger.Log($"[AutoBetting][달랑멘징][달랑] {tableId}: 패배 {stepBefore}→{roomState.CurrentStep}단계, 누적순손실={roomState.DalmenzingDAlembertNetLoss:N0}");
			}
			return;
		}
		int recoveryStepBefore = roomState.DalmenzingRecoveryStep;
		int dalmenzingRecoveryMethod = Context.DalmenzingRecoveryMethod;
		if (isWin)
		{
			roomState.DalmenzingRecoveryNetProfit += lastBetAmt;
		}
		else
		{
			roomState.DalmenzingRecoveryNetProfit -= lastBetAmt;
		}
		if (dalmenzingRecoveryMethod == 1)
		{
			if (isWin)
			{
				if (roomState.DalmenzingRecoveryDoubleUpPhase)
				{
					int maxHkWin = ((Context.DalmenzingHongKongAmounts.Count > 0) ? Context.DalmenzingHongKongAmounts.Keys.Max() : 0);
					int recoveryTarget = (roomState.DalmenzingRecoveryStep = ((maxHkWin <= 0) ? 1 : GetHongKongWinTargetStep(recoveryStepBefore, maxHkWin)));
					roomState.DalmenzingRecoveryDoubleUpPhase = false;
					roomState.DalmenzingRecoveryDoubleUpAmount = 0m;
					FileLogger.Log($"[AutoBetting][달랑멘징][복구-홍콩] {tableId}: ★★ 2연승 클리어! ★★ {recoveryStepBefore}→{recoveryTarget}단계");
				}
				else
				{
					roomState.DalmenzingRecoveryDoubleUpPhase = true;
					roomState.DalmenzingRecoveryDoubleUpAmount = lastBetAmt * 2m;
					FileLogger.Log($"[AutoBetting][달랑멘징][복구-홍콩] {tableId}: ★ 1승 DoubleUp - 2판={roomState.DalmenzingRecoveryDoubleUpAmount:N0}");
				}
			}
			else
			{
				if (roomState.DalmenzingRecoveryDoubleUpPhase)
				{
					roomState.DalmenzingRecoveryDoubleUpPhase = false;
					roomState.DalmenzingRecoveryDoubleUpAmount = 0m;
					FileLogger.Log("[AutoBetting][달랑멘징][복구-홍콩] " + tableId + ": 1승1패 - DoubleUp 해제, 다음 단계");
				}
				int maxHk = ((Context.DalmenzingHongKongAmounts.Count > 0) ? Context.DalmenzingHongKongAmounts.Keys.Max() : 0);
				if (maxHk > 0 && roomState.DalmenzingRecoveryStep >= maxHk)
				{
					FileLogger.Log($"[AutoBetting][달랑멘징][복구-홍콩] {tableId}: ★ 홍콩 최대단계({maxHk}) 실패 → 강제 달랑 1단계 복귀");
					ResetDalmenzingToDalembert(roomState);
					roomState.StepVersion++;
					shouldFireLastStepReset = true;
					return;
				}
				roomState.DalmenzingRecoveryStep++;
				FileLogger.Log($"[AutoBetting][달랑멘징][복구-홍콩] {tableId}: 패배 {recoveryStepBefore}→{roomState.DalmenzingRecoveryStep}단계");
			}
		}
		else if (isWin)
		{
			roomState.DalmenzingRecoveryStep = 1;
			FileLogger.Log($"[AutoBetting][달랑멘징][복구-마틴] {tableId}: 당첨 {recoveryStepBefore}→1단계");
		}
		else
		{
			int maxMartin = ((Context.DalmenzingMartinAmounts.Count > 0) ? Context.DalmenzingMartinAmounts.Keys.Max() : 0);
			if (maxMartin > 0 && roomState.DalmenzingRecoveryStep >= maxMartin)
			{
				FileLogger.Log($"[AutoBetting][달랑멘징][복구-마틴] {tableId}: ★ 마틴 최대단계({maxMartin}) 실패 → 강제 달랑 1단계 복귀");
				ResetDalmenzingToDalembert(roomState);
				roomState.StepVersion++;
				shouldFireLastStepReset = true;
				return;
			}
			roomState.DalmenzingRecoveryStep++;
			FileLogger.Log($"[AutoBetting][달랑멘징][복구-마틴] {tableId}: 패배 {recoveryStepBefore}→{roomState.DalmenzingRecoveryStep}단계");
		}
		if (isWin && roomState.DalmenzingRecoveryNetProfit >= roomState.DalmenzingDAlembertNetLoss && roomState.DalmenzingDAlembertNetLoss > 0m)
		{
			FileLogger.Log($"[AutoBetting][달랑멘징] {tableId}: ★★ 복구 완료! ★★ 수익={roomState.DalmenzingRecoveryNetProfit:N0} ≥ 목표={roomState.DalmenzingDAlembertNetLoss:N0} → 달랑 1단계 회귀");
			ResetDalmenzingToDalembert(roomState);
		}
		roomState.StepVersion++;
	}

	private void HandleMatrixResult(string tableId, RoomBettingState roomState, bool isTie, bool isWin, ref bool shouldFireLastStepReset, ref bool shouldFireStepReset, ref int matrixRestMinutes)
	{
		string logTag = GetBetAmountTypeName(Context.SelectedBetAmountType);
		if (isTie)
		{
			FileLogger.Log($"[AutoBetting][{logTag}] {tableId}: Tie - 좌표 유지 ({roomState.MatrixCurrentRow}차-{roomState.MatrixCurrentCol}단계, 누적당첨={roomState.MatrixRowWinCount}, 사이클순손익={roomState.MatrixCycleNet:+#,##0;-#,##0;0})");
			return;
		}
		int rowBefore = roomState.MatrixCurrentRow;
		int colBefore = roomState.MatrixCurrentCol;
		decimal lastBetAmt = roomState.LastBetAmount;
		decimal cycleDelta = (isWin ? GetNetWinProfit(roomState.LastBetType, lastBetAmt) : (-lastBetAmt));
		roomState.MatrixCycleNet += cycleDelta;
		if (isWin)
		{
			roomState.MatrixRowWinCount++;
			(int, int)? rule = GetMatrixRowRule(rowBefore);
			if (rule.HasValue && roomState.MatrixRowWinCount >= rule.Value.Item1)
			{
				int targetRow = rule.Value.Item2;
				FileLogger.Log($"[AutoBetting][{logTag}] {tableId}: ★ {rowBefore}차-{colBefore}단계 당첨(누적 {roomState.MatrixRowWinCount}/{rule.Value.Item1}) → 규칙 이동 → {targetRow}차-1단계 (당첨금={lastBetAmt:N0})");
				roomState.MatrixCurrentRow = targetRow;
				roomState.MatrixCurrentCol = 1;
				roomState.MatrixRowWinCount = 0;
			}
			else if (!Context.IsStairMatrixStrategy || !TryClimbStairRecovery(tableId, roomState, logTag, cycleDelta))
			{
				if (!rule.HasValue && !Context.IsStairMatrixStrategy && rowBefore >= 2 && roomState.MatrixCycleNet > 0m)
				{
					FileLogger.Log($"[AutoBetting][{logTag}] {tableId}: ★★ 손실 회수 완료! ★★ {rowBefore}차-{colBefore}단계 당첨(순이익={cycleDelta:N0}) → 사이클순손익={roomState.MatrixCycleNet:+#,##0;-#,##0;0} → 1차-1단계 복귀 (이동 규칙 없는 차)");
					ResetMatrixToFirstRow(roomState);
				}
				else
				{
					string need = (rule.HasValue ? rule.Value.Item1.ToString() : "규칙없음");
					bool stepBack = Context.ShouldStepBackOnWin(rowBefore);
					roomState.MatrixCurrentCol = ((!stepBack) ? 1 : Math.Max(1, colBefore - 1));
					int selectedBetAmountType = Context.SelectedBetAmountType;
					bool flag = (uint)(selectedBetAmountType - 13) <= 1u;
					string modeText = ((!flag) ? string.Empty : (stepBack ? ", 방식=달랑" : ", 방식=마틴"));
					FileLogger.Log($"[AutoBetting][{logTag}] {tableId}: 당첨 {rowBefore}차-{colBefore}단계 → {rowBefore}차-{roomState.MatrixCurrentCol}단계 (누적당첨={roomState.MatrixRowWinCount}/{need}{modeText}, 당첨금={lastBetAmt:N0}, 사이클순손익={roomState.MatrixCycleNet:+#,##0;-#,##0;0})");
				}
			}
			if (roomState.MatrixCurrentRow == 1 && roomState.MatrixCurrentCol == 1 && roomState.MatrixCycleNet > 0m)
			{
				FileLogger.Log($"[AutoBetting][{logTag}] {tableId}: 1차-1단계 흑자 복귀 → 사이클 종료, 장부 닫음 (순손익={roomState.MatrixCycleNet:+#,##0;-#,##0;0} → 0)");
				roomState.MatrixCycleNet = 0m;
			}
		}
		else
		{
			int maxCol = GetEffectiveMaxCol(rowBefore);
			if (maxCol == 0)
			{
				maxCol = colBefore;
			}
			if (colBefore >= maxCol)
			{
				bool num = rowBefore == 1 && Context.MatrixFirstRowCapAmount > 0m;
				decimal capBalance = Context.AvailableBalance;
				decimal capGuardLine = Context.MatrixFirstRowCapBaseBalance + Context.MatrixFirstRowCapAmount;
				bool holdOnFirstRow = num && Context.MatrixFirstRowCapBaseBalance > 0m && capBalance > capGuardLine;
				if (rowBefore >= AutoBettingContext.MatrixStepsPerRow.Length)
				{
					FileLogger.Log($"[AutoBetting][{logTag}] {tableId}: ★ {rowBefore}차-{colBefore}단계 미당첨 (최종단) → 1차-1단계 강제 복귀");
					ResetMatrixToFirstRow(roomState);
					matrixRestMinutes = Math.Max(matrixRestMinutes, Context.GetMatrixRestMinutes(rowBefore));
					shouldFireLastStepReset = true;
					if (Context.IsStepResetEnabled)
					{
						ResetAllRoomSteps(tableId);
						shouldFireStepReset = true;
					}
				}
				else if (holdOnFirstRow)
				{
					FileLogger.Log($"[AutoBetting][{logTag}] {tableId}: ★ 1차 상한금액 유지 — 1차-{colBefore}단계(끝) 미당첨, 잔액 {capBalance:N0}원 > 기준선 {capGuardLine:N0}원 (기준 {Context.MatrixFirstRowCapBaseBalance:N0} + 상한 {Context.MatrixFirstRowCapAmount:N0}) → 2차 미진입, 1차-1단계 복귀 (누적 금액 초기화)");
					ResetMatrixToFirstRow(roomState);
				}
				else if (!GetMatrixRowRule(rowBefore).HasValue && rowBefore >= 2 && roomState.MatrixCycleNet >= 0m)
				{
					FileLogger.Log($"[AutoBetting][{logTag}] {tableId}: {rowBefore}차-{colBefore}단계(끝) 미당첨 → 사이클순손익={roomState.MatrixCycleNet:+#,##0;-#,##0;0} (회수 불필요) → 1차-1단계 복귀");
					ResetMatrixToFirstRow(roomState);
				}
				else
				{
					roomState.MatrixCurrentRow = rowBefore + 1;
					roomState.MatrixCurrentCol = 1;
					roomState.MatrixRowWinCount = 0;
					MarkRowEntryNet(roomState, roomState.MatrixCurrentRow);
					matrixRestMinutes = Math.Max(matrixRestMinutes, Context.GetMatrixRestMinutes(rowBefore));
					FileLogger.Log($"[AutoBetting][{logTag}] {tableId}: {rowBefore}차-{colBefore}단계(끝) 미당첨 → {roomState.MatrixCurrentRow}차-1단계 진입 (누적당첨 리셋, 사이클순손익={roomState.MatrixCycleNet:+#,##0;-#,##0;0})");
				}
				if (num && capBalance > 0m && capBalance != Context.MatrixFirstRowCapBaseBalance)
				{
					FileLogger.Log($"[AutoBetting][{logTag}] {tableId}: 1차 상한금액 기준 잔액 갱신 {Context.MatrixFirstRowCapBaseBalance:N0}원 → {capBalance:N0}원 (다음 기준선 {capBalance + Context.MatrixFirstRowCapAmount:N0}원)");
					Context.MatrixFirstRowCapBaseBalance = capBalance;
				}
			}
			else
			{
				roomState.MatrixCurrentCol = colBefore + 1;
				FileLogger.Log($"[AutoBetting][{logTag}] {tableId}: 미당첨 {rowBefore}차-{colBefore}단계 → {rowBefore}차-{roomState.MatrixCurrentCol}단계 (누적당첨={roomState.MatrixRowWinCount}, 사이클순손익={roomState.MatrixCycleNet:+#,##0;-#,##0;0})");
			}
		}
		roomState.StepVersion++;
	}

	private static decimal GetNetWinProfit(string? betType, decimal betAmount)
	{
		if (!string.Equals(betType, "Banker", StringComparison.OrdinalIgnoreCase))
		{
			return betAmount;
		}
		return betAmount * 0.95m;
	}

	private (int WinCount, int TargetRow)? GetMatrixRowRule(int row)
	{
		if (Context.MatrixRowRules != null && Context.MatrixRowRules.TryGetValue(row, out (int, int) rule))
		{
			return rule;
		}
		return null;
	}

	private static void ResetMatrixToFirstRow(RoomBettingState roomState)
	{
		roomState.MatrixCurrentRow = 1;
		roomState.MatrixCurrentCol = 1;
		roomState.MatrixRowWinCount = 0;
		roomState.MatrixCycleNet = 0m;
		Array.Clear(roomState.MatrixRowEntryNet);
	}

	private static decimal GetRowEntryNet(RoomBettingState roomState, int row)
	{
		if (row <= 1)
		{
			return 0m;
		}
		decimal[] entry = roomState.MatrixRowEntryNet;
		if (row > entry.Length)
		{
			return 0m;
		}
		return entry[row - 1];
	}

	private static void MarkRowEntryNet(RoomBettingState roomState, int row)
	{
		decimal[] entry = roomState.MatrixRowEntryNet;
		if (row >= 1 && row <= entry.Length)
		{
			entry[row - 1] = roomState.MatrixCycleNet;
		}
	}

	private bool TryClimbStairRecovery(string tableId, RoomBettingState roomState, string logTag, decimal cycleDelta)
	{
		int rowBefore = roomState.MatrixCurrentRow;
		int colBefore = roomState.MatrixCurrentCol;
		int targetRow = rowBefore;
		while (targetRow >= 2 && roomState.MatrixCycleNet >= GetRowEntryNet(roomState, targetRow - 1))
		{
			targetRow--;
		}
		if (targetRow == rowBefore)
		{
			return false;
		}
		if (targetRow == 1)
		{
			FileLogger.Log($"[AutoBetting][{logTag}] {tableId}: ★★ 전체 손실 회수 완료! ★★ {rowBefore}차-{colBefore}단계 당첨(순이익={cycleDelta:N0}) → 사이클순손익={roomState.MatrixCycleNet:+#,##0;-#,##0;0} → 1차-1단계 복귀, 사이클 종료");
			ResetMatrixToFirstRow(roomState);
		}
		else
		{
			decimal baseline = GetRowEntryNet(roomState, targetRow);
			FileLogger.Log($"[AutoBetting][{logTag}] {tableId}: ★ {targetRow}차 손실 회수! {rowBefore}차-{colBefore}단계 당첨(순이익={cycleDelta:N0}) → 사이클순손익={roomState.MatrixCycleNet:+#,##0;-#,##0;0} ≥ {targetRow}차 기준선({baseline:+#,##0;-#,##0;0}) → {targetRow}차-1단계로 복귀 (장부 유지)");
			roomState.MatrixCurrentRow = targetRow;
			roomState.MatrixCurrentCol = 1;
			roomState.MatrixRowWinCount = 0;
		}
		return true;
	}

	private static MatrixCarry CaptureMatrixCarry(RoomBettingState roomState)
	{
		return new MatrixCarry(roomState.MatrixCurrentRow, roomState.MatrixCurrentCol, roomState.MatrixRowWinCount, roomState.MatrixCycleNet, (decimal[])roomState.MatrixRowEntryNet.Clone());
	}

	private static MatrixCarry CreateEmptyMatrixCarry()
	{
		return new MatrixCarry(1, 1, 0, 0m, new decimal[AutoBettingContext.MatrixStepsPerRow.Length]);
	}

	private int GetEffectiveMaxCol(int row)
	{
		int rowCount = AutoBettingContext.MatrixStepsPerRow.Length;
		if (row < 1 || row > rowCount)
		{
			return 0;
		}
		if (Context.MatrixDefinedMaxCols == null || Context.MatrixDefinedMaxCols.Length < rowCount)
		{
			return 0;
		}
		if (Context.MatrixCells == null || Context.MatrixCells.Length < rowCount)
		{
			return 0;
		}
		int definedMax = Context.MatrixDefinedMaxCols[row - 1];
		decimal[] arr = Context.MatrixCells[row - 1];
		if (arr == null || arr.Length == 0)
		{
			return 0;
		}
		return Math.Min(definedMax, arr.Length);
	}

	private static void ResetDalmenzingToDalembert(RoomBettingState roomState)
	{
		roomState.IsDalmenzingRecoveryPhase = false;
		roomState.CurrentStep = 1;
		roomState.DalmenzingDAlembertNetLoss = 0m;
		roomState.DalmenzingRecoveryNetProfit = 0m;
		roomState.DalmenzingRecoveryStep = 1;
		roomState.DalmenzingRecoveryDoubleUpPhase = false;
		roomState.DalmenzingRecoveryDoubleUpAmount = 0m;
		roomState.IsDoubleUpPhase = false;
		roomState.DoubleUpAmount = 0m;
		roomState.DoubleUpTarget = null;
	}

	private void HandleDAlembertResult(string tableId, RoomBettingState roomState, bool isTie, bool isWin, int stepBefore, ref bool shouldFireLastStepReset, ref bool shouldFireStepReset, ref bool shouldLowerPeakSilently)
	{
		if (isTie)
		{
			FileLogger.Log($"[AutoBetting][달랑베르] {tableId}: Tie - 단계 유지 ({stepBefore})");
			return;
		}
		if (isWin)
		{
			roomState.CurrentStep = Math.Max(1, roomState.CurrentStep - 1);
			roomState.StepVersion++;
			FileLogger.Log($"[AutoBetting][달랑베르] {tableId}: 당첨 → {roomState.CurrentStep}단계 (1단계 후퇴)");
			return;
		}
		int tableMaxStep = ((Context.BetAmounts.Count > 0) ? Context.BetAmounts.Keys.Max() : 50);
		int slMaxStep = ((SecondLogicDAlembertMaxStep > 0) ? Math.Min(SecondLogicDAlembertMaxStep, tableMaxStep) : 0);
		int maxStep = ((slMaxStep > 0) ? slMaxStep : tableMaxStep);
		int nextStep = roomState.CurrentStep + 1;
		if (nextStep > maxStep)
		{
			roomState.CurrentStep = 1;
			roomState.StepVersion++;
			if (slMaxStep > 0)
			{
				shouldLowerPeakSilently = true;
				FileLogger.Log($"[AutoBetting][달랑베르] {tableId}: 세컨로직 최대단계({slMaxStep}) 미당첨 → 1단계 재시작 (선현재금액만 하향, 일시정지·전체 초기화 없음)");
				return;
			}
			shouldFireLastStepReset = true;
			FileLogger.Log($"[AutoBetting][달랑베르] {tableId}: 마지막 단계({stepBefore}) 미당첨 → 1단계 초기화");
			if (Context.IsStepResetEnabled)
			{
				ResetAllRoomSteps(tableId);
				shouldFireStepReset = true;
			}
		}
		else
		{
			roomState.CurrentStep = nextStep;
			roomState.StepVersion++;
			FileLogger.Log($"[AutoBetting][달랑베르] {tableId}: 미당첨 → {nextStep}단계");
			if (Context.IsStepResetEnabled && roomState.CurrentStep >= Context.StepResetTargetStep)
			{
				ResetAllRoomSteps(tableId);
				shouldFireStepReset = true;
				FileLogger.Log($"[AutoBetting][달랑베르] {tableId}: {Context.StepResetTargetStep}단계 도달 → 모든방 초기화");
			}
		}
	}

	private void Handle1326Result(string tableId, RoomBettingState roomState, bool isTie, bool isWin, int stepBefore, ref int nextStep, ref bool shouldFireLastStepReset, ref bool shouldFireStepReset)
	{
		if (isTie)
		{
			nextStep = roomState.CurrentStep;
			FileLogger.Log($"[AutoBetting][1-3-2-6] {tableId}: Tie - 단계 유지 ({nextStep})");
		}
		else if (isWin)
		{
			if (roomState.CurrentStep >= 4)
			{
				if (Context.IsChainMode && Context.ChainSteps[Context.CurrentChainStepIndex].StrategyCompleteTransition)
				{
					FileLogger.Log("[AutoBetting][1-3-2-6] " + tableId + ": ★ 4단계 완주 → 체인 전환 대기 ★");
				}
				else
				{
					roomState.CurrentStep = 1;
					FileLogger.Log("[AutoBetting][1-3-2-6] " + tableId + ": ★ 4단계 완주! ★ 1단계로 리셋");
				}
			}
			else
			{
				roomState.CurrentStep++;
				FileLogger.Log($"[AutoBetting][1-3-2-6] {tableId}: ★ 당첨! ★ {stepBefore}단계 → {roomState.CurrentStep}단계");
			}
			roomState.StepVersion++;
			nextStep = roomState.CurrentStep;
		}
		else
		{
			roomState.CurrentStep = 1;
			roomState.StepVersion++;
			nextStep = 1;
			FileLogger.Log($"[AutoBetting][1-3-2-6] {tableId}: 미당첨 - {stepBefore}단계 → 1단계 리셋");
		}
	}

	private void HandleOscarGrindResult(string tableId, RoomBettingState roomState, bool isTie, bool isWin, int stepBefore, ref int nextStep, ref bool shouldFireLastStepReset, ref bool shouldFireStepReset)
	{
		if (isTie)
		{
			nextStep = roomState.CurrentStep;
			FileLogger.Log($"[AutoBetting][오스카] {tableId}: Tie - 단계 유지 ({nextStep})");
			return;
		}
		if (isWin)
		{
			roomState.OscarSessionProfit += (decimal)roomState.OscarCurrentBetUnits;
			if (roomState.OscarSessionProfit >= (decimal)Context.OscarTargetUnits)
			{
				if (Context.IsChainMode && Context.ChainSteps[Context.CurrentChainStepIndex].StrategyCompleteTransition)
				{
					FileLogger.Log($"[AutoBetting][오스카] {tableId}: ★ 목표 달성 → 체인 전환 대기 ★ 이익={roomState.OscarSessionProfit}단위");
				}
				else
				{
					FileLogger.Log($"[AutoBetting][오스카] {tableId}: ★ 목표 달성! ★ 이익={roomState.OscarSessionProfit}단위, 리셋");
					roomState.OscarSessionProfit = 0m;
					roomState.OscarCurrentBetUnits = 1;
					roomState.CurrentStep = 1;
				}
			}
			else
			{
				roomState.OscarCurrentBetUnits++;
				int maxBet = Context.OscarTargetUnits - (int)roomState.OscarSessionProfit;
				if (maxBet > 0 && roomState.OscarCurrentBetUnits > maxBet)
				{
					roomState.OscarCurrentBetUnits = maxBet;
				}
				roomState.CurrentStep = roomState.OscarCurrentBetUnits;
				FileLogger.Log($"[AutoBetting][오스카] {tableId}: 당첨! 세션이익={roomState.OscarSessionProfit}단위, 다음베팅={roomState.OscarCurrentBetUnits}단위");
			}
		}
		else
		{
			roomState.OscarSessionProfit -= (decimal)roomState.OscarCurrentBetUnits;
			FileLogger.Log($"[AutoBetting][오스카] {tableId}: 미당첨 - 세션이익={roomState.OscarSessionProfit}단위, 베팅 유지={roomState.OscarCurrentBetUnits}단위");
		}
		roomState.StepVersion++;
		nextStep = roomState.CurrentStep;
	}

	private void HandleFlatBettingResult(string tableId, RoomBettingState roomState, bool isTie, bool isWin, int stepBefore, ref int nextStep, ref bool shouldFireLastStepReset, ref bool shouldFireStepReset)
	{
		roomState.CurrentStep = 1;
		roomState.StepVersion++;
		nextStep = 1;
	}

	// 업치기(파레이) 결과 처리: 당첨 → 다음 단계(×2) 자동 진행, 미당첨 → 1단계 초기화,
	// N단계 당첨 완료 → 일정베팅 전환, 사이클 순손익 > 합산수익 임계 → 1단계+수익카운터 리셋.
	private void HandleUpchigiResult(string tableId, RoomBettingState roomState, bool isTie, bool isWin, int stepBefore, ref bool shouldFireLastStepReset, ref bool shouldFireStepReset)
	{
		if (isTie)
		{
			FileLogger.Log($"[AutoBetting][업치기] {tableId}: Tie - 단계/사이클 유지 (단계={roomState.CurrentStep}, 일정={(roomState.IsUpchigiFlatPhase ? "예" : "아니오")})");
			return;
		}
		decimal net = isWin ? GetNetWinProfit(roomState.LastBetType, roomState.LastBetAmount) : (0m - roomState.LastBetAmount);
		roomState.UpchigiCycleNet += net;
		Context.UpchigiGlobalNet += net;   // 전체(모든 방) 합산 순손익 누적 — 합산수익 리셋은 이 값으로 판정
		int N = Math.Max(1, Context.UpchigiStepCount);
		bool hasFlat = Context.UpchigiFlatAmount > 0m;   // 일정금액 미설정이면 일정베팅을 하지 않는다
		bool wasFlatPhase = roomState.IsUpchigiFlatPhase;   // 이번 라운드가 일정베팅 구간이었는지(판정 전 상태)
		if (!wasFlatPhase)
		{
			if (isWin)
			{
				if (roomState.CurrentStep >= N)
				{
					// N단계 완주: 일정금액이 있으면 일정베팅으로, 없으면 바로 1단계부터 업치기 재시작.
					roomState.CurrentStep = 1;
					if (hasFlat)
					{
						roomState.IsUpchigiFlatPhase = true;
						FileLogger.Log($"[AutoBetting][업치기] {tableId}: {N}단계 완주 → 일정베팅 전환(서식 적중 시 업치기 재개) (전체순손익={Context.UpchigiGlobalNet:+#,##0;-#,##0;0})");
					}
					else
					{
						FileLogger.Log($"[AutoBetting][업치기] {tableId}: {N}단계 완주 → 1단계 업치기 재시작(일정금액 미설정) (전체순손익={Context.UpchigiGlobalNet:+#,##0;-#,##0;0})");
					}
				}
				else
				{
					roomState.CurrentStep++;
					FileLogger.Log($"[AutoBetting][업치기] {tableId}: 당첨 → 업치기 {roomState.CurrentStep}단계 (전체순손익={Context.UpchigiGlobalNet:+#,##0;-#,##0;0})");
				}
			}
			else
			{
				roomState.CurrentStep = 1;
				FileLogger.Log($"[AutoBetting][업치기] {tableId}: 미당첨 → 1단계 초기화 (전체순손익={Context.UpchigiGlobalNet:+#,##0;-#,##0;0})");
			}
		}
		else
		{
			// 일정베팅 구간: 일정금액으로 베팅하다가 미당첨(실패)하면 1단계로 초기화(업치기 재시작). 당첨이면 일정베팅 유지.
			if (!isWin)
			{
				roomState.IsUpchigiFlatPhase = false;
				roomState.CurrentStep = 1;
				FileLogger.Log($"[AutoBetting][업치기] {tableId}: 일정베팅 미당첨 → 1단계 초기화(업치기 재시작) (전체순손익={Context.UpchigiGlobalNet:+#,##0;-#,##0;0})");
			}
			else
			{
				FileLogger.Log($"[AutoBetting][업치기] {tableId}: 일정베팅 당첨 → 일정베팅 유지 (전체순손익={Context.UpchigiGlobalNet:+#,##0;-#,##0;0})");
			}
		}
		// 누적실패(미당첨 횟수) 상승: 미당첨마다 +1(업치기 단계·일정베팅 구간 모두 포함), 임계 도달 시 그 방의 시작금액 배수를 설정 배수만큼 누적 상승(×2→×4→…), 배수 상한까지만.
		if (!isWin)
		{
			roomState.CycleIndex++;   // 회차는 미당첨(실패) 때만 +1 (타이는 위에서 return, 당첨/완주는 유지)
			roomState.UpchigiFailAccum++;
			int failThr = Context.UpchigiFailEscalateThreshold;
			decimal escMult = Context.UpchigiFailEscalateMultiplier;
			if (failThr > 0 && escMult > 1m && roomState.UpchigiFailAccum >= failThr)
			{
				decimal nextMult = Math.Max(1m, roomState.UpchigiStartMultiplier) * escMult;
				decimal maxMult = Context.UpchigiFailEscalateMaxMultiplier;
				bool capped = maxMult > 1m && nextMult >= maxMult;
				if (capped)
				{
					nextMult = maxMult;
				}
				roomState.UpchigiStartMultiplier = nextMult;
				roomState.UpchigiFailAccum = 0;
				roomState.CycleIndex = 1;   // 배수 상승 순간 회차를 1부터 다시(표기: "1-1 ×N")
				FileLogger.Log($"[AutoBetting][업치기] {tableId}: ★ 누적실패 {failThr}회 도달 → 시작금액 배수 ×{roomState.UpchigiStartMultiplier:0.##} {(capped ? "(상한 도달)" : "상승")} (회차 1로 리셋)");
			}
		}
		// 합산수익 리셋: 전체(모든 방) 합산 순손익이 합산수익을 넘으면 — 즉 전체 손실을 다 메우고
		//   합산수익만큼 더 벌면 — 단계와 무관하게 모든 방을 1단계로 초기화하고 전체 순손익을 0으로 되돌린다.
		decimal threshold = Context.UpchigiProfitResetThreshold;
		if (threshold > 0m && Context.UpchigiGlobalNet > threshold)
		{
			FileLogger.Log($"[AutoBetting][업치기] {tableId}: ★ 전체 합산수익 도달 ({Context.UpchigiGlobalNet:N0} > {threshold:N0}) → 모든 방 1단계 초기화 + 전체 순손익 리셋");
			foreach (RoomBettingState rs in Context.RoomStates.Values)
			{
				rs.CurrentStep = 1;
				rs.IsUpchigiFlatPhase = false;
				rs.UpchigiCycleNet = 0m;
				rs.UpchigiStartMultiplier = 1m;
				rs.UpchigiFailAccum = 0;
				// 합산수익 리셋은 '실패'가 아니므로 회차(CycleIndex)는 그대로 유지하고 단계만 1로 초기화한다.
			}
			Context.UpchigiGlobalNet = 0m;
		}
		roomState.StepVersion++;
	}

	private void HandleLabouchereResult(string tableId, RoomBettingState roomState, bool isTie, bool isWin, int stepBefore, ref int nextStep, ref bool shouldFireLastStepReset, ref bool shouldFireStepReset)
	{
		if (isTie)
		{
			nextStep = roomState.CurrentStep;
			FileLogger.Log("[AutoBetting][라부셰르] " + tableId + ": Tie - 수열 유지");
			return;
		}
		List<int> seq = roomState.LabouchereSequence;
		if (isWin)
		{
			if (seq.Count > 0)
			{
				seq.RemoveAt(seq.Count - 1);
			}
			if (seq.Count > 0)
			{
				seq.RemoveAt(0);
			}
			if (seq.Count == 0)
			{
				if (Context.IsChainMode && Context.ChainSteps[Context.CurrentChainStepIndex].StrategyCompleteTransition)
				{
					FileLogger.Log("[AutoBetting][라부셰르] " + tableId + ": ★ 사이클 완주 → 체인 전환 대기 ★");
				}
				else
				{
					FileLogger.Log("[AutoBetting][라부셰르] " + tableId + ": ★ 사이클 완주! ★ 리셋");
					roomState.LabouchereSequence = Context.LabouchereInitialSequence.ToList();
					roomState.CurrentStep = 1;
				}
			}
			else
			{
				FileLogger.Log($"[AutoBetting][라부셰르] {tableId}: 당첨! 수열=[{string.Join(",", seq)}]");
			}
		}
		else
		{
			int num;
			if (seq.Count < 2)
			{
				num = ((seq.Count == 1) ? seq[0] : 0);
			}
			else
			{
				num = seq[0] + seq[seq.Count - 1];
			}
			int betUnits = num;
			if (betUnits > 0)
			{
				seq.Add(betUnits);
			}
			if (seq.Count > 20)
			{
				FileLogger.Log($"[AutoBetting][라부셰르] {tableId}: 수열 길이 초과({seq.Count}) → 리셋");
				roomState.LabouchereSequence = Context.LabouchereInitialSequence.ToList();
				roomState.CurrentStep = 1;
			}
			else
			{
				FileLogger.Log($"[AutoBetting][라부셰르] {tableId}: 미당첨 - 수열=[{string.Join(",", seq)}]");
			}
		}
		roomState.StepVersion++;
		nextStep = roomState.CurrentStep;
	}

	private void SwitchToDAlembertPhase(string triggerTableId)
	{
		FileLogger.Log($"[AutoBetting][마틴+달랑] ★ 달랑베르 전환! ★ 트리거방={triggerTableId}, 진입잔액={Context.MartinEntryBalance:N0}, 현재잔액={Context.CurrentBalance:N0}");
		Context.MartinBetAmountsBackup = new Dictionary<int, decimal>(Context.BetAmounts);
		Context.BetAmounts = Enumerable.Range(1, 50).ToDictionary((int step) => step, (int step) => Context.DAlembertBaseAmount * (decimal)step);
		ResetAllRoomSteps();
		Context.IsDAlembertPhase = true;
		FileLogger.Log($"[AutoBetting][마틴+달랑] 달랑베르 전환 완료 - 기본금액={Context.DAlembertBaseAmount:N0}, 1단계금액={Context.BetAmounts.GetValueOrDefault(1):N0}");
	}

	private void SwitchToMartinPhase(string triggerTableId)
	{
		FileLogger.Log($"[AutoBetting][마틴+달랑] ★ 마틴 복귀! ★ 트리거방={triggerTableId}, 진입잔액={Context.MartinEntryBalance:N0}, 현재잔액={Context.CurrentBalance:N0}");
		Context.BetAmounts = Context.MartinBetAmountsBackup.Where((KeyValuePair<int, decimal> kv) => kv.Key <= Context.MartinMaxStep).ToDictionary((KeyValuePair<int, decimal> kv) => kv.Key, (KeyValuePair<int, decimal> kv) => kv.Value);
		ResetAllRoomSteps();
		Context.IsDAlembertPhase = false;
		Context.MartinEntryBalance = Context.AvailableBalance;
		FileLogger.Log($"[AutoBetting][마틴+달랑] 마틴 복귀 완료 - 새 진입잔액={Context.MartinEntryBalance:N0}, 1단계금액={Context.BetAmounts.GetValueOrDefault(1):N0}");
	}

	public void ResetAllRoomsToFirstStep()
	{
		decimal loweredPeak;
		lock (_lock)
		{
			loweredPeak = Context.AvailableBalance;
			ResetAllRoomSteps();
			ResetSecondLogicCycle(clearActiveRoom: true, "수동 초기화");
			if (loweredPeak > 0m)
			{
				Context.PeakBalance = loweredPeak;
			}
		}
		FileLogger.Log((loweredPeak > 0m) ? $"[AutoBetting] 행렬 수동 초기화 - 모든 방 1차 1단계 복귀, 선현재금액 → {loweredPeak:N0}원으로 하향" : "[AutoBetting] 행렬 수동 초기화 - 모든 방 1차 1단계 복귀 (잔액 미감지로 선현재금액은 유지)");
	}

	private void ResetAllRoomSteps(string? excludeTableId = null, bool protectInFlightRooms = false)
	{
		List<string> deferred = new List<string>();
		foreach (KeyValuePair<string, RoomBettingState> kvp in Context.RoomStates)
		{
			if (!(kvp.Key == excludeTableId))
			{
				if (protectInFlightRooms && IsBetMoneyOutstanding(kvp.Value))
				{
					kvp.Value.IsResetDeferred = true;
					deferred.Add(kvp.Key);
				}
				else
				{
					ResetRoomStepCore(kvp.Value);
				}
			}
		}
		if (deferred.Count == 0)
		{
			Context.PendingBetAmount = 0m;
		}
		_slCarryStep = null;
		_slCarryMatrix = null;
		FileLogger.Log("[AutoBetting] 모든 방 1단계 초기화(행렬 1차-1단계·누적당첨 0 포함), 결과 대기 해제, " + ((deferred.Count == 0) ? "진행중베팅=0" : $"진행중베팅={Context.PendingBetAmount:N0} 유지 — 이미 베팅이 나간 {deferred.Count}개 방({string.Join(", ", deferred)})은 결과 판정 후 초기화"));
	}

	private static bool IsBetMoneyOutstanding(RoomBettingState room)
	{
		if (room.IsRequestInFlight)
		{
			return true;
		}
		if (!room.IsWaitingForResult)
		{
			return false;
		}
		DateTime? lastBetTime = room.LastBetTime;
		if (lastBetTime.HasValue)
		{
			DateTime betTime = lastBetTime.GetValueOrDefault();
			return (DateTime.Now - betTime).TotalMinutes < 3.0;
		}
		return false;
	}

	private void ResetRoomStepCore(RoomBettingState room)
	{
		room.CurrentStep = 1;
		room.StepVersion++;
		room.IsWaitingForResult = false;
		room.IsIdleRound = false;
		room.LastBetAmount = 0m;
		room.IsDoubleUpPhase = false;
		room.DoubleUpAmount = 0m;
		room.DoubleUpTarget = null;
		room.IsResetDeferred = false;
		room.OscarSessionProfit = 0m;
		room.OscarCurrentBetUnits = 1;
		if (Context.SelectedBetAmountType == 9)
		{
			room.LabouchereSequence = Context.LabouchereInitialSequence.ToList();
		}
		if (Context.IsChainMode)
		{
			room.ConsecutiveLossCount = 0;
			room.ChainStepLossAmount = 0m;
		}
		room.IsDalmenzingRecoveryPhase = false;
		room.DalmenzingDAlembertNetLoss = 0m;
		room.DalmenzingRecoveryNetProfit = 0m;
		room.DalmenzingRecoveryStep = 1;
		room.DalmenzingRecoveryDoubleUpPhase = false;
		room.DalmenzingRecoveryDoubleUpAmount = 0m;
		room.MatrixCurrentRow = 1;
		room.MatrixCurrentCol = 1;
		room.MatrixRowWinCount = 0;
		room.MatrixCycleNet = 0m;
		Array.Clear(room.MatrixRowEntryNet);
	}

	private bool ApplyDeferredResetIfNeeded(string tableId, RoomBettingState room)
	{
		if (!room.IsResetDeferred)
		{
			return false;
		}
		string stepBefore = (Context.IsMatrixStrategy ? $"{room.MatrixCurrentRow}차-{room.MatrixCurrentCol}단계" : $"{room.CurrentStep}단계");
		ResetRoomStepCore(room);
		FileLogger.Log($"[AutoBetting] {tableId}: 유예했던 모도 초기화 적용 ({stepBefore} → 1단계) - 나가 있던 베팅의 판정이 끝났다");
		return true;
	}

	private void ResetSecondLogicCycle(bool clearActiveRoom, string reason)
	{
		if (!IsSecondLogic)
		{
			return;
		}
		string roomBefore = _secondLogic.ActiveRoom ?? "없음";
		object obj;
		if (!IsSecondLogicMatrix)
		{
			obj = _slCarryStep?.ToString() ?? "없음";
		}
		else
		{
			MatrixCarry? slCarryMatrix = _slCarryMatrix;
			if (slCarryMatrix.HasValue)
			{
				MatrixCarry m = slCarryMatrix.GetValueOrDefault();
				obj = $"{m.Row}차-{m.Col}단계";
			}
			else
			{
				obj = "없음";
			}
		}
		string carryBefore = (string)obj;
		_slRidingTarget = null;
		_slCarryStep = null;
		_slCarryMatrix = null;
		if (clearActiveRoom)
		{
			_secondLogic.Reset();
			_slLastRoom = null;
			_slPendingReleaseRoom = null;
			_slSplitYieldCount = 0;
		}
		else
		{
			_secondLogic.ResetCycleKeepingRoom();
		}
		FileLogger.Log($"[세컨로직] 모도 초기화 - {reason} → 줄타기 해제, 연속당첨·연속미당첨 0, 이월값 비움(직전={carryBefore}), 활성 방 {(clearActiveRoom ? ("비움(직전=" + roomBefore + ", 다음 방 제한 없음)") : ("유지(" + roomBefore + ")"))}");
	}

	private async Task SettleBettingHistoryInDbAsync(string betType, decimal betAmount, bool isWin, bool isTie, string tableId, string gameId)
	{
		try
		{
			string status;
			decimal? winAmount;
			decimal? profit;
			if (isTie)
			{
				status = "Tie";
				winAmount = betAmount;
				profit = 0m;
			}
			else if (isWin)
			{
				status = "Won";
				if (string.Equals(betType, "Banker", StringComparison.OrdinalIgnoreCase))
				{
					winAmount = betAmount * 1.95m;
					profit = betAmount * 0.95m;
				}
				else
				{
					winAmount = betAmount * 2m;
					profit = betAmount;
				}
			}
			else
			{
				status = "Lost";
				winAmount = 0m;
				profit = -betAmount;
			}
			if (!Context.IsVirtualMode)  // 가상베팅 정산은 서버 미기록(로컬 검증용)
			{
				await GetApi().SettleBettingHistoryAsync(betType, betAmount, status, winAmount, profit, Context.IsVirtualMode, tableId, gameId);
			}
			FileLogger.Log($"[AutoBetting] 베팅 정산 완료: {betType} {betAmount:N0} → {status} (profit={profit:+#,##0;-#,##0})");
		}
		catch (Exception ex)
		{
			FileLogger.Log("[AutoBetting] 베팅 정산 실패: " + ex.Message, "Warning", "AutoBetting");
		}
	}

	private void CheckPeakBalance()
	{
		if (!IsRunning || !Context.IsPeakBalanceResetEnabled)
		{
			return;
		}
		bool shouldFirePeakReset = false;
		bool shouldFirePendingBetReset = false;
		decimal newPeakBalance = 0m;
		int globalStepVersion = 0;
		lock (_lock)
		{
			decimal balance = Context.CurrentBalance;
			decimal effectiveBalance = (Context.IsIgnorePendingBetEnabled ? balance : (balance - Context.PendingBetAmount));
			if (effectiveBalance > Context.PeakBalance)
			{
				Context.PeakBalance = effectiveBalance;
				newPeakBalance = effectiveBalance;
				if (!_isPaused)
				{
					ResetAllRoomSteps(null, protectInFlightRooms: true);
					BlockCurrentRoundsForReset($"고점 갱신({effectiveBalance:N0}원)");
					ResetSecondLogicCycle(clearActiveRoom: true, $"고점 갱신({effectiveBalance:N0}원)");
					globalStepVersion = Context.RoomStates.Values.DefaultIfEmpty().Max((RoomBettingState r) => r?.StepVersion ?? 0);
					_isPaused = true;
					Interlocked.Exchange(ref _pauseStartTimeTicks, DateTime.Now.Ticks);
					shouldFirePeakReset = true;
					shouldFirePendingBetReset = true;
					FileLogger.Log($"[AutoBetting] 고점 갱신 ({effectiveBalance:N0}원, 잔액={balance:N0}, 진행중베팅={Context.PendingBetAmount:N0}) - {15}초 일시정지 시작");
				}
				else
				{
					FileLogger.Log($"[AutoBetting] 고점 갱신 ({effectiveBalance:N0}원) - 일시정지 중이므로 타이머 유지");
				}
			}
		}
		if (shouldFirePeakReset)
		{
			CancelAllBettingTimers();
		}
		if (shouldFirePendingBetReset)
		{
			PendingBetAmountChanged?.Invoke(Context.PendingBetAmount);
		}
		if (shouldFirePeakReset)
		{
			PeakBalanceReset?.Invoke(this, new PeakResetEventArgs
			{
				NewPeakBalance = newPeakBalance,
				PauseDurationSeconds = 15,
				GlobalStepVersion = globalStepVersion
			});
			ResumeAfterPauseAsync();
		}
	}

	private void CancelAllBettingTimers()
	{
		int cancelled = 0;
		foreach (KeyValuePair<string, CancellationTokenSource> kvp in _bettingCts)
		{
			if (_bettingCts.TryRemove(kvp.Key, out CancellationTokenSource cts))
			{
				cts.Cancel();
				cancelled++;
			}
		}
		if (cancelled > 0)
		{
			FileLogger.Log($"[AutoBetting] 예약된 베팅 타이머 {cancelled}건 취소");
		}
	}

	private void BlockCurrentRoundsForReset(string reason)
	{
		if (_tableStates == null)
		{
			return;
		}
		List<string> blocked = new List<string>();
		foreach (KeyValuePair<string, TableState> kvp in _tableStates)
		{
			if (kvp.Value.Phase == BettingPhase.BetsOpen && (_selectedTables.Count <= 0 || IsSelectedTable(kvp.Key)))
			{
				_resetBlockedRounds[kvp.Key] = kvp.Value.GameId;
				blocked.Add(kvp.Key);
			}
		}
		if (blocked.Count > 0)
		{
			FileLogger.Log($"[AutoBetting] {reason} - 베팅이 열려 있던 {blocked.Count}개 방의 이번 라운드 봉인, 다음 라운드부터 재개 ({string.Join(", ", blocked)})");
		}
	}

	private void ClearResetBlockIfNewRound(string tableId, string currentGameId)
	{
		if (_resetBlockedRounds.TryGetValue(tableId, out string blockedGameId) && (string.IsNullOrEmpty(blockedGameId) || !string.Equals(blockedGameId, currentGameId, StringComparison.Ordinal)))
		{
			_resetBlockedRounds.TryRemove(tableId, out string _);
			FileLogger.Log($"[AutoBetting] {tableId}: 새 라운드({currentGameId}) 시작 - 모도 초기화 봉인 해제, 베팅 재개");
		}
	}

	private bool IsRoundBlockedByReset(string tableId, string currentGameId)
	{
		if (!_resetBlockedRounds.TryGetValue(tableId, out string blockedGameId))
		{
			return false;
		}
		if (!string.IsNullOrEmpty(blockedGameId) && !string.Equals(blockedGameId, currentGameId, StringComparison.Ordinal))
		{
			_resetBlockedRounds.TryRemove(tableId, out string _);
			FileLogger.Log($"[AutoBetting] {tableId}: 봉인된 라운드({blockedGameId})가 지나감({currentGameId}) - 베팅 재개");
			return false;
		}
		return true;
	}

	private bool CheckLimits()
	{
		if (!IsRunning)
		{
			return true;
		}
		decimal start = Context.StartBalance;
		if (start <= 0m)
		{
			return true;
		}
		if (_isCycleResting)
		{
			return false;
		}
		if (_isProfitCoolingDown)
		{
			return false;
		}
		if (_isMatrixResting)
		{
			return false;
		}
		decimal effectiveBalance = Context.CurrentBalance - Context.PendingBetAmount;
		decimal upperBase;
		decimal profitAmount;
		decimal profitPercent;
		bool num;
		if (Context.IsUpperLimitEnabled)
		{
			upperBase = ((Context.IsTargetCycleEnabled && Context.CycleStartBalance > 0m) ? Context.CycleStartBalance : start);
			profitAmount = effectiveBalance - upperBase;
			profitPercent = ((upperBase > 0m) ? (profitAmount / upperBase * 100m) : 0m);
			if (Context.UpperLimitMode != 1)
			{
				if (Context.UpperLimitPercent.HasValue)
				{
					num = profitPercent >= Context.UpperLimitPercent.Value;
					goto IL_0150;
				}
			}
			else if (Context.TargetProfitAmount.HasValue)
			{
				num = profitAmount >= Context.TargetProfitAmount.Value;
				goto IL_0150;
			}
		}
		goto IL_03ef;
		IL_03ef:
		if (Context.IsLowerLimitEnabled && Context.LowerLimitPercent.HasValue)
		{
			decimal changePercent = (effectiveBalance - start) / start * 100m;
			if (changePercent <= -Context.LowerLimitPercent.Value)
			{
				FileLogger.Log($"[AutoBetting] 하한 도달: {changePercent:F2}% <= -{Context.LowerLimitPercent}% (유효잔액={effectiveBalance:N0}, 시작={start:N0})");
				StopAsync($"하한 도달 ({changePercent:F1}%)");
				return false;
			}
		}
		if (IsSecondLogic && _slUseProfitCooldown)
		{
			decimal cooldownBase = Context.SecondLogicCooldownBaseBalance;
			if (cooldownBase > 0m)
			{
				decimal cooldownProfit = effectiveBalance - cooldownBase;
				if (cooldownProfit >= _slCooldownProfitAmount)
				{
					EnterProfitCooldown(cooldownProfit, cooldownBase);
					return false;
				}
			}
		}
		return true;
		IL_0150:
		if (num)
		{
			string detail = ((Context.UpperLimitMode == 1) ? $"+{profitAmount:N0}원" : $"+{profitPercent:F1}%");
			if (Context.IsTargetCycleEnabled)
			{
				int? sessionCount = Context.SessionCount;
				if (sessionCount.HasValue)
				{
					int total = sessionCount.GetValueOrDefault();
					if (total > 0 && Context.CompletedCycles + 1 < total)
					{
						Context.CompletedCycles++;
						FileLogger.Log($"[AutoBetting] 목표 달성 ({detail}) - 사이클 {Context.CompletedCycles}/{total} 완료, {Context.RestIntervalMinutes}분 휴식 후 재시작");
						EnterCycleRest();
						return false;
					}
				}
			}
			if (Context.IsTargetCycleEnabled)
			{
				Context.CompletedCycles++;
			}
			string reason = (Context.IsTargetCycleEnabled ? $"목표 사이클 완료 ({Context.CompletedCycles}/{Context.SessionCount}회, 마지막 {detail})" : ("상한 도달 (" + detail + ")"));
			FileLogger.Log($"[AutoBetting] {reason} (유효잔액={effectiveBalance:N0}, 기준={upperBase:N0})");
			StopAsync(reason);
			return false;
		}
		goto IL_03ef;
	}

	private void EnterCycleRest()
	{
		_isCycleResting = true;
		foreach (KeyValuePair<string, CancellationTokenSource> kvp in _bettingCts)
		{
			if (_bettingCts.TryRemove(kvp.Key, out CancellationTokenSource cts))
			{
				cts.Cancel();
			}
		}
		lock (_lock)
		{
			Context.PendingBetAmount = 0m;
			Context.RoomStates.Clear();
			Context.UpchigiGlobalNet = 0m;
		}
		int minutes = Context.RestIntervalMinutes.GetValueOrDefault();
		RaiseStateChanged(isRunning: true, $"목표 달성 - {minutes}분 휴식 중 ({Context.CompletedCycles}/{Context.SessionCount}회 완료)");
		_cycleRestCts?.Cancel();
		_cycleRestCts = new CancellationTokenSource();
		ResumeAfterCycleRestAsync(minutes, _cycleRestCts.Token);
	}

	private async Task ResumeAfterCycleRestAsync(int minutes, CancellationToken token)
	{
		try
		{
			await Task.Delay(TimeSpan.FromMinutes(minutes), token);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		if (IsRunning && !token.IsCancellationRequested)
		{
			Context.CycleStartBalance = Context.CurrentBalance;
			Context.LastGlobalBetTime = DateTime.Now;
			_isCycleResting = false;
			FileLogger.Log($"[AutoBetting] 휴식 종료 - 새 사이클 시작 (시작잔액 {Context.CycleStartBalance:N0}원, {Context.CompletedCycles}/{Context.SessionCount}회 완료)");
			RaiseStateChanged(isRunning: true, $"휴식 종료 - 베팅 재개 ({Context.CompletedCycles}/{Context.SessionCount}회 완료)");
		}
	}

	private void EnterMatrixRest(int minutes)
	{
		if (minutes <= 0)
		{
			return;
		}
		if (_isMatrixResting)
		{
			FileLogger.Log($"[AutoBetting][{GetBetAmountTypeName(Context.SelectedBetAmountType)}] 차별 휴식 요청({minutes}분) 무시 - 이미 휴식 중", "Info", "AutoBetting");
			return;
		}
		_isMatrixResting = true;
		foreach (KeyValuePair<string, CancellationTokenSource> kvp in _bettingCts)
		{
			if (_bettingCts.TryRemove(kvp.Key, out CancellationTokenSource cts))
			{
				cts.Cancel();
			}
		}
		string restLogTag = GetBetAmountTypeName(Context.SelectedBetAmountType);
		FileLogger.Log($"[AutoBetting][{restLogTag}] ★ 차 소진 → {minutes}분 휴식 시작 (봇 전체 베팅 정지, 차수/단계는 그대로 보존)", "Warning", "AutoBetting");
		RaiseStateChanged(isRunning: true, $"{restLogTag} 차 소진 - {minutes}분 휴식 중");
		_matrixRestCts?.Cancel();
		_matrixRestCts = new CancellationTokenSource();
		ResumeAfterMatrixRestAsync(minutes, _matrixRestCts.Token);
	}

	private async Task ResumeAfterMatrixRestAsync(int minutes, CancellationToken token)
	{
		try
		{
			await Task.Delay(TimeSpan.FromMinutes(minutes), token);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		if (IsRunning && !token.IsCancellationRequested)
		{
			Context.LastGlobalBetTime = DateTime.Now;
			_isMatrixResting = false;
			string restLogTag = GetBetAmountTypeName(Context.SelectedBetAmountType);
			FileLogger.Log($"[AutoBetting][{restLogTag}] 휴식 종료({minutes}분 경과) - 베팅 재개", "Warning", "AutoBetting");
			RaiseStateChanged(isRunning: true, $"{restLogTag} 휴식 종료({minutes}분) - 베팅 재개");
		}
	}

	private void EnterProfitCooldown(decimal profitAmount, decimal baseBalance)
	{
		int globalStepVersion;
		lock (_lock)
		{
			if (_isProfitCoolingDown)
			{
				return;
			}
			_isProfitCoolingDown = true;
			ResetAllRoomSteps();
			ResetSecondLogicCycle(clearActiveRoom: true, $"수익 쿨다운 진입(+{profitAmount:N0}원)");
			globalStepVersion = Context.RoomStates.Values.DefaultIfEmpty().Max((RoomBettingState r) => r?.StepVersion ?? 0);
		}
		foreach (KeyValuePair<string, CancellationTokenSource> kvp in _bettingCts)
		{
			if (_bettingCts.TryRemove(kvp.Key, out CancellationTokenSource cts))
			{
				cts.Cancel();
			}
		}
		int minutes = _slCooldownMinutes;
		FileLogger.Log($"[세컨로직][쿨다운] 진입 - 기준 {baseBalance:N0}원 대비 +{profitAmount:N0}원 (설정 +{_slCooldownProfitAmount:N0}원) 달성 → {minutes}분 베팅 정지, 모도 초기화 완료");
		PendingBetAmountChanged?.Invoke(0m);
		ProfitCooldownStarted?.Invoke(this, new ProfitCooldownEventArgs
		{
			ProfitAmount = profitAmount,
			BaseBalance = baseBalance,
			CooldownMinutes = minutes,
			GlobalStepVersion = globalStepVersion
		});
		RaiseStateChanged(isRunning: true, $"세컨로직 쿨다운 - +{profitAmount:N0}원 달성, {minutes}분 베팅 정지");
		_profitCooldownCts?.Cancel();
		_profitCooldownCts = new CancellationTokenSource();
		ResumeAfterProfitCooldownAsync(minutes, _profitCooldownCts.Token);
	}

	private async Task ResumeAfterProfitCooldownAsync(int minutes, CancellationToken token)
	{
		try
		{
			await Task.Delay(TimeSpan.FromMinutes(minutes), token);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		if (IsRunning && !token.IsCancellationRequested)
		{
			decimal newBase = Context.CurrentBalance - Context.PendingBetAmount;
			Context.SecondLogicCooldownBaseBalance = newBase;
			Context.LastGlobalBetTime = DateTime.Now;
			_isProfitCoolingDown = false;
			FileLogger.Log($"[세컨로직][쿨다운] 해제({minutes}분 경과) - 베팅 재개, 기준 잔액 재설정 {newBase:N0}원 (다음 쿨다운은 여기서 +{_slCooldownProfitAmount:N0}원)");
			ProfitCooldownEnded?.Invoke(this, new ProfitCooldownEndedEventArgs
			{
				NewBaseBalance = newBase
			});
			RaiseStateChanged(isRunning: true, $"쿨다운 종료 - 베팅 재개 (기준 {newBase:N0}원)");
		}
	}

	private async Task ResumeAfterPauseAsync()
	{
		await Task.Delay(15000);
		_isPaused = false;
		Interlocked.Exchange(ref _pauseStartTimeTicks, 0L);
		FileLogger.Log($"[AutoBetting] 일시정지 해제({15}초 경과), 베팅 재개");
	}

	private void StartInactivityTimer()
	{
		StopInactivityTimer();
		_inactivityTimer = new System.Timers.Timer(30000.0);
		_inactivityTimer.Elapsed += OnInactivityTimerElapsed;
		_inactivityTimer.AutoReset = true;
		_inactivityTimer.Start();
	}

	private void StopInactivityTimer()
	{
		if (_inactivityTimer != null)
		{
			_inactivityTimer.Stop();
			_inactivityTimer.Elapsed -= OnInactivityTimerElapsed;
			_inactivityTimer.Dispose();
			_inactivityTimer = null;
		}
	}

	private async void OnInactivityTimerElapsed(object? sender, ElapsedEventArgs e)
	{
		if (Interlocked.CompareExchange(ref _inactivityTimerRunning, 1, 0) != 0)
		{
			return;
		}
		try
		{
			if (!IsRunning || Context.IsRefreshing)
			{
				return;
			}
			// [진단] 무베팅이 5분 넘게 이어지는데 쿨다운/휴식 플래그가 걸려 복구 체크가 스킵되는 경우,
			// 그 플래그가 안 풀리면 봇은 영영 조용히 멈춘 채로 있는다. 스로틀해서 드러낸다.
			if (_isCycleResting || _isProfitCoolingDown || _isMatrixResting)
			{
				if (Context.LastGlobalBetTime.HasValue && (DateTime.Now - Context.LastGlobalBetTime.Value).TotalSeconds >= 300.0)
				{
					long nowTicks = DateTime.UtcNow.Ticks;
					long prevTicks = Interlocked.Read(ref _lastStallDiagTicks);
					if (prevTicks == 0L || (new DateTime(nowTicks, DateTimeKind.Utc) - new DateTime(prevTicks, DateTimeKind.Utc)).TotalSeconds >= 120.0)
					{
						Interlocked.Exchange(ref _lastStallDiagTicks, nowTicks);
						FileLogger.Log($"[AutoBetting][진단] 무베팅 {(DateTime.Now - Context.LastGlobalBetTime.Value).TotalMinutes:F1}분인데 복구가 스킵됨 — 쉬는 플래그 활성(사이클휴식={_isCycleResting}, 수익쿨다운={_isProfitCoolingDown}, 행렬휴식={_isMatrixResting}). 이 플래그가 풀리지 않으면 베팅이 재개되지 않습니다.", "Warning", "AutoBetting");
					}
				}
				return;
			}
			if (Context.LastGlobalBetTime.HasValue)
			{
				TimeSpan inactiveDuration = DateTime.Now - Context.LastGlobalBetTime.Value;
				if (!(inactiveDuration.TotalSeconds < 300.0))
				{
					FileLogger.Log($"[AutoBetting] 비활성 감지: {inactiveDuration.TotalMinutes:F1}분 베팅 없음 → 새로고침 시도 (IsRunning={IsRunning})", "Warning", "AutoBetting");
					Context.IsRefreshing = true;
					InactivityRefreshStarted?.Invoke(this, new InactivityRefreshEventArgs
					{
						InactiveDuration = inactiveDuration,
						RefreshWaitSeconds = 10
					});
					await Task.Delay(10000);
					Context.IsRefreshing = false;
					Context.LastGlobalBetTime = DateTime.Now;
					RefreshCompleted?.Invoke(this, EventArgs.Empty);
				}
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[AutoBetting] 비활성 체크 오류: " + ex.Message, "Error", "AutoBetting");
			Context.IsRefreshing = false;
		}
		finally
		{
			Interlocked.Exchange(ref _inactivityTimerRunning, 0);
		}
	}

	private void StartKeepAliveTimer()
	{
		StopKeepAliveTimer();
		if (Context.IsKeepAliveBetEnabled)
		{
			if (Context.KeepAliveBetAmount <= 0m || Context.KeepAliveIntervalSeconds <= 0)
			{
				FileLogger.Log($"[KeepAlive] 설정값이 유효하지 않아 비활성 (금액={Context.KeepAliveBetAmount:N0}, 주기={Context.KeepAliveIntervalSeconds}초)", "Warning", "AutoBetting");
				return;
			}
			_keepAliveRotationIndex = 0;
			_keepAliveConsecutiveFails = 0;
			Interlocked.Exchange(ref _keepAlivePending, null);
			Volatile.Write(ref _lastRealBetSentTicks, 0L);
			_keepAliveNextDueAt = DateTime.Now.AddSeconds(Context.KeepAliveIntervalSeconds);
			_keepAliveTimer = new System.Timers.Timer(3000.0);
			_keepAliveTimer.Elapsed += OnKeepAliveTimerElapsed;
			_keepAliveTimer.AutoReset = true;
			_keepAliveTimer.Start();
			FileLogger.Log($"[KeepAlive] 활성({(Context.IsVirtualMode ? "가상" : "실전")}) — {(double)Context.KeepAliveIntervalSeconds / 60.0:0.#}분마다 {Context.KeepAliveBetType} {Context.KeepAliveBetAmount:N0}원 실베팅 (방 1개씩 로테이션, 첫 전송 예정 {_keepAliveNextDueAt:HH:mm:ss})" + (Context.IsVirtualMode ? "" : " — 실전은 마지막 실베팅 이후 한 주기가 지난 유휴 구간에만 전송"), "Info", "AutoBetting");
		}
	}

	private void StopKeepAliveTimer()
	{
		if (_keepAliveTimer != null)
		{
			_keepAliveTimer.Stop();
			_keepAliveTimer.Elapsed -= OnKeepAliveTimerElapsed;
			_keepAliveTimer.Dispose();
			_keepAliveTimer = null;
		}
		Interlocked.Exchange(ref _keepAlivePending, null);
	}

	private async void OnKeepAliveTimerElapsed(object? sender, ElapsedEventArgs e)
	{
		if (Interlocked.CompareExchange(ref _keepAliveTickRunning, 1, 0) != 0)
		{
			return;
		}
		try
		{
			await TrySendKeepAliveBetAsync();
		}
		catch (Exception ex)
		{
			FileLogger.Log("[KeepAlive] 처리 오류: " + ex.Message, "Error", "AutoBetting", ex.StackTrace);
		}
		finally
		{
			Interlocked.Exchange(ref _keepAliveTickRunning, 0);
		}
	}

	private async Task TrySendKeepAliveBetAsync()
	{
		if (!IsRunning || _tableStates == null || !Context.IsKeepAliveBetEnabled || Context.IsRefreshing || DateTime.Now < _keepAliveNextDueAt || (!Context.IsVirtualMode && !PassesRealModeKeepAliveGuards()))
		{
			return;
		}
		if (string.IsNullOrEmpty(Context.PpUserId))
		{
			FileLogger.Log("[KeepAlive] PP UserId 미설정 → 전송 보류 (seat 메시지 대기 중)", "Warning", "AutoBetting");
			_keepAliveNextDueAt = DateTime.Now.AddSeconds(20.0);
			return;
		}
		var (tableId, state) = PickKeepAliveTable();
		if (tableId == null || state == null)
		{
			return;
		}
		decimal amount = Context.KeepAliveBetAmount;
		string betType = Context.KeepAliveBetType;
		string gameId = state.GameId;
		FileLogger.Log($"[KeepAlive] {tableId}: 전송 시도 - {betType} {amount:N0}원 (남은 {state.RemainingTimeMs:F0}ms, gameId={gameId})", "Info", "AutoBetting");
		BetResult result;
		try
		{
			result = await _bettingService.PlaceBetAsync(state, betType, amount, Context.PpUserId);
		}
		catch (Exception ex)
		{
			result = BetResult.Fail("전송 예외: " + ex.Message);
		}
		Context.RecordKeepAliveBet(amount, result.IsSuccess);
		if (result.IsSuccess)
		{
			_keepAliveConsecutiveFails = 0;
			Interlocked.Exchange(ref _keepAlivePending, new KeepAlivePendingBet(tableId, gameId, betType, amount, DateTime.Now));
			_keepAliveNextDueAt = DateTime.Now.AddSeconds(Context.KeepAliveIntervalSeconds);
			FileLogger.Log($"[KeepAlive] {tableId}: 전송 성공{(result.IsUnconfirmed ? "(PP 미확인)" : "")} — 누적 {Context.KeepAliveBetCount}건 / {Context.KeepAliveTotalAmount:N0}원, 다음 예정 {_keepAliveNextDueAt:HH:mm:ss}", "Info", "AutoBetting");
		}
		else
		{
			_keepAliveConsecutiveFails++;
			int retrySeconds = Math.Min(20 * _keepAliveConsecutiveFails, 120);
			_keepAliveNextDueAt = DateTime.Now.AddSeconds(retrySeconds);
			FileLogger.Log($"[KeepAlive] {tableId}: 전송 실패 - {result.ErrorMessage} → {retrySeconds}초 후 재시도 (연속 {_keepAliveConsecutiveFails}회, 누적 실패 {Context.KeepAliveFailCount}건)", "Warning", "AutoBetting");
			if (_keepAliveConsecutiveFails == 3)
			{
				FileLogger.Log($"[KeepAlive] ★ 세션 유지 베팅이 3회 연속 실패했습니다. 실제 잔액 부족이나 테이블 최소 베팅액({Context.KeepAliveBetAmount:N0}원 미만 불가) 여부를 확인해 주세요. 이 상태가 지속되면 PP 서버가 세션을 끊습니다.", "Error", "AutoBetting");
			}
		}
		KeepAliveBetExecuted?.Invoke(this, new KeepAliveBetEventArgs
		{
			TableId = tableId,
			GameId = gameId,
			BetType = betType,
			Amount = amount,
			IsSuccess = result.IsSuccess,
			IsUnconfirmed = result.IsUnconfirmed,
			ErrorMessage = result.ErrorMessage,
			TotalCount = Context.KeepAliveBetCount,
			TotalAmount = Context.KeepAliveTotalAmount,
			NetSpend = Context.KeepAliveNetSpend
		});
	}

	private bool PassesRealModeKeepAliveGuards()
	{
		DateTime now = DateTime.Now;
		long lastRealTicks = Volatile.Read(in _lastRealBetSentTicks);
		if (lastRealTicks > 0 && (now - new DateTime(lastRealTicks)).TotalSeconds < (double)Context.KeepAliveIntervalSeconds)
		{
			_keepAliveNextDueAt = new DateTime(lastRealTicks).AddSeconds(Context.KeepAliveIntervalSeconds);
			return false;
		}
		if (Context.RoomStates.Values.Any((RoomBettingState r) => r.IsRequestInFlight))
		{
			_keepAliveNextDueAt = now.AddSeconds(5.0);
			return false;
		}
		if (Context.AvailableBalance < Context.KeepAliveBetAmount)
		{
			_keepAliveNextDueAt = now.AddSeconds(120.0);
			FileLogger.Log($"[KeepAlive] 실제 잔액 부족으로 전송 보류 (가용={Context.AvailableBalance:N0}, 필요={Context.KeepAliveBetAmount:N0}) → {120}초 후 재확인", "Warning", "AutoBetting");
			return false;
		}
		return true;
	}

	private bool IsEvolutionRoom(string tableId)
	{
		if (PragmaticMessageParser.IsEvolutionTable(tableId))
		{
			return true;
		}
		return _tableStates != null && _tableStates.TryGetValue(tableId, out TableState st)
			&& string.Equals(st.Provider, GameProviderNames.EvolutionKey, StringComparison.OrdinalIgnoreCase);
	}

	private (string? TableId, TableState? State) PickKeepAliveTable()
	{
		if (_tableStates == null)
		{
			return (TableId: null, State: null);
		}
		bool avoidBusyRooms = !Context.IsVirtualMode;
		// 세션유지 베팅은 프라그마틱 세션을 살려두기 위한 것이다. 에볼루션 방이 섞이면 로테이션이
		// 그쪽으로 가서(DOM 경로, 최소 베팅 10,000원) 실패만 반복하고 프라그마틱에는 한 건도 가지 않았다.
		List<string> order = _tableStates.Keys.Where((string tableId) => (_selectedTables.Count == 0 || IsSelectedTable(tableId)) && !IsEvolutionRoom(tableId)).OrderBy<string, string>((string result) => result, StringComparer.Ordinal).ToList();
		if (order.Count == 0)
		{
			return (TableId: null, State: null);
		}
		int start = _keepAliveRotationIndex % order.Count;
		for (int i = 0; i < order.Count; i++)
		{
			int idx = (start + i) % order.Count;
			string id = order[idx];
			if (_tableStates.TryGetValue(id, out TableState state) && state.IsBettingOpen && !string.IsNullOrEmpty(state.GameId) && !(state.RemainingTimeMs < 4000.0) && (!avoidBusyRooms || !Context.RoomStates.TryGetValue(id, out RoomBettingState roomState) || (!roomState.IsRequestInFlight && !roomState.IsWaitingForResult)))
			{
				_keepAliveRotationIndex = (idx + 1) % order.Count;
				return (TableId: id, State: state);
			}
		}
		return (TableId: null, State: null);
	}

	private void SettleKeepAliveBet(string tableId, GameResult result)
	{
		KeepAlivePendingBet pending = Volatile.Read(in _keepAlivePending);
		if (pending == null || !string.Equals(pending.TableId, tableId, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		if ((DateTime.Now - pending.SentAt).TotalMinutes > 5.0)
		{
			if (Interlocked.CompareExchange(ref _keepAlivePending, null, pending) == pending)
			{
				FileLogger.Log($"[KeepAlive] {tableId}: 정산 만료 폐기 (전송 후 {5}분 경과, 환급 미확인 {pending.Amount:N0}원)", "Warning", "AutoBetting");
			}
		}
		else if ((string.IsNullOrEmpty(pending.GameId) || string.IsNullOrEmpty(result.GameId) || string.Equals(pending.GameId, result.GameId, StringComparison.OrdinalIgnoreCase)) && !(Interlocked.CompareExchange(ref _keepAlivePending, null, pending) != pending))
		{
			bool isTie = string.Equals(result.Winner, "Tie", StringComparison.OrdinalIgnoreCase);
			bool isWin = !isTie && string.Equals(pending.BetType, result.Winner, StringComparison.OrdinalIgnoreCase);
			decimal credit = (isTie ? pending.Amount : ((!isWin) ? 0m : (string.Equals(pending.BetType, "Banker", StringComparison.OrdinalIgnoreCase) ? (pending.Amount * 1.95m) : (pending.Amount * 2m))));
			Context.RecordKeepAliveReturn(credit);
			FileLogger.Log($"[KeepAlive] {tableId}: 정산 - 결과={result.Winner} ({(isTie ? "타이" : (isWin ? "당첨" : "미당첨"))}), 환급 {credit:N0}원 → 순지출 {Context.KeepAliveNetSpend:N0}원 (지출 {Context.KeepAliveTotalAmount:N0} / 환급 {Context.KeepAliveReturnedAmount:N0})", "Info", "AutoBetting");
		}
	}

	private async Task LoadFormatSettingsAsync(string userId)
	{
		List<(int Number, string Text)> rawList = (from f in await GetApi().GetFormatsAsync()
			where !string.IsNullOrWhiteSpace(f.Text)
			select (Number: f.Number, Text: f.Text)).ToList();
		Context.Patterns = _patternParser.ParseAll(rawList.Select<(int Number, string Text), (int Number, string Text)>(((int Number, string Text) x) => (Number: x.Number, Text: x.Text)));
		FileLogger.Log($"[AutoBetting] 서식 로드됨: {Context.Patterns.Count}개 유효 패턴 (원본 {rawList.Count}개)");
		string validBrief = string.Join(", ", Context.Patterns.Select((BettingPattern p) => $"#{p.Number}:{p.RawText}→{p.BetTarget.Substring(0, 1)}"));
		FileLogger.Log("[패턴진단] 등록 패턴 목록: [" + validBrief + "]", "Info", "PatternMatch");
		IEnumerable<string> invalidRaw = from r in rawList
			where !Context.Patterns.Any((BettingPattern p) => p.Number == r.Number)
			select $"#{r.Number}:'{r.Text}'";
		string invalidList = string.Join(", ", invalidRaw);
		if (!string.IsNullOrEmpty(invalidList))
		{
			FileLogger.Log("[패턴진단] ⚠ 파싱 실패 서식: [" + invalidList + "]", "Warning", "PatternMatch");
		}
	}

	private async Task<RoomCooldownSettings> LoadRoomCooldownSettingsAsync()
	{
		try
		{
			RoomCooldownSettingDto dto = await GetApi().GetRoomCooldownSettingsAsync();
			if (dto == null)
			{
				FileLogger.Log("[RoomCooldown] 설정 없음 - 기본 비활성");
				return RoomCooldownSettings.Disabled();
			}
			return new RoomCooldownSettings
			{
				IsEnabled = dto.IsEnabled,
				LossThreshold = dto.LossThreshold,
				CooldownMinutes = dto.CooldownMinutes
			};
		}
		catch (Exception ex)
		{
			FileLogger.Log($"[RoomCooldown] 설정 로드 실패 ({ex.GetType().Name}: {ex.Message}) - 기본 비활성", "Warning", "RoomCooldown");
			return RoomCooldownSettings.Disabled();
		}
	}

	private static string GetBetAmountTypeName(int betAmountType)
	{
		return betAmountType switch
		{
			0 => "마틴", 
			1 => "홍콩크루즈", 
			2 => "피보나치", 
			3 => "혼합", 
			4 => "달랑베르", 
			5 => "마틴+달랑 혼합", 
			6 => "1-3-2-6", 
			7 => "오스카 그라인드", 
			8 => "플랫 베팅", 
			9 => "라부셰르", 
			10 => "달랑멘징", 
			11 => "행렬", 
			12 => "행렬달랑베르", 
			13 => "스위칭행렬",
			14 => "달랑행렬",
			15 => "업치기",
			_ => $"타입{betAmountType}",
		};
	}

	private static int NormalizeSecondLogicBetAmountType(int betAmountType)
	{
		bool flag = (((uint)betAmountType <= 1u || betAmountType == 4) ? true : false);
		if (flag || AutoBettingContext.IsMatrixType(betAmountType))
		{
			return betAmountType;
		}
		FileLogger.Log("[세컨로직] 미지원 베팅금타입(" + GetBetAmountTypeName(betAmountType) + ") → 마틴으로 폴백", "Warning");
		return 0;
	}

	private async Task InitializeMixedModeTableIndexMapAsync(string userId)
	{
		Context.TableIndexMap.Clear();
		try
		{
			ApiClient api = GetApi();
			Task<List<GameSelectionDto>> selectionsTask = api.GetUserGameSelectionsAsync();
			Task<List<GameDto>> gamesTask = api.GetBaccaratGamesAsync();
			InlineArray2<Task> buffer = default(InlineArray2<Task>);
			buffer[0] = selectionsTask;
			buffer[1] = gamesTask;
			await Task.WhenAll(buffer);
			HashSet<int> selectedIds = (from s in selectionsTask.Result
				where s.IsSelected
				select s.GameId).ToHashSet();
			List<GameDto> selectedGames = gamesTask.Result.Where((GameDto g) => selectedIds.Contains(g.Id)).ToList();
			HashSet<string> tableKeySet = selectedGames.Select((GameDto g) => g.PpTableId ?? g.GameCode).ToHashSet();
			Dictionary<string, string> matchedStates = _tableStates?.Values.Where((TableState t) => tableKeySet.Contains(t.TableId)).ToDictionary((TableState t) => t.TableId, (TableState t) => t.DisplayName) ?? new Dictionary<string, string>();
			List<(string, string)> ordered = selectedGames.OrderBy<GameDto, string>((GameDto g) => g.TitleKo, StringComparer.Ordinal).Select(delegate(GameDto g)
			{
				string text = g.PpTableId ?? g.GameCode;
				string item = ((matchedStates.TryGetValue(text, out var value) && !string.IsNullOrEmpty(value)) ? value : (g.TitleKo ?? text));
				return (TableId: text, Name: item);
			}).ToList();
			for (int i = 0; i < ordered.Count; i++)
			{
				Context.TableIndexMap[ordered[i].Item1] = i;
			}
			FileLogger.Log($"[AutoBetting][혼합] 테이블 인덱스 맵 초기화: {Context.TableIndexMap.Count}개");
			foreach (var entry in ordered.Select<(string, string), ((string, string), int)>(((string TableId, string Name) x, int item) => (x: x, i: item)))
			{
				string method = ((entry.Item2 % 2 == 0) ? "마틴" : "홍콩크루즈");
				FileLogger.Log($"[AutoBetting][혼합] 방{entry.Item2 + 1}({entry.Item1.Item2}, {entry.Item1.Item1}) → {method}");
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[AutoBetting][혼합] 테이블 인덱스 맵 초기화 실패: " + ex.Message, "Error", "AutoBetting", ex.StackTrace);
		}
	}

	// [성능] 한 번의 게임 시작(StartAsync) 동안 같은 베팅금 타입을 여러 번 조회하던 중복 서버 왕복을 없애기 위한 캐시.
	// StartAsync 시작 시 새로 만들고 끝나면(finally) 비운다 → 세션 간에는 항상 새로 받으므로 설정 변경이 즉시 반영된다.
	private Dictionary<int, List<BetAmountSettingDto>>? _startBetAmountCache;

	private async Task<List<BetAmountSettingDto>> GetBetAmountsForStartAsync(int betAmountType = 0)
	{
		Dictionary<int, List<BetAmountSettingDto>>? cache = _startBetAmountCache;
		if (cache != null && cache.TryGetValue(betAmountType, out List<BetAmountSettingDto>? cached))
		{
			return cached;
		}
		List<BetAmountSettingDto> list = await GetApi().GetBetAmountsAsync(betAmountType);
		if (cache != null)
		{
			cache[betAmountType] = list;
		}
		return list;
	}

	private async Task LoadBetAmountSettingsAsync(string userId, int betAmountType)
	{
		List<BetAmountSettingDto> amounts = await GetBetAmountsForStartAsync(betAmountType);
		Context.HongKongWinTargetSteps.Clear();
		switch (betAmountType)
		{
		case 5:
		{
			BetAmountSettingDto maxStepEntry2 = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 1);
			AutoBettingContext context3 = Context;
			int martinMaxStep;
			switch (maxStepEntry2?.BettingMethod)
			{
			default:
				martinMaxStep = 0;
				break;
			case 1:
			case 2:
			case 3:
			case 4:
			case 5:
			case 6:
			case 7:
			case 8:
			case 9:
			case 10:
			case 11:
			case 12:
			case 13:
			case 14:
			case 15:
			case 16:
			case 17:
			case 18:
			case 19:
			case 20:
			case 21:
			case 22:
			case 23:
			case 24:
			case 25:
			case 26:
			case 27:
			case 28:
			case 29:
			case 30:
			case 31:
			case 32:
			case 33:
			case 34:
			case 35:
			case 36:
			case 37:
			case 38:
			case 39:
			case 40:
			case 41:
			case 42:
			case 43:
			case 44:
			case 45:
			case 46:
			case 47:
			case 48:
			case 49:
			case 50:
				martinMaxStep = maxStepEntry2.BettingMethod.Value;
				break;
			}
			context3.MartinMaxStep = martinMaxStep;
			BetAmountSettingDto dalembertBaseEntry2 = (await GetBetAmountsForStartAsync(4)).FirstOrDefault(delegate(BetAmountSettingDto b)
			{
				if (b.Number == 1 && b.Amount.HasValue)
				{
					decimal? amount = b.Amount;
					return (amount.GetValueOrDefault() > 0m) & amount.HasValue;
				}
				return false;
			});
			Context.DAlembertBaseAmount = (dalembertBaseEntry2?.Amount).GetValueOrDefault();
			Dictionary<int, decimal> allMartinAmounts = (from b in await GetBetAmountsForStartAsync()
				where b.Amount.HasValue
				orderby b.Number
				select b).ToDictionary((BetAmountSettingDto b) => b.Number, (BetAmountSettingDto b) => b.Amount.Value);
			Context.MartinBetAmountsBackup = new Dictionary<int, decimal>(allMartinAmounts);
			Context.BetAmounts = ((Context.MartinMaxStep > 0) ? allMartinAmounts.Where((KeyValuePair<int, decimal> kv) => kv.Key <= Context.MartinMaxStep).ToDictionary((KeyValuePair<int, decimal> kv) => kv.Key, (KeyValuePair<int, decimal> kv) => kv.Value) : new Dictionary<int, decimal>(allMartinAmounts));
			Context.IsDAlembertPhase = false;
			Context.StepBettingMethods.Clear();
			FileLogger.Log($"[AutoBetting][마틴+달랑] 로드: 마틴 {Context.BetAmounts.Count}단계 (maxStep={Context.MartinMaxStep}), 달랑베르 기본금액={Context.DAlembertBaseAmount:N0}");
			break;
		}
		case 6:
		{
			BetAmountSettingDto baseEntry2 = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 1 && b.Amount > (decimal?)0m);
			Context.BaseAmount1326 = (baseEntry2?.Amount).GetValueOrDefault();
			int[] multipliers = new int[4] { 1, 3, 2, 6 };
			Context.BetAmounts = ((Context.BaseAmount1326 > 0m) ? multipliers.Select((int m, int i) => new
			{
				Step = i + 1,
				Amount = Context.BaseAmount1326 * (decimal)m
			}).ToDictionary(x => x.Step, x => x.Amount) : new Dictionary<int, decimal>());
			Context.StepBettingMethods.Clear();
			break;
		}
		case 7:
		{
			BetAmountSettingDto baseEntry3 = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 1 && b.Amount > (decimal?)0m);
			Context.OscarBaseAmount = (baseEntry3?.Amount).GetValueOrDefault();
			AutoBettingContext context2 = Context;
			int? num = baseEntry3?.BettingMethod;
			context2.OscarTargetUnits = ((!num.HasValue || num.GetValueOrDefault() <= 0) ? 1 : baseEntry3.BettingMethod.Value);
			Context.BetAmounts = ((Context.OscarBaseAmount > 0m) ? Enumerable.Range(1, 50).ToDictionary((int step) => step, (int step) => Context.OscarBaseAmount * (decimal)step) : new Dictionary<int, decimal>());
			Context.StepBettingMethods.Clear();
			FileLogger.Log($"[AutoBetting][오스카] 로드: 기본금={Context.OscarBaseAmount:N0}, 목표단위={Context.OscarTargetUnits}");
			break;
		}
		case 8:
		{
			BetAmountSettingDto baseEntry4 = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 1 && b.Amount > (decimal?)0m);
			Context.FlatBetAmount = (baseEntry4?.Amount).GetValueOrDefault();
			Context.BetAmounts = ((Context.FlatBetAmount > 0m) ? new Dictionary<int, decimal> { { 1, Context.FlatBetAmount } } : new Dictionary<int, decimal>());
			Context.StepBettingMethods.Clear();
			break;
		}
		case 15:
		{
			BetAmountSettingDto startEntry = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 1);
			BetAmountSettingDto flatEntry = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 2);
			BetAmountSettingDto thresholdEntry = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 3);
			BetAmountSettingDto failThrEntry = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 4);
			BetAmountSettingDto failMultEntry = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 5);
			BetAmountSettingDto failMaxMultEntry = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 6);
			Context.UpchigiStartAmount = (startEntry?.Amount).GetValueOrDefault();
			Context.UpchigiStepCount = Math.Max(1, (startEntry?.BettingMethod).GetValueOrDefault(1));
			Context.UpchigiFlatAmount = (flatEntry?.Amount).GetValueOrDefault();
			Context.UpchigiProfitResetThreshold = (thresholdEntry?.Amount).GetValueOrDefault();
			Context.UpchigiFailEscalateThreshold = (int)((failThrEntry?.Amount).GetValueOrDefault());
			Context.UpchigiFailEscalateMultiplier = Math.Max(1m, (failMultEntry?.Amount).GetValueOrDefault(1m));
			Context.UpchigiFailEscalateMaxMultiplier = (failMaxMultEntry?.Amount).GetValueOrDefault();
			Context.BetAmounts = ((Context.UpchigiStartAmount > 0m) ? new Dictionary<int, decimal> { { 1, Context.UpchigiStartAmount } } : new Dictionary<int, decimal>());
			Context.StepBettingMethods.Clear();
			FileLogger.Log($"[AutoBetting][업치기] 로드: 시작={Context.UpchigiStartAmount:N0}, 단계={Context.UpchigiStepCount}, 일정={Context.UpchigiFlatAmount:N0}, 합산수익={Context.UpchigiProfitResetThreshold:N0}, 누적실패임계={Context.UpchigiFailEscalateThreshold}, 실패배수=×{Context.UpchigiFailEscalateMultiplier:0.##}, 배수상한={(Context.UpchigiFailEscalateMaxMultiplier > 1m ? $"×{Context.UpchigiFailEscalateMaxMultiplier:0.##}" : "없음")}");
			break;
		}
		case 9:
		{
			List<BetAmountSettingDto> seqEntries = (from b in amounts
				where b.Amount > (decimal?)0m
				orderby b.Number
				select b).ToList();
			AutoBettingContext context4 = Context;
			int? num = seqEntries.FirstOrDefault()?.BettingMethod;
			context4.LabouchereMultiplier = ((num.HasValue && num.GetValueOrDefault() > 0) ? seqEntries.First().BettingMethod.Value : 1000);
			Context.LabouchereInitialSequence = seqEntries.Select((BetAmountSettingDto b) => (int)b.Amount.Value).ToList();
			int maxUnit = ((Context.LabouchereInitialSequence.Count > 0) ? (Context.LabouchereInitialSequence.Max() * 2) : 10);
			Context.BetAmounts = Enumerable.Range(1, 50).ToDictionary((int step) => step, (int step) => Context.LabouchereMultiplier * (decimal)maxUnit);
			Context.StepBettingMethods.Clear();
			FileLogger.Log($"[AutoBetting][라부셰르] 로드: 수열=[{string.Join(",", Context.LabouchereInitialSequence)}], 배수={Context.LabouchereMultiplier:N0}");
			break;
		}
		case 4:
		{
			BetAmountSettingDto baseEntry = amounts.FirstOrDefault(delegate(BetAmountSettingDto b)
			{
				if (b.Number == 1 && b.Amount.HasValue)
				{
					decimal? amount = b.Amount;
					return (amount.GetValueOrDefault() > 0m) & amount.HasValue;
				}
				return false;
			});
			if (baseEntry != null)
			{
				Context.BetAmounts = Enumerable.Range(1, 50).ToDictionary((int step) => step, (int step) => baseEntry.Amount.Value * (decimal)step);
			}
			else
			{
				Context.BetAmounts = new Dictionary<int, decimal>();
			}
			Context.StepBettingMethods.Clear();
			break;
		}
		default:
			if (AutoBettingContext.IsMatrixType(betAmountType))
			{
				int matrixRowCount = AutoBettingContext.MatrixStepsPerRow.Length;
				List<MatrixCellDto> cells = await GetApi().GetMatrixCellsAsync(betAmountType);
				Context.MatrixCells = new decimal[matrixRowCount][];
				Context.MatrixDefinedMaxCols = new int[matrixRowCount];
				for (int r = 0; r < matrixRowCount; r++)
				{
					Context.MatrixCells[r] = new decimal[AutoBettingContext.MatrixStepsPerRow[r]];
				}
				foreach (MatrixCellDto c in cells.Where((MatrixCellDto matrixCellDto) => matrixCellDto.RowIndex >= 1 && matrixCellDto.RowIndex <= matrixRowCount && matrixCellDto.ColIndex >= 1 && matrixCellDto.Amount >= 0m))
				{
					int maxColForRow = AutoBettingContext.MatrixStepsPerRow[c.RowIndex - 1];
					if (c.ColIndex <= maxColForRow)
					{
						Context.MatrixCells[c.RowIndex - 1][c.ColIndex - 1] = c.Amount;
						if (c.ColIndex > Context.MatrixDefinedMaxCols[c.RowIndex - 1])
						{
							Context.MatrixDefinedMaxCols[c.RowIndex - 1] = c.ColIndex;
						}
					}
				}
				Context.BetAmounts = new Dictionary<int, decimal>();
				for (int col = 1; col <= Context.MatrixDefinedMaxCols[0]; col++)
				{
					Context.BetAmounts[col] = Context.MatrixCells[0][col - 1];
				}
				Context.StepBettingMethods.Clear();
				Context.MatrixRowRules = new Dictionary<int, (int, int)>();
				Context.MatrixRowWinModes = new Dictionary<int, int>();
				Context.MatrixRowRestMinutes = new Dictionary<int, int>();
				List<MatrixRowRuleDto> validRowRules = (await GetApi().GetMatrixRowRulesAsync(betAmountType)).Where((MatrixRowRuleDto matrixRowRuleDto) => matrixRowRuleDto.RowIndex >= 1 && matrixRowRuleDto.RowIndex <= matrixRowCount).ToList();
				if (AutoBettingContext.UsesMatrixRowRules(betAmountType))
				{
					foreach (MatrixRowRuleDto rule in validRowRules.Where((MatrixRowRuleDto matrixRowRuleDto) => matrixRowRuleDto.WinCount >= 1 && matrixRowRuleDto.TargetRow >= 1 && matrixRowRuleDto.TargetRow <= matrixRowCount))
					{
						Context.MatrixRowRules[rule.RowIndex] = (rule.WinCount, rule.TargetRow);
					}
				}
				if (betAmountType == 13)
				{
					foreach (MatrixRowRuleDto rule2 in validRowRules.Where((MatrixRowRuleDto matrixRowRuleDto) => matrixRowRuleDto.WinMode == 1))
					{
						Context.MatrixRowWinModes[rule2.RowIndex] = 1;
					}
				}
				Context.StairMatrixStepBackOnWin = betAmountType == 14 && validRowRules.Any((MatrixRowRuleDto matrixRowRuleDto) => matrixRowRuleDto.RowIndex == 1 && matrixRowRuleDto.WinMode == 1);
				if (AutoBettingContext.SupportsMatrixRest(betAmountType))
				{
					foreach (MatrixRowRuleDto rule3 in validRowRules.Where((MatrixRowRuleDto matrixRowRuleDto) => matrixRowRuleDto.RestMinutes >= 1))
					{
						Context.MatrixRowRestMinutes[rule3.RowIndex] = Math.Min(rule3.RestMinutes, 120);
					}
				}
				Context.MatrixFirstRowCapAmount = (from matrixRowRuleDto in validRowRules
					where matrixRowRuleDto.RowIndex == 1 && matrixRowRuleDto.FirstRowCapAmount > 0m
					select matrixRowRuleDto.FirstRowCapAmount).FirstOrDefault();
				int loadedCount = Context.MatrixDefinedMaxCols.Sum();
				string perRowSummary = string.Join(", ", from x in Context.MatrixDefinedMaxCols.Select((int max, int i) => new
					{
						Row = i + 1,
						Max = max
					})
					where x.Max > 0
					select $"{x.Row}차={x.Max}");
				string ruleSummary = string.Join(", ", from kv in Context.MatrixRowRules
					orderby kv.Key
					select $"{kv.Key}차:{kv.Value.WinCount}승→{kv.Value.TargetRow}차");
				string restSummary = ((Context.MatrixRowRestMinutes.Count > 0) ? (" / 차별휴식 " + string.Join(", ", from kv in Context.MatrixRowRestMinutes
					orderby kv.Key
					select $"{kv.Key}차:{kv.Value}분")) : string.Empty);
				string modeSummary = betAmountType switch
				{
					14 => " / 단계방식 " + (Context.StairMatrixStepBackOnWin ? "달랑(한 단계 후퇴)" : "마틴(1단계 복귀)"), 
					13 => " / 차별방식 " + string.Join(", ", from num2 in Enumerable.Range(1, matrixRowCount)
						where Context.MatrixDefinedMaxCols[num2 - 1] > 0
						select $"{num2}차:{((Context.MatrixRowWinModes.GetValueOrDefault(num2) == 1) ? "달랑" : "마틴")}"), 
					_ => string.Empty, 
				};
				string capAmountSummary = ((Context.MatrixFirstRowCapAmount > 0m) ? $" / 1차 상한금액 {Context.MatrixFirstRowCapAmount:N0}원(기준 잔액 {Context.MatrixFirstRowCapBaseBalance:N0}원 + 상한 초과면 2차 미진입·1차 복귀, 1차 실패마다 기준 갱신)" : string.Empty);
				FileLogger.Log($"[AutoBetting][{GetBetAmountTypeName(betAmountType)}] 로드: 단계 {loadedCount}개 ({perRowSummary}) — 0원 셀은 관망 단계로 포함 / 이동규칙 {Context.MatrixRowRules.Count}개 ({ruleSummary}){modeSummary}{restSummary}{capAmountSummary}");
				break;
			}
			if (betAmountType == 10)
			{
				BetAmountSettingDto maxStepEntry = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 1);
				AutoBettingContext context = Context;
				int dalmenzingMaxStep;
				switch (maxStepEntry?.BettingMethod)
				{
				default:
					dalmenzingMaxStep = 0;
					break;
				case 1:
				case 2:
				case 3:
				case 4:
				case 5:
				case 6:
				case 7:
				case 8:
				case 9:
				case 10:
				case 11:
				case 12:
				case 13:
				case 14:
				case 15:
				case 16:
				case 17:
				case 18:
				case 19:
				case 20:
				case 21:
				case 22:
				case 23:
				case 24:
				case 25:
				case 26:
				case 27:
				case 28:
				case 29:
				case 30:
				case 31:
				case 32:
				case 33:
				case 34:
				case 35:
				case 36:
				case 37:
				case 38:
				case 39:
				case 40:
				case 41:
				case 42:
				case 43:
				case 44:
				case 45:
				case 46:
				case 47:
				case 48:
				case 49:
				case 50:
					dalmenzingMaxStep = maxStepEntry.BettingMethod.Value;
					break;
				}
				context.DalmenzingMaxStep = dalmenzingMaxStep;
				BetAmountSettingDto methodEntry = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 2);
				Context.DalmenzingRecoveryMethod = (((object)methodEntry != null && methodEntry.BettingMethod == 1) ? 1 : 0);
				BetAmountSettingDto dalembertBaseEntry = (await GetBetAmountsForStartAsync(4)).FirstOrDefault(delegate(BetAmountSettingDto b)
				{
					if (b.Number == 1 && b.Amount.HasValue)
					{
						decimal? amount = b.Amount;
						return (amount.GetValueOrDefault() > 0m) & amount.HasValue;
					}
					return false;
				});
				Context.DalmenzingDAlembertBaseAmount = (dalembertBaseEntry?.Amount).GetValueOrDefault();
				List<BetAmountSettingDto> martinAmounts = await GetBetAmountsForStartAsync();
				Context.DalmenzingMartinAmounts = (from b in martinAmounts
					where b.Amount.HasValue
					orderby b.Number
					select b).ToDictionary((BetAmountSettingDto b) => b.Number, (BetAmountSettingDto b) => b.Amount.Value);
				List<BetAmountSettingDto> hongKongAmounts = await GetBetAmountsForStartAsync(1);
				Context.DalmenzingHongKongAmounts = (from b in hongKongAmounts
					where b.Amount.HasValue
					orderby b.Number
					select b).ToDictionary((BetAmountSettingDto b) => b.Number, (BetAmountSettingDto b) => b.Amount.Value);
				ApplyHongKongWinTargetSteps(hongKongAmounts);
				Context.BetAmounts = ((Context.DalmenzingDAlembertBaseAmount > 0m) ? Enumerable.Range(1, 50).ToDictionary((int step) => step, (int step) => Context.DalmenzingDAlembertBaseAmount * (decimal)step) : new Dictionary<int, decimal>());
				Context.StepBettingMethods.Clear();
				string recoveryMethodName = ((Context.DalmenzingRecoveryMethod == 1) ? "홍콩크루즈" : "마틴");
				FileLogger.Log($"[AutoBetting][달랑멘징] 로드: 달랑기본금={Context.DalmenzingDAlembertBaseAmount:N0}, N단계={Context.DalmenzingMaxStep}, 복구방식={recoveryMethodName}, 마틴{Context.DalmenzingMartinAmounts.Count}단 / 홍콩{Context.DalmenzingHongKongAmounts.Count}단");
				break;
			}
			Context.BetAmounts = amounts.Where((BetAmountSettingDto b) => b.Amount.HasValue).ToDictionary((BetAmountSettingDto b) => b.Number, (BetAmountSettingDto b) => b.Amount.Value);
			if (betAmountType == 3)
			{
				Context.StepBettingMethods = amounts.Where((BetAmountSettingDto b) => b.Amount.HasValue).ToDictionary((BetAmountSettingDto b) => b.Number, (BetAmountSettingDto b) => b.BettingMethod.GetValueOrDefault());
				List<BetAmountSettingDto> martinEntries = await GetBetAmountsForStartAsync();
				List<BetAmountSettingDto> hongkongEntries = await GetBetAmountsForStartAsync(1);
				Context.MartinBetAmounts = martinEntries.Where((BetAmountSettingDto b) => b.Amount.HasValue).ToDictionary((BetAmountSettingDto b) => b.Number, (BetAmountSettingDto b) => b.Amount.Value);
				Context.HongKongBetAmounts = hongkongEntries.Where((BetAmountSettingDto b) => b.Amount.HasValue).ToDictionary((BetAmountSettingDto b) => b.Number, (BetAmountSettingDto b) => b.Amount.Value);
				ApplyHongKongWinTargetSteps(hongkongEntries);
				FileLogger.Log($"[AutoBetting][혼합] 방식별 베팅금 로드: 마틴 {Context.MartinBetAmounts.Count}단계 / 홍콩크루즈 {Context.HongKongBetAmounts.Count}단계");
			}
			else
			{
				Context.StepBettingMethods.Clear();
				Context.MartinBetAmounts.Clear();
				Context.HongKongBetAmounts.Clear();
				if (betAmountType == 1)
				{
					ApplyHongKongWinTargetSteps(amounts);
				}
			}
			break;
		}
		FileLogger.Log($"[AutoBetting] 베팅금 로드됨: {Context.BetAmounts.Count}단계 (타입={GetBetAmountTypeName(betAmountType)})", "Info", DbLogService.SessionCategory);
	}

	private void ApplyHongKongWinTargetSteps(IEnumerable<BetAmountSettingDto> hongKongEntries)
	{
		Context.HongKongWinTargetSteps = hongKongEntries.Where(delegate(BetAmountSettingDto b)
		{
			int number = b.Number;
			if (number >= 1 && number <= 50)
			{
				int? bettingMethod = b.BettingMethod;
				if (bettingMethod.HasValue)
				{
					number = bettingMethod.GetValueOrDefault();
					if (number >= 1)
					{
						return number <= 50;
					}
				}
				return false;
			}
			return false;
		}).ToDictionary((BetAmountSettingDto b) => b.Number, (BetAmountSettingDto b) => b.BettingMethod.Value);
		if (Context.HongKongWinTargetSteps.Count > 0)
		{
			string summary = string.Join(", ", from kv in Context.HongKongWinTargetSteps
				orderby kv.Key
				select $"{kv.Key}→{kv.Value}");
			FileLogger.Log($"[AutoBetting][홍콩] 2연승 이동 단계 규칙 {Context.HongKongWinTargetSteps.Count}개 로드: {summary}");
		}
	}

	private int GetHongKongWinTargetStep(int stepBefore, int maxStep)
	{
		if (!Context.HongKongWinTargetSteps.TryGetValue(stepBefore, out var target))
		{
			return 1;
		}
		if (target < 1 || target > maxStep)
		{
			FileLogger.Log($"[AutoBetting][홍콩] {stepBefore}단계 이동 규칙 {target}이 금액표 범위(1~{maxStep})를 벗어나 1단계로 대체", "Warning", "AutoBetting");
			return 1;
		}
		return target;
	}

	private decimal GetBetAmountForTable(string tableId, int step)
	{
		if (Context.SelectedBetAmountType != 3)
		{
			return Context.BetAmounts.GetValueOrDefault(step, 0m);
		}
		return ((Context.TableIndexMap.GetValueOrDefault(tableId, 0) % 2 == 0) ? Context.MartinBetAmounts : Context.HongKongBetAmounts).GetValueOrDefault(step, 0m);
	}

	private bool HasBetAmountForTable(string tableId, int step)
	{
		if (Context.SelectedBetAmountType != 3)
		{
			return Context.BetAmounts.ContainsKey(step);
		}
		return ((Context.TableIndexMap.GetValueOrDefault(tableId, 0) % 2 == 0) ? Context.MartinBetAmounts : Context.HongKongBetAmounts).ContainsKey(step);
	}

	private async Task<bool> LoadStrategyChainAsync(string userId)
	{
		StrategyChainDto chainDto = await GetApi().GetActiveStrategyChainAsync();
		if (chainDto == null || chainDto.Steps.Count < 2)
		{
			RaiseStateChanged(isRunning: false, "활성 전략 체인이 없습니다. 체인설정에서 체인을 생성하고 활성화하세요.");
			return false;
		}
		Context.ChainSteps = (from s in chainDto.Steps
			orderby s.Order
			select new ChainStepDefinition
			{
				StrategyType = s.StrategyType,
				MaxStepTransition = s.MaxStepTransition,
				ConsecutiveLossTransition = s.ConsecutiveLossTransition,
				LossAmountTransition = s.LossAmountTransition,
				StrategyCompleteTransition = s.StrategyCompleteTransition
			}).ToList();
		Context.CurrentChainStepIndex = 0;
		Context.ChainCompletionAction = chainDto.CompletionAction;
		List<int> uniqueTypes = Context.ChainSteps.Select((ChainStepDefinition s) => s.StrategyType).Distinct().ToList();
		Context.AllChainBetAmounts = new Dictionary<int, Dictionary<int, decimal>>();
		Context.AllChainContexts = new Dictionary<int, StrategyContextSnapshot>();
		foreach (int strategyType in uniqueTypes)
		{
			var (betAmounts, snapshot) = BuildStrategyData(strategyType, await GetBetAmountsForStartAsync(strategyType));
			Context.AllChainBetAmounts[strategyType] = betAmounts;
			Context.AllChainContexts[strategyType] = snapshot;
		}
		int firstType = Context.ChainSteps[0].StrategyType;
		Context.SelectedBetAmountType = firstType;
		ApplyStrategyFromChain(firstType);
		FileLogger.Log($"[AutoBetting][체인] 로드 완료: '{chainDto.Name}', {Context.ChainSteps.Count}단계, 완료동작={((chainDto.CompletionAction == 1) ? "순환" : "정지")}, 첫 전략={GetBetAmountTypeName(firstType)}");
		if (Context.BetAmounts.Count == 0)
		{
			// 조용한 실패 제거: 체인 첫 전략의 베팅금이 없으면 명확히 알린다.
			RaiseStateChanged(isRunning: false, $"체인의 첫 전략({GetBetAmountTypeName(firstType)})에 베팅금이 설정되지 않았습니다. '베팅금 설정'에서 해당 전략의 금액을 입력한 뒤 다시 시작하세요.");
			return false;
		}
		return true;
	}

	private (Dictionary<int, decimal> BetAmounts, StrategyContextSnapshot Snapshot) BuildStrategyData(int strategyType, List<BetAmountSettingDto> amounts)
	{
		StrategyContextSnapshot snapshot = new StrategyContextSnapshot();
		Dictionary<int, decimal> betAmounts;
		switch (strategyType)
		{
		case 6:
		{
			BetAmountSettingDto baseEntry3 = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 1 && b.Amount > (decimal?)0m);
			snapshot.BaseAmount1326 = (baseEntry3?.Amount).GetValueOrDefault();
			int[] multipliers = new int[4] { 1, 3, 2, 6 };
			betAmounts = ((snapshot.BaseAmount1326 > 0m) ? multipliers.Select((int m, int i) => new
			{
				Step = i + 1,
				Amount = snapshot.BaseAmount1326 * (decimal)m
			}).ToDictionary(x => x.Step, x => x.Amount) : new Dictionary<int, decimal>());
			break;
		}
		case 7:
		{
			BetAmountSettingDto baseEntry2 = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 1 && b.Amount > (decimal?)0m);
			snapshot.OscarBaseAmount = (baseEntry2?.Amount).GetValueOrDefault();
			StrategyContextSnapshot strategyContextSnapshot2 = snapshot;
			int? num = baseEntry2?.BettingMethod;
			strategyContextSnapshot2.OscarTargetUnits = ((!num.HasValue || num.GetValueOrDefault() <= 0) ? 1 : baseEntry2.BettingMethod.Value);
			betAmounts = ((snapshot.OscarBaseAmount > 0m) ? Enumerable.Range(1, 50).ToDictionary((int step) => step, (int step) => snapshot.OscarBaseAmount * (decimal)step) : new Dictionary<int, decimal>());
			break;
		}
		case 8:
		{
			BetAmountSettingDto baseEntry4 = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 1 && b.Amount > (decimal?)0m);
			snapshot.FlatBetAmount = (baseEntry4?.Amount).GetValueOrDefault();
			betAmounts = ((snapshot.FlatBetAmount > 0m) ? new Dictionary<int, decimal> { { 1, snapshot.FlatBetAmount } } : new Dictionary<int, decimal>());
			break;
		}
		case 9:
		{
			List<BetAmountSettingDto> seqEntries = (from b in amounts
				where b.Amount > (decimal?)0m
				orderby b.Number
				select b).ToList();
			StrategyContextSnapshot strategyContextSnapshot = snapshot;
			int? num = seqEntries.FirstOrDefault()?.BettingMethod;
			strategyContextSnapshot.LabouchereMultiplier = ((num.HasValue && num.GetValueOrDefault() > 0) ? seqEntries.First().BettingMethod.Value : 1000);
			snapshot.LabouchereInitialSequence = seqEntries.Select((BetAmountSettingDto b) => (int)b.Amount.Value).ToList();
			int maxUnit = ((snapshot.LabouchereInitialSequence.Count > 0) ? (snapshot.LabouchereInitialSequence.Max() * 2) : 10);
			betAmounts = Enumerable.Range(1, 50).ToDictionary((int step) => step, (int step) => snapshot.LabouchereMultiplier * (decimal)maxUnit);
			break;
		}
		case 4:
		{
			BetAmountSettingDto baseEntry = amounts.FirstOrDefault((BetAmountSettingDto b) => b.Number == 1 && b.Amount > (decimal?)0m);
			snapshot.DAlembertBaseAmount = (baseEntry?.Amount).GetValueOrDefault();
			betAmounts = ((snapshot.DAlembertBaseAmount > 0m) ? Enumerable.Range(1, 50).ToDictionary((int step) => step, (int step) => snapshot.DAlembertBaseAmount * (decimal)step) : new Dictionary<int, decimal>());
			break;
		}
		default:
			betAmounts = (from b in amounts
				where b.Amount.HasValue
				orderby b.Number
				select b).ToDictionary((BetAmountSettingDto b) => b.Number, (BetAmountSettingDto b) => b.Amount.Value);
			if (strategyType == 3)
			{
				snapshot.StepBettingMethods = amounts.Where((BetAmountSettingDto b) => b.Amount.HasValue).ToDictionary((BetAmountSettingDto b) => b.Number, (BetAmountSettingDto b) => b.BettingMethod.GetValueOrDefault());
			}
			break;
		}
		return (BetAmounts: betAmounts, Snapshot: snapshot);
	}

	private void ApplyStrategyFromChain(int strategyType)
	{
		if (!Context.AllChainBetAmounts.TryGetValue(strategyType, out Dictionary<int, decimal> betAmounts))
		{
			Context.BetAmounts = new Dictionary<int, decimal>();
			return;
		}
		Context.BetAmounts = new Dictionary<int, decimal>(betAmounts);
		StrategyContextSnapshot snapshot = Context.AllChainContexts.GetValueOrDefault(strategyType) ?? new StrategyContextSnapshot();
		Context.BaseAmount1326 = snapshot.BaseAmount1326;
		Context.OscarBaseAmount = snapshot.OscarBaseAmount;
		Context.OscarTargetUnits = snapshot.OscarTargetUnits;
		Context.FlatBetAmount = snapshot.FlatBetAmount;
		Context.LabouchereMultiplier = snapshot.LabouchereMultiplier;
		Context.LabouchereInitialSequence = snapshot.LabouchereInitialSequence.ToList();
		Context.DAlembertBaseAmount = snapshot.DAlembertBaseAmount;
		Context.StepBettingMethods = new Dictionary<int, int>(snapshot.StepBettingMethods);
	}

	private bool EvaluateChainTransition(RoomBettingState roomState, bool isWin, int stepBefore)
	{
		ChainStepDefinition currentChainStep = Context.ChainSteps[Context.CurrentChainStepIndex];
		int? maxStepTransition = currentChainStep.MaxStepTransition;
		if (maxStepTransition.HasValue && maxStepTransition.GetValueOrDefault() > 0 && stepBefore >= currentChainStep.MaxStepTransition.Value && !isWin)
		{
			return true;
		}
		maxStepTransition = currentChainStep.ConsecutiveLossTransition;
		if (maxStepTransition.HasValue && maxStepTransition.GetValueOrDefault() > 0 && roomState.ConsecutiveLossCount >= currentChainStep.ConsecutiveLossTransition.Value)
		{
			return true;
		}
		decimal? lossAmountTransition = currentChainStep.LossAmountTransition;
		if (lossAmountTransition.HasValue && lossAmountTransition.GetValueOrDefault() > 0m && roomState.ChainStepLossAmount >= currentChainStep.LossAmountTransition.Value)
		{
			return true;
		}
		if (currentChainStep.StrategyCompleteTransition)
		{
			if (((currentChainStep.StrategyType == 9) & isWin) && roomState.LabouchereSequence.Count == 0)
			{
				return true;
			}
			if (((currentChainStep.StrategyType == 6) & isWin) && stepBefore >= 4)
			{
				return true;
			}
			if (((currentChainStep.StrategyType == 7) & isWin) && roomState.OscarSessionProfit >= (decimal)Context.OscarTargetUnits)
			{
				return true;
			}
		}
		return false;
	}

	private bool AdvanceChainStep(string triggerTableId)
	{
		int prevType = Context.SelectedBetAmountType;
		int nextIndex = Context.CurrentChainStepIndex + 1;
		if (nextIndex >= Context.ChainSteps.Count)
		{
			if (Context.ChainCompletionAction != 1)
			{
				FileLogger.Log("[AutoBetting][체인] ★ 체인 완료 → 자동 베팅 정지 ★ 트리거방=" + triggerTableId);
				return false;
			}
			nextIndex = 0;
			FileLogger.Log("[AutoBetting][체인] ★ 체인 순환 ★ 트리거방=" + triggerTableId);
		}
		Context.CurrentChainStepIndex = nextIndex;
		ChainStepDefinition nextChainStep = Context.ChainSteps[nextIndex];
		Context.SelectedBetAmountType = nextChainStep.StrategyType;
		SwitchToChainStrategy(nextChainStep.StrategyType, triggerTableId, prevType);
		return true;
	}

	private void SwitchToChainStrategy(int strategyType, string triggerTableId, int prevType)
	{
		FileLogger.Log($"[AutoBetting][체인] ★ 전략 전환 ★ {GetBetAmountTypeName(prevType)} → {GetBetAmountTypeName(strategyType)}, 트리거방={triggerTableId}, 체인단계={Context.CurrentChainStepIndex + 1}/{Context.ChainSteps.Count}");
		ApplyStrategyFromChain(strategyType);
		ResetAllRoomSteps();
		foreach (KeyValuePair<string, RoomBettingState> kvp in Context.RoomStates)
		{
			kvp.Value.ConsecutiveLossCount = 0;
			kvp.Value.ChainStepLossAmount = 0m;
		}
		FileLogger.Log($"[AutoBetting][체인] 전환 완료 - 새 전략={GetBetAmountTypeName(strategyType)}, 1단계금액={Context.BetAmounts.GetValueOrDefault(1):N0}");
	}

	private async Task LoadLimitSettingsAsync(string userId)
	{
		try
		{
			LimitSettingDto limits = await GetApi().GetLimitsAsync();
			Context.UpperLimitPercent = limits.UpperLimitPercent;
			Context.IsUpperLimitEnabled = limits.IsUpperLimitEnabled;
			Context.LowerLimitPercent = limits.LowerLimitPercent;
			Context.IsLowerLimitEnabled = limits.IsLowerLimitEnabled;
			Context.MaxActiveBettingRooms = limits.MaxActiveBettingRooms;
			Context.UpperLimitMode = limits.UpperLimitMode;
			Context.TargetProfitAmount = limits.TargetProfitAmount;
			Context.IsTargetCycleEnabled = limits.IsTargetCycleEnabled;
			Context.RestIntervalMinutes = limits.RestIntervalMinutes;
			Context.SessionCount = limits.SessionCount;
			string upperDesc = ((limits.UpperLimitMode == 1) ? $"{limits.TargetProfitAmount:N0}원" : $"{limits.UpperLimitPercent}%");
			FileLogger.Log($"[AutoBetting] 상/하한 로드: 상한={upperDesc} ({limits.IsUpperLimitEnabled}, 모드={((limits.UpperLimitMode == 1) ? "금액" : "퍼센트")}), 하한={limits.LowerLimitPercent}% ({limits.IsLowerLimitEnabled}), 활성방제한={limits.MaxActiveBettingRooms?.ToString() ?? "무제한"}, 사이클={(limits.IsTargetCycleEnabled ? $"휴식 {limits.RestIntervalMinutes}분 × {limits.SessionCount}회" : "미사용")}");
		}
		catch
		{
			Context.IsUpperLimitEnabled = false;
			Context.IsLowerLimitEnabled = false;
		}
	}

	private async Task LoadExceptionFormatSettingsAsync(string userId)
	{
		try
		{
			List<ExceptionFormatDto> formats = await GetApi().GetExceptionFormatsAsync();
			Context.ExceptionPatterns = _patternParser.ParseAllExceptionPatterns(formats.Select((ExceptionFormatDto f) => (Number: f.Number, Pattern: f.Pattern)));
			FileLogger.Log($"[AutoBetting] 예외 패턴 로드됨: {Context.ExceptionPatterns.Count}개");
		}
		catch
		{
			Context.ExceptionPatterns = new List<ExceptionPattern>();
		}
	}

	private async Task LoadRoomGateFormatSettingsAsync(string userId)
	{
		List<RoomGateFormatDto> formats = null;
		string lastError = null;
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			if (formats != null)
			{
				break;
			}
			try
			{
				formats = await GetApi().GetRoomGateFormatsAsync();
			}
			catch (Exception ex)
			{
				lastError = ex.Message;
				FileLogger.Log($"[방게이트] 서식 로드 실패 ({attempt}/{3}): {ex.Message}", "Warning", "RoomGate");
				if (attempt < 3)
				{
					await Task.Delay(700);
				}
			}
		}
		_roomGateFormatsLoaded = true;
		if (formats == null)
		{
			_roomGateLoadError = lastError;
			Context.RoomGateOnPatterns = new List<RoomGatePattern>();
			Context.RoomGateOffPatterns = new List<RoomGatePattern>();
			FileLogger.Log("[방게이트] 서식 로드 최종 실패 — 게이트 판정 없이 모든 방이 베팅 대상이 됩니다: " + lastError, Context.IsRoomGateEnabled ? "Error" : "Warning", "RoomGate");
			return;
		}
		_roomGateLoadError = null;
		Context.RoomGateOnPatterns = _patternParser.ParseAllRoomGatePatterns(from f in formats
			where f.GateType == 0
			select (Number: f.Number, IsOn: true, Pattern: f.Pattern));
		Context.RoomGateOffPatterns = _patternParser.ParseAllRoomGatePatterns(from f in formats
			where f.GateType == 1
			select (Number: f.Number, IsOn: false, Pattern: f.Pattern));
		string unavailable = RoomGateUnavailableReason;
		FileLogger.Log($"[방게이트] 서식 로드됨: On {Context.RoomGateOnPatterns.Count}개, Off {Context.RoomGateOffPatterns.Count}개 (기능={(Context.IsRoomGateEnabled ? "ON — 모든 방이 닫힌 채 시작" : "OFF — 판정하지 않음")})" + ((unavailable == null) ? "" : (" ※" + unavailable + " → 게이트 판정 없이 모든 방이 베팅 대상입니다")), (unavailable == null) ? "Info" : "Error", "RoomGate");
	}

	private void RaiseStateChanged(bool isRunning, string message, string? stopReason = null)
	{
		StateChanged?.Invoke(this, new AutoBettingStateChangedEventArgs
		{
			IsRunning = isRunning,
			Message = message,
			StopReason = stopReason
		});
	}
}
