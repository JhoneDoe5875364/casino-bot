using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using PragmaticBot.Models.Api;

namespace PragmaticBot.Services;

public class ApiClient : IDisposable
{
	private readonly HttpClient _httpClient;

	private readonly JsonSerializerOptions _jsonOptions;

	private string? _jwtToken;

	private bool _disposed;

	public string? JwtToken => _jwtToken;

	public ApiClient(string baseUrl)
	{
		_httpClient = new HttpClient
		{
			BaseAddress = new Uri(baseUrl.TrimEnd('/')),
			Timeout = TimeSpan.FromSeconds(8L)
		};
		_jsonOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};
	}

	public void SetToken(string token)
	{
		_jwtToken = token;
		_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
	}

	public void ClearToken()
	{
		_jwtToken = null;
		_httpClient.DefaultRequestHeaders.Authorization = null;
	}

	public async Task<LoginResponse> LoginAsync(string username, string password)
	{
		LoginRequest request = new LoginRequest(username, password, AppVersion.Current);
		LoginResponse response = await PostAsync<LoginResponse>("api/auth/login", request);
		SetToken(response.Token);
		return response;
	}

	public async Task<SessionCheckResponse> CheckSessionAsync()
	{
		return await GetAsync<SessionCheckResponse>("api/auth/session/check");
	}

	public async Task HeartbeatAsync()
	{
		// 서버가 갱신 토큰을 주면 즉시 교체한다(짧은 토큰 수명에도 켜진 봇은 끊기지 않게).
		HeartbeatResponse resp = await PostAsync<HeartbeatResponse>("api/auth/heartbeat", new { });
		if (resp != null && !string.IsNullOrEmpty(resp.Token))
		{
			SetToken(resp.Token);
		}
	}

	public async Task LogoutAsync()
	{
		try
		{
			await PostAsync("api/auth/logout", new { });
		}
		catch
		{
		}
		finally
		{
			ClearToken();
		}
	}

	public async Task<DomainSettingDto> GetDomainAsync(string provider = "PragmaticPlay")
	{
		return await GetAsync<DomainSettingDto>("api/domain?provider=" + Uri.EscapeDataString(provider));
	}

	public async Task<List<DomainSiteDto>> GetDomainSitesAsync(string provider = "PragmaticPlay")
	{
		return await GetAsync<List<DomainSiteDto>>("api/domain/sites?provider=" + Uri.EscapeDataString(provider));
	}

	public async Task SaveDomainSitesAsync(List<DomainSiteDto> sites)
	{
		await PostAsync("api/domain/sites", sites);
	}

	public async Task UpdateWebSocketUrlAsync(string webSocketUrl, string provider = "PragmaticPlay", int siteSlot = 0)
	{
		await PutAsync($"api/domain/websocket-url?provider={Uri.EscapeDataString(provider)}&siteSlot={siteSlot}", new
		{
			WebSocketUrl = webSocketUrl
		});
	}

	public async Task UpdateBalanceSelectorAsync(string selector)
	{
		await PutAsync("api/domain/balance-selector?provider=PragmaticPlay", new
		{
			Selector = selector
		});
	}

	// 서식 — 로컬 우선(있으면 즉시), 없으면 서버에서 받아 로컬에 캐시. 저장은 로컬 즉시 + 서버 반영.
	public async Task<List<FormatSettingDto>> GetFormatsAsync()
	{
		string? uid = AuthService.CurrentUserId;
		List<FormatSettingDto>? local = LocalSettingsStore.GetFormats(uid);
		if (local != null)
		{
			return local;
		}
		List<FormatSettingDto> server = await GetAsync<List<FormatSettingDto>>("api/settings/formats");
		LocalSettingsStore.SetFormats(uid, server);
		return server;
	}

	public async Task SaveFormatsAsync(List<FormatSettingDto> formats)
	{
		LocalSettingsStore.SetFormats(AuthService.CurrentUserId, formats);
		await PostAsync("api/settings/formats", formats);
	}

	// 베팅금(타입별) — 로컬 우선. 저장은 타입별로 로컬 즉시 반영 후 서버 반영.
	public async Task<List<BetAmountSettingDto>> GetBetAmountsAsync(int betAmountType = 0)
	{
		string? uid = AuthService.CurrentUserId;
		List<BetAmountSettingDto>? local = LocalSettingsStore.GetBetAmounts(uid, betAmountType);
		if (local != null)
		{
			return local;
		}
		List<BetAmountSettingDto> server = await GetAsync<List<BetAmountSettingDto>>($"api/settings/bet-amounts?betAmountType={betAmountType}");
		LocalSettingsStore.SetBetAmounts(uid, betAmountType, server);
		return server;
	}

	public async Task SaveBetAmountsAsync(List<BetAmountSettingDto> betAmounts)
	{
		string? uid = AuthService.CurrentUserId;
		if (uid != null && betAmounts != null)
		{
			foreach (IGrouping<int, BetAmountSettingDto> g in betAmounts.GroupBy(b => b.BetAmountType))
			{
				LocalSettingsStore.SetBetAmounts(uid, g.Key, g.ToList());
			}
		}
		await PostAsync("api/settings/bet-amounts", betAmounts);
	}

	/// <summary>
	/// 로그인 직후 백그라운드로 호출된다. 서버의 서식·베팅금을 당겨 로컬을 덮어써서
	/// (1) 관리자가 서버에서 바꾼 설정을 반영하고 (2) 설정 화면이 처음부터 로컬(빠름)로 열리게 한다.
	/// 실패는 조용히 무시 — 로컬이 없으면 각 화면이 그때 서버에서 받아 채운다.
	/// </summary>
	public async Task SeedSettingsFromServerAsync()
	{
		string? uid = AuthService.CurrentUserId;
		if (string.IsNullOrEmpty(uid))
		{
			return;
		}
		try
		{
			List<FormatSettingDto> formats = await GetAsync<List<FormatSettingDto>>("api/settings/formats");
			LocalSettingsStore.SetFormats(uid, formats);
		}
		catch
		{
		}
		// 봇이 쓰는 베팅금 타입들(행렬 제외)을 미리 당겨 로컬에 채운다.
		int[] betTypes = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
		foreach (int t in betTypes)
		{
			try
			{
				List<BetAmountSettingDto> amounts = await GetAsync<List<BetAmountSettingDto>>($"api/settings/bet-amounts?betAmountType={t}");
				LocalSettingsStore.SetBetAmounts(uid, t, amounts);
			}
			catch
			{
			}
		}
		// 행렬(11~14) 셀·행규칙도 미리 당겨 로컬에 채운다.
		int[] matrixTypes = { 11, 12, 13, 14 };
		foreach (int mt in matrixTypes)
		{
			try
			{
				List<MatrixCellDto> cells = await GetAsync<List<MatrixCellDto>>($"api/settings/matrix-cells?betAmountType={mt}");
				LocalSettingsStore.SetMatrixCells(uid, mt, cells);
			}
			catch
			{
			}
			try
			{
				List<MatrixRowRuleDto> rules = await GetAsync<List<MatrixRowRuleDto>>($"api/settings/matrix-row-rules?betAmountType={mt}");
				LocalSettingsStore.SetMatrixRowRules(uid, mt, rules);
			}
			catch
			{
			}
		}
		// 예외서식·방게이트·한도·방쿨다운도 로컬에 씨딩.
		try { LocalSettingsStore.SetExceptionFormats(uid, await GetAsync<List<ExceptionFormatDto>>("api/settings/exception-formats")); } catch { }
		try { LocalSettingsStore.SetRoomGateFormats(uid, await GetAsync<List<RoomGateFormatDto>>("api/settings/room-gate-formats")); } catch { }
		try { LocalSettingsStore.SetLimits(uid, await GetAsync<LimitSettingDto>("api/settings/limits")); } catch { }
		try { LocalSettingsStore.SetRoomCooldown(uid, await GetAsync<RoomCooldownSettingDto>("api/settings/room-cooldown")); } catch { }
		try { LocalSettingsStore.SetStrategyChains(uid, await GetAsync<List<StrategyChainDto>>("api/settings/strategy-chains")); } catch { }
	}

	// 행렬 셀 — 로컬 우선.
	public async Task<List<MatrixCellDto>> GetMatrixCellsAsync(int betAmountType = 11)
	{
		string? uid = AuthService.CurrentUserId;
		List<MatrixCellDto>? local = LocalSettingsStore.GetMatrixCells(uid, betAmountType);
		if (local != null)
		{
			return local;
		}
		List<MatrixCellDto> server = await GetAsync<List<MatrixCellDto>>($"api/settings/matrix-cells?betAmountType={betAmountType}");
		LocalSettingsStore.SetMatrixCells(uid, betAmountType, server);
		return server;
	}

	public async Task SaveMatrixCellsAsync(List<MatrixCellDto> cells, int betAmountType = 11)
	{
		LocalSettingsStore.SetMatrixCells(AuthService.CurrentUserId, betAmountType, cells);
		await PostAsync($"api/settings/matrix-cells?betAmountType={betAmountType}", cells);
	}

	// 행렬 행규칙 — 로컬 우선.
	public async Task<List<MatrixRowRuleDto>> GetMatrixRowRulesAsync(int betAmountType = 11)
	{
		string? uid = AuthService.CurrentUserId;
		List<MatrixRowRuleDto>? local = LocalSettingsStore.GetMatrixRowRules(uid, betAmountType);
		if (local != null)
		{
			return local;
		}
		List<MatrixRowRuleDto> server = await GetAsync<List<MatrixRowRuleDto>>($"api/settings/matrix-row-rules?betAmountType={betAmountType}");
		LocalSettingsStore.SetMatrixRowRules(uid, betAmountType, server);
		return server;
	}

	public async Task SaveMatrixRowRulesAsync(List<MatrixRowRuleDto> rules, int betAmountType = 11)
	{
		LocalSettingsStore.SetMatrixRowRules(AuthService.CurrentUserId, betAmountType, rules);
		await PostAsync($"api/settings/matrix-row-rules?betAmountType={betAmountType}", rules);
	}

	// 한도 — 로컬 우선.
	public async Task<LimitSettingDto> GetLimitsAsync()
	{
		string? uid = AuthService.CurrentUserId;
		LimitSettingDto? local = LocalSettingsStore.GetLimits(uid);
		if (local != null)
		{
			return local;
		}
		LimitSettingDto server = await GetAsync<LimitSettingDto>("api/settings/limits");
		LocalSettingsStore.SetLimits(uid, server);
		return server;
	}

	public async Task SaveLimitsAsync(LimitSettingDto limits)
	{
		LocalSettingsStore.SetLimits(AuthService.CurrentUserId, limits);
		await PostAsync("api/settings/limits", limits);
	}

	// 예외 서식 — 로컬 우선.
	public async Task<List<ExceptionFormatDto>> GetExceptionFormatsAsync()
	{
		string? uid = AuthService.CurrentUserId;
		List<ExceptionFormatDto>? local = LocalSettingsStore.GetExceptionFormats(uid);
		if (local != null)
		{
			return local;
		}
		List<ExceptionFormatDto> server = await GetAsync<List<ExceptionFormatDto>>("api/settings/exception-formats");
		LocalSettingsStore.SetExceptionFormats(uid, server);
		return server;
	}

	public async Task SaveExceptionFormatsAsync(List<ExceptionFormatDto> formats)
	{
		LocalSettingsStore.SetExceptionFormats(AuthService.CurrentUserId, formats);
		await PostAsync("api/settings/exception-formats", formats);
	}

	// 방 게이트 서식 — 로컬 우선(NotFound 는 빈 목록으로 캐시).
	public async Task<List<RoomGateFormatDto>> GetRoomGateFormatsAsync()
	{
		string? uid = AuthService.CurrentUserId;
		List<RoomGateFormatDto>? local = LocalSettingsStore.GetRoomGateFormats(uid);
		if (local != null)
		{
			return local;
		}
		try
		{
			List<RoomGateFormatDto> server = await GetAsync<List<RoomGateFormatDto>>("api/settings/room-gate-formats");
			LocalSettingsStore.SetRoomGateFormats(uid, server);
			return server;
		}
		catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
		{
			List<RoomGateFormatDto> empty = new List<RoomGateFormatDto>();
			LocalSettingsStore.SetRoomGateFormats(uid, empty);
			return empty;
		}
	}

	public async Task SaveRoomGateFormatsAsync(List<RoomGateFormatDto> formats)
	{
		LocalSettingsStore.SetRoomGateFormats(AuthService.CurrentUserId, formats);
		await PostAsync("api/settings/room-gate-formats", formats);
	}

	// 방 쿨다운 — 로컬 우선(NotFound 는 비활성 기본값으로 캐시).
	public async Task<RoomCooldownSettingDto?> GetRoomCooldownSettingsAsync()
	{
		string? uid = AuthService.CurrentUserId;
		RoomCooldownSettingDto? local = LocalSettingsStore.GetRoomCooldown(uid);
		if (local != null)
		{
			return local;
		}
		try
		{
			RoomCooldownSettingDto server = await GetAsync<RoomCooldownSettingDto>("api/settings/room-cooldown");
			LocalSettingsStore.SetRoomCooldown(uid, server);
			return server;
		}
		catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
		{
			RoomCooldownSettingDto disabled = new RoomCooldownSettingDto(false, 0, 0);
			LocalSettingsStore.SetRoomCooldown(uid, disabled);
			return disabled;
		}
	}

	public async Task SaveRoomCooldownSettingsAsync(RoomCooldownSettingDto dto)
	{
		LocalSettingsStore.SetRoomCooldown(AuthService.CurrentUserId, dto);
		await PutAsync("api/settings/room-cooldown", dto);
	}

	// 전략 체인 — 로컬 우선(목록 캐시). 활성 체인은 목록에서 도출. 저장/활성화/삭제 시 캐시 무효화.
	public async Task<StrategyChainDto?> GetActiveStrategyChainAsync()
	{
		string? uid = AuthService.CurrentUserId;
		List<StrategyChainDto>? localList = LocalSettingsStore.GetStrategyChains(uid);
		if (localList != null)
		{
			return localList.FirstOrDefault(c => c.IsActive);
		}
		try
		{
			return await GetAsync<StrategyChainDto>("api/settings/strategy-chains/active");
		}
		catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	public async Task<List<StrategyChainDto>> GetStrategyChainsAsync()
	{
		string? uid = AuthService.CurrentUserId;
		List<StrategyChainDto>? local = LocalSettingsStore.GetStrategyChains(uid);
		if (local != null)
		{
			return local;
		}
		List<StrategyChainDto> server = await GetAsync<List<StrategyChainDto>>("api/settings/strategy-chains");
		LocalSettingsStore.SetStrategyChains(uid, server);
		return server;
	}

	public async Task SaveStrategyChainAsync(StrategyChainDto chain)
	{
		await PostAsync("api/settings/strategy-chains", chain);
		LocalSettingsStore.InvalidateStrategyChains(AuthService.CurrentUserId);
	}

	public async Task DeleteStrategyChainAsync(long chainId)
	{
		await DeleteAsync($"api/settings/strategy-chains/{chainId}");
		LocalSettingsStore.InvalidateStrategyChains(AuthService.CurrentUserId);
	}

	public async Task ActivateStrategyChainAsync(long chainId)
	{
		await PostAsync($"api/settings/strategy-chains/{chainId}/activate", new { });
		LocalSettingsStore.InvalidateStrategyChains(AuthService.CurrentUserId);
	}

	public async Task SaveBettingHistoryAsync(string betType, decimal betAmount, string status, decimal balanceBefore, decimal balanceAfter, bool isVirtual = false, string? tableId = null, string? gameId = null, int? betAmountType = null, int? matrixRow = null, int? matrixCol = null)
	{
		await PostAsync("api/betting/history", new
		{
			betType, betAmount, status, balanceBefore, balanceAfter, isVirtual, tableId, gameId, betAmountType, matrixRow,
			matrixCol
		});
	}

	public async Task SaveBettingHistoryWithAllocationsAsync(BettingHistoryWithAllocationsDto dto)
	{
		await PostAsync("api/betting/history/with-allocations", dto);
	}

	public async Task SettleBettingHistoryAsync(string betType, decimal betAmount, string status, decimal? winAmount, decimal? profit, bool isVirtual = false, string? tableId = null, string? gameId = null)
	{
		await PostAsync("api/betting/history/settle", new { betType, betAmount, status, winAmount, profit, isVirtual, tableId, gameId });
	}

	public async Task CancelBettingHistoryAsync(string betType, decimal betAmount, string? tableId = null, string? gameId = null)
	{
		await PostAsync("api/betting/history/cancel", new { betType, betAmount, tableId, gameId });
	}

	public async Task<List<MatrixStatDto>> GetMatrixStatsAsync(DateTime? fromUtc = null, DateTime? toUtc = null, int? betAmountType = null, bool includeVirtual = false)
	{
		List<string> query = new List<string>();
		if (fromUtc.HasValue)
		{
			query.Add("fromUtc=" + Uri.EscapeDataString(fromUtc.Value.ToString("O")));
		}
		if (toUtc.HasValue)
		{
			query.Add("toUtc=" + Uri.EscapeDataString(toUtc.Value.ToString("O")));
		}
		if (betAmountType.HasValue)
		{
			query.Add($"betAmountType={betAmountType.Value}");
		}
		if (includeVirtual)
		{
			query.Add("includeVirtual=true");
		}
		string path = "api/betting/matrix-stats" + ((query.Count > 0) ? ("?" + string.Join("&", query)) : "");
		return await GetAsync<List<MatrixStatDto>>(path);
	}

	public async Task SaveBettingSessionAsync(decimal startBalance, decimal endBalance, decimal totalBetAmount, bool isVirtual = false)
	{
		await PostAsync("api/betting/session", new { startBalance, endBalance, totalBetAmount, isVirtual });
	}

	/// <summary>
	/// Tables for 방 설정. Defaults to whatever the signed-in account was granted, so an
	/// Evolution-only member is never shown Pragmatic tables.
	/// </summary>
	public async Task<List<GameDto>> GetBaccaratGamesAsync(string? provider = null)
	{
		string q = provider ?? GameProviderNames.ApiList(AuthService.CurrentProviders);
		return await GetAsync<List<GameDto>>("api/games/baccarat?provider=" + Uri.EscapeDataString(q));
	}

	public async Task<List<GameSelectionDto>> GetUserGameSelectionsAsync()
	{
		return await GetAsync<List<GameSelectionDto>>("api/games/selections");
	}

	public async Task SaveUserGameSelectionsAsync(List<GameSelectionDto> selections)
	{
		await PostAsync("api/games/selections", selections);
	}

	/// <summary>
	/// Tells the server about tables seen in live traffic so the catalog fills itself in.
	/// Failures are swallowed: this is bookkeeping, never a reason to interrupt betting.
	/// </summary>
	public async Task<DiscoveredGamesResult?> ReportDiscoveredGamesAsync(List<DiscoveredGameDto> games, string provider)
	{
		try
		{
			return await PostAsync<DiscoveredGamesResult>(
				"api/games/discovered?provider=" + Uri.EscapeDataString(provider), games);
		}
		catch
		{
			return null;
		}
	}

	public async Task<ActiveNoticeResponse> GetActiveNoticeAsync()
	{
		return await GetAsync<ActiveNoticeResponse>("api/notice/active");
	}

	public async Task<bool> SendLogsAsync(List<AppLogDto> logs)
	{
		try
		{
			await PostAsync("api/logs", logs);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private async Task<T> GetAsync<T>(string path)
	{
		HttpResponseMessage response = await _httpClient.GetAsync(path);
		await EnsureSuccessAsync(response);
		await EnsureJsonContentTypeAsync(response);
		return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
	}

	private async Task<T> PostAsync<T>(string path, object content)
	{
		HttpResponseMessage response = await _httpClient.PostAsJsonAsync(path, content, _jsonOptions);
		await EnsureSuccessAsync(response);
		await EnsureJsonContentTypeAsync(response);
		return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
	}

	private async Task PostAsync(string path, object content)
	{
		await EnsureSuccessAsync(await _httpClient.PostAsJsonAsync(path, content, _jsonOptions));
	}

	private async Task PutAsync(string path, object content)
	{
		await EnsureSuccessAsync(await _httpClient.PutAsJsonAsync(path, content, _jsonOptions));
	}

	private async Task DeleteAsync(string path)
	{
		await EnsureSuccessAsync(await _httpClient.DeleteAsync(path));
	}

	private async Task EnsureSuccessAsync(HttpResponseMessage response)
	{
		if (response.IsSuccessStatusCode)
		{
			return;
		}
		string errorBody = await response.Content.ReadAsStringAsync();
		switch (response.StatusCode)
		{
		case HttpStatusCode.Unauthorized:
			throw new ApiUnauthorizedException(TryParseErrorMessage(errorBody) ?? "인증이 만료되었습니다. 다시 로그인하세요.");
		case HttpStatusCode.BadRequest:
			throw new ApiBadRequestException(TryParseErrorMessage(errorBody) ?? "잘못된 요청입니다.");
		case HttpStatusCode.Conflict:
			throw new ApiSessionConflictException(TryParseErrorMessage(errorBody) ?? "다른 장치에서 로그인되었습니다.");
		case HttpStatusCode.UpgradeRequired:
			throw new ApiVersionMismatchException(TryParseErrorMessage(errorBody) ?? "봇 버전이 서버와 일치하지 않습니다. 최신 봇으로 업데이트하세요.");
		default:
			throw new ApiException($"API 오류 ({(int)response.StatusCode}): " + (TryParseErrorMessage(errorBody) ?? response.ReasonPhrase ?? "알 수 없는 오류"), response.StatusCode);
		}
	}

	private static async Task EnsureJsonContentTypeAsync(HttpResponseMessage response)
	{
		string contentType = response.Content.Headers.ContentType?.MediaType;
		if (contentType != null && !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
		{
			string preview = await response.Content.ReadAsStringAsync();
			if (preview.Length > 200)
			{
				preview = preview.Substring(0, 200);
			}
			throw new ApiException($"서버가 JSON이 아닌 '{contentType}' 형식으로 응답했습니다. HTTPS 리디렉션 또는 프록시 설정을 확인하세요. 응답 미리보기: {preview}");
		}
	}

	private string? TryParseErrorMessage(string body)
	{
		try
		{
			return JsonSerializer.Deserialize<ApiErrorResponse>(body, _jsonOptions)?.Message;
		}
		catch
		{
			return null;
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_httpClient.Dispose();
			_disposed = true;
		}
		GC.SuppressFinalize(this);
	}
}
public class ApiException : Exception
{
	/// <summary>
	/// The HTTP status the server answered with, where one is known.
	/// <para>
	/// Callers that treat "not configured yet" as a normal answer need this: the request never fails
	/// at the transport level, so there is no HttpRequestException to read a status off.
	/// </para>
	/// </summary>
	public HttpStatusCode? StatusCode { get; }

	public ApiException(string message)
		: base(message)
	{
	}

	public ApiException(string message, HttpStatusCode statusCode)
		: base(message)
	{
		StatusCode = statusCode;
	}
}
public class ApiUnauthorizedException : ApiException
{
	public ApiUnauthorizedException(string message)
		: base(message)
	{
	}
}
public class ApiVersionMismatchException : ApiException
{
	public ApiVersionMismatchException(string message)
		: base(message)
	{
	}
}
public class ApiBadRequestException : ApiException
{
	public ApiBadRequestException(string message)
		: base(message)
	{
	}
}
public class ApiSessionConflictException : ApiException
{
	public ApiSessionConflictException(string message)
		: base(message)
	{
	}
}
