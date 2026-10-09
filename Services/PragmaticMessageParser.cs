using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;

namespace PragmaticBot.Services;

public class PragmaticMessageParser
{
	private long _parseMessageCount;

	/// <summary>Counts blocked Evolution frames so the warning is logged once, not per message.</summary>
	private long _evoBlockedCount;

	private readonly LinkedList<string> _recentPayloadOrder = new LinkedList<string>();

	private readonly HashSet<string> _recentPayloads = new HashSet<string>(StringComparer.Ordinal);

	private const int RecentPayloadCapacity = 600;

	private readonly object _dedupLock = new object();

	private readonly ConcurrentDictionary<string, int> _unknownTypeLogCount = new ConcurrentDictionary<string, int>();

	private const int UnknownTypeLogLimit = 3;

	public event EventHandler<PpGameStartedArgs>? GameStarted;

	public event EventHandler<PpTimerArgs>? TimerReceived;

	public event EventHandler<PpTableEventArgs>? BetsOpened;

	public event EventHandler<PpTableEventArgs>? BetsClosingSoon;

	public event EventHandler<PpTableEventArgs>? BetsClosed;

	public event EventHandler<PpTableEventArgs>? DealingStarted;

	public event EventHandler<PpGameResultArgs>? GameResultReceived;

	public event EventHandler<PpBetConfirmArgs>? BetConfirmed;

	public event EventHandler<PpBetsConfirmArgs>? BetsConfirmed;

	public event EventHandler<PpWinArgs>? WinReceived;

	public event EventHandler<PpShoeSummaryArgs>? ShoeSummaryReceived;

	public event EventHandler<PpCardDealtArgs>? CardDealt;

	public event EventHandler<PpCardIncArgs>? CardIncremented;

	public event EventHandler<PpSeatArgs>? SeatReceived;

	public event EventHandler<PpStatisticArgs>? StatisticReceived;

	/// <summary>Tables seen carrying Evolution traffic, so a bet can pick the right wire format.</summary>
	public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> EvolutionTables
		= new(StringComparer.OrdinalIgnoreCase);

	/// <summary>True when this table has been observed speaking Evolution's protocol.</summary>
	public static bool IsEvolutionTable(string tableId)
		=> !string.IsNullOrEmpty(tableId) && EvolutionTables.ContainsKey(tableId);

	/// <summary>
	/// Set the first time any Evolution (baccarat.*/balanceUpdated) frame is parsed. Signals the rest
	/// of the app that this is a live-casino session, so the page-scraped balance (the portal wallet,
	/// unrelated to the game balance) must be ignored in favour of the socket's balanceUpdated.
	/// </summary>
	public bool HasEvolutionActivity { get; private set; }

	public event EventHandler<PpTableConfigArgs>? TableConfigReceived;

	public event EventHandler<PpBetValidationErrorArgs>? BetValidationErrorReceived;

	/// <summary>
	/// Evolution pushes the wallet balance over the game socket (balanceUpdated). Treated as an
	/// authoritative figure: its /api/wallet/balance endpoint is Pragmatic-only, so the HTTP poll
	/// can never answer on an Evolution site and the balance would fall back to DOM scraping.
	/// </summary>
	public event EventHandler<decimal>? AuthoritativeBalanceReceived;

	private bool IsDuplicatePayload(string jsonData)
	{
		lock (_dedupLock)
		{
			if (!_recentPayloads.Add(jsonData))
			{
				return true;
			}
			_recentPayloadOrder.AddLast(jsonData);
			if (_recentPayloadOrder.Count > 600)
			{
				string oldest = _recentPayloadOrder.First.Value;
				_recentPayloadOrder.RemoveFirst();
				_recentPayloads.Remove(oldest);
			}
			return false;
		}
	}

