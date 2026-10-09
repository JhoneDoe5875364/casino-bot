namespace PragmaticBot.Services;

public static class FileLogger
{
	public static void Log(string message)
	{
		FileLogSink.Write("Info", null, message);
		DbLogService.Log("Info", null, message);
	}

	public static void Log(string message, string level, string? category = null, string? stackTrace = null, string? additionalData = null)
	{
		FileLogSink.Write(level, category, message);
		DbLogService.Log(level, category, message, stackTrace, additionalData);
	}
}
