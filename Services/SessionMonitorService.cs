using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Timer = System.Windows.Forms.Timer;

namespace PragmaticBot.Services;

public class SessionMonitorService : IDisposable
{
	private readonly Timer _timer;

	private HubConnection? _hubConnection;

	private bool _disposed;

	private bool _isChecking;

	private const int CheckIntervalMs = 10000;

	/// <summary>Heartbeats between diagnostic-mode refreshes — 6 × 10s, so about once a minute.</summary>
	private const int DiagnosticCheckEveryTicks = 6;

	private int _sinceDiagnosticCheck = DiagnosticCheckEveryTicks;

	/// <summary>서버 접속 실패가 이 시간 이상 연속되면 봇을 정지·로그아웃한다(일시적 1~2회는 무시).</summary>
	private const double ServerDownGraceSeconds = 90.0;

	/// <summary>연속 서버 접속 실패 구간의 첫 실패 시각(UTC ticks). 0 = 실패 streak 없음.</summary>
	private long _firstConnFailTicksUtc;

	public event EventHandler<string>? SessionInvalidated;

	public SessionMonitorService()
	{
		_timer = new Timer
		{
			Interval = 10000
		};
		_timer.Tick += Timer_Tick;
	}

	public void Start()
	{
		_timer.Start();
	}

	public void Stop()
	{
		_timer.Stop();
	}

	public async Task ConnectSignalRAsync(string hubUrl, string jwtToken)
	{
		_hubConnection = ((IHubConnectionBuilder)new HubConnectionBuilder()).WithUrl(hubUrl, (Action<HttpConnectionOptions>)delegate(HttpConnectionOptions options)
		{
			// 하트비트로 토큰이 갱신되므로, 재연결 시 항상 최신 토큰을 쓴다(만료된 초기 토큰 고정 방지).
			options.AccessTokenProvider = () => Task.FromResult(AuthService.Api?.JwtToken ?? jwtToken);
		}).WithAutomaticReconnect().Build();
		_hubConnection.On("ForceLogout", delegate(string message)
		{
			_timer.Stop();
			SessionInvalidated?.Invoke(this, message);
		});
		try
		{
			await _hubConnection.StartAsync();
		}
		catch (Exception)
		{
		}
	}

	private async void Timer_Tick(object? sender, EventArgs e)
	{
		if (_isChecking)
		{
			return;
		}
		_isChecking = true;
		try
		{
			ApiClient api = AuthService.Api;
			if (api != null)
			{
				await api.HeartbeatAsync();
				// 서버 응답 성공 → 접속 실패 streak 초기화(정지 카운트다운 리셋)
				System.Threading.Interlocked.Exchange(ref _firstConnFailTicksUtc, 0L);
				// the same poll tells us whether this account is in diagnostic mode, so Info logging
				// can be switched on from the admin site without restarting the bot
				if (++_sinceDiagnosticCheck >= DiagnosticCheckEveryTicks)
				{
					_sinceDiagnosticCheck = 0;
					DbLogService.DiagnosticLogging = (await api.CheckSessionAsync()).DiagnosticLogging;
				}
			}
		}
		catch (ApiSessionConflictException ex)
		{
			// 409: 다른 기기에서 로그인됨 → 현재 세션 종료
			_timer.Stop();
			SessionInvalidated?.Invoke(this, ex.Message);
		}
		catch (ApiUnauthorizedException ex)
		{
			// 401: 사용 기간 만료 / 비활성 / 계정 없음 → 강제 로그아웃.
			// (예전엔 아래 catch(Exception) 에서 조용히 삼켜져, 만료돼도 봇이 계속 돌았다.)
			// 서버는 만료 계정의 재로그인도 거부하므로, 관리자가 기간을 연장해줄 때까지 다시 들어올 수 없다.
			_timer.Stop();
			SessionInvalidated?.Invoke(this, ex.Message);
		}
		catch (Exception ex)
		{
			// 서버 접속 실패(서버 다운/연결 거부/타임아웃)가 ServerDownGraceSeconds(90초) 이상 연속되면
			// 봇을 정지·로그아웃한다. 일시적 1~2회 실패는 무시(streak 시작만 기록).
			if (IsConnectionError(ex))
			{
				long nowTicks = DateTime.UtcNow.Ticks;
				long prev = System.Threading.Interlocked.CompareExchange(ref _firstConnFailTicksUtc, nowTicks, 0L);
				long firstTicks = (prev == 0L) ? nowTicks : prev;
				double downSeconds = (DateTime.UtcNow - new DateTime(firstTicks, DateTimeKind.Utc)).TotalSeconds;
				if (downSeconds >= ServerDownGraceSeconds)
				{
					System.Threading.Interlocked.Exchange(ref _firstConnFailTicksUtc, 0L);
					_timer.Stop();
					SessionInvalidated?.Invoke(this, $"서버에 접속할 수 없습니다({(int)downSeconds}초 연속). 세션을 종료합니다.");
				}
			}
			// 연결 오류가 아닌 다른 예외는 기존대로 무시한다.
		}
		finally
		{
			_isChecking = false;
		}
	}

	/// <summary>서버 접속 실패(서버 다운/연결 거부/타임아웃 등)인지 판별한다.</summary>
	private static bool IsConnectionError(Exception ex)
	{
		for (Exception? e = ex; e != null; e = e.InnerException)
		{
			if (e is System.Net.Http.HttpRequestException
				|| e is System.Net.Sockets.SocketException
				|| e is TaskCanceledException
				|| e is TimeoutException)
			{
				return true;
			}
			string m = e.Message ?? string.Empty;
			if (m.Contains("refused", StringComparison.OrdinalIgnoreCase)
				|| m.Contains("actively", StringComparison.OrdinalIgnoreCase)
				|| m.Contains("target machine", StringComparison.OrdinalIgnoreCase)
				|| m.Contains("connection could be made", StringComparison.OrdinalIgnoreCase)
				|| m.Contains("연결", StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_timer.Stop();
			_timer.Tick -= Timer_Tick;
			_timer.Dispose();
			if (_hubConnection != null)
			{
				_hubConnection.DisposeAsync();
				_hubConnection = null;
			}
			_disposed = true;
		}
		GC.SuppressFinalize(this);
	}
}
