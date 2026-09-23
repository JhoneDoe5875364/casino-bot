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
