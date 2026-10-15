using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using PragmaticBot.Services;
using PuppeteerSharp;
using PuppeteerSharp.BrowserData;

namespace PragmaticBot;

public static class ChromeShortcutHelper
{
	[ComImport]
	[Guid("00021401-0000-0000-C000-000000000046")]
	private class ShellLink
	{
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("000214F9-0000-0000-C000-000000000046")]
	private interface IShellLink
	{
		void GetPath([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out nint pfd, int fFlags);

		void GetIDList(out nint ppidl);

		void SetIDList(nint pidl);

		void GetDescription([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);

		void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

		void GetWorkingDirectory([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);

		void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

		void GetArguments([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);

		void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

		void GetHotkey(out short pwHotkey);

		void SetHotkey(short wHotkey);

		void GetShowCmd(out int piShowCmd);

		void SetShowCmd(int iShowCmd);

		void GetIconLocation([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);

		void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

		void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);

		void Resolve(nint hwnd, int fFlags);

		void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
	}

	private static readonly int[] CandidateDebugPorts = new int[6] { 9222, 9500, 9700, 18222, 28222, 38222 };

	private const string UserDataDir = "C:\\ChromeDebugProfile";

	private const string BuildMarkerFile = ".pragmaticbot-build";

	private const string BundleDir = "C:\\PragmaticBot\\Chromium";

	/// <summary>
	/// Chrome 137부터 --load-extension 스위치를 무시하므로, 익스텐션을 자동으로 붙이려면
	/// 137 미만 빌드를 써야 합니다. false로 두면 최신 빌드를 받고 익스텐션 없이
	/// CDP 경로로만 동작합니다.
	/// </summary>
	private static readonly bool UsePinnedBuild = true;

	private const int ExtensionBlockedFromMajor = 137;

	private static readonly string[] PinnedBuildIds = new string[5] { "136.0.7103.113", "136.0.7103.94", "135.0.7049.114", "134.0.6998.165", "133.0.6943.141" };

	/// <summary>Why the last EnsureChromeRunningAsync attempt gave up, so the UI can say something useful.</summary>
	public enum StartFailure
	{
		None,
		/// <summary>번들 Chromium을 내려받거나 푸는 데 실패했고, 설치된 Chrome도 없음.</summary>
		BrowserUnavailable,
		/// <summary>후보 디버깅 포트를 하나도 바인딩하지 못함 (Windows 예약 대역 등).</summary>
		NoDebugPort,
		/// <summary>실행 파일은 있는데 프로세스를 띄우지 못함.</summary>
		ProcessStartFailed,
		/// <summary>기동은 했지만 디버깅 포트가 응답하지 않음.</summary>
		DebugPortSilent
	}

	public static StartFailure LastFailure { get; private set; }

	public static string? LastFailureDetail { get; private set; }

	private static void Fail(StartFailure reason, string? detail = null)
	{
		LastFailure = reason;
		LastFailureDetail = detail;
	}

	private static string? _cachedBundleExePath;

	private static readonly SemaphoreSlim _downloadGate = new SemaphoreSlim(1, 1);

	private static int _activeDebugPort = CandidateDebugPorts[0];

	public static int CurrentDebugPort => _activeDebugPort;

	public static string BundleRootDir => "C:\\PragmaticBot\\Chromium";

	/// <summary>
	/// Opens <paramref name="url"/> in a new tab of the debug-enabled Chromium. Works both for a
	/// browser just launched and one that was already running and reused — a command-line start URL
	/// would only cover the first case.
	/// </summary>
	public static async Task<bool> OpenUrlAsync(int port, string url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return false;
		}
		try
		{
			using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
			string endpoint = $"http://127.0.0.1:{port}/json/new?{Uri.EscapeDataString(url)}";
			// Chrome 111+ only accepts PUT here; GET is refused.
			using HttpResponseMessage resp = await http.PutAsync(endpoint, null).ConfigureAwait(continueOnCapturedContext: false);
			bool ok = resp.IsSuccessStatusCode;
			FileLogger.Log($"[ChromeShortcutHelper] 사이트 열기 {(ok ? "성공" : "실패(" + (int)resp.StatusCode + ")")}: {url}", ok ? "Info" : "Warning", "Init");
			return ok;
		}
		catch (Exception ex)
		{
			FileLogger.Log("[ChromeShortcutHelper] 사이트 열기 실패: " + url + " — " + ex.Message, "Warning", "Init");
			return false;
		}
	}

	private static string BuildArguments(int port)
	{
		// PP(프라그마틱)는 데이터·베팅·잔액이 전부 CDP로 동작하므로 익스텐션이 필요 없다. 에볼루션만 소켓
		// 암호화 때문에 복호화·후킹 inject.js(MAIN world)가 익스텐션으로만 돈다 → 에볼루션 권한일 때만 로드.
		GameProviders granted = AuthService.CurrentProviders;
		bool useExtension = AppConfiguration.UseExtension;
		string loadArg = string.Empty;
		if (useExtension)
		{
			string evoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtensionEvolution");
			if (granted.HasFlag(GameProviders.Evolution) && Directory.Exists(evoPath))
			{
				loadArg = evoPath;
			}
		}
		FileLogger.Log($"[ChromeShortcutHelper] 익스텐션 로드: {(string.IsNullOrEmpty(loadArg) ? "없음(PP는 CDP)" : loadArg)}", "Info", DbLogService.SessionCategory);
		List<string> argList = new List<string>
		{
			$"--remote-debugging-port={port}",
			"--user-data-dir=\"C:\\ChromeDebugProfile\"",
			"--no-first-run",
			"--no-default-browser-check",
			"--mute-audio",
			// GPU 가속 렌더러 크래시(STATUS_ACCESS_VIOLATION, "Aw Snap") 회피 — stake 로그인/게임 페이지에서
			// GPU 드라이버 경로가 렌더러를 죽이는 것을 막는다. 영상은 소프트웨어 렌더되지만 베팅은 데이터 소켓이라 무관.
			"--disable-gpu",
			// RendererCodeIntegrity: 백신/보안 SW가 렌더러에 서명 안 된 DLL을 주입해 생기는 STATUS_ACCESS_VIOLATION
			//   ("Aw Snap")의 대표 원인. 설치된 크롬은 화이트리스트라 괜찮고 번들 크롬(비표준 경로)만 주입 충돌로
			//   죽는 패턴과 일치 → 끈다. (렌더러 보안 완화일 뿐 봇 동작엔 무관)
			// LocalNetworkAccessChecks...: content.js -> ws://127.0.0.1 릴레이가 로컬 네트워크 정책에 막히는 것 방지.
			"--disable-features=RendererCodeIntegrity,LocalNetworkAccessChecks,BlockInsecurePrivateNetworkRequests,PrivateNetworkAccessSendPreflights,PrivateNetworkAccessRespectPreflightResults"
		};
		if (useExtension && !string.IsNullOrEmpty(loadArg))
		{
			argList.Add("--load-extension=\"" + loadArg + "\"");
		}
		return string.Join(' ', argList);
	}

	public static void CreateDesktopShortcut()
	{
	}

	public static Task<string?> GetBundleExecutableAsync(IProgress<string>? progress = null)
	{
		return GetOrDownloadBundleAsync(progress);
	}

	public static async Task<bool> EnsureChromeRunningAsync(int waitSeconds = 15, IProgress<string>? progress = null)
	{
		Fail(StartFailure.None);
		progress?.Report("Chromium 준비 중...");
		string exePath = await GetOrDownloadBundleAsync(progress).ConfigureAwait(continueOnCapturedContext: false);
		if (exePath == null)
		{
			Fail(StartFailure.BrowserUnavailable);
			return false;
		}
		if (KillBundledChromeProcesses(exePath) > 0)
		{
			progress?.Report("이전 버전 Chromium 정리 중...");
			await Task.Delay(1500).ConfigureAwait(continueOnCapturedContext: false);
		}
		int[] candidateDebugPorts = CandidateDebugPorts;
		foreach (int port in candidateDebugPorts)
		{
			// only adopt a running browser if it is ours — an unrelated Chrome on 9222 must not be
			// mistaken for the game window
			if (await IsOurDebugPortAsync(port).ConfigureAwait(continueOnCapturedContext: false))
			{
				_activeDebugPort = port;
				FileLogger.Log($"[ChromeShortcutHelper] 기존 Chromium 재사용 (포트 {port})", "Info", "Init");
				return true;
			}
		}
		if (KillBundledChromeProcesses() > 0)
		{
			progress?.Report("응답 없는 Chromium 정리 중...");
			await Task.Delay(1500).ConfigureAwait(continueOnCapturedContext: false);
		}
		int? chosen = SelectBindablePort();
		if (!chosen.HasValue)
		{
			FileLogger.Log("[ChromeShortcutHelper] 사용 가능한 디버깅 포트 없음. 후보: " + string.Join(", ", CandidateDebugPorts) + ". `netsh interface ipv4 show excludedportrange protocol=tcp`로 예약 대역을 확인하세요.", "Error", "Init");
			progress?.Report("사용 가능한 디버깅 포트를 찾지 못했습니다.");
			Fail(StartFailure.NoDebugPort, string.Join(", ", CandidateDebugPorts));
			return false;
		}
		_activeDebugPort = chosen.Value;
		// a pinned/older build cannot open a profile a newer one has touched
		ResetProfileIfDowngraded(exePath);

		// A damaged or version-mismatched profile makes Chrome exit moments after start, so the
		// debugging port never answers. Version checks cannot catch every such case, so on the
		// first failure the profile is set aside and the launch retried once from a clean one.
		for (int attempt = 1; attempt <= 2; attempt++)
		{
			if (!TryStartChrome(exePath, progress))
			{
				return false;
			}
			if (await WaitForDebugPortAsync(waitSeconds).ConfigureAwait(continueOnCapturedContext: false))
			{
				return true;
			}
			if (attempt == 1)
			{
				KillBundledChromeProcesses();
				if (ArchiveProfile("launch-failed"))
				{
					progress?.Report("프로필 문제로 보입니다. 새 프로필로 다시 시도합니다...");
					await Task.Delay(1500).ConfigureAwait(continueOnCapturedContext: false);
					continue;
				}
			}
			break;
		}
		FileLogger.Log($"[ChromeShortcutHelper] 포트 {_activeDebugPort}에서 CDP 응답 없음 (프로필 교체 후 재시도 포함). DevToolsActivePort 존재 여부: {File.Exists(Path.Combine(UserDataDir, "DevToolsActivePort"))}", "Error", "Init");
		Fail(StartFailure.DebugPortSilent, _activeDebugPort.ToString());
		return false;
	}

	private static bool TryStartChrome(string exePath, IProgress<string>? progress)
	{
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = exePath,
				Arguments = BuildArguments(_activeDebugPort),
				UseShellExecute = true
			};
			FileLogger.Log($"[ChromeShortcutHelper] Chromium 기동: {exePath} (포트 {_activeDebugPort})", "Info", "Init");
			Process.Start(startInfo);
			MarkProfileBuild(exePath);
			return true;
		}
		catch (Exception ex)
		{
			FileLogger.Log("[ChromeShortcutHelper] Chromium 프로세스 시작 실패: " + ex.Message, "Error", "Init");
			progress?.Report("Chromium 시작 실패: " + ex.Message);
			Fail(StartFailure.ProcessStartFailed, ex.Message);
			return false;
		}
	}

