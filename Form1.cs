using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Media;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;
using Syncfusion.Windows.Forms.Tools;
using Syncfusion.WinForms.Controls;
using Syncfusion.WinForms.DataGrid;

namespace PragmaticBot;

public partial class Form1 : Form
{
	private readonly PragmaticWebSocketServer _wsServer;

	private readonly PragmaticMessageParser _messageParser;

	private readonly PragmaticBettingService _bettingService;

	private readonly PragmaticAutoBettingService _autoBettingService;

	private readonly string _currentUserId;

	private readonly string _currentUserName;

	private List<GameDto> _baccaratGames = new List<GameDto>();

	private HashSet<int> _selectedGameIds = new HashSet<int>();

	private bool _isLoadingGames;

	private int _startAmountCaptured;

	/// <summary>
	/// False while 시작 금액 is only the number the page happened to be showing. The wallet API is the
	/// authority; a page-scraped figure gets replaced as soon as a real one arrives.
	/// </summary>
	private bool _startAmountAuthoritative;

	/// <summary>When the wallet API last answered. Ticks, UTC; 0 = never.</summary>
	private long _lastAuthoritativeBalanceTicksUtc;

	/// <summary>
	/// Set once the game socket has ever pushed an authoritative balance (Evolution's balanceUpdated —
	/// PP never fires it). On an Evolution site the only trustworthy figure is that socket value: the
	/// page DOM shows the portal's wallet (e.g. a bogus 1,000,000) which has nothing to do with the
	/// live-casino balance, so once the socket has spoken we stop letting any scraped/parsed number
	/// seed or override the balance. Leaving PP untouched, which relies on the DOM/WS fallback.
	/// </summary>
	private volatile bool _socketBalanceAuthoritativeSeen;

	/// <summary>
	/// How long a wallet reading keeps outranking the page. The wallet poll runs every few seconds,
	/// so anything inside this window means the authoritative source is alive and the number rendered
	/// on the page — which can lag the wallet by a long way — must not overwrite it.
	/// </summary>
	private const int AuthoritativeBalanceTrustSeconds = 60;

	private bool IsAuthoritativeBalanceFresh()
	{
		long ticks = Volatile.Read(in _lastAuthoritativeBalanceTicksUtc);
		return ticks > 0L
			&& (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds < AuthoritativeBalanceTrustSeconds;
	}

	private decimal _startAmount;

	private readonly object _balanceUiLock = new object();

	private decimal _lastDomBalance;

	private decimal _lastHttpBalance;

	private const decimal BalanceSurgeGuardRatio = 10m;

	private readonly ConcurrentDictionary<string, byte> _appliedWinRounds = new ConcurrentDictionary<string, byte>();

	private readonly ConcurrentQueue<string> _appliedWinOrder = new ConcurrentQueue<string>();

	private const int AppliedWinRoundCapacity = 2000;

	private volatile bool _isDomBalanceActive;

	private readonly ConcurrentDictionary<string, TableState> _tableStates = new ConcurrentDictionary<string, TableState>();

	private readonly ConcurrentDictionary<string, List<GameResult>> _gameHistories = new ConcurrentDictionary<string, List<GameResult>>();

	private volatile HashSet<string> _selectedTableIdsCache = new HashSet<string>();

	private const int MaxHistoryPerTable = 200;

	private System.Windows.Forms.Timer? _elapsedTimer;

	private DateTime _timerStartTime;

	private TimeSpan _pausedElapsed;

	private System.Windows.Forms.Timer? _balanceSyncTimer;

	private const int BalanceSyncIntervalMs = 8000;

	private CancellationTokenSource? _pauseCountdownCts;

	private readonly SessionMonitorService _sessionMonitor;

	private readonly GameCatalogSyncService _gameCatalogSync = new GameCatalogSyncService();

	private readonly PragmaticChromeDevToolsService _cdpService;

	private readonly PragmaticSiteSessionManager _siteManager;

	private List<BettingSite> _multiSites = DomainSettingService.GetDefaultSites();

	private volatile int _splitCount = 1;

	private const int SplitDisabled = 1;

	private decimal _lastMasterBalance;

	private decimal _lastWalletBalance;

	private bool _forceClose;

	private bool _shutdownCompleted;

	private decimal _startAmountMaster;

	private long _cdpMessageReceivedCount;

	private int _receivedCount;

	private int _savedCount;

	private System.Windows.Forms.Timer? _noticePollingTimer;

	private System.Windows.Forms.Timer? _noticeMarqueeTimer;

	private long _lastNoticeId;

	private int _marqueeOffset;

	private SoundPlayer? _alarmPlayer;

	private readonly ConcurrentDictionary<string, Panel> _gameIdToPanelMap = new ConcurrentDictionary<string, Panel>();

	private Label? _lblRoomSwitchSummary;

	private static readonly Color RoomPanelOnColor = Color.FromArgb(22, 33, 62);

	private static readonly Color RoomPanelOffColor = Color.FromArgb(40, 40, 44);

	private static readonly Color RoomPanelGateClosedColor = Color.FromArgb(30, 30, 52);

	private static readonly Color RoomPanelGateStaleColor = Color.FromArgb(58, 28, 34);

	private const int RoomGateWatchdogIntervalTicks = 60;

	private int _roomGateWatchdogTicks;

	private static readonly Color OptionButtonNormalColor = Color.FromArgb(15, 52, 96);

	private static readonly Color OptionButtonWarningColor = Color.FromArgb(180, 60, 20);

	private readonly ConcurrentDictionary<string, int> _lastShoeTotalGames = new ConcurrentDictionary<string, int>();

	private readonly Dictionary<string, int> _lastAppliedStepVersion = new Dictionary<string, int>();

	/// <summary>Rows behind sfDataGrid1 — newest first, capped so a long session cannot grow unbounded.</summary>
	private readonly BindingList<BetHistoryRow> _betHistoryRows = new BindingList<BetHistoryRow>();

	private const int MaxBetHistoryRows = 500;

	private readonly Dictionary<string, int> _roomBetStep = new Dictionary<string, int>();

	private readonly Dictionary<string, (int Row, int Col)> _roomBetMatrix = new Dictionary<string, (int, int)>();

	private int _sessionPeakStep;

	private (int Row, int Col) _sessionPeakMatrix;

	private readonly ConcurrentDictionary<string, string> _ppTableIdToMapKey = new ConcurrentDictionary<string, string>();

	private readonly ConcurrentDictionary<string, int> _unmappedGameCodes = new ConcurrentDictionary<string, int>();

	private readonly ConcurrentDictionary<string, GameDto> _allUnmappedGames = new ConcurrentDictionary<string, GameDto>();

	private readonly ConcurrentDictionary<string, byte> _unmappedPpTableIds = new ConcurrentDictionary<string, byte>();

	private readonly List<(int Row, int Col)> _matrixAlarmCells = new List<(int, int)>();

	private BotOptionState _options = new BotOptionState();

	private readonly List<string> _betStepItems = new List<string>();

	private readonly List<string> _alarmStepItems = new List<string>();

	private static readonly string[] ShoeControlThresholds = new string[4] { "35", "40", "45", "50" };

	/// <summary>
	/// Every progression the engine implements. The list had been trimmed to five entries even though
	/// PragmaticAutoBettingService handles all fifteen and BetAmountSettingForm can edit all fifteen,
	/// which left ten working strategies unreachable. Names match GetBetAmountTypeName.
	/// </summary>
	private static readonly (string Name, int Type)[] BetAmountOptions = new(string, int)[16]
	{
		("마틴", 0),
		("홍콩크루즈", 1),
		("피보나치", 2),
		("혼합(마틴+홍콩)", 3),
		("달랑베르", 4),
		("마틴+달랑 혼합", 5),
		("1-3-2-6", 6),
		("오스카 그라인드", 7),
		("플랫 베팅", 8),
		("라부셰르", 9),
		("달랑멘징", 10),
		("행렬", 11),
		("행렬달랑베르", 12),
		("스위칭행렬(마틴+달랑)", 13),
		("달랑행렬", 14),
		("업치기", 15)
	};

	private IContainer components;

	private bool IsSplitEnabled => _splitCount > 1;

	public Form1(string userId, string userName)
	{
		InitializeComponent();
		Icon = AppIcon.Value;
		_currentUserId = userId;
		_currentUserName = userName;
		_wsServer = new PragmaticWebSocketServer();
		_messageParser = new PragmaticMessageParser();
		_cdpService = new PragmaticChromeDevToolsService();
		_bettingService = new PragmaticBettingService(_wsServer, _cdpService);
		_bettingService.SubscribeToParser(_messageParser);
		_autoBettingService = new PragmaticAutoBettingService(_bettingService);
		_autoBettingService.SetDataReferences(_tableStates, _gameHistories);
		_autoBettingService.SetTableKeyResolver(ResolveTableKey);
		_sessionMonitor = new SessionMonitorService();
		_siteManager = new PragmaticSiteSessionManager(ResolveTableKey);
		ConfigureMultiSites();
		Text = "KingTigerBet (" + _currentUserName + ")";
		lblUser.Text = _currentUserName;
	}

	private async void Form1_Load(object? sender, EventArgs e)
	{
		InitializeBetHistoryGrid();
		// room panels follow the window width instead of scrolling sideways
		panelPlayRoomContainer.AutoScroll = false;
		panelPlayRoomContainer.HorizontalScroll.Enabled = false;
		panelPlayRoomContainer.HorizontalScroll.Visible = false;
		panelPlayRoomContainer.AutoScroll = true;
		panelPlayRoomContainer.SizeChanged -= PanelPlayRoomContainer_SizeChanged;
		panelPlayRoomContainer.SizeChanged += PanelPlayRoomContainer_SizeChanged;
		string savedPpUserId = PpUserIdStore.Load(_currentUserId);
		if (!string.IsNullOrEmpty(savedPpUserId))
		{
			_autoBettingService.Context.PpUserId = savedPpUserId;
			FileLogger.Log("[Form1] PP UserId 복원: " + savedPpUserId);
		}
		_gameCatalogSync.CatalogChanged -= GameCatalogSync_CatalogChanged;
		_gameCatalogSync.CatalogChanged += GameCatalogSync_CatalogChanged;
		_gameCatalogSync.Start();
		_wsServer.ConnectionStateChanged -= WsServer_ConnectionStateChanged;
		_wsServer.WebSocketMessageCaptured -= WsServer_MessageCaptured;
		_wsServer.GameMessageReceived -= WsServer_GameMessageReceived;
		_wsServer.ConnectionStateChanged += WsServer_ConnectionStateChanged;
		_wsServer.WebSocketMessageCaptured += WsServer_MessageCaptured;
		_wsServer.GameMessageReceived += WsServer_GameMessageReceived;
		_wsServer.BalanceReceived -= WsServer_BalanceReceived;
		_wsServer.BalanceReceived += WsServer_BalanceReceived;
		_wsServer.HttpBalanceReceived -= WsServer_HttpBalanceReceived;
		_wsServer.HttpBalanceReceived += WsServer_HttpBalanceReceived;
		_wsServer.WsMessageBalanceReceived -= WsServer_WsMessageBalanceReceived;
		_wsServer.WsMessageBalanceReceived += WsServer_WsMessageBalanceReceived;
		_wsServer.BalanceSelectorDetected -= WsServer_BalanceSelectorDetected;
		_wsServer.BalanceSelectorDetected += WsServer_BalanceSelectorDetected;
		_wsServer.DomainMismatchDetected -= WsServer_DomainMismatchDetected;
		_wsServer.DomainMismatchDetected += WsServer_DomainMismatchDetected;
		_cdpService.WebSocketMessageReceived -= CdpService_WebSocketMessageReceived;
		_cdpService.ConnectionStateChanged -= CdpService_ConnectionStateChanged;
		_cdpService.ErrorOccurred -= CdpService_ErrorOccurred;
		_cdpService.BalanceReceived -= CdpService_BalanceReceived;
		_cdpService.WebSocketMessageReceived += CdpService_WebSocketMessageReceived;
		_cdpService.ConnectionStateChanged += CdpService_ConnectionStateChanged;
		_cdpService.ErrorOccurred += CdpService_ErrorOccurred;
		_cdpService.BalanceReceived += CdpService_BalanceReceived;
		_cdpService.BalanceSelectorDetected -= CdpService_BalanceSelectorDetected;
		_cdpService.BalanceSelectorDetected += CdpService_BalanceSelectorDetected;
		_cdpService.HttpBalanceReceived -= CdpService_HttpBalanceReceived;
		_cdpService.HttpBalanceReceived += CdpService_HttpBalanceReceived;
		_cdpService.PpUserIdDetected -= CdpService_PpUserIdDetected;
		_cdpService.PpUserIdDetected += CdpService_PpUserIdDetected;
		chkSelectAll.CheckedChanged += ChkSelectAll_CheckedChanged;
		cmbBetAmountType.Items.AddRange(((IEnumerable<(string, int)>)BetAmountOptions).Select((Func<(string, int), object>)(((string Name, int Type) o) => o.Name)).ToArray());
		// 전략 체인: 실제 베팅금 타입이 아니라(=-1) 여러 전략을 순서대로 잇는 모드. 콤보 끝에 추가 항목으로 붙인다.
		cmbBetAmountType.Items.Add("전략 체인");
		cmbBetAmountType.SelectedIndex = 0;
		cmbBetAmountType.SelectedIndexChanged += CmbBetAmountType_SelectedIndexChanged;
		btnGameStart.Click += BtnGameStart_Click;
		btnPause.Click += BtnPause_Click;
		btnStopBet.Click += BtnStopBet_Click;
		SetupConsolidatedUi();
		_elapsedTimer = new System.Windows.Forms.Timer();
		_elapsedTimer.Interval = 1000;
		_elapsedTimer.Tick += ElapsedTimer_Tick;
		_balanceSyncTimer = new System.Windows.Forms.Timer();
		_balanceSyncTimer.Interval = 8000;
		_balanceSyncTimer.Tick += BalanceSyncTimer_Tick;
		_bettingService.BetAmountConfirmed -= BettingService_BetAmountConfirmed;
		_bettingService.BetAmountConfirmed += BettingService_BetAmountConfirmed;
		_bettingService.LateConfirmationReceived -= BettingService_LateConfirmationReceived;
		_bettingService.LateConfirmationReceived += BettingService_LateConfirmationReceived;
		_messageParser.GameStarted -= Parser_GameStarted;
		_messageParser.TimerReceived -= Parser_TimerReceived;
		_messageParser.BetsOpened -= Parser_BetsOpened;
		_messageParser.BetsClosingSoon -= Parser_BetsClosingSoon;
		_messageParser.BetsClosed -= Parser_BetsClosed;
		_messageParser.DealingStarted -= Parser_DealingStarted;
		_messageParser.GameResultReceived -= Parser_GameResultReceived;
		_messageParser.WinReceived -= Parser_WinReceived;
		_messageParser.ShoeSummaryReceived -= Parser_ShoeSummaryReceived;
		_messageParser.CardDealt -= Parser_CardDealt;
		_messageParser.CardIncremented -= Parser_CardIncremented;
		_messageParser.SeatReceived -= Parser_SeatReceived;
		_messageParser.GameStarted += Parser_GameStarted;
		_messageParser.AuthoritativeBalanceReceived -= Parser_AuthoritativeBalanceReceived;
		_messageParser.AuthoritativeBalanceReceived += Parser_AuthoritativeBalanceReceived;
		_messageParser.TimerReceived += Parser_TimerReceived;
		_messageParser.BetsOpened += Parser_BetsOpened;
		_messageParser.BetsClosingSoon += Parser_BetsClosingSoon;
		_messageParser.BetsClosed += Parser_BetsClosed;
		_messageParser.DealingStarted += Parser_DealingStarted;
		_messageParser.GameResultReceived += Parser_GameResultReceived;
		_messageParser.WinReceived += Parser_WinReceived;
		_messageParser.ShoeSummaryReceived += Parser_ShoeSummaryReceived;
		_messageParser.CardDealt += Parser_CardDealt;
		_messageParser.CardIncremented += Parser_CardIncremented;
		_messageParser.SeatReceived += Parser_SeatReceived;
		_messageParser.StatisticReceived += Parser_StatisticReceived;
		_messageParser.TableConfigReceived += Parser_TableConfigReceived;
		_autoBettingService.StateChanged -= AutoBetting_StateChanged;
		_autoBettingService.BetExecuted -= AutoBetting_BetExecuted;
		_autoBettingService.ResultJudged -= AutoBetting_ResultJudged;
		_autoBettingService.PeakBalanceReset -= AutoBetting_PeakBalanceReset;
		_autoBettingService.ProfitCooldownStarted -= AutoBetting_ProfitCooldownStarted;
		_autoBettingService.ProfitCooldownEnded -= AutoBetting_ProfitCooldownEnded;
		_autoBettingService.InactivityRefreshStarted -= AutoBetting_InactivityRefreshStarted;
		_autoBettingService.RefreshCompleted -= AutoBetting_RefreshCompleted;
		_autoBettingService.PendingBetAmountChanged -= AutoBetting_PendingBetAmountChanged;
		_autoBettingService.KeepAliveBetExecuted -= AutoBetting_KeepAliveBetExecuted;
		_autoBettingService.RoomGateChanged -= AutoBetting_RoomGateChanged;
		_autoBettingService.StateChanged += AutoBetting_StateChanged;
		_autoBettingService.BetExecuted += AutoBetting_BetExecuted;
		_autoBettingService.ResultJudged += AutoBetting_ResultJudged;
		_autoBettingService.PeakBalanceReset += AutoBetting_PeakBalanceReset;
		_autoBettingService.ProfitCooldownStarted += AutoBetting_ProfitCooldownStarted;
		_autoBettingService.ProfitCooldownEnded += AutoBetting_ProfitCooldownEnded;
		_autoBettingService.InactivityRefreshStarted += AutoBetting_InactivityRefreshStarted;
		_autoBettingService.RefreshCompleted += AutoBetting_RefreshCompleted;
		_autoBettingService.PendingBetAmountChanged += AutoBetting_PendingBetAmountChanged;
		_autoBettingService.KeepAliveBetExecuted += AutoBetting_KeepAliveBetExecuted;
		_autoBettingService.RoomGateChanged += AutoBetting_RoomGateChanged;
		UpdateConnectionStatus(isConnected: false);
		_noticePollingTimer = new System.Windows.Forms.Timer
		{
			Interval = 10000
		};
		_noticePollingTimer.Tick += NoticePollingTimer_Tick;
		_noticePollingTimer.Start();
		_noticeMarqueeTimer = new System.Windows.Forms.Timer
		{
			Interval = 30
		};
		_noticeMarqueeTimer.Tick += NoticeMarqueeTimer_Tick;
		PollNoticeAsync();
		btnExit.Enabled = true;
		if (AuthService.Api != null)
		{
			DbLogService.Initialize(AuthService.Api);
		}
		UpdateConnectionStatus(isConnected: false);
		await LoadGameDomainsAsync();
		await LoadMultiSitesAsync();
		await LoadAndShowPlayRoomAsync();
		await LoadBetStepsAsync();
		await LoadAlarmStepsAsync();
		InitializeAlarmPlayer();
		LoadSavedOptions();
		_sessionMonitor.SessionInvalidated += SessionMonitor_SessionInvalidated;
		_sessionMonitor.Start();
		if (AuthService.Api?.JwtToken != null)
		{
			string hubUrl = AppConfiguration.ApiBaseUrl.TrimEnd('/') + "/hubs/session";
			_sessionMonitor.ConnectSignalRAsync(hubUrl, AuthService.Api.JwtToken);
		}
	}

	private int GetSelectedBetAmountType()
	{
		int idx = cmbBetAmountType?.SelectedIndex ?? 0;
		if (idx < 0 || idx >= BetAmountOptions.Length)
		{
			return BetAmountOptions[0].Type;
		}
		return BetAmountOptions[idx].Type;
	}

	private async Task LoadBetStepsAsync()
	{
		_ = 2;
		try
		{
			int betAmountType = GetSelectedBetAmountType();
			_betStepItems.Clear();
			if ((betAmountType == 4 || (uint)(betAmountType - 6) <= 3u) ? true : false)
			{
				int stepCount = betAmountType switch
				{
					6 => 4, 
					8 => 1, 
					_ => 50, 
				};
				for (int i = 1; i <= stepCount; i++)
				{
					_betStepItems.Add($"{i}단계");
				}
			}
			else
			{
				switch (betAmountType)
				{
				case 5:
					foreach (BetAmountSettingDto setting2 in await BetAmountSettingService.GetAllSettingsAsync(_currentUserId, 5))
					{
						_betStepItems.Add($"{setting2.Number}단계");
					}
					break;
				case 10:
				{
					int maxStep = (from s in (await BetAmountSettingService.GetAllSettingsAsync(_currentUserId, 10)).Where(delegate(BetAmountSettingDto s)
						{
							if (s.Number == 1)
							{
								int? bettingMethod = s.BettingMethod;
								if (bettingMethod.HasValue)
								{
									int valueOrDefault = bettingMethod.GetValueOrDefault();
									if (valueOrDefault > 0)
									{
										return valueOrDefault <= 50;
									}
								}
								return false;
							}
							return false;
						})
						select s.BettingMethod.Value).FirstOrDefault();
					if (maxStep > 0)
					{
						for (int i2 = 1; i2 <= maxStep; i2++)
						{
							_betStepItems.Add($"{i2}단계");
						}
					}
					break;
				}
				default:
					if (AutoBettingContext.IsMatrixType(betAmountType))
					{
						_betStepItems.Add("1단계");
						break;
					}
					foreach (BetAmountSettingDto setting in await BetAmountSettingService.GetAllSettingsAsync(_currentUserId, betAmountType))
					{
						_betStepItems.Add($"{setting.Number}단계");
					}
					break;
				}
			}
			_options.StepResetIndex = ((_betStepItems.Count <= 0) ? (-1) : 0);
			UpdateStepResetSetting();
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Form1] 베팅 단계 로드 실패: " + ex.Message, "Warning", "Init");
		}
	}

	private async void CmbBetAmountType_SelectedIndexChanged(object? sender, EventArgs e)
	{
		bool isChain = cmbBetAmountType.SelectedItem?.ToString() == "전략 체인";
		if (!isChain)
		{
			await LoadBetStepsAsync();
			await LoadAlarmStepsAsync();
		}
	}

