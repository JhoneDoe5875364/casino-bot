using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PragmaticBot.Models;

namespace PragmaticBot.Services;

public class SecondLogicSettingService
{
	private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PragmaticBot", "secondlogic.json");

	private static readonly object FileLock = new object();

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public SecondLogicSetting Get(string userId)
	{
		try
		{
			lock (FileLock)
			{
				if (LoadMap().TryGetValue(userId, out SecondLogicSetting setting) && setting != null)
				{
					return setting;
				}
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[SecondLogic] 설정 로드 실패(기본값 사용): " + ex.Message, "Warning");
		}
		return SecondLogicSetting.Default();
	}

	public void Save(string userId, SecondLogicSetting setting)
	{
		lock (FileLock)
		{
			Dictionary<string, SecondLogicSetting> map = LoadMap();
			map[userId] = setting;
			string dir = Path.GetDirectoryName(SettingsPath);
			if (!string.IsNullOrEmpty(dir))
			{
				Directory.CreateDirectory(dir);
			}
			File.WriteAllText(SettingsPath, JsonSerializer.Serialize(map, JsonOptions));
		}
	}

	private static Dictionary<string, SecondLogicSetting> LoadMap()
	{
		if (!File.Exists(SettingsPath))
		{
			return new Dictionary<string, SecondLogicSetting>();
		}
		string json = File.ReadAllText(SettingsPath);
		if (string.IsNullOrWhiteSpace(json))
		{
			return new Dictionary<string, SecondLogicSetting>();
		}
		return JsonSerializer.Deserialize<Dictionary<string, SecondLogicSetting>>(json) ?? new Dictionary<string, SecondLogicSetting>();
	}
}
