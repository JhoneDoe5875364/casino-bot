using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace PragmaticBot.Services;

public static class UiPreferenceService
{
	public const string RoomGateEnabledKey = "roomGateEnabled";

	public const string PeakResetEnabledKey = "peakResetEnabled";

	public const string StepResetEnabledKey = "stepResetEnabled";

	public const string StepResetIndexKey = "stepResetIndex";

	public const string VirtualBetEnabledKey = "virtualBetEnabled";

	public const string IgnorePendingBetEnabledKey = "ignorePendingBetEnabled";

	public const string ShoeControlEnabledKey = "shoeControlEnabled";

	public const string ShoeControlIndexKey = "shoeControlIndex";

	public const string StepAlarmEnabledKey = "stepAlarmEnabled";

	public const string StepAlarmIndexKey = "stepAlarmIndex";

	public const string BetSplitCountKey = "betSplitCount";

	private static readonly Lock Gate = new Lock();

	private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PragmaticBot", "ui-preferences.json");

	public static bool GetBool(string userId, string key, bool defaultValue = false)
	{
		if (string.IsNullOrEmpty(userId))
		{
			return defaultValue;
		}
		using (Gate.EnterScope())
		{
			if (!Load().TryGetValue(MakeKey(userId, key), out var value))
			{
				return defaultValue;
			}
			return value.ValueKind switch
			{
				JsonValueKind.True => true, 
				JsonValueKind.False => false, 
				_ => defaultValue, 
			};
		}
	}

	public static int GetInt(string userId, string key, int defaultValue = -1)
	{
		if (string.IsNullOrEmpty(userId))
		{
			return defaultValue;
		}
		using (Gate.EnterScope())
		{
			if (!Load().TryGetValue(MakeKey(userId, key), out var value))
			{
				return defaultValue;
			}
			int number;
			return (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number)) ? number : defaultValue;
		}
	}

	public static void SetBool(string userId, string key, bool value)
	{
		Set(userId, key, JsonSerializer.SerializeToElement(value), value);
	}

	public static void SetInt(string userId, string key, int value)
	{
		Set(userId, key, JsonSerializer.SerializeToElement(value), value);
	}

	private static void Set(string userId, string key, JsonElement value, object logValue)
	{
		if (string.IsNullOrEmpty(userId))
		{
			return;
		}
		using (Gate.EnterScope())
		{
			Dictionary<string, JsonElement> all = Load();
			all[MakeKey(userId, key)] = value;
			try
			{
				string filePath = FilePath;
				Directory.CreateDirectory(Path.GetDirectoryName(filePath));
				File.WriteAllText(filePath, JsonSerializer.Serialize(all, WriteOptions));
			}
			catch (Exception ex)
			{
				FileLogger.Log($"[화면설정] 저장 실패 ({key}={logValue}): {ex.Message}", "Warning", "UI");
			}
		}
	}

	private static string MakeKey(string userId, string key)
	{
		return userId + ":" + key;
	}

	private static Dictionary<string, JsonElement> Load()
	{
		try
		{
			string path = FilePath;
			if (!File.Exists(path))
			{
				return new Dictionary<string, JsonElement>();
			}
			return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path)) ?? new Dictionary<string, JsonElement>();
		}
		catch (Exception ex)
		{
			FileLogger.Log("[화면설정] 불러오기 실패 — 기본값으로 시작합니다: " + ex.Message, "Warning", "UI");
			return new Dictionary<string, JsonElement>();
		}
	}
}
