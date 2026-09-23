using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PragmaticBot.Models;

namespace PragmaticBot.Services;

public sealed class PragmaticSiteSessionManager : IDisposable
{
	private readonly Dictionary<int, PragmaticSiteSession> _sessions = new Dictionary<int, PragmaticSiteSession>();

	private readonly Func<string, string> _resolveTableKey;

	private bool _disposed;

	public IReadOnlyList<PragmaticSiteSession> Sessions => _sessions.Values.OrderBy((PragmaticSiteSession s) => s.Site.SiteSlot).ToList();

	public bool AnyConfigured => _sessions.Count > 0;

	public PragmaticSiteSessionManager(Func<string, string> resolveTableKey)
	{
		_resolveTableKey = resolveTableKey;
	}

	public void Configure(IReadOnlyList<BettingSite> sites)
	{
		DisposeSessions();
		_sessions.Clear();
		foreach (BettingSite site in from s in sites
			where s.SiteSlot > 0 && s.IsActive
			orderby s.SiteSlot
			select s)
		{
			_sessions[site.SiteSlot] = new PragmaticSiteSession(site, _resolveTableKey);
			FileLogger.Log($"[SiteManager] 세션 구성: 슬롯{site.SiteSlot}({site.BrowserType}) CDP {site.DebugPort}, WS {site.WsServerPort}, 도메인 {site.Domain}", "Info", "MultiSite");
		}
	}

	public async Task<bool> ConnectSitesAsync(IReadOnlyCollection<int> slots, int masterDebugPort, IProgress<string>? progress = null)
	{
		List<PragmaticSiteSession> targets = (from s in _sessions.Values
			where slots.Contains(s.Site.SiteSlot)
			orderby s.Site.SiteSlot
			select s).ToList();
		if (targets.Count == 0)
		{
			return true;
		}
		bool allOk = true;
		foreach (PragmaticSiteSession session in targets)
		{
			HashSet<int> usedPorts = (from p in (from s in _sessions.Values
					where s != session && s.ActiveDebugPort > 0
					select s.ActiveDebugPort).Append(masterDebugPort)
				where p > 0
				select p).ToHashSet();
			progress?.Report($"사이트 {session.Site.SiteSlot + 1} 브라우저 준비 중...");
			if (!(await session.ConnectAsync(usedPorts, progress).ConfigureAwait(continueOnCapturedContext: false)))
			{
				allOk = false;
			}
		}
		return allOk;
	}

	public PragmaticSiteSession? GetSession(int siteSlot)
	{
		if (!_sessions.TryGetValue(siteSlot, out PragmaticSiteSession s))
		{
			return null;
		}
		return s;
	}

	public async Task DisconnectAllAsync()
	{
		foreach (PragmaticSiteSession value in _sessions.Values)
		{
			await value.DisconnectAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private void DisposeSessions()
	{
		foreach (PragmaticSiteSession session in _sessions.Values)
		{
			try
			{
				session.Dispose();
			}
			catch
			{
			}
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			DisposeSessions();
			_sessions.Clear();
		}
	}
}
