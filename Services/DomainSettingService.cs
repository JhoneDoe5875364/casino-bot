using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PragmaticBot.Models;
using PragmaticBot.Models.Api;

namespace PragmaticBot.Services;

public class DomainSettingService
{
	public const int MultiSiteSlotCount = 6;

	public const int WsPortStridePerSlot = 5;

	public const int WsServerBasePort = 29876;

	private static ApiClient GetApi()
	{
		return AuthService.Api ?? throw new InvalidOperationException("로그인이 필요합니다.");
	}

	public static List<BettingSite> GetDefaultSites()
	{
		return (from slot in Enumerable.Range(0, 6)
			select new BettingSite
			{
				SiteSlot = slot,
				BrowserType = GetDefaultBrowserType(slot),
				DebugPort = 9222 + slot,
				WsServerPort = 29876 + slot * 5,
				Domain = string.Empty,
				SiteUrl = null,
				IsActive = true
			}).ToList();
	}

	public static string GetDefaultBrowserType(int siteSlot)
	{
		if (siteSlot % 2 != 0)
		{
			return "Edge";
		}
		return "Chrome";
	}

	public static async Task<List<BettingSite>> GetBettingSitesAsync(string userId)
	{
		List<BettingSite> sites = GetDefaultSites();
		try
		{
			foreach (DomainSiteDto dto in await GetApi().GetDomainSitesAsync())
			{
				BettingSite site = sites.FirstOrDefault((BettingSite s) => s.SiteSlot == dto.SiteSlot);
				if (site != null)
				{
					if (!string.IsNullOrWhiteSpace(dto.Domain))
					{
						site.Domain = dto.Domain;
					}
					if (!string.IsNullOrWhiteSpace(dto.BrowserType))
					{
						site.BrowserType = dto.BrowserType;
					}
					if (dto.DebugPort > 0)
					{
						site.DebugPort = dto.DebugPort;
					}
					site.SiteUrl = dto.SiteUrl;
					site.BalanceSelector = dto.BalanceSelector;
					site.IsActive = dto.SiteSlot == 0 || dto.IsActive;
				}
			}
			FileLogger.Log("[DomainSettingService] 멀티 사이트 로드됨(" + userId + "): " + string.Join(", ", sites.Select((BettingSite s) => $"슬롯{s.SiteSlot}({s.Domain}/CDP{s.DebugPort}/WS{s.WsServerPort})")));
		}
		catch (Exception ex)
		{
			FileLogger.Log("[DomainSettingService] 멀티 사이트 로드 실패(기본값 사용): " + ex.Message, "Warning", "Domain");
		}
		return sites;
	}

	public static async Task<List<string>> GetUserDomainAsync(string userId)
	{
		try
		{
			DomainSettingDto domain = await GetApi().GetDomainAsync();
			if (string.IsNullOrEmpty(domain.WebSocketUrl))
			{
				return new List<string>();
			}
			try
			{
				Uri uri = new Uri(domain.WebSocketUrl);
				if (!string.IsNullOrEmpty(uri.Host))
				{
					return new List<string>(1) { uri.Host };
				}
			}
			catch (UriFormatException)
			{
			}
			return new List<string>();
		}
		catch (Exception)
		{
			return new List<string>();
		}
	}

	public static async Task UpdateDomainAsync(string capturedWebSocketUrl, int siteSlot = 0)
	{
		try
		{
			await GetApi().UpdateWebSocketUrlAsync(capturedWebSocketUrl, "PragmaticPlay", siteSlot);
			FileLogger.Log($"[DomainSettingService] 도메인 자동 업데이트 완료 (슬롯{siteSlot}): {capturedWebSocketUrl}");
		}
		catch (Exception ex)
		{
			FileLogger.Log("[DomainSettingService] 도메인 자동 업데이트 실패: " + ex.Message, "Warning", "Domain");
		}
	}

	public static async Task SaveBalanceSelectorAsync(string selector)
	{
		try
		{
			await GetApi().UpdateBalanceSelectorAsync(selector);
			FileLogger.Log("[DomainSettingService] 잔액 셀렉터 자동 저장 완료: " + selector);
		}
		catch (Exception ex)
		{
			FileLogger.Log("[DomainSettingService] 잔액 셀렉터 저장 실패: " + ex.Message, "Warning", "Domain");
		}
	}

	public static async Task<List<string>> GetBalanceSelectorsAsync()
	{
		try
		{
			DomainSettingDto domain = await GetApi().GetDomainAsync();
			if (string.IsNullOrWhiteSpace(domain.BalanceSelector))
			{
				return new List<string>();
			}
			return (from s in domain.BalanceSelector.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				where !string.IsNullOrWhiteSpace(s)
				select s).ToList();
		}
		catch (Exception)
		{
			return new List<string>();
		}
	}
}
