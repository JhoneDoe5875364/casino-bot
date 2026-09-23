using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using PragmaticBot.Models;

namespace PragmaticBot.Services;

public class RoomCooldownService
{
	private readonly ConcurrentDictionary<string, RoomCooldownState> _states = new ConcurrentDictionary<string, RoomCooldownState>();

	private RoomCooldownSettings _settings = RoomCooldownSettings.Disabled();

	private string? _userId;

	public RoomCooldownSettings Settings => _settings;

	public event EventHandler<RoomCooldownChangedEventArgs>? CooldownChanged;

	public void Start(string userId, RoomCooldownSettings settings)
	{
		_userId = userId;
		_settings = settings ?? RoomCooldownSettings.Disabled();
		_states.Clear();
		FileLogger.Log($"[RoomCooldown] 시작 - 활성={_settings.IsEnabled}, 임계값={_settings.LossThreshold}연패, 쿨다운={_settings.CooldownMinutes}분");
	}

	public void Stop()
	{
		_states.Clear();
		_userId = null;
		FileLogger.Log("[RoomCooldown] 중지");
	}

	public CooldownCheck Check(string tableId)
	{
		CooldownCheck result = new CooldownCheck();
		if (!_settings.IsEnabled || string.IsNullOrEmpty(_userId))
		{
			return result;
		}
		if (!_states.TryGetValue(tableId, out RoomCooldownState state))
		{
			return result;
		}
		result.ConsecutiveLossCount = state.ConsecutiveLossCount;
		DateTime? cooldownUntilUtc = state.CooldownUntilUtc;
		if (cooldownUntilUtc.HasValue)
		{
			DateTime until = cooldownUntilUtc.GetValueOrDefault();
			DateTime now = DateTime.UtcNow;
			if (now < until)
			{
				result.IsBlocked = true;
				result.RemainingSeconds = (int)Math.Ceiling((until - now).TotalSeconds);
				return result;
			}
			state.CooldownUntilUtc = null;
			state.ConsecutiveLossCount = 0;
			state.LastUpdatedUtc = now;
			FileLogger.Log("[RoomCooldown] " + tableId + ": 쿨다운 해제 (카운터 리셋)");
			RaiseChanged(tableId, state, isInCooldown: false, 0);
		}
		return result;
	}

	public void OnBetResult(string tableId, bool isWin, bool isTie)
	{
		if (!_settings.IsEnabled || string.IsNullOrEmpty(_userId) || isTie)
		{
			return;
		}
		RoomCooldownState state = _states.GetOrAdd(tableId, (string _) => new RoomCooldownState
		{
			TableId = tableId
		});
		if (isWin)
		{
			if (state.ConsecutiveLossCount > 0)
			{
				FileLogger.Log($"[RoomCooldown] {tableId}: 승리 - 연패 카운터 리셋 ({state.ConsecutiveLossCount} → 0)");
			}
			state.ConsecutiveLossCount = 0;
			state.LastUpdatedUtc = DateTime.UtcNow;
			RaiseChanged(tableId, state, isInCooldown: false, 0);
			return;
		}
		state.ConsecutiveLossCount++;
		state.LastUpdatedUtc = DateTime.UtcNow;
		if (state.ConsecutiveLossCount >= _settings.LossThreshold)
		{
			DateTime until = DateTime.UtcNow.AddMinutes(_settings.CooldownMinutes);
			state.CooldownUntilUtc = until;
			state.CooldownHitCount++;
			FileLogger.Log($"[RoomCooldown] {tableId}: ★ {state.ConsecutiveLossCount}연패 도달 → 쿨다운 발동 ({_settings.CooldownMinutes}분, 해제: {until.ToLocalTime():HH:mm:ss})", "Warning", "RoomCooldown");
			RaiseChanged(tableId, state, isInCooldown: true, _settings.CooldownMinutes * 60);
		}
		else
		{
			FileLogger.Log($"[RoomCooldown] {tableId}: 연패 {state.ConsecutiveLossCount}/{_settings.LossThreshold}");
			RaiseChanged(tableId, state, isInCooldown: false, 0);
		}
	}

	public RoomCooldownState? GetState(string tableId)
	{
		if (!_states.TryGetValue(tableId, out RoomCooldownState s))
		{
			return null;
		}
		return s;
	}

	public IReadOnlyDictionary<string, RoomCooldownState> GetAllStates()
	{
		return _states;
	}

	private void RaiseChanged(string tableId, RoomCooldownState state, bool isInCooldown, int remainingSeconds)
	{
		CooldownChanged?.Invoke(this, new RoomCooldownChangedEventArgs
		{
			TableId = tableId,
			ConsecutiveLossCount = state.ConsecutiveLossCount,
			IsInCooldown = isInCooldown,
			RemainingSeconds = remainingSeconds
		});
	}
}
