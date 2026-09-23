using System;
using System.Threading.Tasks;
using PragmaticBot.Models.Api;

namespace PragmaticBot.Services;

public class RoomCooldownSettingService
{
	private static ApiClient GetApi()
	{
		return AuthService.Api ?? throw new InvalidOperationException("로그인이 필요합니다.");
	}

	public async Task<RoomCooldownSettingDto> GetAsync()
	{
		return (await GetApi().GetRoomCooldownSettingsAsync()) ?? new RoomCooldownSettingDto(IsEnabled: false, 5, 3);
	}

	public async Task SaveAsync(RoomCooldownSettingDto dto)
	{
		await GetApi().SaveRoomCooldownSettingsAsync(dto);
	}
}
