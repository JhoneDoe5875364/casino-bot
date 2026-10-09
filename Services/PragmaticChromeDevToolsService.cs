using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;
using Timer = System.Threading.Timer;

namespace PragmaticBot.Services;

public class PragmaticChromeDevToolsService : IPragmaticChromeDevToolsService, IDisposable
{
	private record struct SessionPollResult(string? SessionId, decimal Balance, string? MatchType, string? Tag, string? Cls, bool IsPP, string? Selector);

	private IBrowser? _browser;

	private bool _disposed;

	private readonly List<ICDPSession> _attachedSessions = new List<ICDPSession>();

	private readonly HashSet<string> _attachedTargetIds = new HashSet<string>();

	private readonly Dictionary<string, string> _sessionUrls = new Dictionary<string, string>();

	private readonly ConcurrentDictionary<string, byte> _filteredRequestIds = new ConcurrentDictionary<string, byte>();

	private long _frameCount;

	private long _cdpMessageCount;

	private long _balancePollCount;

	private long _lastBetSentTicksUtc;

	private const int BetDiagnosticWindowSeconds = 10;

	private Timer? _balanceTimer;

	private decimal _lastBalance;

	private Timer? _targetRescanTimer;

	// 암호화 사이트 + 릴레이(ws://127.0.0.1) 차단 환경 대비: 익스텐션이 window.__evoRxQueue 에 쌓은
	// 복호화 평문을 CDP 로 직접 비워 온다. 릴레이에 의존하지 않는 수신 경로.
	private Timer? _evoRxDrainTimer;
	private int _evoRxDraining;
	private long _evoRxDrainTick;

	private int _rescanInFlight;

	private const int TargetRescanPeriodMs = 3000;

	private long _lastHttpBalanceTicksUtc;

	private const int HttpBalanceTimeoutSeconds = 15;

	private string? _balanceSessionId;

	private bool _balanceSelectorSaved;

	private int _consecutiveZeroPollCount;

	private const int ConsecutiveZeroThreshold = 5;

	private bool _walkerFallbackConfirmed;

	private List<string> _customBalanceSelectors = new List<string>();

	private readonly ConcurrentDictionary<string, int> _zeroSelectorBlacklist = new ConcurrentDictionary<string, int>();

	private const int BlacklistThreshold = 3;

	private int _pollInFlight;

	private string? _injectJsContent;

	/// <summary>
	/// Tags every WebSocket with the table ids seen on it, so a bet can be sent on the socket that
	/// actually serves that table. Evolution's multi-table page holds several sockets (lobby/widget,
	/// metrics, game) and only the game socket acts on a bet; the others discard it in silence.
	/// Appended to inject.js so it is present regardless of which extension the account's grant loaded.
	/// </summary>
	private const string TableRouteHookJs = @"
(function(){
  if (window.__botTableRouteHooked) return;
  window.__botTableRouteHooked = true;
  var Native = window.WebSocket;
  if (!Native) return;
  var scan = function(ws, text){
    var i = text.indexOf('""tableId"":""');
    while (i >= 0) {
      var s = i + 11, e = text.indexOf('""', s);
      if (e < 0) break;
      ws.__botTables[text.slice(s, e)] = 1;
      i = text.indexOf('""tableId"":""', e);
    }
  };
  // ---- Evolution app-layer crypto (RC4 path) -------------------------------------------------
  // Evolution wraps its socket in its own cipher when the URL carries ?nonce=&encrypted=true.
  // The key is derived entirely from the URL plus a constant baked into their client, so a bet can
  // be encrypted exactly the way the page would. Plaintext deployments are untouched: no nonce, no
  // cipher, send the string as before.
  var RC4_SALT = '560a7047c677ab04d413e1f19a19fb517a9e53eeea5ae2f265e8d432131b4192';
  var rc4Init = function(key){
    var S = new Uint8Array(256), i, j = 0, t;
    for (i = 0; i < 256; i++) S[i] = i;
    for (i = 0; i < 256; i++) {
      j = (j + S[i] + key[i % key.length]) & 255;
      t = S[i]; S[i] = S[j]; S[j] = t;
    }
    return S;
  };
  var rc4Process = function(state, data){
    // The client restarts the keystream for every message (rc4StateArray.slice()). Carrying the
    // state over instead would corrupt every frame after the first.
    var S = state.slice(), i = 0, j = 0, t, out = new Uint8Array(data.length);
    for (var n = 0; n < data.length; n++) {
      i = (i + 1) & 255; j = (j + S[i]) & 255;
      t = S[i]; S[i] = S[j]; S[j] = t;
      out[n] = data[n] ^ S[(S[i] + S[j]) & 255];
    }
    return out;
  };
  var makeCrypto = function(url){
    try {
      var q = {}, qs = String(url || '').split('?')[1] || '';
      qs.split('&').forEach(function(kv){
        // split on the FIRST '=' only: nonce is base64 and its '==' padding is part of the value.
        // Losing it changes the key and every frame decrypts to noise.
        var eq = kv.indexOf('=');
        if (eq <= 0) return;
        var k = kv.slice(0, eq), v = kv.slice(eq + 1);
        q[decodeURIComponent(k)] = decodeURIComponent(v.replace(/\+/g, ' '));
      });
      if (!q.nonce || !q.instance) return null;       // plaintext socket
      var key = new TextEncoder().encode(RC4_SALT + ':' + q.instance + ':' + q.nonce);
      var state = rc4Init(key);
      return {
        instance: q.instance,
        encrypt: function(text){
          var body = new TextEncoder().encode(text);
          var payload = new Uint8Array(body.length + 1);
          payload[0] = 0;                              // sub-frame 0 = stored raw (no zstd needed)
          payload.set(body, 1);
          var enc = rc4Process(state, payload);
          var frame = new Uint8Array(enc.length + 2);
          frame[0] = 1; frame[1] = 3;                  // version 1, RC4 + compression sub-frame
          frame.set(enc, 2);
          return frame;
        },
        decrypt: function(buf){
          var f = new Uint8Array(buf);
          if (f.length < 2 || f[0] !== 1) return null;
          if (f[1] !== 1 && f[1] !== 3) return null;   // AES-GCM frames are left to the page
          var data = rc4Process(state, f.subarray(2));
          if (f[1] === 3) {
            if (data[0] === 1) return null;            // zstd body: the page decodes it for us
            data = data.subarray(1);
          }
          return new TextDecoder().decode(data);
        }
      };
    } catch (e) { return null; }
  };
  window.__evoMakeCrypto = makeCrypto;

  var watch = function(ws){
    if (!ws || ws.__botTables) return;
    try { ws.__botTables = {}; } catch (e) { return; }
    try { ws.__evoCrypto = makeCrypto(ws.url); } catch (e) {}
    try {
      ws.addEventListener('message', function(ev){
        var d = ev && ev.data;
        if (typeof d === 'string' && d.length < 200000) { scan(ws, d); return; }
        // Encrypted socket: decrypt just enough to keep the table tags up to date, so the bet
        // still goes out on the socket that serves the table.
        if (ws.__evoCrypto && d && d.byteLength && d.byteLength < 200000) {
          try {
            var txt = ws.__evoCrypto.decrypt(d);
            if (txt) scan(ws, txt);
          } catch (e) {}
        }
      });
    } catch (e) {}
  };
  window.__botWatchSocket = watch;
  var Wrapped = function(url, protocols){
    var ws = (protocols === undefined) ? new Native(url) : new Native(url, protocols);
    watch(ws);
    // Registered here as well, so the socket list does not depend on inject.js also being in place.
    try {
      var reg = window.__pragmaticbot_websockets || (window.__pragmaticbot_websockets = []);
      if (reg.indexOf(ws) < 0) reg.push(ws);
    } catch (e) {}
    return ws;
  };
  Wrapped.prototype = Native.prototype;
  Wrapped.CONNECTING = 0; Wrapped.OPEN = 1; Wrapped.CLOSING = 2; Wrapped.CLOSED = 3;
  try { window.WebSocket = Wrapped; } catch (e) {}
  // inject.js also replaces window.WebSocket; whichever wrapper ends up outermost, this sweep picks
  // up every socket the other one registered, so tagging never depends on load order.
  var sweep = function(){
    try {
      var seen = (window.__pragmaticbot_websockets || []).slice();
      if (window.__pragmaticbot_ws) seen.push(window.__pragmaticbot_ws);
      for (var k = 0; k < seen.length; k++) watch(seen[k]);
    } catch (e) {}
  };
  sweep();
  try { setInterval(sweep, 2000); } catch (e) {}
})();
";


	private static readonly string[] DefaultBalanceSelectors = new string[12]
	{
		".balance-value", ".balanceValue", "[data-qa=\"balance\"]", "[data-testid=\"balance\"]", ".user-balance", ".balance-amount", ".lobby-balance-value", ".balance .value", ".headerBalance", ".header-balance",
		"#balance", ".money"
	};

	private static readonly Regex DateLikeTextRegex = new Regex("\\d{4}\\s*[-/.]\\s*\\d{1,2}\\s*[-/.]\\s*\\d{1,2}|\\d{1,2}\\s*:\\s*\\d{2}", RegexOptions.Compiled);

	private const decimal MaxPlausibleDomBalance = 10000000000m;

	private const string NoCommissionOffScript = "(function() {\n    try {\n        if (typeof document === 'undefined') return 'no-document';\n        var i = document.querySelector('input[name=\"noComission\"]');\n        if (!i) return 'absent';\n        if (!i.checked) return 'already-off';\n\n        var label = i.id ? document.querySelector('label[for=\"' + i.id + '\"]') : null;\n        if (label) label.click(); else i.click();\n\n        return i.checked ? 'click-no-effect' : 'turned-off';\n    } catch (e) {\n        return 'error:' + (e && e.message ? e.message : String(e));\n    }\n})()";

	private readonly ConcurrentDictionary<string, string> _pendingBalanceRequests = new ConcurrentDictionary<string, string>();

	private const int MaxPendingBalanceRequests = 64;

	private const string WalletBalanceUrlMarker = "wallet/balance";

	// 베팅 전송용 Runtime.evaluate 타임아웃. 기본 프로토콜 타임아웃(180초)을 쓰면, 죽은/멈춘
	// 실행 컨텍스트 하나가 베팅 창(10~15초)을 통째로 잡아먹어 명령이 브라우저로 나가지 못한다.
	// 살아있는 컨텍스트는 보통 <200ms 에 응답하므로, 짧게 끊고 다음 컨텍스트로 넘어간다.
	private const int BetEvalTimeoutMs = 4000;

	private decimal _lastHttpBalanceValue;

	private static readonly string[] BalanceJsonKeys = new string[12]
	{
		"balance", "credit", "fund", "amount", "money", "wallet", "cash", "coins", "total_balance", "totalbalance",
		"player_balance", "playerbalance"
	};

	private const int WalletFetchIntervalSeconds = 5;

	private string? _walletSessionId;

	private long _lastWalletFetchTicksUtc;

	private string? _walletUrl;

	// 네트워크에서 관찰된 모든 wallet/balance 응답 URL(사이트 구조와 무관하게 후보로 모아둔다).
	// 하나만 고정하면 그게 오답(예: 로더 도메인)일 때 무한 재시도에 빠지고, 해제할 때 진짜 URL까지
	// 버려 다신 못 찾는다. 관찰된 것을 전부 후보로 돌려 '유효한 잔액을 돌려주는' URL을 고른다 → 동적.
	private readonly List<string> _observedWalletUrls = new List<string>();

	private readonly object _walletUrlsLock = new object();

	private void RecordObservedWalletUrl(string url)
	{
		if (string.IsNullOrEmpty(url))
		{
			return;
		}
		lock (_walletUrlsLock)
		{
			_observedWalletUrls.Remove(url);      // 최신을 앞으로
			_observedWalletUrls.Insert(0, url);
			if (_observedWalletUrls.Count > 24)
			{
				_observedWalletUrls.RemoveRange(24, _observedWalletUrls.Count - 24);
			}
		}
	}

	private const string DefaultWalletPath = "/api/wallet/balance";

	private int _walletFetchFailStreak;

	private const int WalletFetchFailStreakBeforeReset = 3;

	private const int HttpBalanceForcePublishSeconds = 30;

	private long _lastHttpBalancePublishTicksUtc;

	public bool IsConnected => _browser?.IsConnected ?? false;

	public event EventHandler<PragmaticWebSocketMessageEventArgs>? WebSocketMessageReceived;

	public event EventHandler<bool>? ConnectionStateChanged;

	public event EventHandler<string>? ErrorOccurred;

	public event EventHandler<decimal>? BalanceReceived;

	public event EventHandler<decimal>? HttpBalanceReceived;

	public event EventHandler<string>? BalanceSelectorDetected;

	public event EventHandler<string>? PpUserIdDetected;

	private string? _lastPpUserId;

