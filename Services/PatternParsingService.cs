using System;
using System.Collections.Generic;
using System.Linq;
using PragmaticBot.Models;

namespace PragmaticBot.Services;

public class PatternParsingService
{
	public BettingPattern Parse(string text, int number = 0)
	{
		BettingPattern pattern = new BettingPattern
		{
			RawText = text,
			Number = number
		};
		if (string.IsNullOrWhiteSpace(text))
		{
			pattern.IsValid = false;
			pattern.ErrorMessage = "빈 패턴";
			return pattern;
		}
		string[] parts = text.ToLower().Trim().Split('-');
		if (parts.Length != 2)
		{
			pattern.IsValid = false;
			pattern.ErrorMessage = "잘못된 형식 (예: pb-p)";
			return pattern;
		}
		string sequenceStr = parts[0];
		if (string.IsNullOrEmpty(sequenceStr))
		{
			pattern.IsValid = false;
			pattern.ErrorMessage = "패턴 시퀀스가 비어있습니다";
			return pattern;
		}
		string text2 = sequenceStr;
		foreach (char c in text2)
		{
			string result = c switch
			{
				'p' => "Player", 
				'b' => "Banker", 
				't' => "Tie", 
				_ => null, 
			};
			if (result == null)
			{
				pattern.IsValid = false;
				pattern.ErrorMessage = $"알 수 없는 문자: {c}";
				return pattern;
			}
			pattern.Sequence.Add(result);
		}
		BettingPattern bettingPattern = pattern;
		bettingPattern.BetTarget = parts[1].Trim() switch
		{
			"p" => "Player", 
			"b" => "Banker", 
			"t" => "Tie", 
			_ => "", 
		};
		if (string.IsNullOrEmpty(pattern.BetTarget))
		{
			pattern.IsValid = false;
			pattern.ErrorMessage = "알 수 없는 베팅 타겟: " + parts[1];
			return pattern;
		}
		pattern.IsValid = true;
		return pattern;
	}

	private static bool IsTailMatched(List<GameResult> history, List<string> sequence)
	{
		if (sequence.Count == 0)
		{
			return false;
		}
		if (history == null || history.Count == 0)
		{
			return false;
		}
		List<string> mainResults = (from r in history
			where r.Winner.Equals("Player", StringComparison.OrdinalIgnoreCase) || r.Winner.Equals("Banker", StringComparison.OrdinalIgnoreCase)
			select r.Winner).ToList();
		if (mainResults.Count < sequence.Count)
		{
			return false;
		}
		return mainResults.Skip(mainResults.Count - sequence.Count).Select((string winner, int i) => string.Equals(winner, sequence[i], StringComparison.OrdinalIgnoreCase)).All((bool matched) => matched);
	}

	public bool IsPatternMatched(List<GameResult> history, BettingPattern pattern)
	{
		if (!pattern.IsValid)
		{
			return false;
		}
		return IsTailMatched(history, pattern.Sequence);
	}

	public BettingPattern? FindMatchingPattern(List<GameResult> history, IEnumerable<BettingPattern> patterns)
	{
		return patterns.FirstOrDefault((BettingPattern p) => IsPatternMatched(history, p));
	}

	public List<BettingPattern> ParseAll(IEnumerable<(int Number, string Text)> formatSettings)
	{
		return (from f in formatSettings
			select Parse(f.Text, f.Number) into p
			where p.IsValid
			select p).ToList();
	}