	/// <summary>Moves the debug profile aside so the next launch starts from a clean one.</summary>
	private static bool ArchiveProfile(string reason)
	{
		try
		{
			if (!Directory.Exists(UserDataDir))
			{
				return false;
			}
			string moved = $"{UserDataDir}.{reason}-{DateTime.Now:yyyyMMddHHmmss}";
			Directory.Move(UserDataDir, moved);
			FileLogger.Log($"[ChromeShortcutHelper] Chromium 기동 실패 — 프로필을 {moved} 로 옮기고 새 프로필로 재시도합니다.", "Warning", "Init");
			return true;
		}
		catch (Exception ex)
		{
			FileLogger.Log("[ChromeShortcutHelper] 프로필 교체 실패: " + ex.Message, "Warning", "Init");
			return false;
		}
	}

	private static async Task<bool> WaitForDebugPortAsync(int waitSeconds)
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
		while (DateTime.UtcNow < deadline)
		{
			await Task.Delay(400).ConfigureAwait(continueOnCapturedContext: false);
			if (await IsDebugPortAliveAsync(_activeDebugPort).ConfigureAwait(continueOnCapturedContext: false))
			{
				FileLogger.Log($"[ChromeShortcutHelper] CDP 준비 완료 (포트 {_activeDebugPort})", "Info", "Init");
				return true;
			}
		}
		FileLogger.Log($"[ChromeShortcutHelper] 포트 {_activeDebugPort}에서 {waitSeconds}초간 CDP 응답 없음. DevToolsActivePort 존재 여부: {File.Exists(Path.Combine("C:\\ChromeDebugProfile", "DevToolsActivePort"))}", "Error", "Init");
		Fail(StartFailure.DebugPortSilent, _activeDebugPort.ToString());
		return false;
	}

