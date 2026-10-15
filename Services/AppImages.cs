using System;
using System.Drawing;
using System.IO;

namespace PragmaticBot.Services;

/// <summary>
/// The KingTigerBet logo bitmap, shared across the UI.
/// <para>
/// Loaded from the embedded resource first (a single-file publish leaves no logo.png on disk),
/// falling back to a file beside the exe. Loaded once and cached.
/// </para>
/// </summary>
public static class AppImages
{
	private static Image? _logo;

	private static bool _tried;

	private static Image? _emblem;

	private static bool _triedEmblem;

	/// <summary>
	/// 메인 상단바 전용 와이드 엠블럼(Pragmatic Play × Evolution). 로고(logo.png)와 별개의 임베디드
	/// 리소스(emblem.png)에서 로드한다. 로그인·설명서 화면은 기존 로고를 그대로 쓰므로 영향 없음.
	/// 못 불러오면 null → 호출측이 기존 Logo로 폴백한다.
	/// </summary>
	public static Image? Emblem
	{
		get
		{
			if (_triedEmblem)
			{
				return _emblem;
			}
			_triedEmblem = true;
			try
			{
				using Stream? stream = typeof(AppImages).Assembly
					.GetManifestResourceStream("PragmaticBot.emblem.png");
				if (stream != null)
				{
					using var ms = new MemoryStream();
					stream.CopyTo(ms);
					ms.Position = 0;
					_emblem = Image.FromStream(ms);
					return _emblem;
				}
				string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "emblem.png");
				if (File.Exists(path))
				{
					using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
					using var ms = new MemoryStream();
					fs.CopyTo(ms);
					ms.Position = 0;
					_emblem = Image.FromStream(ms);
				}
			}
			catch (Exception ex)
			{
				FileLogger.Log("[AppImages] 엠블럼 로드 실패: " + ex.Message, "Warning");
			}
			return _emblem;
		}
	}

	public static Image? Logo
	{
		get
		{
			if (_tried)
			{
				return _logo;
			}
			_tried = true;
			try
			{
				using Stream? stream = typeof(AppImages).Assembly
					.GetManifestResourceStream("PragmaticBot.logo.png");
				if (stream != null)
				{
					// Copy into memory so the image survives the stream being disposed.
					using var ms = new MemoryStream();
					stream.CopyTo(ms);
					ms.Position = 0;
					_logo = Image.FromStream(ms);
					return _logo;
				}
				string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
				if (File.Exists(path))
				{
					using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
					using var ms = new MemoryStream();
					fs.CopyTo(ms);
					ms.Position = 0;
					_logo = Image.FromStream(ms);
				}
			}
			catch (Exception ex)
			{
				FileLogger.Log("[AppImages] 로고 로드 실패: " + ex.Message, "Warning");
			}
			return _logo;
		}
	}
}