	public ExceptionPattern ParseExceptionPattern(string text, int number = 0)
	{
		ExceptionPattern pattern = new ExceptionPattern
		{
			RawText = text,
			Number = number
		};
		if (string.IsNullOrWhiteSpace(text))
		{
			pattern.IsValid = false;
			pattern.ErrorMessage = "빈 패턴";
			return pattern;
		}
		string sequenceStr = text.ToLower().Trim();
		if (sequenceStr.Contains('-'))
		{
			pattern.IsValid = false;
			pattern.ErrorMessage = "예외 패턴은 시퀀스만 입력 (예: pppbb)";
			return pattern;
		}
		string text2 = sequenceStr;
		foreach (char c in text2)
		{
			string result = c switch
			{
				'p' => "Player", 
				'b' => "Banker", 
				_ => null, 
			};
			if (result == null)
			{
				pattern.IsValid = false;
				pattern.ErrorMessage = $"알 수 없는 문자: {c} (p/b만 허용)";
				return pattern;
			}
			pattern.Sequence.Add(result);
		}
		if (pattern.Sequence.Count == 0)
		{
			pattern.IsValid = false;
			pattern.ErrorMessage = "시퀀스가 비어있습니다";
			return pattern;
		}
		pattern.IsValid = true;
		return pattern;
	}

	public bool IsExceptionPatternMatched(List<GameResult> history, ExceptionPattern pattern)
	{
		if (!pattern.IsValid)
		{
			return false;
		}
		return IsTailMatched(history, pattern.Sequence);
	}

	public ExceptionPattern? FindMatchingExceptionPattern(List<GameResult> history, IEnumerable<ExceptionPattern> patterns)
	{
		return patterns.FirstOrDefault((ExceptionPattern p) => IsExceptionPatternMatched(history, p));
	}

	public List<ExceptionPattern> ParseAllExceptionPatterns(IEnumerable<(int Number, string? Pattern)> formatSettings)
	{
		return (from f in formatSettings
			where !string.IsNullOrWhiteSpace(f.Pattern)
			select ParseExceptionPattern(f.Pattern, f.Number) into p
			where p.IsValid
			select p).ToList();
	}

	public RoomGatePattern ParseRoomGatePattern(string text, bool isOn, int number = 0)
	{
		RoomGatePattern pattern = new RoomGatePattern
		{
			RawText = text,
			IsOn = isOn,
			Number = number
		};
		if (string.IsNullOrWhiteSpace(text))
		{
			pattern.IsValid = false;
			pattern.ErrorMessage = "빈 패턴";
			return pattern;
		}
		string sequenceStr = text.ToLower().Trim();
		if (sequenceStr.Contains('-'))
		{
			pattern.IsValid = false;
			pattern.ErrorMessage = "게이트 패턴은 시퀀스만 입력 (예: ppbbppp)";
			return pattern;
		}
		string text2 = sequenceStr;
		foreach (char c in text2)
		{
			string result = c switch
			{
				'p' => "Player", 
				'b' => "Banker", 
				_ => null, 
			};
			if (result == null)
			{
				pattern.IsValid = false;
				pattern.ErrorMessage = $"알 수 없는 문자: {c} (p/b만 허용)";
				return pattern;
			}
			pattern.Sequence.Add(result);
		}
		if (pattern.Sequence.Count == 0)
		{
			pattern.IsValid = false;
			pattern.ErrorMessage = "시퀀스가 비어있습니다";
			return pattern;
		}
		pattern.IsValid = true;
		return pattern;
	}

	public bool IsRoomGatePatternMatched(List<GameResult> history, RoomGatePattern pattern)
	{
		if (!pattern.IsValid)
		{
			return false;
		}
		return IsTailMatched(history, pattern.Sequence);
	}

	public RoomGatePattern? FindMatchingRoomGatePattern(List<GameResult> history, IEnumerable<RoomGatePattern> patterns)
	{
		return patterns.FirstOrDefault((RoomGatePattern p) => IsRoomGatePatternMatched(history, p));
	}

	public List<RoomGatePattern> ParseAllRoomGatePatterns(IEnumerable<(int Number, bool IsOn, string? Pattern)> formatSettings)
	{
		return (from f in formatSettings
			where !string.IsNullOrWhiteSpace(f.Pattern)
			select ParseRoomGatePattern(f.Pattern, f.IsOn, f.Number) into p
			where p.IsValid
			orderby p.Sequence.Count descending, p.Number
			select p).ToList();
	}
}
