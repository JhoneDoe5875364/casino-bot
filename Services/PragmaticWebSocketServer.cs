using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Fleck;
using Timer = System.Timers.Timer;

namespace PragmaticBot.Services;

public class PragmaticWebSocketServer : IDisposable
{
	private WebSocketServer? _server;

	private IWebSocketConnection? _clientSocket;

	private IWebSocketConnection? _gameSocket;

	private readonly HashSet<IWebSocketConnection> _allSockets = new HashSet<IWebSocketConnection>();

	// Evolution 익스텐션처럼 평문만 릴레이하는 "수신 전용" 소켓. 명령 채널(_clientSocket/_gameSocket)
	// 후보에서 제외해, 봇 명령(SEND_BET, FETCH_BALANCE 등)이 이쪽으로 새지 않도록 한다.
	private readonly HashSet<IWebSocketConnection> _receiveOnlySockets = new HashSet<IWebSocketConnection>();

	private bool _disposed;

	private readonly object _lock = new object();

	private List<string> _gameDomains = new List<string>();

	private string _lastCapturedWebSocketUrl = "";

	private const int DefaultPort = 29876;

	private const int MaxPortAttempts = 5;

	private Timer? _heartbeatTimer;

	private const int HeartbeatIntervalMs = 30000;

	private string? _primaryHttpBalanceSessionId;

	private DateTime _primaryHttpBalanceLastSeen = DateTime.MinValue;

	private const int HttpSourceTimeoutSeconds = 60;

	private decimal _lastWsBalance;

	private static readonly string[] BalanceKeys = new string[12]
	{
		"balance", "credit", "fund", "amount", "money", "wallet", "cash", "coins", "total_balance", "totalBalance",
		"player_balance", "playerBalance"
	};

	public bool IsDomBalanceActive { get; set; }

	public bool IsListening { get; private set; }

	public int ServerPort { get; private set; }

	public bool IsExtensionConnected => _clientSocket?.IsAvailable ?? false;

	public bool IsWebSocketCaptured { get; internal set; }

	public event EventHandler<decimal>? WsMessageBalanceReceived;

	public event EventHandler<string>? MessageReceived;

	public event EventHandler<bool>? ConnectionStateChanged;

	public event EventHandler<CapturedMessage>? WebSocketMessageCaptured;

	public event EventHandler<string>? GameMessageReceived;

	public event EventHandler<bool>? BetResultReceived;

	public event EventHandler<decimal>? BalanceReceived;

	public event EventHandler<decimal>? HttpBalanceReceived;

	public event EventHandler<string>? BalanceSelectorDetected;

	public event EventHandler<(string CapturedUrl, List<string> RegisteredDomains)>? DomainMismatchDetected;

	// 자기 자신이 전에 접속했던 사이트(필터)가, 지금 붙어있는 새 사이트와 달라서 한 번도 안
	// 잡히는 잔여 케이스용 워치독. SetGameDomains 로 넣은 필터값이 바뀌지 않았는데 일정 시간 지나도
	// 캡처가 안 되면, 필터를 비워 "전체 캡처"로 한 번 더 시도한다(inject.js 가 이미 추적 중이던
	// 소켓을 소급 캡처 — 페이지 새로고침 불필요). 세대 번호로, 그 사이 다른 SetGameDomains 호출이나
	// 정상 캡처가 있었으면 아무 것도 안 한다.
	private int _domainFilterGeneration;

	private static readonly TimeSpan EmptyFilterFallbackDelay = TimeSpan.FromSeconds(15);

	public void SetGameDomains(List<string> domains)
	{
		_gameDomains = domains;
		SendDomainConfigAsync();
		ScheduleEmptyFilterFallback(domains);
	}

	private void ScheduleEmptyFilterFallback(List<string> domains)
	{
		if (domains == null || domains.Count == 0)
		{
			return;
		}
		int myGeneration = Interlocked.Increment(ref _domainFilterGeneration);
		_ = Task.Run(async delegate
		{
			await Task.Delay(EmptyFilterFallbackDelay).ConfigureAwait(continueOnCapturedContext: false);
			if (myGeneration != Volatile.Read(ref _domainFilterGeneration))
			{
				return;
			}
			if (IsWebSocketCaptured)
			{
				return;
			}
			FileLogger.Log($"[WebSocketServer] 기록된 도메인({string.Join(", ", domains)})으로 {EmptyFilterFallbackDelay.TotalSeconds:F0}초간 캡처되지 않음 — 필터를 비우고 전체 캡처로 재시도", "Warning", "Domain");
			_gameDomains = new List<string>();
			await SendDomainConfigAsync().ConfigureAwait(continueOnCapturedContext: false);
		});
	}

