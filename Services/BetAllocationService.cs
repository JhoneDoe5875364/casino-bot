using System;
using System.Collections.Generic;
using System.Linq;
using PragmaticBot.Models;

namespace PragmaticBot.Services;

public class BetAllocationService(Random? random = null)
{
	public record AllocationResult(IReadOnlyList<SiteBetSplit> Splits, bool ExceedsPerSiteLimit)
	{
		public static readonly AllocationResult Empty = new AllocationResult(Array.Empty<SiteBetSplit>(), ExceedsPerSiteLimit: false);

		public static readonly AllocationResult OverLimit = new AllocationResult(Array.Empty<SiteBetSplit>(), ExceedsPerSiteLimit: true);
	}

	public const decimal MinPerSite = 10000m;

	public const decimal SplitThreshold = 40000m;

	public const decimal MaxPerSite = 5000000m;

	public const long SplitUnit = 1000L;

	private readonly Random _random = random ?? Random.Shared;

	public AllocationResult Allocate(decimal stepAmount, IReadOnlyList<BettingSite> activeSites, int maxSites, decimal maxPerSite = 0m, decimal splitThreshold = 0m)
	{
		if (stepAmount <= 0m || activeSites == null)
		{
			return AllocationResult.Empty;
		}
		List<BettingSite> sites = activeSites.Where((BettingSite s) => s.IsActive).ToList();
		if (sites.Count == 0)
		{
			return AllocationResult.Empty;
		}
		if (maxSites < 1)
		{
			maxSites = 1;
		}
		long total = (long)stepAmount;
		long min = 10000L;
		// 사이트당 한도: 설정값(원) 사용, 없으면 기본 500만. 1,000원 단위로 내림 정렬해 분배 단위와 맞춘다.
		long max = ((maxPerSite >= 10000m) ? ((long)maxPerSite / 1000L * 1000L) : 5000000L);
		// 찢어베팅 시작 금액: 설정값 사용(최소 min 이상), 없으면 기본 4만.
		long threshold = ((splitThreshold >= min) ? (long)splitThreshold : 40000L);
		int siteCount;
		if (total < threshold)
		{
			siteCount = 1;
		}
		else
		{
			int byAmount = (int)(total / min);
			siteCount = Math.Max(1, Math.Min(Math.Min(maxSites, byAmount), sites.Count));
		}
		if (total > siteCount * max)
		{
			return AllocationResult.OverLimit;
		}
		List<BettingSite> picked = sites.OrderBy((BettingSite s) => s.SiteSlot).Take(siteCount).ToList();
		if (siteCount == 1)
		{
			return new AllocationResult([
				new SiteBetSplit
				{
					SiteSlot = picked[0].SiteSlot,
					Domain = picked[0].Domain,
					Amount = total
				}
			], ExceedsPerSiteLimit: false);
		}
		long capUnits = (max - min) / 1000;
		long remainingUnits = (total - siteCount * min) / 1000;
		long[] extra = new long[siteCount];
		for (int i = 0; i < siteCount - 1; i++)
		{
			long othersCap = (siteCount - 1 - i) * capUnits;
			long minForThis = Math.Max(0L, remainingUnits - othersCap);
			long maxForThis = Math.Min(capUnits, remainingUnits);
			remainingUnits -= (extra[i] = _random.NextInt64(minForThis, maxForThis + 1));
		}
		extra[siteCount - 1] = remainingUnits;
		List<SiteBetSplit> splits = new List<SiteBetSplit>(siteCount);
		for (int i2 = 0; i2 < siteCount; i2++)
		{
			splits.Add(new SiteBetSplit
			{
				SiteSlot = picked[i2].SiteSlot,
				Domain = picked[i2].Domain,
				Amount = min + extra[i2] * 1000
			});
		}
		return new AllocationResult(splits, ExceedsPerSiteLimit: false);
	}
}
