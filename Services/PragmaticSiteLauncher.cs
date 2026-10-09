using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PragmaticBot.Services;

public static class PragmaticSiteLauncher
{
	public const string ProfileRootDir = "C:\\PragmaticBotProfiles";

	private static readonly int[] CandidateBasePorts = new int[5] { 9222, 9500, 18222, 28222, 38222 };

	public static async Task<int?> EnsureSiteChromeRunningAsync(int siteSlot, string browserType, int preferredPort, int wsServerPort, string? startUrl = null, IReadOnlyCollection<int>? excludePorts = null, int timeoutSeconds = 20, IProgress<string>? progress = null)
	{
		bool isEdge = string.Equals(browserType, "Edge", StringComparison.OrdinalIgnoreCase);
		List<int> candidates = BuildCandidatePorts(preferredPort, siteSlot, excludePorts);
		if (candidates.Count == 0)
		{
			FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: 후보 포트가 모두 다른 슬롯에 점유되어 있습니다. " + "사이트 설정의 디버그 포트가 슬롯끼리 겹치지 않는지 확인하세요.", "Error", "MultiSite");
			return null;
		}
		string profileDir = GetProfileDir(siteSlot, browserType);
		string exePath;
		if (isEdge)
		{
			exePath = EdgeExecutableLocator.Find();
			if (exePath == null)
			{
				FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: Microsoft Edge를 찾지 못했습니다. " + "Edge를 설치하거나 이 슬롯의 브라우저 종류를 Chrome으로 바꾸세요.", "Error", "MultiSite");
				return null;
			}
		}
		else
		{
			exePath = await ChromeShortcutHelper.GetBundleExecutableAsync(progress).ConfigureAwait(continueOnCapturedContext: false);
			if (exePath == null || !File.Exists(exePath))
			{
				FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: 번들 Chromium 확보 실패", "Error", "MultiSite");
				return null;
			}
		}
		if (KillZombieBrowsers(profileDir, siteSlot, exePath) > 0)
		{
			await Task.Delay(1500).ConfigureAwait(continueOnCapturedContext: false);
		}
		foreach (int port in candidates)
		{
			if (await IsCdpReachableAsync(port).ConfigureAwait(continueOnCapturedContext: false))
			{
				FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}({browserType}): 포트 {port}에 이미 CDP 응답 중 - 재사용");
				return port;
			}
		}
		// Slot browsers obey the same grant as the main one — otherwise a Pragmatic-only account
		// would still get Evolution's hooks on every split-betting window.
		GameProviders granted = AuthService.CurrentProviders;
		string? extensionDir = granted.HasFlag(GameProviders.PragmaticPlay)
			? PrepareSlotExtension(siteSlot, browserType, wsServerPort)
			: null;
		string? evolutionDir = granted.HasFlag(GameProviders.Evolution)
			? PrepareSlotExtensionEvolution(siteSlot, browserType, wsServerPort)
			: null;
		if (extensionDir == null && evolutionDir == null)
		{
			// never launch with an empty --load-extension
			extensionDir = PrepareSlotExtension(siteSlot, browserType, wsServerPort);
			if (extensionDir == null)
			{
				FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: Extension 사본 생성 실패", "Error", "MultiSite");
				return null;
			}
		}
		string loadArg = string.Join(",", new[] { extensionDir, evolutionDir }.Where(d => !string.IsNullOrEmpty(d)));
		FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot} 익스텐션 로드: {GameProviderNames.Label(granted)}", "Info", "MultiSite");
		if (KillZombieBrowsers(profileDir, siteSlot) > 0)
		{
			await Task.Delay(1500).ConfigureAwait(continueOnCapturedContext: false);
		}
		int? chosenPort = SelectBindablePort(candidates, siteSlot);
		if (!chosenPort.HasValue)
		{
			FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: 사용 가능한 디버깅 포트가 없습니다. 후보: {string.Join(", ", candidates)}. " + "`netsh interface ipv4 show excludedportrange protocol=tcp`로 Windows 예약 대역을 확인하세요.", "Error", "MultiSite");
			return null;
		}
		try
		{
			Directory.CreateDirectory(profileDir);
		}
		catch (Exception ex)
		{
			FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: 프로필 디렉터리 생성 실패: {ex.Message}", "Warning", "MultiSite");
		}
		List<string> argList = new List<string>
		{
			$"--remote-debugging-port={chosenPort.Value}",
			"--user-data-dir=\"" + profileDir + "\"",
			// 권한에 포함된 익스텐션만 콤마로 결합해 로드
			"--load-extension=\"" + loadArg + "\"",
			"--no-first-run",
			"--no-default-browser-check",
			// 라이브 바카라 테이블마다 오디오 스트림을 디코딩하지 않도록 음소거 — 베팅은 데이터 소켓으로
			// 하므로 소리는 불필요하고, 테이블이 많을수록 CPU 절감이 크다.
			"--mute-audio",
			// content.js -> ws://127.0.0.1 릴레이가 Chrome의 Local/Private Network Access 정책에
			// 막히는 것(ERR_BLOCKED_BY_LOCAL_NETWORK_ACCESS_CHECKS) 방지
			"--disable-features=LocalNetworkAccessChecks,BlockInsecurePrivateNetworkRequests,PrivateNetworkAccessSendPreflights,PrivateNetworkAccessRespectPreflightResults"
		};
		if (!string.IsNullOrWhiteSpace(startUrl))
		{
			argList.Add("\"" + startUrl + "\"");
		}
		string args = string.Join(' ', argList);
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = exePath,
				Arguments = args,
				UseShellExecute = true
			});
			FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot} {browserType} 기동 (CDP {chosenPort.Value}, WS {wsServerPort}): {exePath}");
			if (isEdge)
			{
				FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: Edge는 버전에 따라 Extension 로드가 차단될 수 있습니다. " + "그 경우 CDP 경로만으로 동작하며, 잔액이 DOM 폴링으로 잡히는지 확인하세요.", "Info", "MultiSite");
			}
		}
		catch (Exception ex2)
		{
			FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot} 기동 실패: {ex2.Message}", "Error", "MultiSite");
			return null;
		}
		DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
		while (DateTime.UtcNow < deadline)
		{
			await Task.Delay(400).ConfigureAwait(continueOnCapturedContext: false);
			if (await IsCdpReachableAsync(chosenPort.Value).ConfigureAwait(continueOnCapturedContext: false))
			{
				FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: 포트 {chosenPort.Value} CDP 응답 확인");
				return chosenPort.Value;
			}
		}
		FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: 포트 {chosenPort.Value} CDP 응답 없음 ({timeoutSeconds}초 타임아웃). 같은 프로필({profileDir})로 떠 있는 응답 없는 브라우저가 있으면 종료 후 다시 시도하세요.", "Warning", "MultiSite");
		return null;
	}

	public static string GetProfileDir(int siteSlot, string browserType)
	{
		return Path.Combine("C:\\PragmaticBotProfiles", $"Site{siteSlot}_{NormalizeBrowserType(browserType)}");
	}

	private static string NormalizeBrowserType(string browserType)
	{
		if (!string.Equals(browserType, "Edge", StringComparison.OrdinalIgnoreCase))
		{
			return "Chrome";
		}
		return "Edge";
	}

	// Evolution 전용 익스텐션 사본 준비. 폴더가 없으면(선택 사항) null 반환 → 호출측은 Pragmatic만 로드.
	private static string? PrepareSlotExtensionEvolution(int siteSlot, string browserType, int wsServerPort)
	{
		try
		{
			string sourceDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtensionEvolution");
			if (!Directory.Exists(sourceDir))
			{
				FileLogger.Log("[SiteLauncher] ExtensionEvolution 폴더 없음(선택) - Pragmatic만 로드: " + sourceDir, "Info", "MultiSite");
				return null;
			}
			string targetDir = Path.Combine(GetProfileDir(siteSlot, browserType), "ExtensionEvolution");
			Directory.CreateDirectory(targetDir);
			foreach (string file in Directory.GetFiles(sourceDir))
			{
				File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
			}
			string contentPath = Path.Combine(targetDir, "content.js");
			if (File.Exists(contentPath))
			{
				string? replaced = ReplaceBasePort(File.ReadAllText(contentPath), wsServerPort);
				if (replaced != null)
				{
					File.WriteAllText(contentPath, replaced);
				}
			}
			FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot} ExtensionEvolution 사본 준비 완료 (기준 포트 {wsServerPort}): {targetDir}");
			return targetDir;
		}
		catch (Exception ex)
		{
			FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot} ExtensionEvolution 사본 생성 오류(무시): {ex.Message}", "Warning", "MultiSite");
			return null;
		}
	}

	private static string? PrepareSlotExtension(int siteSlot, string browserType, int wsServerPort)
	{
		try
		{
			string sourceDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Extension");
			if (!Directory.Exists(sourceDir))
			{
				FileLogger.Log("[SiteLauncher] 원본 Extension 폴더 없음: " + sourceDir, "Error", "MultiSite");
				return null;
			}
			string targetDir = Path.Combine(GetProfileDir(siteSlot, browserType), "Extension");
			Directory.CreateDirectory(targetDir);
			string[] files = Directory.GetFiles(sourceDir);
			foreach (string file in files)
			{
				string dest = Path.Combine(targetDir, Path.GetFileName(file));
				File.Copy(file, dest, overwrite: true);
			}
			string contentPath = Path.Combine(targetDir, "content.js");
			if (File.Exists(contentPath))
			{
				string replaced = ReplaceBasePort(File.ReadAllText(contentPath), wsServerPort);
				if (replaced == null)
				{
					FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: content.js에서 BASE_PORT 상수를 찾지 못했습니다. " + "Extension을 수정했다면 상수 선언 형식을 확인하세요.", "Error", "MultiSite");
					return null;
				}
				File.WriteAllText(contentPath, replaced);
			}
			FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot} Extension 사본 준비 완료 (기준 포트 {wsServerPort}): {targetDir}");
			return targetDir;
		}
		catch (Exception ex)
		{
			FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot} Extension 사본 생성 오류: {ex.Message}", "Error", "MultiSite", ex.StackTrace);
			return null;
		}
	}

	private static string? ReplaceBasePort(string js, int port)
	{
		Regex pattern = new Regex("(const\\s+BASE_PORT\\s*=\\s*)\\d+", RegexOptions.None);
		if (!pattern.IsMatch(js))
		{
			return null;
		}
		return pattern.Replace(js, $"${{1}}{port}", 1);
	}

	private static List<int> BuildCandidatePorts(int preferredPort, int siteSlot, IReadOnlyCollection<int>? excludePorts)
	{
		List<int> ports = new List<int>();
		if (preferredPort > 0)
		{
			ports.Add(preferredPort);
		}
		ports.AddRange(CandidateBasePorts.Select((int basePort) => basePort + siteSlot));
		return (from port in ports.Distinct()
			where excludePorts == null || !excludePorts.Contains(port)
			select port).ToList();
	}

	private static int? SelectBindablePort(IReadOnlyList<int> candidates, int siteSlot)
	{
		foreach (int port in candidates)
		{
			if (IsPortBindable(port, siteSlot))
			{
				return port;
			}
		}
		return null;
	}

	private static bool IsPortBindable(int port, int siteSlot)
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
			FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: 포트 {port} 사용 불가 — {reason} ({ex.SocketErrorCode}/{ex.ErrorCode}). 다음 후보 시도.", "Warning", "MultiSite");
			return false;
		}
		finally
		{
			listener?.Stop();
		}
	}

	private static int KillZombieBrowsers(string userDataDir, int siteSlot, string? keepExePath = null)
	{
		if (IsUserDefaultProfile(userDataDir))
		{
			FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: 프로필({userDataDir})이 사용자 기본 프로필로 판단되어 좀비 정리를 건너뜁니다.", "Warning", "MultiSite");
			return 0;
		}
		string targetDir;
		try
		{
			targetDir = NormalizePath(userDataDir);
		}
		catch
		{
			return 0;
		}
		List<int> pids;
		try
		{
			using ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine, ExecutablePath FROM Win32_Process WHERE Name = 'chrome.exe' OR Name = 'msedge.exe'");
			pids = (from num in searcher.Get().Cast<ManagementObject>().Select(delegate(ManagementObject mo)
				{
					using (mo)
					{
						string text = ((!(mo["CommandLine"] is string commandLine)) ? null : ExtractUserDataDir(commandLine));
						if (text == null || !string.Equals(text, targetDir, StringComparison.OrdinalIgnoreCase))
						{
							return 0;
						}
						if (keepExePath != null)
						{
							if (!(mo["ExecutablePath"] is string text2) || string.Equals(text2, keepExePath, StringComparison.OrdinalIgnoreCase))
							{
								return 0;
							}
							FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: 이전 버전 브라우저 종료 대상 - {text2}", "Warning", "MultiSite");
						}
						return Convert.ToInt32(mo["ProcessId"]);
					}
				})
				where num > 0
				select num).ToList();
		}
		catch (Exception ex)
		{
			FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: 프로세스 조회 실패로 좀비 정리를 건너뜁니다. ({ex.Message})", "Warning", "MultiSite");
			return 0;
		}
		int killed = 0;
		foreach (int pid in pids)
		{
			try
			{
				using Process process = Process.GetProcessById(pid);
				process.Kill(entireProcessTree: true);
				process.WaitForExit(3000);
				killed++;
			}
			catch
			{
			}
		}
		if (killed > 0)
		{
			string what = ((keepExePath == null) ? "응답 없는 브라우저" : "이전 버전 브라우저");
			FileLogger.Log($"[SiteLauncher] 슬롯{siteSlot}: {what} {killed}개 종료 (프로필 {targetDir})", "Warning", "MultiSite");
		}
		return killed;
	}

	private static bool IsUserDefaultProfile(string userDataDir)
	{
		try
		{
			string target = NormalizePath(userDataDir);
			string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			return new string[2]
			{
				Path.Combine(localAppData, "Google", "Chrome", "User Data"),
				Path.Combine(localAppData, "Microsoft", "Edge", "User Data")
			}.Any((string profile) => string.Equals(NormalizePath(profile), target, StringComparison.OrdinalIgnoreCase));
		}
		catch
		{
			return true;
		}
	}

	private static string? ExtractUserDataDir(string commandLine)
	{
		int idx = commandLine.IndexOf("--user-data-dir=", StringComparison.OrdinalIgnoreCase);
		if (idx < 0)
		{
			return null;
		}
		string rest = commandLine.Substring(idx + "--user-data-dir=".Length);
		string raw;
		if (rest.StartsWith('"'))
		{
			int end = rest.IndexOf('"', 1);
			if (end <= 1)
			{
				return null;
			}
			raw = rest.Substring(1, end - 1);
		}
		else
		{
			int cut = new int[2]
			{
				rest.IndexOf('"'),
				rest.IndexOf(" --", StringComparison.Ordinal)
			}.Where((int i) => i >= 0).DefaultIfEmpty(-1).Min();
			raw = ((cut >= 0) ? rest.Substring(0, cut) : rest).Trim();
		}
		try
		{
			return string.IsNullOrWhiteSpace(raw) ? null : NormalizePath(raw);
		}
		catch
		{
			return null;
		}
	}

	private static string NormalizePath(string path)
	{
		return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
	}

	private static async Task<bool> IsCdpReachableAsync(int port)
	{
		_ = 1;
		try
		{
			using HttpClient http = new HttpClient
			{
				Timeout = TimeSpan.FromSeconds(1L)
			};
			HttpResponseMessage resp = await http.GetAsync($"http://127.0.0.1:{port}/json/version").ConfigureAwait(continueOnCapturedContext: false);
			if (!resp.IsSuccessStatusCode)
			{
				return false;
			}
			using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false));
			JsonElement value;
			return doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out value);
		}
		catch
		{
			return false;
		}
	}
}