	public void ParseMessage(string jsonData)
	{
		if (string.IsNullOrWhiteSpace(jsonData) || IsDuplicatePayload(jsonData))
		{
			return;
		}
		long msgNum = Interlocked.Increment(ref _parseMessageCount);
		try
		{
			using JsonDocument doc = JsonDocument.Parse(jsonData);
			// Evolution(라이브 바카라) 메시지: 최상위에 {"type":"baccarat.xxx","args":{...}} 형태.
			// 복호화 평문이 이 형태로 들어오면 별도 라우팅으로 처리하고 기존 PP 스위치는 건너뛴다.
			JsonElement evoRoot = doc.RootElement;
			if (evoRoot.ValueKind == JsonValueKind.Object
				&& evoRoot.TryGetProperty("type", out var evoTypeEl)
				&& evoTypeEl.ValueKind == JsonValueKind.String)
			{
				string evoType = evoTypeEl.GetString() ?? string.Empty;
				if (evoType.StartsWith("baccarat.", StringComparison.Ordinal)
					|| string.Equals(evoType, "balanceUpdated", StringComparison.Ordinal))
				{
					// Second line of defence behind the extension grant: even if Evolution traffic
					// reaches an account that was not given the provider, it is not acted on.
					if (!AuthService.CurrentProviders.HasFlag(GameProviders.Evolution))
					{
						if (Interlocked.Increment(ref _evoBlockedCount) == 1)
						{
							FileLogger.Log("[Parser] 에볼루션 메시지 수신했으나 이 계정에는 권한이 없어 무시합니다.", "Warning", "Auth");
						}
						return;
					}
					ParseEvolutionMessage(evoType, evoRoot);
					return;
				}
			}
			using JsonElement.ObjectEnumerator objectEnumerator = doc.RootElement.EnumerateObject().GetEnumerator();
			if (!objectEnumerator.MoveNext())
			{
				return;
			}
			JsonProperty prop = objectEnumerator.Current;
			string msgType = prop.Name;
			JsonElement payload = prop.Value;
			if (msgNum <= 10)
			{
				FileLogger.Log($"[Parser] 메시지 #{msgNum} 타입: {msgType}");
			}
			switch (msgType)
			{
			case "game":
				ParseGameMessage(payload);
				break;
			case "timer":
				ParseTimerMessage(payload);
				break;
			case "betsopen":
				ParseBetsOpenMessage(payload);
				break;
			case "betsclosingsoon":
				ParseBetsClosingSoonMessage(payload);
				break;
			case "betsclosed":
				ParseBetsClosedMessage(payload);
				break;
			case "startDealing":
				ParseDealingStartedMessage(payload);
				break;
			case "gameresult":
				FileLogger.Log($"[Parser] gameresult 수신 (메시지 #{msgNum}): {payload.GetRawText()}");
				ParseGameResultMessage(payload);
				break;
			case "bet":
				ParseBetMessage(payload);
				break;
			case "bets":
				ParseBetsMessage(payload);
				break;
			case "win":
				FileLogger.Log($"[Parser] win 수신 (메시지 #{msgNum}): {payload.GetRawText()}");
				ParseWinMessage(payload);
				break;
			case "ShoeSummary":
				ParseShoeSummaryMessage(payload);
				break;
			case "card":
				ParseCardMessage(payload);
				break;
			case "cardinc":
				ParseCardIncMessage(payload);
				break;
			case "seat":
				ParseSeatMessage(payload);
				break;
			case "statistic":
				ParseStatisticMessage(payload);
				break;
			case "table":
			case "goodroad":
			case "currentShoe":
			case "statisticLA":
				if (msgNum <= 30)
				{
					string raw = payload.ToString();
					string truncated = ((raw.Length > 500) ? (raw.Substring(0, 500) + "...(truncated)") : raw);
					FileLogger.Log($"[Parser] {msgType} 수신 (메시지 #{msgNum}): {truncated}");
				}
				break;
			case "tableconfig":
				ParseTableConfigMessage(payload);
				break;
			case "toasterMessage":
				FileLogger.Log("[Parser] toasterMessage 수신: " + payload.GetRawText(), "Info", "Betting");
				break;
			case "betValidationError":
				ParseBetValidationErrorMessage(payload);
				break;
			default:
				LogUnknownMessageType(msgType, payload);
				break;
			case "pong":
				break;
			case "betstats":
				break;
			case "disablesidebets":
				break;
			case "mb_stats_s":
				break;
			case "endshuffling":
				break;
			case "tablesorder":
				break;
			case "multipliers":
				break;
			case "winners":
				break;
			case "command":
				break;
			case "diceinc":
				break;
			case "mb_dice":
				break;
			case "voip_cc":
				break;
			case "subscribe":
				break;
			case "startshuffling":
				break;
			case "dealer":
				break;
			}
		}
		catch (JsonException)
		{
			if (msgNum <= 20)
			{
				string preview = ((jsonData.Length > 100) ? (jsonData.Substring(0, 100) + "...") : jsonData);
				FileLogger.Log($"[Parser] JSON 파싱 실패 #{msgNum}: {preview}");
			}
		}
	}

	private void ParseBetValidationErrorMessage(JsonElement payload)
	{
		string table = payload.GetStringOrDefault("table");
		string code = payload.GetStringOrDefault("code");
		string desc = payload.GetStringOrDefault("desc");
		string betCode = payload.GetStringOrDefault("betCode");
		string amountText = payload.GetStringOrDefault("amount");
		FileLogger.Log($"[Parser] ✗ 베팅 거절 통보: table={table}, code={code}, desc={desc}, amount={amountText}, betCode={betCode}", "Warning", "Betting");
		BetValidationErrorReceived?.Invoke(this, new PpBetValidationErrorArgs
		{
			TableId = table,
			Code = code,
			Description = desc,
			BetCode = betCode,
			Amount = (decimal.TryParse(amountText, out var amount) ? amount : 0m)
		});
	}

