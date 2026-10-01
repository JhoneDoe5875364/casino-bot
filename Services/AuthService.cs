using System;
using System.Threading;
using System.Threading.Tasks;
using PragmaticBot.Models.Api;

namespace PragmaticBot.Services;

public class AuthService
{
	private const int LogoutTimeoutSeconds = 3;

	public static string? CurrentUserId { get; private set; }

	public static string? CurrentUserName { get; private set; }

	public static bool IsActive { get; private set; }

	public static DateTime? ExpirationDate { get; private set; }

	public static string? CurrentSessionId { get; private set; }

	/// <summary>
	/// Providers this account may play, as granted by the admin site. Bit flags: 1 = Pragmatic Play,
	/// 2 = Evolution. Read before the browser starts so only the permitted extensions are loaded.
	/// </summary>
	public static GameProviders CurrentProviders { get; private set; } = GameProviders.PragmaticPlay;

	public static ApiClient? Api { get; private set; }

	public async Task<LoginResponse?> ValidateAsync(string username, string password)
	{
		try
		{
			Api = new ApiClient(AppConfiguration.ApiBaseUrl);
			LoginResponse obj = await Api.LoginAsync(username, password);
			CurrentUserId = obj.UserId;
			CurrentUserName = obj.UserName;
			// 로그인 직후 백그라운드로 서버 설정을 로컬에 씨딩(관리자 수정 반영 + 화면 즉시 열기). UI는 기다리지 않는다.
			_ = Api.SeedSettingsFromServerAsync();
			IsActive = obj.IsActive;
			ExpirationDate = obj.ExpirationDate;
			CurrentSessionId = obj.SessionId;
			CurrentProviders = (GameProviders)obj.Providers;
			if (CurrentProviders == GameProviders.None)
			{
				CurrentProviders = GameProviders.PragmaticPlay;
			}
			FileLogger.Log("[Auth] 이용 게임 권한: " + GameProviderNames.Label(CurrentProviders), "Info", DbLogService.SessionCategory);
			return obj;
		}
		catch (ApiUnauthorizedException)
		{
			return null;
		}
		catch (Exception ex2)
		{
			FileLogger.Log("[Auth] 로그인 실패: " + ex2.Message, "Error", "Auth", ex2.StackTrace);
			throw;
		}
	}

	public static async Task LogoutAsync()
	{
		_ = 1;
		try
		{
			if (Api == null)
			{
				return;
			}
			Task logoutTask = Api.LogoutAsync();
			if (await Task.WhenAny(logoutTask, Task.Delay(TimeSpan.FromSeconds(3L))) == logoutTask)
			{
				try
				{
					await logoutTask;
					FileLogger.Log("[Auth] 서버 로그아웃 완료", "Info", "Auth");
				}
				catch (Exception ex)
				{
					FileLogger.Log("[Auth] 서버 로그아웃 실패: " + ex.Message + " — 로컬 세션만 정리", "Warning", "Auth");
				}
				return;
			}
			FileLogger.Log($"[Auth] 서버 로그아웃 응답 없음 — {3}초 타임아웃, 로컬 세션만 정리", "Warning", "Auth");
			logoutTask.ContinueWith(delegate(Task t)
			{
				_ = t.Exception;
			}, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
		}
		finally
		{
			CurrentUserId = null;
			CurrentUserName = null;
			IsActive = false;
			ExpirationDate = null;
			CurrentSessionId = null;
		}
	}
}
