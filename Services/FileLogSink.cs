using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Timer = System.Threading.Timer;

namespace PragmaticBot.Services;

/// <summary>
/// 모든 로그를 로컬 파일로도 떨군다(서버 업로드 게이팅과 무관하게 전량 기록).
/// 핫패스는 '큐에 넣기'만 하고, 백그라운드 타이머가 모아서 디스크에 쓴다(수신 스레드 부하 최소화).
/// 파일은 exe 옆 Logs\bot_YYYYMMDD_NNN.log 이며, 한 파일이 100KB 를 넘으면 다음 번호 파일로 이어 쓴다.
/// </summary>
public static class FileLogSink
{
	private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
	private static readonly object _startLock = new object();
	private static readonly Encoding _utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

	private static Timer? _timer;
	private static volatile bool _enabled = true;
	private static volatile bool _started;
	private static int _flushing;   // 0/1 재진입 가드

	private static string _dir = "";
	private static string _curPath = "";
	private static string _curDate = "";
	private static int _seq;
	private static long _curBytes;
	private static DateTime _lastRetentionCheck = DateTime.MinValue;

	private const long MaxFileBytes = 100 * 1024;   // 100KB 넘으면 새 파일
	private const int FlushIntervalMs = 1000;
	private const int MaxBufferLines = 50000;        // 디스크가 못 따라올 때 메모리 보호 상한
	private const int MaxLinesPerFlush = 10000;
	private const int RetentionDays = 7;

	/// <summary>파일 로깅 on/off (기본 on). 너무 무거우면 끌 수 있게 노출.</summary>
	public static bool Enabled
	{
		get => _enabled;
		set => _enabled = value;
	}

	private static void EnsureStarted()
	{
		if (_started)
		{
			return;
		}
		lock (_startLock)
		{
			if (_started)
			{
				return;
			}
			try
			{
				_dir = Path.Combine(AppContext.BaseDirectory, "Logs");
				Directory.CreateDirectory(_dir);
			}
			catch
			{
			}
			_timer = new Timer(delegate { Flush(); }, null, FlushIntervalMs, FlushIntervalMs);
			_started = true;
		}
	}

	public static void Write(string level, string? category, string message)
	{
		if (!_enabled)
		{
			return;
		}
		EnsureStarted();
		if (_queue.Count >= MaxBufferLines)
		{
			_queue.TryDequeue(out string _);   // 가장 오래된 것 버림(메모리 폭주 방지)
		}
		string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		string cat = string.IsNullOrEmpty(category) ? "" : category;
		_queue.Enqueue(ts + "\t" + level + "\t" + cat + "\t" + message);
	}

	/// <summary>즉시 비우기(종료 시 호출).</summary>
	public static void FlushNow()
	{
		Flush();
	}

	private static void Flush()
	{
		if (Interlocked.Exchange(ref _flushing, 1) == 1)
		{
			return;
		}
		try
		{
			if (string.IsNullOrEmpty(_dir))
			{
				return;
			}
			StringBuilder sb = new StringBuilder();
			string? batchPath = null;
			int n = 0;
			while (n < MaxLinesPerFlush && _queue.TryDequeue(out string? line))
			{
				long lineBytes = _utf8.GetByteCount(line) + 1;
				EnsureFile(lineBytes);
				// 파일이 롤오버되면, 모아둔 내용을 이전 파일에 먼저 기록한다.
				if (batchPath != null && !string.Equals(batchPath, _curPath, StringComparison.Ordinal))
				{
					File.AppendAllText(batchPath, sb.ToString(), _utf8);
					sb.Clear();
				}
				batchPath = _curPath;
				sb.Append(line).Append('\n');
				_curBytes += lineBytes;
				n++;
			}
			if (batchPath != null && sb.Length > 0)
			{
				File.AppendAllText(batchPath, sb.ToString(), _utf8);
			}
			CleanupOld();
		}
		catch
		{
		}
		finally
		{
			Interlocked.Exchange(ref _flushing, 0);
		}
	}

	/// <summary>쓸 파일을 정하고, 날짜 변경·100KB 초과 시 다음 번호 파일로 넘긴다.</summary>
	private static void EnsureFile(long incomingBytes)
	{
		string today = DateTime.Now.ToString("yyyyMMdd");
		if (_curPath.Length == 0 || !string.Equals(_curDate, today, StringComparison.Ordinal))
		{
			_curDate = today;
			_seq = HighestSeqForToday(today);
			if (_seq == 0)
			{
				_seq = 1;
			}
			_curPath = Path.Combine(_dir, $"bot_{today}_{_seq:D3}.log");
			_curBytes = SafeLen(_curPath);
		}
		if (_curBytes > 0 && _curBytes + incomingBytes > MaxFileBytes)
		{
			_seq++;
			_curPath = Path.Combine(_dir, $"bot_{today}_{_seq:D3}.log");
			_curBytes = 0;
		}
	}

	private static int HighestSeqForToday(string today)
	{
		int max = 0;
		try
		{
			string prefix = "bot_" + today + "_";
			foreach (string f in Directory.GetFiles(_dir, "bot_" + today + "_*.log"))
			{
				string name = Path.GetFileNameWithoutExtension(f);
				if (name.StartsWith(prefix, StringComparison.Ordinal)
					&& int.TryParse(name.Substring(prefix.Length), out int s) && s > max)
				{
					max = s;
				}
			}
		}
		catch
		{
		}
		return max;
	}

	private static long SafeLen(string path)
	{
		try
		{
			FileInfo fi = new FileInfo(path);
			return fi.Exists ? fi.Length : 0L;
		}
		catch
		{
			return 0L;
		}
	}

	private static void CleanupOld()
	{
		if ((DateTime.Now - _lastRetentionCheck).TotalHours < 6.0)
		{
			return;
		}
		_lastRetentionCheck = DateTime.Now;
		try
		{
			DateTime cutoff = DateTime.Now.Date.AddDays(-RetentionDays);
			foreach (string f in Directory.GetFiles(_dir, "bot_*.log"))
			{
				try
				{
					if (File.GetLastWriteTime(f) < cutoff)
					{
						File.Delete(f);
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}
}
