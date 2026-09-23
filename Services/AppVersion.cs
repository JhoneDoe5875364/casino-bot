namespace PragmaticBot.Services;

/// <summary>
/// 이 봇의 버전. 서버의 "필요 봇 버전"과 <b>정확히 일치</b>해야 접속·작동한다.
/// 서버 변경으로 봇을 강제 교체해야 할 때 이 값을 올리고, 서버 관리화면의 필요 버전도 같이 올린다.
/// (서버측 기준값은 PragmaticBot.Server.Services.BotVersionInfo.Current 와 맞춘다)
/// </summary>
public static class AppVersion
{
    public const string Current = "1.3.0";
}