	private void LogUnknownMessageType(string msgType, JsonElement payload)
	{
		int seen = _unknownTypeLogCount.AddOrUpdate(msgType, 1, (string _, int v) => v + 1);
		if (seen <= 3)
		{
			string raw = payload.GetRawText();
			string truncated = ((raw.Length > 600) ? (raw.Substring(0, 600) + "...(truncated)") : raw);
			FileLogger.Log($"[Parser] ★ 미처리 메시지 타입 '{msgType}' (#{seen}): {truncated}", "Warning", "Betting");
		}
	}

	private void ParseGameMessage(JsonElement payload)
	{
		string gameId = payload.GetStringOrDefault("id");
		string table = payload.GetStringOrDefault("table");
		string value = payload.GetStringOrDefault("value");
		if (!string.IsNullOrEmpty(gameId) && !string.IsNullOrEmpty(table))
		{
			GameStarted?.Invoke(this, new PpGameStartedArgs
			{
				GameId = gameId,
				TableId = table,
				StartTime = value
			});
		}
	}

	private void ParseTimerMessage(JsonElement payload)
	{
		string table = payload.GetStringOrDefault("table");
		string valueStr = payload.GetStringOrDefault("value");
		if (!string.IsNullOrEmpty(table) && int.TryParse(valueStr, out var seconds))
		{
			TimerReceived?.Invoke(this, new PpTimerArgs
			{
				TableId = table,
				SecondsRemaining = seconds
			});
		}
	}

	// Evolution(라이브 바카라) 프로토콜 라우팅. 기존 PP 이벤트를 그대로 재사용하므로
	// 하위 소비처(Form1 → TableState/베팅 로직)는 수정 없이 동작한다.
	// 매핑: newGame→GameStarted, gameState/tableState(betting)→BetsOpened/BetsClosed,
	//       cardDealt→CardDealt, resolved→GameResultReceived, shoeState→ShoeSummaryReceived.
	/// <summary>Reads gameData.&lt;hand&gt;.cards, the full hand Evolution repeats on every cardDealt frame.</summary>
	private static List<string>? ReadHandCards(JsonElement args, string handKey)
	{
		if (!args.TryGetProperty("gameData", out var gameData) || gameData.ValueKind != JsonValueKind.Object
			|| !gameData.TryGetProperty(handKey, out var hand) || hand.ValueKind != JsonValueKind.Object
			|| !hand.TryGetProperty("cards", out var cards) || cards.ValueKind != JsonValueKind.Array)
		{
			return null;
		}
		List<string> list = new List<string>();
		foreach (JsonElement c in cards.EnumerateArray())
		{
			if (c.ValueKind != JsonValueKind.String) { continue; }
			string code = c.GetString() ?? string.Empty;
			// 일부 사이트(Always 8/9 등 특수 바카라)는 베팅 시간에 실제 카드 대신 "BAC8"/"BAC9" 같은
			// 예상-점수 placeholder 를 뱅커 자리에 넣는다. 이건 카드가 아니므로 걸러낸다.
			if (IsRealCardCode(code)) { list.Add(code); }
		}
		return list;
	}

	/// <summary>랭크(A,2~9,T/10,J,Q,K,0=hidden)+무늬(C,D,H,S) 또는 XX(뒷면)만 카드로 인정.</summary>
	private static bool IsRealCardCode(string code)
	{
		if (string.IsNullOrEmpty(code)) { return false; }
		if (code == "XX" || code == "00") { return true; }  // 뒷면/미공개
		if (code.Length < 2 || code.Length > 3) { return false; }
		char suit = char.ToUpperInvariant(code[code.Length - 1]);
		if (suit != 'C' && suit != 'D' && suit != 'H' && suit != 'S') { return false; }
		string rank = code.Substring(0, code.Length - 1).ToUpperInvariant();
		switch (rank)
		{
		case "A": case "2": case "3": case "4": case "5": case "6": case "7":
		case "8": case "9": case "10": case "T": case "J": case "Q": case "K": case "0":
			return true;
		default:
			return false;
		}
	}

	private static int ReadHandScore(JsonElement args, string handKey)
	{
		if (args.TryGetProperty("gameData", out var gameData) && gameData.ValueKind == JsonValueKind.Object
			&& gameData.TryGetProperty(handKey, out var hand) && hand.ValueKind == JsonValueKind.Object
			&& hand.TryGetProperty("score", out var scoreEl) && scoreEl.TryGetInt32(out var sc))
		{
			return sc;
		}
		return 0;
	}