	private static int? SelectBindablePort()
	{
		int[] candidateDebugPorts = CandidateDebugPorts;
		foreach (int port in candidateDebugPorts)
		{
			if (IsPortBindable(port))
			{
				return port;
			}
		}
		return null;
	}

	private static bool IsPortBindable(int port)
	{
		TcpListener listener = null;
		try
		{
			listener = new TcpListener(IPAddress.Loopback, port);
			listener.Start();
			return true;
		}
		catch (SocketException ex)
		{
			string reason = ((ex.SocketErrorCode == SocketError.AccessDenied) ? "Windows 예약 대역(excludedportrange)에 포함됨" : "다른 프로세스가 점유 중이거나 바인딩 불가");
			FileLogger.Log($"[ChromeShortcutHelper] 포트 {port} 사용 불가 — {reason} ({ex.SocketErrorCode}/{ex.ErrorCode}). 다음 후보 시도.", "Warning", "Init");
			return false;
		}
		finally
		{
			listener?.Stop();
		}
	}

	/// <summary>
	/// 봇 종료 시 호출한다. 봇 전용 번들 브라우저(C:\PragmaticBot\Chromium)를 모두 닫는다.
	/// 이걸 안 닫고 두면, 다음에 봇을 새로 켰을 때 '이미 로드된 그 브라우저'에 붙게 되어
	/// 페이지 로드 시점의 1회성 요청(지갑 조회 등)을 놓친다 — 매번 새로 띄워야 로드부터 감시된다.
	/// 사용자의 일반 Chrome(다른 경로)은 건드리지 않는다.
	/// </summary>
	public static int CloseBundledBrowsers()
	{
		return KillBundledChromeProcesses();
	}

