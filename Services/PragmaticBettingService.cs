using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using PragmaticBot.Models;

namespace PragmaticBot.Services;

public class PragmaticBettingService
{
	private readonly PragmaticWebSocketServer _wsServer;

	private readonly IPragmaticChromeDevToolsService? _cdpService;

	private readonly object _tcsLock = new object();

	private long _currentBetAttemptId;

	private long _activeBetAttemptId;

	private TaskCompletionSource<bool>? _betResultTcs;

	private TaskCompletionSource<bool>? _ppBetConfirmTcs;

	private TaskCompletionSource<bool>? _previousPpBetConfirmTcs;

	private const int ExtensionTimeoutMs = 5000;

	private const int PpServerTimeoutMs = 8000;

	private const int MaxRetries = 1;

	private const int BaseBackoffMs = 200;

	private const int SafetyMarginMs = 50;

	private const int GracePeriodMs = 1500;

	private const int LateObservationMs = 8000;

	private static readonly Dictionary<string, string> BetCodeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		{ "Player", "0" },
		{ "Banker", "1" }
	};

	private static readonly HashSet<string> KnownBetCodes = new HashSet<string>(StringComparer.Ordinal) { "0", "1", "10", "11" };

	private string? _inFlightTableId;

	private string? _inFlightBetCode;

	public event EventHandler<decimal>? BetAmountConfirmed;

	public event EventHandler<decimal>? LateConfirmationReceived;

	private static string ResolveBetCode(string betType)
	{
		if (!BetCodeMap.TryGetValue(betType, out string bc))
		{
			return "0";
		}
		return bc;
	}

	public PragmaticBettingService(PragmaticWebSocketServer wsServer, IPragmaticChromeDevToolsService? cdpService = null)
	{
		_wsServer = wsServer;
		_cdpService = cdpService;
		_wsServer.BetResultReceived += OnBetResultReceived;
	}

	public void SubscribeToParser(PragmaticMessageParser parser)
	{
		parser.BetConfirmed += OnPpBetConfirmed;
		parser.BetsConfirmed += OnPpBetsConfirmed;
		parser.BetValidationErrorReceived += OnPpBetValidationError;
	}

	private void OnPpBetValidationError(object? sender, PpBetValidationErrorArgs args)
	{
		string cause = (args.IsCommissionCodeRejected ? " [1057: 노커미션 방에 커미션 코드]" : (args.IsNoCommissionCodeRejected ? " [1058: 커미션 방에 노커미션 코드]" : (args.IsBelowTableMinimum ? " [1013: 최소 한도 미달 — 1057/1058의 후속 증상일 수 있음]" : "")));
		lock (_tcsLock)
		{
			if (string.IsNullOrEmpty(_inFlightTableId))
			{
				FileLogger.Log($"[PpBetting] {args.TableId}: 반려 {args.Code} 수신(대기 중인 베팅 없음 → 상태 변경 없음){cause}", "Warning", "Betting");
			}
			else if (!string.IsNullOrEmpty(args.TableId) && !string.Equals(args.TableId, _inFlightTableId, StringComparison.OrdinalIgnoreCase))
			{
				FileLogger.Log($"[PpBetting] 다른 방({args.TableId})의 반려 {args.Code} 무시{cause} — 현재 대기 중인 방은 {_inFlightTableId}", "Info", "Betting");
			}
			else if (KnownBetCodes.Contains(args.BetCode) && !string.IsNullOrEmpty(_inFlightBetCode) && !string.Equals(args.BetCode, _inFlightBetCode, StringComparison.Ordinal))
			{
				FileLogger.Log($"[PpBetting] {args.TableId}: 지난 코드(betCode={args.BetCode})에 대한 반려 {args.Code} 무시 — 현재 대기 중인 코드는 {_inFlightBetCode}", "Info", "Betting");
			}
			else
			{
				FileLogger.Log($"[PpBetting] ✗ {args.TableId}: PP 반려 통보 → 대기 즉시 종료 (code={args.Code}, desc={args.Description}, amount={args.Amount:N0}, betCode={args.BetCode}){cause}", "Warning", "Betting");
				_ppBetConfirmTcs?.TrySetResult(result: false);
			}
		}
	}

	private void OnBetResultReceived(object? sender, bool success)
	{
		lock (_tcsLock)
		{
			if (_currentBetAttemptId == _activeBetAttemptId)
			{
				_betResultTcs?.TrySetResult(success);
			}
		}
	}

	private void OnPpBetConfirmed(object? sender, PpBetConfirmArgs args)
	{
		lock (_tcsLock)
		{
			if (_currentBetAttemptId == _activeBetAttemptId)
			{
				if (args.IsConfirmed)
				{
					FileLogger.Log($"[PpBetting] ★ bet 확인 수신: amount={args.Amount}, betcode={args.BetCode}");
					BetAmountConfirmed?.Invoke(this, args.Amount);
					_ppBetConfirmTcs?.TrySetResult(result: true);
				}
				else
				{
					FileLogger.Log($"[PpBetting] ✗ bet 거절 수신: bc=false, amount={args.Amount}, betcode={args.BetCode}", "Warning", "Betting");
					_ppBetConfirmTcs?.TrySetResult(result: false);
				}
			}
			else
			{
				FileLogger.Log($"[PpBetting] 지연 bet 수신 → 이전 TCS에 전달: confirmed={args.IsConfirmed}, amount={args.Amount}");
				if (args.IsConfirmed)
				{
					BetAmountConfirmed?.Invoke(this, args.Amount);
				}
				_previousPpBetConfirmTcs?.TrySetResult(args.IsConfirmed);
			}
		}
	}

	private void OnPpBetsConfirmed(object? sender, PpBetsConfirmArgs args)
	{
		lock (_tcsLock)
		{
			if (_currentBetAttemptId == _activeBetAttemptId)
			{
				if (args.IsConfirmed)
				{
					FileLogger.Log($"[PpBetting] ★ bets 확인 수신: {args.Bets.Count}건");
					_ppBetConfirmTcs?.TrySetResult(result: true);
				}
				else
				{
					FileLogger.Log($"[PpBetting] ✗ bets 거절 수신: bc=false, {args.Bets.Count}건", "Warning", "Betting");
					_ppBetConfirmTcs?.TrySetResult(result: false);
				}
			}
			else
			{
				FileLogger.Log($"[PpBetting] 지연 bets 수신 → 이전 TCS에 전달: confirmed={args.IsConfirmed}, {args.Bets.Count}건");
				_previousPpBetConfirmTcs?.TrySetResult(args.IsConfirmed);
			}
		}
	}

	public string CreateBetXml(string tableId, string gameId, string ppUserId, string betType, decimal amount)
	{
		string betCode = ResolveBetCode(betType);
		long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		return $"<command channel=\"table-{tableId}\"><lpbet gm=\"mtb_desktop\" gId=\"{gameId}\" uId=\"{ppUserId}\" ck=\"{timestamp}\"><bet amt=\"{(int)amount}\" bc=\"{betCode}\" ck=\"{timestamp}\"/></lpbet></command>";
	}

	private static readonly Random _idRandom = new Random();

	private static string RandomToken(int length, string alphabet)
	{
		char[] buf = new char[length];
		lock (_idRandom)
		{
			for (int i = 0; i < length; i++)
			{
				buf[i] = alphabet[_idRandom.Next(alphabet.Length)];
			}
		}
		return new string(buf);
	}

	/// <summary>
	/// Evolution's bet command, mirroring a frame captured from the real client:
	/// <code>
	/// {"id":"91ixyycng9","type":"baccarat.playerBetRequest","args":{
	///   "tableId":"774SuperSpeedBac","gameId":"18d5b3b1…","replyId":"baccarat.playerBetRequest-714029248-1789534005226",
	///   "timestamp":1789534005226,"betTags":{…},"action":{"name":"Chips","chips":{"Banker":10000}},
	///   "correlationId":"a3ce43bdc7f21e265784"}}
	/// </code>
	/// No account id is needed — the socket is already bound to the session.
	/// </summary>
	public string CreateEvolutionBetJson(string tableId, string gameId, string betType, decimal amount)
	{
		string spot = string.Equals(betType, "Banker", StringComparison.OrdinalIgnoreCase) ? "Banker" : "Player";
		long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		string id = RandomToken(10, "abcdefghijklmnopqrstuvwxyz0123456789");
		string correlation = RandomToken(20, "abcdef0123456789");
		int replySeed = RandomToken(9, "123456789").Length == 9 ? Math.Abs(Guid.NewGuid().GetHashCode()) : 0;

		var payload = new
		{
			id,
			type = "baccarat.playerBetRequest",
			args = new
			{
				tableId,
				gameId,
				replyId = $"baccarat.playerBetRequest-{replySeed}-{timestamp}",
				timestamp,
				betTags = new { btTableView = "0", mwLayout = 9, openMwTables = 2 },
				action = new
				{
					name = "Chips",
					chips = new Dictionary<string, decimal> { [spot] = decimal.Truncate(amount) }
				},
				correlationId = correlation
			}
		};
		return JsonSerializer.Serialize(payload);
	}

	/// <summary>Evolution 베팅 방식(봇에서 선택). 기본 DOM. 두 방식이 동시에 동작하지 않는다.</summary>
	// Evolution 칩 액면(원). 큰 칩부터 greedy로 최소 개수 조합을 만든다.
	private static readonly (string Label, decimal Won)[] EvolutionChips = new[]
	{
		("1M", 1000000m), ("200k", 200000m), ("100k", 100000m), ("50k", 50000m), ("10k", 10000m), ("2000", 2000m)
	};

	// 금액을 최소 개수 칩 조합으로 분해 → "50k:1,10k:1" 형식. (2000 미만 잔돈은 버림)
	private static string BuildChipPlan(decimal amount)
	{
		var parts = new List<string>();
		decimal remaining = amount;
		foreach ((string label, decimal won) in EvolutionChips)
		{
			int count = (int)(remaining / won);
			if (count > 0)
			{
				parts.Add(label + ":" + count);
				remaining -= count * won;
			}
		}
		return string.Join(",", parts);
	}

	// Evolution 전용: 금액을 최소 개수 칩 조합으로 분해해 CDP DOM 자동화(신뢰 클릭)로 베팅.
	private async Task<BetResult> PlaceEvolutionBetViaDomAsync(TableState tableState, string betType, decimal amount)
	{
		IPragmaticChromeDevToolsService? cdp = _cdpService;
		if (cdp == null || !cdp.IsConnected)
		{
			return BetResult.Fail("CDP 미연결 — Evolution DOM 베팅 불가");
		}
		// 최소 칩(2000) 배수로 반올림 후 최소 개수 조합
		decimal rounded = Math.Round(amount / 2000m, MidpointRounding.AwayFromZero) * 2000m;
		if (rounded < 2000m)
		{
			rounded = 2000m;
		}
		string plan = BuildChipPlan(rounded);
		if (string.IsNullOrEmpty(plan))
		{
			return BetResult.Fail("칩 조합 실패 (금액 부족)");
		}
		string spot = string.Equals(betType, "Banker", StringComparison.OrdinalIgnoreCase) ? "Banker" : "Player";
		FileLogger.Log($"[PpBetting] Evolution DOM 베팅: table={tableState.TableId}, 조합=[{plan}]=₩{rounded:N0} (요청 {amount:N0}), spot={spot}", "Info", "Betting");
		string status = await cdp.PlaceEvolutionBetPlanAsync(tableState.TableId, spot, plan).ConfigureAwait(continueOnCapturedContext: false);
		// 반환 형식: "OK" 또는 "OK:<실제걸린금액>". 실제 금액이 있으면 상위 회계를 그 값으로 맞춘다.
		if (status == "OK" || status.StartsWith("OK:", StringComparison.Ordinal))
		{
			decimal? actual = null;
			if (status.Length > 3 && decimal.TryParse(status.Substring(3), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal a) && a > 0m)
			{
				actual = a;
			}
			return BetResult.Ok("EVO-DOM", actual);
		}
		FileLogger.Log($"[PpBetting] Evolution DOM 베팅 실패: table={tableState.TableId}, {status}", "Warning", "Betting");
		return BetResult.Fail("Evolution DOM 베팅 실패: " + status);
	}

	// Evolution 전용(추가 경로): 봇은 평문만 만들어 익스텐션으로 넘기고, 익스텐션이 담당 게임 소켓으로
	// 보낸다. 암호화가 필요한 사이트면 익스텐션이 암호화한다. DOM 경로는 손대지 않았다.
	private async Task<BetResult> PlaceEvolutionBetViaExtensionAsync(TableState tableState, string betType, decimal amount)
	{
		string json = CreateEvolutionBetJson(tableState.TableId, tableState.GameId, betType, amount);
		FileLogger.Log($"[PpBetting] Evolution 익스텐션 베팅: table={tableState.TableId}, {betType} ₩{amount:N0}", "Info", "Betting");

		// 1순위: CDP 직접 전달. 익스텐션의 ws://127.0.0.1 릴레이를 막는 보안/방화벽 환경에서도 동작한다.
		string cdpStatus = "CDP 미연결";
		IPragmaticChromeDevToolsService? cdp = _cdpService;
		if (cdp != null && cdp.IsConnected)
		{
			(bool cdpOk, string st) = await cdp.SendEvolutionBetViaCdpAsync(tableState.TableId, json)
				.ConfigureAwait(continueOnCapturedContext: false);
			if (cdpOk)
			{
				FileLogger.Log($"[PpBetting] Evolution 익스텐션 전송 성공(CDP): table={tableState.TableId}, status={st}", "Info", "Betting");
				return BetResult.Ok("EVO-EXT-CDP:" + st);
			}
			cdpStatus = st;
		}

		// 2순위: 릴레이 경로(bet_bridge). 릴레이가 열려 있는 환경의 폴백.
		(bool ok, string status) = await _wsServer.SendEvolutionBetAsync(tableState.TableId, json)
			.ConfigureAwait(continueOnCapturedContext: false);
		if (ok)
		{
			FileLogger.Log($"[PpBetting] Evolution 익스텐션 전송 성공(릴레이): table={tableState.TableId}, status={status}", "Info", "Betting");
			return BetResult.Ok("EVO-EXT:" + status);
		}
		FileLogger.Log($"[PpBetting] Evolution 익스텐션 전송 실패: table={tableState.TableId}, CDP={cdpStatus}, 릴레이={status}", "Warning", "Betting");
		return BetResult.Fail($"Evolution 익스텐션 전송 실패 (CDP={cdpStatus}, 릴레이={status})");
	}

	public async Task<BetResult> PlaceBetAsync(TableState tableState, string betType, decimal amount, string ppUserId)
	{
		if (tableState == null)
		{
			return BetResult.Fail("테이블 상태가 없습니다.");
		}
		if (!tableState.IsBettingOpen)
		{
			return BetResult.Fail($"현재 베팅이 불가능합니다. (상태: {tableState.Phase})");
		}
		if (string.IsNullOrEmpty(tableState.GameId))
		{
			return BetResult.Fail("게임 ID가 없습니다.");
		}
		if (!BetCodeMap.ContainsKey(betType))
		{
			return BetResult.Fail("잘못된 베팅 타입입니다: " + betType + " (Player/Banker만 가능)");
		}
		if (amount <= 0m)
		{
			return BetResult.Fail("베팅 금액은 0보다 커야 합니다.");
		}
		bool cdpReady = _cdpService?.IsConnected ?? false;
		if (!_wsServer.IsExtensionConnected && !cdpReady)
		{
			FileLogger.Log("[진단] 전송 경로 미연결 감지, 재연결 대기 시작 (table=" + tableState.TableId + ")", "Warning", "Betting");
			while (!_wsServer.IsExtensionConnected)
			{
				IPragmaticChromeDevToolsService? cdpService = _cdpService;
				if ((cdpService != null && cdpService.IsConnected) || !tableState.IsBettingOpen || !(tableState.RemainingTimeMs > 1000.0))
				{
					break;
				}
				await Task.Delay(200);
			}
			cdpReady = _cdpService?.IsConnected ?? false;
			if (!_wsServer.IsExtensionConnected && !cdpReady)
			{
				FileLogger.Log($"[진단] 전송 경로 확보 실패 - 베팅 마감 (table={tableState.TableId}, bet={betType}, amount={amount:N0})", "Warning", "Betting");
				return BetResult.Fail("Extension·CDP 어느 쪽도 연결되지 않았습니다.");
			}
			FileLogger.Log($"[진단] 전송 경로 확보됨 (table={tableState.TableId}, ext={_wsServer.IsExtensionConnected}, cdp={cdpReady}, remaining={tableState.RemainingTimeMs}ms)", "Info", "Betting");
		}
		if (!_wsServer.IsWebSocketCaptured && !cdpReady)
		{
			FileLogger.Log("[진단] WebSocket 미캡처 감지, 캡처 대기 시작 (table=" + tableState.TableId + ")", "Warning", "Betting");
			while (!_wsServer.IsWebSocketCaptured)
			{
				IPragmaticChromeDevToolsService? cdpService2 = _cdpService;
				if ((cdpService2 != null && cdpService2.IsConnected) || !tableState.IsBettingOpen || !(tableState.RemainingTimeMs > 1000.0))
				{
					break;
				}
				await Task.Delay(200);
			}
			if (!_wsServer.IsWebSocketCaptured)
			{
				IPragmaticChromeDevToolsService? cdpService3 = _cdpService;
				if (cdpService3 == null || !cdpService3.IsConnected)
				{
					FileLogger.Log("[진단] WebSocket 캡처 실패 - 베팅 마감 (table=" + tableState.TableId + ")", "Warning", "Betting");
					return BetResult.Fail("PP WebSocket이 캡처되지 않았습니다.");
				}
			}
			FileLogger.Log($"[진단] WebSocket 캡처됨 (table={tableState.TableId}, remaining={tableState.RemainingTimeMs}ms)", "Info", "Betting");
		}
		// Evolution speaks JSON over the same socket; Pragmatic speaks the <lpbet> XML command.
		// The choice is per table, not per account: an account granted both providers has PP and
		// Evolution tables side by side, and sending one's format to the other is silently ignored.
		bool isEvolution = PragmaticMessageParser.IsEvolutionTable(tableState.TableId)
			|| string.Equals(tableState.Provider, GameProviderNames.EvolutionKey, StringComparison.OrdinalIgnoreCase);
		FileLogger.Log($"[PpBetting] 전송 포맷 결정: table={tableState.TableId}, provider={(isEvolution ? "Evolution" : "PragmaticPlay")}", "Info", "Betting");
		if (isEvolution)
		{
			// 설정으로 고른 전송 방식. 기본값은 지금까지의 DOM 방식이라, 설정을 건드리지 않으면
			// 동작이 전혀 바뀌지 않는다.
			if (AppConfiguration.UseEvolutionExtensionBet)
			{
				return await PlaceEvolutionBetViaExtensionAsync(tableState, betType, amount).ConfigureAwait(continueOnCapturedContext: false);
			}
			// Evolution은 DOM 자동화(신뢰 클릭)로만 베팅한다 — 페이지의 진짜 베팅 코드를 실행시켜
			// 사이트가 알아서 올바르게(암호화 포함) 전송하게 하는 방식. (Pragmatic 경로는 아래 그대로 유지)
			return await PlaceEvolutionBetViaDomAsync(tableState, betType, amount).ConfigureAwait(continueOnCapturedContext: false);
		}
		string xmlMessage = CreateBetXml(tableState.TableId, tableState.GameId, ppUserId, betType, amount);
		string lastError = null;
		try
		{
			for (int attempt = 0; attempt <= 1; attempt++)
			{
				try
				{
					if (!HasEnoughTime(tableState, attempt))
					{
						if (attempt == 0)
						{
							return BetResult.Fail("베팅 시간이 부족합니다.");
						}
						break;
					}
					if (attempt > 0)
					{
						int backoffMs = 200 * (int)Math.Pow(2.0, attempt - 1);
						FileLogger.Log($"[PpBetting] 재시도 {attempt}/{1}, {backoffMs}ms 대기");
						await Task.Delay(backoffMs);
						if (!tableState.IsBettingOpen)
						{
							return BetResult.Fail("베팅이 마감되었습니다.");
						}
					}
					lock (_tcsLock)
					{
						_previousPpBetConfirmTcs = _ppBetConfirmTcs;
						_activeBetAttemptId = Interlocked.Increment(ref _currentBetAttemptId);
						_betResultTcs = new TaskCompletionSource<bool>();
						_ppBetConfirmTcs = new TaskCompletionSource<bool>();
						_inFlightTableId = tableState.TableId;
						_inFlightBetCode = ResolveBetCode(betType);
					}
					_wsServer.SendSnapshotBeforeBetAsync();
					Task<bool> extTask = _wsServer.SendBetMessageAsync(xmlMessage);
					Task<bool> cdpTask = ((_cdpService != null) ? _cdpService.SendBetDirectAsync(xmlMessage) : Task.FromResult(result: false));
					InlineArray2<Task<bool>> buffer = default(InlineArray2<Task<bool>>);
					buffer[0] = extTask;
					buffer[1] = cdpTask;
					await Task.WhenAll<bool>(buffer);
					bool extSent = extTask.Result;
					bool cdpSent = cdpTask.Result;
					if (!extSent && !cdpSent)
					{
						lastError = "Extension/CDP 양측 베팅 메시지 전송 실패";
						FileLogger.Log("[PpBetting] 양측 경로 모두 실패 - 재시도", "Warning", "Betting");
						continue;
					}
					if (cdpSent && !extSent)
					{
						FileLogger.Log("[PpBetting] Extension 실패 → CDP 경로로 전송 성공");
					}
					bool? sendResult = await WaitWithTimeoutAsync(_betResultTcs.Task, 5000);
					if (!sendResult.HasValue)
					{
						if (cdpSent)
						{
							FileLogger.Log("[PpBetting] Extension 응답 없음 (CDP 전송 성공 상태) - 진행");
						}
						else
						{
							FileLogger.Log($"[PpBetting] Extension {5}초 타임아웃 - 성공 간주");
						}
						goto IL_0997;
					}
					if (sendResult.Value || cdpSent)
					{
						goto IL_0997;
					}
					lastError = "WebSocket 메시지 전송 실패 (Extension 응답)";
					goto end_IL_05c6;
					IL_0997:
					Task.Run(async delegate
					{
						await Task.Delay(1500);
						await _wsServer.SendSnapshotAfterBetAsync(amount);
					});
					int dynamicTimeout = GetDynamicTimeout(tableState);
					bool? ppResult = await WaitWithTimeoutAsync(_ppBetConfirmTcs.Task, dynamicTimeout);
					if (!ppResult.HasValue)
					{
						FileLogger.Log($"[PpBetting] PP 타임아웃({dynamicTimeout}ms) → Grace Period {1500}ms 추가 대기");
						ppResult = await WaitWithTimeoutAsync(_ppBetConfirmTcs.Task, 1500);
						if (ppResult.HasValue)
						{
							FileLogger.Log($"[PpBetting] ★ Grace Period 내 확인 수신! (result={ppResult.Value})");
						}
					}
					if (!ppResult.HasValue)
					{
						TaskCompletionSource<bool> capturedTcs = _ppBetConfirmTcs;
						Task.Run(async delegate
						{
							try
							{
								bool? lateResult = await WaitWithTimeoutAsync(capturedTcs.Task, 8000);
								if (lateResult == true)
								{
									FileLogger.Log($"[PpBetting] ★★ 비동기 후속 확인 수신! (timeout 후 {8000}ms 이내)");
									LateConfirmationReceived?.Invoke(this, 0m);
								}
								else if (lateResult == false)
								{
									FileLogger.Log("[PpBetting] 비동기 후속: bet 거절 수신", "Warning", "Betting");
								}
								else
								{
									FileLogger.Log($"[PpBetting] 비동기 후속: {8000}ms 내 미수신 (최종 미확인)");
								}
							}
							catch (Exception ex2)
							{
								FileLogger.Log("[PpBetting] 비동기 후속 오류: " + ex2.Message, "Warning", "Betting");
							}
						});
						FileLogger.Log("[PpBetting] PP 서버 응답 타임아웃+Grace → 미확인 반환 (비동기 관찰 시작)", "Warning", "Betting");
						return BetResult.Unconfirmed(xmlMessage);
					}
					if (ppResult.Value)
					{
						if (attempt > 0)
						{
							FileLogger.Log($"[PpBetting] 베팅 성공 (재시도 {attempt}회 후)");
						}
						return BetResult.Ok(xmlMessage);
					}
					lastError = "PP 서버에서 베팅이 거절되었습니다.";
					FileLogger.Log("[PpBetting] " + lastError, "Warning", "Betting");
					return BetResult.Fail(lastError);
					end_IL_05c6:;
				}
				catch (Exception ex)
				{
					lastError = "베팅 전송 오류: " + ex.Message;
					FileLogger.Log($"[PpBetting] {lastError} (attempt {attempt + 1})", "Error", "Betting", ex.StackTrace);
				}
			}
			return BetResult.Fail(lastError ?? "알 수 없는 오류");
		}
		finally
		{
			lock (_tcsLock)
			{
				_inFlightTableId = null;
				_inFlightBetCode = null;
			}
		}
	}

	private static async Task<bool?> WaitWithTimeoutAsync(Task<bool> task, int timeoutMs)
	{
		Task timeoutTask = Task.Delay(timeoutMs);
		if (await Task.WhenAny(task, timeoutTask) == timeoutTask)
		{
			return null;
		}
		return await task;
	}

	private bool HasEnoughTime(TableState tableState, int attempt)
	{
		double remainingTimeMs = tableState.RemainingTimeMs;
		int requiredMs = ((attempt > 0) ? (200 * (int)Math.Pow(2.0, attempt - 1)) : 0) + 1000;
		return remainingTimeMs > (double)requiredMs;
	}

	private int GetDynamicTimeout(TableState tableState)
	{
		return Math.Clamp((int)(tableState.RemainingTimeMs - 50.0), 1000, 8000);
	}
}
