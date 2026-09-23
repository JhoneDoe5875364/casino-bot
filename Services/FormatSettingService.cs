using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PragmaticBot.Models.Api;

namespace PragmaticBot.Services;

public class FormatSettingService
{
	private static ApiClient GetApi()
	{
		return AuthService.Api ?? throw new InvalidOperationException("로그인이 필요합니다.");
	}

	public async Task<List<FormatSettingDto>> GetUserFormatsAsync(string userId)
	{
		return await GetApi().GetFormatsAsync();
	}

	public async Task SaveUserFormatsAsync(string userId, IDictionary<int, string?> formats)
	{
		List<FormatSettingDto> dtos = (from kv in formats
			where kv.Key >= 1 && kv.Key <= 50
			select new FormatSettingDto(kv.Key, kv.Value)).ToList();
		await GetApi().SaveFormatsAsync(dtos);
	}
}