	private void ParseEvolutionMessage(string type, JsonElement root)
	{
		if (!root.TryGetProperty("args", out var args) || args.ValueKind != JsonValueKind.Object)
		{
			return;
		}
		HasEvolutionActivity = true;
		string tableId = args.GetStringOrDefault("tableId");
		// The socket itself is the only trustworthy source of a table's provider: an account granted
		// both providers has PP and Evolution tables side by side in one room list, and each bet must
		// go out in the protocol of the table it belongs to.
		if (!string.IsNullOrEmpty(tableId))
		{
			EvolutionTables[tableId] = 0;
		}
		switch (type)
		{
		case "balanceUpdated":
			if (args.TryGetProperty("balance", out var balEl)
				&& balEl.ValueKind == JsonValueKind.Number
				&& balEl.TryGetDecimal(out var bal)
				&& bal > 0m)
			{
				AuthoritativeBalanceReceived?.Invoke(this, bal);
			}
			break;
		case "baccarat.playerBettingState":
		{
			// Evolution's acknowledgement of a bet. Mapped onto the existing PP confirmation event so
			// the betting service's wait/retry logic needs no provider-specific branch.
			if (!args.TryGetProperty("state", out var betState) || betState.ValueKind != JsonValueKind.Object)
			{
				break;
			}
			decimal total = (betState.TryGetProperty("totalAmount", out var totalEl)
				&& totalEl.ValueKind == JsonValueKind.Number
				&& totalEl.TryGetDecimal(out var tv)) ? tv : 0m;
			bool anyAccepted = betState.TryGetProperty("acceptedBets", out var accepted)
				&& accepted.ValueKind == JsonValueKind.Object
				&& accepted.EnumerateObject().Any();
			bool rejected = betState.TryGetProperty("rejectedBets", out var rejEl)
				&& rejEl.ValueKind == JsonValueKind.Object
				&& rejEl.EnumerateObject().Any();

			// "restore" frames replay the current state on (re)subscribe — not an answer to our bet
			bool isRestore = args.TryGetProperty("restore", out var restoreEl)
				&& restoreEl.ValueKind == JsonValueKind.True;
			if (isRestore || (!anyAccepted && !rejected && total <= 0m))
			{
				break;
			}

			List<(decimal, string)> bets = new List<(decimal, string)>();
			if (anyAccepted)
			{
				foreach (JsonProperty spot in accepted.EnumerateObject())
				{
					decimal amt = (spot.Value.ValueKind == JsonValueKind.Number && spot.Value.TryGetDecimal(out var av)) ? av : 0m;
					bets.Add((amt, spot.Name));
				}
			}
			BetsConfirmed?.Invoke(this, new PpBetsConfirmArgs
			{
				TableId = tableId,
				IsConfirmed = anyAccepted && !rejected,
				Bets = bets
			});
			break;
		}
		case "baccarat.newGame":
			GameStarted?.Invoke(this, new PpGameStartedArgs
			{
				GameId = args.GetStringOrDefault("gameId"),
				TableId = tableId,
				StartTime = args.GetStringOrDefault("gameNumber")
			});
			break;
		case "baccarat.gameState":
		case "baccarat.tableState":
		{
			JsonElement game = args;
			if (type == "baccarat.tableState" && args.TryGetProperty("currentGame", out var currentGame) && currentGame.ValueKind == JsonValueKind.Object)
			{
				game = currentGame;
				// tableState is the only Evolution frame carrying a readable table name, so it plays
				// the same role PP's tableconfig does: it feeds the 방 설정 catalogue.
				string tableName = args.GetStringOrDefault("tableName");
				if (!string.IsNullOrEmpty(tableName))
				{
					TableConfigReceived?.Invoke(this, new PpTableConfigArgs
					{
						TableId = tableId,
						TableName = tableName,
						TableType = "baccarat",
						Provider = GameProviderNames.EvolutionKey
					});
				}
			}
			string betting = game.GetStringOrDefault("betting");
			string gameId = game.GetStringOrDefault("gameId");
			if (string.Equals(betting, "BetsOpen", StringComparison.OrdinalIgnoreCase))
			{
				// The engine schedules a bet from the countdown, never from BetsOpen alone; without
				// this every round logged "타이머 스킵 (remaining=0ms)". Evolution reports it in
				// milliseconds, PP in seconds.
				if (game.TryGetProperty("timeRemaining", out var remainEl)
					&& remainEl.ValueKind == JsonValueKind.Number
					&& remainEl.TryGetInt32(out var remainMs)
					&& remainMs > 0)
				{
					TimerReceived?.Invoke(this, new PpTimerArgs
					{
						TableId = tableId,
						SecondsRemaining = (int)Math.Round(remainMs / 1000.0)
					});
				}
				BetsOpened?.Invoke(this, new PpTableEventArgs { TableId = tableId, GameId = gameId });
			}
			else if (string.Equals(betting, "BetsClosed", StringComparison.OrdinalIgnoreCase))
			{
				BetsClosed?.Invoke(this, new PpTableEventArgs { TableId = tableId, GameId = gameId });
			}
			break;
		}
		case "baccarat.cardDealt":
		{
			string card = args.GetStringOrDefault("card");
			string spot = args.GetStringOrDefault("spot");
			int running = 0;
			if (args.TryGetProperty("gameData", out var gameData) && gameData.ValueKind == JsonValueKind.Object)
			{
				string handKey = string.Equals(spot, "Player", StringComparison.OrdinalIgnoreCase) ? "playerHand" : "bankerHand";
				if (gameData.TryGetProperty(handKey, out var hand) && hand.ValueKind == JsonValueKind.Object
					&& hand.TryGetProperty("score", out var scoreEl) && scoreEl.TryGetInt32(out var sc))
				{
					running = sc;
				}
			}
			string rank = string.Empty;
			string suit = string.Empty;
			if (card.Length >= 2)
			{
				suit = card.Substring(card.Length - 1);
				rank = card.Substring(0, card.Length - 1);
			}
			var _ph = ReadHandCards(args, "playerHand");
			var _bh = ReadHandCards(args, "bankerHand");
			FileLogger.Log($"[카드진단] table={tableId} card='{card}' spot={spot} P=[{(_ph==null?"null":string.Join(",",_ph))}] B=[{(_bh==null?"null":string.Join(",",_bh))}]", "Info", "PatternMatch");
			CardDealt?.Invoke(this, new PpCardDealtArgs
			{
				TableId = tableId,
				GameId = args.GetStringOrDefault("gameId"),
				CardCode = card,
				Rank = rank,
				Suit = suit,
				Place = spot,
				RunningScore = running,
				PlayerHand = _ph,
				BankerHand = _bh,
				PlayerScore = ReadHandScore(args, "playerHand"),
				BankerScore = ReadHandScore(args, "bankerHand")
			});
			break;
		}
		case "baccarat.resolved":
		{
			if (args.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
			{
				string winnerRaw = result.GetStringOrDefault("winner");
				string winner = winnerRaw.ToLowerInvariant() switch
				{
					"player" => "Player",
					"banker" => "Banker",
					"tie" => "Tie",
					_ => winnerRaw,
				};
				int ps = (result.TryGetProperty("playerScore", out var psEl) && psEl.TryGetInt32(out var pv)) ? pv : 0;
				int bs = (result.TryGetProperty("bankerScore", out var bsEl) && bsEl.TryGetInt32(out var bv)) ? bv : 0;
				bool natural = result.TryGetProperty("natural", out var natEl) && natEl.ValueKind == JsonValueKind.True;
				bool playerPair = result.TryGetProperty("playerPair", out var ppEl) && ppEl.ValueKind == JsonValueKind.True;
				bool bankerPair = result.TryGetProperty("bankerPair", out var bpEl) && bpEl.ValueKind == JsonValueKind.True;
				GameResultReceived?.Invoke(this, new PpGameResultArgs
				{
					Provider = GameProviderNames.EvolutionKey,
					TableId = tableId,
					GameId = args.GetStringOrDefault("gameId"),
					Winner = winner,
					Score = ps + ":" + bs,
					IsNatural = natural,
					PlayerPair = playerPair,
					BankerPair = bankerPair
				});
			}
			break;
		}
		case "baccarat.shoeState":
		case "baccarat.encodedShoeState":
		{
			if (args.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
			{
				int StatInt(string name) => (stats.TryGetProperty(name, out var e) && e.TryGetInt32(out var v)) ? v : 0;
				ShoeSummaryReceived?.Invoke(this, new PpShoeSummaryArgs
				{
					TableId = tableId,
					BankerWinCount = StatInt("bankerWins"),
					PlayerWinCount = StatInt("playerWins"),
					TieCount = StatInt("ties"),
					TotalGames = StatInt("gameCount"),
					BankerPairCount = StatInt("bankerPairs"),
					PlayerPairCount = StatInt("playerPairs")
				});
			}
			// 로드맵(빅로드) 복원: 이 사이트는 개별 resolved 대신 encodedShoeState.history_v2 에 전체
			// 결과 히스토리를 담아 보낸다. winner 순서를 그대로 statistic 복원 이벤트로 흘려 로드맵을 그린다.
			if (args.TryGetProperty("history_v2", out var hist) && hist.ValueKind == JsonValueKind.Array)
			{
				List<string> road = new List<string>();
				foreach (JsonElement it in hist.EnumerateArray())
				{
					if (it.ValueKind != JsonValueKind.Object) { continue; }
					string w = it.GetStringOrDefault("winner");
					string winner = w.ToLowerInvariant() switch
					{
						"player" => "Player",
						"banker" => "Banker",
						"tie" => "Tie",
						_ => null,
					};
					if (winner != null) { road.Add(winner); }
				}
				if (road.Count > 0)
				{
					StatisticReceived?.Invoke(this, new PpStatisticArgs { TableId = tableId, BigRoadHistory = road });
				}
			}
			break;
		}
		}
	}

	private void ParseBetsOpenMessage(JsonElement payload)
	{
		string table = payload.GetStringOrDefault("table");
		string gameId = payload.GetStringOrDefault("game");
		if (!string.IsNullOrEmpty(table))
		{
			BetsOpened?.Invoke(this, new PpTableEventArgs
			{
				TableId = table,
				GameId = gameId
			});
		}
	}

	private void ParseBetsClosingSoonMessage(JsonElement payload)
	{
		string table = payload.GetStringOrDefault("table");
		string gameId = payload.GetStringOrDefault("game");
		if (!string.IsNullOrEmpty(table))
		{
			BetsClosingSoon?.Invoke(this, new PpTableEventArgs
			{
				TableId = table,
				GameId = gameId
			});
		}
	}

	private void ParseBetsClosedMessage(JsonElement payload)
	{
		string table = payload.GetStringOrDefault("table");
		string gameId = payload.GetStringOrDefault("game");
		if (!string.IsNullOrEmpty(table))
		{
			BetsClosed?.Invoke(this, new PpTableEventArgs
			{
				TableId = table,
				GameId = gameId
			});
		}
	}

	private void ParseDealingStartedMessage(JsonElement payload)
	{
		string table = payload.GetStringOrDefault("table");
		if (!string.IsNullOrEmpty(table))
		{
			DealingStarted?.Invoke(this, new PpTableEventArgs
			{
				TableId = table
			});
		}
	}

	private void ParseGameResultMessage(JsonElement payload)
	{
		string table = payload.GetStringOrDefault("table");
		string gameId = payload.GetStringOrDefault("gameId");
		string result = payload.GetStringOrDefault("result");
		string score = payload.GetStringOrDefault("score");
		string natural = payload.GetStringOrDefault("natural");
		string playerPair = payload.GetStringOrDefault("player_pair");
		string bankerPair = payload.GetStringOrDefault("banker_pair");
		if (!string.IsNullOrEmpty(table) && !string.IsNullOrEmpty(result))
		{
			string winner = result.ToLowerInvariant() switch
			{
				"player" => "Player", 
				"banker" => "Banker", 
				"tie" => "Tie", 
				_ => result, 
			};
			GameResultReceived?.Invoke(this, new PpGameResultArgs
			{
				TableId = table,
				GameId = gameId,
				Winner = winner,
				Score = score,
				IsNatural = string.Equals(natural, "true", StringComparison.OrdinalIgnoreCase),
				PlayerPair = string.Equals(playerPair, "true", StringComparison.OrdinalIgnoreCase),
				BankerPair = string.Equals(bankerPair, "true", StringComparison.OrdinalIgnoreCase)
			});
		}
	}

	private void ParseBetMessage(JsonElement payload)
	{
		string table = payload.GetStringOrDefault("table");
		string bcStr = payload.GetStringOrDefault("bc");
		string amountStr = payload.GetStringOrDefault("amount");
		string betcodeStr = payload.GetStringOrDefault("betcode");
		if (!string.IsNullOrEmpty(table))
		{
			bool isConfirmed = string.Equals(bcStr, "true", StringComparison.OrdinalIgnoreCase);
			BetConfirmed?.Invoke(this, new PpBetConfirmArgs
			{
				TableId = table,
				IsConfirmed = isConfirmed,
				Amount = (decimal.TryParse(amountStr, out var amt) ? amt : 0m),
				BetCode = betcodeStr
			});
		}
	}

	private void ParseBetsMessage(JsonElement payload)
	{
		string table = payload.GetStringOrDefault("table");
		string bcStr = payload.GetStringOrDefault("bc");
		if (string.IsNullOrEmpty(table))
		{
			return;
		}
		List<(decimal, string)> bets = new List<(decimal, string)>();
		if (payload.TryGetProperty("bet", out var betArray) && betArray.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in betArray.EnumerateArray())
			{
				decimal amount = (decimal.TryParse(item.GetStringOrDefault("amount"), out var a) ? a : 0m);
				string betcode = item.GetStringOrDefault("betcode");
				bets.Add((amount, betcode));
			}
		}
		BetsConfirmed?.Invoke(this, new PpBetsConfirmArgs
		{
			TableId = table,
			IsConfirmed = string.Equals(bcStr, "true", StringComparison.OrdinalIgnoreCase),
			Bets = bets
		});
	}

	private void ParseWinMessage(JsonElement payload)
	{
		string table = payload.GetStringOrDefault("table");
		string gameId = payload.GetStringOrDefault("gameId");
		string nwbStr = payload.GetStringOrDefault("nwb");
		string winStr = payload.GetStringOrDefault("win");
		if (!string.IsNullOrEmpty(table))
		{
			WinReceived?.Invoke(this, new PpWinArgs
			{
				TableId = table,
				GameId = gameId,
				NetWinBalance = (decimal.TryParse(nwbStr, out var nwb) ? nwb : 0m),
				WinAmount = (decimal.TryParse(winStr, out var win) ? win : 0m)
			});
		}
	}

	private void ParseCardMessage(JsonElement payload)
	{
		string table = payload.GetStringOrDefault("table");
		string gameId = payload.GetStringOrDefault("game");
		string sc = payload.GetStringOrDefault("sc");
		string place = payload.GetStringOrDefault("place");
		string valueStr = payload.GetStringOrDefault("value");
		string cardCountStr = payload.GetStringOrDefault("cardCount");
		if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(place))
		{
			return;
		}
		string text = place.ToLowerInvariant();
		string text2 = ((text == "player") ? "Player" : ((!(text == "banker")) ? place : "Banker"));
		string normalizedPlace = text2;
		string rank = "";
		string suit = "";
		if (!string.IsNullOrEmpty(sc) && sc.Length >= 2)
		{
			if (sc.StartsWith("10"))
			{
				rank = "10";
				suit = ((sc.Length > 2) ? sc[2].ToString() : "");
			}
			else
			{
				rank = sc[0].ToString();
				suit = ((sc.Length > 1) ? sc[1].ToString() : "");
			}
		}
		CardDealt?.Invoke(this, new PpCardDealtArgs
		{
			TableId = table,
			GameId = gameId,
			CardCode = sc,
			Rank = rank,
			Suit = suit,
			Place = normalizedPlace,
			RunningScore = (int.TryParse(valueStr, out var v) ? v : 0),
			CardCount = (int.TryParse(cardCountStr, out var cc) ? cc : 0)
		});
	}

