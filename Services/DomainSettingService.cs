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

	// 예전엔 전 계정이 공유하는 DomainSettings(=GetDomainAsync) 한 줄을 읽었는데, 계정마다 다른
	// 사이트를 쓰는 구조에서는 다른 계정이 마지막에 보고한 도메인으로 덮여 캡처 필터가 틀어졌다
	// (예: A가 a.com으로 로그인한 직후 B가 로그인하면 B의 필터가 a.com이 되어 B의 실제 소켓은
	// 하나도 안 걸려서 "접속 도메인"이 영원히 비게 된다). 이 계정 자신의 슬롯0(주) 기록만 쓴다.
	public static async Task<List<string>> GetUserDomainAsync(string userId)
	{
		try
		{
			List<DomainSiteDto> sites = await GetApi().GetDomainSitesAsync();
			DomainSiteDto? mine = sites.FirstOrDefault((DomainSiteDto s) => s.SiteSlot == 0);
			string? domain = mine?.Domain;
			if (string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(mine?.WebSocketUrl))
			{
				try
				{
					Uri uri = new Uri(mine.WebSocketUrl);
					if (!string.IsNullOrEmpty(uri.Host))
					{
						domain = uri.Host;
					}
				}
				catch (UriFormatException)
				{
				}
			}
			return string.IsNullOrWhiteSpace(domain) ? new List<string>() : new List<string>(1) { domain };
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
