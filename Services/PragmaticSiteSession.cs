using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PragmaticBot.Models;

namespace PragmaticBot.Services;

public sealed class PragmaticSiteSession : IDisposable
{
	private readonly Func<string, string> _resolveTableKey;

	private bool _disposed;

	private string _lastPersistedWsUrl = string.Empty;

	private DateTime _lastRecoveryAt = DateTime.MinValue;

	private const int RoomRecoveryCooldownSeconds = 90;

	private int _recoveryRunning;

	public BettingSite Site { get; }

	public PragmaticWebSocketServer WsServer { get; }

	public PragmaticChromeDevToolsService Cdp { get; }

	public PragmaticMessageParser Parser { get; }

	public PragmaticBettingService Betting { get; }

	public ConcurrentDictionary<string, TableState> TableStates { get; } = new ConcurrentDictionary<string, TableState>();

	public decimal? Balance { get; private set; }

	public decimal StartBalance { get; private set; }

	public string PpUserId { get; private set; } = string.Empty;

	public int ActiveDebugPort { get; private set; }

	public bool IsCdpConnected => Cdp.IsConnected;

	public bool IsExtensionConnected => WsServer.IsExtensionConnected;

	public bool IsReady
	{
		get
		{
			if (!string.IsNullOrEmpty(PpUserId))
			{
				if (!IsCdpConnected)
				{
					return IsExtensionConnected;
				}
				return true;
			}
			return false;
		}
	}

	public event Action<int, decimal>? BalanceUpdated;

	public PragmaticSiteSession(BettingSite site, Func<string, string> resolveTableKey)
	{
		Site = site;
		_resolveTableKey = resolveTableKey;
		WsServer = new PragmaticWebSocketServer();
		Cdp = new PragmaticChromeDevToolsService();
		Parser = new PragmaticMessageParser();
		Betting = new PragmaticBettingService(WsServer, Cdp);
		Betting.SubscribeToParser(Parser);
		WireEvents();
	}

	private void WireEvents()
	{
		WsServer.GameMessageReceived += delegate(object? _, string json)
		{
			Parser.ParseMessage(json);
		};
		Cdp.WebSocketMessageReceived += delegate(object? _, PragmaticWebSocketMessageEventArgs e)
		{
			Parser.ParseMessage(e.Data);
		};
		Parser.GameStarted += delegate(object? _, PpGameStartedArgs e)
		{
			TableState orAddState = GetOrAddState(e.TableId);
			orAddState.GameId = e.GameId;
			orAddState.GameStartTime = e.StartTime;
			orAddState.Phase = BettingPhase.GameStarted;
			orAddState.LastUpdated = DateTime.Now;
		};
		Parser.TimerReceived += delegate(object? _, PpTimerArgs e)
		{
			TableState orAddState = GetOrAddState(e.TableId);
			orAddState.TimerSeconds = e.SecondsRemaining;
			orAddState.TimerReceivedAt = DateTime.Now;
			orAddState.LastUpdated = DateTime.Now;
		};
		Parser.BetsOpened += delegate(object? _, PpTableEventArgs e)
		{
			TableState orAddState = GetOrAddState(e.TableId);
			orAddState.Phase = BettingPhase.BetsOpen;
			if (!string.IsNullOrEmpty(e.GameId))
			{
				orAddState.GameId = e.GameId;
			}
			orAddState.LastUpdated = DateTime.Now;
		};
		Parser.BetsClosingSoon += delegate(object? _, PpTableEventArgs e)
		{
			TableState orAddState = GetOrAddState(e.TableId);
			orAddState.Phase = BettingPhase.BetsClosingSoon;
			orAddState.LastUpdated = DateTime.Now;
		};
		Parser.BetsClosed += delegate(object? _, PpTableEventArgs e)
		{
			TableState orAddState = GetOrAddState(e.TableId);
			orAddState.Phase = BettingPhase.BetsClosed;
			orAddState.LastUpdated = DateTime.Now;
		};
		Parser.DealingStarted += delegate(object? _, PpTableEventArgs e)
		{
			TableState orAddState = GetOrAddState(e.TableId);
			orAddState.Phase = BettingPhase.Dealing;
			orAddState.LastUpdated = DateTime.Now;
		};
		Parser.GameResultReceived += delegate(object? _, PpGameResultArgs e)
		{
			TableState orAddState = GetOrAddState(e.TableId);
			orAddState.Phase = BettingPhase.Result;
			orAddState.LastUpdated = DateTime.Now;
		};
		Parser.SeatReceived += delegate(object? _, PpSeatArgs e)
		{
			if (!string.IsNullOrEmpty(e.UserId) && string.IsNullOrEmpty(PpUserId))
			{
				PpUserId = e.UserId;
				FileLogger.Log($"[Site{Site.SiteSlot}] PP UserId 자동 감지: {e.UserId}", "Info", "MultiSite");
			}
		};
		WsServer.HttpBalanceReceived += delegate(object? _, decimal balance)
		{
			Cdp.NotifyHttpBalanceReceived();
			UpdateBalance(balance, "HTTP");
		};
		WsServer.WsMessageBalanceReceived += delegate(object? _, decimal balance)
		{
			UpdateBalance(balance, "WS");
		};
		Cdp.BalanceReceived += delegate(object? _, decimal balance)
		{
			UpdateBalance(balance, "DOM");
		};
		Cdp.HttpBalanceReceived += delegate(object? _, decimal balance)
		{
			UpdateBalance(balance, "CDP-HTTP");
		};
		WsServer.DomainMismatchDetected += delegate(object? _, (string CapturedUrl, List<string> RegisteredDomains) e)
		{
			PersistCapturedUrl(e.CapturedUrl);
		};
	}