	private static int KillBundledChromeProcesses(string? keepExePath = null)
	{
		int killed = 0;
		Process[] processesByName = Process.GetProcessesByName("chrome");
		foreach (Process p in processesByName)
		{
			try
			{
				string path = p.MainModule?.FileName;
				if (path != null && path.StartsWith("C:\\PragmaticBot\\Chromium", StringComparison.OrdinalIgnoreCase) && (keepExePath == null || !string.Equals(path, keepExePath, StringComparison.OrdinalIgnoreCase)))
				{
					if (keepExePath != null)
					{
						FileLogger.Log($"[ChromeShortcutHelper] 이전 버전 Chromium 종료: {path} (고정 빌드 {keepExePath}로 전환)", "Warning", "Init");
					}
					p.Kill(entireProcessTree: true);
					p.WaitForExit(3000);
					killed++;
				}
			}
			catch
			{
			}
			finally
			{
				p.Dispose();
			}
		}
		if (killed > 0 && keepExePath == null)
		{
			FileLogger.Log($"[ChromeShortcutHelper] 응답 없는 번들 Chromium {killed}개 종료", "Warning", "Init");
		}
		return killed;
	}

	private static async Task<string?> GetOrDownloadBundleAsync(IProgress<string>? progress)
	{
		if (_cachedBundleExePath != null && File.Exists(_cachedBundleExePath))
		{
			return _cachedBundleExePath;
		}
		await _downloadGate.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (_cachedBundleExePath != null && File.Exists(_cachedBundleExePath))
			{
				return _cachedBundleExePath;
			}
			Directory.CreateDirectory("C:\\PragmaticBot\\Chromium");
			BrowserFetcher fetcher = new BrowserFetcher(new BrowserFetcherOptions
			{
				Browser = SupportedBrowser.Chrome,
				Path = "C:\\PragmaticBot\\Chromium"
			});
			InstalledBrowser usable = (from b in fetcher.GetInstalledBrowsers()
				where b.Browser == SupportedBrowser.Chrome && File.Exists(b.GetExecutablePath()) && (!UsePinnedBuild || IsExtensionCompatible(b.BuildId))
				orderby ParseMajor(b.BuildId) descending
				select b).FirstOrDefault();
			if (usable != null)
			{
				_cachedBundleExePath = usable.GetExecutablePath();
				LogSelectedBuild(usable.BuildId, "설치본 재사용");
				return _cachedBundleExePath;
			}
			// Nothing usable — if a previous run died mid-extraction its leftovers would make
			// every download below fail on an "already exists" error, so clear them first.
			if (RemoveIncompleteBuilds() > 0)
			{
				progress?.Report("손상된 Chromium 설치 정리 중...");
			}
			if (UsePinnedBuild)
			{
				string[] pinnedBuildIds = PinnedBuildIds;
				foreach (string buildId in pinnedBuildIds)
				{
					try
					{
						if (!(await fetcher.CanDownloadAsync(buildId).ConfigureAwait(continueOnCapturedContext: false)))
						{
							FileLogger.Log("[ChromeShortcutHelper] 빌드 " + buildId + " 내려받을 수 없음 - 다음 후보 시도", "Warning", "Init");
							continue;
						}
						progress?.Report("Chromium " + buildId + " 다운로드 중 (~200MB, 최초 1회)...");
						InstalledBrowser downloaded = await fetcher.DownloadAsync(buildId).ConfigureAwait(continueOnCapturedContext: false);
						string pinnedPath = downloaded.GetExecutablePath();
						if (File.Exists(pinnedPath))
						{
							_cachedBundleExePath = pinnedPath;
							LogSelectedBuild(downloaded.BuildId, "고정 빌드 다운로드");
							return pinnedPath;
						}
					}
					catch (Exception ex)
					{
						FileLogger.Log($"[ChromeShortcutHelper] 빌드 {buildId} 다운로드 실패: {ex.Message} - 다음 후보 시도", "Warning", "Init");
					}
				}
			}
			else
			{
				progress?.Report("Chromium 다운로드 중 (~200MB, 최초 1회)...");
				InstalledBrowser downloaded2 = await fetcher.DownloadAsync().ConfigureAwait(continueOnCapturedContext: false);
				string exePath = downloaded2.GetExecutablePath();
				if (File.Exists(exePath))
				{
					_cachedBundleExePath = exePath;
					LogSelectedBuild(downloaded2.BuildId, "최신 빌드 다운로드");
					return exePath;
				}
			}
			FileLogger.Log("[ChromeShortcutHelper] 사용 가능한 Chromium이 없습니다.", "Error", "Init");
			return FallBackToSystemChrome(progress);
		}
		catch (Exception ex2)
		{
			progress?.Report("Chromium 다운로드 실패: " + ex2.Message);
			FileLogger.Log("[ChromeShortcutHelper] Chromium 준비 실패: " + ex2.Message, "Error", "Init", ex2.StackTrace);
			return FallBackToSystemChrome(progress);
		}
		finally
		{
			_downloadGate.Release();
		}
	}

	private static void LogSelectedBuild(string? buildId, string how)
	{
		bool extOk = IsExtensionCompatible(buildId);
		FileLogger.Log($"[ChromeShortcutHelper] Chromium {buildId} ({how}) — " + (extOk ? "Extension 로드 가능" : "Extension 미로드(137+) → 잔액은 CDP 지갑 응답 경로로 수집"), "Info", "Init");
	}

	/// <summary>
	/// Clears build folders left half-extracted by an interrupted download.
	///
	/// PuppeteerSharp will neither reuse such a folder (no chrome.exe) nor overwrite it
	/// (its unzip refuses to clobber existing files), so without this every later attempt
	/// re-downloads ~180MB and then dies on the first file it already wrote.
	/// </summary>
	/// <summary>
	/// Last resort when the bundled Chromium cannot be provisioned: drive the Chrome the user
	/// already has installed. It is launched with the same debugging flags, so everything
	/// downstream (CDP, extension) behaves the same.
	/// </summary>
	private static string? FallBackToSystemChrome(IProgress<string>? progress)
	{
		string? systemChrome = ChromeExecutableLocator.Find();
		if (systemChrome == null)
		{
			FileLogger.Log("[ChromeShortcutHelper] 설치된 Chrome도 찾지 못했습니다.", "Error", "Init");
			return null;
		}
		_cachedBundleExePath = systemChrome;
		progress?.Report("설치된 Chrome으로 대체합니다.");
		FileLogger.Log("[ChromeShortcutHelper] 번들 Chromium 대신 설치된 Chrome 사용: " + systemChrome, "Warning", "Init");
		return systemChrome;
	}

	/// <summary>
	/// Chrome refuses a profile that a newer build has already used — it shows
	/// "profile is from a newer version" and exits immediately, so the debugging port never
	/// opens. Downgrading (latest 146 -> pinned 136 for extension support) therefore has to start
	/// from a clean profile. The old one is moved aside rather than deleted so any site logins
	/// in it can still be recovered by hand.
	/// </summary>
	private static void ResetProfileIfDowngraded(string exePath)
	{
		try
		{
			int newMajor = ParseMajor(BuildIdFromPath(exePath));
			if (newMajor <= 0 || !Directory.Exists(UserDataDir))
			{
				return;
			}
			// Two records of who used the profile last, and either can be the higher one:
			// our marker (what the bot launched) and Chrome's own "Last Version" (updated by
			// any Chrome that opened it, including one started outside the bot). Take the max.
			int lastMajor = 0;
			string marker = Path.Combine(UserDataDir, BuildMarkerFile);
			if (File.Exists(marker))
			{
				lastMajor = Math.Max(lastMajor, ParseMajor(File.ReadAllText(marker).Trim()));
			}
			string lastVersion = Path.Combine(UserDataDir, "Last Version");
			if (File.Exists(lastVersion))
			{
				lastMajor = Math.Max(lastMajor, ParseMajor(File.ReadAllText(lastVersion).Trim()));
			}
			if (lastMajor <= newMajor)
			{
				return;
			}
			string moved = $"{UserDataDir}.v{lastMajor}-{DateTime.Now:yyyyMMddHHmmss}";
			Directory.Move(UserDataDir, moved);
			FileLogger.Log($"[ChromeShortcutHelper] Chrome {lastMajor} -> {newMajor} 다운그레이드: 프로필을 {moved} 로 옮기고 새로 시작합니다.", "Warning", "Init");
		}
		catch (Exception ex)
		{
			FileLogger.Log("[ChromeShortcutHelper] 프로필 정리 실패: " + ex.Message, "Warning", "Init");
		}
	}

	/// <summary>Records the build that owns the profile, so a later downgrade can be spotted.</summary>
	private static void MarkProfileBuild(string exePath)
	{
		try
		{
			Directory.CreateDirectory(UserDataDir);
			File.WriteAllText(Path.Combine(UserDataDir, BuildMarkerFile), BuildIdFromPath(exePath) ?? "");
		}
		catch
		{
		}
	}

	/// <summary>Pulls the build id out of a bundled Chromium path (Win64-136.0.7103.113).</summary>
	private static string? BuildIdFromPath(string exePath)
	{
		try
		{
			DirectoryInfo? dir = new FileInfo(exePath).Directory?.Parent;
			string name = dir?.Name ?? "";
			int dash = name.IndexOf('-');
			return (dash >= 0) ? name.Substring(dash + 1) : null;
		}
		catch
		{
			return null;
		}
	}
	private static int RemoveIncompleteBuilds()
	{
		int removed = 0;
		try
		{
			string chromeRoot = Path.Combine("C:\\PragmaticBot\\Chromium", "Chrome");
			if (Directory.Exists(chromeRoot))
			{
				string[] buildDirs = Directory.GetDirectories(chromeRoot);
				foreach (string buildDir in buildDirs)
				{
					if (Directory.EnumerateFiles(buildDir, "chrome.exe", SearchOption.AllDirectories).Any())
					{
						continue;
					}
					try
					{
						Directory.Delete(buildDir, recursive: true);
						removed++;
						FileLogger.Log("[ChromeShortcutHelper] 손상된 Chromium 설치 제거: " + buildDir, "Warning", "Init");
					}
					catch (Exception ex)
					{
						FileLogger.Log("[ChromeShortcutHelper] 손상된 설치 제거 실패: " + buildDir + " - " + ex.Message, "Warning", "Init");
					}
				}
			}

			// the archive is only left behind when extraction did not finish
			if (Directory.Exists("C:\\PragmaticBot\\Chromium"))
			{
				string[] leftovers = Directory.GetFiles("C:\\PragmaticBot\\Chromium", "*.zip");
				foreach (string zip in leftovers)
				{
					try
					{
						File.Delete(zip);
						FileLogger.Log("[ChromeShortcutHelper] 남은 다운로드 파일 제거: " + zip, "Warning", "Init");
					}
					catch
					{
					}
				}
			}
		}
		catch (Exception ex2)
		{
			FileLogger.Log("[ChromeShortcutHelper] 설치 정리 중 오류: " + ex2.Message, "Warning", "Init");
		}
		return removed;
	}

	private static bool IsExtensionCompatible(string? buildId)
	{
		int major = ParseMajor(buildId);
		if (major > 0)
		{
			return major < 137;
		}
		return false;
	}

	private static int ParseMajor(string? buildId)
	{
		if (string.IsNullOrWhiteSpace(buildId))
		{
			return 0;
		}
		if (!int.TryParse(buildId.Split('.')[0], out var major))
		{
			return 0;
		}
		return major;
	}

	private static async Task<bool> IsDebugPortAliveAsync(int port)
	{
		try
		{
			using HttpClient http = new HttpClient
			{
				Timeout = TimeSpan.FromMilliseconds(1000L)
			};
			return (await http.GetAsync($"http://127.0.0.1:{port}/json/version").ConfigureAwait(continueOnCapturedContext: false)).IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Whether the browser answering this port is the one we launched.
	/// <para>
	/// Any Chrome started with --remote-debugging-port answers /json/version, so "the port responds"
	/// used to be enough to adopt it. An unrelated browser sitting on 9222 was therefore reported as
	/// "Chrome 연결됨" while no game window existed, and the balance scraper read that window's page.
	/// Chrome writes the port it actually bound into DevToolsActivePort inside its own profile, so a
	/// match there proves the listener belongs to our profile.
	/// </para>
	/// </summary>
	private static async Task<bool> IsOurDebugPortAsync(int port)
	{
		if (!await IsDebugPortAliveAsync(port).ConfigureAwait(continueOnCapturedContext: false))
		{
			return false;
		}
		try
		{
			string marker = Path.Combine(UserDataDir, "DevToolsActivePort");
			if (File.Exists(marker))
			{
				string first = File.ReadLines(marker).FirstOrDefault() ?? "";
				if (int.TryParse(first.Trim(), out int boundPort))
				{
					if (boundPort == port)
					{
						return true;
					}
					FileLogger.Log($"[ChromeShortcutHelper] 포트 {port}는 다른 브라우저입니다 (우리 프로필이 잡은 포트는 {boundPort})", "Warning", "Init");
					return false;
				}
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[ChromeShortcutHelper] DevToolsActivePort 확인 실패: " + ex.Message, "Warning", "Init");
		}

		// No usable marker: fall back to the build. Our bundled Chrome is one of the pinned versions,
		// so anything else answering the port is somebody else's browser.
		if (!UsePinnedBuild)
		{
			return true;
		}
		try
		{
			using HttpClient http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1500L) };
			string body = await http.GetStringAsync($"http://127.0.0.1:{port}/json/version").ConfigureAwait(continueOnCapturedContext: false);
			using JsonDocument doc = JsonDocument.Parse(body);
			string browser = doc.RootElement.TryGetProperty("Browser", out var b) ? (b.GetString() ?? "") : "";
			if (PinnedBuildIds.Any(id => browser.Contains(id, StringComparison.Ordinal)))
			{
				return true;
			}
			FileLogger.Log($"[ChromeShortcutHelper] 포트 {port}에 다른 브라우저가 있습니다: {browser} (봇은 고정 빌드를 사용합니다)", "Warning", "Init");
			return false;
		}
		catch (Exception ex)
		{
			FileLogger.Log("[ChromeShortcutHelper] 브라우저 버전 확인 실패: " + ex.Message, "Warning", "Init");
			return false;
		}
	}

	private static string? FindChromePath()
	{
		string[] array = new string[3]
		{
			"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
			"C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe",
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google\\Chrome\\Application\\chrome.exe")
		};
		foreach (string path in array)
		{
			if (File.Exists(path))
			{
				return path;
			}
		}
		return TryGetChromePathFromRegistry();
	}

	private static string? TryGetChromePathFromRegistry()
	{
		try
		{
			using RegistryKey key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\chrome.exe");
			string path = key?.GetValue(null) as string;
			if (!string.IsNullOrEmpty(path) && File.Exists(path))
			{
				return path;
			}
			using RegistryKey userKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\chrome.exe");
			string userPath = userKey?.GetValue(null) as string;
			if (!string.IsNullOrEmpty(userPath) && File.Exists(userPath))
			{
				return userPath;
			}
		}
		catch
		{
		}
		return null;
	}

	private static void CreateShortcut(string shortcutPath, string targetPath, string arguments, string description)
	{
		IShellLink obj = (IShellLink)new ShellLink();
		obj.SetPath(targetPath);
		obj.SetArguments(arguments);
		obj.SetDescription(description);
		obj.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? "");
		((IPersistFile)obj).Save(shortcutPath, fRemember: false);
	}
}