	private async Task LoadAlarmStepsAsync()
	{
		_ = 2;
		try
		{
			int betAmountType = GetSelectedBetAmountType();
			int previousIndex = _options.StepAlarmIndex;
			_alarmStepItems.Clear();
			_matrixAlarmCells.Clear();
			if ((betAmountType == 4 || (uint)(betAmountType - 6) <= 3u) ? true : false)
			{
				int stepCount = betAmountType switch
				{
					6 => 4, 
					8 => 1, 
					_ => 50, 
				};
				for (int i = 1; i <= stepCount; i++)
				{
					_alarmStepItems.Add($"{i}단계");
				}
			}
			else if (betAmountType == 10)
			{
				int maxStep = (from s in (await BetAmountSettingService.GetAllSettingsAsync(_currentUserId, 10)).Where(delegate(BetAmountSettingDto s)
					{
						if (s.Number == 1)
						{
							int? bettingMethod = s.BettingMethod;
							if (bettingMethod.HasValue)
							{
								int valueOrDefault = bettingMethod.GetValueOrDefault();
								if (valueOrDefault > 0)
								{
									return valueOrDefault <= 50;
								}
							}
							return false;
						}
						return false;
					})
					select s.BettingMethod.Value).FirstOrDefault();
				if (maxStep > 0)
				{
					for (int i2 = 1; i2 <= maxStep; i2++)
					{
						_alarmStepItems.Add($"{i2}단계");
					}
				}
			}
			else if (AutoBettingContext.IsMatrixType(betAmountType))
			{
				ApiClient api = AuthService.Api;
				if (api != null)
				{
					foreach (var (row, col) in (from c in (await api.GetMatrixCellsAsync(betAmountType)).Where(delegate(MatrixCellDto c)
						{
							if (c.RowIndex >= 1 && c.RowIndex <= AutoBettingContext.MatrixStepsPerRow.Length)
							{
								int colIndex = c.ColIndex;
								if (colIndex >= 1 && colIndex <= 20)
								{
									return c.Amount > 0m;
								}
							}
							return false;
						})
						orderby c.RowIndex, c.ColIndex
						select (RowIndex: c.RowIndex, ColIndex: c.ColIndex)).ToList())
					{
						_matrixAlarmCells.Add((row, col));
						_alarmStepItems.Add($"{row}차 {col}단계");
					}
				}
			}
			else
			{
				foreach (BetAmountSettingDto setting in await BetAmountSettingService.GetAllSettingsAsync(_currentUserId, betAmountType))
				{
					_alarmStepItems.Add($"{setting.Number}단계");
				}
			}
			_options.StepAlarmIndex = ((_alarmStepItems.Count == 0) ? (-1) : ((previousIndex >= 0 && previousIndex < _alarmStepItems.Count) ? previousIndex : 0));
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Form1] 알람 단계 로드 실패: " + ex.Message, "Warning", "Init");
		}
	}

	private void InitializeAlarmPlayer()
	{
		try
		{
			string wavPath = Path.Combine(AppContext.BaseDirectory, "gamestart.wav");
			if (File.Exists(wavPath))
			{
				_alarmPlayer = new SoundPlayer(wavPath);
				_alarmPlayer.Load();
			}
			else
			{
				FileLogger.Log("[Form1] 알람 파일 없음: " + wavPath, "Warning", "Init");
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Form1] 알람 플레이어 초기화 실패: " + ex.Message, "Warning", "Init");
		}
	}

	private void ApplyRoomGateOption(bool enabled)
	{
		_autoBettingService.Context.IsRoomGateEnabled = enabled;
		if (enabled)
		{
			_autoBettingService.ResetAllRoomGates("방 On/Off 서식 기능 켜짐");
		}
		else
		{
			FileLogger.Log("[방게이트] 기능 꺼짐 — 게이트 판정 없이 모든 방이 베팅 대상입니다.", "Info", "RoomGate");
		}
		WarnIfRoomGateUnavailable();
		RefreshAllRoomVisuals();
	}

	private void WarnIfRoomGateUnavailable()
	{
		string reason = _autoBettingService.RoomGateUnavailableReason;
		Color color = ((reason == null) ? OptionButtonNormalColor : OptionButtonWarningColor);
		btnOptionSetting.BackColor = color;
		btnOptionSetting.Style.BackColor = color;
		if (reason != null)
		{
			MessageBox.Show(this, "방 On/Off 서식을 적용할 수 없습니다.\n\n" + reason + "\n\n게이트 판정 없이 모든 방이 베팅 대상이 됩니다.", "방 On/Off 서식", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
	}

	private void PlayAlarmRepeat(int count)
	{
		if (_alarmPlayer == null)
		{
			return;
		}
		Task.Run(delegate
		{
			for (int i = 0; i < count; i++)
			{
				_alarmPlayer.PlaySync();
			}
		});
	}

	private async Task LoadGameDomainsAsync()
	{
		_ = 1;
		try
		{
			List<string> domains = await DomainSettingService.GetUserDomainAsync(_currentUserId);
			_wsServer.SetGameDomains(domains);
			if (domains.Count > 0)
			{
				FileLogger.Log("[Form1] 사용자 게임 도메인 로드됨: " + string.Join(", ", domains));
			}
			else
			{
				FileLogger.Log("[Form1] 사용자 도메인 설정 없음 - 모든 WebSocket 캡처 모드");
			}
			List<string> balanceSelectors = await DomainSettingService.GetBalanceSelectorsAsync();
			_cdpService.SetCustomBalanceSelectors(balanceSelectors);
			if (balanceSelectors.Count > 0)
			{
				FileLogger.Log("[Form1] 사이트별 잔액 셀렉터 로드됨: " + string.Join(", ", balanceSelectors));
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Form1] 게임 도메인 로드 실패: " + ex.Message, "Warning", "Init");
		}
	}

	private void ConfigureMultiSites()
	{
		_siteManager.Configure(_multiSites);
		foreach (PragmaticSiteSession session in _siteManager.Sessions)
		{
			session.BalanceUpdated -= OnAuxSiteBalanceUpdated;
			session.BalanceUpdated += OnAuxSiteBalanceUpdated;
		}
		_autoBettingService.SetMultiSiteReferences(_siteManager.Sessions.ToDictionary((PragmaticSiteSession s) => s.Site.SiteSlot, (PragmaticSiteSession s) => s.Betting), _siteManager.Sessions.ToDictionary((PragmaticSiteSession s) => s.Site.SiteSlot, (PragmaticSiteSession s) => s.TableStates), ((IEnumerable<PragmaticSiteSession>)_siteManager.Sessions).ToDictionary((Func<PragmaticSiteSession, int>)((PragmaticSiteSession s) => s.Site.SiteSlot), (Func<PragmaticSiteSession, Func<decimal?>>)((PragmaticSiteSession s) => () => s.Balance)), ((IEnumerable<PragmaticSiteSession>)_siteManager.Sessions).ToDictionary((Func<PragmaticSiteSession, int>)((PragmaticSiteSession s) => s.Site.SiteSlot), (Func<PragmaticSiteSession, Func<string>>)((PragmaticSiteSession s) => () => s.PpUserId)), () => (!(_lastMasterBalance > 0m)) ? _autoBettingService.Context.AvailableBalance : _lastMasterBalance, () => _splitCount, _multiSites, RequestAuxRoomRecovery);
	}

	private async Task LoadMultiSitesAsync()
	{
		try
		{
			_multiSites = await DomainSettingService.GetBettingSitesAsync(_currentUserId);
			ConfigureMultiSites();
			FileLogger.Log("[Form1] 찢어베팅 사이트 로드됨: " + string.Join(", ", _multiSites.Select((BettingSite s) => $"슬롯{s.SiteSlot}({(string.IsNullOrEmpty(s.Domain) ? "미설정" : s.Domain)})")), "Info", "MultiSite");
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Form1] 찢어베팅 사이트 로드 실패: " + ex.Message, "Warning", "MultiSite");
		}
	}

	private async Task ApplySplitCountAsync(int n)
	{
		if (n <= 1)
		{
			_splitCount = 1;
			FileLogger.Log("[Form1] 찢어베팅 사용안함 — 브라우저 1대 단독 베팅", "Info", "MultiSite");
			return;
		}
		_splitCount = n;
		FileLogger.Log($"[Form1] 찢어베팅 분할 사이트 수 변경: {n}곳", "Info", "MultiSite");
		if (!_cdpService.IsConnected && !_wsServer.IsExtensionConnected)
		{
			return;
		}
		List<int> auxSlots = GetSlotsForSplitCount(n).Where(delegate(int s)
		{
			if (s > 0)
			{
				PragmaticSiteSession? session = _siteManager.GetSession(s);
				if (session == null)
				{
					return false;
				}
				return session.ActiveDebugPort == 0;
			}
			return false;
		}).ToList();
		if (auxSlots.Count == 0)
		{
			return;
		}
		try
		{
			btnOptionSetting.Enabled = false;
			lblStatus.Text = $"보조 사이트 {auxSlots.Count}곳 준비 중...";
			Progress<string> progress = new Progress<string>(delegate(string msg)
			{
				if (lblStatus.InvokeRequired)
				{
					lblStatus.Invoke(() => lblStatus.Text = msg);
				}
				else
				{
					lblStatus.Text = msg;
				}
			});
			await _siteManager.ConnectSitesAsync(auxSlots, ChromeShortcutHelper.CurrentDebugPort, progress);
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Form1] 보조 사이트 추가 연결 실패: " + ex.Message, "Warning", "MultiSite");
		}
		finally
		{
			btnOptionSetting.Enabled = true;
			UpdateConnectionStatus(_cdpService.IsConnected || _wsServer.IsExtensionConnected);
		}
	}

	private static List<int> GetSlotsForSplitCount(int n)
	{
		if (n <= 1)
		{
			int num = 1;
			List<int> list = new List<int>(num);
			CollectionsMarshal.SetCount(list, num);
			CollectionsMarshal.AsSpan(list)[0] = 0;
			return list;
		}
		int count = Math.Clamp(n, 1, 6);
		return Enumerable.Range(0, count).ToList();
	}

	private void RequestAuxRoomRecovery(int slot, string tableId)
	{
		PragmaticSiteSession session = _siteManager.GetSession(slot);
		if (session != null)
		{
			Task.Run(async () => await session.TryRecoverRoomAsync(tableId));
		}
	}

	private void OnAuxSiteBalanceUpdated(int slot, decimal balance)
	{
		if (IsSplitEnabled)
		{
			SyncCombinedBalance();
		}
		if (base.InvokeRequired)
		{
			BeginInvoke(UpdateBalanceUI);
		}
		else
		{
			UpdateBalanceUI();
		}
	}

	private void SyncCombinedBalance()
	{
		if ((_autoBettingService.IsRunning && _autoBettingService.Context.IsVirtualMode) || _lastMasterBalance <= 0m)
		{
			return;
		}
		bool useAux = IsSplitEnabled;
		decimal auxSum = (useAux ? _siteManager.Sessions.Sum((PragmaticSiteSession s) => s.Balance.GetValueOrDefault()) : 0m);
		decimal combined = _lastMasterBalance + auxSum;
		AutoBettingContext context = _autoBettingService.Context;
		decimal startCombined = ((_startAmountMaster <= 0m) ? 0m : (_startAmountMaster + (useAux ? _siteManager.Sessions.Sum((PragmaticSiteSession s) => s.StartBalance) : 0m)));
		if (startCombined > 0m && startCombined != _startAmount)
		{
			_startAmount = startCombined;
			context.StartBalance = startCombined;
			if (!_autoBettingService.IsRunning && context.PeakBalance < startCombined)
			{
				context.PeakBalance = startCombined;
			}
		}
		context.CurrentBalance = combined + context.PendingBetAmount;
		_lastWalletBalance = combined;
	}

	/// <summary>
	/// New tables reached the catalog, so refresh the in-memory game list. Rooms themselves are
	/// only drawn for games the user has ticked in 방 설정, so nothing on screen moves by itself.
	/// </summary>
	private void GameCatalogSync_CatalogChanged(object? sender, int added)
	{
		if (base.InvokeRequired)
		{
			Invoke(delegate
			{
				GameCatalogSync_CatalogChanged(sender, added);
			});
			return;
		}
		_ = RefreshGameCatalogAsync(added);
	}

	private async Task RefreshGameCatalogAsync(int added)
	{
		try
		{
			ApiClient? api = AuthService.Api;
			if (api == null)
			{
				return;
			}
			_baccaratGames = await api.GetBaccaratGamesAsync();
			FileLogger.Log($"[Form1] 테이블 {added}개가 카탈로그에 추가되어 게임 목록을 새로 읽었습니다 (총 {_baccaratGames.Count}개). 방 설정에서 선택하면 방이 표시됩니다.", "Info", "GameCatalog");
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Form1] 게임 목록 갱신 실패: " + ex.Message, "Warning", "GameCatalog");
		}
	}

	/// <summary>
	/// Returns the balance the CDP wallet poll has already delivered, waiting up to
	/// <paramref name="wait"/> for the next poll when nothing has arrived yet.
	/// </summary>
	/// <summary>
	/// Waits for a balance to start from, preferring the wallet API over anything scraped from the
	/// page.
	/// <para>
	/// The DOM number used to be consulted before the wallet value. That number is whatever the outer
	/// casino page last rendered, which goes stale as soon as the wallet moves — starting from it made
	/// 시작 금액, and therefore 이익, wrong for the whole session. The DOM is now only a last resort,
	/// and the caller is told which source it got.
	/// </para>
	/// </summary>
	private async Task<(decimal Balance, bool IsAuthoritative)> WaitForCdpBalanceAsync(TimeSpan wait)
	{
		DateTime deadline = DateTime.UtcNow + wait;
		while (true)
		{
			decimal authoritative;
			decimal domBalance;
			lock (_balanceUiLock)
			{
				authoritative = _lastHttpBalance;
				domBalance = _lastDomBalance;
			}
			if (authoritative <= 0m)
			{
				authoritative = _lastWalletBalance;
			}
			if (authoritative > 0m)
			{
				return (authoritative, true);
			}
			if (DateTime.UtcNow >= deadline)
			{
				return (domBalance, false);
			}
			await Task.Delay(500).ConfigureAwait(continueOnCapturedContext: true);
		}
	}

	private async Task LoadAndShowPlayRoomAsync()
	{
		_ = 2;
		try
		{
			ApiClient api = AuthService.Api;
			if (api != null)
			{
				_baccaratGames = await api.GetBaccaratGamesAsync();
				_selectedGameIds = (from s in await api.GetUserGameSelectionsAsync()
					where s.IsSelected
					select s.GameId).ToHashSet();
				if (_selectedGameIds.Count > 0)
				{
					await CreatePlayRoomPanelsAsync();
					panelRoomSettings.Visible = false;
					panelPlayRoomContainer.Visible = true;
					FileLogger.Log($"[Form1] 자동 로드: {_selectedGameIds.Count}개 게임방");
				}
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Form1] 플레이방 자동 로드 실패: " + ex.Message, "Warning", "Init");
		}
	}

	private void WsServer_GameMessageReceived(object? sender, string jsonData)
	{
		_messageParser.ParseMessage(jsonData);
	}

	private void Parser_GameStarted(object? sender, PpGameStartedArgs e)
	{
		string tableKey = ResolveTableKey(e.TableId);
		TableState orAdd = _tableStates.GetOrAdd(tableKey, (string _) => new TableState
		{
			TableId = tableKey,
			DisplayName = GetTableDisplayName(tableKey),
			Provider = GetTableProvider(tableKey)
		});
		orAdd.GameId = e.GameId;
		orAdd.GameStartTime = e.StartTime;
		orAdd.Phase = BettingPhase.GameStarted;
		orAdd.LastUpdated = DateTime.Now;
		orAdd.LiveScore.Reset();
		_autoBettingService.OnGameStarted(tableKey, e.GameId);
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				UpdateTableStateLabel(tableKey);
			});
		}
		else
		{
			UpdateTableStateLabel(tableKey);
		}
		if (!_gameIdToPanelMap.TryGetValue(tableKey, out Panel gamePanel))
		{
			return;
		}
		Control control = gamePanel.Controls.Find("lblBetType", searchAllChildren: false).FirstOrDefault();
		Label lblBetType = control as Label;
		if (lblBetType == null)
		{
			return;
		}
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				lblBetType.Text = "";
			});
		}
		else
		{
			lblBetType.Text = "";
		}
	}

	/// <summary>
	/// Balance pushed by the provider's own game socket. Evolution has no /api/wallet/balance, so this
	/// is the only authoritative figure available there — without it the start balance falls back to
	/// whatever a page happens to render.
	/// </summary>
	private void Parser_AuthoritativeBalanceReceived(object? sender, decimal balance)
	{
		if (balance > 0m)
		{
			// balanceUpdated 는 Evolution 게임 소켓에서만 온다 → 이 세션은 Evolution.
			// 이후 DOM/WS 로 긁힌 잔액(포털의 가짜 값)은 무시한다.
			_socketBalanceAuthoritativeSeen = true;
			ProcessBalanceUpdate(balance, isAuthoritative: true);
		}
	}

	private void Parser_TimerReceived(object? sender, PpTimerArgs e)
	{
		string tableKey = ResolveTableKey(e.TableId);
		if (_tableStates.TryGetValue(tableKey, out TableState state))
		{
			state.TimerSeconds = e.SecondsRemaining;
			state.TimerReceivedAt = DateTime.Now;
			state.LastUpdated = DateTime.Now;
		}
		_autoBettingService.OnTimerReceived(tableKey, e.SecondsRemaining);
	}

	private void Parser_BetsOpened(object? sender, PpTableEventArgs e)
	{
		string tableKey = ResolveTableKey(e.TableId);
		TableState state = _tableStates.GetOrAdd(tableKey, (string _) => new TableState
		{
			TableId = tableKey,
			DisplayName = GetTableDisplayName(tableKey),
			Provider = GetTableProvider(tableKey)
		});
		state.Phase = BettingPhase.BetsOpen;
		if (!string.IsNullOrEmpty(e.GameId))
		{
			state.GameId = e.GameId;
		}
		state.LastUpdated = DateTime.Now;
		_autoBettingService.OnBetsOpened(tableKey, e.GameId);
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				UpdateTableStateLabel(tableKey);
			});
		}
		else
		{
			UpdateTableStateLabel(tableKey);
		}
	}

	private void Parser_BetsClosingSoon(object? sender, PpTableEventArgs e)
	{
		string tableKey = ResolveTableKey(e.TableId);
		if (_tableStates.TryGetValue(tableKey, out TableState state))
		{
			state.Phase = BettingPhase.BetsClosingSoon;
			state.LastUpdated = DateTime.Now;
		}
		_autoBettingService.OnBetsClosingSoon(tableKey);
	}

	private void Parser_BetsClosed(object? sender, PpTableEventArgs e)
	{
		string tableKey = ResolveTableKey(e.TableId);
		if (_tableStates.TryGetValue(tableKey, out TableState state))
		{
			state.Phase = BettingPhase.BetsClosed;
			state.LastUpdated = DateTime.Now;
		}
		_autoBettingService.OnBetsClosed(tableKey);
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				UpdateTableStateLabel(tableKey);
			});
		}
		else
		{
			UpdateTableStateLabel(tableKey);
		}
	}

	private void Parser_DealingStarted(object? sender, PpTableEventArgs e)
	{
		string tableKey = ResolveTableKey(e.TableId);
		if (_tableStates.TryGetValue(tableKey, out TableState state))
		{
			state.Phase = BettingPhase.Dealing;
			state.LastUpdated = DateTime.Now;
		}
		_autoBettingService.OnDealingStarted(tableKey);
	}

	private void Parser_GameResultReceived(object? sender, PpGameResultArgs e)
	{
		string tableKey = ResolveTableKey(e.TableId);
		List<GameResult> history = _gameHistories.GetOrAdd(tableKey, (string _) => new List<GameResult>());
		lock (history)
		{
			if (history.Any((GameResult h) => h.GameId == e.GameId))
			{
				FileLogger.Log($"[Form1] 중복 게임 결과 스킵: table={e.TableId}, tableKey={tableKey}, gameId={e.GameId}");
				return;
			}
		}
		FileLogger.Log($"[Form1] 게임 결과: table={e.TableId}, tableKey={tableKey}, winner={e.Winner}, gameId={e.GameId}");
		_gameCatalogSync.Observe(e.TableId, null, null, e.Provider);
		GameResult gameResult = new GameResult
		{
			GameId = e.GameId,
			TableId = tableKey,
			Winner = e.Winner,
			Score = e.Score,
			IsNatural = e.IsNatural,
			PlayerPair = e.PlayerPair,
			BankerPair = e.BankerPair,
			ReceivedAt = DateTime.Now
		};
		int totalAfter;
		string tailAfter;
		lock (history)
		{
			history.Add(gameResult);
			if (history.Count > 200)
			{
				history.RemoveRange(0, history.Count - 200);
			}
			totalAfter = history.Count;
			tailAfter = string.Concat(from r in history.TakeLast(15)
				select (r.Winner.Length <= 0) ? '?' : char.ToUpper(r.Winner[0]));
		}
		FileLogger.Log($"[패턴진단] gameresult 추가: key={tableKey}, winner={e.Winner}, gameId={e.GameId}, history={totalAfter}건(말미15={tailAfter})", "Info", "PatternMatch");
		if (_tableStates.TryGetValue(tableKey, out TableState state))
		{
			state.Phase = BettingPhase.Result;
			state.LastUpdated = DateTime.Now;
		}
		_autoBettingService.OnGameResultReceived(tableKey, gameResult);
		if (!_gameIdToPanelMap.TryGetValue(tableKey, out Panel panel))
		{
			return;
		}
		PictureBox picRoad = panel.Controls.OfType<PictureBox>().FirstOrDefault((PictureBox p) => p.Name == "picRoad");
		if (picRoad == null)
		{
			return;
		}
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				picRoad.Invalidate();
			});
		}
		else
		{
			picRoad.Invalidate();
		}
	}

	private void Parser_WinReceived(object? sender, PpWinArgs e)
	{
		string tableKey = ResolveTableKey(e.TableId);
		_autoBettingService.OnWinReceived(tableKey, e.NetWinBalance, e.WinAmount);
		FileLogger.Log($"[Win] {tableKey}: nwb={e.NetWinBalance}, win={e.WinAmount}");
		ApplyWinToBalance(tableKey, e.GameId, e.WinAmount);
	}

	private void ApplyWinToBalance(string tableKey, string gameId, decimal payout)
	{
		if (_autoBettingService.Context.IsVirtualMode || !_autoBettingService.IsRunning)
		{
			return;
		}
		if (string.IsNullOrEmpty(gameId))
		{
			FileLogger.Log($"[WinBalance] {tableKey}: gameId 없음 → 즉시 반영 생략 (payout={payout:N0})", "Warning", "Balance");
			return;
		}
		decimal betAmount = _autoBettingService.GetBetAmountForRound(tableKey, gameId);
		if (betAmount <= 0m)
		{
			if (payout > 0m)
			{
				FileLogger.Log($"[WinBalance] {tableKey}: 우리 베팅 라운드 아님 → 반영 생략 (game={gameId}, payout={payout:N0})", "Warning", "Balance");
			}
			return;
		}
		decimal netWin = payout - betAmount;
		string roundKey = tableKey + ":" + gameId;
		if (!_appliedWinRounds.TryAdd(roundKey, 0))
		{
			FileLogger.Log($"[WinBalance] {tableKey}: 중복 win 무시 (game={gameId}, payout={payout:N0})");
			return;
		}
		_appliedWinOrder.Enqueue(roundKey);
		string oldKey;
		while (_appliedWinOrder.Count > 2000 && _appliedWinOrder.TryDequeue(out oldKey))
		{
			_appliedWinRounds.TryRemove(oldKey, out var _);
		}
		if (netWin == 0m)
		{
			FileLogger.Log($"[WinBalance] {tableKey}: 타이/변동없음 (game={gameId}, 베팅={betAmount:N0}, payout={payout:N0})");
			return;
		}
		decimal newBalance = _autoBettingService.Context.AddToCurrentBalance(netWin);
		FileLogger.Log($"[WinBalance] {tableKey}: 베팅={betAmount:N0}, payout={payout:N0} → 순손익={netWin:+#,##0;-#,##0} (game={gameId}) → CurrentBalance={newBalance:N0}");
		if (base.InvokeRequired)
		{
			BeginInvoke(UpdateBalanceUI);
		}
		else
		{
			UpdateBalanceUI();
		}
	}

	private void WsServer_BalanceReceived(object? sender, decimal balance)
	{
	}

	private void WsServer_HttpBalanceReceived(object? sender, decimal balance)
	{
		_cdpService.NotifyHttpBalanceReceived();
		ProcessBalanceUpdate(balance, isAuthoritative: true);
	}

	private void CdpService_BalanceReceived(object? sender, decimal balance)
	{
		if (_socketBalanceAuthoritativeSeen || _messageParser.HasEvolutionActivity)
		{
			// Evolution 세션: DOM 잔액은 포털의 가짜 값이므로 버린다(소켓 balanceUpdated 만 신뢰).
			return;
		}
		if (!(balance <= 0m))
		{
			ProcessBalanceUpdate(balance);
		}
	}

	private void CdpService_HttpBalanceReceived(object? sender, decimal balance)
	{
		if (!(balance <= 0m))
		{
			ProcessBalanceUpdate(balance, isAuthoritative: true);
		}
	}

	private void CdpService_BalanceSelectorDetected(object? sender, string selector)
	{
		Task.Run(async delegate
		{
			await DomainSettingService.SaveBalanceSelectorAsync(selector);
		});
	}

	private void WsServer_WsMessageBalanceReceived(object? sender, decimal balance)
	{
		if (_socketBalanceAuthoritativeSeen || _messageParser.HasEvolutionActivity)
		{
			// Evolution 세션: WS 파싱 잔액도 신뢰하지 않는다(소켓 balanceUpdated 만 권위).
			return;
		}
		if (!_isDomBalanceActive)
		{
			ProcessBalanceUpdate(balance);
		}
	}

	private void WsServer_BalanceSelectorDetected(object? sender, string selector)
	{
		FileLogger.Log("[DOM Diff] Extension에서 잔액 셀렉터 감지 → DB 저장: " + selector);
		Task.Run(async delegate
		{
			await DomainSettingService.SaveBalanceSelectorAsync(selector);
		});
	}

	private async void WsServer_DomainMismatchDetected(object? sender, (string CapturedUrl, List<string> RegisteredDomains) e)
	{
		string capturedHost;
		try
		{
			capturedHost = new Uri(e.CapturedUrl).Host;
		}
		catch
		{
			(capturedHost, _) = e;
		}
		string registeredText = string.Join(", ", e.RegisteredDomains);
		FileLogger.Log($"[Form1] WebSocket 도메인 불일치 감지 - 새 도메인: {capturedHost}, 기존: {registeredText}. DB 자동 업데이트 시작...", "Info", "Domain");
		try
		{
			await DomainSettingService.UpdateDomainAsync(e.CapturedUrl);
			FileLogger.Log("[Form1] 도메인 자동 업데이트 완료: " + registeredText + " → " + capturedHost, "Info", "Domain");
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Form1] 도메인 자동 업데이트 실패: " + ex.Message + " (베팅은 계속 진행)", "Warning", "Domain");
		}
	}

	private void ProcessBalanceUpdate(decimal balance, bool isAuthoritative = false)
	{
		if (balance <= 0m || (_autoBettingService.IsRunning && _autoBettingService.Context.IsVirtualMode && _startAmountCaptured != 0))
		{
			return;
		}
		if (isAuthoritative)
		{
			Interlocked.Exchange(ref _lastAuthoritativeBalanceTicksUtc, DateTime.UtcNow.Ticks);
		}
		else if (IsAuthoritativeBalanceFresh())
		{
			// the wallet answered moments ago; the figure painted on the page is the stale one
			decimal known;
			lock (_balanceUiLock)
			{
				known = _lastHttpBalance;
			}
			if (balance != known)
			{
				FileLogger.Log($"[Balance] 화면표시 잔액 무시: {balance:N0} (지갑 최신값 {known:N0} 유효)", "Info", "Balance");
			}
			return;
		}
		if (!_isDomBalanceActive)
		{
			_isDomBalanceActive = true;
			_wsServer.IsDomBalanceActive = true;
			FileLogger.Log("[Balance] DOM 기반 잔액 활성화 → WS 파싱 2순위 비활성");
		}
		decimal referenceBalance;
		lock (_balanceUiLock)
		{
			referenceBalance = ((_lastHttpBalance > 0m) ? _lastHttpBalance : ((!(_lastDomBalance > 0m)) ? _autoBettingService.Context.AvailableBalance : _lastDomBalance));
		}
		if (!isAuthoritative && referenceBalance > 0m && balance < referenceBalance * 0.2m)
		{
			FileLogger.Log($"[Balance] 급락 무시: {referenceBalance:N0} → {balance:N0} (80%+ 하락)", "Warning", "Balance");
			return;
		}
		if (!isAuthoritative && referenceBalance > 0m && balance > referenceBalance * 10m)
		{
			FileLogger.Log($"[Balance] 급등 무시: {referenceBalance:N0} → {balance:N0} ({10m}배+ 상승)", "Warning", "Balance");
			return;
		}
		if (isAuthoritative)
		{
			if (Interlocked.CompareExchange(ref _startAmountCaptured, 1, 0) == 0)
			{
				_startAmountMaster = balance;
				_startAmount = balance;
				_autoBettingService.Context.StartBalance = balance;
				_autoBettingService.Context.CurrentBalance = balance;
				if (_autoBettingService.Context.PeakBalance <= 0m)
				{
					_autoBettingService.Context.PeakBalance = balance;
				}
				lock (_balanceUiLock)
				{
					_lastDomBalance = balance;
					_lastMasterBalance = balance;
					_lastHttpBalance = balance;
				}
				_startAmountAuthoritative = true;
				FileLogger.Log($"[Balance] 시작금액 확정(권위값): {balance:N0}");
			}
			else if (!_startAmountAuthoritative && _autoBettingService.Context.TotalBetAmount <= 0m
				&& balance > 0m && balance != _startAmount)
			{
				// the session started on the page's rendered number because the wallet was unreachable.
				// Nothing has been staked yet, so the real figure can still replace it — otherwise
				// 시작 금액 and 이익 stay wrong for the rest of the run.
				decimal stale = _startAmount;
				_startAmountAuthoritative = true;
				_startAmountMaster = balance;
				_startAmount = balance;
				_autoBettingService.Context.StartBalance = balance;
				_autoBettingService.Context.CurrentBalance = balance;
				_autoBettingService.Context.PeakBalance = balance;
				lock (_balanceUiLock)
				{
					_lastDomBalance = balance;
					_lastMasterBalance = balance;
					_lastHttpBalance = balance;
				}
				FileLogger.Log($"[Balance] ★ 시작금액 보정: {stale:N0} (화면표시) → {balance:N0} (지갑 권위값)", "Warning", "Balance");
				SetBetStatusRemark($"시작금액 보정 {stale:N0} → {balance:N0}", Color.LimeGreen);
			}
		}
		else if (Volatile.Read(in _startAmountCaptured) == 0)
		{
			bool seeded = false;
			lock (_balanceUiLock)
			{
				if (_lastMasterBalance <= 0m)
				{
					_lastDomBalance = balance;
					_lastMasterBalance = balance;
					seeded = true;
				}
			}
			if (seeded)
			{
				_autoBettingService.Context.CurrentBalance = balance;
				FileLogger.Log($"[Balance] 비권위 잔액 잠정 반영: {balance:N0} (시작금액·보관금액 미확정 — 권위값 대기)", "Info", "Balance");
			}
		}
		bool acceptNonAuthoritative = !_autoBettingService.IsRunning;
		bool balanceChanged = false;
		lock (_balanceUiLock)
		{
			if (isAuthoritative)
			{
				_lastHttpBalance = balance;
				_lastDomBalance = balance;
				_lastMasterBalance = balance;
				balanceChanged = true;
			}
			else if (balance != _lastDomBalance)
			{
				_lastDomBalance = balance;
				if (acceptNonAuthoritative)
				{
					_lastMasterBalance = balance;
					balanceChanged = true;
				}
			}
		}
		if (!isAuthoritative && !acceptNonAuthoritative)
		{
			FileLogger.Log($"[Balance] 비권위 잔액 무시(운용 중): input={balance:N0}, 유지={_autoBettingService.Context.AvailableBalance:N0} (내부 원장 우선 — 지갑 권위값만 보정)", "Info", "Balance");
		}
		if (balanceChanged)
		{
			SyncCombinedBalance();
		}
		FileLogger.Log($"[Balance] 처리완료: input={balance:N0}, current={_autoBettingService.Context.CurrentBalance:N0}, pending={_autoBettingService.Context.PendingBetAmount:N0}, available={_autoBettingService.Context.AvailableBalance:N0}, peak={_autoBettingService.Context.PeakBalance:N0}, auth={isAuthoritative}, thread={Environment.CurrentManagedThreadId}");
		_autoBettingService.CheckBalanceAfterDomUpdate();
		if (base.InvokeRequired)
		{
			BeginInvoke(UpdateBalanceUI);
		}
		else
		{
			UpdateBalanceUI();
		}
	}

	private void UpdateBalanceUI()
	{
		if (_startAmount > 0m)
		{
			string startText = _startAmount.ToString("N0");
			if (lblStartAmountValue.Text != startText)
			{
				lblStartAmountValue.Text = startText;
			}
		}
		AutoBettingContext context = _autoBettingService.Context;
		decimal liveBalance = context.AvailableBalance;
		string afterText = ((context.IsVirtualMode || _lastWalletBalance <= 0m || _lastWalletBalance == liveBalance) ? liveBalance.ToString("N0") : $"{liveBalance:N0} (지갑확정 {_lastWalletBalance:N0})");
		if (lblAfterAmountValue.Text != afterText)
		{
			lblAfterAmountValue.Text = afterText;
		}
		// lblCurrentAmount* used to show PeakBalance under the label "선 현재금액"; it now carries the
		// session profit, which is what the operator actually wants next to the running balance.
		if (_startAmount > 0m)
		{
			decimal profit = liveBalance - _startAmount;
			string profitText = profit.ToString("+#,##0;-#,##0;0");
			if (lblCurrentAmountValue.Text != profitText)
			{
				lblCurrentAmountValue.Text = profitText;
			}
			Color profitColor = (profit > 0m) ? Color.LimeGreen : ((profit < 0m) ? Color.OrangeRed : Color.Gold);
			if (lblCurrentAmountValue.ForeColor != profitColor)
			{
				lblCurrentAmountValue.ForeColor = profitColor;
			}
		}
	}

	private void Parser_ShoeSummaryReceived(object? sender, PpShoeSummaryArgs e)
	{
		string tableKey = ResolveTableKey(e.TableId);
		bool hadPrev = _lastShoeTotalGames.TryGetValue(tableKey, out var prevTotal);
		_lastShoeTotalGames[tableKey] = e.TotalGames;
		bool shoeReset = hadPrev && e.TotalGames < prevTotal;
		if (_gameHistories.TryGetValue(tableKey, out List<GameResult> history))
		{
			lock (history)
			{
				if (!shoeReset && history.Count > e.TotalGames + 5)
				{
					shoeReset = true;
				}
				if (shoeReset)
				{
					history.Clear();
				}
			}
		}
		if (shoeReset)
		{
			FileLogger.Log($"[Form1] {tableKey}: 슈 교체 감지 → 히스토리 초기화 (TotalGames {(hadPrev ? prevTotal.ToString() : "?")} → {e.TotalGames})");
			_autoBettingService.ResetRoomGate(tableKey, "슈 교체");
			if (_tableStates.TryGetValue(tableKey, out TableState shoeTableState))
			{
				shoeTableState.ResetRoundTracking();
			}
			if (_autoBettingService.Context.RoomStates.TryGetValue(tableKey, out RoomBettingState roomState) && roomState.IsShoeControlBlocked)
			{
				roomState.IsShoeControlBlocked = false;
				FileLogger.Log("[Form1] 슈 컨트롤: " + tableKey + " 베팅 차단 해제 (슈 교체)");
			}
		}
	}

	private void Parser_CardDealt(object? sender, PpCardDealtArgs e)
	{
		string tableKey = ResolveTableKey(e.TableId);
		TableState state = _tableStates.GetOrAdd(tableKey, (string _) => new TableState
		{
			TableId = tableKey,
			DisplayName = GetTableDisplayName(tableKey),
			Provider = GetTableProvider(tableKey)
		});
		if (state.GameId != e.GameId && !string.IsNullOrEmpty(e.GameId))
		{
			state.LiveScore.Reset();
			state.GameId = e.GameId;
		}
		// Evolution repeats the whole hand on every cardDealt frame, so the hand is rebuilt from that
		// snapshot rather than appended to. Appending doubled every card whenever the same frame was
		// delivered twice — and it is: CDP reports one frame once per session attached to the
		// document, and several sessions end up attached to the same one.
		if (e.PlayerHand != null || e.BankerHand != null)
		{
			ReplaceHand(state.LiveScore.PlayerCards, e.PlayerHand, "Player");
			ReplaceHand(state.LiveScore.BankerCards, e.BankerHand, "Banker");
			// Trust the provider's totals only when they are real baccarat scores; otherwise work them
			// out from the cards.
			state.LiveScore.RecomputeScores();
			if (LiveScoreState.IsPlausibleScore(e.PlayerScore))
			{
				state.LiveScore.PlayerScore = e.PlayerScore;
			}
			if (LiveScoreState.IsPlausibleScore(e.BankerScore))
			{
				state.LiveScore.BankerScore = e.BankerScore;
			}
			state.LiveScore.TotalCardCount = state.LiveScore.PlayerCards.Count + state.LiveScore.BankerCards.Count;
		}
		else
		{
			CardInfo card = new CardInfo
			{
				CardCode = e.CardCode,
				Rank = e.Rank,
				Suit = e.Suit,
				Place = e.Place
			};
			if (e.Place == "Player")
			{
				state.LiveScore.PlayerCards.Add(card);
			}
			else if (e.Place == "Banker")
			{
				state.LiveScore.BankerCards.Add(card);
			}
			// Pragmatic's running-total field carries values such as 43, which is not a baccarat score,
			// so the hand is scored from its cards and the field is used only when it is credible.
			state.LiveScore.RecomputeScores();
			if (LiveScoreState.IsPlausibleScore(e.RunningScore))
			{
				if (e.Place == "Player")
				{
					state.LiveScore.PlayerScore = e.RunningScore;
				}
				else if (e.Place == "Banker")
				{
					state.LiveScore.BankerScore = e.RunningScore;
				}
			}
			state.LiveScore.TotalCardCount = e.CardCount;
		}
		if (!_gameIdToPanelMap.TryGetValue(tableKey, out Panel gamePanel))
		{
			return;
		}
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				UpdateLiveScoreLabel(gamePanel, state.LiveScore);
			});
		}
		else
		{
			UpdateLiveScoreLabel(gamePanel, state.LiveScore);
		}
	}

	/// <summary>Rewrites a hand from the provider's snapshot; leaves it untouched when none was sent.</summary>
	private static void ReplaceHand(List<CardInfo> target, List<string>? codes, string place)
	{
		if (codes == null)
		{
			return;
		}
		target.Clear();
		foreach (string code in codes)
		{
			target.Add(new CardInfo
			{
				CardCode = code,
				Rank = code.Length >= 2 ? code.Substring(0, code.Length - 1) : code,
				Suit = code.Length >= 2 ? code.Substring(code.Length - 1) : string.Empty,
				Place = place
			});
		}
	}

	private void Parser_CardIncremented(object? sender, PpCardIncArgs e)
	{
		string tableKey = ResolveTableKey(e.TableId);
		if (!_tableStates.TryGetValue(tableKey, out TableState state))
		{
			return;
		}
		state.LiveScore.RecomputeScores();
		if (LiveScoreState.IsPlausibleScore(e.RunningScore))
		{
			if (e.Place == "Player")
			{
				state.LiveScore.PlayerScore = e.RunningScore;
			}
			else if (e.Place == "Banker")
			{
				state.LiveScore.BankerScore = e.RunningScore;
			}
		}
		if (!_gameIdToPanelMap.TryGetValue(tableKey, out Panel gamePanel))
		{
			return;
		}
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				UpdateLiveScoreLabel(gamePanel, state.LiveScore);
			});
		}
		else
		{
			UpdateLiveScoreLabel(gamePanel, state.LiveScore);
		}
	}

	private void Parser_SeatReceived(object? sender, PpSeatArgs e)
	{
		if (!string.IsNullOrEmpty(e.UserId) && string.IsNullOrEmpty(_autoBettingService.Context.PpUserId))
		{
			_autoBettingService.Context.PpUserId = e.UserId;
			FileLogger.Log("[Form1] PP UserId 자동 감지: " + e.UserId);
		}
	}

	private void Parser_StatisticReceived(object? sender, PpStatisticArgs e)
	{
		if (e.BigRoadHistory.Count == 0)
		{
			return;
		}
		string tableKey = ResolveTableKey(e.TableId);
		// PP 는 statistic 을 한 번만 부트스트랩하고 이후 실시간 갱신은 gameresult 로 받는다.
		// 그러나 이 Evolution 사이트(스카이라인)는 개별 resolved/gameresult 를 보내지 않고
		// encodedShoeState.history_v2 로만 결과를 흘린다. 예전 코드는 "history_ 항목이 있으면 return"
		// 하여 최초 1회 복원 뒤 로드맵이 그대로 얼어붙었다(=실시간 미갱신 원인).
		// → Evolution 이면 history_v2 를 매 수신마다 병합해 로드맵을 갱신한다.
		bool isEvolution = PragmaticMessageParser.IsEvolutionTable(e.TableId);
		List<GameResult> history = _gameHistories.GetOrAdd(tableKey, (string _) => new List<GameResult>());
		bool changed = false;
		bool firstBootstrap = false;
		lock (history)
		{
			List<GameResult> restored = history.Where((GameResult h) => h.GameId.StartsWith("history_")).ToList();
			if (!isEvolution)
			{
				// PP: 1회 부트스트랩만.
				if (restored.Count > 0)
				{
					return;
				}
			}
			// 설치할 새 복원 시퀀스를 결정한다.
			List<string> newSeq;
			if (isEvolution && e.BigRoadHistory.Count < restored.Count)
			{
				// 짧은 델타: 기존 시퀀스를 유지하되 최신 결과가 새 것이면 한 건만 덧붙인다.
				newSeq = restored.Select((GameResult r) => r.Winner).ToList();
				string lastHave = restored.Count > 0 ? restored[restored.Count - 1].Winner : null;
				string lastNew = e.BigRoadHistory[e.BigRoadHistory.Count - 1];
				if (!string.Equals(lastHave, lastNew, StringComparison.OrdinalIgnoreCase))
				{
					newSeq.Add(lastNew);
				}
			}
			else
			{
				// 전체 스냅샷(또는 같은 길이의 갱신): 그대로 채택.
				newSeq = e.BigRoadHistory.ToList();
			}
			// 실제 변화가 없으면 조용히 종료(불필요한 재그리기·게이트 리셋 방지).
			bool same = newSeq.Count == restored.Count
				&& !newSeq.Where((string w, int i) => !string.Equals(w, restored[i].Winner, StringComparison.OrdinalIgnoreCase)).Any();
			if (same)
			{
				return;
			}
			firstBootstrap = restored.Count == 0;
			List<GameResult> nonRestored = history.Where((GameResult h) => !h.GameId.StartsWith("history_")).ToList();
			history.Clear();
			for (int i = 0; i < newSeq.Count; i++)
			{
				history.Add(new GameResult
				{
					GameId = $"history_{i}",
					TableId = tableKey,
					Winner = newSeq[i],
					ReceivedAt = DateTime.Now
				});
			}
			foreach (GameResult existing in nonRestored)
			{
				if (!history.Any((GameResult h) => h.GameId == existing.GameId))
				{
					history.Add(existing);
				}
			}
			if (history.Count > 200)
			{
				history.RemoveRange(0, history.Count - 200);
			}
			changed = true;
		}
		if (!changed)
		{
			return;
		}
		// 게이트/라운드추적 리셋은 최초 부트스트랩에서만. 증분 갱신마다 리셋하면 베팅 흐름을 방해한다.
		if (firstBootstrap)
		{
			_autoBettingService.ResetRoomGate(tableKey, "빅로드 재수집");
			if (_tableStates.TryGetValue(tableKey, out TableState bigRoadTableState))
			{
				bigRoadTableState.ResetRoundTracking();
			}
		}
		int restoredCount;
		string restoredTail;
		lock (history)
		{
			restoredCount = history.Count;
			restoredTail = string.Concat(from r in history.TakeLast(20)
				select (r.Winner.Length <= 0) ? '?' : char.ToUpper(r.Winner[0]));
		}
		string bigRoadTail = string.Concat(from w in e.BigRoadHistory.TakeLast(20)
			select (w.Length <= 0) ? '?' : char.ToUpper(w[0]));
		FileLogger.Log($"[패턴진단] statistic 복원: table={e.TableId} → key={tableKey}, bigRoad={e.BigRoadHistory.Count}건(말미20={bigRoadTail}), 최종 history={restoredCount}건(말미20={restoredTail})", "Info", "PatternMatch");
		if (!_gameIdToPanelMap.TryGetValue(tableKey, out Panel panel))
		{
			return;
		}
		PictureBox picRoad = panel.Controls.OfType<PictureBox>().FirstOrDefault((PictureBox p) => p.Name == "picRoad");
		if (picRoad == null)
		{
			return;
		}
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				picRoad.Invalidate();
			});
		}
		else
		{
			picRoad.Invalidate();
		}
	}

	/// <summary>
	/// Shows the hand in progress (dealt cards and running totals) for one room.
	/// <para>
	/// It used to sit at (5, 2), i.e. on top of the header row, where the ON button and the title
	/// label — added earlier and therefore above it in z-order — covered all but its top few pixels.
	/// What stayed visible looked like leftover paint that changed colour on its own. It now lives in
	/// the strip below the big road, which is always free: the road is capped at six rows of 18px.
	/// </para>
	/// </summary>
	private static void UpdateLiveScoreLabel(Panel gamePanel, LiveScoreState liveScore)
	{
		Label lblScore = gamePanel.Controls.Find("lblLiveScore", searchAllChildren: false).FirstOrDefault() as Label;
		if (lblScore == null)
		{
			PictureBox road = gamePanel.Controls.OfType<PictureBox>().FirstOrDefault(p => p.Name == "picRoad");
			int roadLeft = road?.Left ?? 10;
			int roadTop = road?.Top ?? 35;
			int roadHeight = road?.Height ?? 155;
			lblScore = new Label
			{
				Name = "lblLiveScore",
				AutoSize = false,
				// sits on the floor of the road canvas; the road itself is capped at 6 rows of 18px
				// plus a 2px margin, so it can never reach down here
				Size = new Size((road?.Width ?? 200) - 12, 20),
				Font = new Font("맑은 고딕", 10f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft,
				Location = new Point(roadLeft + 6, roadTop + roadHeight - 22),
				Anchor = (AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right),
				BackColor = Color.FromArgb(18, 18, 38)
			};
			gamePanel.Controls.Add(lblScore);
			lblScore.BringToFront();
		}
		if (liveScore.PlayerCards.Count == 0 && liveScore.BankerCards.Count == 0)
		{
			lblScore.Visible = false;
			return;
		}
		string playerCards = string.Join(" ", liveScore.PlayerCards.Select((CardInfo c) => c.DisplayText));
		string bankerCards = string.Join(" ", liveScore.BankerCards.Select((CardInfo c) => c.DisplayText));
		lblScore.Text = $"P[{playerCards}]={liveScore.PlayerScore}  B[{bankerCards}]={liveScore.BankerScore}";
		// Matched to the big road's own colours, and readable on the dark panel — the old
		// Blue/DarkGreen were all but invisible against it.
		if (liveScore.PlayerScore > liveScore.BankerScore)
		{
			lblScore.ForeColor = Color.DodgerBlue;
		}
		else if (liveScore.BankerScore > liveScore.PlayerScore)
		{
			lblScore.ForeColor = Color.Crimson;
		}
		else
		{
			lblScore.ForeColor = Color.LimeGreen;
		}
		lblScore.Visible = true;
	}

	private void WsServer_ConnectionStateChanged(object? sender, bool isConnected)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				WsServer_ConnectionStateChanged(sender, isConnected);
			});
		}
		else
		{
			UpdateConnectionStatus(isConnected);
		}
	}

	/// <summary>
	/// Pulls the PP account id out of an outgoing bet frame.
	/// <para>
	/// The bot normally learns it from a <c>seat</c> message, but PP MULTIPLAY never sends one — the
	/// id only ever appears as the <c>uId</c> attribute of an <c>lpbet</c> command. Reading it off a
	/// bet the player places by hand lets automatic betting start without leaving MULTIPLAY. It is
	/// deliberately restricted to frames we sent: the incoming <c>winners</c> broadcasts carry other
	/// players' ids and picking one of those would bet under someone else's account.
	/// </para>
	/// </summary>
	private static readonly Regex OutgoingBetUserIdRegex =
		new Regex("<lpbet[^>]*\\suId=\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private void LearnPpUserIdFromSentFrame(string data)
	{
		if (data.IndexOf("lpbet", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return;
		}
		Match match = OutgoingBetUserIdRegex.Match(data);
		if (!match.Success)
		{
			return;
		}
		ApplyPpUserId(match.Groups[1].Value, "수동 베팅 프레임");
	}

	private void CdpService_PpUserIdDetected(object? sender, string userId)
	{
		ApplyPpUserId(userId, "지갑 응답");
	}

	/// <summary>
	/// Adopts a PP account id from whichever source saw it first and remembers it for next time.
	/// A different id means the operator signed in to another PP account, so the newest one wins
	/// rather than the bot staying locked to the one it happened to see first.
	/// </summary>
	private void ApplyPpUserId(string userId, string source)
	{
		if (string.IsNullOrWhiteSpace(userId) || userId == _autoBettingService.Context.PpUserId)
		{
			return;
		}
		_autoBettingService.Context.PpUserId = userId;
		PpUserIdStore.Save(_currentUserId, userId);
		FileLogger.Log($"[Form1] PP UserId 자동 감지 ({source}): {userId}", "Info", DbLogService.SessionCategory);
		string remark = "PP 계정 확인됨 (" + userId + ") — 자동 베팅 준비 완료";
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				SetBetStatusRemark(remark, Color.LimeGreen);
			});
		}
		else
		{
			SetBetStatusRemark(remark, Color.LimeGreen);
		}
	}

	private void CdpService_WebSocketMessageReceived(object? sender, PragmaticWebSocketMessageEventArgs e)
	{
		if (e.Direction == "send")
		{
			if (!string.IsNullOrEmpty(e.Data))
			{
				LearnPpUserIdFromSentFrame(e.Data);
			}
			return;
		}
		if (!(e.Direction == "receive") || string.IsNullOrEmpty(e.Data))
		{
			return;
		}
		long cdpMsgNum = Interlocked.Increment(ref _cdpMessageReceivedCount);
		if (cdpMsgNum <= 10)
		{
			string preview = ((e.Data.Length > 200) ? (e.Data.Substring(0, 200) + "...") : e.Data);
			FileLogger.Log($"[Form1] CDP 메시지 #{cdpMsgNum}: len={e.Data.Length}, data={preview}");
		}
		_messageParser.ParseMessage(e.Data);
		if (!_wsServer.IsWebSocketCaptured)
		{
			_wsServer.IsWebSocketCaptured = true;
			FileLogger.Log("[Form1] CDP 경로에서 WebSocket 캡처 상태 설정");
		}
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				_receivedCount++;
				UpdateMessageCounter();
			});
		}
		else
		{
			_receivedCount++;
			UpdateMessageCounter();
		}
	}

	private void CdpService_ConnectionStateChanged(object? sender, bool isConnected)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				CdpService_ConnectionStateChanged(sender, isConnected);
			});
			return;
		}
		FileLogger.Log($"[Form1] CDP 연결 상태 변경: {isConnected}");
		UpdateConnectionStatus(isConnected || _wsServer.IsExtensionConnected);
	}

	private void CdpService_ErrorOccurred(object? sender, string errorMessage)
	{
		FileLogger.Log("[Form1] CDP 오류: " + errorMessage, "Warning", "CDP");
	}

	private void WsServer_MessageCaptured(object? sender, CapturedMessage msg)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				WsServer_MessageCaptured(sender, msg);
			});
		}
		else
		{
			_receivedCount++;
			UpdateMessageCounter();
		}
	}

	private async void SessionMonitor_SessionInvalidated(object? sender, string message)
	{
		if (base.InvokeRequired)
		{
			Invoke(delegate
			{
				SessionMonitor_SessionInvalidated(sender, message);
			});
			return;
		}
		if (_autoBettingService.IsRunning)
		{
			await _autoBettingService.StopAsync();
		}
		// 세션 종료 사유는 message 에 담겨 온다(중복 로그인 / 사용 기간 만료 / 비활성 계정 등).
		// 만료 시엔 서버가 재로그인도 거부하므로, 관리자 연장 전까지는 다시 들어올 수 없다.
		MessageBox.Show(message + "\n\n프로그램을 종료합니다.", "세션 종료", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		_forceClose = true;
		Close();
	}

	private void AutoBetting_StateChanged(object? sender, AutoBettingStateChangedEventArgs e)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				AutoBetting_StateChanged(sender, e);
			});
			return;
		}
		btnGameStart.Enabled = !e.IsRunning;
		if (_btnVirtualBet != null)
		{
			// 실행 중엔 두 시작 버튼 모두 비활성(실/가상 배타적). 가상 실행 중엔 라벨로 표시.
			_btnVirtualBet.Enabled = !e.IsRunning;
			_btnVirtualBet.Text = (e.IsRunning && _autoBettingService.Context.IsVirtualMode) ? "[가상] 실행 중" : "가상베팅";
		}
		btnPause.Enabled = e.IsRunning;
		btnStopBet.Enabled = e.IsRunning;
		// The strategy is read once, when 게임시작 is pressed. Leaving the box editable let the screen
		// claim 플랫 베팅 while the engine was still running 마틴 from the start of the session.
		cmbBetAmountType.Enabled = !e.IsRunning;
		btnExit.Enabled = !_cdpService.IsConnected && !_wsServer.IsExtensionConnected && !e.IsRunning;
		if (e.IsRunning)
		{
			if (_autoBettingService.Context.IsVirtualMode)
			{
				btnGameStart.Text = "[가상] 실행 중...";
				btnGameStart.BackColor = Color.FromArgb(0, 150, 200);
			}
			else
			{
				btnGameStart.Text = "실행 중...";
				btnGameStart.BackColor = Color.Gray;
			}
		}
		else
		{
			btnGameStart.Text = "게임시작";
			btnGameStart.BackColor = Color.FromArgb(40, 167, 69);
		}
		if (!e.IsRunning)
		{
			_lastAppliedStepVersion.Clear();
			ClearAllRoomBetSteps();
		}
		if (!e.IsRunning && !string.IsNullOrEmpty(e.StopReason))
		{
			_elapsedTimer?.Stop();
			_balanceSyncTimer?.Stop();
			lblTimer.Text = "00:00:00";
			MessageBox.Show(e.Message + "\n\n사유: " + e.StopReason, "자동 베팅 정지", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
	}

	private void AutoBetting_BetExecuted(object? sender, AutoBettingExecutedEventArgs e)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				AutoBetting_BetExecuted(sender, e);
			});
			return;
		}
		if (e.IsSuccess)
		{
			string newText = _autoBettingService.Context.TotalBetAmount.ToString("N0");
			if (lblTotalBetValue.Text != newText)
			{
				lblTotalBetValue.Text = newText;
			}
			if (_autoBettingService.Context.IsVirtualMode)
			{
				UpdateBalanceUI();
			}
		}
		if (e.IsSuccess && !e.IsIdleRound)
		{
			RecordRoomBetStep(e.TableId, e.Step, e.MatrixRow, e.MatrixCol);
		}
		if (_lastAppliedStepVersion.TryGetValue(e.TableId, out var lastVer) && e.StepVersion < lastVer)
		{
			FileLogger.Log($"[Form1] BetExecuted stale 무시: {e.TableId}, StepVersion={e.StepVersion} < last={lastVer}", "Warning", "UI");
			return;
		}
		_lastAppliedStepVersion[e.TableId] = e.StepVersion;
		decimal? actualAmount = null;
		AutoBettingContext ctx = _autoBettingService.Context;
		if ((ctx.SelectedBetAmountType == 1 || (ctx.SelectedBetAmountType == 3 && ctx.TableIndexMap.GetValueOrDefault(e.TableId, 0) % 2 == 1)) && ctx.BetAmounts.TryGetValue(e.Step, out var stepAmount) && e.Amount != stepAmount)
		{
			actualAmount = e.Amount;
		}
		UpdateBetStepLabel(e.TableId, e.Step, e.BetType, actualAmount);
		if (!e.IsIdleRound)
		{
			AddBetHistoryRow(e, actualAmount ?? e.Amount);
		}
		if (e.IsSuccess && !string.IsNullOrEmpty(e.SiteSplitRemark))
		{
			lblSiteSplitInfo.Text = "분할 내역  " + e.SiteSplitRemark;
		}
		if (!e.IsIdleRound)
		{
			if (e.IsSuccess)
			{
				SetBetStatusRemark($"{GetTableDisplayName(e.TableId)}  {actualAmount:N0}원  베팅중", Color.Orange);
			}
			else
			{
				SetBetStatusRemark($"{GetTableDisplayName(e.TableId)}  {actualAmount:N0}원  베팅실패" + (string.IsNullOrEmpty(e.ErrorMessage) ? "" : (" (" + e.ErrorMessage + ")")), Color.OrangeRed);
			}
		}
	}

	private void UpdateBetStatusRemark(AutoBettingResultEventArgs e)
	{
		SettleBetHistoryRow(e);
		string roomName = GetTableDisplayName(e.TableId);
		bool isTie = string.Equals(e.GameResult, "Tie", StringComparison.OrdinalIgnoreCase);
		if (e.IsIdleRound)
		{
			string idleResult = (isTie ? "타이" : (e.IsWin ? "당첨" : "미당첨"));
			SetBetStatusRemark(roomName + "  관망  (" + idleResult + ")", Color.Silver);
		}
		else if (!(e.BetAmount <= 0m))
		{
			if (isTie)
			{
				SetBetStatusRemark($"{roomName}  {e.BetAmount:N0}원  타이 0", Color.Gold);
			}
			else if (e.IsWin)
			{
				decimal profit = (string.Equals(e.BetType, "Banker", StringComparison.OrdinalIgnoreCase) ? (e.BetAmount * 0.95m) : e.BetAmount);
				SetBetStatusRemark($"{roomName}  {e.BetAmount:N0}원  당첨 {profit:+#,##0}", Color.LimeGreen);
			}
			else
			{
				SetBetStatusRemark($"{roomName}  {e.BetAmount:N0}원  미당첨 {-e.BetAmount:+#,##0;-#,##0}", Color.OrangeRed);
			}
		}
	}

	private Label? lblGameProvider;

	private Button? btnSettingsHub;

	private PictureBox? _picLogo;

	/// <summary>
	/// 흩어진 설정 버튼들을 하나의 "설정" 버튼(→모달)으로 모으고, 하단바에 현재 게임형식(에볼루션/
	/// 프라그마틱, 강조)과 봇 가동시간을 표시한다. 가동시간 라벨은 상단 메뉴바에서 하단바로 옮긴다.
	/// </summary>
	private void SetupConsolidatedUi()
	{
		// 0) 좌측 상단 로고: "KingTigerBet" 텍스트를 로고 이미지로 교체하고, 클릭하면 이용설명서를 연다.
		SetupLogo();

		// 1) 설정 허브 버튼을 상단 메뉴바에 추가
		btnSettingsHub = new Button
		{
			Name = "btnSettingsHub",
			Text = "⚙ 설정",
			Size = new Size(120, 37),
			// 우측 유저이름(lblUser, x=1850) 왼쪽에 배치하고 우측 앵커로 창 크기에 따라 함께 움직이게 한다.
			Location = new Point(1718, 12),
			Anchor = AnchorStyles.Top | AnchorStyles.Right,
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(34, 110, 58),
			ForeColor = Color.White,
			Font = new Font("맑은 고딕", 10f, FontStyle.Bold),
			Cursor = Cursors.Hand
		};
		btnSettingsHub.FlatAppearance.BorderSize = 0;
		btnSettingsHub.Click += OpenSettingsHub;
		panelMenu.Controls.Add(btnSettingsHub);
		btnSettingsHub.BringToFront();
		// 폼이 디자인 크기(2065)에서 실제 크기로 자동 스케일 축소되면, 코드로 넣은 고정 X 좌표(1718)는
		// 스케일되지 않아 오른쪽 화면 밖으로 나간다. 우측 유저이름(lblUser) 왼쪽에 붙이고, 창 크기가
		// 바뀔 때마다 다시 배치한다.
		panelMenu.SizeChanged += delegate { PositionSettingsHubButton(); };
		Shown += delegate { PositionSettingsHubButton(); };
		PositionSettingsHubButton();

		// 2) 기존 설정 버튼들 숨김(핸들러는 그대로 → 허브에서 직접 호출).
		//    플레이 방·방 설정은 메인 뷰전환이라 허브에 넣지 않고 원래 자리(상단바)에 그대로 둔다.
		Control[] toHide = { btnFormatManage, btnPriorityManage, sfButton1, btnOptionSetting, btnSecondLogic, btnMatrixStats, btnRoomCooldown, btnExceptionFormat, btnMatrixReset };
		foreach (Control c in toHide)
		{
			if (c != null) { c.Visible = false; }
		}

		// 3) 하단바: 게임형식(강조) + 가동시간
		lblGameProvider = new Label
		{
			Name = "lblGameProvider",
			AutoSize = true,
			Font = new Font("맑은 고딕", 12f, FontStyle.Bold),
			ForeColor = Color.FromArgb(120, 200, 255),
			Location = new Point(700, 20),
			Text = "게임: —",
			Anchor = AnchorStyles.Bottom | AnchorStyles.Left
		};
		panelStatus.Controls.Add(lblGameProvider);
		lblGameProvider.BringToFront();

		// 가동시간(lblTimer)을 상단 메뉴바 → 하단바 게임형식 옆으로 이동
		panelMenu.Controls.Remove(lblTimer);
		panelStatus.Controls.Add(lblTimer);
		lblTimer.Font = new Font("Consolas", 14f, FontStyle.Bold);
		lblTimer.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
		lblTimer.Location = new Point(880, 18);
		lblTimer.BringToFront();

		UpdateGameProviderLabel();
	}

	/// <summary>좌측 상단 로고 이미지를 배치한다. 클릭하면 이용설명서 모달이 뜬다.
	/// 로고를 못 불러오면 기존 텍스트 라벨을 그대로 두되 클릭으로 설명서는 열 수 있게 한다.</summary>
	private void SetupLogo()
	{
		if (lblLogo != null)
		{
			lblLogo.Cursor = Cursors.Hand;
			lblLogo.Click -= LogoClicked;
			lblLogo.Click += LogoClicked;
		}
		Image img = PragmaticBot.Services.AppImages.Logo;
		if (img == null || lblLogo == null) { return; }

		lblLogo.Text = string.Empty;
		lblLogo.Visible = false;

		int h = Math.Max(24, panelMenu.ClientSize.Height - 12);
		_picLogo = new PictureBox
		{
			Name = "picLogo",
			Image = img,
			SizeMode = PictureBoxSizeMode.Zoom,
			Size = new Size(h, h),
			Location = new Point(lblLogo.Left, Math.Max(0, (panelMenu.ClientSize.Height - h) / 2)),
			BackColor = Color.White,
			Cursor = Cursors.Hand,
			TabStop = false
		};
		_picLogo.Click += LogoClicked;
		panelMenu.Controls.Add(_picLogo);
		_picLogo.BringToFront();
	}

	private void LogoClicked(object sender, EventArgs e) => ShowUserManual();

	/// <summary>이용설명서 모달을 연다.</summary>
	private void ShowUserManual()
	{
		try
		{
			using UserManualForm f = new UserManualForm();
			f.ShowDialog(this);
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Form1] 이용설명서 열기 실패: " + ex.Message, "Warning");
		}
	}

	/// <summary>설정 허브 버튼을 우측 유저이름(lblUser) 바로 왼쪽에 배치한다(스케일·리사이즈 안전).</summary>
	private void PositionSettingsHubButton()
	{
		if (btnSettingsHub == null) { return; }
		int x = lblUser.Left - btnSettingsHub.Width - 12;
		if (x < 0) { x = 0; }
		btnSettingsHub.Location = new Point(x, lblUser.Top - 2);
		btnSettingsHub.BringToFront();
	}

	private void OpenSettingsHub(object? sender, EventArgs e)
	{
		// 마지막 인자 CloseHubAfter: true 면 실행 후 허브를 닫는다.
		//  - 모달 설정(형식/베팅금/상하한/옵션/세컨로직/행렬통계/쿨다운/예외서식/행렬초기화): false
		//    → 허브 위에 새 모달로 뜨고, 닫으면 허브로 복귀. 여러 설정을 연달아 진행 가능.
		//  - 플레이 방/방 설정: 메인 화면을 바꾸는 뷰전환이라 true → 실행 후 허브를 닫아 바뀐 화면을 보여준다.
		List<(string, System.Action, bool)> items = new List<(string, System.Action, bool)>
		{
			(btnFormatManage.Text, delegate { BtnFormatManage_Click(this, EventArgs.Empty); }, false),
			(btnPriorityManage.Text, delegate { BtnPriorityManage_Click(this, EventArgs.Empty); }, false),
			(sfButton1.Text, delegate { SfButton1_Click(this, EventArgs.Empty); }, false),
			("체인설정", delegate { OpenStrategyChainSettings(); }, false),
			(btnOptionSetting.Text, delegate { BtnOptionSetting_Click(this, EventArgs.Empty); }, false),
			(btnSecondLogic.Text, delegate { BtnSecondLogic_Click(this, EventArgs.Empty); }, false),
			(btnMatrixStats.Text, delegate { BtnMatrixStats_Click(this, EventArgs.Empty); }, false),
			(btnRoomCooldown.Text, delegate { BtnRoomCooldown_Click(this, EventArgs.Empty); }, false),
			(btnExceptionFormat.Text, delegate { BtnExceptionFormat_Click(this, EventArgs.Empty); }, false),
			(btnMatrixReset.Text, delegate { BtnMatrixReset_Click(this, EventArgs.Empty); }, false)
		};
		using SettingsHubForm hub = new SettingsHubForm(items);
		hub.ShowDialog(this);
	}

	/// <summary>하단바 게임형식 라벨을 현재 방들의 provider 로 갱신(에볼루션/프라그마틱, 강조체).</summary>
	private void UpdateGameProviderLabel()
	{
		if (lblGameProvider == null) { return; }
		bool anyEvo = false, anyPp = false;
		foreach (TableState ts in _tableStates.Values)
		{
			if (string.Equals(ts.Provider, PragmaticBot.Services.GameProviderNames.EvolutionKey, StringComparison.OrdinalIgnoreCase)) { anyEvo = true; }
			else { anyPp = true; }
		}
		if (!anyEvo && _messageParser.HasEvolutionActivity) { anyEvo = true; }
		string text; Color color;
		if (anyEvo && anyPp) { text = "에볼루션 + 프라그마틱"; color = Color.FromArgb(255, 210, 100); }
		else if (anyEvo) { text = "에볼루션"; color = Color.FromArgb(120, 200, 255); }
		else if (anyPp) { text = "프라그마틱"; color = Color.FromArgb(120, 230, 150); }
		else { text = "—"; color = Color.Gray; }
		string newText = "게임: " + text;
		if (lblGameProvider.Text != newText) { lblGameProvider.Text = newText; }
		if (lblGameProvider.ForeColor != color) { lblGameProvider.ForeColor = color; }
	}

	private void SetBetStatusRemark(string text, Color color)
	{
		if (lblBetStatusInfo.Text != text)
		{
			lblBetStatusInfo.Text = text;
		}
		if (lblBetStatusInfo.ForeColor != color)
		{
			lblBetStatusInfo.ForeColor = color;
		}
	}

	/// <summary>
	/// Builds the 베팅 이력 grid. The designer leaves sfDataGrid1 without columns or a data source,
	/// so everything it needs is set up here once.
	/// </summary>
	private void InitializeBetHistoryGrid()
	{
		sfDataGrid1.AutoGenerateColumns = false;
		sfDataGrid1.AllowEditing = false;
		sfDataGrid1.AllowSorting = false;
		sfDataGrid1.SelectionMode = Syncfusion.WinForms.DataGrid.Enums.GridSelectionMode.Single;
		// 열 너비 사용자 조절 허용 + 고정폭(합이 패널보다 넓으면 수평 스크롤바 자동 표시)
		sfDataGrid1.AllowResizingColumns = true;
		sfDataGrid1.AutoSizeColumnsMode = Syncfusion.WinForms.DataGrid.Enums.AutoSizeColumnsMode.None;
		sfDataGrid1.ShowRowHeader = false;
		sfDataGrid1.HeaderRowHeight = 28;
		sfDataGrid1.RowHeight = 26;

		sfDataGrid1.Style.HeaderStyle.BackColor = Color.FromArgb(30, 44, 82);
		sfDataGrid1.Style.HeaderStyle.TextColor = Color.FromArgb(234, 234, 234);
		sfDataGrid1.Style.CellStyle.BackColor = Color.FromArgb(22, 33, 62);
		sfDataGrid1.Style.CellStyle.TextColor = Color.FromArgb(222, 222, 222);
		sfDataGrid1.Style.SelectionStyle.BackColor = Color.FromArgb(45, 65, 110);
		sfDataGrid1.Style.SelectionStyle.TextColor = Color.White;
		sfDataGrid1.Style.BorderColor = Color.FromArgb(45, 58, 92);

		sfDataGrid1.Columns.Clear();
		sfDataGrid1.Columns.Add(new GridTextColumn
		{
			MappingName = "Time",
			HeaderText = "시간",
			Width = 68,
			Format = "HH:mm:ss"
		});
		sfDataGrid1.Columns.Add(new GridTextColumn
		{
			MappingName = "RoomName",
			HeaderText = "방",
			Width = 180
		});
		sfDataGrid1.Columns.Add(new GridTextColumn
		{
			MappingName = "Step",
			HeaderText = "단계",
			Width = 46
		});
		sfDataGrid1.Columns.Add(new GridTextColumn
		{
			MappingName = "BetType",
			HeaderText = "베팅",
			Width = 60
		});
		sfDataGrid1.Columns.Add(new GridTextColumn
		{
			MappingName = "BetAmount",
			HeaderText = "금액",
			Width = 84,
			Format = "N0"
		});
		sfDataGrid1.Columns.Add(new GridTextColumn
		{
			MappingName = "Status",
			HeaderText = "결과",
			Width = 68
		});
		sfDataGrid1.Columns.Add(new GridTextColumn
		{
			MappingName = "Profit",
			HeaderText = "손익",
			Width = 84,
			Format = "+#,##0;-#,##0;0"
		});

		// colour the 결과/손익 cells the same way the 비고 line is coloured
		sfDataGrid1.QueryCellStyle += BetHistoryGrid_QueryCellStyle;
		sfDataGrid1.DataSource = _betHistoryRows;

		// sits in the action bar alongside 배팅중지 — LayoutActionButtons gives all four the same size
		_btnBetHistoryDetail = new Button
		{
			Name = "btnBetHistoryDetail",
			Text = "축소버젼",
			Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(40, 90, 160),
			ForeColor = Color.White,
			Size = new Size(178, btnStopBet.Height),
			Cursor = Cursors.Hand,
			TabStop = false
		};
		_btnBetHistoryDetail.FlatAppearance.BorderSize = 0;
		_btnBetHistoryDetail.Click += BtnBetHistoryDetail_Click;
		panelActions.Controls.Add(_btnBetHistoryDetail);

		// 가상베팅: 실제 전송 없이 지정한 서식으로 모의 베팅해 수익성만 검증한다. 게임시작(실베팅)과 배타적.
		_btnVirtualBet = new Button
		{
			Name = "btnVirtualBet",
			Text = "가상베팅",
			Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(120, 80, 200),
			ForeColor = Color.White,
			Size = new Size(178, btnStopBet.Height),
			Cursor = Cursors.Hand,
			TabStop = false
		};
		_btnVirtualBet.FlatAppearance.BorderSize = 0;
		_btnVirtualBet.Click += BtnVirtualBet_Click;
		panelActions.Controls.Add(_btnVirtualBet);

		// crossing the point where a vertical scrollbar appears changes how much width is usable
		_betHistoryRows.ListChanged += delegate
		{
			FitBetHistoryColumns();
		};

		// the grid is still at its designer size during Load, so refit once the form has actually
		// been laid out as well as on every later resize
		sfDataGrid1.SizeChanged -= BetHistoryGrid_SizeChanged;
		sfDataGrid1.SizeChanged += BetHistoryGrid_SizeChanged;
		panelHistory.SizeChanged -= BetHistoryGrid_SizeChanged;
		panelHistory.SizeChanged += BetHistoryGrid_SizeChanged;
		base.Shown -= Form1_ShownFitColumns;
		base.Shown += Form1_ShownFitColumns;
		panelActions.SizeChanged -= PanelActions_SizeChanged;
		panelActions.SizeChanged += PanelActions_SizeChanged;
		FitBetHistoryColumns();
		LayoutActionButtons();
	}

	private void Form1_ShownFitColumns(object? sender, EventArgs e)
	{
		FitBetHistoryColumns();
		LayoutActionButtons();
	}

	private void BetHistoryGrid_SizeChanged(object? sender, EventArgs e)
	{
		FitBetHistoryColumns();
	}

	private BetHistoryForm? _openHistoryForm;

	private void BtnBetHistoryDetail_Click(object? sender, EventArgs e)
	{
		// 축소버젼: 상세보기 창만 남기고 메인폼은 최소화. 상세창의 "메인버젼" 버튼(또는 닫기/X)으로 복귀.
		if (_openHistoryForm != null && !_openHistoryForm.IsDisposed)
		{
			_openHistoryForm.Activate();
			return;
		}
		BetHistoryForm form = new BetHistoryForm(
			_betHistoryRows,
			delegate
			{
				// 읽기 전용 — 베팅 로직과 무관하게 현재 값만 표시
				var c = _autoBettingService.Context;
				decimal profit = c.AvailableBalance - c.StartBalance;
				return (c.StartBalance.ToString("N0") + "원",
					c.AvailableBalance.ToString("N0") + "원",
					profit.ToString("+#,##0;-#,##0;0") + "원",
					c.TotalBetAmount.ToString("N0") + "원");
			},
			delegate
			{
				// 메인버젼: 메인폼 복귀
				WindowState = FormWindowState.Normal;
				Activate();
			});
		_openHistoryForm = form;
		form.FormClosed += delegate
		{
			_openHistoryForm = null;
			if (WindowState == FormWindowState.Minimized)
			{
				WindowState = FormWindowState.Normal;
			}
			Activate();
		};
		// 소유자 없이 독립 창으로 띄워야 메인 최소화 시에도 상세창이 남는다
		form.Show();
		WindowState = FormWindowState.Minimized;
	}

	/// <summary>
	/// Share of the grid width each column gets, and the narrowest it may become. Fixed widths do not
	/// work here: the history panel can be a good deal narrower than the designer size, and any fixed
	/// set eventually adds up past the client area and brings the horizontal scrollbar back.
	/// </summary>
	private static readonly (string Mapping, double Weight, int Min)[] BetHistoryColumnLayout =
	{
		("Time", 0.13, 52),
		("RoomName", 0.27, 74),
		("Step", 0.08, 34),
		("BetType", 0.12, 46),
		("BetAmount", 0.15, 56),
		("Status", 0.11, 44),
		("Profit", 0.14, 56)
	};

	private int _lastFittedGridWidth = -1;

	/// <summary>
	/// (디자인 변경) 예전에는 열을 패널 너비에 맞춰 압축해 수평 스크롤을 없앴으나, 이제는
	/// 고정폭을 유지해 수평 스크롤로 전체 이력을 보고 사용자가 열 너비를 직접 조절하도록 둔다.
	/// 그래서 이 메서드는 더 이상 열 폭을 재계산하지 않는다(no-op).
	/// </summary>
	private void FitBetHistoryColumns()
	{
		// 고정폭 유지: 압축하지 않음 → 수평 스크롤/열 조절 보존
		return;
#pragma warning disable CS0162
		if (sfDataGrid1.Columns.Count == 0)
		{
			return;
		}

		// Only hold back the vertical scrollbar once there are actually enough rows to need one —
		// reserving it unconditionally left the header visibly short of the table's right edge.
		int bodyHeight = sfDataGrid1.ClientSize.Height - sfDataGrid1.HeaderRowHeight;
		int visibleRows = (sfDataGrid1.RowHeight > 0) ? (bodyHeight / sfDataGrid1.RowHeight) : 0;
		int reserve = (_betHistoryRows.Count > visibleRows) ? 20 : 2;

		int available = sfDataGrid1.ClientSize.Width - reserve;
		if (available <= 0 || available == _lastFittedGridWidth)
		{
			return;
		}
		_lastFittedGridWidth = available;

		int[] widths = new int[BetHistoryColumnLayout.Length];
		int total = 0;
		for (int i = 0; i < BetHistoryColumnLayout.Length; i++)
		{
			var (_, weight, min) = BetHistoryColumnLayout[i];
			widths[i] = Math.Max(min, (int)(available * weight));
			total += widths[i];
		}

		// the minimums can push the total past what we have — shave the overflow off the widest
		// columns first so the narrow ones stay readable
		while (total > available)
		{
			int widest = 0;
			for (int i = 1; i < widths.Length; i++)
			{
				if (widths[i] - BetHistoryColumnLayout[i].Min > widths[widest] - BetHistoryColumnLayout[widest].Min)
				{
					widest = i;
				}
			}
			if (widths[widest] <= BetHistoryColumnLayout[widest].Min)
			{
				break; // nothing left to give; the grid is simply too narrow
			}
			int take = Math.Min(total - available, widths[widest] - BetHistoryColumnLayout[widest].Min);
			widths[widest] -= take;
			total -= take;
		}

		for (int i = 0; i < BetHistoryColumnLayout.Length; i++)
		{
			GridColumn column = sfDataGrid1.Columns[BetHistoryColumnLayout[i].Mapping];
			if (column != null)
			{
				column.Width = widths[i];
			}
		}
#pragma warning restore CS0162
	}

	private Button? _btnBetHistoryDetail;

	private Button? _btnVirtualBet;

	/// <summary>가상베팅 시작 자금(사용자가 지정). 다음 실행 때 기본값으로 다시 보여준다.</summary>
	private decimal _virtualStartAmount = 1000000m;

	/// <summary>
	/// Spreads 게임시작·일시정지·배팅중지·상세이력보기 evenly across the action bar, all the same size.
	/// They used to be stacked at a fixed x, which both wasted height and overflowed once the panel
	/// was narrower than the designer size.
	/// </summary>
	private void LayoutActionButtons()
	{
		const int gap = 8;
		const int margin = 13;
		List<Control> buttonList = new List<Control> { btnGameStart };
		if (_btnVirtualBet != null) { buttonList.Add(_btnVirtualBet); }
		buttonList.Add(btnPause);
		buttonList.Add(btnStopBet);
		if (_btnBetHistoryDetail != null) { buttonList.Add(_btnBetHistoryDetail); }
		Control[] buttons = buttonList.ToArray();

		int usable = panelActions.ClientSize.Width - (margin * 2) - (gap * (buttons.Length - 1));
		if (usable <= 0)
		{
			return;
		}
		int each = usable / buttons.Length;
		int height = btnGameStart.Height;
		int top = Math.Max(6, (panelActions.ClientSize.Height - height) / 2);
		int x = margin;
		for (int i = 0; i < buttons.Length; i++)
		{
			// the last one takes the rounding remainder so the row ends flush on the right
			int width = (i == buttons.Length - 1) ? (panelActions.ClientSize.Width - margin - x) : each;
			buttons[i].Bounds = new Rectangle(x, top, width, height);
			x += width + gap;
		}
	}

	private void PanelActions_SizeChanged(object? sender, EventArgs e)
	{
		LayoutActionButtons();
	}

	private void BetHistoryGrid_QueryCellStyle(object? sender, Syncfusion.WinForms.DataGrid.Events.QueryCellStyleEventArgs e)
	{
		if (e.DataRow?.RowData is not BetHistoryRow row)
		{
			return;
		}
		if (e.Column.MappingName != "Status" && e.Column.MappingName != "Profit")
		{
			return;
		}
		e.Style.TextColor = row.Status switch
		{
			"당첨" => Color.LimeGreen,
			"미당첨" => Color.OrangeRed,
			"타이" => Color.Gold,
			"관망" => Color.Silver,
			"실패" => Color.OrangeRed,
			_ => Color.Orange
		};
	}

	/// <summary>Records an outgoing bet as a pending row at the top of the grid.</summary>
	private void AddBetHistoryRow(AutoBettingExecutedEventArgs e, decimal amount)
	{
		BetHistoryRow row = new BetHistoryRow
		{
			TableId = e.TableId,
			GameId = e.GameId,
			RoomName = GetTableDisplayName(e.TableId),
			Step = e.Step,
			BetType = BetTypeLabel(e.BetType),
			BetAmount = amount,
			Status = (e.IsSuccess ? "베팅중" : "실패")
		};
		if (!e.IsSuccess)
		{
			row.Settle("실패", "", 0m);
		}
		_betHistoryRows.Insert(0, row);
		while (_betHistoryRows.Count > MaxBetHistoryRows)
		{
			_betHistoryRows.RemoveAt(_betHistoryRows.Count - 1);
		}
	}

	/// <summary>
	/// Fills in the result on the pending row for this round. A settlement with no matching bet row
	/// (a 관망 round, or a result that arrived after a restart) is appended instead of dropped.
	/// </summary>
	private void SettleBetHistoryRow(AutoBettingResultEventArgs e)
	{
		bool isTie = string.Equals(e.GameResult, "Tie", StringComparison.OrdinalIgnoreCase);
		string status = (e.IsIdleRound ? "관망" : (isTie ? "타이" : (e.IsWin ? "당첨" : "미당첨")));
		decimal profit = 0m;
		if (!e.IsIdleRound && !isTie && e.BetAmount > 0m)
		{
			profit = (e.IsWin
				? (string.Equals(e.BetType, "Banker", StringComparison.OrdinalIgnoreCase) ? e.BetAmount * 0.95m : e.BetAmount)
				: (-e.BetAmount));
		}

		BetHistoryRow? pending = _betHistoryRows.FirstOrDefault((BetHistoryRow r) =>
			!r.IsSettled && r.TableId == e.TableId &&
			(string.IsNullOrEmpty(e.GameId) || string.IsNullOrEmpty(r.GameId) || r.GameId == e.GameId));

		if (pending != null)
		{
			pending.Settle(status, ResultLabel(e.GameResult), profit);
			return;
		}

		BetHistoryRow row = new BetHistoryRow
		{
			TableId = e.TableId,
			GameId = e.GameId,
			RoomName = GetTableDisplayName(e.TableId),
			BetType = (e.IsIdleRound ? "-" : BetTypeLabel(e.BetType)),
			BetAmount = (e.IsIdleRound ? 0m : e.BetAmount)
		};
		row.Settle(status, ResultLabel(e.GameResult), profit);
		_betHistoryRows.Insert(0, row);
		while (_betHistoryRows.Count > MaxBetHistoryRows)
		{
			_betHistoryRows.RemoveAt(_betHistoryRows.Count - 1);
		}
	}

	private static string BetTypeLabel(string betType)
	{
		if (string.Equals(betType, "Player", StringComparison.OrdinalIgnoreCase))
		{
			return "플레이어";
		}
		if (string.Equals(betType, "Banker", StringComparison.OrdinalIgnoreCase))
		{
			return "뱅커";
		}
		return betType ?? "";
	}

	private static string ResultLabel(string gameResult)
	{
		if (string.Equals(gameResult, "Player", StringComparison.OrdinalIgnoreCase))
		{
			return "P";
		}
		if (string.Equals(gameResult, "Banker", StringComparison.OrdinalIgnoreCase))
		{
			return "B";
		}
		if (string.Equals(gameResult, "Tie", StringComparison.OrdinalIgnoreCase))
		{
			return "T";
		}
		return gameResult ?? "";
	}

	private void AutoBetting_RoomGateChanged(object? sender, RoomGateChangedEventArgs e)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				AutoBetting_RoomGateChanged(sender, e);
			});
			return;
		}
		if (_gameIdToPanelMap.TryGetValue(e.TableId, out Panel panel))
		{
			ApplyRoomVisual(panel, e.TableId);
		}
		UpdateRoomSwitchSummary();
	}

	private void AutoBetting_ResultJudged(object? sender, AutoBettingResultEventArgs e)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				AutoBetting_ResultJudged(sender, e);
			});
			return;
		}
		if (e.IsSecondLogicRoomSwitched || IsRoomBetStepRolledBack(e.TableId, e.NextStep, e.NextMatrixRow, e.NextMatrixCol))
		{
			ClearRoomBetStep(e.TableId);
		}
		UpdateBetStatusRemark(e);
		if (_lastAppliedStepVersion.TryGetValue(e.TableId, out var lastVer) && e.StepVersion < lastVer)
		{
			FileLogger.Log($"[Form1] ResultJudged stale 무시: {e.TableId}, StepVersion={e.StepVersion} < last={lastVer}", "Warning", "UI");
			return;
		}
		_lastAppliedStepVersion[e.TableId] = e.StepVersion;
		bool num;
		if (!_autoBettingService.Context.IsMatrixStrategy)
		{
			num = e.NextStep == 1;
		}
		else
		{
			if (e.NextMatrixRow != 1)
			{
				goto IL_02a5;
			}
			num = e.NextMatrixCol == 1;
		}
		if (num && _options.ShoeControl && GetShoeControlThreshold() > 0)
		{
			int threshold = GetShoeControlThreshold();
			if (threshold > 0 && _gameHistories.TryGetValue(e.TableId, out List<GameResult> history))
			{
				int bigRoadCount;
				lock (history)
				{
					bigRoadCount = history.Count;
				}
				if (bigRoadCount >= threshold && _autoBettingService.Context.RoomStates.TryGetValue(e.TableId, out RoomBettingState roomState))
				{
					roomState.IsShoeControlBlocked = true;
					FileLogger.Log($"[Form1] 슈 컨트롤: {e.TableId} 베팅 차단 (빅로드={bigRoadCount}, 임계값={threshold})");
				}
			}
		}
		goto IL_02a5;
		IL_02a5:
		if (!e.IsWin && _options.StepAlarm && _options.StepAlarmIndex >= 0)
		{
			if (_autoBettingService.Context.IsMatrixStrategy)
			{
				int idx = _options.StepAlarmIndex;
				if (idx < _matrixAlarmCells.Count)
				{
					var (alarmRow, alarmCol) = _matrixAlarmCells[idx];
					if (e.NextMatrixRow == alarmRow && e.NextMatrixCol == alarmCol)
					{
						PlayAlarmRepeat(3);
					}
				}
			}
			else
			{
				int alarmStep = _options.StepAlarmIndex + 1;
				if (e.NextStep == alarmStep)
				{
					PlayAlarmRepeat(3);
				}
			}
		}
		UpdateBetStepLabel(e.TableId, e.NextStep);
		if (_autoBettingService.Context.IsVirtualMode || e.IsPeakLoweredSilently)
		{
			UpdateBalanceUI();
		}
	}

	private async void AutoBetting_PeakBalanceReset(object? sender, PeakResetEventArgs e)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				AutoBetting_PeakBalanceReset(sender, e);
			});
			return;
		}
		CancellationTokenSource oldCts = _pauseCountdownCts;
		if (oldCts != null)
		{
			oldCts.Cancel();
			oldCts.Dispose();
		}
		_pauseCountdownCts = new CancellationTokenSource();
		CancellationToken token = _pauseCountdownCts.Token;
		foreach (var (tableId, _) in _gameIdToPanelMap)
		{
			if (_autoBettingService.Context.RoomStates.TryGetValue(tableId, out RoomBettingState rs))
			{
				_lastAppliedStepVersion[tableId] = rs.StepVersion;
			}
		}
		ClearAllRoomBetSteps();
		// the peak no longer has a slot on screen — that row shows 이익 now, refreshed by UpdateBalanceUI
		UpdateBalanceUI();
		try
		{
			for (int i = e.PauseDurationSeconds; i > 0; i--)
			{
				token.ThrowIfCancellationRequested();
				UpdateAllRoomsPauseLabel(i);
				await Task.Delay(1000, token);
			}
			UpdateAllRoomsToStep1();
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async void AutoBetting_ProfitCooldownStarted(object? sender, ProfitCooldownEventArgs e)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				AutoBetting_ProfitCooldownStarted(sender, e);
			});
			return;
		}
		CancellationTokenSource oldCts = _pauseCountdownCts;
		if (oldCts != null)
		{
			oldCts.Cancel();
			oldCts.Dispose();
		}
		_pauseCountdownCts = new CancellationTokenSource();
		CancellationToken token = _pauseCountdownCts.Token;
		foreach (var (tableId, _) in _gameIdToPanelMap)
		{
			if (_autoBettingService.Context.RoomStates.TryGetValue(tableId, out RoomBettingState rs))
			{
				_lastAppliedStepVersion[tableId] = rs.StepVersion;
			}
		}
		ClearAllRoomBetSteps();
		try
		{
			for (int i = e.CooldownMinutes * 60; i > 0; i--)
			{
				token.ThrowIfCancellationRequested();
				UpdateAllRoomsCooldownLabel(i);
				await Task.Delay(1000, token);
			}
			UpdateAllRoomsToStep1();
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void AutoBetting_ProfitCooldownEnded(object? sender, ProfitCooldownEndedEventArgs e)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				AutoBetting_ProfitCooldownEnded(sender, e);
			});
			return;
		}
		CancellationTokenSource oldCts = _pauseCountdownCts;
		if (oldCts != null)
		{
			oldCts.Cancel();
			oldCts.Dispose();
			_pauseCountdownCts = null;
		}
		UpdateAllRoomsToStep1();
	}

	private async void AutoBetting_InactivityRefreshStarted(object? sender, InactivityRefreshEventArgs e)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				AutoBetting_InactivityRefreshStarted(sender, e);
			});
			return;
		}
		// [1단계: 알림+진단] 자동복구(새로고침)는 익스텐션 릴레이로 나간다. 보안소프트가 그 릴레이를
		// 막으면(익스텐션 미연결) 새로고침이 전송되지 않아 데이터/베팅이 영영 복구되지 않는데, 지금까지는
		// 조용한 Info 로그뿐이라 사용자가 몰랐다. 여기서 상태를 상세히 남기고 화면에 눈에 띄게 알린다.
		// (다음 단계에서 이 진단을 근거로 CDP 리로드 폴백 등 자동복구를 완성한다.)
		bool extRefreshOk = await _wsServer.SendRefreshCommandAsync();
		if (!extRefreshOk)
		{
			string lastBet = _autoBettingService.Context.LastGlobalBetTime.HasValue
				? _autoBettingService.Context.LastGlobalBetTime.Value.ToString("HH:mm:ss")
				: "없음";
			string diag = $"CDP연결={_cdpService.IsConnected}, 익스텐션연결={_wsServer.IsExtensionConnected}, "
				+ $"IsRunning={_autoBettingService.IsRunning}, 마지막베팅={lastBet}, "
				+ $"무베팅경과={(_autoBettingService.Context.LastGlobalBetTime.HasValue ? $"{(DateTime.Now - _autoBettingService.Context.LastGlobalBetTime.Value).TotalMinutes:F1}분" : "?")}";
			// [2단계 자동복구] 익스텐션 릴레이가 막혀 새로고침이 안 나갔다 → CDP 로 게임 페이지를 직접
			// 리로드해 데이터 파이프라인을 되살린다(CDP 는 익스텐션과 무관하게 연결돼 있음). 분할세션이
			// 이미 쓰는 폴백을 메인세션에도 적용한다.
			bool cdpReloadOk = false;
			try
			{
				cdpReloadOk = await _cdpService.ReloadGamePagesAsync();
			}
			catch (Exception exReload)
			{
				FileLogger.Log("[Form1][자동복구] CDP 리로드 예외: " + exReload.Message, "Warning", "Refresh");
			}
			if (cdpReloadOk)
			{
				FileLogger.Log($"[Form1][자동복구] 익스텐션 미연결 → CDP 게임페이지 리로드로 복구 시도. 상태: {diag}", "Warning", "Refresh");
				SetBetStatusRemark("자동복구: CDP로 페이지 새로고침 중…", Color.Orange);
			}
			else
			{
				FileLogger.Log($"[Form1][자동복구실패] 새로고침 실패 — 익스텐션 미연결 + CDP 리로드도 실패. 데이터·베팅이 복구되지 않습니다. 상태: {diag}", "Error", "Refresh");
				SetBetStatusRemark("⚠ 자동복구 실패(익스텐션·CDP 모두) — 수동 새로고침 필요", Color.Red);
			}
		}
		else
		{
			FileLogger.Log("[Form1][자동복구] 새로고침 명령 전송 성공(익스텐션)", "Info", "Refresh");
		}
		if (IsSplitEnabled)
		{
			foreach (PragmaticSiteSession session in _siteManager.Sessions)
			{
				if (!(await session.WsServer.SendRefreshCommandAsync()))
				{
					await session.Cdp.ReloadGamePagesAsync();
				}
			}
		}
		for (int i = e.RefreshWaitSeconds; i > 0; i--)
		{
			UpdateAllRoomsRefreshLabel(i);
			await Task.Delay(1000);
		}
	}

	private void AutoBetting_RefreshCompleted(object? sender, EventArgs e)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				AutoBetting_RefreshCompleted(sender, e);
			});
		}
		else
		{
			UpdateAllRoomsToStep1();
		}
	}

	private void AutoBetting_PendingBetAmountChanged(decimal pendingAmount)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				AutoBetting_PendingBetAmountChanged(pendingAmount);
			});
			return;
		}
		string newText = ((pendingAmount > 0m) ? $"(-{pendingAmount:N0})" : "0");
		if (lblPendingBetValue.Text != newText)
		{
			lblPendingBetValue.Text = newText;
			lblPendingBetValue.ForeColor = ((pendingAmount > 0m) ? Color.Orange : Color.FromArgb(234, 234, 234));
		}
		UpdateBalanceUI();
	}

	private void AutoBetting_KeepAliveBetExecuted(object? sender, KeepAliveBetEventArgs e)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				AutoBetting_KeepAliveBetExecuted(sender, e);
			});
		}
		else
		{
			UpdateKeepAliveUI();
		}
	}

	private void UpdateKeepAliveUI()
	{
		AutoBettingContext context = _autoBettingService.Context;
		string text;
		if (!context.IsKeepAliveBetEnabled)
		{
			text = "-";
		}
		else if (context.KeepAliveBetCount == 0 && context.KeepAliveFailCount == 0)
		{
			text = "대기 중";
		}
		else
		{
			text = $"{context.KeepAliveBetCount}건 / 순지출 {context.KeepAliveNetSpend:N0}";
			if (context.KeepAliveFailCount > 0)
			{
				text += $" (실패 {context.KeepAliveFailCount})";
			}
		}
		if (lblKeepAliveValue.Text != text)
		{
			lblKeepAliveValue.Text = text;
		}
	}

	private void BettingService_BetAmountConfirmed(object? sender, decimal totalAmount)
	{
		FileLogger.Log($"[Balance] PP 서버 베팅 확인: {totalAmount:N0}원");
	}

	private void BettingService_LateConfirmationReceived(object? sender, decimal amount)
	{
		_autoBettingService.OnLateConfirmationReceived();
	}

	private void BtnExit_Click(object? sender, EventArgs e)
	{
		Close();
	}

	private void BtnFormatManage_Click(object? sender, EventArgs e)
	{
		AutoBettingContext context = _autoBettingService.Context;
		using FormatSettingForm form = new FormatSettingForm(_currentUserId, context.IsPbCrossEnabled, context.IsBpCrossOnTwoLossEnabled);
		if (form.ShowDialog(this) == DialogResult.OK)
		{
			_autoBettingService.SetPbCrossEnabled(form.PbCrossEnabled, form.BpCrossOnTwoLossEnabled);
		}
	}

	private void BtnPriorityManage_Click(object? sender, EventArgs e)
	{
		using BetAmountSettingForm form = new BetAmountSettingForm(_currentUserId);
		form.ShowDialog(this);
	}

	private void SfButton1_Click(object? sender, EventArgs e)
	{
		using LimitSettingForm form2 = new LimitSettingForm(_currentUserId);
		form2.ShowDialog(this);
	}

	/// <summary>전략 체인 설정 창을 연다(설정 메뉴의 '체인설정' 버튼 전용).</summary>
	private void OpenStrategyChainSettings()
	{
		ApiClient api = AuthService.Api;
		if (api == null)
		{
			return;
		}
		using StrategyChainSettingForm form = new StrategyChainSettingForm(api);
		form.ShowDialog(this);
	}

	private void BtnExceptionFormat_Click(object? sender, EventArgs e)
	{
		using ExceptionFormatSettingForm form = new ExceptionFormatSettingForm(_currentUserId);
		form.ShowDialog(this);
	}

	private async Task ApplyRoomGateFormatChangeAsync()
	{
		if (_autoBettingService.IsRunning)
		{
			await _autoBettingService.ReloadRoomGateFormatsAsync();
			WarnIfRoomGateUnavailable();
			RefreshAllRoomVisuals();
		}
	}

	private void BtnRoomCooldown_Click(object? sender, EventArgs e)
	{
		using RoomCooldownSettingForm form = new RoomCooldownSettingForm(_currentUserId);
		form.ShowDialog(this);
	}

	private void BtnSecondLogic_Click(object? sender, EventArgs e)
	{
		using SecondLogicSettingForm form = new SecondLogicSettingForm(_currentUserId);
		form.ShowDialog(this);
	}

	private void BtnMatrixStats_Click(object? sender, EventArgs e)
	{
		using MatrixStatsForm form = new MatrixStatsForm();
		form.ShowDialog(this);
	}

	private void BtnMatrixReset_Click(object? sender, EventArgs e)
	{
		if (MessageBox.Show(this, "모든 방의 베팅 진행 상태를 1차 1단계로 초기화합니다.\n\n포함되는 항목:\n  · 행렬 (차수/단계)\n  · 마틴/달랑멘징/홍콩크루즈/오스카 진행도\n  · 결과 대기 / 진행 중 베팅금\n  · 연패 카운터 등 방별 누적 상태\n  · 선현재금액(보관금액) → 현재금액으로 하향\n\n정말 초기화하시겠습니까?", "행렬 수동 초기화 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
		{
			return;
		}
		try
		{
			_autoBettingService.ResetAllRoomsToFirstStep();
			UpdateBalanceUI();
			MessageBox.Show(this, $"모든 방을 1차 1단계로 초기화했습니다.\n선현재금액: {_autoBettingService.Context.PeakBalance:N0}원", "행렬 수동 초기화 완료", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Form1] 행렬 수동 초기화 실패: " + ex.Message, "Error", "UI");
			MessageBox.Show(this, "초기화 중 오류가 발생했습니다.\n\n" + ex.Message, "행렬 수동 초기화 실패", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private async void BtnConnect_Click(object? sender, EventArgs e)
	{
		_ = 4;
		// 열 사이트를 먼저 묻는다. 취소하면 아무것도 하지 않고, 비워 두면 예전처럼 연결만 한다.
		string siteUrl;
		using (SiteUrlPromptForm prompt = new SiteUrlPromptForm())
		{
			if (prompt.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			siteUrl = prompt.SiteUrl;
		}
		try
		{
			btnConnect.Enabled = false;
			lblStatus.Text = "Chromium 준비 중...";
			lblStatus.ForeColor = Color.Orange;
			Progress<string> progress = new Progress<string>(delegate(string msg)
			{
				if (lblStatus.InvokeRequired)
				{
					lblStatus.Invoke(() => lblStatus.Text = msg);
				}
				else
				{
					lblStatus.Text = msg;
				}
				FileLogger.Log("[Form1] " + msg, "Info", "Init");
			});
			if (!(await ChromeShortcutHelper.EnsureChromeRunningAsync(20, progress)))
			{
				FileLogger.Log($"[Form1] Chromium 기동 실패 ({ChromeShortcutHelper.LastFailure})", "Warning", "Init");
				lblStatus.Text = "Chromium 기동 실패";
				lblStatus.ForeColor = Color.Red;
				MessageBox.Show(DescribeChromeFailure(), "Chromium 기동 실패", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				btnConnect.Enabled = true;
				return;
			}
			lblStatus.Text = "연결 중...";
			int debugPort = ChromeShortcutHelper.CurrentDebugPort;
			try
			{
				await _cdpService.ConnectAsync(debugPort);
				FileLogger.Log($"[Form1] CDP 연결 성공 (포트 {debugPort})");
			}
			catch (Exception ex)
			{
				FileLogger.Log("[Form1] CDP 연결 실패 (Extension으로 대체): " + ex.Message, "Warning", "Init");
			}
			await _wsServer.StartAsync();
			if (_wsServer.IsListening)
			{
				FileLogger.Log($"[Form1] WebSocket 서버 시작됨 (포트: {_wsServer.ServerPort})");
				await LoadGameDomainsAsync();
			}
			// CDP 와 릴레이 서버가 준비된 뒤에 연다. 먼저 열면 페이지가 우리 훅보다 앞서 소켓을 만든다.
			if (!string.IsNullOrEmpty(siteUrl))
			{
				lblStatus.Text = "사이트 여는 중...";
				if (!await ChromeShortcutHelper.OpenUrlAsync(debugPort, siteUrl))
				{
					MessageBox.Show("사이트를 열지 못했습니다. Chrome에서 직접 주소를 입력해 주세요." + Environment.NewLine + Environment.NewLine + siteUrl,
						"사이트 열기 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			}
			if (IsSplitEnabled)
			{
				List<int> auxSlots = (from s in GetSlotsForSplitCount(_splitCount)
					where s > 0
					select s).ToList();
				if (auxSlots.Count > 0)
				{
					lblStatus.Text = $"보조 사이트 {auxSlots.Count}곳 준비 중...";
					if (!(await _siteManager.ConnectSitesAsync(auxSlots, ChromeShortcutHelper.CurrentDebugPort, progress)))
					{
						FileLogger.Log("[Form1] 일부 보조 사이트 연결 실패 — 준비된 사이트만으로 분할됩니다.", "Warning", "MultiSite");
					}
				}
			}
			if (_cdpService.IsConnected)
			{
				UpdateConnectionStatus(isConnected: true);
			}
			else if (!_wsServer.IsExtensionConnected)
			{
				lblStatus.Text = "대기 중... (CDP/Extension 연결 대기)";
				lblStatus.ForeColor = Color.Orange;
				btnConnect.Enabled = false;
				btnDisconnect.Enabled = true;
			}
		}
		catch (Exception ex2)
		{
			FileLogger.Log("[Form1] 연결 시작 실패: " + ex2.Message, "Error", "Init", ex2.StackTrace);
			MessageBox.Show("연결 실패:\n" + ex2.Message + "\n\n확인 사항:\n1. 이전 PragmaticBot 프로세스가 완전히 종료되었는지 확인\n   (작업 관리자에서 PragmaticBot 프로세스 확인)\n2. 포트 충돌 확인: netstat -an | findstr 29876\n3. Windows 예약 포트 확인: netsh interface ipv4 show excludedportrange protocol=tcp\n" + $"4. Chromium이 --remote-debugging-port={ChromeShortcutHelper.CurrentDebugPort} 플래그로 시작했는지 확인", "연결 오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			UpdateConnectionStatus(isConnected: false);
		}
	}

	private async void BtnDisconnect_Click(object? sender, EventArgs e)
	{
		_ = 2;
		try
		{
			btnDisconnect.Enabled = false;
			lblStatus.Text = "연결 해제 중...";
			lblStatus.ForeColor = Color.Orange;
			await _cdpService.DisconnectAsync();
			await _wsServer.StopAsync();
			await _siteManager.DisconnectAllAsync();
			UpdateConnectionStatus(isConnected: false);
		}
		catch (Exception ex)
		{
			MessageBox.Show("연결 해제 실패: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			UpdateConnectionStatus(_cdpService.IsConnected || _wsServer.IsExtensionConnected);
		}
	}

	private async void BtnOptionSetting_Click(object? sender, EventArgs e)
	{
		BotOptionState before = _options;
		using OptionSettingForm form = new OptionSettingForm(before, _betStepItems, _alarmStepItems, ShoeControlThresholds, _autoBettingService.IsRunning, _autoBettingService.RoomGateUnavailableReason, _currentUserId);
		DialogResult result = form.ShowDialog(this);
		if (form.RoomGateFormatChanged)
		{
			await ApplyRoomGateFormatChangeAsync();
		}
		if (result == DialogResult.OK)
		{
			_options = form.Result;
			await ApplyOptionsAsync(before, _options);
			SaveOptions(_options);
		}
	}

	private async Task ApplyOptionsAsync(BotOptionState before, BotOptionState after)
	{
		AutoBettingContext context = _autoBettingService.Context;
		context.IsPeakBalanceResetEnabled = after.PeakReset;
		context.IsIgnorePendingBetEnabled = after.IgnorePendingBet;
		context.IsVirtualMode = after.VirtualBet;
		UpdateStepResetSetting();
		UpdateVirtualBadge();
		if (before.RoomGate != after.RoomGate)
		{
			ApplyRoomGateOption(after.RoomGate);
		}
		if (before.SplitCount != after.SplitCount)
		{
			await ApplySplitCountAsync(after.SplitCount);
		}
	}

	private void SaveOptions(BotOptionState o)
	{
		UiPreferenceService.SetBool(_currentUserId, "peakResetEnabled", o.PeakReset);
		UiPreferenceService.SetBool(_currentUserId, "stepResetEnabled", o.StepReset);
		UiPreferenceService.SetInt(_currentUserId, "stepResetIndex", o.StepResetIndex);
		UiPreferenceService.SetBool(_currentUserId, "roomGateEnabled", o.RoomGate);
		UiPreferenceService.SetBool(_currentUserId, "virtualBetEnabled", o.VirtualBet);
		UiPreferenceService.SetBool(_currentUserId, "ignorePendingBetEnabled", o.IgnorePendingBet);
		UiPreferenceService.SetBool(_currentUserId, "shoeControlEnabled", o.ShoeControl);
		UiPreferenceService.SetInt(_currentUserId, "shoeControlIndex", o.ShoeControlIndex);
		UiPreferenceService.SetBool(_currentUserId, "stepAlarmEnabled", o.StepAlarm);
		UiPreferenceService.SetInt(_currentUserId, "stepAlarmIndex", o.StepAlarmIndex);
		UiPreferenceService.SetInt(_currentUserId, "betSplitCount", o.SplitCount);
	}

	private void LoadSavedOptions()
	{
		_options = new BotOptionState
		{
			PeakReset = UiPreferenceService.GetBool(_currentUserId, "peakResetEnabled"),
			StepReset = UiPreferenceService.GetBool(_currentUserId, "stepResetEnabled"),
			StepResetIndex = ClampIndex(UiPreferenceService.GetInt(_currentUserId, "stepResetIndex"), _betStepItems.Count),
			RoomGate = UiPreferenceService.GetBool(_currentUserId, "roomGateEnabled"),
			VirtualBet = UiPreferenceService.GetBool(_currentUserId, "virtualBetEnabled"),
			IgnorePendingBet = UiPreferenceService.GetBool(_currentUserId, "ignorePendingBetEnabled"),
			ShoeControl = UiPreferenceService.GetBool(_currentUserId, "shoeControlEnabled"),
			ShoeControlIndex = ClampIndex(UiPreferenceService.GetInt(_currentUserId, "shoeControlIndex"), ShoeControlThresholds.Length),
			StepAlarm = UiPreferenceService.GetBool(_currentUserId, "stepAlarmEnabled"),
			StepAlarmIndex = ClampIndex(UiPreferenceService.GetInt(_currentUserId, "stepAlarmIndex"), _alarmStepItems.Count),
			SplitCount = Math.Max(1, UiPreferenceService.GetInt(_currentUserId, "betSplitCount", 1))
		};
		AutoBettingContext context = _autoBettingService.Context;
		context.IsPeakBalanceResetEnabled = _options.PeakReset;
		context.IsIgnorePendingBetEnabled = _options.IgnorePendingBet;
		context.IsVirtualMode = _options.VirtualBet;
		context.IsRoomGateEnabled = _options.RoomGate;
		_splitCount = _options.SplitCount;
		UpdateStepResetSetting();
		UpdateVirtualBadge();
		FileLogger.Log($"[Form1] 화면 옵션 복원: 모도초기화={_options.PeakReset}, 미당첨초기화={_options.StepReset}, 방게이트={_options.RoomGate}, 가상베팅={_options.VirtualBet}, 분할={_options.SplitCount}곳", "Info", "UI");
	}

	private static int ClampIndex(int index, int count)
	{
		if (count <= 0)
		{
			return -1;
		}
		if (index < 0 || index >= count)
		{
			return 0;
		}
		return index;
	}

	private void UpdateVirtualBadge()
	{
		lblVirtualBadge.Visible = _options.VirtualBet;
	}

	private void UpdateStepResetSetting()
	{
		_autoBettingService.Context.IsStepResetEnabled = _options.StepReset;
		int index = _options.StepResetIndex;
		if (index >= 0 && index < _betStepItems.Count && int.TryParse(_betStepItems[index].Replace("단계", ""), out var step))
		{
			_autoBettingService.Context.StepResetTargetStep = step;
		}
	}

	private int GetShoeControlThreshold()
	{
		int index = _options.ShoeControlIndex;
		if (index < 0 || index >= ShoeControlThresholds.Length)
		{
			return 0;
		}
		if (!int.TryParse(ShoeControlThresholds[index], out var threshold))
		{
			return 0;
		}
		return threshold;
	}

	private async void BtnGameStart_Click(object? sender, EventArgs e)
	{
		await StartBettingAsync(false, 0m);
	}

	private async void BtnVirtualBet_Click(object? sender, EventArgs e)
	{
		if (_autoBettingService.IsRunning)
		{
			MessageBox.Show("이미 실행 중입니다. 중지 후 다시 시작하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		using VirtualStartPromptForm prompt = new VirtualStartPromptForm(_virtualStartAmount);
		if (prompt.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		_virtualStartAmount = prompt.Amount;
		await StartBettingAsync(true, _virtualStartAmount);
	}

	/// <summary>게임시작(실베팅)·가상베팅 공통 시작 경로. isVirtual=true면 실제 전송 없이 가상으로만 돈다.</summary>
	private async Task StartBettingAsync(bool isVirtual, decimal virtualStartAmount)
	{
		// 실/가상 모드는 누른 버튼이 결정한다(옵션 체크박스보다 우선).
		_autoBettingService.Context.IsVirtualMode = isVirtual;
		_options.VirtualBet = isVirtual;
		if (lblVirtualBadge != null) { lblVirtualBadge.Visible = isVirtual; }

		if (!_cdpService.IsConnected && !_wsServer.IsExtensionConnected)
		{
			MessageBox.Show("Chrome이 연결되지 않았습니다.\n\n" + $"Chrome 연결 버튼을 클릭하여 먼저 크롬에 연결해주세요", "Chrome 미연결", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		if (!_wsServer.IsWebSocketCaptured)
		{
			MessageBox.Show("WebSocket이 캡처되지 않았습니다.\n\n게임 페이지를 새로고침하거나\n게임에 입장하여 WebSocket이 연결되도록 하세요.", "WebSocket 미캡처", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		_lastAppliedStepVersion.Clear();
		ClearAllRoomBetSteps();
		if (_autoBettingService.Context.IsVirtualMode)
		{
			decimal vStart = (virtualStartAmount > 0m) ? virtualStartAmount : 1000000m;
			Interlocked.Exchange(ref _startAmountCaptured, 1);
			_startAmount = vStart;
			_autoBettingService.Context.StartBalance = vStart;
			_autoBettingService.Context.CurrentBalance = vStart;
			_autoBettingService.Context.PeakBalance = vStart;
			lock (_balanceUiLock)
			{
				_lastHttpBalance = vStart;
				_lastDomBalance = vStart;
			}
			FileLogger.Log($"[Form1] 가상 모드 시작잔액: {vStart:N0}원 (사용자 지정)");
		}
		else
		{
			btnGameStart.Enabled = false;
			btnGameStart.Text = "잔액 조회 중...";
			try
			{
				TaskCompletionSource<decimal> balanceTcs = new TaskCompletionSource<decimal>();
				_wsServer.HttpBalanceReceived += onHttpBalance;
				try
				{
					// The extension is the quickest route to a fresh balance, not the only one —
					// the CDP wallet poll already tracks it. A missing extension is therefore a
					// slower path rather than a dead end, so environments that block the
					// extension's loopback socket can still start a game.
					bool extensionAsked = await _wsServer.SendFetchBalanceAsync();
					if (!extensionAsked)
					{
						btnGameStart.Text = "잔액 조회 중 (CDP)...";
						FileLogger.Log("[Form1] Extension 미연결 → CDP 지갑 경로로 시작 잔액 조회", "Warning", "Init");
					}
					decimal balance;
					bool authoritative = true;
					bool fromExtension = extensionAsked
						&& await Task.WhenAny(balanceTcs.Task, Task.Delay(5000)) == balanceTcs.Task;
					if (fromExtension)
					{
						balance = balanceTcs.Task.Result;
						FileLogger.Log($"[Form1] FETCH_BALANCE 수신: {balance:N0}원");
					}
					else
					{
						// 지갑(권위값)을 넉넉히 기다린다. 이 사이트는 지갑 iframe attach 가 늦어 확정까지
						// 시간이 걸리는데, 여기서 성급히 화면 숫자(포털의 가짜 100만원)로 떨어지면 그게
						// 시작 금액으로 굳는다. 12초 → 25초로 늘려 지갑이 먼저 확정될 여지를 준다.
						(balance, authoritative) = await WaitForCdpBalanceAsync(TimeSpan.FromSeconds(25));
						FileLogger.Log($"[Form1] CDP 잔액으로 시작: {balance:N0}원 (Extension {(extensionAsked ? "응답없음" : "미연결")}, 출처={(authoritative ? "지갑" : (balance > 0m ? "화면표시" : "미확정"))})", "Warning", "Init");
					}
					if (balance <= 0m)
					{
						// 지갑 미확정 + 화면값도 가짜(라운드 100만배수)라 거부됨 → 가짜값을 시작금액으로
						// 굳히지 않는다. _startAmountCaptured 를 0 으로 두면, 곧 도착할 지갑 권위값이
						// 시작 금액을 확정한다(ProcessBalanceUpdate). 그때까지 베팅은 보류(가용=0).
						FileLogger.Log("[Form1] 시작 잔액 미확정 — 가짜값 미사용, 지갑 권위값 도착 시 시작금액 자동 확정(그때까지 베팅 보류)", "Warning", "Init");
						SetBetStatusRemark("지갑 확인 대기 중 — 잔액 확정 후 베팅 시작", Color.Orange);
					}
					else
					{
						_startAmountAuthoritative = authoritative;
						if (!authoritative)
						{
							FileLogger.Log($"[Form1] ⚠ 시작금액을 화면 표시값({balance:N0})으로 잠정 설정 — 지갑 조회 실패. 지갑값이 들어오면 자동 보정합니다.", "Warning", "Init");
							SetBetStatusRemark($"시작금액 잠정 {balance:N0}원 (지갑 확인 대기 중)", Color.Orange);
						}
						Interlocked.Exchange(ref _startAmountCaptured, 1);
						_startAmount = balance;
						_autoBettingService.Context.StartBalance = balance;
						_autoBettingService.Context.CurrentBalance = balance;
						_autoBettingService.Context.PeakBalance = balance;
						lock (_balanceUiLock)
						{
							_lastHttpBalance = balance;
							_lastDomBalance = balance;
						}
						FileLogger.Log($"[Form1] 초기 캡처 완료: Start={balance:N0}, Peak={balance:N0}, HttpRef={balance:N0}", "Info", DbLogService.SessionCategory);
					}
				}
				finally
				{
					_wsServer.HttpBalanceReceived -= onHttpBalance;
				}
				void onHttpBalance(object? s, decimal bal)
				{
					if (bal > 0m)
					{
						balanceTcs.TrySetResult(bal);
					}
				}
			}
			finally
			{
				btnGameStart.Enabled = true;
				btnGameStart.Text = "게임시작";
			}
		}
		_autoBettingService.Context.IsPeakBalanceResetEnabled = _options.PeakReset;
		_autoBettingService.Context.IsStepResetEnabled = _options.StepReset;
		_autoBettingService.Context.IsRoomGateEnabled = _options.RoomGate;
		FileLogger.Log($"[Form1] 시작 시 옵션 동기화: PeakReset={_options.PeakReset}, StepReset={_options.StepReset}, 방게이트={_options.RoomGate}");
		_appliedWinRounds.Clear();
		_appliedWinOrder.Clear();
		FileLogger.Log("[WinBalance] StartAsync: 반영 완료 라운드 목록 초기화");
		_lastShoeTotalGames.Clear();
		int selectedBetType = ((cmbBetAmountType.SelectedItem?.ToString() == "전략 체인") ? (-1) : GetSelectedBetAmountType());
		await _autoBettingService.StartAsync(_currentUserId, selectedBetType);
		if (_autoBettingService.IsRunning)
		{
			_timerStartTime = DateTime.Now;
			_pausedElapsed = TimeSpan.Zero;
			_elapsedTimer?.Start();
			_balanceSyncTimer?.Start();
			Task.Run(async delegate
			{
				try
				{
					await _cdpService.EnsureNoCommissionOffAsync();
				}
				catch (Exception ex)
				{
					FileLogger.Log("[Form1] 시작 시 노커미션 토글 확인 실패: " + ex.Message, "Warning", "Betting");
				}
			});
		}
		UpdateKeepAliveUI();
		_roomGateWatchdogTicks = 0;
		WarnIfRoomGateUnavailable();
		RefreshAllRoomVisuals();
	}

	private async void BtnPause_Click(object? sender, EventArgs e)
	{
		if (_autoBettingService.IsRunning)
		{
			_elapsedTimer?.Stop();
			_balanceSyncTimer?.Stop();
			await _autoBettingService.StopAsync();
			MessageBox.Show("자동 베팅이 일시정지되었습니다.", "일시정지", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		else
		{
			MessageBox.Show("자동 베팅이 실행 중이 아닙니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
	}

	private async void BtnStopBet_Click(object? sender, EventArgs e)
	{
		if (_autoBettingService.IsRunning)
		{
			_elapsedTimer?.Stop();
			_balanceSyncTimer?.Stop();
			lblTimer.Text = "00:00:00";
			await _autoBettingService.StopAsync();
			MessageBox.Show("자동 베팅이 중지되었습니다.", "중지", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		else
		{
			MessageBox.Show("자동 베팅이 실행 중이 아닙니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
	}

	private void ElapsedTimer_Tick(object? sender, EventArgs e)
	{
		TimeSpan elapsed = DateTime.Now - _timerStartTime + _pausedElapsed;
		lblTimer.Text = elapsed.ToString("hh\\:mm\\:ss");
		UpdateGameProviderLabel();
		UpdateKeepAliveUI();
		if (++_roomGateWatchdogTicks >= 60)
		{
			_roomGateWatchdogTicks = 0;
			RunRoomGateWatchdog();
		}
	}

	private void RunRoomGateWatchdog()
	{
		IReadOnlyList<string> stale = _autoBettingService.CheckRoomGateWatchdog();
		if (stale.Count == 0)
		{
			return;
		}
		foreach (string tableId in stale)
		{
			if (_gameIdToPanelMap.TryGetValue(tableId, out Panel panel))
			{
				ApplyRoomVisual(panel, tableId);
			}
		}
		UpdateRoomSwitchSummary();
	}

	private async void BalanceSyncTimer_Tick(object? sender, EventArgs e)
	{
		try
		{
			await _cdpService.EnsureNoCommissionOffAsync();
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Form1] 노커미션 토글 확인 실패: " + ex.Message, "Warning", "Betting");
		}
		if (!_autoBettingService.IsRunning || _autoBettingService.Context.IsVirtualMode)
		{
			return;
		}
		try
		{
			await _wsServer.SendFetchBalanceAsync();
		}
		catch (Exception ex2)
		{
			FileLogger.Log("[Form1] 주기 FETCH_BALANCE 실패: " + ex2.Message, "Warning", "Balance");
		}
	}

	private async void NoticePollingTimer_Tick(object? sender, EventArgs e)
	{
		await PollNoticeAsync();
	}

	private async Task PollNoticeAsync()
	{
		try
		{
			ApiClient api = AuthService.Api;
			if (api == null)
			{
				return;
			}
			ActiveNoticeResponse response = await api.GetActiveNoticeAsync();
			if ((object)response != null && response.Success && (object)response.Notice != null)
			{
				if (response.Notice.Id != _lastNoticeId)
				{
					_lastNoticeId = response.Notice.Id;
					ShowNotice(response.Notice.Message);
				}
			}
			else if (_lastNoticeId != 0L)
			{
				_lastNoticeId = 0L;
				StopNoticeMarquee();
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Notice] 폴링 실패: " + ex.Message, "Error", "Notice", ex.StackTrace);
		}
	}

	private void ShowNotice(string message)
	{
		if (base.InvokeRequired)
		{
			Invoke(delegate
			{
				ShowNotice(message);
			});
			return;
		}
		panelNotice.Visible = true;
		// lblNoticeText is added to the panel first, which puts it at the FRONT of the z-order, so
		// the scrolling text was painting over the NOTICE badge. Pull the badge up and the text
		// slides underneath it instead.
		lblNoticeIcon.BringToFront();
		lblNoticeText.Text = message;
		lblNoticeText.ForeColor = Color.FromArgb(255, 220, 80);
		_marqueeOffset = panelNotice.Width;
		lblNoticeText.Location = new Point(_marqueeOffset, 4);
		_noticeMarqueeTimer?.Start();
	}

	private void StopNoticeMarquee()
	{
		if (base.InvokeRequired)
		{
			Invoke(StopNoticeMarquee);
			return;
		}
		_noticeMarqueeTimer?.Stop();
		lblNoticeText.Text = "";
		panelNotice.Visible = false;
	}

	private void NoticeMarqueeTimer_Tick(object? sender, EventArgs e)
	{
		_marqueeOffset -= 2;
		if (_marqueeOffset < -lblNoticeText.Width)
		{
			_marqueeOffset = panelNotice.Width;
		}
		lblNoticeText.Location = new Point(_marqueeOffset, 4);
	}

	/// <summary>
	/// Turns the helper's failure reason into advice that matches what actually went wrong.
	/// The debugging-port hint is useless when the browser could never be downloaded at all.
	/// </summary>
	private static string DescribeChromeFailure()
	{
		string? detail = ChromeShortcutHelper.LastFailureDetail;
		const string tail = "\n\n자세한 원인은 Logs 폴더의 로그를 확인해주세요.";
		return ChromeShortcutHelper.LastFailure switch
		{
			ChromeShortcutHelper.StartFailure.BrowserUnavailable =>
				"Chromium을 준비하지 못했습니다.\n\n번들 Chromium을 내려받거나 압축을 푸는 데 실패했고, 설치된 Chrome도 찾지 못했습니다.\n\n확인 사항:\n1. 인터넷 연결 (약 180MB를 내려받습니다)\n2. C 드라이브 여유 공간 (1GB 이상)\n3. 백신이 다운로드 폴더를 차단하는지\n\n"
				+ ChromeShortcutHelper.BundleRootDir + " 폴더를 지우고 다시 시도하면 새로 내려받습니다." + tail,

			ChromeShortcutHelper.StartFailure.NoDebugPort =>
				"디버깅 포트를 확보하지 못했습니다.\n\n후보 포트: " + (detail ?? "-")
				+ "\n\n대개 Windows 예약 대역에 걸린 경우입니다. 관리자 명령 프롬프트에서 아래를 실행해 확인하세요.\n\n  netsh interface ipv4 show excludedportrange protocol=tcp" + tail,

			ChromeShortcutHelper.StartFailure.ProcessStartFailed =>
				"Chromium 실행 파일을 시작하지 못했습니다.\n\n" + (detail ?? "")
				+ "\n\n백신이 실행을 막았거나 파일이 손상되었을 수 있습니다." + tail,

			ChromeShortcutHelper.StartFailure.DebugPortSilent =>
				"Chromium은 시작했지만 디버깅 포트 " + (detail ?? "-") + " 가 응답하지 않습니다."
				+ "\n\n다른 프로필로 실행 중인 Chrome이 있으면 모두 종료한 뒤 다시 시도하세요." + tail,

			_ => "Chromium을 기동하지 못했습니다." + tail
		};
	}

	/// <summary>Provider whose tables 방 설정 is showing; null = every provider the account holds.</summary>
	private string? _roomProviderFilter;

	private Panel? _roomProviderTabs;

	private async void BtnRoomManage_Click(object? sender, EventArgs e)
	{
		panelPlayRoomContainer.Visible = false;
		panelRoomSettings.Visible = true;
		EnsureRoomProviderTabs();
		int tabsHeight = (_roomProviderTabs != null && _roomProviderTabs.Visible) ? _roomProviderTabs.Height + 6 : 0;
		flowPanelGames.Top = 85 + tabsHeight;
		flowPanelGames.Width = panelRoomSettings.ClientSize.Width - 26;
		flowPanelGames.Height = panelRoomSettings.ClientSize.Height - 100 - tabsHeight;
		await LoadGameListAsync();
	}

	/// <summary>
	/// Tab strip above the table list. An account allowed both providers gets both catalogues mixed
	/// together, which is unusable — these split them. Hidden when only one provider is granted.
	/// </summary>
	private void EnsureRoomProviderTabs()
	{
		if (_roomProviderTabs != null)
		{
			return;
		}
		_roomProviderTabs = new Panel
		{
			Name = "panelRoomProviderTabs",
			Location = new Point(13, 82),
			Size = new Size(panelRoomSettings.ClientSize.Width - 26, 34),
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			BackColor = Color.Transparent
		};
		panelRoomSettings.Controls.Add(_roomProviderTabs);
		_roomProviderTabs.BringToFront();
		BuildRoomProviderTabs();
	}

	private void BuildRoomProviderTabs()
	{
		if (_roomProviderTabs == null)
		{
			return;
		}
		GameProviders granted = AuthService.CurrentProviders;
		List<(string? Key, string Label)> tabs = new List<(string?, string)>();
		if (granted == GameProviders.All)
		{
			tabs.Add((null, "전체"));
		}
		if (granted.HasFlag(GameProviders.Evolution))
		{
			tabs.Add((GameProviderNames.EvolutionKey, "에볼루션"));
		}
		if (granted.HasFlag(GameProviders.PragmaticPlay))
		{
			tabs.Add((GameProviderNames.PragmaticPlayKey, "프라그마틱"));
		}

		// one provider means nothing to switch between
		_roomProviderTabs.Visible = tabs.Count > 1;
		_roomProviderTabs.Controls.Clear();
		int x = 0;
		foreach (var (key, label) in tabs)
		{
			Button tab = new Button
			{
				Text = label,
				Tag = key,
				Name = "roomTab",
				Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold),
				FlatStyle = FlatStyle.Flat,
				Size = new Size(110, 30),
				Location = new Point(x, 2),
				Cursor = Cursors.Hand,
				TabStop = false
			};
			tab.FlatAppearance.BorderSize = 0;
			tab.Click += RoomProviderTab_Click;
			_roomProviderTabs.Controls.Add(tab);
			x += tab.Width + 4;
		}
		ApplyRoomProviderTabVisual();
	}

	private void ApplyRoomProviderTabVisual()
	{
		if (_roomProviderTabs == null)
		{
			return;
		}
		foreach (Button tab in _roomProviderTabs.Controls.OfType<Button>())
		{
			bool active = (tab.Tag as string) == _roomProviderFilter;
			tab.BackColor = active ? Color.FromArgb(40, 90, 160) : Color.FromArgb(30, 38, 66);
			tab.ForeColor = active ? Color.White : Color.FromArgb(150, 160, 190);
		}
	}

	private void RoomProviderTab_Click(object? sender, EventArgs e)
	{
		if (sender is Button { Tag: var tag })
		{
			_roomProviderFilter = tag as string;
			ApplyRoomProviderTabVisual();
			UpdateGameCheckboxes();
		}
	}

	private async void BtnPlayRoom_Click(object? sender, EventArgs e)
	{
		await SaveAllSelectionsAsync();
		await CreatePlayRoomPanelsAsync();
		panelRoomSettings.Visible = false;
		panelPlayRoomContainer.Visible = true;
	}

	private async Task LoadGameListAsync()
	{
		if (_isLoadingGames)
		{
			return;
		}
		_isLoadingGames = true;
		try
		{
			ApiClient api = AuthService.Api;
			if (api != null)
			{
				_baccaratGames = await api.GetBaccaratGamesAsync();
				_selectedGameIds = (from s in await api.GetUserGameSelectionsAsync()
					where s.IsSelected
					select s.GameId).ToHashSet();
				UpdateGameCheckboxes();
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("게임 리스트 로드 실패:\n" + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			_isLoadingGames = false;
		}
	}

	private void UpdateGameCheckboxes()
	{
		flowPanelGames.SuspendLayout();
		flowPanelGames.Controls.Clear();
		flowPanelGames.FlowDirection = FlowDirection.LeftToRight;
		flowPanelGames.WrapContents = true;
		foreach (GameDto game in VisibleRoomGames)
		{
			CheckBox chk = new CheckBox
			{
				Text = (game.TitleKo ?? game.Title),
				Tag = game.Id,
				AutoSize = false,
				ForeColor = Color.FromArgb(234, 234, 234),
				Checked = _selectedGameIds.Contains(game.Id),
				Width = 260,
				Height = 28,
				Margin = new Padding(8, 6, 8, 6),
				Padding = new Padding(2, 0, 0, 0)
			};
			chk.CheckedChanged += GameCheckbox_CheckedChanged;
			flowPanelGames.Controls.Add(chk);
		}
		flowPanelGames.ResumeLayout();
		UpdateSelectAllCheckbox();
	}

	private void GameCheckbox_CheckedChanged(object? sender, EventArgs e)
	{
		if (!_isLoadingGames && sender is CheckBox chk)
		{
			int gameId = (int)chk.Tag;
			if (chk.Checked)
			{
				_selectedGameIds.Add(gameId);
			}
			else
			{
				_selectedGameIds.Remove(gameId);
			}
			UpdateSelectAllCheckbox();
		}
	}

	/// <summary>Tables on the open provider tab; the whole catalogue when no tab filters it.</summary>
	private List<GameDto> VisibleRoomGames => (_roomProviderFilter == null)
		? _baccaratGames
		: _baccaratGames.Where(g => string.Equals(g.Provider, _roomProviderFilter, StringComparison.OrdinalIgnoreCase)).ToList();

	private void UpdateSelectAllCheckbox()
	{
		// scoped to the tab — otherwise the box only ticks when every provider is fully selected
		List<GameDto> shown = VisibleRoomGames;
		chkSelectAll.CheckedChanged -= ChkSelectAll_CheckedChanged;
		chkSelectAll.Checked = shown.Count > 0 && shown.All(g => _selectedGameIds.Contains(g.Id));
		chkSelectAll.CheckedChanged += ChkSelectAll_CheckedChanged;
	}

	private void ChkSelectAll_CheckedChanged(object? sender, EventArgs e)
	{
		if (_isLoadingGames)
		{
			return;
		}
		bool selectAll = chkSelectAll.Checked;
		_isLoadingGames = true;
		try
		{
			foreach (CheckBox control in flowPanelGames.Controls)
			{
				control.Checked = selectAll;
			}
			// only the open tab's tables change; the other provider's picks are left alone
			foreach (GameDto g in VisibleRoomGames)
			{
				if (selectAll)
				{
					_selectedGameIds.Add(g.Id);
				}
				else
				{
					_selectedGameIds.Remove(g.Id);
				}
			}
		}
		finally
		{
			_isLoadingGames = false;
		}
	}

	private async Task SaveAllSelectionsAsync()
	{
		if (_baccaratGames.Count == 0)
		{
			return;
		}
		try
		{
			ApiClient api = AuthService.Api;
			if (api != null)
			{
				List<GameSelectionDto> selections = _baccaratGames.Select((GameDto g) => new GameSelectionDto(g.Id, _selectedGameIds.Contains(g.Id))).ToList();
				await api.SaveUserGameSelectionsAsync(selections);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("선택 저장 실패:\n" + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private async void BtnSaveRoomSettings_Click(object? sender, EventArgs e)
	{
		btnSaveRoomSettings.Enabled = false;
		try
		{
			await SaveAllSelectionsAsync();
			int checkedCount = flowPanelGames.Controls.OfType<CheckBox>().Count((CheckBox c) => c.Checked);
			MessageBox.Show($"선택한 게임 {checkedCount}개가 저장되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		finally
		{
			btnSaveRoomSettings.Enabled = true;
		}
	}

	private async Task CreatePlayRoomPanelsAsync()
	{
		flowPanelPlayRoom.SuspendLayout();
		try
		{
			flowPanelPlayRoom.Controls.Clear();
			_gameIdToPanelMap.Clear();
			_ppTableIdToMapKey.Clear();
			_unmappedGameCodes.Clear();
			_unmappedPpTableIds.Clear();
			_allUnmappedGames.Clear();
			foreach (GameDto game in _baccaratGames)
			{
				if (string.IsNullOrEmpty(game.PpTableId))
				{
					_allUnmappedGames[game.GameCode] = game;
				}
			}
			FileLogger.Log($"[Form1] PP Table ID 미매핑 게임 수: {_allUnmappedGames.Count}개 (전체 {_baccaratGames.Count}개 중)");
			List<GameDto> selectedGames = _baccaratGames.Where((GameDto g) => _selectedGameIds.Contains(g.Id)).OrderBy<GameDto, string>((GameDto g) => g.TitleKo, StringComparer.Ordinal).ToList();
			_selectedTableIdsCache = selectedGames.Select((GameDto g) => g.PpTableId ?? g.GameCode).ToHashSet();
			FileLogger.Log("[Form1] 선택 테이블 (PpTableId/GameCode): " + string.Join(", ", _selectedTableIdsCache));
			flowPanelPlayRoom.Controls.Add(CreateRoomSwitchHeaderPanel());
			foreach (GameDto game2 in selectedGames)
			{
				Panel panel = CreateGamePanel(game2);
				flowPanelPlayRoom.Controls.Add(panel);
				string tableKey = game2.PpTableId ?? game2.GameCode;
				_gameIdToPanelMap[tableKey] = panel;
				if (!string.IsNullOrEmpty(game2.PpTableId))
				{
					_ppTableIdToMapKey[game2.PpTableId] = game2.PpTableId;
				}
				else
				{
					_unmappedGameCodes[game2.GameCode] = game2.Id;
				}
			}
			_autoBettingService.SetSelectedTables(_selectedTableIdsCache);
			UpdateRoomSwitchSummary();
		}
		finally
		{
			flowPanelPlayRoom.ResumeLayout(performLayout: true);
		}
		ResizeRoomPanels();
		panelPlayRoomContainer.AutoScrollMinSize = new Size(0, flowPanelPlayRoom.PreferredSize.Height + 20);
		string key;
		Panel value;
		foreach (KeyValuePair<string, Panel> item in _gameIdToPanelMap)
		{
			item.Deconstruct(out key, out value);
			string tableId = key;
			Panel panel2 = value;
			if (_gameHistories.ContainsKey(tableId))
			{
				panel2.Controls.OfType<PictureBox>().FirstOrDefault((PictureBox p) => p.Name == "picRoad")?.Invalidate();
			}
		}
		if (_autoBettingService.Context.BetAmounts.Count > 0)
		{
			foreach (KeyValuePair<string, Panel> item2 in _gameIdToPanelMap)
			{
				item2.Deconstruct(out key, out value);
				string tableId2 = key;
				int step = 1;
				if (_autoBettingService.Context.RoomStates.TryGetValue(tableId2, out RoomBettingState roomState))
				{
					step = roomState.CurrentStep;
				}
				UpdateBetStepLabel(tableId2, step);
			}
		}
		await Task.CompletedTask;
	}

	/// <summary>Widest a room panel is allowed to get — beyond this the big road just looks stretched.</summary>
	private const int MaxRoomPanelWidth = 1100;

	/// <summary>Below this the header buttons and the table id would start overlapping.</summary>
	private const int MinRoomPanelWidth = 520;

	/// <summary>
	/// Width a room panel should take so it fits the container instead of forcing a horizontal
	/// scrollbar. Panels used to be a fixed 1100px, so narrowing the window clipped them.
	/// </summary>
	private int GetRoomPanelWidth()
	{
		int available = panelPlayRoomContainer.ClientSize.Width
			- flowPanelPlayRoom.Padding.Horizontal
			- 6; // the panels' own Margin (3 left + 3 right)
		return Math.Max(MinRoomPanelWidth, Math.Min(MaxRoomPanelWidth, available));
	}

	/// <summary>
	/// Re-widths the header and every room panel after the window is resized. Child controls are
	/// anchored, so their own layout follows automatically.
	/// </summary>
	private void ResizeRoomPanels()
	{
		if (flowPanelPlayRoom.Controls.Count == 0)
		{
			return;
		}
		int width = GetRoomPanelWidth();
		flowPanelPlayRoom.SuspendLayout();
		try
		{
			foreach (Control child in flowPanelPlayRoom.Controls)
			{
				if (child.Width != width)
				{
					child.Width = width;
				}
			}
		}
		finally
		{
			flowPanelPlayRoom.ResumeLayout(performLayout: true);
		}
	}

	private void PanelPlayRoomContainer_SizeChanged(object? sender, EventArgs e)
	{
		ResizeRoomPanels();
	}

	private Panel CreateGamePanel(GameDto game)
	{
		int panelWidth = GetRoomPanelWidth();
		string ppTableId = game.PpTableId ?? game.GameCode;
		Panel panel = new Panel
		{
			Width = panelWidth,
			Height = 200,
			BackColor = Color.FromArgb(22, 33, 62),
			BorderStyle = BorderStyle.FixedSingle,
			Margin = new Padding(3),
			Tag = ppTableId
		};
		string titleText = game.TitleKo ?? game.Title;
		if (string.IsNullOrEmpty(game.PpTableId))
		{
			titleText += " [PP ID 미설정]";
		}
		Button btnRoomSwitch = new Button
		{
			Name = "btnRoomSwitch",
			Tag = ppTableId,
			Text = "ON",
			Font = new Font("맑은 고딕", 8f, FontStyle.Bold),
			FlatStyle = FlatStyle.Flat,
			Location = new Point(8, 5),
			Size = new Size(46, 23),
			TabStop = false,
			Cursor = Cursors.Hand
		};
		btnRoomSwitch.FlatAppearance.BorderSize = 0;
		btnRoomSwitch.Click += BtnRoomSwitch_Click;
		panel.Controls.Add(btnRoomSwitch);
		Label lblTitle = new Label
		{
			Name = "lblRoomTitle",
			Text = titleText,
			Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
			ForeColor = (string.IsNullOrEmpty(game.PpTableId) ? Color.Orange : Color.Yellow),
			Location = new Point(btnRoomSwitch.Right + 8, 8),
			AutoSize = true
		};
		panel.Controls.Add(lblTitle);
		Label lblTableState = new Label
		{
			Name = "lblTableState",
			Text = "| TID: " + ppTableId + " | 상태: -",
			Font = new Font("맑은 고딕", 9f),
			ForeColor = Color.Gray,
			Location = new Point(lblTitle.Left + lblTitle.PreferredWidth + 10, 8),
			AutoSize = true
		};
		panel.Controls.Add(lblTableState);
		Label lblBetStep = new Label
		{
			Name = "lblBetStep",
			Text = "1단계:0원",
			Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
			ForeColor = Color.LimeGreen,
			AutoSize = false,
			Size = new Size(240, 18),
			TextAlign = ContentAlignment.MiddleRight,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right)
		};
		lblBetStep.Location = new Point(panelWidth - lblBetStep.Width - 15, 8);
		panel.Controls.Add(lblBetStep);
		Label lblBetType = new Label
		{
			Name = "lblBetType",
			Text = "",
			Font = new Font("맑은 고딕", 10f, FontStyle.Bold),
			ForeColor = Color.DodgerBlue,
			AutoSize = false,
			Size = new Size(20, 18),
			TextAlign = ContentAlignment.MiddleCenter,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right)
		};
		lblBetType.Location = new Point(lblBetStep.Left - lblBetType.Width - 4, 7);
		panel.Controls.Add(lblBetType);
		PictureBox picRoad = new PictureBox
		{
			Name = "picRoad",
			Location = new Point(10, 35),
			Size = new Size(panelWidth - 30, 155),
			// stretches with the panel so the big road stays readable at any window width
			Anchor = (AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right),
			BackColor = Color.FromArgb(18, 18, 38)
		};
		picRoad.Paint += PicRoad_Paint;
		panel.Controls.Add(picRoad);
		ApplyRoomVisual(panel, ppTableId);
		return panel;
	}

	private Panel CreateRoomSwitchHeaderPanel()
	{
		int headerWidth = GetRoomPanelWidth();
		Panel obj = new Panel
		{
			Width = headerWidth,
			Height = 36,
			BackColor = Color.FromArgb(18, 25, 45),
			Margin = new Padding(3, 3, 3, 0)
		};
		Button btnAllOn = CreateHeaderButton("전체 ON", new Point(8, 5), Color.FromArgb(34, 110, 58));
		btnAllOn.Click += delegate
		{
			SetAllRoomSwitches(enabled: true);
		};
		obj.Controls.Add(btnAllOn);
		Button btnAllOff = CreateHeaderButton("전체 OFF", new Point(btnAllOn.Right + 6, 5), Color.FromArgb(120, 40, 44));
		btnAllOff.Click += delegate
		{
			SetAllRoomSwitches(enabled: false);
		};
		obj.Controls.Add(btnAllOff);
		_lblRoomSwitchSummary = new Label
		{
			Name = "lblRoomSwitchSummary",
			Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
			ForeColor = Color.Silver,
			Location = new Point(btnAllOff.Right + 14, 10),
			AutoSize = true
		};
		obj.Controls.Add(_lblRoomSwitchSummary);
		Label lblHint = new Label
		{
			Text = "OFF된 방은 베팅만 제외됩니다 (빅로드·단계는 그대로 유지)",
			Font = new Font("맑은 고딕", 8.5f),
			ForeColor = Color.FromArgb(120, 130, 150),
			AutoSize = false,
			Size = new Size(360, 18),
			TextAlign = ContentAlignment.MiddleRight,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right),
			// pinned to the right edge rather than a fixed x, so it rides the panel as it narrows
			Location = new Point(headerWidth - 375, 10)
		};
		obj.Controls.Add(lblHint);
		return obj;
	}

	private static Button CreateHeaderButton(string text, Point location, Color backColor)
	{
		Button button = new Button();
		button.Text = text;
		button.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
		button.FlatStyle = FlatStyle.Flat;
		button.BackColor = backColor;
		button.ForeColor = Color.White;
		button.Location = location;
		button.Size = new Size(80, 25);
		button.TabStop = false;
		button.Cursor = Cursors.Hand;
		button.FlatAppearance.BorderSize = 0;
		return button;
	}

	private void BtnRoomSwitch_Click(object? sender, EventArgs e)
	{
		if (sender is Button { Tag: string tableId } btn)
		{
			bool enable = !_autoBettingService.IsRoomBettingEnabled(tableId);
			_autoBettingService.SetRoomBettingEnabled(tableId, enable);
			if (btn.Parent is Panel panel)
			{
				ApplyRoomVisual(panel, tableId);
			}
			UpdateRoomSwitchSummary();
		}
	}

	private void SetAllRoomSwitches(bool enabled)
	{
		foreach (var (tableId, panel2) in _gameIdToPanelMap)
		{
			_autoBettingService.SetRoomBettingEnabled(tableId, enabled);
			ApplyRoomVisual(panel2, tableId);
		}
		UpdateRoomSwitchSummary();
	}

	private void ApplyRoomVisual(Panel panel, string tableId)
	{
		ApplyRoomSwitchVisual(panel, _autoBettingService.IsRoomBettingEnabled(tableId), _autoBettingService.GetRoomGateDisplay(tableId));
	}

	private void RefreshAllRoomVisuals()
	{
		foreach (var (tableId, panel2) in _gameIdToPanelMap)
		{
			ApplyRoomVisual(panel2, tableId);
		}
		UpdateRoomSwitchSummary();
	}

	private static void ApplyRoomSwitchVisual(Panel panel, bool enabled, RoomGateDisplay gate)
	{
		bool flag = (uint)(gate - 2) <= 1u;
		bool gateClosed = flag;
		panel.BackColor = ((!enabled) ? RoomPanelOffColor : ((gate == RoomGateDisplay.StaleClosed) ? RoomPanelGateStaleColor : (gateClosed ? RoomPanelGateClosedColor : RoomPanelOnColor)));
		foreach (Button item in from b in panel.Controls.OfType<Button>()
			where b.Name == "btnRoomSwitch"
			select b)
		{
			item.Text = (enabled ? "ON" : "OFF");
			item.BackColor = (enabled ? Color.FromArgb(34, 110, 58) : Color.FromArgb(150, 42, 46));
			item.ForeColor = Color.White;
		}
		foreach (Label lbl in from l in panel.Controls.OfType<Label>()
			where l.Name == "lblRoomTitle"
			select l)
		{
			string baseText = StripRoomTitleStatus(lbl.Text);
			Label label = lbl;
			string text = baseText;
			string text2 = (enabled ? (gate switch
			{
				RoomGateDisplay.StaleClosed => $" [게이트 OFF·{45}분↑]", 
				RoomGateDisplay.Closed => " [게이트 OFF]", 
				RoomGateDisplay.Open => " [게이트 ON]", 
				_ => "", 
			}) : " [OFF]");
			label.Text = text + text2;
			lbl.ForeColor = ((!enabled) ? Color.FromArgb(130, 130, 135) : ((gate == RoomGateDisplay.StaleClosed) ? Color.FromArgb(255, 120, 120) : (gateClosed ? Color.FromArgb(150, 160, 190) : (baseText.Contains("[PP ID 미설정]") ? Color.Orange : Color.Yellow))));
			// The title auto-sizes, and the gate suffix makes it much wider. Without moving the state
			// label out of the way the two overlap and both texts are drawn on top of each other;
			// without the invalidate, a shrinking title leaves its old pixels on the panel.
			ReflowRoomTitleRow(panel, lbl);
		}
	}

	/// <summary>Keeps the TID/state label clear of the auto-sizing title and repaints what the title vacated.</summary>
	private static void ReflowRoomTitleRow(Panel panel, Label title)
	{
		Label? state = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Name == "lblTableState");
		if (state == null)
		{
			return;
		}
		int desiredLeft = title.Left + title.PreferredWidth + 10;
		if (state.Left != desiredLeft)
		{
			state.Left = desiredLeft;
		}
		panel.Invalidate(new Rectangle(0, 0, panel.ClientSize.Width, 32));
	}

	private static string StripRoomTitleStatus(string title)
	{
		string stripped = title.Replace(" [OFF]", "");
		int gateIdx = stripped.IndexOf(" [게이트", StringComparison.Ordinal);
		if (gateIdx < 0)
		{
			return stripped;
		}
		return stripped.Substring(0, gateIdx);
	}

	private static void RetagPanel(Panel panel, string newTableId)
	{
		panel.Tag = newTableId;
		foreach (Button item in from b in panel.Controls.OfType<Button>()
			where b.Name == "btnRoomSwitch"
			select b)
		{
			item.Tag = newTableId;
		}
	}

	private void UpdateRoomSwitchSummary()
	{
		if (_lblRoomSwitchSummary != null)
		{
			int total = _gameIdToPanelMap.Count;
			int offCount = _gameIdToPanelMap.Keys.Count((string k) => !_autoBettingService.IsRoomBettingEnabled(k));
			if (total == 0)
			{
				_lblRoomSwitchSummary.Text = "표시할 방이 없습니다";
				_lblRoomSwitchSummary.ForeColor = Color.Silver;
			}
			else if (offCount == 0)
			{
				_lblRoomSwitchSummary.Text = $"전체 {total}개 방 ON";
				_lblRoomSwitchSummary.ForeColor = Color.FromArgb(120, 210, 140);
			}
			else if (offCount >= total)
			{
				_lblRoomSwitchSummary.Text = $"⚠ {total}개 방 전부 OFF — 베팅이 나가지 않습니다";
				_lblRoomSwitchSummary.ForeColor = Color.FromArgb(255, 96, 96);
			}
			else
			{
				_lblRoomSwitchSummary.Text = $"{total}개 중 {offCount}개 OFF (베팅 대상 {total - offCount}개)";
				_lblRoomSwitchSummary.ForeColor = Color.Orange;
			}
			AppendRoomGateSummary(total);
		}
	}

	private void AppendRoomGateSummary(int total)
	{
		if (_lblRoomSwitchSummary != null && _autoBettingService.Context.IsRoomGateActive && total != 0)
		{
			int openCount = _gameIdToPanelMap.Keys.Count(_autoBettingService.IsRoomGateOpen);
			_lblRoomSwitchSummary.Text += $"  |  게이트 열림 {openCount}/{total}";
			if (openCount == 0)
			{
				_lblRoomSwitchSummary.ForeColor = Color.FromArgb(255, 150, 96);
			}
		}
	}

	private void PicRoad_Paint(object? sender, PaintEventArgs e)
	{
		if (sender is PictureBox pic && pic.Parent?.Tag is string gameId && _gameHistories.TryGetValue(gameId, out List<GameResult> history))
		{
			List<GameResult> snapshot;
			lock (history)
			{
				snapshot = new List<GameResult>(history);
			}
			DrawBigRoad(e.Graphics, snapshot, pic.Width, pic.Height);
		}
	}

	private static void DrawBigRoad(Graphics g, List<GameResult> results, int width, int height)
	{
		HashSet<(int, int)> occupied = new HashSet<(int, int)>();
		int col = 0;
		int row = 0;
		int streakStartCol = 0;
		bool isTurned = false;
		string prevWinner = null;
		int lastDrawnCol = 0;
		int lastDrawnRow = 0;
		bool hasDrawn = false;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		foreach (GameResult result in results)
		{
			if (result.Winner == "Tie")
			{
				if (hasDrawn)
				{
					int tieX = 2 + lastDrawnCol * 18 + 9;
					int tieY = 2 + lastDrawnRow * 18 + 9;
					using SolidBrush tieBrush = new SolidBrush(Color.LimeGreen);
					g.FillEllipse(tieBrush, tieX - 3, tieY - 3, 6, 6);
				}
				continue;
			}
			if (prevWinner == null)
			{
				col = 0;
				row = 0;
				streakStartCol = 0;
				isTurned = false;
			}
			else if (result.Winner != prevWinner)
			{
				col = streakStartCol + 1;
				row = 0;
				streakStartCol = col;
				isTurned = false;
			}
			else if (isTurned)
			{
				col++;
			}
			else
			{
				int nextRow = row + 1;
				if (nextRow >= 6 || occupied.Contains((col, nextRow)))
				{
					col++;
					isTurned = true;
				}
				else
				{
					row = nextRow;
				}
			}
			occupied.Add((col, row));
			lastDrawnCol = col;
			lastDrawnRow = row;
			hasDrawn = true;
			int x = 2 + col * 18;
			int y = 2 + row * 18;
			string winner = result.Winner;
			Color color = ((winner == "Player") ? Color.DodgerBlue : ((!(winner == "Banker")) ? Color.Gray : Color.Crimson));
			using (SolidBrush brush = new SolidBrush(color))
			{
				g.FillEllipse(brush, x + 1, y + 1, 16, 16);
			}
			if (result.PlayerPair)
			{
				using SolidBrush pairBrush = new SolidBrush(Color.Blue);
				g.FillEllipse(pairBrush, x, y, 5, 5);
			}
			if (result.BankerPair)
			{
				using SolidBrush pairBrush2 = new SolidBrush(Color.Red);
				g.FillEllipse(pairBrush2, x + 18 - 6, y + 18 - 6, 5, 5);
			}
			prevWinner = result.Winner;
		}
	}

	private void UpdateConnectionStatus(bool isConnected)
	{
		if (isConnected)
		{
			_receivedCount = 0;
			_savedCount = 0;
			string source = ((_cdpService.IsConnected && _wsServer.IsExtensionConnected) ? "CDP+Extension" : (_cdpService.IsConnected ? "CDP" : "Extension"));
			lblStatus.Text = "Chrome 연결됨 (" + source + ")";
			lblStatus.ForeColor = Color.Green;
			btnConnect.Enabled = false;
			btnDisconnect.Enabled = true;
		}
		else if (_wsServer.IsListening || _cdpService.IsConnected)
		{
			if (_cdpService.IsConnected)
			{
				lblStatus.Text = "Chrome 연결됨 (CDP)";
				lblStatus.ForeColor = Color.Green;
				btnConnect.Enabled = false;
				btnDisconnect.Enabled = true;
			}
			else
			{
				lblStatus.Text = "대기 중... (CDP/Extension 연결 대기)";
				lblStatus.ForeColor = Color.Orange;
				btnConnect.Enabled = false;
				btnDisconnect.Enabled = true;
			}
		}
		else
		{
			lblStatus.Text = "연결 안됨";
			lblStatus.ForeColor = Color.Gray;
			btnConnect.Enabled = true;
			btnDisconnect.Enabled = false;
		}
		btnExit.Enabled = !isConnected && !_autoBettingService.IsRunning;
	}

	private void UpdateMessageCounter()
	{
		string counterText = $"수신: {_receivedCount} | 저장: {_savedCount}";
		bool isAnyConnected = _cdpService.IsConnected || _wsServer.IsExtensionConnected;
		lblStatus.Text = (isAnyConnected ? ("Chrome 연결됨 (" + counterText + ")") : "연결 안됨");
	}

	/// <summary>
	/// Provider of a table as recorded in the room catalogue. Used to pick a bet's wire format when the
	/// account holds both grants; live socket traffic (<see cref="PragmaticMessageParser.IsEvolutionTable"/>)
	/// overrides it, since that is observed rather than configured.
	/// </summary>
	private string GetTableProvider(string tableId)
	{
		if (PragmaticMessageParser.IsEvolutionTable(tableId))
		{
			return GameProviderNames.EvolutionKey;
		}
		List<GameDto> games = _baccaratGames;
		if (games == null || games.Count == 0)
		{
			return GameProviderNames.PragmaticPlayKey;
		}
		GameDto game = games.FirstOrDefault((GameDto g) => g.PpTableId == tableId)
			?? games.FirstOrDefault((GameDto g) => g.GameCode == tableId);
		return string.IsNullOrEmpty(game?.Provider) ? GameProviderNames.PragmaticPlayKey : game.Provider;
	}

	private string GetTableDisplayName(string tableId)
	{
		List<GameDto> games = _baccaratGames;
		if (games == null || games.Count == 0)
		{
			return tableId;
		}
		GameDto game = games.FirstOrDefault((GameDto g) => g.PpTableId == tableId) ?? games.FirstOrDefault((GameDto g) => g.GameCode == tableId);
		return game?.TitleKo ?? game?.Title ?? tableId;
	}

	private string ResolveTableKey(string wsTableId)
	{
		if (_gameIdToPanelMap.ContainsKey(wsTableId))
		{
			return wsTableId;
		}
		if (_ppTableIdToMapKey.TryGetValue(wsTableId, out string mappedKey))
		{
			return mappedKey;
		}
		TryAutoMapTableId(wsTableId);
		return wsTableId;
	}

	private void TryAutoMapTableId(string wsTableId)
	{
		if (!_ppTableIdToMapKey.ContainsKey(wsTableId) && _unmappedPpTableIds.TryAdd(wsTableId, 0))
		{
			FileLogger.Log("[Form1] 미매핑 PP tableId: " + wsTableId + " (매핑 안됨 → 무시)");
		}
	}

	private void Parser_TableConfigReceived(object? sender, PpTableConfigArgs e)
	{
		// 이름까지 알 수 있는 유일한 지점이라, 매핑 여부와 무관하게 먼저 카탈로그에 알린다
		_gameCatalogSync.Observe(e.TableId, e.TableName, e.TableType, e.Provider);
		// A table already in flight keeps its stale provider otherwise, and the first bet goes out
		// in the wrong protocol.
		if (!string.IsNullOrEmpty(e.Provider)
			&& _tableStates.TryGetValue(ResolveTableKey(e.TableId), out TableState knownState))
		{
			knownState.Provider = e.Provider;
		}
		if (_ppTableIdToMapKey.ContainsKey(e.TableId))
		{
			return;
		}
		string normalizedPpName = NormalizePpTableName(e.TableName);
		GameDto learnedGame = FindMatchingGameForLearning(normalizedPpName);
		if (learnedGame != null)
		{
			_allUnmappedGames.TryRemove(learnedGame.GameCode, out GameDto _);
			FileLogger.Log($"[Form1] PP Table ID 학습: PP tableId={e.TableId}, table_name={e.TableName} → Game={learnedGame.Title} (GameCode={learnedGame.GameCode})");
			Task.Run(() => SavePpTableIdAsync(learnedGame.Id, e.TableId));
		}
		if (!_ppTableIdToMapKey.ContainsKey(e.TableId) && !_gameIdToPanelMap.ContainsKey(e.TableId))
		{
			GameDto matchedGame = FindMatchingGame(normalizedPpName);
			if (!(matchedGame == null))
			{
				ApplyTableMapping(matchedGame, e.TableId);
			}
		}
	}

	private static string NormalizePpTableName(string ppTableName)
	{
		return ppTableName.ToLowerInvariant().Replace('_', ' ').Trim();
	}

	private static string CompactName(string name)
	{
		StringBuilder sb = new StringBuilder(name.Length);
		foreach (char c in name)
		{
			if (char.IsLetterOrDigit(c) || c == '.')
			{
				sb.Append(c);
			}
		}
		return sb.ToString();
	}

	private GameDto? FindMatchingGameForLearning(string normalizedPpName)
	{
		string compactPp = CompactName(normalizedPpName);
		foreach (KeyValuePair<string, GameDto> kvp in _allUnmappedGames)
		{
			if ((kvp.Value.Title?.ToLowerInvariant().Trim() ?? "") == normalizedPpName)
			{
				return kvp.Value;
			}
		}
		foreach (KeyValuePair<string, GameDto> kvp2 in _allUnmappedGames)
		{
			string compactTitle = CompactName(kvp2.Value.Title?.ToLowerInvariant().Trim() ?? "");
			if (compactTitle == compactPp && compactTitle.Length > 0)
			{
				return kvp2.Value;
			}
		}
		return null;
	}

	private GameDto? FindMatchingGame(string normalizedPpName)
	{
		string compactPp = CompactName(normalizedPpName);
		foreach (GameDto game in _baccaratGames)
		{
			if (_unmappedGameCodes.ContainsKey(game.GameCode) && (game.Title?.ToLowerInvariant().Trim() ?? "") == normalizedPpName)
			{
				return game;
			}
		}
		foreach (GameDto game2 in _baccaratGames)
		{
			if (_unmappedGameCodes.ContainsKey(game2.GameCode))
			{
				string compactTitle = CompactName(game2.Title?.ToLowerInvariant().Trim() ?? "");
				if (compactTitle == compactPp && compactTitle.Length > 0)
				{
					return game2;
				}
			}
		}
		return null;
	}

	private void ApplyTableMapping(GameDto game, string ppTableId)
	{
		string oldKey = game.PpTableId ?? game.GameCode;
		_ppTableIdToMapKey[ppTableId] = ppTableId;
		_unmappedGameCodes.TryRemove(game.GameCode, out var _);
		_unmappedPpTableIds.TryRemove(ppTableId, out var _);
		if (_gameIdToPanelMap.TryRemove(oldKey, out Panel panel))
		{
			_gameIdToPanelMap[ppTableId] = panel;
			if (base.InvokeRequired)
			{
				BeginInvoke(delegate
				{
					RetagPanel(panel, ppTableId);
				});
			}
			else
			{
				RetagPanel(panel, ppTableId);
			}
		}
		_autoBettingService.RenameRoomKey(oldKey, ppTableId);
		if (_gameHistories.TryRemove(oldKey, out List<GameResult> history))
		{
			_gameHistories[ppTableId] = history;
		}
		else if (oldKey != ppTableId)
		{
			_gameHistories.ContainsKey(ppTableId);
		}
		if (_lastShoeTotalGames.TryRemove(oldKey, out var lastTotalGames))
		{
			_lastShoeTotalGames[ppTableId] = lastTotalGames;
		}
		if (_tableStates.TryRemove(oldKey, out TableState tableState))
		{
			tableState.TableId = ppTableId;
			_tableStates[ppTableId] = tableState;
		}
		HashSet<string> newCache = new HashSet<string>(_selectedTableIdsCache);
		newCache.Remove(oldKey);
		newCache.Add(ppTableId);
		_selectedTableIdsCache = newCache;
		_autoBettingService.SetSelectedTables(newCache);
		FileLogger.Log($"[Form1] tableconfig 자동 매핑: PP tableId={ppTableId} → Game={game.Title} (GameCode={game.GameCode})");
		if (_gameIdToPanelMap.TryGetValue(ppTableId, out Panel mappedPanel) && _gameHistories.ContainsKey(ppTableId))
		{
			PictureBox picRoad = mappedPanel.Controls.OfType<PictureBox>().FirstOrDefault((PictureBox p) => p.Name == "picRoad");
			if (picRoad != null)
			{
				if (base.InvokeRequired)
				{
					BeginInvoke(delegate
					{
						picRoad.Invalidate();
					});
				}
				else
				{
					picRoad.Invalidate();
				}
			}
		}
		Task.Run(() => SavePpTableIdAsync(game.Id, ppTableId));
	}

	private async Task SavePpTableIdAsync(int gameId, string ppTableId)
	{
		try
		{
			string token = AuthService.Api?.JwtToken;
			if (string.IsNullOrEmpty(token))
			{
				FileLogger.Log("[Form1] PpTableId DB 저장 실패: JWT 토큰 없음");
				return;
			}
			using HttpClient client = new HttpClient();
			client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
			HttpResponseMessage response = await client.PutAsJsonAsync($"{AppConfiguration.ApiBaseUrl.TrimEnd('/')}/api/games/{gameId}/pp-table-id", new { ppTableId });
			if (response.IsSuccessStatusCode)
			{
				FileLogger.Log($"[Form1] PpTableId DB 저장 성공: GameId={gameId}, PpTableId={ppTableId}");
			}
			else
			{
				FileLogger.Log($"[Form1] PpTableId DB 저장 실패: {response.StatusCode}");
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Form1] PpTableId DB 저장 오류: " + ex.Message);
		}
	}

	private void UpdateTableStateLabel(string tableId)
	{
		if (!_gameIdToPanelMap.TryGetValue(tableId, out Panel panel) || !(panel.Controls.Find("lblTableState", searchAllChildren: false).FirstOrDefault() is Label lblState))
		{
			return;
		}
		if (_tableStates.TryGetValue(tableId, out TableState state))
		{
			string gameId = state.GameId ?? "-";
			if (state.IsBettingOpen)
			{
				lblState.Text = "| TID: " + tableId + " | ● Betting | GID: " + gameId;
				lblState.ForeColor = Color.LimeGreen;
				return;
			}
			lblState.Text = $"| TID: {tableId} | ● {state.Phase} | GID: {gameId}";
			lblState.ForeColor = Color.OrangeRed;
		}
		else
		{
			lblState.Text = "| TID: " + tableId + " | 상태: 미수신";
			lblState.ForeColor = Color.Gray;
		}
	}

	private void UpdateBetStepLabel(string tableId, int step, string? betType = null, decimal? actualAmount = null)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				UpdateBetStepLabel(tableId, step, betType, actualAmount);
			});
		}
		else
		{
			if (!_gameIdToPanelMap.TryGetValue(tableId, out Panel panel))
			{
				return;
			}
			Label lblBetStep = panel.Controls.Find("lblBetStep", searchAllChildren: false).FirstOrDefault() as Label;
			Label lblBetTypeCtrl = panel.Controls.Find("lblBetType", searchAllChildren: false).FirstOrDefault() as Label;
			if (lblBetStep == null)
			{
				return;
			}
			AutoBettingContext ctx = _autoBettingService.Context;
			bool isMixed = ctx.SelectedBetAmountType == 3;
			string methodLabel = null;
			decimal amount = 0m;
			if (ctx.IsMatrixStrategy && ctx.RoomStates.TryGetValue(tableId, out RoomBettingState matrixState))
			{
				int row = matrixState.MatrixCurrentRow;
				int col = matrixState.MatrixCurrentCol;
				if (row >= 1 && col >= 1 && row <= ctx.MatrixCells.Length && ctx.MatrixCells[row - 1] != null && col <= ctx.MatrixCells[row - 1].Length)
				{
					amount = ctx.MatrixCells[row - 1][col - 1];
				}
				decimal displayAmt = actualAmount ?? amount;
				string matrixText = (actualAmount.HasValue ? $"{row}차-{col}단계:{displayAmt:N0}원(실제)" : $"{row}차-{col}단계:{amount:N0}원");
				if (lblBetStep.Text != matrixText)
				{
					lblBetStep.Text = matrixText;
				}
				lblBetStep.ForeColor = Color.LimeGreen;
				if (lblBetTypeCtrl != null)
				{
					string pbType = ((!string.IsNullOrEmpty(betType)) ? betType : matrixState.LastBetType);
					if (!string.IsNullOrEmpty(pbType) && pbType.Equals("Player", StringComparison.OrdinalIgnoreCase))
					{
						lblBetTypeCtrl.Text = "P";
						lblBetTypeCtrl.ForeColor = Color.DodgerBlue;
					}
					else if (!string.IsNullOrEmpty(pbType) && pbType.Equals("Banker", StringComparison.OrdinalIgnoreCase))
					{
						lblBetTypeCtrl.Text = "B";
						lblBetTypeCtrl.ForeColor = Color.Crimson;
					}
				}
				return;
			}
			RoomBettingState rs;
			decimal betAmount;
			if (isMixed)
			{
				bool num = ctx.TableIndexMap.GetValueOrDefault(tableId, 0) % 2 == 1;
				amount = (num ? ctx.HongKongBetAmounts : ctx.MartinBetAmounts).GetValueOrDefault(step, 0m);
				methodLabel = (num ? "홍콩" : "마틴");
			}
			else if (ctx.SelectedBetAmountType == 10 && ctx.RoomStates.TryGetValue(tableId, out rs))
			{
				if (!rs.IsDalmenzingRecoveryPhase)
				{
					amount = ctx.DalmenzingDAlembertBaseAmount * (decimal)step;
					methodLabel = "달랑";
				}
				else
				{
					int recStep = rs.DalmenzingRecoveryStep;
					if (ctx.DalmenzingRecoveryMethod == 1)
					{
						amount = (rs.DalmenzingRecoveryDoubleUpPhase ? rs.DalmenzingRecoveryDoubleUpAmount : ctx.DalmenzingHongKongAmounts.GetValueOrDefault(recStep, 0m));
						methodLabel = "복구-홍콩";
					}
					else
					{
						amount = ctx.DalmenzingMartinAmounts.GetValueOrDefault(recStep, 0m);
						methodLabel = "복구-마틴";
					}
					step = recStep;
				}
			}
			else if (ctx.BetAmounts.TryGetValue(step, out betAmount))
			{
				amount = betAmount;
			}
			decimal displayAmount = actualAmount ?? amount;
			string suffix = ((methodLabel != null) ? (" [" + methodLabel + "]") : string.Empty);
			string newText = (actualAmount.HasValue ? $"{step}단계:{displayAmount:N0}원(실제){suffix}" : $"{step}단계:{amount:N0}원{suffix}");
			if (lblBetStep.Text != newText)
			{
				lblBetStep.Text = newText;
				lblBetStep.ForeColor = ((step == 1) ? Color.LimeGreen : Color.Orange);
			}
			if (lblBetTypeCtrl == null)
			{
				return;
			}
			if (!string.IsNullOrEmpty(betType))
			{
				if (betType.Equals("Player", StringComparison.OrdinalIgnoreCase))
				{
					lblBetTypeCtrl.Text = "P";
					lblBetTypeCtrl.ForeColor = Color.DodgerBlue;
				}
				else if (betType.Equals("Banker", StringComparison.OrdinalIgnoreCase))
				{
					lblBetTypeCtrl.Text = "B";
					lblBetTypeCtrl.ForeColor = Color.Red;
				}
			}
			else
			{
				lblBetTypeCtrl.Text = "";
			}
			if (!string.IsNullOrEmpty(lblBetTypeCtrl.Text))
			{
				lblBetTypeCtrl.Location = new Point(lblBetStep.Left - lblBetTypeCtrl.PreferredWidth - 8, lblBetTypeCtrl.Top);
			}
		}
	}

	private void UpdateAllRoomsCooldownLabel(int seconds)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				UpdateAllRoomsCooldownLabel(seconds);
			});
			return;
		}
		string text = $"쿨다운 {seconds / 60}:{seconds % 60:D2}";
		foreach (KeyValuePair<string, Panel> item in _gameIdToPanelMap)
		{
			item.Deconstruct(out var _, out var value);
			if (value.Controls.Find("lblBetStep", searchAllChildren: false).FirstOrDefault() is Label lblBetStep)
			{
				lblBetStep.Text = text;
				lblBetStep.ForeColor = Color.DeepSkyBlue;
			}
		}
	}

	private void UpdateAllRoomsPauseLabel(int seconds)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				UpdateAllRoomsPauseLabel(seconds);
			});
			return;
		}
		foreach (KeyValuePair<string, Panel> item in _gameIdToPanelMap)
		{
			item.Deconstruct(out var _, out var value);
			if (value.Controls.Find("lblBetStep", searchAllChildren: false).FirstOrDefault() is Label lblBetStep)
			{
				lblBetStep.Text = $"일시정지 {seconds}초";
				lblBetStep.ForeColor = Color.Red;
			}
		}
	}

	private void UpdateAllRoomsToStep1()
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(UpdateAllRoomsToStep1);
			return;
		}
		foreach (var (tableId, _) in _gameIdToPanelMap)
		{
			UpdateBetStepLabel(tableId, 1);
		}
	}

	private void RecordRoomBetStep(string tableId, int step, int matrixRow = 0, int matrixCol = 0)
	{
		if (_autoBettingService.Context.IsMatrixStrategy)
		{
			if (matrixRow < 1 || matrixCol < 1)
			{
				return;
			}
			_roomBetMatrix[tableId] = (matrixRow, matrixCol);
		}
		else
		{
			if (step < 1)
			{
				return;
			}
			_roomBetStep[tableId] = step;
		}
		RefreshLivePeakStepLabel();
		UpdateSessionPeakStep(step, matrixRow, matrixCol);
	}

	private void UpdateSessionPeakStep(int step, int matrixRow, int matrixCol)
	{
		bool changed;
		if (_autoBettingService.Context.IsMatrixStrategy)
		{
			changed = matrixRow > _sessionPeakMatrix.Row || (matrixRow == _sessionPeakMatrix.Row && matrixCol > _sessionPeakMatrix.Col);
			if (changed)
			{
				_sessionPeakMatrix = (Row: matrixRow, Col: matrixCol);
			}
		}
		else
		{
			changed = step > _sessionPeakStep;
			if (changed)
			{
				_sessionPeakStep = step;
			}
		}
		if (changed)
		{
			RefreshSessionPeakStepLabel();
		}
	}

	private void RefreshSessionPeakStepLabel()
	{
		string value = ((!_autoBettingService.Context.IsMatrixStrategy) ? ((_sessionPeakStep >= 1) ? $"{_sessionPeakStep}단계" : "-") : ((_sessionPeakMatrix.Row >= 1 && _sessionPeakMatrix.Col >= 1) ? $"{_sessionPeakMatrix.Row}차-{_sessionPeakMatrix.Col}단계" : "-"));
		string text = "최고단계 : " + value;
		if (lblSessionPeakStepValue.Text != text)
		{
			lblSessionPeakStepValue.Text = text;
		}
	}

	private bool IsRoomBetStepRolledBack(string tableId, int nextStep, int nextMatrixRow, int nextMatrixCol)
	{
		if (_autoBettingService.Context.IsMatrixStrategy)
		{
			if (!_roomBetMatrix.TryGetValue(tableId, out (int, int) last))
			{
				return false;
			}
			if (nextMatrixRow < 1 || nextMatrixCol < 1)
			{
				return false;
			}
			if (nextMatrixRow >= last.Item1)
			{
				if (nextMatrixRow == last.Item1)
				{
					return nextMatrixCol < last.Item2;
				}
				return false;
			}
			return true;
		}
		if (_roomBetStep.TryGetValue(tableId, out var lastStep))
		{
			return nextStep < lastStep;
		}
		return false;
	}

	private void ClearRoomBetStep(string tableId)
	{
		if (_roomBetStep.Remove(tableId) | _roomBetMatrix.Remove(tableId))
		{
			RefreshLivePeakStepLabel();
		}
	}

	private void ClearAllRoomBetSteps()
	{
		_roomBetStep.Clear();
		_roomBetMatrix.Clear();
		RefreshLivePeakStepLabel();
	}

	private void RefreshLivePeakStepLabel()
	{
		string value;
		if (_autoBettingService.Context.IsMatrixStrategy)
		{
			(int, int) top = (from c in _roomBetMatrix.Values
				orderby c.Row descending, c.Col descending
				select c).FirstOrDefault();
			value = ((top.Item1 >= 1 && top.Item2 >= 1) ? $"{top.Item1}차-{top.Item2}단계" : "-");
		}
		else
		{
			int top2 = _roomBetStep.Values.DefaultIfEmpty(0).Max();
			value = ((top2 >= 1) ? $"{top2}단계" : "-");
		}
		string text = "실시간 최고단계 : " + value;
		if (lblPeakStepValue.Text != text)
		{
			lblPeakStepValue.Text = text;
		}
	}

	private void UpdateAllRoomsRefreshLabel(int seconds)
	{
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				UpdateAllRoomsRefreshLabel(seconds);
			});
			return;
		}
		foreach (KeyValuePair<string, Panel> item in _gameIdToPanelMap)
		{
			item.Deconstruct(out var _, out var value);
			if (value.Controls.Find("lblBetStep", searchAllChildren: false).FirstOrDefault() is Label lblBetStep)
			{
				lblBetStep.Text = $"새로고침 {seconds}초";
				lblBetStep.ForeColor = Color.Orange;
			}
		}
	}

	private async Task SaveBettingSessionAsync()
	{
		try
		{
			AutoBettingContext context = _autoBettingService.Context;
			if (!(context.TotalBetAmount <= 0m))
			{
				ApiClient api = AuthService.Api;
				if (api != null)
				{
					await api.SaveBettingSessionAsync(context.StartBalance, context.AvailableBalance, context.TotalBetAmount, context.IsVirtualMode);
					FileLogger.Log($"[Form1] 베팅 세션 저장: 시작={context.StartBalance:N0}, 종료={context.AvailableBalance:N0}, 누적={context.TotalBetAmount:N0}");
				}
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[Form1] 베팅 세션 저장 실패: " + ex.Message, "Error", "Session");
		}
	}

	private async void Form1_FormClosing(object? sender, FormClosingEventArgs e)
	{
		if (_shutdownCompleted)
		{
			return;
		}
		if (!ConfirmExit(e.CloseReason))
		{
			e.Cancel = true;
			return;
		}
		e.Cancel = true;
		try
		{
			await ShutdownAsync();
		}
		finally
		{
			_shutdownCompleted = true;
			Close();
		}
	}

	private bool ConfirmExit(CloseReason closeReason)
	{
		if (_forceClose)
		{
			return true;
		}
		if ((closeReason == CloseReason.WindowsShutDown || closeReason == CloseReason.TaskManagerClosing) ? true : false)
		{
			return true;
		}
		bool isBetting = _autoBettingService.IsRunning;
		if (MessageBox.Show(isBetting ? "자동 베팅이 진행 중입니다.\n종료하면 베팅이 중단되고 로그아웃됩니다.\n\n종료하시겠습니까?" : "프로그램을 종료하고 로그아웃합니다.\n\n종료하시겠습니까?", "종료 확인", MessageBoxButtons.YesNo, isBetting ? MessageBoxIcon.Exclamation : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
		{
			FileLogger.Log("[Form1] 종료 취소됨 (사용자 선택)", "Info", "System");
			return false;
		}
		return true;
	}

	private async Task ShutdownAsync()
	{
		try
		{
			try
			{
				if (_autoBettingService.IsRunning)
				{
					await _autoBettingService.StopAsync();
				}
			}
			catch (Exception ex)
			{
				FileLogger.Log("[Form1] 종료 중 자동 베팅 정지 실패: " + ex.Message, "Warning", "System");
			}
			try
			{
				await SaveBettingSessionAsync();
			}
			catch (Exception ex2)
			{
				FileLogger.Log("[Form1] 종료 중 세션 저장 실패: " + ex2.Message, "Warning", "System");
			}
		}
		finally
		{
			try
			{
				await AuthService.LogoutAsync();
				FileLogger.Log("[Form1] 로그아웃 완료", "Info", "System");
			}
			catch (Exception ex3)
			{
				FileLogger.Log("[Form1] 로그아웃 실패: " + ex3.Message, "Error", "System");
			}
		}
		_autoBettingService.StateChanged -= AutoBetting_StateChanged;
		_autoBettingService.BetExecuted -= AutoBetting_BetExecuted;
		_autoBettingService.ResultJudged -= AutoBetting_ResultJudged;
		_autoBettingService.PendingBetAmountChanged -= AutoBetting_PendingBetAmountChanged;
		_autoBettingService.PeakBalanceReset -= AutoBetting_PeakBalanceReset;
		_autoBettingService.ProfitCooldownStarted -= AutoBetting_ProfitCooldownStarted;
		_autoBettingService.ProfitCooldownEnded -= AutoBetting_ProfitCooldownEnded;
		_autoBettingService.InactivityRefreshStarted -= AutoBetting_InactivityRefreshStarted;
		_autoBettingService.RefreshCompleted -= AutoBetting_RefreshCompleted;
		_autoBettingService.RoomGateChanged -= AutoBetting_RoomGateChanged;
		if (_noticePollingTimer != null)
		{
			_noticePollingTimer.Stop();
			_noticePollingTimer.Tick -= NoticePollingTimer_Tick;
			_noticePollingTimer.Dispose();
			_noticePollingTimer = null;
		}
		if (_noticeMarqueeTimer != null)
		{
			_noticeMarqueeTimer.Stop();
			_noticeMarqueeTimer.Tick -= NoticeMarqueeTimer_Tick;
			_noticeMarqueeTimer.Dispose();
			_noticeMarqueeTimer = null;
		}
		if (_elapsedTimer != null)
		{
			_elapsedTimer.Stop();
			_elapsedTimer.Tick -= ElapsedTimer_Tick;
			_elapsedTimer.Dispose();
			_elapsedTimer = null;
		}
		if (_balanceSyncTimer != null)
		{
			_balanceSyncTimer.Stop();
			_balanceSyncTimer.Tick -= BalanceSyncTimer_Tick;
			_balanceSyncTimer.Dispose();
			_balanceSyncTimer = null;
		}
		_alarmPlayer?.Dispose();
		_alarmPlayer = null;
		if (_pauseCountdownCts != null)
		{
			_pauseCountdownCts.Cancel();
			_pauseCountdownCts.Dispose();
			_pauseCountdownCts = null;
		}
		_messageParser.GameStarted -= Parser_GameStarted;
		_messageParser.TimerReceived -= Parser_TimerReceived;
		_messageParser.BetsOpened -= Parser_BetsOpened;
		_messageParser.BetsClosingSoon -= Parser_BetsClosingSoon;
		_messageParser.BetsClosed -= Parser_BetsClosed;
		_messageParser.DealingStarted -= Parser_DealingStarted;
		_messageParser.GameResultReceived -= Parser_GameResultReceived;
		_messageParser.WinReceived -= Parser_WinReceived;
		_messageParser.ShoeSummaryReceived -= Parser_ShoeSummaryReceived;
		_messageParser.CardDealt -= Parser_CardDealt;
		_messageParser.CardIncremented -= Parser_CardIncremented;
		_wsServer.ConnectionStateChanged -= WsServer_ConnectionStateChanged;
		_wsServer.WebSocketMessageCaptured -= WsServer_MessageCaptured;
		_wsServer.GameMessageReceived -= WsServer_GameMessageReceived;
		_wsServer.BalanceReceived -= WsServer_BalanceReceived;
		_wsServer.HttpBalanceReceived -= WsServer_HttpBalanceReceived;
		_wsServer.WsMessageBalanceReceived -= WsServer_WsMessageBalanceReceived;
		_wsServer.BalanceSelectorDetected -= WsServer_BalanceSelectorDetected;
		_wsServer.DomainMismatchDetected -= WsServer_DomainMismatchDetected;
		_cdpService.WebSocketMessageReceived -= CdpService_WebSocketMessageReceived;
		_cdpService.ConnectionStateChanged -= CdpService_ConnectionStateChanged;
		_cdpService.ErrorOccurred -= CdpService_ErrorOccurred;
		_cdpService.BalanceReceived -= CdpService_BalanceReceived;
		_cdpService.BalanceSelectorDetected -= CdpService_BalanceSelectorDetected;
		_cdpService.Dispose();
		_sessionMonitor.SessionInvalidated -= SessionMonitor_SessionInvalidated;
		_gameCatalogSync.CatalogChanged -= GameCatalogSync_CatalogChanged;
		_gameCatalogSync.Dispose();
		_sessionMonitor.Dispose();
		try
		{
			await DbLogService.FlushAsync();
		}
		catch
		{
		}
		DbLogService.Dispose();
		foreach (PragmaticSiteSession session in _siteManager.Sessions)
		{
			session.BalanceUpdated -= OnAuxSiteBalanceUpdated;
		}
		try
		{
			await _siteManager.DisconnectAllAsync();
		}
		catch
		{
		}
		_siteManager.Dispose();
		await _wsServer.StopAsync();
		_wsServer.Dispose();
		// 봇이 띄운 전용 브라우저를 모두 닫는다. 이렇게 해야 다음 기동 때 이미 로드된 스테일
		// 브라우저에 붙어 지갑 조회 등 로드-시점 요청을 놓치는 일이 없다(항상 새로 띄워 로드부터 감시).
		try
		{
			int closed = ChromeShortcutHelper.CloseBundledBrowsers();
			if (closed > 0)
			{
				FileLogger.Log($"[Form1] 종료 정리: 봇 전용 브라우저 {closed}개 닫음", "Info", "System");
			}
		}
		catch (Exception exKill)
		{
			FileLogger.Log("[Form1] 종료 시 브라우저 정리 실패: " + exKill.Message, "Warning", "System");
		}
	}

	// ── [임시] Evolution DOM 자동베팅 단독 테스트: Ctrl+Shift+E → tableId,칩,스팟[,횟수] 입력 → 1회 실베팅 ──
	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if (keyData == (Keys.Control | Keys.Shift | Keys.E))
		{
			_ = RunEvoBetTestAsync();
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	private async Task RunEvoBetTestAsync()
	{
		try
		{
			string? input = ShowEvoBetPrompt();
			if (string.IsNullOrWhiteSpace(input))
			{
				return;
			}
			string[] parts = input.Split(',');
			if (parts.Length < 3)
			{
				MessageBox.Show("형식: tableId,칩값,스팟[,횟수]\n예: pezjou3ltf6hvzjk,10k,뱅커,1", "Evo 테스트");
				return;
			}
			string tableId = parts[0].Trim();
			string chip = parts[1].Trim();
			string spot = parts[2].Trim();
			int units = (parts.Length >= 4 && int.TryParse(parts[3].Trim(), out var u) && u > 0) ? u : 1;
			string status = await _cdpService.PlaceEvolutionBetVerboseAsync(tableId, chip, spot, units).ConfigureAwait(continueOnCapturedContext: true);
			MessageBox.Show(status == "OK"
				? $"베팅 시도 성공: table={tableId} / chip={chip} / spot={spot} x{units}"
				: status, "Evo 테스트");
		}
		catch (Exception ex)
		{
			MessageBox.Show("Evo 테스트 예외: " + ex.Message, "Evo 테스트");
		}
	}

	private static string? ShowEvoBetPrompt()
	{
		using Form f = new Form
		{
			Text = "Evo 테스트 베팅  (tableId,칩,스팟[,횟수])",
			Width = 470,
			Height = 160,
			StartPosition = FormStartPosition.CenterScreen,
			FormBorderStyle = FormBorderStyle.FixedDialog,
			MinimizeBox = false,
			MaximizeBox = false
		};
		TextBox tb = new TextBox { Left = 12, Top = 15, Width = 430, Text = "pezjou3ltf6hvzjk,10k,뱅커,1" };
		Label lbl = new Label { Left = 12, Top = 45, Width = 430, Height = 30, Text = "스팟: 플레이어 / 뱅커 / 무 / 플레이어 페어 / 뱅커 페어 (Player/Banker/Tie 도 가능)" };
		Button okBtn = new Button { Text = "베팅", Left = 286, Top = 80, Width = 70, DialogResult = DialogResult.OK };
		Button cancelBtn = new Button { Text = "취소", Left = 366, Top = 80, Width = 70, DialogResult = DialogResult.Cancel };
		f.Controls.Add(tb);
		f.Controls.Add(lbl);
		f.Controls.Add(okBtn);
		f.Controls.Add(cancelBtn);
		f.AcceptButton = okBtn;
		f.CancelButton = cancelBtn;
		return (f.ShowDialog() == DialogResult.OK) ? tb.Text : null;
	}
}
