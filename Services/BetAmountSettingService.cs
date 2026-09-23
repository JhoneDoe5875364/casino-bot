using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PragmaticBot.Models.Api;

namespace PragmaticBot.Services;

public class BetAmountSettingService
{
	private static ApiClient GetApi()
	{
		return AuthService.Api ?? throw new InvalidOperationException("로그인이 필요합니다.");
	}

	public async Task<List<BetAmountSettingDto>> GetUserBetAmountsAsync(string userId, int betAmountType = 0)
	{
		return await GetApi().GetBetAmountsAsync(betAmountType);
	}

	public async Task SaveUserBetAmountsAsync(string userId, IDictionary<int, decimal?> betAmounts, int betAmountType = 0)
	{
		List<BetAmountSettingDto> dtos = (from kv in betAmounts
			where kv.Key >= 1 && kv.Key <= 50
			select new BetAmountSettingDto(kv.Key, kv.Value, betAmountType)).ToList();
		await GetApi().SaveBetAmountsAsync(dtos);
	}

	public async Task SaveMixedBetAmountsAsync(string userId, IDictionary<int, (decimal? Amount, int? BettingMethod)> betAmounts)
	{
		List<BetAmountSettingDto> dtos = (from kv in betAmounts
			where kv.Key >= 1 && kv.Key <= 50
			select new BetAmountSettingDto(kv.Key, kv.Value.Amount, 3, kv.Value.BettingMethod)).ToList();
		await GetApi().SaveBetAmountsAsync(dtos);
	}

	public async Task SaveMartinDAlembertBetAmountsAsync(string userId, IDictionary<int, (decimal? Amount, int? BettingMethod)> betAmounts, int betAmountType = 5)
	{
		List<BetAmountSettingDto> dtos = (from kv in betAmounts
			where kv.Key >= 1 && kv.Key <= 50
			select new BetAmountSettingDto(kv.Key, kv.Value.Amount, betAmountType, kv.Value.BettingMethod)).ToList();
		await GetApi().SaveBetAmountsAsync(dtos);
	}

	public static async Task<List<BetAmountSettingDto>> GetAllSettingsAsync(string userId, int betAmountType = 0)
	{
		return (from b in (await GetApi().GetBetAmountsAsync(betAmountType)).Where(delegate(BetAmountSettingDto b)
			{
				if (b.Amount.HasValue)
				{
					decimal? amount = b.Amount;
					return (amount.GetValueOrDefault() > 0m) & amount.HasValue;
				}
				return false;
			})
			orderby b.Number
			select b).ToList();
	}
}
