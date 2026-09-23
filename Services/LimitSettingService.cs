using System;
using System.Threading.Tasks;
using PragmaticBot.Models.Api;

namespace PragmaticBot.Services;

public class LimitSettingService
{
	private static ApiClient GetApi()
	{
		return AuthService.Api ?? throw new InvalidOperationException("로그인이 필요합니다.");
	}

	public async Task<LimitSettingDto> GetLimitsAsync(string userId)
	{
		return await GetApi().GetLimitsAsync();
	}

	public async Task SaveLimitsAsync(string userId, LimitSettingDto limits)
	{
		await GetApi().SaveLimitsAsync(limits);
	}
}
