using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PragmaticBot.Services;

public interface IPragmaticChromeDevToolsService : IDisposable
{
	bool IsConnected { get; }

	event EventHandler<PragmaticWebSocketMessageEventArgs>? WebSocketMessageReceived;

	event EventHandler<bool>? ConnectionStateChanged;

	event EventHandler<string>? ErrorOccurred;

	event EventHandler<decimal>? BalanceReceived;

	event EventHandler<decimal>? HttpBalanceReceived;

	event EventHandler<string>? BalanceSelectorDetected;

	/// <summary>
	/// Raised with the PP account id once it is seen in a wallet response. MULTIPLAY never sends the
	/// <c>seat</c> message the bot used to learn it from, and betting cannot proceed without it.
	/// </summary>
	event EventHandler<string>? PpUserIdDetected;

	Task ConnectAsync(int port = 9222);

	Task DisconnectAsync();

	void SetCustomBalanceSelectors(List<string> selectors);

	void NotifyHttpBalanceReceived();

	Task<bool> SendBetDirectAsync(string betXml);

	/// <summary>에볼루션 평문 베팅을 CDP로 익스텐션(bet_send.js)에 직접 전달해 소켓 전송시킨다. 릴레이 차단 우회.</summary>
	Task<(bool Ok, string Status)> SendEvolutionBetViaCdpAsync(string tableId, string message);

	Task<bool> ReloadGamePagesAsync();

	Task EnsureNoCommissionOffAsync();

	/// <summary>
	/// Evolution 라이브 바카라(멀티플레이) DOM 자동 베팅. baccarat.multiplay 프레임에서
	/// 지정 칩값을 고르고, 대상 테이블(가상 스크롤이라 필요 시 스크롤로 렌더)의 스팟을 신뢰 클릭한다.
	/// tableId: baccarat.tableState.tableId 와 동일 (DOM의 [data-role=table][data-tableid]로 매칭 — 번역 불필요).
	/// spot: Player/Banker/Tie/PlayerPair/BankerPair (한글 플레이어/뱅커/무 등도 허용).
	/// </summary>
	Task<bool> PlaceEvolutionBetAsync(string tableId, string chipValue, string spot, int units = 1);

	/// <summary>
	/// PlaceEvolutionBetAsync 와 동일하되 성공/실패 사유를 문자열로 반환("OK"=성공, 그 외=실패 사유).
	/// </summary>
	Task<string> PlaceEvolutionBetVerboseAsync(string tableId, string chipValue, string spot, int units = 1);

	/// <summary>
	/// 칩 플랜("50k:1,10k:1")대로 최소 개수 칩 조합을 스팟에 올린다. 최소베팅 미달이면 실패.
	/// 성공 시 "OK", 아니면 실패 사유 문자열.
	/// </summary>
	Task<string> PlaceEvolutionBetPlanAsync(string tableId, string spot, string chipPlan);
}
public class PragmaticWebSocketMessageEventArgs : EventArgs
{
	public string Url { get; set; } = string.Empty;

	public string Direction { get; set; } = "receive";

	public string Data { get; set; } = string.Empty;

	public DateTime Timestamp { get; set; } = DateTime.Now;

	public string? RequestId { get; set; }
}
