using System;

namespace PragmaticBot.Services;

/// <summary>
/// Live-casino providers this account may play. Mirrors the server's enum of the same name; the value
/// travels in <c>LoginResponse.Providers</c> as a bit field.
/// </summary>
[Flags]
public enum GameProviders
{
	None = 0,
	PragmaticPlay = 1,
	Evolution = 2,
	All = PragmaticPlay | Evolution
}

public static class GameProviderNames
{
	/// <summary>Server-side provider key for a single flag.</summary>
	public const string PragmaticPlayKey = "PragmaticPlay";

	public const string EvolutionKey = "Evolution";

	/// <summary>Comma-separated provider keys for the games API query string.</summary>
	public static string ApiList(GameProviders p)
	{
		var parts = new System.Collections.Generic.List<string>();
		if (p.HasFlag(GameProviders.PragmaticPlay))
		{
			parts.Add(PragmaticPlayKey);
		}
		if (p.HasFlag(GameProviders.Evolution))
		{
			parts.Add(EvolutionKey);
		}
		return parts.Count == 0 ? PragmaticPlayKey : string.Join(",", parts);
	}

	public static string Label(GameProviders p)
	{
		return p switch
		{
			GameProviders.All => "전체 (프라그마틱 · 에볼루션)",
			GameProviders.PragmaticPlay => "프라그마틱",
			GameProviders.Evolution => "에볼루션",
			_ => "없음"
		};
	}
}
