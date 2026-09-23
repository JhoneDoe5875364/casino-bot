using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PragmaticBot.Services;

/// <summary>
/// Remembers the PP account id (the <c>uId</c> of a bet frame) between runs.
/// <para>
/// PP MULTIPLAY never sends a <c>seat</c> message, so the id can only be read off an outgoing bet.
/// Without this the operator would have to place one bet by hand after every restart just to unblock
/// automatic betting. The id is stored per bot account, because two operators on one machine sign in
/// to different PP accounts.
/// </para>
/// </summary>
public static class PpUserIdStore
{
	private static string FilePath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"PragmaticBot",
		"ppuserid.json");

	public static string Load(string botUserId)
	{
		try
		{
			if (!File.Exists(FilePath))
			{
				return string.Empty;
			}
			var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
			if (map != null && map.TryGetValue(botUserId ?? "", out string? stored) && !string.IsNullOrWhiteSpace(stored))
			{
				return stored;
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[PpUserIdStore] 로드 실패: " + ex.Message, "Warning");
		}
		return string.Empty;
	}

	public static void Save(string botUserId, string ppUserId)
	{
		if (string.IsNullOrWhiteSpace(botUserId) || string.IsNullOrWhiteSpace(ppUserId))
		{
			return;
		}
		try
		{
			string path = FilePath;
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			Dictionary<string, string> map = new Dictionary<string, string>();
			if (File.Exists(path))
			{
				map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? map;
			}
			map[botUserId] = ppUserId;
			File.WriteAllText(path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception ex)
		{
			FileLogger.Log("[PpUserIdStore] 저장 실패: " + ex.Message, "Warning");
		}
	}
}
