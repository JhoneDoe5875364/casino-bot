using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PragmaticBot.Models.Api;

namespace PragmaticBot.Services;

public class RoomGateFormatSettingService
{
	public const int GateTypeOn = 0;

	public const int GateTypeOff = 1;

	public const int MaxNumber = 20;

	private static ApiClient GetApi()
	{
		return AuthService.Api ?? throw new InvalidOperationException("로그인이 필요합니다.");
	}

	public async Task<List<RoomGateFormatDto>> GetRoomGateFormatsAsync(string userId)
	{
		return await GetApi().GetRoomGateFormatsAsync();
	}

	public async Task SaveRoomGateFormatsAsync(string userId, IDictionary<int, string?> onFormats, IDictionary<int, string?> offFormats)
	{
		List<RoomGateFormatDto> dtos = (from f in onFormats.Select<KeyValuePair<int, string>, (int Key, int GateType, string Value)>((KeyValuePair<int, string> kv) => (Key: kv.Key, GateType: 0, Value: kv.Value)).Concat(offFormats.Select<KeyValuePair<int, string>, (int Key, int GateType, string Value)>((KeyValuePair<int, string> kv) => (Key: kv.Key, GateType: 1, Value: kv.Value)))
			where f.Key >= 1 && f.Key <= 20
			select new RoomGateFormatDto(f.Key, f.GateType, f.Value)).ToList();
		await GetApi().SaveRoomGateFormatsAsync(dtos);
	}
}
