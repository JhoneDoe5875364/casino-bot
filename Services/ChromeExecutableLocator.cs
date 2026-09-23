using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace PragmaticBot.Services;

/// <summary>
/// Finds a Chrome already installed on the machine. Used as a fallback when the bundled
/// Chromium cannot be downloaded, so a network or disk problem does not stop the bot outright.
/// </summary>
public static class ChromeExecutableLocator
{
	private static readonly string[] FallbackPaths = new string[3]
	{
		"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
		"C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe",
		Path.Combine(
			System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
			"Google\\Chrome\\Application\\chrome.exe")
	};

	public static string? Find()
	{
		try
		{
			string appPath = Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\chrome.exe", null, null) as string;
			if (!string.IsNullOrWhiteSpace(appPath) && File.Exists(appPath))
			{
				return appPath;
			}
		}
		catch
		{
		}
		return FallbackPaths.FirstOrDefault(File.Exists);
	}
}
