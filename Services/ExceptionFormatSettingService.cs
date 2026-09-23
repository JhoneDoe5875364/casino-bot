using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PragmaticBot.Models.Api;

namespace PragmaticBot.Services;

public class ExceptionFormatSettingService
{
	private static ApiClient GetApi()
	{
		return AuthService.Api ?? throw new InvalidOperationException("로그인이 필요합니다.");
	}

	public async Task<List<ExceptionFormatDto>> GetExceptionFormatsAsync(string userId)
	{
		return await GetApi().GetExceptionFormatsAsync();
	}

	public async Task SaveExceptionFormatsAsync(string userId, IDictionary<int, string?> formats)
	{
		List<ExceptionFormatDto> dtos = (from kv in formats
			where kv.Key >= 1 && kv.Key <= 20
			select new ExceptionFormatDto(kv.Key, kv.Value)).ToList();
		await GetApi().SaveExceptionFormatsAsync(dtos);
	}
}
