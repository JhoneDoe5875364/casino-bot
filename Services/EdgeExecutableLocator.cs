using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace PragmaticBot.Services;

public static class EdgeExecutableLocator
{
	private static readonly string[] FallbackPaths = new string[2] { "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe", "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe" };

	public static string? Find()
	{
		try
		{
			string appPath = Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\msedge.exe", null, null) as string;
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
