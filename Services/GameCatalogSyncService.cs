using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PragmaticBot.Models.Api;
using Timer = System.Threading.Timer;

namespace PragmaticBot.Services;

/// <summary>
/// Keeps the server's game catalog in step with what the bot actually sees.
///
/// Every table the live traffic mentions is queued here and reported in batches. Without this the
/// catalog has to be filled in by hand, and a table that is missing from it simply never gets a
/// room panel — the bot receives its results and has nowhere to show them.
/// </summary>
public sealed class GameCatalogSyncService : IDisposable
{
	private const int FlushDelayMs = 5000;

	/// <summary>Tables already accepted by the server this session — never re-sent.</summary>
	private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>Tables waiting to go out, keyed by table id so the best title wins.</summary>
	private readonly ConcurrentDictionary<string, DiscoveredGameDto> _pending =
		new ConcurrentDictionary<string, DiscoveredGameDto>(StringComparer.OrdinalIgnoreCase);

	private readonly SemaphoreSlim _flushGate = new SemaphoreSlim(1, 1);
	private Timer? _timer;
	private bool _disposed;

	/// <summary>Raised after the server accepted new tables, so the UI can reload its game list.</summary>
	public event EventHandler<int>? CatalogChanged;

	public void Start()
	{
		_timer ??= new Timer(delegate { _ = FlushAsync(); }, null, FlushDelayMs, FlushDelayMs);
	}

	/// <summary>Which provider each observed table id belongs to.</summary>
	private readonly ConcurrentDictionary<string, string> _providerOf = new(StringComparer.Ordinal);

	public void Stop()
	{
		_timer?.Dispose();
		_timer = null;
	}

	/// <summary>
	/// Records a table. A later report carrying a real name replaces an earlier id-only one,
	/// so the server gets the best title we have by the time the batch goes out.
	/// </summary>
	public void Observe(string? tableId, string? tableName = null, string? tableType = null,
		string provider = GameProviderNames.PragmaticPlayKey)
	{
		if (_disposed || string.IsNullOrWhiteSpace(tableId))
		{
			return;
		}
		string id = tableId.Trim();
		// a table id only means something within its provider, so both parts form the key
		_providerOf[id] = provider;
		lock (_reported)
		{
			// already stored server-side, unless we have since learned its name
			if (_reported.Contains(id) && string.IsNullOrWhiteSpace(tableName))
			{
				return;
			}
		}
		_pending.AddOrUpdate(id,
			_ => new DiscoveredGameDto(id, Blank(tableName), Blank(tableType)),
			(_, existing) => new DiscoveredGameDto(
				id,
				Blank(tableName) ?? existing.Title,
				Blank(tableType) ?? existing.TableType));
	}

	public async Task FlushAsync()
	{
		if (_disposed || _pending.IsEmpty)
		{
			return;
		}
		ApiClient? api = AuthService.Api;
		if (api == null)
		{
			return;
		}
		if (!await _flushGate.WaitAsync(0).ConfigureAwait(continueOnCapturedContext: false))
		{
			return;
		}
		try
		{
			List<DiscoveredGameDto> batch = _pending.Keys
				.Select(k => _pending.TryRemove(k, out DiscoveredGameDto? v) ? v : null)
				.Where(v => v != null)
				.ToList()!;
			if (batch.Count == 0)
			{
				return;
			}

			// one call per provider: the server files rows under the provider it is told
			DiscoveredGamesResult? result = null;
			foreach (var group in batch.GroupBy(b => _providerOf.TryGetValue(b.PpTableId, out string? pv)
				? pv
				: GameProviderNames.PragmaticPlayKey))
			{
				DiscoveredGamesResult? one = await api
					.ReportDiscoveredGamesAsync(group.ToList(), group.Key)
					.ConfigureAwait(continueOnCapturedContext: false);
				if (one == null)
				{
					// server unreachable — put them back so the next tick retries
					foreach (DiscoveredGameDto item in group)
					{
						_pending.TryAdd(item.PpTableId, item);
					}
					continue;
				}
				result = one;
			}
			if (result == null)
			{
				return;
			}

			lock (_reported)
			{
				foreach (DiscoveredGameDto item in batch)
				{
					_reported.Add(item.PpTableId);
				}
			}

			if (result.Added > 0 || result.Updated > 0)
			{
				FileLogger.Log(
					$"[GameCatalog] 테이블 자동 등록: 신규 {result.Added}개, 이름 보완 {result.Updated}개 (카탈로그 총 {result.Total}개)",
					"Info", "GameCatalog");
				if (result.Added > 0)
				{
					CatalogChanged?.Invoke(this, result.Added);
				}
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[GameCatalog] 테이블 보고 실패: " + ex.Message, "Warning", "GameCatalog");
		}
		finally
		{
			_flushGate.Release();
		}
	}

	private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		Stop();
		_flushGate.Dispose();
	}
}
