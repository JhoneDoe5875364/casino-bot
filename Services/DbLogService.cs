using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PragmaticBot.Models.Api;
using Timer = System.Threading.Timer;

namespace PragmaticBot.Services;

public static class DbLogService
{
	private static readonly ConcurrentQueue<AppLogDto> _queue = new ConcurrentQueue<AppLogDto>();

	private static Timer? _flushTimer;

	private static ApiClient? _apiClient;

	private static bool _initialized;

	private static bool _flushing;

	private const int FlushIntervalMs = 3000;

	private const int MaxBatchSize = 100;

	private const int MaxBufferSize = 1000;

	private static readonly HashSet<string> _dbLogLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Info", "Warning", "Error", "Critical" };

	public static void Initialize(ApiClient apiClient)
	{
		if (!_initialized)
		{
			_apiClient = apiClient;
			_flushTimer = new Timer(OnFlushTimer, null, 3000, 3000);
			_initialized = true;
		}
	}

	/// <summary>
	/// Whether this account is in diagnostic mode. Server-controlled, refreshed by the session check.
	/// </summary>
	public static bool DiagnosticLogging { get; set; }

	/// <summary>
	/// Category for the handful of Info lines worth keeping outside diagnostic mode — which strategy a
	/// session started on, the captured start balance, why it stopped. A few rows per session, against
	/// the tens of thousands of frame dumps the rest of Info produces.
	/// </summary>
	public const string SessionCategory = "Session";

	/// <summary>
	/// Info is the overwhelming majority of what the bot writes and almost none of it is ever read, so
	/// it only reaches the server in diagnostic mode. Warning and above always go.
	/// </summary>
	private static bool ShouldSend(string level, string? category)
	{
		if (!_dbLogLevels.Contains(level))
		{
			return false;
		}
		if (!string.Equals(level, "Info", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return DiagnosticLogging
			|| string.Equals(category, SessionCategory, StringComparison.OrdinalIgnoreCase);
	}

	public static void Log(string level, string? category, string message, string? stackTrace = null, string? additionalData = null)
	{
		if (_initialized && ShouldSend(level, category))
		{
			while (_queue.Count >= 1000)
			{
				_queue.TryDequeue(out AppLogDto _);
			}
			_queue.Enqueue(new AppLogDto(level, category, message, stackTrace, additionalData));
		}
	}

	private static async void OnFlushTimer(object? state)
	{
		await FlushInternalAsync();
	}

	public static async Task FlushAsync()
	{
		await FlushInternalAsync();
	}

	private static async Task FlushInternalAsync()
	{
		if (_apiClient == null || _flushing || _queue.IsEmpty)
		{
			return;
		}
		_flushing = true;
		try
		{
			List<AppLogDto> batch = new List<AppLogDto>();
			AppLogDto log;
			while (batch.Count < 100 && _queue.TryDequeue(out log))
			{
				batch.Add(log);
			}
			if (batch.Count > 0)
			{
				await _apiClient.SendLogsAsync(batch);
			}
		}
		catch
		{
		}
		finally
		{
			_flushing = false;
		}
	}

	public static void Dispose()
	{
		_flushTimer?.Dispose();
		_flushTimer = null;
		_initialized = false;
	}
}