	private void ParseCardIncMessage(JsonElement payload)
	{
		string table = payload.GetStringOrDefault("table");
		string gameId = payload.GetStringOrDefault("game");
		string place = payload.GetStringOrDefault("place");
		string valueStr = payload.GetStringOrDefault("value");
		if (!string.IsNullOrEmpty(table) && !string.IsNullOrEmpty(place))
		{
			string text = place.ToLowerInvariant();
			string text2 = ((text == "player") ? "Player" : ((!(text == "banker")) ? place : "Banker"));
			string normalizedPlace = text2;
			CardIncremented?.Invoke(this, new PpCardIncArgs
			{
				TableId = table,
				GameId = gameId,
				Place = normalizedPlace,
				RunningScore = (int.TryParse(valueStr, out var v) ? v : 0)
			});
		}
	}

	private void ParseSeatMessage(JsonElement payload)
	{
		string userId = payload.GetStringOrDefault("user_id");
		string tableId = payload.GetStringOrDefault("table_id");
		string seatEvent = payload.GetStringOrDefault("event");
		string idle = payload.GetStringOrDefault("idle");
		if (!string.IsNullOrEmpty(userId))
		{
			SeatReceived?.Invoke(this, new PpSeatArgs
			{
				UserId = userId,
				TableId = tableId,
				Event = seatEvent,
				IsIdle = string.Equals(idle, "true", StringComparison.OrdinalIgnoreCase)
			});
		}
	}