	private TableState GetOrAddState(string ppTableId)
	{
		string key = _resolveTableKey(ppTableId);
		return TableStates.GetOrAdd(key, (string _) => new TableState
		{
			TableId = key
		});
	}

	private void UpdateBalance(decimal balance, string source)
	{
		if (!(balance <= 0m))
		{
			decimal? previous = Balance;
			Balance = balance;
			if (!previous.HasValue)
			{
				StartBalance = balance;
				FileLogger.Log($"[Site{Site.SiteSlot}] 잔액 최초 수신({source}): {balance:N0}", "Info", "MultiSite");
			}
			BalanceUpdated?.Invoke(Site.SiteSlot, balance);
		}
	}

	private void PersistCapturedUrl(string capturedUrl)
	{
		if (!string.IsNullOrWhiteSpace(capturedUrl) && !string.Equals(capturedUrl, _lastPersistedWsUrl, StringComparison.OrdinalIgnoreCase))
		{
			_lastPersistedWsUrl = capturedUrl;
			Task.Run(async delegate
			{
				await DomainSettingService.UpdateDomainAsync(capturedUrl, Site.SiteSlot).ConfigureAwait(continueOnCapturedContext: false);
			});
		}
	}

	public async Task<bool> ConnectAsync(IReadOnlyCollection<int>? excludePorts = null, IProgress<string>? progress = null)
	{
		_ = 2;
		try
		{
			await WsServer.StartAsync(Site.WsServerPort).ConfigureAwait(continueOnCapturedContext: false);
			if (WsServer.IsListening)
			{
				FileLogger.Log($"[Site{Site.SiteSlot}] WebSocket 서버 시작 (포트 {WsServer.ServerPort})", "Info", "MultiSite");
			}
			string startUrl = ((!string.IsNullOrWhiteSpace(Site.SiteUrl)) ? Site.SiteUrl : ((!string.IsNullOrWhiteSpace(Site.Domain)) ? ("https://" + Site.Domain) : null));
			int? port = await PragmaticSiteLauncher.EnsureSiteChromeRunningAsync(Site.SiteSlot, Site.BrowserType, Site.DebugPort, WsServer.IsListening ? WsServer.ServerPort : Site.WsServerPort, startUrl, excludePorts, 20, progress).ConfigureAwait(continueOnCapturedContext: false);
			if (!port.HasValue)
			{
				FileLogger.Log($"[Site{Site.SiteSlot}] {Site.BrowserType} 기동 실패", "Error", "MultiSite");
				return false;
			}
			ActiveDebugPort = port.Value;
			try
			{
				await Cdp.ConnectAsync(port.Value).ConfigureAwait(continueOnCapturedContext: false);
				FileLogger.Log($"[Site{Site.SiteSlot}] CDP 연결 성공 (포트 {port.Value})", "Info", "MultiSite");
			}
			catch (Exception ex)
			{
				FileLogger.Log($"[Site{Site.SiteSlot}] CDP 연결 실패 (Extension 경로로 대체): {ex.Message}", "Warning", "MultiSite");
			}
			if (!string.IsNullOrWhiteSpace(Site.BalanceSelector))
			{
				Cdp.SetCustomBalanceSelectors(Site.BalanceSelector.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList());
			}
			if (!string.IsNullOrWhiteSpace(Site.Domain))
			{
				WsServer.SetGameDomains(new List<string>(1) { Site.Domain });
			}
			return Cdp.IsConnected || WsServer.IsListening;
		}
		catch (Exception ex2)
		{
			FileLogger.Log($"[Site{Site.SiteSlot}] 연결 오류: {ex2.Message}", "Error", "MultiSite", ex2.StackTrace);
			return false;
		}
	}

	public async Task<bool> TryRecoverRoomAsync(string tableId)
	{
		if ((DateTime.Now - _lastRecoveryAt).TotalSeconds < 90.0)
		{
			return false;
		}
		if (Interlocked.CompareExchange(ref _recoveryRunning, 1, 0) != 0)
		{
			return false;
		}
		try
		{
			_lastRecoveryAt = DateTime.Now;
			bool sent = await WsServer.SendRefreshCommandAsync().ConfigureAwait(continueOnCapturedContext: false);
			if (!sent)
			{
				sent = await Cdp.ReloadGamePagesAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			FileLogger.Log(sent ? $"[Site{Site.SiteSlot}] {tableId} 방 구독 없음 → 게임 화면 새로고침으로 복구 시도" : $"[Site{Site.SiteSlot}] {tableId} 방 복구 실패 — Extension·CDP 모두 응답 없음", sent ? "Info" : "Warning", "MultiSite");
			return sent;
		}
		catch (Exception ex)
		{
			FileLogger.Log($"[Site{Site.SiteSlot}] 방 복구 오류: {ex.Message}", "Warning", "MultiSite");
			return false;
		}
		finally
		{
			Interlocked.Exchange(ref _recoveryRunning, 0);
		}
	}

	public async Task DisconnectAsync()
	{
		try
		{
			await Cdp.DisconnectAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			FileLogger.Log($"[Site{Site.SiteSlot}] CDP 해제 오류: {ex.Message}", "Warning", "MultiSite");
		}
		try
		{
			await WsServer.StopAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex2)
		{
			FileLogger.Log($"[Site{Site.SiteSlot}] WS 서버 종료 오류: {ex2.Message}", "Warning", "MultiSite");
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		try
		{
			Cdp.Dispose();
		}
		catch
		{
		}
		try
		{
			WsServer.Dispose();
		}
		catch
		{
		}
	}
}
