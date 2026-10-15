using System;
using Microsoft.Extensions.Configuration;

namespace PragmaticBot.Services;

public static class AppConfiguration
{
	private static IConfiguration? _configuration;

	public static IConfiguration Configuration
	{
		get
		{
			if (_configuration == null)
			{
				_configuration = new ConfigurationBuilder().SetBasePath(AppDomain.CurrentDomain.BaseDirectory).AddJsonFile("appsettings.json", optional: false, reloadOnChange: true).AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true)
					.Build();
			}
			return _configuration;
		}
	}

	public static string ApiBaseUrl => Configuration["ApiBaseUrl"] ?? throw new InvalidOperationException("ApiBaseUrl이 설정되지 않았습니다.");

	/// <summary>
	/// 에볼루션 베팅 전송 방식. appsettings.json 의 "EvolutionBetMode" 로 바꾼다.
	/// <list type="bullet">
	/// <item>"dom" (기본, 생략 시) — 지금까지의 화면 클릭 방식. 설정을 건드리지 않으면 동작이 전혀 바뀌지 않는다.</item>
	/// <item>"extension" — 봇이 평문을 익스텐션으로 보내고 익스텐션이 게임 소켓으로 전송(필요하면 암호화).</item>
	/// </list>
	/// </summary>
	public static bool UseEvolutionExtensionBet =>
		string.Equals(Configuration["EvolutionBetMode"], "extension", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// 로컬 파일 로그 기록 여부. appsettings.json 의 "FileLogging" 으로 바꾼다.
	/// 기본은 켜짐(항목이 없거나 값이 이상하면 true). "false" 로 두면 파일에 로그를 떨구지 않는다.
	/// </summary>
	public static bool FileLogging =>
		!bool.TryParse(Configuration["FileLogging"], out bool on) || on;

	/// <summary>
	/// 브라우저 기동 시 익스텐션(--load-extension) 로드 여부. appsettings.json 의 "UseExtension" 으로 바꾼다.
	/// 기본은 켜짐(항목 없거나 값 이상이면 true). "false" 로 두면 익스텐션 없이 CDP 경로로만 동작한다.
	/// (PP 데이터·베팅은 CDP로 되므로 영향 없음. 에볼루션 익스텐션 베팅을 쓸 때만 켜면 된다.)
	/// </summary>
	public static bool UseExtension =>
		!bool.TryParse(Configuration["UseExtension"], out bool on2) || on2;
}