	private void ParseShoeSummaryMessage(JsonElement payload)
	{
		string table = payload.GetStringOrDefault("table");
		if (!string.IsNullOrEmpty(table))
		{
			ShoeSummaryReceived?.Invoke(this, new PpShoeSummaryArgs
			{
				TableId = table,
				BankerWinCount = (int.TryParse(payload.GetStringOrDefault("bankerWinCounter"), out var bw) ? bw : 0),
				PlayerWinCount = (int.TryParse(payload.GetStringOrDefault("playerWinCounter"), out var pw) ? pw : 0),
				TieCount = (int.TryParse(payload.GetStringOrDefault("tieCounter"), out var tc) ? tc : 0),
				TotalGames = (int.TryParse(payload.GetStringOrDefault("totalGames"), out var tg) ? tg : 0),
				BankerPairCount = (int.TryParse(payload.GetStringOrDefault("bankerPairCounter"), out var bp) ? bp : 0),
				PlayerPairCount = (int.TryParse(payload.GetStringOrDefault("playerPairCounter"), out var pp) ? pp : 0)
			});
		}
	}

	private void ParseStatisticMessage(JsonElement payload)
	{
		string table = payload.GetStringOrDefault("table");
		string valueStr = payload.GetStringOrDefault("value");
		if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(valueStr))
		{
			return;
		}
		try
		{
			using JsonDocument valueDoc = JsonDocument.Parse(valueStr);
			if (!valueDoc.RootElement.TryGetProperty("bigRoad", out var bigRoadArray) || bigRoadArray.ValueKind != JsonValueKind.Array)
			{
				return;
			}
			List<string> history = new List<string>();
			foreach (JsonElement column in bigRoadArray.EnumerateArray())
			{
				if (column.ValueKind != JsonValueKind.Array)
				{
					continue;
				}
				foreach (JsonElement item in column.EnumerateArray())
				{
					string cellStr = item.GetString();
					if (!string.IsNullOrEmpty(cellStr) && !(cellStr == "---"))
					{
						string winner = cellStr[0] switch
						{
							'B' => "Banker", 
							'P' => "Player", 
							'T' => "Tie", 
							_ => null, 
						};
						if (winner != null)
						{
							history.Add(winner);
						}
					}
				}
			}
			if (history.Count > 0)
			{
				FileLogger.Log($"[Parser] statistic bigRoad 파싱: table={table}, 히스토리 {history.Count}건");
				StatisticReceived?.Invoke(this, new PpStatisticArgs
				{
					TableId = table,
					BigRoadHistory = history
				});
			}
		}
		catch (JsonException ex)
		{
			FileLogger.Log("[Parser] statistic value 파싱 실패: table=" + table + ", error=" + ex.Message);
		}
	}

	private void ParseTableConfigMessage(JsonElement payload)
	{
		string tableId = payload.GetStringOrDefault("tableId");
		string tableName = payload.GetStringOrDefault("table_name");
		string tableType = payload.GetStringOrDefault("table_type");
		if (!string.IsNullOrEmpty(tableId) && !string.IsNullOrEmpty(tableName))
		{
			FileLogger.Log($"[Parser] tableconfig: tableId={tableId}, table_name={tableName}, table_type={tableType}");
			TableConfigReceived?.Invoke(this, new PpTableConfigArgs
			{
				TableId = tableId,
				TableName = tableName,
				TableType = tableType
			});
		}
	}
}
public class PpGameStartedArgs : EventArgs
{
	public string GameId { get; set; } = string.Empty;