	/// <summary>
	/// Reads the PP account id out of a wallet/preferences response body.
	/// <para>
	/// Only the <c>userId</c> field is accepted. The same payload carries a <c>casinoId</c> that looks
	/// almost identical (<c>ppchk…</c>), and the live socket broadcasts other players' ids in its
	/// <c>winners</c> frames — betting under either would place bets on the wrong account.
	/// </para>
	/// </summary>
	private void TryPublishPpUserId(string json)
	{
		string? userId;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(json);
			if (doc.RootElement.ValueKind != JsonValueKind.Object
				|| !doc.RootElement.TryGetProperty("userId", out var idEl)
				|| idEl.ValueKind != JsonValueKind.String)
			{
				return;
			}
			userId = idEl.GetString();
		}
		catch (Exception)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(userId) || string.Equals(userId, _lastPpUserId, StringComparison.Ordinal))
		{
			return;
		}
		_lastPpUserId = userId;
		FileLogger.Log("[CDP][Wallet] PP UserId 감지: " + userId, "Info", "CDP");
		PpUserIdDetected?.Invoke(this, userId);
	}

	private bool IsWithinBetDiagnosticWindow()
	{
		long ticks = Interlocked.Read(in _lastBetSentTicksUtc);
		if (ticks == 0L)
		{
			return false;
		}
		return (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds <= 10.0;
	}

	public void NotifyHttpBalanceReceived()
	{
		Interlocked.Exchange(ref _lastHttpBalanceTicksUtc, DateTime.UtcNow.Ticks);
	}

	private bool IsHttpBalanceActive()
	{
		long lastTicks = Interlocked.Read(in _lastHttpBalanceTicksUtc);
		if (lastTicks == 0L)
		{
			return false;
		}
		return (DateTime.UtcNow - new DateTime(lastTicks, DateTimeKind.Utc)).TotalSeconds < 15.0;
	}

	public void SetCustomBalanceSelectors(List<string> selectors)
	{
		_customBalanceSelectors = selectors ?? new List<string>();
		if (_customBalanceSelectors.Count > 0)
		{
			FileLogger.Log("[CDP] 사이트별 잔액 셀렉터 설정됨: " + string.Join(", ", _customBalanceSelectors));
		}
	}

	private string BuildBalanceQueryJs()
	{
		IEnumerable<string> allSelectors = from s in _customBalanceSelectors.Concat(DefaultBalanceSelectors)
			where !_zeroSelectorBlacklist.ContainsKey(s)
			select s;
		string selectorArrayJs = string.Join(", ", allSelectors.Select((string s) => "'" + s.Replace("'", "\\'") + "'"));
		return "(function() {\n    // 1단계: balance 전용 셀렉터로 정확한 요소 탐색\n    var selectors = [" + selectorArrayJs + "];\n    for (var i = 0; i < selectors.length; i++) {\n        var el = document.querySelector(selectors[i]);\n        if (el && el.textContent.trim()) {\n            return JSON.stringify([{\n                tag: el.tagName, cls: el.className?.toString()?.substring(0, 60),\n                id: el.id, text: el.textContent.trim().substring(0, 40), match: 'selector',\n                selector: selectors[i]\n            }]);\n        }\n    }\n    // 2단계: balance/money/wallet 속성을 가진 leaf 요소 탐색\n    // ★ 날짜·시각 표기 요소 제외 (2026-08-16 실장애):\n    //   '.money_date'(충전 일시)가 'money' 조건에 걸려 \"2026-08-16 04:27\"을 잔액으로 잡았고,\n    //   숫자만 추출되어 202,608,160,427원(2,026억)이 시작금액·보관금액으로 확정됐다.\n    //   클래스/ID 토큰 단위로 비교해 'balance-update' 같은 정상 요소는 살린다.\n    var dateWords = ['date', 'time', 'datetime', 'day', 'clock', 'expire', 'expiry', 'period', 'regdate'];\n    var dateLike = /\\d{4}\\s*[-\\/.]\\s*\\d{1,2}\\s*[-\\/.]\\s*\\d{1,2}|\\d{1,2}\\s*:\\s*\\d{2}/;\n    var all = document.querySelectorAll('*');\n    for (var j = 0; j < all.length; j++) {\n        var e = all[j];\n        var cls = (e.className || '').toString().toLowerCase();\n        var eid = (e.id || '').toLowerCase();\n        var tokens = (cls + ' ' + eid).split(/[\\s_\\-.]+/);\n        var isDateish = false;\n        for (var t = 0; t < tokens.length; t++) {\n            if (dateWords.indexOf(tokens[t]) >= 0) { isDateish = true; break; }\n        }\n        if (isDateish) continue;\n        if ((cls.includes('balance') || eid.includes('balance') ||\n             cls.includes('money') || cls.includes('wallet')) &&\n            e.children.length === 0) {\n            var txt = e.textContent.trim();\n            if (!txt || dateLike.test(txt)) continue;\n            if (/[\\d,]+/.test(txt)) {\n                // 금액 상한 100억 (3단계 walker와 동일한 성격의 방어. 날짜 연결 숫자 차단)\n                var aVal = parseInt(txt.replace(/[^0-9]/g, ''), 10);\n                if (!isFinite(aVal) || aVal > 10000000000) continue;\n                var detectedSelector = eid ? ('#' + eid) : (e.className ? ('.' + e.className.toString().trim().split(/\\s+/)[0]) : e.tagName.toLowerCase());\n                return JSON.stringify([{\n                    tag: e.tagName, cls: cls.substring(0, 60),\n                    id: eid, text: txt.substring(0, 40), match: 'attribute',\n                    selector: detectedSelector\n                }]);\n            }\n        }\n    }\n    // 3단계: 진단용 - 텍스트 노드에서 10,000 이상 숫자만 탐색 (최소 베팅금 제외)\n    // AG_AH 등 PP 게임 ID 요소 제외, 금액 상한 1억원 적용\n    var results = [];\n    var walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);\n    while (walker.nextNode()) {\n        var text = walker.currentNode.textContent.trim();\n        if (text && /\\d{5,}/.test(text.replace(/,/g, ''))) {\n            // ★ 날짜·시각 텍스트 제외: \"2026-08-16\"은 20260816(2,026만)으로 파싱되어\n            //   1억 상한 필터를 그대로 통과하므로 패턴으로 걸러야 한다.\n            if (dateLike.test(text)) continue;\n            var parent = walker.currentNode.parentElement;\n            var pcls = parent?.className?.toString()?.toLowerCase() || '';\n            // PP 게임 ID 요소 제외 (AG_AH 등 게임 ID 클래스)\n            if (pcls.includes('ag_ah') || pcls.includes('ag_')) continue;\n            // 베팅 칩 버튼 제외 (button-cart-price 등 베팅 금액 UI 요소)\n            if (pcls.includes('button-cart') || pcls.includes('chip')\n                || pcls.includes('stake') || pcls.includes('bet-amount')) continue;\n            if (parent?.tagName === 'BUTTON') continue;\n            var numOnly = text.replace(/[^0-9]/g, '');\n            var numVal = parseInt(numOnly, 10);\n            // 금액 상한 1억원 (게임 ID 등 비정상 큰 숫자 차단)\n            if (numVal > 100000000) continue;\n            // 진단: 통화 기호 포함 또는 5자리 이상 숫자 → 부모 체인 5단계 수집\n            var ancestors = [];\n            if (/[₩¥$€]/.test(text) || numVal >= 10000) {\n                var aNode = parent;\n                for (var depth = 0; aNode && depth < 5; depth++) {\n                    var attrs = {};\n                    if (aNode.attributes) {\n                        for (var a = 0; a < aNode.attributes.length; a++) {\n                            var attr = aNode.attributes[a];\n                            if (attr.name.startsWith('data-') || attr.name === 'class' || attr.name === 'id' || attr.name === 'role')\n                                attrs[attr.name] = attr.value.substring(0, 80);\n                        }\n                    }\n                    ancestors.push({ tag: aNode.tagName, attrs: attrs });\n                    aNode = aNode.parentElement;\n                }\n            }\n            results.push({\n                tag: parent?.tagName,\n                cls: parent?.className?.toString()?.substring(0, 60),\n                id: parent?.id,\n                text: text.substring(0, 40),\n                match: 'walker',\n                ancestors: ancestors.length > 0 ? ancestors : undefined\n            });\n        }\n    }\n    return JSON.stringify(results.slice(0, 20));\n})()";
	}

	public async Task ConnectAsync(int port = 9222)
	{
		_ = 2;
		try
		{
			if (IsConnected)
			{
				await DisconnectAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			// 127.0.0.1, not "localhost": Chrome binds the debugging port to IPv4 only, while
			// "localhost" resolves to ::1 first on Windows and the connection is refused.
			// Every other CDP/port check in this app already uses the literal address.
			string browserUrl = $"http://127.0.0.1:{port}";
			_browser = await Puppeteer.ConnectAsync(new ConnectOptions
			{
				BrowserURL = browserUrl,
				DefaultViewport = null
			}).ConfigureAwait(continueOnCapturedContext: false);
			_browser.Disconnected += Browser_Disconnected;
			_browser.TargetCreated += Browser_TargetCreated;
			_browser.TargetDestroyed += Browser_TargetDestroyed;
			ITarget[] targets = _browser.Targets();
			ITarget[] array = targets;
			foreach (ITarget target in array)
			{
				await AttachToTargetAsync(target).ConfigureAwait(continueOnCapturedContext: false);
			}
			_balanceTimer?.Dispose();
			_balanceTimer = new Timer(delegate(object? _)
			{
				_ = PollBalanceAsync();
			}, null, TimeSpan.FromSeconds(1L), TimeSpan.FromSeconds(2L));
			StartTargetRescan();
			StartEvoRxDrain();
			ConnectionStateChanged?.Invoke(this, e: true);
			FileLogger.Log("[CDP] Chrome DevTools 연결 성공 (잔액 폴링 + 타깃 재스캔 시작)");
		}
		catch (Exception ex)
		{
			FileLogger.Log("[CDP] Chrome 연결 실패: " + ex.Message, "Error", "CDP", ex.StackTrace);
			ErrorOccurred?.Invoke(this, "Chrome 연결 실패: " + ex.Message);
			throw;
		}
	}

	public async Task DisconnectAsync()
	{
		_balanceTimer?.Dispose();
		_balanceTimer = null;
		StopTargetRescan();
		if (_browser != null)
		{
			_browser.Disconnected -= Browser_Disconnected;
			_browser.TargetCreated -= Browser_TargetCreated;
			_browser.TargetDestroyed -= Browser_TargetDestroyed;
			ICDPSession[] sessionsToDetach;
			lock (_attachedSessions)
			{
				sessionsToDetach = _attachedSessions.ToArray();
				_attachedSessions.Clear();
				_attachedTargetIds.Clear();
				_sessionUrls.Clear();
			}
			_filteredRequestIds.Clear();
			ICDPSession[] array = sessionsToDetach;
			foreach (ICDPSession session in array)
			{
				try
				{
					await session.DetachAsync().ConfigureAwait(continueOnCapturedContext: false);
				}
				catch
				{
				}
			}
			_balanceSessionId = null;
			_balanceSelectorSaved = false;
			_consecutiveZeroPollCount = 0;
			_walkerFallbackConfirmed = false;
			_zeroSelectorBlacklist.Clear();
			_browser.Disconnect();
			_browser = null;
			FileLogger.Log("[CDP] Chrome DevTools 연결 해제");
		}
		ConnectionStateChanged?.Invoke(this, e: false);
		await Task.CompletedTask;
	}

	private async Task AttachToTargetAsync(ITarget target)
	{
		string targetId = target.Url ?? target.GetHashCode().ToString();
		lock (_attachedSessions)
		{
			if (_attachedTargetIds.Contains(targetId))
			{
				return;
			}
		}
		if (target.Type == TargetType.Browser || target.Type == TargetType.BackgroundPage || (target.Url?.StartsWith("devtools://") ?? false))
		{
			return;
		}
		try
		{
			bool detachDuplicate = false;
			ICDPSession client = await target.CreateCDPSessionAsync().ConfigureAwait(continueOnCapturedContext: false);
			client.MessageReceived += delegate(object? sender, MessageEventArgs e)
			{
				ProcessCdpMessage(client, e.MessageID, e.MessageData.ToString() ?? "");
			};
			await client.SendAsync("Network.enable").ConfigureAwait(continueOnCapturedContext: false);
			// Runtime events tell us every frame's execution context. The PP game socket lives in the
			// /desktop/multibaccarat/ iframe, not the top-level document, and a plain Runtime.evaluate
			// only reaches the default context — which is why bets never found an open socket.
			try
			{
				await client.SendAsync("Runtime.enable").ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception)
			{
			}
			await InjectHookScriptAsync(client, target).ConfigureAwait(continueOnCapturedContext: false);
			lock (_attachedSessions)
			{
				if (!_attachedTargetIds.Add(targetId))
				{
					detachDuplicate = true;
				}
				else
				{
					_attachedSessions.Add(client);
					_sessionUrls[client.Id] = target.Url ?? "";
				}
			}
			if (detachDuplicate)
			{
				try
				{
					await client.DetachAsync().ConfigureAwait(continueOnCapturedContext: false);
					return;
				}
				catch
				{
					return;
				}
			}
			FileLogger.Log($"[CDP] 타겟 연결됨: Type={target.Type}, URL={target.Url}");
		}
		catch (Exception)
		{
		}
	}

	/// <summary>
	/// Execution context ids per attached session, so a bet can be evaluated in every frame of the
	/// page rather than only the default one.
	/// </summary>
	/// <summary>Consecutive bets that reached no socket at all. Reset by the first success.</summary>
	private long _sendFailStreak;

	// 에볼루션 DOM 베팅은 공유된 가상 스크롤 목록 하나를 조작한다. 두 방이 동시에 스크롤하면
	// 서로의 마킹 노드를 파괴하므로(핸들 상실), 한 번에 하나만 돌도록 직렬화한다.
	private readonly SemaphoreSlim _evoDomBetLock = new SemaphoreSlim(1, 1);

	private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, byte>> _sessionContexts = new();

	private ConcurrentDictionary<int, byte> ContextsFor(string sessionId)
	{
		return _sessionContexts.GetOrAdd(sessionId ?? "", _ => new ConcurrentDictionary<int, byte>());
	}

	private void TrackExecutionContext(ICDPSession client, string messageData, bool created)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(messageData);
			JsonElement root = doc.RootElement;
			int id;
			if (created)
			{
				if (!root.TryGetProperty("context", out var ctx) || !ctx.TryGetProperty("id", out var idEl))
				{
					return;
				}
				id = idEl.GetInt32();
				ContextsFor(client.Id)[id] = 0;
			}
			else
			{
				if (!root.TryGetProperty("executionContextId", out var idEl))
				{
					return;
				}
				id = idEl.GetInt32();
				ContextsFor(client.Id).TryRemove(id, out _);
			}
		}
		catch (Exception)
		{
		}
	}

	// bet_send.js(MAIN world)가 window.__evobetSend 로 노출한 전송 함수를 CDP 로 직접 호출한다.
	// 봇→Chrome 방향이라 익스텐션의 ws://127.0.0.1 릴레이를 막는 보안/방화벽과 무관하게 동작한다.
	public async Task<(bool Ok, string Status)> SendEvolutionBetViaCdpAsync(string tableId, string message)
	{
		if (string.IsNullOrEmpty(tableId) || string.IsNullOrEmpty(message))
		{
			return (false, "빈 인자");
		}
		ICDPSession[] sessions;
		lock (_attachedSessions)
		{
			sessions = _attachedSessions.ToArray();
		}
		if (sessions.Length == 0)
		{
			return (false, "CDP 세션 없음");
		}
		string callId = Guid.NewGuid().ToString("N").Substring(0, 10);
		string expr = "(function(){try{return (window.__evobetSend?window.__evobetSend("
			+ JsonSerializer.Serialize(tableId) + "," + JsonSerializer.Serialize(message) + ","
			+ JsonSerializer.Serialize(callId) + "):'no-hook');}catch(e){return 'error:'+(e&&e.message?e.message:e);}})()";
		string lastStatus = "no-socket";
		foreach (ICDPSession session in sessions)
		{
			// null = 기본 컨텍스트, 나머지 = 자식 프레임. 게임 소켓을 가진 프레임만 'sent'를 돌려주고
			// 나머지는 'no-socket'/'no-hook'이라, 하나라도 성공하면 그것으로 끝낸다.
			List<int?> contexts = new List<int?> { null };
			contexts.AddRange(ContextsFor(session.Id).Keys.OrderBy(k => k).Select(k => (int?)k));
			foreach (int? contextId in contexts)
			{
				try
				{
					object payload = contextId.HasValue
						? new { expression = expr, returnByValue = true, contextId = contextId.Value }
						: (object)new { expression = expr, returnByValue = true };
					JsonElement? response = await session.SendAsync("Runtime.evaluate", payload, true, new CommandOptions { Timeout = BetEvalTimeoutMs })
						.ConfigureAwait(continueOnCapturedContext: false);
					if (!response.HasValue || !response.Value.TryGetProperty("result", out var resultEl)
						|| !resultEl.TryGetProperty("value", out var valueEl) || valueEl.ValueKind != JsonValueKind.String)
					{
						continue;
					}
					string status = valueEl.GetString() ?? "";
					if (status.StartsWith("sent"))
					{
						FileLogger.Log("[CDP] 에볼루션 익스텐션 전송(CDP) 성공: " + tableId + ", status=" + status, "Info", DbLogService.SessionCategory);
						return (true, status);
					}
					if (status != "no-hook" && status != "no-socket" && status != "dup-skip")
					{
						lastStatus = status;
					}
				}
				catch (Exception ex)
				{
					if (IsSessionGone(ex))
					{
						RetireSession(session);
						break;
					}
				}
			}
		}
		return (false, lastStatus);
	}

	public async Task<bool> SendBetDirectAsync(string betXml)
	{
		if (string.IsNullOrEmpty(betXml))
		{
			return false;
		}
		Interlocked.Exchange(ref _lastBetSentTicksUtc, DateTime.UtcNow.Ticks);
		Dictionary<string, string> sessionUrls;
		ICDPSession[] sessionsSnapshot;
		lock (_attachedSessions)
		{
			sessionsSnapshot = _attachedSessions.ToArray();
			sessionUrls = new Dictionary<string, string>(_sessionUrls);
		}
		if (sessionsSnapshot.Length == 0)
		{
			FileLogger.Log("[CDP] SendBetDirect 실패 - attached session 없음", "Warning", "CDP");
			return false;
		}
		int totalSessions = sessionsSnapshot.Length;
		// Last, not first: sessions are appended in attach order, so after a page reload the live
		// session sits behind the stale one for the same URL. First() handed every bet to the dead
		// session and threw the working one away.
		sessionsSnapshot = (from s in sessionsSnapshot
			group s by (!sessionUrls.TryGetValue(s.Id, out string value) || string.IsNullOrEmpty(value)) ? ("\0unknown:" + s.Id) : value into g
			select g.Last()).ToArray();
		if (sessionsSnapshot.Length != totalSessions)
		{
			FileLogger.Log($"[CDP] SendBetDirect 세션 중복 제거: {totalSessions} → {sessionsSnapshot.Length} (동일 URL 문서에 중복 attach)");
		}
		string jsXmlLiteral = JsonSerializer.Serialize(betXml);
		// The duplicate guard is stamped only AFTER the frame actually leaves the socket. It used to be
		// stamped up front, so a send that failed with 'no-ws' still poisoned the guard and the retry
		// came back 'dup-skip' — which this method counts as success. That reported bets as placed
		// when nothing had ever been transmitted.
		string betJs = "(function(xml,strict){try{if(typeof window==='undefined')return 'no-window';var g=window.__pragmaticbot_bet_guard||(window.__pragmaticbot_bet_guard={});var now=Date.now();if(g.xml===xml&&now-(g.at||0)<3000)return 'dup-skip';var list=(window.__pragmaticbot_websockets||[]).slice();var cur=window.__pragmaticbot_ws;if(cur&&list.indexOf(cur)<0)list.push(cur);var open=[];for(var i=0;i<list.length;i++){if(list[i]&&list[i].readyState===1)open.push(list[i]);}if(open.length===0)return 'no-ws';var tid='';var m=/\\\"tableId\\\":\\\"([^\\\"]+)\\\"/.exec(xml)||/channel=\\\"table-([^\\\"]+)\\\"/.exec(xml);if(m)tid=m[1];var fire=function(ws,tag){var enc=null;try{if(ws.__evoCrypto)enc=ws.__evoCrypto.encrypt(xml);}catch(e){return 'encrypt-error:'+(e&&e.message?e.message:e);}ws.send(enc||xml);g.xml=xml;g.at=Date.now();return enc?(tag+'+enc'):tag;};for(var j=0;j<open.length;j++){var u=open[j].url||'';if(u.indexOf('pragmaticplaylive')>=0||u.indexOf('multibaccarat')>=0){return fire(open[j],'sent-pp');}}if(tid){for(var k=0;k<open.length;k++){var t=open[k].__botTables;if(t&&t[tid]){return fire(open[k],'sent-table');}}}var why='(ws='+open.length+',tid='+(tid||'?')+',tagged='+(open.filter(function(w){return w.__botTables;}).length)+')';if(strict)return 'no-match'+why;if(cur&&cur.readyState===1){return fire(cur,'sent-current'+why);}return fire(open[0],'sent-fallback'+why);}catch(e){return 'error:'+(e&&e.message?e.message:String(e));}})(" + jsXmlLiteral;
		int successCount = 0;

		// Two passes. The first only fires on a socket that is known to serve this table; the second
		// takes whatever is open. Firing blind on the first context that had any socket at all is how
		// bets went out on the lobby feed — reported as sent, silently discarded by the game server —
		// while the context holding the real game socket was never even tried.
		async Task RunPassAsync(bool strict)
		{
			string expression = betJs + (strict ? ",true)" : ",false)");
			foreach (ICDPSession session in sessionsSnapshot)
			{
				// null = the default context; the rest are the page's other frames. The game socket
				// sits in a child iframe, so the default context alone always reported 'no-ws'.
				List<int?> contexts = new List<int?> { null };
				contexts.AddRange(ContextsFor(session.Id).Keys.OrderBy(k => k).Select(k => (int?)k));

				foreach (int? contextId in contexts)
				{
					if (successCount > 0)
					{
						return; // the frame is out; sending again would place a second bet
					}
					try
					{
						object payload = contextId.HasValue
							? new { expression, returnByValue = true, contextId = contextId.Value }
							: (object)new { expression, returnByValue = true };
						JsonElement? response = await session.SendAsync("Runtime.evaluate", payload, true, new CommandOptions { Timeout = BetEvalTimeoutMs })
							.ConfigureAwait(continueOnCapturedContext: false);
						if (!response.HasValue || !response.Value.TryGetProperty("result", out var resultEl)
							|| !resultEl.TryGetProperty("value", out var valueEl) || valueEl.ValueKind != JsonValueKind.String)
						{
							continue;
						}
						string status = valueEl.GetString() ?? "";
						string where = session.Id + (contextId.HasValue ? $"/ctx{contextId.Value}" : "/default");
						if (status.StartsWith("sent"))
						{
							successCount++;
							FileLogger.Log("[CDP] SendBetDirect 성공: " + where + ", status=" + status, "Info", DbLogService.SessionCategory);
							// An encrypted frame is unreadable on the wire, so the plaintext is recorded
							// here — otherwise there is no way to see what was actually bet.
							if (status.EndsWith("+enc", StringComparison.Ordinal))
							{
								FileLogger.Log("[CDP][송신명령·암호화] " + Truncate(betXml, 1200), "Info", "Betting");
							}
							return;
						}
						if (status == "dup-skip")
						{
							successCount++;
							FileLogger.Log("[CDP] SendBetDirect 중복 차단(이미 전송됨): " + where);
							return;
						}
						if (status != "no-ws" && status != "no-window" && !status.StartsWith("no-match"))
						{
							FileLogger.Log("[CDP] SendBetDirect 결과: " + where + ", status=" + status);
						}
					}
					catch (Exception ex)
					{
						FileLogger.Log("[CDP] SendBetDirect 컨텍스트 실패: " + session.Id
							+ (contextId.HasValue ? $"/ctx{contextId.Value}" : "/default") + ", error=" + ex.Message, "Warning", "CDP");
						// Backstop for a page that went away without a TargetDestroyed we acted on: retiring it
						// here stops the next bet from picking the same corpse.
						if (IsSessionGone(ex))
						{
							RetireSession(session);
							break;
						}
					}
				}
			}
		}

		await RunPassAsync(strict: true).ConfigureAwait(continueOnCapturedContext: false);
		if (successCount == 0)
		{
			FileLogger.Log("[CDP] SendBetDirect: 이 테이블을 담당하는 소켓을 찾지 못해 폴백으로 전송합니다", "Warning", "CDP");
			await RunPassAsync(strict: false).ConfigureAwait(continueOnCapturedContext: false);
		}
		bool num = Volatile.Read(in successCount) > 0;
		if (!num)
		{
			// Silence here is how 247 consecutive failures went unnoticed: nothing said the bets were
			// never leaving the browser.
			long streak = Interlocked.Increment(ref _sendFailStreak);
			FileLogger.Log($"[CDP] SendBetDirect 전체 실패 - 연결된 iframe 중 __pragmaticbot_ws OPEN 없음 (sessions={sessionsSnapshot.Length}, 연속 {streak}회)", "Warning", "CDP");
			if (streak == 3 || streak % 10 == 0)
			{
				FileLogger.Log($"[CDP] ★ 베팅 전송이 {streak}회 연속 실패했습니다 — 명령이 브라우저로 나가지 않고 있습니다. Chrome 연결을 확인하세요.", "Error", "CDP");
			}
		}
		else
		{
			Interlocked.Exchange(ref _sendFailStreak, 0L);
		}
		return num;
	}

	/// <summary>True when the error says the session's page or frame no longer exists.</summary>
	private static bool IsSessionGone(Exception ex)
	{
		string m = ex.Message ?? "";
		return m.Contains("Session closed", StringComparison.OrdinalIgnoreCase)
			|| m.Contains("detachedFromTarget", StringComparison.OrdinalIgnoreCase)
			|| m.Contains("Target closed", StringComparison.OrdinalIgnoreCase)
			// A context that will not answer Runtime.evaluate within the betting timeout is, for our
			// purposes, dead: retire it so the next bet does not keep picking the same frozen frame.
			|| ex is TimeoutException
			|| m.Contains("Timeout of", StringComparison.OrdinalIgnoreCase)
			|| m.Contains("ms exceeded", StringComparison.OrdinalIgnoreCase);
	}

	private void RetireSession(ICDPSession session)
	{
		bool removed;
		lock (_attachedSessions)
		{
			removed = _attachedSessions.Remove(session);
			if (removed && _sessionUrls.TryGetValue(session.Id, out string url))
			{
				_sessionUrls.Remove(session.Id);
				_attachedTargetIds.Remove(url);   // lets the rescan attach to that page again
			}
		}
		if (removed)
		{
			_sessionContexts.TryRemove(session.Id, out _);
			FileLogger.Log("[CDP] 끊어진 세션 제거: " + session.Id, "Warning", "CDP");
		}
	}

	public async Task<bool> ReloadGamePagesAsync()
	{
		Dictionary<string, string> sessionUrls;
		ICDPSession[] sessionsSnapshot;
		lock (_attachedSessions)
		{
			sessionsSnapshot = _attachedSessions.ToArray();
			sessionUrls = new Dictionary<string, string>(_sessionUrls);
		}
		if (sessionsSnapshot.Length == 0)
		{
			FileLogger.Log("[CDP] ReloadGamePages 실패 - attached session 없음", "Warning", "CDP");
			return false;
		}
		sessionsSnapshot = (from s in sessionsSnapshot
			group s by (!sessionUrls.TryGetValue(s.Id, out string value) || string.IsNullOrEmpty(value)) ? (" unknown:" + s.Id) : value into g
			select g.First()).ToArray();
		int reloaded = 0;
		await Task.WhenAll(((IEnumerable<ICDPSession>)sessionsSnapshot).Select((Func<ICDPSession, Task>)async delegate(ICDPSession session)
		{
			try
			{
				await session.SendAsync("Page.reload", new
				{
					ignoreCache = false
				}).ConfigureAwait(continueOnCapturedContext: false);
				Interlocked.Increment(ref reloaded);
			}
			catch (Exception ex)
			{
				FileLogger.Log("[CDP] ReloadGamePages 세션 실패: " + session.Id + ", error=" + ex.Message, "Warning", "CDP");
			}
		})).ConfigureAwait(continueOnCapturedContext: false);
		return Volatile.Read(in reloaded) > 0;
	}

	public async Task EnsureNoCommissionOffAsync()
	{
		ICDPSession[] sessionsSnapshot;
		lock (_attachedSessions)
		{
			sessionsSnapshot = _attachedSessions.ToArray();
		}
		if (sessionsSnapshot.Length == 0)
		{
			return;
		}
		List<string> results = new List<string>();
		ICDPSession[] array = sessionsSnapshot;
		foreach (ICDPSession session in array)
		{
			try
			{
				JsonElement? r = await session.SendAsync("Runtime.evaluate", new
				{
					expression = "(function() {\n    try {\n        if (typeof document === 'undefined') return 'no-document';\n        var i = document.querySelector('input[name=\"noComission\"]');\n        if (!i) return 'absent';\n        if (!i.checked) return 'already-off';\n\n        var label = i.id ? document.querySelector('label[for=\"' + i.id + '\"]') : null;\n        if (label) label.click(); else i.click();\n\n        return i.checked ? 'click-no-effect' : 'turned-off';\n    } catch (e) {\n        return 'error:' + (e && e.message ? e.message : String(e));\n    }\n})()",
					returnByValue = true
				}).ConfigureAwait(continueOnCapturedContext: false);
				if (r.HasValue && r.Value.TryGetProperty("result", out var res) && res.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
				{
					string s = v.GetString();
					if (!string.IsNullOrEmpty(s))
					{
						results.Add(s);
					}
				}
			}
			catch
			{
			}
		}
		int turnedOff = results.Count((string x) => x == "turned-off");
		int failed = results.Count((string x) => x == "click-no-effect" || x.StartsWith("error:", StringComparison.Ordinal));
		if (turnedOff > 0)
		{
			FileLogger.Log($"[CDP] ★ 노커미션 토글을 껐습니다 ({turnedOff}개 프레임) — 커미션 모드로 복귀, 베팅 코드 0/1 사용", "Info", "Betting");
		}
		if (failed <= 0)
		{
			return;
		}
		FileLogger.Log("[CDP] 노커미션 토글 조작이 반영되지 않았습니다 " + $"({failed}건: {string.Join(", ", results.Where(delegate(string x)
		{
			bool flag;
			switch (x)
			{
			case "already-off":
			case "absent":
			case "turned-off":
			case "no-document":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			return !flag;
		}).Distinct())}) " + "— 화면에서 직접 꺼야 커미션 코드가 통과합니다", "Warning", "Betting");
	}

	// ===================== Evolution 멀티플레이 DOM 자동 베팅 (방식 C) =====================
	// 칩: [data-role=revolver-chip-item] (값=텍스트, 리볼버). 스팟: 텍스트(플레이어/뱅커/무/…)로 클릭영역 탐색.
	// 테이블: [data-role=table-name], 목록은 Virtuoso 가상 스크롤([data-role=tablesList])이라
	//         화면 밖 테이블은 DOM에 없다 → 스크롤로 렌더시킨 뒤 신뢰 클릭(ClickAsync).
	// 대상 칩 요소를 마킹(존재 확인). 값 없으면 false.
	private static readonly string MarkChipJs = @"(value) => {
  const chips = Array.from(document.querySelectorAll('[data-role=""revolver-chip-item""]'));
  document.querySelectorAll('[data-evobet-chip]').forEach(e => e.removeAttribute('data-evobet-chip'));
  const t = chips.find(c => (c.textContent||'').replace(/\s/g,'') === value);
  if (!t) return false;
  t.setAttribute('data-evobet-chip','1');
  return true;
}";

	// 사용 가능한 칩 값 목록(진단용)
	private static readonly string ChipListJs = @"() => Array.from(document.querySelectorAll('[data-role=""revolver-chip-item""]')).map(c => (c.textContent||'').replace(/\s/g,'')).join(', ')";

	// 잔고 부족 등 베팅 거절 토스트/툴팁을 감지한다. 감지되면 사유 텍스트, 아니면 빈 문자열.
	// 스카이라인은 칩 선택 시 스팟에 [data-role=tooltip] 로 '잔고가 너무 부족합니다' 를 띄운다.
	private static readonly string BetRejectReasonJs = @"() => {
  const keys = ['잔고','부족','한도','초과','insufficient','limit','maximum','minimum'];
  const nodes = document.querySelectorAll('[data-role=""tooltip""],[data-role=""tooltip-label""],[class*=""toast""],[class*=""Toast""],[class*=""error""],[class*=""Error""]');
  for (const n of nodes) {
    const t = (n.textContent||'').trim();
    if (t && keys.some(k => t.indexOf(k) >= 0)) { return t.slice(0, 60); }
  }
  return '';
}";

	// 테이블 블록 헤더의 최소 베팅액(₩10,000 등)을 원 단위로 반환. 못 찾으면 0.
	private static readonly string GetTableMinJs = @"(tableId) => {
  const block = document.querySelector('[data-role=""table""][data-tableid=""' + tableId + '""]');
  if (!block) return 0;
  const m = (block.textContent||'').match(/₩\s*([\d,]+)/);
  if (!m) return 0;
  const n = parseInt(m[1].replace(/,/g,''), 10);
  return isNaN(n) ? 0 : n;
}";

	// 특정 테이블/스팟에 현재 걸린 금액 문자열 반환("0"=없음, "10k"=베팅됨). 셀 텍스트=라벨+금액.
	private static readonly string GetSpotAmountJs = @"(tableId, spot) => {
  const block = document.querySelector('[data-role=""table""][data-tableid=""' + tableId + '""]');
  if (!block) return null;
  const label = Array.from(block.querySelectorAll('span')).find(s => (s.textContent||'').replace(/\s+/g,' ').trim() === spot);
  if (!label) return null;
  let cell = label;
  for (let i=0;i<4 && cell.parentElement;i++){ cell = cell.parentElement; const r = cell.getBoundingClientRect(); if (r.width>=30 && r.height>=30) break; }
  let txt = (cell.textContent||'').replace(/\s+/g,' ').trim();
  if (txt.indexOf(spot) === 0) txt = txt.slice(spot.length).trim();
  return txt;
}";

	// 부채꼴이 펼쳐졌는지 — 칩들이 흩어져 있으면 open. (접힘: 거의 겹침, 펼침: 넓게 퍼짐)
	private static readonly string IsFanOpenJs = @"() => {
  const chips = Array.from(document.querySelectorAll('[data-role=""revolver-chip-item""]'));
  if (chips.length < 2) return false;
  let minX=1e9,maxX=-1e9,minY=1e9,maxY=-1e9;
  for (const c of chips) { const r=c.getBoundingClientRect(); const x=r.left+r.width/2, y=r.top+r.height/2; if(x<minX)minX=x; if(x>maxX)maxX=x; if(y<minY)minY=y; if(y>maxY)maxY=y; }
  return ((maxX-minX)+(maxY-minY)) > 60;
}";



	// 가상 스크롤 목록에서 대상 테이블을 "순서 인덱스 x 블록높이"로 한 번에 화면에 올린다.
	// 기존 순차 스캔(최대 40회)을 1회 점프로 대체한다. 블록 높이는 목록에서 실측한 값을 쓴다.
	// 반환: 'ready'(대상이 화면에 옴) | 'noscroller' | 'notfound'
	// 클릭 직전 스팟 재확정: 대상 테이블 블록을 화면에 스크롤로 고정하고, 스팟 셀을 다시 마킹한다.
	// 반환: 'marked' | 'closed'(베팅 마감) | 'notable'(블록 없음) | 'nospot'
	private static readonly string ReMarkSpotJs = @"(tableId, spot) => {
  const block = document.querySelector('[data-role=""table""][data-tableid=""' + tableId + '""]');
  if (!block) return 'notable';
  // 베팅 마감이면 클릭해도 무의미 → 조기 실패
  const closed = block.querySelector('[class*=""closed""],[class*=""Closed""],[data-betting=""BetsClosed""]');
  const txt = (block.textContent || '');
  if (txt.indexOf('BetsClosed') >= 0) return 'closed';
  block.scrollIntoView({ block: 'center' });
  const span = Array.from(block.querySelectorAll('span')).find(s => (s.textContent||'').replace(/\s+/g,' ').trim() === spot);
  if (!span) return 'nospot';
  let cell = span;
  for (let i=0;i<4 && cell.parentElement;i++){ cell = cell.parentElement; const r = cell.getBoundingClientRect(); if (r.width>=30 && r.height>=30) break; }
  document.querySelectorAll('[data-evobet-spot]').forEach(e => e.removeAttribute('data-evobet-spot'));
  cell.setAttribute('data-evobet-spot','1');
  return 'marked';
}";

	// scrollIntoView 없는 재마킹: 락으로 방을 독점 중일 때 2번째 이후 클릭에 쓴다(스크롤 미이동).
	private static readonly string ReMarkSpotNoScrollJs = @"(tableId, spot) => {
  const block = document.querySelector('[data-role=""table""][data-tableid=""' + tableId + '""]');
  if (!block) return 'notable';
  if ((block.textContent || '').indexOf('BetsClosed') >= 0) return 'closed';
  const span = Array.from(block.querySelectorAll('span')).find(s => (s.textContent||'').replace(/\s+/g,' ').trim() === spot);
  if (!span) return 'nospot';
  let cell = span;
  for (let i=0;i<4 && cell.parentElement;i++){ cell = cell.parentElement; const r = cell.getBoundingClientRect(); if (r.width>=30 && r.height>=30) break; }
  document.querySelectorAll('[data-evobet-spot]').forEach(e => e.removeAttribute('data-evobet-spot'));
  cell.setAttribute('data-evobet-spot','1');
  return 'marked';
}";

	private static readonly string JumpToTableJs = @"(async (tableId) => {
  const sc = document.querySelector('[data-role=""tablesList""]');
  if (!sc) return 'noscroller';
  const blocks0 = document.querySelectorAll('[data-role=""table""]');
  const unit = (blocks0[0] ? blocks0[0].offsetHeight : 188) || 188;
  // 캐시는 localStorage 에 둔다: 같은 목록이 여러 프레임에 렌더되므로 window 변수로는 공유되지 않아
  // 프레임마다 지도를 다시 만드는 문제가 있었다(마킹 8초). origin 공유 저장소로 한 번만 만든다.
  const CKEY = 'evoTableOrder_v1';
  let cache = null;
  try { const raw = localStorage.getItem(CKEY); if (raw) cache = JSON.parse(raw); } catch (e) {}
  if (!cache || !cache.order || cache.unit !== unit) {
    const seen = {}; const order = [];
    sc.scrollTop = 0; await new Promise(r => setTimeout(r, 70));
    for (let step = 0; step < 90; step++) {
      const arr = Array.from(document.querySelectorAll('[data-role=""table""]'))
        .map(b => ({ t: b.getAttribute('data-tableid'), o: b.offsetTop }))
        .sort((a, b) => a.o - b.o);
      for (const x of arr) { if (x.t && !(x.t in seen)) { seen[x.t] = 1; order.push(x.t); } }
      if (sc.scrollTop + sc.clientHeight >= sc.scrollHeight - 2) break;
      sc.scrollTop = Math.min(sc.scrollHeight, sc.scrollTop + Math.floor(sc.clientHeight * 0.9));
      await new Promise(r => setTimeout(r, 55));
    }
    cache = { order, unit, at: Date.now() };
    try { localStorage.setItem(CKEY, JSON.stringify(cache)); } catch (e) {}
  }
  const idx = cache.order.indexOf(tableId);
  if (idx < 0) return 'notfound';  // 목록에 없는 방(순서 유지, 재구축 안 함)
  const targetTop = idx * cache.unit;
  sc.scrollTop = Math.max(0, targetTop - Math.floor(sc.clientHeight / 2) + cache.unit / 2);
  for (let k = 0; k < 12; k++) {
    await new Promise(r => setTimeout(r, 40));
    if (document.querySelector('[data-role=""table""][data-tableid=""' + tableId + '""]')) return 'ready';
  }
  // 점프가 빗나가도 캐시는 버리지 않는다(순서 불변). 근처를 미세 조정으로 재시도.
  for (let pass = -1; pass <= 1; pass += 2) {
    sc.scrollTop = Math.max(0, targetTop - Math.floor(sc.clientHeight / 2) + cache.unit / 2 + pass * sc.clientHeight * 0.5);
    for (let k = 0; k < 6; k++) {
      await new Promise(r => setTimeout(r, 40));
      if (document.querySelector('[data-role=""table""][data-tableid=""' + tableId + '""]')) return 'ready';
    }
  }
  return 'notfound';
})";

	private static readonly string LocateSpotJs = @"(tableId, spot) => {
  const scroller = document.querySelector('[data-role=""tablesList""]');
  const block = document.querySelector('[data-role=""table""][data-tableid=""' + tableId + '""]');
  if (block) {
    const span = Array.from(block.querySelectorAll('span')).find(s => (s.textContent||'').replace(/\s+/g,' ').trim() === spot);
    if (!span) return 'nospot';
    let cell = span;
    for (let i=0;i<4 && cell.parentElement;i++){ cell = cell.parentElement; const r = cell.getBoundingClientRect(); if (r.width>=30 && r.height>=30) break; }
    document.querySelectorAll('[data-evobet-spot]').forEach(e => e.removeAttribute('data-evobet-spot'));
    cell.setAttribute('data-evobet-spot','1');
    return 'marked';
  } else {
    if (!scroller) return 'noscroller';
    const before = scroller.scrollTop;
    scroller.scrollTop = Math.min(scroller.scrollHeight, before + Math.floor(scroller.clientHeight*0.8));
    return (scroller.scrollTop <= before + 1) ? 'bottom' : 'searching';
  }
}";

	private static readonly string CursorOverlayJs = @"(function(){
  if (window.__evoCursor) return;
  window.__evoCursor = true;
  var c = document.createElement('canvas'); c.width=12; c.height=12;
  var x = c.getContext('2d'); x.beginPath(); x.arc(6,6,6,0,2*Math.PI,false); x.fillStyle='red'; x.fill();
  c.style.position='fixed'; c.style.zIndex=2147483647; c.style.pointerEvents='none'; c.style.left='-20px'; c.style.top='-20px';
  document.documentElement.appendChild(c);
  document.addEventListener('mousemove', function(e){ c.style.left=(e.clientX-6)+'px'; c.style.top=(e.clientY-6)+'px'; }, true);
})();";

	private static string MapSpotToKorean(string spot)
	{
		switch ((spot ?? string.Empty).Trim().ToLowerInvariant())
		{
		case "player":
		case "p":
		case "플레이어":
			return "플레이어";
		case "banker":
		case "b":
		case "뱅커":
			return "뱅커";
		case "tie":
		case "t":
		case "무":
		case "타이":
			return "무";
		case "playerpair":
		case "ppair":
		case "플레이어페어":
			return "플레이어 페어";
		case "bankerpair":
		case "bpair":
		case "뱅커페어":
			return "뱅커 페어";
		default:
			return spot ?? string.Empty;
		}
	}

	// multiplay 베팅 프레임을 URL(해시 라우팅이라 frame.Url에 안 잡힐 수 있음)이 아니라
	// DOM 존재(칩 트레이/테이블 목록)로 판별한다 — 가장 확실.
	private async Task<IFrame?> GetMultiplayFrameAsync()
	{
		IBrowser? browser = _browser;
		if (browser == null)
		{
			return null;
		}
		try
		{
			IPage[] pages = await browser.PagesAsync().ConfigureAwait(continueOnCapturedContext: false);
			foreach (IPage page in pages)
			{
				foreach (IFrame frame in page.Frames)
				{
					// detached 프레임(슈 교체·재접속으로 교체된 옛 게임 iframe)은 evaluate가
					// 프로토콜 타임아웃(180초)까지 매달리므로 아예 건드리지 않는다.
					if (frame.Detached)
					{
						continue;
					}
					try
					{
						// 살아있어도 응답이 멈춘 프레임이 검색 전체를 붙잡지 못하도록 2초 상한을 둔다.
						bool isBettingFrame = await WithTimeout(
							frame.EvaluateFunctionAsync<bool>(
								"() => !!document.querySelector('[data-role=\"tablesList\"]') || !!document.querySelector('[data-role=\"revolver-chip-item\"]')"),
							2000, false).ConfigureAwait(continueOnCapturedContext: false);
						if (isBettingFrame && !frame.Detached)
						{
							return frame;
						}
					}
					catch
					{
						// cross-origin/detached 프레임 — 무시하고 다음 프레임
					}
				}
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[CDP] multiplay 프레임 조회 실패: " + ex.Message, "Warning", "Betting");
		}
		return null;
	}

	/// <summary>task 가 ms 안에 끝나면 그 결과를, 아니면 onTimeout 을 돌려준다.
	/// 멈춘(detached) 프레임의 CDP 호출이 베팅 락을 오래 물지 않도록 하는 안전장치.</summary>
	private static async Task<T> WithTimeout<T>(Task<T> task, int ms, T onTimeout)
	{
		Task finished = await Task.WhenAny(task, Task.Delay(ms)).ConfigureAwait(continueOnCapturedContext: false);
		if (finished == task)
		{
			return await task.ConfigureAwait(continueOnCapturedContext: false);
		}
		// task 는 백그라운드에서 자체 타임아웃까지 계속 돌다 끝난다. 미관측 예외만 흡수한다.
		_ = task.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
		return onTimeout;
	}

	/// <summary>최근 프레임-이상 리로드 시각(UTC ticks). 0 = 없음. 30초에 한 번만 리로드(폭주 방지).</summary>
	private long _lastFrameReloadTicksUtc;

	/// <summary>멀티방 게임 프레임이 죽은 것으로 보일 때(베팅이 오래 매달림) 게임 페이지 리로드
	/// 복구를 throttle 걸어 요청한다. 죽은 프레임 하나가 7시간 전체 베팅을 마비시키던 문제 대응.</summary>
	private void RequestMultiplayFrameReload(string reason)
	{
		long nowTicks = DateTime.UtcNow.Ticks;
		long prev = Interlocked.Read(ref _lastFrameReloadTicksUtc);
		if (prev != 0L && (new DateTime(nowTicks, DateTimeKind.Utc) - new DateTime(prev, DateTimeKind.Utc)).TotalSeconds < 30.0)
		{
			return; // 최근에 이미 리로드함 — 중복 폭주 방지
		}
		Interlocked.Exchange(ref _lastFrameReloadTicksUtc, nowTicks);
		FileLogger.Log($"[CDP] ★ 멀티방 프레임 이상 감지({reason}) → 게임 페이지 리로드 복구 시도", "Warning", "Betting");
		_ = Task.Run(async () =>
		{
			try { await ReloadGamePagesAsync().ConfigureAwait(continueOnCapturedContext: false); }
			catch (Exception ex) { FileLogger.Log("[CDP] 프레임 이상 리로드 복구 실패: " + ex.Message, "Warning", "Betting"); }
		});
	}

	private static decimal ChipToWonCdp(string chip)
	{
		switch ((chip ?? string.Empty).Trim().ToLowerInvariant())
		{
		case "2000": return 2000m;
		case "10k": return 10000m;
		case "50k": return 50000m;
		case "100k": return 100000m;
		case "200k": return 200000m;
		case "1m": return 1000000m;
		default:
			return (decimal.TryParse((chip ?? string.Empty).Replace(",", string.Empty), out var v) && v > 0m) ? v : 0m;
		}
	}

	// 대상 칩을 활성화한다. 이 사이트(스카이라인)는 리볼버 칩이 항상 부채꼴로 펼쳐져 있어
	// "부채꼴이 닫혔는가"로 성공을 판정하면 영원히 실패한다(=기존 버그, 방당 7.7초 낭비+오탐).
	// 실제 베팅 성사는 상위 흐름의 "스팟 금액 변화"로 검증하므로, 여기서는 칩을 신뢰 클릭하는 것까지만 책임진다.
	// 접힌 리볼버(다른 사이트)도 지원: 칩이 겹쳐 있으면 한 번 눌러 펼친 뒤 대상 칩을 클릭한다.
	private async Task<bool> SelectChipAsync(IFrame frame, string chipValue)
	{
		bool exists = await frame.EvaluateFunctionAsync<bool>(MarkChipJs, chipValue).ConfigureAwait(continueOnCapturedContext: false);
		if (!exists)
		{
			return false;
		}
		for (int attempt = 0; attempt < 3; attempt++)
		{
			// 접힌 리볼버면 먼저 펼친다. 이미 펼쳐져 있으면(이 사이트) 이 블록은 건너뛴다.
			bool fanOpen = await frame.EvaluateFunctionAsync<bool>(IsFanOpenJs).ConfigureAwait(continueOnCapturedContext: false);
			if (!fanOpen)
			{
				IElementHandle? openEl = await frame.QuerySelectorAsync("[data-evobet-chip='1']").ConfigureAwait(continueOnCapturedContext: false);
				if (openEl != null)
				{
					try { await openEl.ClickAsync().ConfigureAwait(continueOnCapturedContext: false); } catch { }
				}
				for (int w = 0; w < 8 && !fanOpen; w++)
				{
					await Task.Delay(120).ConfigureAwait(continueOnCapturedContext: false);
					fanOpen = await frame.EvaluateFunctionAsync<bool>(IsFanOpenJs).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (!fanOpen)
				{
					// 펼치지 못했으면 다시 시도.
					continue;
				}
			}
			// 대상 칩을 다시 마킹 후 신뢰 클릭.
			await frame.EvaluateFunctionAsync<bool>(MarkChipJs, chipValue).ConfigureAwait(continueOnCapturedContext: false);
			IElementHandle? chipEl = await frame.QuerySelectorAsync("[data-evobet-chip='1']").ConfigureAwait(continueOnCapturedContext: false);
			if (chipEl == null)
			{
				continue;
			}
			try { await chipEl.ClickAsync().ConfigureAwait(continueOnCapturedContext: false); }
			catch
			{
				try { await chipEl.EvaluateFunctionAsync("el => el.click()").ConfigureAwait(continueOnCapturedContext: false); }
				catch { }
			}
			// 선택 반영 대기(짧게). 실제 성사 검증은 상위(스팟 금액 변화)에서 한다.
			await Task.Delay(150).ConfigureAwait(continueOnCapturedContext: false);
			return true;
		}
		return false;
	}

	public async Task<bool> PlaceEvolutionBetAsync(string tableId, string chipValue, string spot, int units = 1)
	{
		string status = await PlaceEvolutionBetPlanAsync(tableId, spot, chipValue + ":" + Math.Max(1, units)).ConfigureAwait(continueOnCapturedContext: false);
		return status == "OK";
	}

	// 단일 칩×units 편의 래퍼(테스트 트리거용). 내부는 플랜 방식과 동일.
	public Task<string> PlaceEvolutionBetVerboseAsync(string tableId, string chipValue, string spot, int units = 1)
		=> PlaceEvolutionBetPlanAsync(tableId, spot, chipValue + ":" + Math.Max(1, units));

	// 칩 플랜("50k:1,10k:1")대로 최소 개수 칩 조합을 스팟에 올린다.
	// 최소베팅 미달이면 클릭 전 실패 반환. 성공 시 "OK".
	public async Task<string> PlaceEvolutionBetPlanAsync(string tableId, string spot, string chipPlan)
	{
		// 공유 스크롤 목록 경합 방지: 베팅 1건씩 직렬 처리. 트리거를 마감 10초 전으로 당겼으므로
		// 락 대기 상한은 5초로 둔다(그 이상 기다리면 이 방은 이미 마감 임박이라 시도해도 무의미).
		var swLock = System.Diagnostics.Stopwatch.StartNew();
		if (!await _evoDomBetLock.WaitAsync(5000).ConfigureAwait(continueOnCapturedContext: false))
		{
			return "실패: 다른 방 베팅 처리 대기 초과(5초)";
		}
		long lockWaitMs = swLock.ElapsedMilliseconds;
		try
		{
			// 코어가 죽은(detached) 프레임에서 evaluate에 매달리면 최대 180초까지 락을 물어,
			// 나머지 방이 전부 "대기 초과(5초)"로 무너진다(7시간 전체 마비의 근본 원인).
			// 12초 상한을 둬 락을 즉시 풀고, 프레임 이상이면 게임 페이지 리로드 복구를 요청한다.
			const int betDeadlineMs = 12000;
			Task<string> coreTask = PlaceEvolutionBetPlanCoreAsync(tableId, spot, chipPlan);
			Task finished = await Task.WhenAny(coreTask, Task.Delay(betDeadlineMs)).ConfigureAwait(continueOnCapturedContext: false);
			string r;
			if (finished == coreTask)
			{
				r = await coreTask.ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				_ = coreTask.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
				RequestMultiplayFrameReload($"베팅 처리 {betDeadlineMs / 1000}초 초과(table={tableId})");
				r = "실패: 베팅 처리 시간 초과 — 게임 프레임 이상(리로드 복구)";
			}
			if (lockWaitMs > 500)
			{
				FileLogger.Log($"[CDP] 락 대기 {lockWaitMs}ms 후 처리: table={tableId}, 결과={r}", "Info", "Betting");
			}
			return r;
		}
		finally
		{
			_evoDomBetLock.Release();
		}
	}

	private async Task<string> PlaceEvolutionBetPlanCoreAsync(string tableId, string spot, string chipPlan)
	{
		// 플랜 파싱: "칩:개수,칩:개수"
		var plan = new List<(string Chip, int Count)>();
		decimal expectedWon = 0m;
		foreach (string part in (chipPlan ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
		{
			string[] kv = part.Split(':');
			if (kv.Length != 2) { continue; }
			string chip = kv[0].Trim();
			if (!int.TryParse(kv[1].Trim(), out int cnt) || cnt <= 0) { continue; }
			plan.Add((chip, cnt));
			expectedWon += ChipToWonCdp(chip) * cnt;
		}
		if (plan.Count == 0)
		{
			return "실패: 칩 플랜이 비었음";
		}
		string spotKo = MapSpotToKorean(spot);
		IFrame? frame = await GetMultiplayFrameAsync().ConfigureAwait(continueOnCapturedContext: false);
		if (frame == null)
		{
			FileLogger.Log("[CDP] Evolution multiplay 프레임 없음 — 베팅 불가", "Warning", "Betting");
			return "실패: multiplay 베팅 프레임을 못 찾음 (멀티방 로드/CDP 연결 확인)";
		}
		var __sw = System.Diagnostics.Stopwatch.StartNew();
		long __tLocate = 0, __tMark = 0, __tClick = 0;
		try
		{
			try { await frame.EvaluateExpressionAsync(CursorOverlayJs).ConfigureAwait(continueOnCapturedContext: false); }
			catch { }

			// 1) 대상 테이블 탐색
			// 0) 먼저 현재 화면 확인 — 이미 보이면 스크롤 없이 즉시 마킹(가장 빠름, 방향전환 없어 안전)
			string status = await frame.EvaluateFunctionAsync<string>(LocateSpotJs, tableId, spotKo).ConfigureAwait(continueOnCapturedContext: false);
			if (status == "nospot") { return $"실패: 테이블 '{tableId}'에서 스팟 '{spotKo}' 없음"; }
			if (status == "noscroller") { return "실패: tablesList 스크롤러 없음 (레이아웃 확인)"; }
			if (status != "marked")
			{
				// 0.5) 인덱스 점프: 지도(캐시)로 대상 행 위치를 계산해 한 번에 스크롤. 실측 1회 170ms.
				//      점프가 방을 화면에 올렸으면 스팟 마킹까지 재시도로 확실히 끝낸다(순차 스캔 회피).
				string jump = "?";
				try
				{
					jump = await frame.EvaluateFunctionAsync<string>(JumpToTableJs, tableId).ConfigureAwait(continueOnCapturedContext: false);
					if (jump == "ready")
					{
						// 점프 직후 렌더가 한 박자 늦을 수 있어, 마킹을 짧게 재시도한다.
						for (int m = 0; m < 6 && status != "marked"; m++)
						{
							status = await frame.EvaluateFunctionAsync<string>(LocateSpotJs, tableId, spotKo).ConfigureAwait(continueOnCapturedContext: false);
							if (status == "marked" || status == "nospot" || status == "noscroller") { break; }
							await Task.Delay(40).ConfigureAwait(continueOnCapturedContext: false);
						}
					}
				}
				catch (Exception exJump)
				{
					FileLogger.Log("[CDP] 인덱스 점프 예외(순차 스캔으로 대체): " + exJump.Message, "Info", "Betting");
				}
				FileLogger.Log($"[CDP] 점프 결과: table={tableId}, jump={jump}, mark후status={status}", "Info", "Betting");
			}
			if (status != "marked")
			{
				// 안 보이면 검증된 방식(맨 위 리셋 → 아래로 스캔)으로, 대기 100ms로 ~2배 빠르게. 스텝 0.8 유지(겹침).
				try { await frame.EvaluateExpressionAsync(@"(function(){var s=document.querySelector('[data-role=""tablesList""]'); if(s) s.scrollTop=0;})()").ConfigureAwait(continueOnCapturedContext: false); }
				catch { }
				await Task.Delay(100).ConfigureAwait(continueOnCapturedContext: false);
				for (int i = 0; i < 40; i++)
				{
					status = await frame.EvaluateFunctionAsync<string>(LocateSpotJs, tableId, spotKo).ConfigureAwait(continueOnCapturedContext: false);
					if (status == "marked") { break; }
					if (status == "nospot") { return $"실패: 테이블 '{tableId}'에서 스팟 '{spotKo}' 없음"; }
					if (status == "noscroller") { return "실패: tablesList 스크롤러 없음 (레이아웃 확인)"; }
					if (status == "bottom")
					{
						status = await frame.EvaluateFunctionAsync<string>(LocateSpotJs, tableId, spotKo).ConfigureAwait(continueOnCapturedContext: false);
						if (status == "marked") { break; }
						return $"실패: 테이블 '{tableId}' 목록에 없음(스크롤 바닥)";
					}
					await Task.Delay(100).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			if (status != "marked")
			{
				return $"실패: 테이블 '{tableId}' 탐색 타임아웃";
			}

			__tMark = __sw.ElapsedMilliseconds;

			// 2) 최소 베팅 체크 (서버 거부 사전 차단)
			int tableMin = 0;
			try { tableMin = await frame.EvaluateFunctionAsync<int>(GetTableMinJs, tableId).ConfigureAwait(continueOnCapturedContext: false); }
			catch { }
			if (tableMin > 0 && expectedWon < tableMin)
			{
				FileLogger.Log($"[CDP] 최소베팅 미달: table={tableId}, 시도={expectedWon:N0}, min={tableMin:N0}", "Warning", "Betting");
				return $"실패: 최소베팅 미달 (min=₩{tableMin:N0}, 시도=₩{expectedWon:N0})";
			}

			IElementHandle? spotEl = await frame.QuerySelectorAsync("[data-evobet-spot='1']").ConfigureAwait(continueOnCapturedContext: false);
			if (spotEl == null)
			{
				return "실패: 스팟 요소 핸들 획득 실패";
			}
			string beforeAmt = await frame.EvaluateFunctionAsync<string>(GetSpotAmountJs, tableId, spotKo).ConfigureAwait(continueOnCapturedContext: false) ?? string.Empty;

			// 3) 플랜대로: 칩 선택 → 스팟 count회 클릭 (최소 개수 조합)
			foreach ((string chip, int count) in plan)
			{
				bool chipOk = await SelectChipAsync(frame, chip).ConfigureAwait(continueOnCapturedContext: false);
				if (!chipOk)
				{
					string avail = string.Empty;
					try { avail = await frame.EvaluateFunctionAsync<string>(ChipListJs).ConfigureAwait(continueOnCapturedContext: false); }
					catch { }
					return $"실패: 칩 '{chip}' 선택 실패 (가용=[{avail}])";
				}
				for (int u = 0; u < count; u++)
				{
					// 첫 클릭에서만 스팟을 화면에 고정(scrollIntoView)한다. 락으로 이 방이 독점 중이라
					// 이후엔 스크롤이 움직이지 않으므로, 재스크롤 없이 마킹된 요소를 다시 잡아 클릭한다.
					//   - 재스크롤을 매번 하던 것이 경합·지연의 원인이었다.
					string reMark = await frame.EvaluateFunctionAsync<string>(u == 0 ? ReMarkSpotJs : ReMarkSpotNoScrollJs, tableId, spotKo).ConfigureAwait(continueOnCapturedContext: false);
					if (reMark == "closed") { return "실패: 베팅 마감(BetsClosed) — 클릭 중단"; }
					if (reMark != "marked") { return "실패: 스팟 재확정 실패 (" + reMark + ")"; }
					spotEl = await frame.QuerySelectorAsync("[data-evobet-spot='1']").ConfigureAwait(continueOnCapturedContext: false);
					if (spotEl == null) { return "실패: 스팟 핸들 상실(재확정 후)"; }
					await spotEl.ClickAsync().ConfigureAwait(continueOnCapturedContext: false);
					await Task.Delay(50).ConfigureAwait(continueOnCapturedContext: false);
				}
			}

			__tClick = __sw.ElapsedMilliseconds;

			// 4) 사후 검증 + 부족분 보충 + 실제 금액 확정.
			//    칩이 서버에 순차 확인되며 스팟 금액이 점점 커지므로, 안정화될 때까지 기다려 최종 최댓값을
			//    읽는다. 그런데 클릭 일부가 누락되면 목표(expectedWon)에 못 미친 채 끝난다(예: 40,000
			//    목표에 30,000). 그래서 목표 미달 + 베팅이 아직 열려 있으면(시간 남음) 부족분만큼 칩을
			//    더 올려 목표를 채운다.
			decimal beforeVal = ParseSpotAmount(beforeAmt);
			decimal targetTotal = beforeVal + expectedWon;
			decimal maxVal = await StabilizeSpotAmountAsync(frame, tableId, spotKo, beforeVal).ConfigureAwait(continueOnCapturedContext: false);

			for (int topUp = 0; topUp < 12 && maxVal + 1m < targetTotal; topUp++)
			{
				// 베팅이 마감됐거나 스팟을 못 잡으면 더 올릴 수 없다.
				string reMarkTu = await frame.EvaluateFunctionAsync<string>(ReMarkSpotNoScrollJs, tableId, spotKo).ConfigureAwait(continueOnCapturedContext: false);
				if (reMarkTu == "closed" || reMarkTu != "marked") { break; }
				decimal shortfall = targetTotal - maxVal;
				string? chipTu = LargestChipTokenForAmount(shortfall);
				if (chipTu == null) { break; }   // 부족분이 최소 칩(2000)보다 작음 → 더 못 채움
				if (!await SelectChipAsync(frame, chipTu).ConfigureAwait(continueOnCapturedContext: false)) { break; }
				IElementHandle? spotTu = await frame.QuerySelectorAsync("[data-evobet-spot='1']").ConfigureAwait(continueOnCapturedContext: false);
				if (spotTu == null) { break; }
				try { await spotTu.ClickAsync().ConfigureAwait(continueOnCapturedContext: false); }
				catch { break; }
				FileLogger.Log($"[CDP] 부족분 보충: table='{tableId}', spot={spotKo}, 현재 ₩{(maxVal - beforeVal):N0}/목표 ₩{expectedWon:N0} → 칩 '{chipTu}' 추가", "Info", "Betting");
				maxVal = await StabilizeSpotAmountAsync(frame, tableId, spotKo, maxVal).ConfigureAwait(continueOnCapturedContext: false);
			}

			if (!(maxVal > beforeVal))
			{
				// 거절 사유(잔고 부족/한도 등) 툴팁이 떴는지 확인해 정확한 실패 메시지를 남긴다.
				string reject = string.Empty;
				try { reject = await frame.EvaluateFunctionAsync<string>(BetRejectReasonJs).ConfigureAwait(continueOnCapturedContext: false) ?? string.Empty; }
				catch { }
				if (!string.IsNullOrEmpty(reject))
				{
					FileLogger.Log($"[CDP] 베팅 거절: table='{tableId}', spot={spotKo}, 사유='{reject}' | 소요 마킹={__tMark}ms 클릭완료={__tClick}ms 확인={__sw.ElapsedMilliseconds}ms", "Warning", "Betting");
					return $"실패: 베팅 거절 — {reject}";
				}
				FileLogger.Log($"[CDP] 베팅 미반영: table='{tableId}', spot={spotKo}, 금액 {beforeAmt}→₩{maxVal:N0} (마감/거절 추정) | 소요 마킹={__tMark}ms 클릭완료={__tClick}ms 확인={__sw.ElapsedMilliseconds}ms", "Warning", "Betting");
				return $"실패: 베팅 미반영 (마감/거절 추정) — 스팟 금액 변화 없음";
			}
			// 안정화된 최종 스팟 총액이 이번 라운드에 실제로 걸린 금액이다(사이트가 정산하는 값과 일치).
			decimal actualWon = maxVal;
			string shortNote = (actualWon + 1m < targetTotal) ? $" ⚠부족(마감으로 목표 ₩{expectedWon:N0} 미달)" : string.Empty;
			FileLogger.Log($"[CDP] ★ Evolution 베팅 반영: table='{tableId}', plan={chipPlan}(₩{expectedWon:N0}), spot={spotKo}, 금액 {beforeAmt}→₩{maxVal:N0}(실제 ₩{actualWon:N0}){shortNote} | 소요 마킹={__tMark}ms 클릭완료={__tClick}ms 확인={__sw.ElapsedMilliseconds}ms", "Info", "Betting");
			return actualWon > 0m ? $"OK:{actualWon.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : "OK";
		}
		catch (Exception ex)
		{
			FileLogger.Log("[CDP] Evolution 베팅 예외: " + ex.Message, "Warning", "Betting", ex.StackTrace);
			return "실패: 예외 " + ex.Message;
		}
	}

	// 스카이라인/에볼루션 표준 칩 액면(내림차순). 부족분 보충 시 부족분 이하의 가장 큰 칩을 고른다.
	private static readonly (string Token, decimal Won)[] EvoChipDenoms = new[]
	{
		("1M", 1000000m), ("200k", 200000m), ("100k", 100000m), ("50k", 50000m), ("10k", 10000m), ("2000", 2000m)
	};

	/// <summary>부족분(원) 이하인 가장 큰 칩 토큰을 반환. 부족분이 최소 칩(2000)보다 작으면 null.</summary>
	private static string? LargestChipTokenForAmount(decimal amount)
	{
		foreach ((string token, decimal won) in EvoChipDenoms)
		{
			if (won <= amount) { return token; }
		}
		return null;
	}

	/// <summary>
	/// 스팟 금액이 더 커지지 않을 때까지(안정) 기다린 뒤 최종 최댓값을 반환한다. 칩이 서버에 순차
	/// 확인되며 금액이 점점 커지므로, 첫 변화에서 끊으면 부분 금액을 읽는다. floor(=시작 하한) 초과로
	/// 커진 뒤 3연속 같은 값이면 확정.
	/// </summary>
	private async Task<decimal> StabilizeSpotAmountAsync(IFrame frame, string tableId, string spotKo, decimal floor)
	{
		decimal maxVal = floor;
		int stable = 0;
		for (int w = 0; w < 16; w++)
		{
			await Task.Delay(120).ConfigureAwait(continueOnCapturedContext: false);
			string cur = await frame.EvaluateFunctionAsync<string>(GetSpotAmountJs, tableId, spotKo).ConfigureAwait(continueOnCapturedContext: false) ?? string.Empty;
			decimal curVal = ParseSpotAmount(cur);
			if (curVal > maxVal)
			{
				maxVal = curVal;
				stable = 0;
			}
			else if (maxVal > floor)
			{
				if (++stable >= 3) { break; }
			}
		}
		return maxVal;
	}

	/// <summary>
	/// 스팟에 표시된 금액 텍스트를 원 단위 숫자로 파싱한다. "70k"→70000, "70,000"→70000,
	/// "₩70,000"→70000, "1M"→1000000. 파싱 불가면 0.
	/// </summary>
	private static decimal ParseSpotAmount(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) { return 0m; }
		var m = System.Text.RegularExpressions.Regex.Match(text, @"([\d][\d.,]*)\s*([kKmM]?)");
		if (!m.Success) { return 0m; }
		string num = m.Groups[1].Value.Replace(",", "");
		if (!decimal.TryParse(num, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal v) || v <= 0m)
		{
			return 0m;
		}
		string suf = m.Groups[2].Value.ToLowerInvariant();
		if (suf == "k") { v *= 1000m; }
		else if (suf == "m") { v *= 1000000m; }
		return v;
	}

	private async Task InjectHookScriptAsync(ICDPSession client, ITarget target)
	{
		if (target.Type != TargetType.Page && target.Type != TargetType.IFrame)
		{
			return;
		}
		if (_injectJsContent == null)
		{
			try
			{
				string extPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Extension", "inject.js");
				if (!File.Exists(extPath))
				{
					FileLogger.Log("[CDP] inject.js 파일 없음: " + extPath, "Warning", "CDP");
					_injectJsContent = string.Empty;
					return;
				}
				_injectJsContent = await File.ReadAllTextAsync(extPath).ConfigureAwait(continueOnCapturedContext: false)
					+ Environment.NewLine + TableRouteHookJs;
				FileLogger.Log($"[CDP] inject.js 로드됨: {_injectJsContent.Length} bytes (테이블 라우팅 훅 포함)");
			}
			catch (Exception ex)
			{
				FileLogger.Log("[CDP] inject.js 로드 실패: " + ex.Message, "Warning", "CDP");
				_injectJsContent = string.Empty;
				return;
			}
		}
		if (string.IsNullOrEmpty(_injectJsContent))
		{
			return;
		}
		try
		{
			await client.SendAsync("Page.enable").ConfigureAwait(continueOnCapturedContext: false);
			await client.SendAsync("Page.addScriptToEvaluateOnNewDocument", new
			{
				source = _injectJsContent
			}).ConfigureAwait(continueOnCapturedContext: false);
			await client.SendAsync("Runtime.evaluate", new
			{
				expression = _injectJsContent,
				awaitPromise = false,
				returnByValue = false
			}).ConfigureAwait(continueOnCapturedContext: false);
			FileLogger.Log($"[CDP] inject.js 주입 완료: Type={target.Type}, URL={target.Url}");
		}
		catch (Exception ex2)
		{
			FileLogger.Log("[CDP] inject.js 주입 실패: URL=" + target.Url + ", error=" + ex2.Message, "Warning", "CDP");
		}
	}

	private void ProcessCdpMessage(ICDPSession client, string messageId, string messageData)
	{
		try
		{
			long cdpMsgNum = Interlocked.Increment(ref _cdpMessageCount);
			if (cdpMsgNum <= 20)
			{
				FileLogger.Log($"[CDP] CDP 메시지 #{cdpMsgNum}: {messageId}");
			}
			switch (messageId)
			{
			case "Network.webSocketFrameReceived":
				ProcessWebSocketFrame(messageData, "receive");
				break;
			case "Network.webSocketFrameSent":
				ProcessWebSocketFrame(messageData, "send");
				break;
			case "Network.webSocketCreated":
				ProcessWebSocketCreated(messageData);
				break;
			case "Network.webSocketClosed":
				ProcessWebSocketClosed(messageData);
				break;
			case "Network.responseReceived":
				ProcessResponseReceived(messageData);
				break;
			case "Network.loadingFinished":
				ProcessLoadingFinished(client, messageData);
				break;
			case "Runtime.executionContextCreated":
				TrackExecutionContext(client, messageData, created: true);
				break;
			case "Runtime.executionContextDestroyed":
				TrackExecutionContext(client, messageData, created: false);
				break;
			case "Runtime.executionContextsCleared":
				ContextsFor(client.Id).Clear();
				break;
			}
		}
		catch (Exception)
		{
		}
	}

	private List<string> BuildWalletUrlCandidates()
	{
		List<string> candidates = new List<string>();
		void AddWithBareVariant(string u)
		{
			if (string.IsNullOrEmpty(u) || candidates.Contains(u))
			{
				return;
			}
			candidates.Add(u);
			int q = u.IndexOf('?');
			if (q > 0 && !candidates.Contains(u.Substring(0, q)))
			{
				candidates.Add(u.Substring(0, q));
			}
		}
		// 1) 확정된 URL(성공 이력) 최우선.
		AddWithBareVariant(_walletUrl);
		// 2) 네트워크에서 본 모든 후보(최신순) — 어느 도메인이 진짜 지갑인지는 '유효 잔액을 돌려주는지'로
		//    가려낸다. 사이트마다 도메인이 달라도 여기서 자동으로 맞는 것을 찾는다.
		lock (_walletUrlsLock)
		{
			foreach (string u in _observedWalletUrls)
			{
				AddWithBareVariant(u);
			}
		}
		// 3) 상대경로 폴백 — 각 세션 자기 origin 에 붙으므로, 지갑 iframe 세션에서 바로 맞는다.
		if (!candidates.Contains(DefaultWalletPath))
		{
			candidates.Add(DefaultWalletPath);
		}
		return candidates;
	}

	// 게임 소켓 URL에서 지갑 조회 URL을 유도해 직접 fetch 한다. 네트워크 응답을 '관찰'해 URL을 얻는
	// 방식은 지갑 XHR이 iframe 초기화 때 한 번 나가고 끝이라 봇의 감시보다 앞서면(레이스) 영영 못 본다.
	// 반면 게임 소켓은 항상 훅(__pragmaticbot_websockets)에 잡혀 있으므로, 그 host + JSESSIONID 로
	// 'https://games.<base>/api/wallet/balance?JSESSIONID=..' (및 소켓 자체 host)를 만들어 조회한다.
	// 도메인을 하드코딩하지 않고 런타임 소켓에서 뽑으므로 어느 PP 사이트든 동적으로 맞는다.
	private const string DeriveWalletFromSocketJs =
		"(async function(){try{" +
		"var list=(window.__pragmaticbot_websockets||[]).slice();var cur=window.__pragmaticbot_ws;if(cur&&list.indexOf(cur)<0)list.push(cur);" +
		"var cands=[];var seen={};" +
		"for(var i=0;i<list.length;i++){var u=(list[i]&&list[i].url)||'';var m=u.match(/^wss?:\\/\\/([^\\/?]+)/);if(!m)continue;var host=m[1];" +
		"var sm=u.match(/JSESSIONID=([^&]+)/);if(!sm)continue;var sid=sm[1];" +
		"var parts=host.split('.');var base=parts.length>2?parts.slice(1).join('.'):host;" +
		"['https://games.'+base+'/api/wallet/balance?JSESSIONID='+sid,'https://'+host+'/api/wallet/balance?JSESSIONID='+sid].forEach(function(k){if(!seen[k]){seen[k]=1;cands.push(k);}});}" +
		"var usable=function(s){if(!s)return false;var c=s.trim().charAt(0);return c==='{'||c==='[';};" +
		"for(var j=0;j<cands.length;j++){try{var r=await fetch(cands[j],{credentials:'include'});if(!r||!r.ok)continue;var t=await r.text();if(usable(t))return JSON.stringify({url:cands[j],body:t.slice(0,2000)});}catch(e){}}" +
		"return '';}catch(e){return '';}})()";

	private async Task<bool> TryFetchWalletBalanceAsync()
	{
		long lastTicks = Interlocked.Read(in _lastWalletFetchTicksUtc);
		if (lastTicks > 0 && (DateTime.UtcNow - new DateTime(lastTicks, DateTimeKind.Utc)).TotalSeconds < 5.0)
		{
			return false;
		}
		Interlocked.Exchange(ref _lastWalletFetchTicksUtc, DateTime.UtcNow.Ticks);
		List<ICDPSession> sessions;
		lock (_attachedSessions)
		{
			sessions = ((_walletSessionId != null) ? _attachedSessions.Where((ICDPSession s) => s.Id == _walletSessionId).ToList() : _attachedSessions.ToList());
			if (_walletSessionId != null && sessions.Count == 0)
			{
				_walletSessionId = null;
				sessions = _attachedSessions.ToList();
			}
		}
		if (sessions.Count == 0)
		{
			return false;
		}
		// 1순위: 게임 소켓에서 지갑 URL 유도 → 직접 조회(동적, 관찰 레이스 무관).
		foreach (ICDPSession session in sessions)
		{
			try
			{
				JsonElement? dresp = await session.SendAsync("Runtime.evaluate", new
				{
					expression = DeriveWalletFromSocketJs,
					awaitPromise = true,
					returnByValue = true
				}, true, new CommandOptions { Timeout = BetEvalTimeoutMs * 2 }).ConfigureAwait(continueOnCapturedContext: false);
				if (!dresp.HasValue || !dresp.Value.TryGetProperty("result", out var dres)
					|| !dres.TryGetProperty("value", out var dval) || dval.ValueKind != JsonValueKind.String)
				{
					continue;
				}
				string djson = dval.GetString() ?? string.Empty;
				if (djson.Length == 0 || djson.TrimStart().Length == 0 || djson.TrimStart()[0] != '{')
				{
					continue;
				}
				using JsonDocument doc = JsonDocument.Parse(djson);
				string durl = doc.RootElement.TryGetProperty("url", out var uEl) ? (uEl.GetString() ?? "") : "";
				string dbody = doc.RootElement.TryGetProperty("body", out var bEl) ? (bEl.GetString() ?? "") : "";
				TryPublishPpUserId(dbody);
				decimal? dbal = TryExtractBalanceFromJson(dbody);
				if (dbal > (decimal?)0m)
				{
					if (_walletSessionId != session.Id || !string.Equals(_walletUrl, durl, StringComparison.Ordinal))
					{
						FileLogger.Log($"[CDP][Wallet] 지갑 URL 유도 성공(소켓 기반): {session.Id} (URL={durl})", "Info", "CDP");
					}
					_walletSessionId = session.Id;
					_walletUrl = durl;
					RecordObservedWalletUrl(durl);
					Interlocked.Exchange(ref _walletFetchFailStreak, 0);
					PublishHttpBalance(dbal.Value, durl, "Wallet");
					return true;
				}
			}
			catch (Exception)
			{
			}
		}
		foreach (string target in BuildWalletUrlCandidates())
		{
			string urlLiteral = JsonSerializer.Serialize(target);
			// If the pinned URL's JSESSIONID has gone stale we fall back to the bare path, which the
			// API rejects. Rebuild it from the game page's own ssid before giving up — otherwise the
			// wallet reads as unavailable and the balance silently degrades to DOM scraping.
			// Without the session id the endpoint answers 200 with the two-character body `""` rather
			// than an error, so "did the call work" has to mean "did it return an object", not merely
			// a non-empty string — otherwise the retry never runs and the balance never arrives.
			string expression = "(async function(u){try{if(typeof fetch!=='function')return '';"
				+ "var usable=function(s){if(!s)return false;var c=s.trim().charAt(0);return c==='{'||c==='[';};"
				+ "var attempt=async function(x){try{var r=await fetch(x,{credentials:'include'});"
				+ "if(!r.ok)return '';return await r.text();}catch(e){return '';}};"
				+ "var t=await attempt(u);if(usable(t))return t;"
				+ "var sid=null;try{sid=new URLSearchParams(location.search).get('ssid');}catch(e){}"
				+ "if(sid&&u.indexOf('JSESSIONID=')<0){var t2=await attempt(u+(u.indexOf('?')<0?'?':'&')+'JSESSIONID='+sid);"
				+ "if(usable(t2))return t2;}"
				+ "return '';}catch(e){return '';}})(" + urlLiteral + ")";
			foreach (ICDPSession session in sessions)
			{
				try
				{
					JsonElement? response = await session.SendAsync("Runtime.evaluate", new
					{
						expression = expression,
						awaitPromise = true,
						returnByValue = true
					}).ConfigureAwait(continueOnCapturedContext: false);
					if (!response.HasValue || !response.Value.TryGetProperty("result", out var resultEl) || !resultEl.TryGetProperty("value", out var valueEl) || valueEl.ValueKind != JsonValueKind.String)
					{
						continue;
					}
					string body = valueEl.GetString();
					if (string.IsNullOrWhiteSpace(body))
					{
						continue;
					}
					string trimmed = body.TrimStart();
					if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
					{
						continue;
					}
					TryPublishPpUserId(trimmed);
					decimal? balance = TryExtractBalanceFromJson(trimmed);
					if (!(balance > (decimal?)0m))
					{
						continue;
					}
					if (_walletSessionId != session.Id)
					{
						_walletSessionId = session.Id;
						FileLogger.Log($"[CDP][Wallet] 지갑 세션 확정: {session.Id} (URL={target})", "Info", "CDP");
					}
					if (!string.Equals(_walletUrl, target, StringComparison.Ordinal))
					{
						FileLogger.Log("[CDP][Wallet] 조회 URL 교체: " + (_walletUrl ?? "(없음)") + " → " + target, "Info", "CDP");
						_walletUrl = target;
					}
					Interlocked.Exchange(ref _walletFetchFailStreak, 0);
					PublishHttpBalance(balance.Value, target, "Wallet");
					return true;
				}
				catch (Exception)
				{
				}
			}
		}
		if (Interlocked.Increment(ref _walletFetchFailStreak) >= 3)
		{
			Interlocked.Exchange(ref _walletFetchFailStreak, 0);
			FileLogger.Log($"[CDP][Wallet] 지갑 조회 {3}회 연속 실패 → 고정 URL·세션 해제 후 재탐색 (URL={_walletUrl ?? "(없음)"}, session={_walletSessionId ?? "(없음)"})", "Warning", "CDP");
			_walletUrl = null;
			lock (_attachedSessions)
			{
				_walletSessionId = null;
			}
		}
		return false;
	}

	private void PublishHttpBalance(decimal balance, string url, string source)
	{
		long lastPublish = Interlocked.Read(in _lastHttpBalancePublishTicksUtc);
		bool stale = lastPublish == 0L || (DateTime.UtcNow - new DateTime(lastPublish, DateTimeKind.Utc)).TotalSeconds >= 30.0;
		bool changed = _lastHttpBalanceValue != balance;
		if (changed || stale)
		{
			_lastHttpBalanceValue = balance;
			Interlocked.Exchange(ref _lastHttpBalancePublishTicksUtc, DateTime.UtcNow.Ticks);
			NotifyHttpBalanceReceived();
			if (changed)
			{
				FileLogger.Log($"[CDP][HTTP Balance] 잔액 감지({source}): {balance:N0}원 (URL: {url})");
			}
			else
			{
				FileLogger.Log($"[CDP][HTTP Balance] 정기 재확인({source}): {balance:N0}원", "Info", "CDP");
			}
			HttpBalanceReceived?.Invoke(this, balance);
		}
	}

	private void ProcessResponseReceived(string messageData)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(messageData);
			JsonElement root = doc.RootElement;
			if (!root.TryGetProperty("requestId", out var ridEl))
			{
				return;
			}
			string requestId = ridEl.GetString();
			if (string.IsNullOrEmpty(requestId) || !root.TryGetProperty("response", out var respEl))
			{
				return;
			}
			string url = (respEl.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null);
			if (!string.IsNullOrEmpty(url) && url.IndexOf("wallet/balance", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				// 관찰된 URL 은 후보 집합에 모은다(단일 고정 금지). 확정(_walletUrl)은 실제 잔액을
				// 돌려준 fetch 성공 시에만 갱신한다 → 오답 URL 에 묶여 무한 재시도하는 문제를 없앤다.
				RecordObservedWalletUrl(url);
				if (_pendingBalanceRequests.Count >= 64)
				{
					_pendingBalanceRequests.Clear();
				}
				_pendingBalanceRequests[requestId] = url;
			}
		}
		catch (Exception)
		{
		}
	}

	private void ProcessLoadingFinished(ICDPSession client, string messageData)
	{
		string requestId = null;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(messageData);
			if (!doc.RootElement.TryGetProperty("requestId", out var ridEl))
			{
				return;
			}
			requestId = ridEl.GetString();
		}
		catch (Exception)
		{
			return;
		}
		if (!string.IsNullOrEmpty(requestId) && _pendingBalanceRequests.TryRemove(requestId, out string url))
		{
			Task.Run(async delegate
			{
				await ReadWalletBalanceAsync(client, requestId, url).ConfigureAwait(continueOnCapturedContext: false);
			});
		}
	}

	private async Task ReadWalletBalanceAsync(ICDPSession client, string requestId, string url)
	{
		try
		{
			JsonElement? response = await client.SendAsync("Network.getResponseBody", new { requestId }).ConfigureAwait(continueOnCapturedContext: false);
			if (!response.HasValue)
			{
				return;
			}
			JsonElement root = response.Value;
			if (!root.TryGetProperty("body", out var bodyEl))
			{
				return;
			}
			string body = bodyEl.GetString();
			if (string.IsNullOrWhiteSpace(body))
			{
				return;
			}
			if (root.TryGetProperty("base64Encoded", out var b64El) && b64El.ValueKind == JsonValueKind.True)
			{
				try
				{
					body = Encoding.UTF8.GetString(Convert.FromBase64String(body));
				}
				catch
				{
					return;
				}
			}
			string trimmed = body.TrimStart();
			if (trimmed.Length != 0 && (trimmed[0] == '{' || trimmed[0] == '['))
			{
				TryPublishPpUserId(trimmed);
				decimal? balance = TryExtractBalanceFromJson(trimmed);
				if (balance > (decimal?)0m)
				{
					PublishHttpBalance(balance.Value, url, "응답가로채기");
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private static decimal? TryExtractBalanceFromJson(string json)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(json);
			return ExtractBalance(doc.RootElement);
		}
		catch
		{
			return null;
		}
	}

	private static decimal? ExtractBalance(JsonElement element)
	{
		switch (element.ValueKind)
		{
		case JsonValueKind.Object:
			foreach (JsonProperty prop in element.EnumerateObject())
			{
				string name = prop.Name.ToLowerInvariant();
				if (BalanceJsonKeys.Any((string k) => name.Contains(k, StringComparison.Ordinal)))
				{
					decimal? direct = ReadPositiveNumber(prop.Value);
					if (direct > (decimal?)0m)
					{
						return direct;
					}
				}
				decimal? nested = ExtractBalance(prop.Value);
				if (nested > (decimal?)0m)
				{
					return nested;
				}
			}
			return null;
		case JsonValueKind.Array:
			foreach (JsonElement item in element.EnumerateArray())
			{
				decimal? found = ExtractBalance(item);
				if (found > (decimal?)0m)
				{
					return found;
				}
			}
			return null;
		default:
			return null;
		}
	}

	private static decimal? ReadPositiveNumber(JsonElement value)
	{
		if (value.ValueKind == JsonValueKind.Number)
		{
			if (!value.TryGetDecimal(out var num) || !(num > 0m))
			{
				return null;
			}
			return num;
		}
		if (value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		string raw = value.GetString();
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		if (!decimal.TryParse(new string(raw.Where((char c) => char.IsDigit(c) || c == '.').ToArray()), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) || !(parsed > 0m))
		{
			return null;
		}
		return parsed;
	}

	/// <summary>
	/// Whether an outgoing frame is a game command worth recording. Lobby chatter (pings, subscribe,
	/// telemetry) is constant and uninteresting; table commands are a handful per round.
	/// </summary>
	private static bool IsGameCommandFrame(string payload)
	{
		if (string.IsNullOrEmpty(payload) || payload.Length > 4000)
		{
			return false;
		}
		if (payload.Contains("\"type\":\"metrics.", StringComparison.Ordinal)
			|| payload.Contains("\"type\":\"lobby.", StringComparison.Ordinal)
			|| payload.Contains("\"type\":\"settings.", StringComparison.Ordinal))
		{
			return false;
		}
		// Matched on the actual command shapes, not on the substring "bet": betting sites carry
		// analytics payloads ("aid":"betbook", event names containing "bet"…) that flooded this log
		// at Warning level on every page of the site.
		return payload.Contains("\"type\":\"baccarat.", StringComparison.Ordinal)
			|| payload.Contains("\"type\":\"CLIENT_BET", StringComparison.Ordinal)
			|| payload.Contains("<lpbet", StringComparison.OrdinalIgnoreCase)
			|| payload.Contains("<command channel=\"table-", StringComparison.OrdinalIgnoreCase);
	}

	private static string Truncate(string s, int max)
	{
		return (s.Length > max) ? (s.Substring(0, max) + "...") : s;
	}

	private void ProcessWebSocketCreated(string messageData)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(messageData);
			JsonElement root = doc.RootElement;
			string requestId = (root.TryGetProperty("requestId", out var ridEl) ? ridEl.GetString() : null);
			string url = (root.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null);
			FileLogger.Log("[CDP] WebSocket 생성: requestId=" + requestId + ", URL=" + url);
			if (!string.IsNullOrEmpty(requestId) && !string.IsNullOrEmpty(url) && (url.Contains("localhost") || url.Contains("127.0.0.1")))
			{
				_filteredRequestIds.TryAdd(requestId, 0);
				FileLogger.Log("[CDP] localhost WebSocket 필터링됨: requestId=" + requestId);
			}
		}
		catch (Exception)
		{
		}
	}

	private void ProcessWebSocketClosed(string messageData)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(messageData);
			string requestId = (doc.RootElement.TryGetProperty("requestId", out var ridEl) ? ridEl.GetString() : null);
			if (!string.IsNullOrEmpty(requestId))
			{
				_filteredRequestIds.TryRemove(requestId, out var _);
			}
		}
		catch (Exception)
		{
		}
	}

	private void ProcessWebSocketFrame(string messageData, string direction)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(messageData);
			JsonElement root = doc.RootElement;
			string requestId = null;
			string payloadData = null;
			int opcode = 1;
			if (root.TryGetProperty("requestId", out var requestIdEl))
			{
				requestId = requestIdEl.GetString();
			}
			if (!string.IsNullOrEmpty(requestId) && _filteredRequestIds.ContainsKey(requestId))
			{
				return;
			}
			if (root.TryGetProperty("response", out var responseEl))
			{
				if (responseEl.TryGetProperty("payloadData", out var payloadEl))
				{
					payloadData = payloadEl.GetString();
				}
				if (responseEl.TryGetProperty("opcode", out var opcodeEl))
				{
					opcode = opcodeEl.GetInt32();
				}
			}
			if (string.IsNullOrEmpty(payloadData))
			{
				return;
			}
			string decodedData;
			if (opcode == 2)
			{
				try
				{
					byte[] bytes = Convert.FromBase64String(payloadData);
					decodedData = Encoding.UTF8.GetString(bytes);
				}
				catch (FormatException)
				{
					decodedData = payloadData;
				}
			}
			else
			{
				decodedData = payloadData;
			}
			long count = Interlocked.Increment(ref _frameCount);
			bool inBetWindow = IsWithinBetDiagnosticWindow();

			// Outgoing game commands are what an unknown provider's bet format has to be learned from,
			// and they are rare — the 1-in-100 sampling below reliably misses them. Log them in full.
			if (direction == "send" && IsGameCommandFrame(decodedData))
			{
				FileLogger.Log("[CDP][송신명령] " + Truncate(decodedData, 1200), "Info", "Betting");
			}

			// 베팅 창 동안 매 프레임(betstats 스트림)을 찍던 것을 제거 — 진단 모드에서 이 홍수가 로그 큐(1000건)를
			// 꽉 채워 [베팅시간] 등 유용한 로그를 밀어냈고, 그 자체로도 부하였다. 이제 주기적 샘플만 남긴다.
			if (count <= 20 || count % 100 == 0)
			{
				int max = (inBetWindow ? 700 : 200);
				string preview = ((decodedData.Length > max) ? (decodedData.Substring(0, max) + "...") : decodedData);
				FileLogger.Log($"[CDP] WS 프레임 #{count}{(inBetWindow ? " [베팅직후]" : "")}: dir={direction}, opcode={opcode}, len={decodedData.Length}, preview={preview}");
			}
			WebSocketMessageReceived?.Invoke(this, new PragmaticWebSocketMessageEventArgs
			{
				RequestId = requestId,
				Url = "",
				Direction = direction,
				Data = decodedData,
				Timestamp = DateTime.Now
			});
		}
		catch (Exception)
		{
		}
	}

	private void Browser_Disconnected(object? sender, EventArgs e)
	{
		lock (_attachedSessions)
		{
			_attachedSessions.Clear();
			_attachedTargetIds.Clear();
			_sessionUrls.Clear();
		}
		_filteredRequestIds.Clear();
		_balanceSessionId = null;
		_balanceSelectorSaved = false;
		_consecutiveZeroPollCount = 0;
		_walkerFallbackConfirmed = false;
		_zeroSelectorBlacklist.Clear();
		FileLogger.Log("[CDP] Chrome 연결 끊김");
		ConnectionStateChanged?.Invoke(this, e: false);
	}

	private void StartEvoRxDrain()
	{
		_evoRxDrainTimer?.Dispose();
		// 게임 프레임은 초당 수십 건이라 300ms 간격으로 자주 비운다(지연 최소화 + 큐 상한 500).
		_evoRxDrainTimer = new Timer(delegate(object? _)
		{
			if (!_disposed && Interlocked.Exchange(ref _evoRxDraining, 1) != 1)
			{
				_ = DrainEvoRxQueueAsync().ContinueWith(t => Interlocked.Exchange(ref _evoRxDraining, 0));
			}
		}, null, 800, 300);
	}

	// 브라우저의 모든 프레임을 직접 열거해 window.__evoRxQueue 를 비운다. 세션 attach 여부에 의존하지
	// 않으므로, 봇이 세션을 못 붙인 OOPIF 게임 iframe(so-07/skyline 등)의 큐도 확실히 가져온다.
	private async Task DrainEvoRxQueueAsync()
	{
		IBrowser? browser = _browser;
		if (browser == null) { return; }
		const string expr = "(function(){try{var q=window.__evoRxQueue;if(!q||!q.length)return '[]';window.__evoRxQueue=[];return JSON.stringify(q);}catch(e){return '[]';}})()";
		int frameCount = 0, drained = 0;
		try
		{
			IPage[] pages = await browser.PagesAsync().ConfigureAwait(continueOnCapturedContext: false);
			foreach (IPage page in pages)
			{
				foreach (IFrame frame in page.Frames)
				{
					frameCount++;
					try
					{
						string json = await frame.EvaluateExpressionAsync<string>(expr).ConfigureAwait(continueOnCapturedContext: false);
						if (string.IsNullOrEmpty(json) || json.Length <= 2) { continue; }
						string[] items;
						try { items = JsonSerializer.Deserialize<string[]>(json) ?? System.Array.Empty<string>(); }
						catch { continue; }
						if (items.Length == 0) { continue; }
						drained += items.Length;
						foreach (string data in items)
						{
							if (string.IsNullOrEmpty(data)) { continue; }
							WebSocketMessageReceived?.Invoke(this, new PragmaticWebSocketMessageEventArgs
							{
								RequestId = "evo-rx",
								Url = "",
								Direction = "receive",
								Data = data,
								Timestamp = DateTime.Now
							});
						}
					}
					catch (Exception)
					{
						// cross-origin/detached 프레임 — 무시
					}
				}
			}
		}
		catch (Exception) { return; }
		long dc = Interlocked.Increment(ref _evoRxDrainTick);
		if (drained > 0)
		{
			FileLogger.Log($"[CDP] evo-rx 드레인: {drained}건 (프레임 {frameCount}개 순회)", "Info", "CDP");
		}
		else if (dc % 30 == 1)
		{
			FileLogger.Log($"[CDP] evo-rx 드레인 순회: 프레임 {frameCount}개 (수집 0)", "Info", "CDP");
		}
	}

	private void StartTargetRescan()
	{
		_targetRescanTimer?.Dispose();
		_targetRescanTimer = new Timer(delegate(object? _)
		{
			if (!_disposed && _browser != null && Interlocked.Exchange(ref _rescanInFlight, 1) != 1)
			{
				_ = RescanTargetsAsync().ContinueWith((Task task) => Interlocked.Exchange(ref _rescanInFlight, 0));
			}
		}, null, 3000, 3000);
	}

	private void StopTargetRescan()
	{
		_targetRescanTimer?.Dispose();
		_targetRescanTimer = null;
		_evoRxDrainTimer?.Dispose();
		_evoRxDrainTimer = null;
	}

	private async Task RescanTargetsAsync()
	{
		IBrowser browser = _browser;
		if (browser == null)
		{
			return;
		}
		try
		{
			ITarget[] array = browser.Targets();
			foreach (ITarget target in array)
			{
				if (_disposed)
				{
					break;
				}
				await AttachToTargetAsync(target).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception)
		{
		}
	}

	private async void Browser_TargetCreated(object? sender, TargetChangedArgs e)
	{
		try
		{
			await AttachToTargetAsync(e.Target).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception)
		{
		}
	}

	private void Browser_TargetDestroyed(object? sender, TargetChangedArgs e)
	{
		string targetId = e.Target.Url ?? e.Target.GetHashCode().ToString();
		List<ICDPSession> dead = new List<ICDPSession>();
		lock (_attachedSessions)
		{
			_attachedTargetIds.Remove(targetId);
			// Only the id used to be dropped, so the session object for the closed page stayed in the
			// list for ever. Bets kept being sent through it and failed with 'Target.detachedFromTarget'.
			foreach (ICDPSession s in _attachedSessions.ToArray())
			{
				if (_sessionUrls.TryGetValue(s.Id, out string url) && url == targetId)
				{
					dead.Add(s);
					_attachedSessions.Remove(s);
					_sessionUrls.Remove(s.Id);
				}
			}
		}
		foreach (ICDPSession s in dead)
		{
			_sessionContexts.TryRemove(s.Id, out _);
		}
		if (dead.Count > 0)
		{
			FileLogger.Log($"[CDP] 페이지 종료 → 세션 {dead.Count}개 정리 (URL={targetId})");
		}
	}

	private async Task PollBalanceAsync()
	{
		if (_disposed || !IsConnected || await TryFetchWalletBalanceAsync().ConfigureAwait(continueOnCapturedContext: false) || IsHttpBalanceActive() || Interlocked.CompareExchange(ref _pollInFlight, 1, 0) != 0)
		{
			return;
		}
		try
		{
			await PollBalanceCoreAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			Interlocked.Exchange(ref _pollInFlight, 0);
		}
	}

	private async Task PollBalanceCoreAsync()
	{
		long pollNum = Interlocked.Increment(ref _balancePollCount);
		bool isDiagnostic = pollNum <= 10;
		int sessionCount;
		List<ICDPSession> sessionsToCheck;
		lock (_attachedSessions)
		{
			sessionCount = _attachedSessions.Count;
			sessionsToCheck = ((_balanceSessionId != null) ? _attachedSessions.Where((ICDPSession s) => s.Id == _balanceSessionId).ToList() : _attachedSessions.ToList());
			if (_balanceSessionId != null && sessionsToCheck.Count == 0)
			{
				FileLogger.Log("[CDP] 잔액 세션 " + _balanceSessionId + " 소실 → 리셋하여 전체 탐색");
				_balanceSessionId = null;
				sessionsToCheck = _attachedSessions.ToList();
			}
		}
		FileLogger.Log($"[CDP] 폴링 시작 #{pollNum} (sessions={sessionCount}, locked={_balanceSessionId != null})");
		string jsExpression = BuildBalanceQueryJs();
		ConcurrentBag<SessionPollResult> sessionResults = new ConcurrentBag<SessionPollResult>();
		await Task.WhenAll(sessionsToCheck.Select((ICDPSession session) => PollSessionAsync(session, jsExpression, pollNum, isDiagnostic, sessionResults))).ConfigureAwait(continueOnCapturedContext: false);
		SessionPollResult selectorMatch = sessionResults.FirstOrDefault(delegate(SessionPollResult r)
		{
			string matchType = r.MatchType;
			return ((matchType == "selector" || matchType == "attribute") ? true : false) && r.Balance > 0m;
		});
		if (selectorMatch.SessionId != null)
		{
			if (_balanceSessionId != selectorMatch.SessionId)
			{
				_balanceSessionId = selectorMatch.SessionId;
				FileLogger.Log($"[CDP] 잔액 세션 확정: {selectorMatch.SessionId} (match={selectorMatch.MatchType}, balance={selectorMatch.Balance:N0})");
				if (!_balanceSelectorSaved && _customBalanceSelectors.Count == 0 && !string.IsNullOrEmpty(selectorMatch.Selector))
				{
					_balanceSelectorSaved = true;
					FileLogger.Log("[CDP] 잔액 셀렉터 자동 감지: " + selectorMatch.Selector);
					BalanceSelectorDetected?.Invoke(this, selectorMatch.Selector);
				}
			}
			_consecutiveZeroPollCount = 0;
			if (!string.IsNullOrEmpty(selectorMatch.Selector))
			{
				_zeroSelectorBlacklist.TryRemove(selectorMatch.Selector, out var _);
			}
			if (selectorMatch.Balance != _lastBalance)
			{
				_lastBalance = selectorMatch.Balance;
				FileLogger.Log($"[CDP] 잔액 감지: {selectorMatch.Balance:N0}원 (세션: {selectorMatch.SessionId}, match={selectorMatch.MatchType}, tag={selectorMatch.Tag}, cls={selectorMatch.Cls})");
			}
			BalanceReceived?.Invoke(this, selectorMatch.Balance);
			return;
		}
		SessionPollResult zeroSelectorMatch = sessionResults.FirstOrDefault(delegate(SessionPollResult r)
		{
			string matchType = r.MatchType;
			return ((matchType == "selector" || matchType == "attribute") ? true : false) && r.Balance == 0m;
		});
		if (zeroSelectorMatch.SessionId != null)
		{
			if (!string.IsNullOrEmpty(zeroSelectorMatch.Selector))
			{
				int newCount = _zeroSelectorBlacklist.AddOrUpdate(zeroSelectorMatch.Selector, 1, (string _, int v) => v + 1);
				if (newCount == 3)
				{
					FileLogger.Log($"[CDP] 제로 셀렉터 블랙리스트 등재: '{zeroSelectorMatch.Selector}' (연속 {newCount}회 0원 반환)", "Warning", "CDP");
				}
			}
			if (_walkerFallbackConfirmed)
			{
				if (_balanceSessionId != null)
				{
					_balanceSessionId = null;
				}
				if (isDiagnostic)
				{
					FileLogger.Log("[CDP] " + zeroSelectorMatch.MatchType + " 매치 값=0 무시 (walker fallback 확정 이력 있음) → walker 재탐색");
				}
			}
			else
			{
				_consecutiveZeroPollCount++;
				if (_consecutiveZeroPollCount < 5)
				{
					if (_balanceSessionId != zeroSelectorMatch.SessionId)
					{
						_balanceSessionId = zeroSelectorMatch.SessionId;
						FileLogger.Log($"[CDP] 잔액 세션 확정 (0원): {zeroSelectorMatch.SessionId} (match={zeroSelectorMatch.MatchType}) → walker fallback 차단 ({_consecutiveZeroPollCount}/{5})");
						if (!_balanceSelectorSaved && _customBalanceSelectors.Count == 0 && !string.IsNullOrEmpty(zeroSelectorMatch.Selector))
						{
							_balanceSelectorSaved = true;
							FileLogger.Log("[CDP] 잔액 셀렉터 자동 감지: " + zeroSelectorMatch.Selector);
							BalanceSelectorDetected?.Invoke(this, zeroSelectorMatch.Selector);
						}
					}
					if (isDiagnostic)
					{
						FileLogger.Log($"[CDP] {zeroSelectorMatch.MatchType} 매치 값=0 (세션: {zeroSelectorMatch.SessionId}) → 세션 잠금 ({_consecutiveZeroPollCount}/{5}), 0원 리포트");
					}
					BalanceReceived?.Invoke(this, 0m);
					return;
				}
				if (_balanceSessionId != null)
				{
					FileLogger.Log($"[CDP] 연속 {_consecutiveZeroPollCount}회 0원 감지 → 세션 락 해제, walker fallback 허용 (잘못된 #userbalance 요소일 가능성)", "Warning", "CDP");
					_balanceSessionId = null;
				}
			}
		}
		if (_balanceSessionId == null)
		{
			List<SessionPollResult> source = sessionResults.Where((SessionPollResult r) => r.MatchType == "walker" && r.Balance >= 10000m).ToList();
			SessionPollResult ppWalker = source.FirstOrDefault((SessionPollResult r) => r.IsPP);
			SessionPollResult nonPpWalker = source.FirstOrDefault((SessionPollResult r) => !r.IsPP);
			SessionPollResult best = ((ppWalker.SessionId != null) ? ppWalker : nonPpWalker);
			string source2 = ((ppWalker.SessionId != null) ? "PP우선" : "일반");
			if (best.SessionId != null && best.Balance > 0m)
			{
				bool isRoundNumber = best.Balance % 1000000m == 0m;
				string filterReason = null;
				bool filteredOut;
				if (_lastBalance > 0m)
				{
					filteredOut = isRoundNumber && Math.Abs(best.Balance - _lastBalance) > _lastBalance * 0.01m;
					if (filteredOut)
					{
						filterReason = "라운드넘버";
					}
					if (!filteredOut)
					{
						decimal ratio = best.Balance / _lastBalance;
						if (ratio < 0.5m || ratio > 1.5m)
						{
							filteredOut = true;
							filterReason = $"이전 잔액 ±50% 밴드 이탈 (ratio={ratio:0.000})";
						}
					}
				}
				else
				{
					// 첫 감지에서 정확히 라운드 100만원 배수(1,000,000 / 2,000,000 …)는 포털의 프로모/데모
					// 플레이스홀더인 경우가 대부분이라(실계정 잔액이 딱 떨어지는 100만 배수인 경우는 드묾)
					// 시작금액으로 잡지 않는다. 진짜 잔액은 지갑(권위값)이 곧 채운다.
					filteredOut = isRoundNumber && best.Balance >= 1000000m;
					if (filteredOut)
					{
						filterReason = "첫감지 라운드 100만배수(플레이스홀더 의심)";
					}
					if (!filteredOut && best.Balance < 1000000m && best.Balance % 10000m == 0m)
					{
						filteredOut = true;
						filterReason = "첫감지 칩금액 의심 (< 100만원 & 10,000 단위)";
					}
				}
				if (filteredOut)
				{
					FileLogger.Log($"[CDP] walker fallback 오감지 무시 ({filterReason}): {best.Balance:N0} (이전: {_lastBalance:N0}, tag={best.Tag}, cls={best.Cls})", "Warning", "CDP");
				}
				else
				{
					if (best.Balance != _lastBalance)
					{
						_lastBalance = best.Balance;
						FileLogger.Log($"[CDP] 잔액 감지 (walker fallback-{source2}): {best.Balance:N0}원 (세션: {best.SessionId}, tag={best.Tag}, cls={best.Cls})");
					}
					_consecutiveZeroPollCount = 0;
					if (!_walkerFallbackConfirmed)
					{
						_walkerFallbackConfirmed = true;
						FileLogger.Log("[CDP] walker fallback 확정 이력 기록 → 이후 #userbalance=0 매치 무시", "Info", "CDP");
					}
					BalanceReceived?.Invoke(this, best.Balance);
				}
			}
		}
		FileLogger.Log($"[CDP] 폴링 완료 #{pollNum}");
	}

	private async Task PollSessionAsync(ICDPSession session, string jsExpression, long pollNum, bool isDiagnostic, ConcurrentBag<SessionPollResult> results)
	{
		Stopwatch sw = Stopwatch.StartNew();
		try
		{
			JsonElement? response = await session.SendAsync("Runtime.evaluate", new
			{
				expression = jsExpression,
				returnByValue = true
			}).ConfigureAwait(continueOnCapturedContext: false);
			sw.Stop();
			if (sw.ElapsedMilliseconds > 3000)
			{
				FileLogger.Log($"[CDP] 세션 {session.Id} Runtime.evaluate 느림: {sw.ElapsedMilliseconds}ms (폴링 #{pollNum})", "Warning", "CDP");
			}
			if (!response.HasValue)
			{
				if (isDiagnostic)
				{
					FileLogger.Log($"[CDP] 잔액폴링 #{pollNum}: 세션 {session.Id} → response=null");
				}
				return;
			}
			JsonElement responseEl = response.Value;
			if (isDiagnostic)
			{
				string rawResponse = responseEl.ToString();
				string preview = ((rawResponse.Length > 500) ? (rawResponse.Substring(0, 500) + "...") : rawResponse);
				FileLogger.Log($"[CDP] 잔액폴링 #{pollNum}: 세션 {session.Id} → {preview}");
			}
			if (responseEl.TryGetProperty("exceptionDetails", out var exDetails))
			{
				if (isDiagnostic)
				{
					FileLogger.Log($"[CDP] 잔액폴링 #{pollNum}: JS 예외 발생 → {exDetails}");
				}
				return;
			}
			string resultValue = null;
			if (responseEl.TryGetProperty("result", out var resultEl) && resultEl.TryGetProperty("value", out var valueEl) && valueEl.ValueKind == JsonValueKind.String)
			{
				resultValue = valueEl.GetString();
			}
			if (string.IsNullOrWhiteSpace(resultValue))
			{
				if (isDiagnostic)
				{
					FileLogger.Log($"[CDP] 잔액폴링 #{pollNum}: 세션 {session.Id} → 빈 결과");
				}
				return;
			}
			if (isDiagnostic)
			{
				FileLogger.Log($"[CDP] DOM 숫자 탐색 #{pollNum}: {resultValue}");
			}
			try
			{
				using JsonDocument items = JsonDocument.Parse(resultValue);
				string sessionUrl;
				lock (_attachedSessions)
				{
					sessionUrl = (_sessionUrls.TryGetValue(session.Id, out string sUrl) ? sUrl : "");
				}
				bool isPP = sessionUrl.Contains("pragmaticplaylive.net", StringComparison.OrdinalIgnoreCase);
				foreach (JsonElement item in items.RootElement.EnumerateArray())
				{
					string text = (item.TryGetProperty("text", out var textEl) ? textEl.GetString() : null);
					if (string.IsNullOrEmpty(text))
					{
						continue;
					}
					string matchType = (item.TryGetProperty("match", out var matchEl) ? matchEl.GetString() : "unknown");
					if (DateLikeTextRegex.IsMatch(text))
					{
						if (isDiagnostic)
						{
							FileLogger.Log($"[CDP] 잔액 후보 제외(날짜·시각 형식): \"{text}\" (match={matchType})", "Warning", "CDP");
						}
					}
					else
					{
						if (!decimal.TryParse(Regex.Replace(text, "[^0-9.,]", "").Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var balance))
						{
							continue;
						}
						if (balance > 10000000000m)
						{
							if (isDiagnostic)
							{
								FileLogger.Log($"[CDP] 잔액 후보 제외(상한 {10000000000m:N0} 초과): {balance:N0} (match={matchType})", "Warning", "CDP");
							}
						}
						else
						{
							string selector = (item.TryGetProperty("selector", out var selEl) ? selEl.GetString() : null);
							string tag = (item.TryGetProperty("tag", out var tagEl) ? tagEl.GetString() : "?");
							string cls = (item.TryGetProperty("cls", out var clsEl) ? clsEl.GetString() : "");
							results.Add(new SessionPollResult(session.Id, balance, matchType, tag, cls, isPP, selector));
							if ((matchType == "selector" || matchType == "attribute") ? true : false)
							{
								break;
							}
						}
					}
				}
			}
			catch (JsonException ex)
			{
				if (isDiagnostic)
				{
					FileLogger.Log($"[CDP] 잔액폴링 #{pollNum}: JSON 파싱 실패 → {ex.Message}");
				}
			}
		}
		catch (Exception ex2)
		{
			if (isDiagnostic)
			{
				FileLogger.Log($"[CDP] 잔액폴링 #{pollNum}: 세션 오류 → {ex2.Message}");
			}
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_balanceTimer?.Dispose();
			_balanceTimer = null;
			StopTargetRescan();
			try
			{
				if (_browser != null)
				{
					_browser.Disconnected -= Browser_Disconnected;
					_browser.TargetCreated -= Browser_TargetCreated;
					_browser.TargetDestroyed -= Browser_TargetDestroyed;
					ICDPSession[] sessionsToDetach;
					lock (_attachedSessions)
					{
						sessionsToDetach = _attachedSessions.ToArray();
						_attachedSessions.Clear();
						_attachedTargetIds.Clear();
						_sessionUrls.Clear();
					}
					_filteredRequestIds.Clear();
					ICDPSession[] array = sessionsToDetach;
					foreach (ICDPSession session in array)
					{
						try
						{
							session.DetachAsync().GetAwaiter().GetResult();
						}
						catch
						{
						}
					}
					_balanceSessionId = null;
					_balanceSelectorSaved = false;
					_browser.Disconnect();
					_browser = null;
				}
			}
			catch
			{
			}
		}
		GC.SuppressFinalize(this);
	}
}
