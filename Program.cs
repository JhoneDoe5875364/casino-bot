using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;
using Syncfusion.Licensing;

namespace PragmaticBot;

internal static class Program
{
	[STAThread]
	private static void Main()
	{
		// 파일 로깅 on/off 는 appsettings.json 의 "FileLogging" 으로 정한다(기본 on). 첫 로그보다 먼저 반영.
		try { FileLogSink.Enabled = AppConfiguration.FileLogging; } catch { }

		// 스레드풀 기아 방지 (베팅 지연의 근본 원인 제거).
		// 다수 테이블의 WebSocket 수신 파싱 + WinForms UI 갱신 + 방별 베팅 Task.Run 이 동시에 스레드풀을
		// 점유하면, 기본 min(코어 수)으로는 새 스레드를 약 500ms에 1개씩만 늘려 Task.Delay(베팅 타이머)와
		// 전송 continuation 이 제때 깨어나지 못한다 → 베팅이 늦게 들어가거나 누락된다.
		// min 워커/IO 스레드를 넉넉히 미리 확보해 즉시 스케줄되게 한다.
		int minThreads = Math.Max(64, Environment.ProcessorCount * 8);
		ThreadPool.GetMinThreads(out int curWorker, out int curIo);
		int wantWorker = Math.Max(curWorker, minThreads);
		int wantIo = Math.Max(curIo, minThreads);
		bool poolOk = ThreadPool.SetMinThreads(wantWorker, wantIo);
		FileLogger.Log($"[시작] ThreadPool MinThreads 설정: worker {curWorker}→{wantWorker}, io {curIo}→{wantIo} (적용={poolOk}, 코어={Environment.ProcessorCount})", "Info", "Startup");
		FileLogger.Log($"[시작] 파일 로그 기록 위치: {System.IO.Path.Combine(AppContext.BaseDirectory, "Logs")} (bot_YYYYMMDD_NNN.log, 100KB마다 새 파일)", "Info", "Startup");

		SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjGyl/VkR+XU9Ff1RDX3xKf0x/TGpQb19xflBPallYVBYiSV9jS3hTd0ViW31fdHVQQGReVU91XQ==");
		RegisterGlobalExceptionHandlers();
		ApplicationConfiguration.Initialize();
		ChromeShortcutHelper.CreateDesktopShortcut();
		using LoginForm loginForm = new LoginForm();
		if (loginForm.ShowDialog() != DialogResult.OK || loginForm.LoginResult == null)
		{
			return;
		}
		try
		{
			LoginResponse result = loginForm.LoginResult;
			Application.Run(new Form1(result.UserId, result.UserName));
		}
		catch (Exception ex)
		{
			MessageBox.Show("Form1 로드 오류:\n" + ex.Message + "\n\n" + ex.StackTrace, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			// 종료 시 큐에 남은 로그를 디스크로 마저 떨군다(마지막 라인 유실 방지).
			FileLogSink.FlushNow();
		}
	}

	private static void RegisterGlobalExceptionHandlers()
	{
		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		Application.ThreadException += delegate(object _, ThreadExceptionEventArgs e)
		{
			LogFatal("UI 스레드", e.Exception);
		};
		AppDomain.CurrentDomain.UnhandledException += delegate(object _, UnhandledExceptionEventArgs e)
		{
			LogFatal("백그라운드 스레드", e.ExceptionObject as Exception);
		};
		TaskScheduler.UnobservedTaskException += delegate(object? _, UnobservedTaskExceptionEventArgs e)
		{
			// Closing Chrome tears down the CDP socket while its read loop is still pending, which
			// surfaces here as an aborted I/O operation. That is shutdown, not a fault — logging it
			// as Fatal buried the real errors under hundreds of these.
			if (IsSocketTeardown(e.Exception))
			{
				FileLogger.Log("[Fatal] 미관측 Task: 소켓 종료에 따른 I/O 중단 (무해)", "Info", "Fatal");
			}
			else
			{
				LogFatal("미관측 Task", e.Exception);
			}
			e.SetObserved();
		};
	}

	/// <summary>True when every inner exception is the I/O abort that a closing socket produces.</summary>
	private static bool IsSocketTeardown(AggregateException? ex)
	{
		if (ex == null)
		{
			return false;
		}
		System.Collections.ObjectModel.ReadOnlyCollection<Exception> inner = ex.Flatten().InnerExceptions;
		if (inner.Count == 0)
		{
			return false;
		}
		foreach (Exception e in inner)
		{
			// 995 = ERROR_OPERATION_ABORTED, what a pending socket read raises when the handle closes.
			bool teardown = e is System.Net.Sockets.SocketException
				|| e is System.Net.WebSockets.WebSocketException
				|| e is System.IO.IOException
				|| e is ObjectDisposedException
				|| e is OperationCanceledException;
			if (!teardown)
			{
				return false;
			}
		}
		return true;
	}

	private static void LogFatal(string source, Exception? ex)
	{
		try
		{
			if (ex == null)
			{
				FileLogger.Log("[Fatal] " + source + ": 알 수 없는 예외 (Exception 객체 없음)", "Error", "Fatal");
				return;
			}
			FileLogger.Log($"[Fatal] {source} 미처리 예외: {ex.GetType().FullName}: {ex.Message}", "Error", "Fatal", ex.ToString());
		}
		catch
		{
		}
	}
}