	public Task<bool> SendDomainConfigAsync()
	{
		List<IWebSocketConnection> sockets;
		lock (_lock)
		{
			sockets = _allSockets.Where((IWebSocketConnection s) => s.IsAvailable).ToList();
		}
		if (sockets.Count == 0)
		{
			return Task.FromResult(result: false);
		}
		try
		{
			string message = JsonSerializer.Serialize(new
			{
				type = "DOMAIN_CONFIG",
				domains = _gameDomains
			});
			foreach (IWebSocketConnection socket in sockets)
			{
				try
				{
					socket.Send(message);
				}
				catch (Exception)
				{
				}
			}
			return Task.FromResult(result: true);
		}
		catch (Exception)
		{
			return Task.FromResult(result: false);
		}
	}

	public async Task StartAsync(int port = 29876)
	{
		if (IsListening)
		{
			return;
		}
		if (_server != null)
		{
			try
			{
				_server.Dispose();
			}
			catch
			{
			}
			_server = null;
			await Task.Delay(500);
		}
		Exception lastEx = null;
		for (int i = 0; i < 5; i++)
		{
			int tryPort = port + i;
			if (IsPortInUse(tryPort))
			{
				continue;
			}
			try
			{
				FleckLog.Level = LogLevel.Error;
				_server = new WebSocketServer($"ws://127.0.0.1:{tryPort}");
				_server.RestartAfterListenError = true;
				_server.Start(delegate(IWebSocketConnection socket)
				{
					socket.OnOpen = delegate
					{
						lock (_lock)
						{
							_allSockets.Add(socket);
							if (_clientSocket == null || !_clientSocket.IsAvailable)
							{
								_clientSocket = socket;
							}
						}
						ConnectionStateChanged?.Invoke(this, e: true);
						SendDomainConfigAsync();
					};
					socket.OnClose = delegate
					{
						bool flag = false;
						lock (_lock)
						{
							_allSockets.Remove(socket);
							_receiveOnlySockets.Remove(socket);
							if (_gameSocket == socket)
							{
								_gameSocket = null;
								flag = true;
							}
							if (_clientSocket == socket)
							{
								_clientSocket = _allSockets.FirstOrDefault((IWebSocketConnection s) => s.IsAvailable && !_receiveOnlySockets.Contains(s));
							}
						}
						if (flag)
						{
							IsWebSocketCaptured = false;
						}
						ConnectionStateChanged?.Invoke(this, IsExtensionConnected);
					};
					socket.OnMessage = delegate(string message)
					{
						ProcessMessage(message, socket);
					};
					socket.OnError = delegate
					{
					};
				});
				ServerPort = tryPort;
				IsListening = true;
				StartHeartbeatTimer();
				return;
			}
			catch (Exception ex)
			{
				lastEx = ex;
				try
				{
					_server?.Dispose();
				}
				catch
				{
				}
				_server = null;
			}
		}
		IsListening = false;
		throw new InvalidOperationException($"WebSocket 서버 시작 실패 (포트 {port}~{port + 5 - 1} 모두 실패)\n오류: {lastEx?.Message}\n\n" + "해결 방법:\n1. 이전 PragmaticBot 프로세스가 완전히 종료되었는지 확인하세요.\n   (작업 관리자에서 PragmaticBot 프로세스 확인)\n" + $"2. 명령 프롬프트에서 포트 사용 확인: netstat -an | findstr {port}\n" + "3. Windows 예약 포트 확인: netsh interface ipv4 show excludedportrange protocol=tcp\n4. Windows 방화벽에서 해당 포트를 허용하세요.", lastEx);
	}

	private static bool IsPortInUse(int port)
	{
		return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any((IPEndPoint ep) => ep.Port == port);
	}

