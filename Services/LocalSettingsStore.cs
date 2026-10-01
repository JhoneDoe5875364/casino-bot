using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PragmaticBot.Models.Api;

namespace PragmaticBot.Services;

/// <summary>
/// 봇 폴더에 회원별 설정을 json 으로 저장하는 로컬 우선 캐시.
/// (서식 · 베팅금 · 행렬 셀/행규칙 · 예외서식 · 방게이트서식 · 한도 · 방쿨다운)
/// — 원격 서버 왕복 없이 즉시 열고 저장하기 위한 것.
///
/// 서버는 백업/관리자 관리용 단일 원본으로 유지된다. 로그인 시 <see cref="ApiClient"/> 가
/// 서버 값을 당겨 로컬을 덮어쓰므로, 관리자가 서버에서 바꾼 설정은 다음 로그인에 반영된다.
/// 저장은 로컬에 즉시 쓰고 서버로는 반영한다.
///
/// 어떤 이유로든 로컬 접근이 실패하면(권한/디스크/손상) 조용히 null 을 돌려주고,
/// 호출부는 기존처럼 서버에서 받아온다 — 즉 로컬 계층이 깨져도 봇은 예전대로 동작한다.
/// </summary>
public static class LocalSettingsStore
{
    private static readonly object Gate = new object();

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // 봇 실행 폴더\settings\<userId>.json
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "settings");

    private static string FileFor(string userId) => Path.Combine(Dir, userId + ".json");

    private sealed class Bundle
    {
        public List<FormatSettingDto>? Formats { get; set; }
        public Dictionary<int, List<BetAmountSettingDto>>? BetAmounts { get; set; }
        public Dictionary<int, List<MatrixCellDto>>? MatrixCells { get; set; }
        public Dictionary<int, List<MatrixRowRuleDto>>? MatrixRowRules { get; set; }
        public List<ExceptionFormatDto>? ExceptionFormats { get; set; }
        public List<RoomGateFormatDto>? RoomGateFormats { get; set; }
        public LimitSettingDto? Limits { get; set; }
        public RoomCooldownSettingDto? RoomCooldown { get; set; }
        public List<StrategyChainDto>? StrategyChains { get; set; }
    }

    private static Bundle Load(string userId)
    {
        try
        {
            string path = FileFor(userId);
            if (!File.Exists(path))
                return new Bundle();
            return JsonSerializer.Deserialize<Bundle>(File.ReadAllText(path), JsonOptions) ?? new Bundle();
        }
        catch
        {
            return new Bundle();
        }
    }

    private static void Store(string userId, Bundle bundle)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FileFor(userId), JsonSerializer.Serialize(bundle, JsonOptions));
        }
        catch
        {
            // 로컬 저장 실패는 무시한다 — 서버 백업이 진짜 원본이므로 봇 동작에는 지장 없다.
        }
    }

    // ---------- 서식 ----------

    public static List<FormatSettingDto>? GetFormats(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        lock (Gate) return Load(userId).Formats;
    }

    public static void SetFormats(string? userId, List<FormatSettingDto> formats)
    {
        if (string.IsNullOrEmpty(userId)) return;
        lock (Gate) { Bundle b = Load(userId); b.Formats = formats; Store(userId, b); }
    }

    // ---------- 베팅금 (타입별) ----------

    public static List<BetAmountSettingDto>? GetBetAmounts(string? userId, int betAmountType)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        lock (Gate)
        {
            Bundle b = Load(userId);
            if (b.BetAmounts != null && b.BetAmounts.TryGetValue(betAmountType, out List<BetAmountSettingDto>? list)) return list;
            return null;
        }
    }

    public static void SetBetAmounts(string? userId, int betAmountType, List<BetAmountSettingDto> betAmounts)
    {
        if (string.IsNullOrEmpty(userId)) return;
        lock (Gate)
        {
            Bundle b = Load(userId);
            b.BetAmounts ??= new Dictionary<int, List<BetAmountSettingDto>>();
            b.BetAmounts[betAmountType] = betAmounts;
            Store(userId, b);
        }
    }

    // ---------- 행렬 셀 (타입별) ----------

    public static List<MatrixCellDto>? GetMatrixCells(string? userId, int betAmountType)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        lock (Gate)
        {
            Bundle b = Load(userId);
            if (b.MatrixCells != null && b.MatrixCells.TryGetValue(betAmountType, out List<MatrixCellDto>? list)) return list;
            return null;
        }
    }

    public static void SetMatrixCells(string? userId, int betAmountType, List<MatrixCellDto> cells)
    {
        if (string.IsNullOrEmpty(userId)) return;
        lock (Gate)
        {
            Bundle b = Load(userId);
            b.MatrixCells ??= new Dictionary<int, List<MatrixCellDto>>();
            b.MatrixCells[betAmountType] = cells;
            Store(userId, b);
        }
    }

    // ---------- 행렬 행규칙 (타입별) ----------

    public static List<MatrixRowRuleDto>? GetMatrixRowRules(string? userId, int betAmountType)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        lock (Gate)
        {
            Bundle b = Load(userId);
            if (b.MatrixRowRules != null && b.MatrixRowRules.TryGetValue(betAmountType, out List<MatrixRowRuleDto>? list)) return list;
            return null;
        }
    }

    public static void SetMatrixRowRules(string? userId, int betAmountType, List<MatrixRowRuleDto> rules)
    {
        if (string.IsNullOrEmpty(userId)) return;
        lock (Gate)
        {
            Bundle b = Load(userId);
            b.MatrixRowRules ??= new Dictionary<int, List<MatrixRowRuleDto>>();
            b.MatrixRowRules[betAmountType] = rules;
            Store(userId, b);
        }
    }

    // ---------- 예외 서식 ----------

    public static List<ExceptionFormatDto>? GetExceptionFormats(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        lock (Gate) return Load(userId).ExceptionFormats;
    }

    public static void SetExceptionFormats(string? userId, List<ExceptionFormatDto> formats)
    {
        if (string.IsNullOrEmpty(userId)) return;
        lock (Gate) { Bundle b = Load(userId); b.ExceptionFormats = formats; Store(userId, b); }
    }

    // ---------- 방 게이트 서식 ----------

    public static List<RoomGateFormatDto>? GetRoomGateFormats(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        lock (Gate) return Load(userId).RoomGateFormats;
    }

    public static void SetRoomGateFormats(string? userId, List<RoomGateFormatDto> formats)
    {
        if (string.IsNullOrEmpty(userId)) return;
        lock (Gate) { Bundle b = Load(userId); b.RoomGateFormats = formats; Store(userId, b); }
    }

    // ---------- 한도 (단일 객체) ----------

    public static LimitSettingDto? GetLimits(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        lock (Gate) return Load(userId).Limits;
    }

    public static void SetLimits(string? userId, LimitSettingDto limits)
    {
        if (string.IsNullOrEmpty(userId)) return;
        lock (Gate) { Bundle b = Load(userId); b.Limits = limits; Store(userId, b); }
    }

    // ---------- 방 쿨다운 (단일 객체) ----------

    public static RoomCooldownSettingDto? GetRoomCooldown(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        lock (Gate) return Load(userId).RoomCooldown;
    }

    public static void SetRoomCooldown(string? userId, RoomCooldownSettingDto dto)
    {
        if (string.IsNullOrEmpty(userId)) return;
        lock (Gate) { Bundle b = Load(userId); b.RoomCooldown = dto; Store(userId, b); }
    }

    // ---------- 전략 체인 (목록; 활성 체인은 목록에서 IsActive 로 도출) ----------

    public static List<StrategyChainDto>? GetStrategyChains(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        lock (Gate) return Load(userId).StrategyChains;
    }

    public static void SetStrategyChains(string? userId, List<StrategyChainDto> chains)
    {
        if (string.IsNullOrEmpty(userId)) return;
        lock (Gate) { Bundle b = Load(userId); b.StrategyChains = chains; Store(userId, b); }
    }

    /// <summary>체인 저장/활성화/삭제 후 호출 — 다음 읽기가 서버에서 새로 받도록 로컬 캐시를 비운다.</summary>
    public static void InvalidateStrategyChains(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return;
        lock (Gate) { Bundle b = Load(userId); b.StrategyChains = null; Store(userId, b); }
    }
}
