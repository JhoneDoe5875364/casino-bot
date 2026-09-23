using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PragmaticBot.Models.Api;

namespace PragmaticBot.Services;

public class MatrixStatsService
{
	private static ApiClient GetApi()
	{
		return AuthService.Api ?? throw new InvalidOperationException("로그인이 필요합니다.");
	}

	public async Task<List<MatrixStatDto>> GetAsync(DateTime? fromLocal, DateTime? toLocal, int? betAmountType = null, bool includeVirtual = false)
	{
		return await GetApi().GetMatrixStatsAsync(fromLocal?.ToUniversalTime(), toLocal?.ToUniversalTime(), betAmountType, includeVirtual);
	}
}