	public string TableId { get; set; } = string.Empty;

	public string StartTime { get; set; } = string.Empty;
}
public class PpTimerArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public int SecondsRemaining { get; set; }
}
public class PpTableEventArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public string GameId { get; set; } = string.Empty;
}
public class PpGameResultArgs : EventArgs
{
	/// <summary>Which site this result came from.</summary>
	public string Provider { get; set; } = GameProviderNames.PragmaticPlayKey;

	public string TableId { get; set; } = string.Empty;

	public string GameId { get; set; } = string.Empty;

	public string Winner { get; set; } = string.Empty;

	public string Score { get; set; } = string.Empty;

	public bool IsNatural { get; set; }

	public bool PlayerPair { get; set; }

	public bool BankerPair { get; set; }
}
public class PpBetConfirmArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public bool IsConfirmed { get; set; }

	public decimal Amount { get; set; }

	public string BetCode { get; set; } = string.Empty;
}
public class PpBetsConfirmArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public bool IsConfirmed { get; set; }

	public List<(decimal Amount, string BetCode)> Bets { get; set; } = new List<(decimal, string)>();
}
public class PpWinArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public string GameId { get; set; } = string.Empty;

	public decimal NetWinBalance { get; set; }

	public decimal WinAmount { get; set; }
}
public class PpShoeSummaryArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public int BankerWinCount { get; set; }

	public int PlayerWinCount { get; set; }

	public int TieCount { get; set; }

	public int TotalGames { get; set; }

	public int BankerPairCount { get; set; }

	public int PlayerPairCount { get; set; }
}
public class PpCardDealtArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public string GameId { get; set; } = string.Empty;

	public string CardCode { get; set; } = string.Empty;

	public string Rank { get; set; } = string.Empty;

	public string Suit { get; set; } = string.Empty;

	public string Place { get; set; } = string.Empty;

	public int RunningScore { get; set; }

	public int CardCount { get; set; }

	/// <summary>
	/// The complete hand as of this frame, when the provider sends one (Evolution does). Null for
	/// providers that only announce the single new card. A snapshot is authoritative: rebuilding the
	/// hand from it is idempotent, so a frame delivered more than once cannot double the cards.
	/// </summary>
	public List<string>? PlayerHand { get; set; }

	public List<string>? BankerHand { get; set; }

	public int PlayerScore { get; set; }

	public int BankerScore { get; set; }
}
public class PpSeatArgs : EventArgs
{
	public string UserId { get; set; } = string.Empty;