	public Task StopAsync()
	{
		IsListening = false;
		StopHeartbeatTimer();
		lock (_lock)
		{
			if (_clientSocket != null)
			{
				try
				{
					_clientSocket.Close();
				}
				catch
				{
				}
				_clientSocket = null;
			}
			_gameSocket = null;
			_allSockets.Clear();
		}
		IsWebSocketCaptured = false;
		_server?.Dispose();
		_server = null;
		return Task.CompletedTask;
	}

	// ------------------------------------------------------------------------------------------
	// Evolution 베팅 전송 (추가 경로). 기존 SendBetMessageAsync(프라그마틱)는 그대로 둔다.
	// 명령은 source:'evolution' 로 알린 "수신 전용" 소켓들에만 나가므로 프라그마틱 명령 채널과
	// 절대 섞이지 않는다. 결과는 익스텐션이 EVO_BET_RESULT 로 돌려준다.
	// ------------------------------------------------------------------------------------------
	private readonly ConcurrentDictionary<string, TaskCompletionSource<(bool Ok, string Status)>> _evoBetWaiters = new();

	public async Task<(bool Ok, string Status)> SendEvolutionBetAsync(string tableId, string plaintext, int timeoutMs = 4000)
	{
		List<IWebSocketConnection> targets;
		lock (_lock)
		{
			targets = _receiveOnlySockets.Where(s => s != null && s.IsAvailable).ToList();
		}
		if (targets.Count == 0)
		{
			return (false, "에볼루션 익스텐션 미연결");
		}

		string id = Guid.NewGuid().ToString("N").Substring(0, 12);
		var waiter = new TaskCompletionSource<(bool, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
		_evoBetWaiters[id] = waiter;
		try
		{
			string payload = JsonSerializer.Serialize(new
			{
				type = "EVO_SEND_BET",
				id,
				tableId,
				message = plaintext
			});
			int delivered = 0;
			foreach (IWebSocketConnection s in targets)
			{
				try { s.Send(payload); delivered++; } catch (Exception) { }
			}
			if (delivered == 0)
			{
				return (false, "명령 전달 실패");
			}
			// 명령은 모든 프레임으로 나가지만, 해당 테이블을 담당하는 소켓을 가진 프레임만 실제로
			// 전송한다. 나머지는 'no-socket' 을 돌려주므로 성공 응답만 기다린다.
			Task done = await Task.WhenAny(waiter.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
			if (done != waiter.Task)
			{
				return (false, "익스텐션 응답 없음(" + timeoutMs + "ms)");
			}
			return await waiter.Task.ConfigureAwait(false);
		}
		finally
		{
			_evoBetWaiters.TryRemove(id, out _);
		}
	}

	public async Task<bool> SendBetMessageAsync(string betMessage)
	{
		IWebSocketConnection socket;
		lock (_lock)
		{
			socket = _gameSocket ?? _clientSocket;
		}
		if (socket == null || !socket.IsAvailable)
		{
			return false;
		}
		try
		{
			string message = JsonSerializer.Serialize(new
			{
				type = "SEND_BET",
				message = betMessage
			});
			socket.Send(message);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public Task<bool> SendSnapshotBeforeBetAsync()
	{
		IWebSocketConnection socket;
		lock (_lock)
		{
			socket = _gameSocket ?? _clientSocket;
		}
		if (socket == null || !socket.IsAvailable)
		{
			return Task.FromResult(result: false);
		}
		try
		{
			socket.Send(JsonSerializer.Serialize(new
			{
				type = "SNAPSHOT_BEFORE_BET"
			}));
			return Task.FromResult(result: true);
		}
		catch
		{
			return Task.FromResult(result: false);
		}
	}

	public Task<bool> SendSnapshotAfterBetAsync(decimal betAmount)
	{
		IWebSocketConnection socket;
		lock (_lock)
		{
			socket = _gameSocket ?? _clientSocket;
		}
		if (socket == null || !socket.IsAvailable)
		{
			return Task.FromResult(result: false);
		}
		try
		{
			socket.Send(JsonSerializer.Serialize(new
			{
				type = "SNAPSHOT_AFTER_BET",
				betAmount = betAmount
			}));
			return Task.FromResult(result: true);
		}
		catch
		{
			return Task.FromResult(result: false);
		}
	}

	public Task<bool> SendFetchBalanceAsync()
	{
		IWebSocketConnection socket;
		lock (_lock)
		{
			socket = _gameSocket ?? _clientSocket;
		}
		if (socket == null || !socket.IsAvailable)
		{
			return Task.FromResult(result: false);
		}
		try
		{
			socket.Send(JsonSerializer.Serialize(new
			{
				type = "FETCH_BALANCE"
			}));
			return Task.FromResult(result: true);
		}
		catch
		{
			return Task.FromResult(result: false);
		}
	}

	public Task<bool> SendRefreshCommandAsync()
	{
		IWebSocketConnection socket;
		lock (_lock)
		{
			socket = _gameSocket ?? _clientSocket;
		}
		if (socket == null || !socket.IsAvailable)
		{
			return Task.FromResult(result: false);
		}
		try
		{
			socket.Send(JsonSerializer.Serialize(new
			{
				type = "REFRESH_GAME"
			}));
			return Task.FromResult(result: true);
		}
		catch
		{
			return Task.FromResult(result: false);
		}
	}

	private void StartHeartbeatTimer()
	{
		StopHeartbeatTimer();
		_heartbeatTimer = new Timer(30000.0);
		_heartbeatTimer.Elapsed += OnHeartbeatTimerElapsed;
		_heartbeatTimer.AutoReset = true;
		_heartbeatTimer.Start();
	}

	private void StopHeartbeatTimer()
	{
		if (_heartbeatTimer != null)
		{
			_heartbeatTimer.Stop();
			_heartbeatTimer.Elapsed -= OnHeartbeatTimerElapsed;
			_heartbeatTimer.Dispose();
			_heartbeatTimer = null;
		}
	}

	private void OnHeartbeatTimerElapsed(object? sender, ElapsedEventArgs e)
	{
		if (!IsListening || !IsExtensionConnected)
		{
			return;
		}
		IWebSocketConnection socket;
		lock (_lock)
		{
			socket = _clientSocket;
		}
		if (socket == null || !socket.IsAvailable)
		{
			return;
		}
		try
		{
			string message = JsonSerializer.Serialize(new
			{
				type = "PING",
				timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});
			socket.Send(message);
		}
		catch
		{
		}
	}

	private void ProcessMessage(string message, IWebSocketConnection socket)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(message);
			JsonElement root = doc.RootElement;
			if (!root.TryGetProperty("type", out var typeEl))
			{
				return;
			}
			string type = typeEl.GetString();
			switch (type)
			{
			case "WS_CAPTURED":
			{
				string wsUrl = (root.TryGetProperty("url", out var urlEl) ? (urlEl.GetString() ?? "") : "");
				bool domainMismatch = _gameDomains.Count > 0 && !_gameDomains.Any((string d) => wsUrl.Contains(d, StringComparison.OrdinalIgnoreCase));
				bool urlChanged = !string.Equals(_lastCapturedWebSocketUrl, wsUrl, StringComparison.Ordinal);
				if (domainMismatch | urlChanged)
				{
					DomainMismatchDetected?.Invoke(this, (wsUrl, new List<string>(_gameDomains)));
					try
					{
						string newHost = new Uri(wsUrl).Host;
						int num = 1;
						List<string> list = new List<string>(num);
						CollectionsMarshal.SetCount(list, num);
						CollectionsMarshal.AsSpan(list)[0] = newHost;
						_gameDomains = list;
					}
					catch (UriFormatException)
					{
					}
					_lastCapturedWebSocketUrl = wsUrl;
				}
				lock (_lock)
				{
					_gameSocket = socket;
					if (_clientSocket == null || !_clientSocket.IsAvailable)
					{
						_clientSocket = socket;
					}
				}
				IsWebSocketCaptured = true;
				break;
			}
			case "WS_MESSAGE":
			{
				string direction = (root.TryGetProperty("direction", out var dirEl) ? (dirEl.GetString() ?? "unknown") : "unknown");
				string wsData = (root.TryGetProperty("data", out var dataEl) ? (dataEl.GetString() ?? "") : "");
				string msgUrl = (root.TryGetProperty("url", out var msgUrlEl) ? (msgUrlEl.GetString() ?? "") : "");
				WebSocketMessageCaptured?.Invoke(this, new CapturedMessage
				{
					Timestamp = DateTime.Now,
					Direction = direction,
					Url = msgUrl,
					Data = ((wsData.Length > 500) ? (wsData.Substring(0, 500) + "...") : wsData),
					RawLength = wsData.Length
				});
				if (direction == "receive" && !string.IsNullOrEmpty(wsData))
				{
					GameMessageReceived?.Invoke(this, wsData);
					if (!IsDomBalanceActive)
					{
						TryParseBalanceFromWsMessage(wsData);
					}
				}
				break;
			}
			case "BET_RESULT":
			{
				bool betSuccess = root.TryGetProperty("success", out var successEl) && successEl.GetBoolean();
				BetResultReceived?.Invoke(this, betSuccess);
				break;
			}
			case "BALANCE_UPDATE":
			{
				if (root.TryGetProperty("balance", out var balEl) && balEl.TryGetDecimal(out var bal))
				{
					BalanceReceived?.Invoke(this, bal);
				}
				break;
			}
			case "HTTP_BALANCE_UPDATE":
			{
				if (!root.TryGetProperty("balance", out var httpBalEl) || !httpBalEl.TryGetDecimal(out var httpBal) || !(httpBal > 0m))
				{
					break;
				}
				string httpUrl = (root.TryGetProperty("url", out var httpUrlEl) ? (httpUrlEl.GetString() ?? "") : "");
				string sessionId = ExtractSessionId(httpUrl);
				if (!string.IsNullOrEmpty(sessionId))
				{
					if (_primaryHttpBalanceSessionId == null)
					{
						_primaryHttpBalanceSessionId = sessionId;
						FileLogger.Log("[HTTP Balance] 1순위 세션 확정: " + sessionId);
					}
					else if (sessionId != _primaryHttpBalanceSessionId)
					{
						if (!((DateTime.Now - _primaryHttpBalanceLastSeen).TotalSeconds > 60.0))
						{
							break;
						}
						_primaryHttpBalanceSessionId = sessionId;
						FileLogger.Log("[HTTP Balance] 세션 전환: " + sessionId + " (이전 세션 타임아웃)");
					}
					_primaryHttpBalanceLastSeen = DateTime.Now;
				}
				FileLogger.Log($"[HTTP Balance] 잔액 감지: {httpBal:N0}원 (URL: {httpUrl})");
				HttpBalanceReceived?.Invoke(this, httpBal);
				break;
			}
			case "BALANCE_SELECTOR_DETECTED":
			{
				string detectedSelector = (root.TryGetProperty("selector", out var selEl) ? selEl.GetString() : null);
				if (!string.IsNullOrEmpty(detectedSelector))
				{
					FileLogger.Log("[DOM Diff] 잔액 셀렉터 감지: " + detectedSelector);
					BalanceSelectorDetected?.Invoke(this, detectedSelector);
				}
				if (root.TryGetProperty("balance", out var detBalEl) && detBalEl.TryGetDecimal(out var detBal) && detBal > 0m)
				{
					BalanceReceived?.Invoke(this, detBal);
				}
				break;
			}
			case "GAME_STATE":
			{
				if (root.TryGetProperty("data", out var gsDataEl))
				{
					string gsData = gsDataEl.GetString() ?? "";
					WebSocketMessageCaptured?.Invoke(this, new CapturedMessage
					{
						Timestamp = DateTime.Now,
						Direction = "receive",
						Url = "GAME_STATE",
						Data = ((gsData.Length > 500) ? (gsData.Substring(0, 500) + "...") : gsData),
						RawLength = gsData.Length
					});
				}
				break;
			}
			default:
				MessageReceived?.Invoke(this, message);
				break;
			case "EXTENSION_CONNECTED":
			{
				bool isEvolution = root.TryGetProperty("source", out var srcEl)
					&& srcEl.ValueKind == JsonValueKind.String
					&& string.Equals(srcEl.GetString(), "evolution", StringComparison.OrdinalIgnoreCase);
				if (isEvolution)
				{
					lock (_lock)
					{
						_receiveOnlySockets.Add(socket);
							// 명령 채널로 잡혀 있었다면 수신전용이 아닌 다른 소켓으로 재배정(없으면 null)
						if (_clientSocket == socket)
						{
							_clientSocket = _allSockets.FirstOrDefault((IWebSocketConnection s) => s.IsAvailable && !_receiveOnlySockets.Contains(s));
						}
						if (_gameSocket == socket)
						{
							_gameSocket = null;
						}
					}
				}
				break;
			}
			case "EVO_BET_RESULT":
			{
				// 성공 응답만 기다림을 깨운다. 담당 소켓이 없는 프레임의 'no-socket' 으로 끝내면
				// 실제로 보낸 프레임의 응답을 놓친다.
				string evoId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
				bool evoOk = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
				string evoStatus = root.TryGetProperty("status", out var stEl) ? stEl.GetString() ?? "" : "";
				if (!string.IsNullOrEmpty(evoId) && evoOk
					&& _evoBetWaiters.TryGetValue(evoId, out var evoWaiter))
				{
					evoWaiter.TrySetResult((true, evoStatus));
				}
				break;
			}
			case "PONG":
				break;
			}
		}
		catch (Exception)
		{
		}
	}

	private void TryParseBalanceFromWsMessage(string wsData)
	{
		try
		{
			if (string.IsNullOrEmpty(wsData) || (wsData[0] != '{' && wsData[0] != '['))
			{
				return;
			}
			using JsonDocument doc = JsonDocument.Parse(wsData);
			decimal? bestBalance = TryExtractBalance(doc.RootElement);
			if (bestBalance.HasValue && bestBalance.Value > 0m)
			{
				if (bestBalance.Value != _lastWsBalance)
				{
					_lastWsBalance = bestBalance.Value;
					FileLogger.Log($"[WS Parse] 잔액 감지: {bestBalance.Value:N0}원 (WS 메시지 파싱)");
				}
				WsMessageBalanceReceived?.Invoke(this, bestBalance.Value);
			}
		}
		catch (JsonException)
		{
		}
		catch (Exception)
		{
		}
	}

	private static decimal? TryExtractBalance(JsonElement element)
	{
		switch (element.ValueKind)
		{
		case JsonValueKind.Object:
			foreach (JsonProperty prop in element.EnumerateObject())
			{
				string key = prop.Name.ToLowerInvariant();
				if (BalanceKeys.Any((string bk) => key.Contains(bk)))
				{
					if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out var numVal) && numVal > 0m)
					{
						return numVal;
					}
					if (prop.Value.ValueKind == JsonValueKind.String)
					{
						string str = prop.Value.GetString();
						if (!string.IsNullOrEmpty(str) && decimal.TryParse(Regex.Replace(str, "[^0-9.,]", "").Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) && parsed > 0m)
						{
							return parsed;
						}
					}
				}
				if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
				{
					decimal? nested2 = TryExtractBalance(prop.Value);
					if (nested2.HasValue)
					{
						return nested2;
					}
				}
			}
			break;
		case JsonValueKind.Array:
			foreach (JsonElement item in element.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
				{
					decimal? nested = TryExtractBalance(item);
					if (nested.HasValue)
					{
						return nested;
					}
				}
			}
			break;
		}
		return null;
	}

	private static string? ExtractSessionId(string url)
	{
		int idx = url.IndexOf("JSESSIONID=", StringComparison.OrdinalIgnoreCase);
		if (idx < 0)
		{
			return null;
		}
		int start = idx + "JSESSIONID=".Length;
		int end = url.IndexOfAny(new char[3] { '&', ' ', '#' }, start);
		if (end >= 0)
		{
			int num = start;
			return url.Substring(num, end - num);
		}
		return url.Substring(start);
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			IsListening = false;
			StopHeartbeatTimer();
			lock (_lock)
			{
				if (_clientSocket != null)
				{
					try
					{
						_clientSocket.Close();
					}
					catch
					{
					}
					_clientSocket = null;
				}
				_gameSocket = null;
				_allSockets.Clear();
			}
			IsWebSocketCaptured = false;
			try
			{
				_server?.Dispose();
			}
			catch
			{
			}
			_server = null;
		}
		GC.SuppressFinalize(this);
	}
}
public class CapturedMessage
{
	public DateTime Timestamp { get; set; }

	public string Direction { get; set; } = string.Empty;

	public string Url { get; set; } = string.Empty;

	public string Data { get; set; } = string.Empty;

	public int RawLength { get; set; }
}
