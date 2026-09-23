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