	public string TableId { get; set; } = string.Empty;

	public string Event { get; set; } = string.Empty;

	public bool IsIdle { get; set; }
}
public class PpCardIncArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public string GameId { get; set; } = string.Empty;

	public string Place { get; set; } = string.Empty;

	public int RunningScore { get; set; }
}
public class PpStatisticArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public List<string> BigRoadHistory { get; set; } = new List<string>();
}
public class PpTableConfigArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public string TableName { get; set; } = string.Empty;

	public string TableType { get; set; } = string.Empty;

	/// <summary>Which site this table belongs to, so the catalogue files it correctly.</summary>
	public string Provider { get; set; } = GameProviderNames.PragmaticPlayKey;
}
public class PpBetValidationErrorArgs : EventArgs
{
	public string TableId { get; set; } = string.Empty;

	public string Code { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	public string BetCode { get; set; } = string.Empty;

	public decimal Amount { get; set; }

	public bool IsCommissionCodeRejected => string.Equals(Code, "1057", StringComparison.Ordinal);

	public bool IsNoCommissionCodeRejected => string.Equals(Code, "1058", StringComparison.Ordinal);

	public bool IsBelowTableMinimum => string.Equals(Code, "1013", StringComparison.Ordinal);
}
internal static class JsonElementExtensions
{
	public static string GetStringOrDefault(this JsonElement element, string propertyName, string defaultValue = "")
	{
		if (element.TryGetProperty(propertyName, out var prop))
		{
			return prop.ValueKind switch
			{
				JsonValueKind.String => prop.GetString() ?? defaultValue, 
				JsonValueKind.Number => prop.GetRawText(), 
				_ => defaultValue, 
			};
		}
		return defaultValue;
	}
}
