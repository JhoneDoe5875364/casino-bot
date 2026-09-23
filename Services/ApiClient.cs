using System;
using System.Collections.Generic;
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

	public async Task<List<FormatSettingDto>> GetFormatsAsync()
	{
		return await GetAsync<List<FormatSettingDto>>("api/settings/formats");
	}

	public async Task SaveFormatsAsync(List<FormatSettingDto> formats)
	{
		await PostAsync("api/settings/formats", formats);
	}

	public async Task<List<BetAmountSettingDto>> GetBetAmountsAsync(int betAmountType = 0)
	{
		return await GetAsync<List<BetAmountSettingDto>>($"api/settings/bet-amounts?betAmountType={betAmountType}");
	}

	public async Task SaveBetAmountsAsync(List<BetAmountSettingDto> betAmounts)
	{
		await PostAsync("api/settings/bet-amounts", betAmounts);
	}

	public async Task<List<MatrixCellDto>> GetMatrixCellsAsync(int betAmountType = 11)
	{
		return await GetAsync<List<MatrixCellDto>>($"api/settings/matrix-cells?betAmountType={betAmountType}");
	}

	public async Task SaveMatrixCellsAsync(List<MatrixCellDto> cells, int betAmountType = 11)
	{
		await PostAsync($"api/settings/matrix-cells?betAmountType={betAmountType}", cells);
	}

	public async Task<List<MatrixRowRuleDto>> GetMatrixRowRulesAsync(int betAmountType = 11)
	{
		return await GetAsync<List<MatrixRowRuleDto>>($"api/settings/matrix-row-rules?betAmountType={betAmountType}");
	}

	public async Task SaveMatrixRowRulesAsync(List<MatrixRowRuleDto> rules, int betAmountType = 11)
	{
		await PostAsync($"api/settings/matrix-row-rules?betAmountType={betAmountType}", rules);
	}

	public async Task<LimitSettingDto> GetLimitsAsync()
	{
		return await GetAsync<LimitSettingDto>("api/settings/limits");
	}

	public async Task SaveLimitsAsync(LimitSettingDto limits)
	{
		await PostAsync("api/settings/limits", limits);
	}

	public async Task<List<ExceptionFormatDto>> GetExceptionFormatsAsync()
	{
		return await GetAsync<List<ExceptionFormatDto>>("api/settings/exception-formats");
	}

	public async Task SaveExceptionFormatsAsync(List<ExceptionFormatDto> formats)
	{
		await PostAsync("api/settings/exception-formats", formats);
	}

	public async Task<List<RoomGateFormatDto>> GetRoomGateFormatsAsync()
	{
		try
		{
			return await GetAsync<List<RoomGateFormatDto>>("api/settings/room-gate-formats");
		}
		catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
		{
			return new List<RoomGateFormatDto>();
		}
	}

	public async Task SaveRoomGateFormatsAsync(List<RoomGateFormatDto> formats)
	{
		await PostAsync("api/settings/room-gate-formats", formats);
	}

	public async Task<RoomCooldownSettingDto?> GetRoomCooldownSettingsAsync()
	{
		try
		{
			return await GetAsync<RoomCooldownSettingDto>("api/settings/room-cooldown");
		}
		catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	public async Task SaveRoomCooldownSettingsAsync(RoomCooldownSettingDto dto)
	{
		await PutAsync("api/settings/room-cooldown", dto);
	}

	public async Task<StrategyChainDto?> GetActiveStrategyChainAsync()
	{
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
		return await GetAsync<List<StrategyChainDto>>("api/settings/strategy-chains");
	}

	public async Task SaveStrategyChainAsync(StrategyChainDto chain)
	{
		await PostAsync("api/settings/strategy-chains", chain);
	}

	public async Task DeleteStrategyChainAsync(long chainId)
	{
		await DeleteAsync($"api/settings/strategy-chains/{chainId}");
	}

	public async Task ActivateStrategyChainAsync(long chainId)
	{
		await PostAsync($"api/settings/strategy-chains/{chainId}/activate", new { });
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
