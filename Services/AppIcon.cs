using System;
using System.Drawing;
using System.IO;

namespace PragmaticBot.Services;

/// <summary>
/// The KingTigerBet window icon.
/// <para>
/// &lt;ApplicationIcon&gt; only stamps the executable — WinForms windows still show the default icon
/// unless one is assigned, so both forms pull it from here. Loaded once and shared.
/// </para>
/// </summary>
public static class AppIcon
{
	private static Icon? _icon;

	private static bool _tried;

	public static Icon? Value
	{
		get
		{
			if (_tried)
			{
				return _icon;
			}
			_tried = true;
			try
			{
				// embedded resource first — a single-file publish leaves no app.ico on disk
				using Stream? stream = typeof(AppIcon).Assembly
					.GetManifestResourceStream("PragmaticBot.app.ico");
				if (stream != null)
				{
					_icon = new Icon(stream);
					return _icon;
				}
				string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
				if (File.Exists(path))
				{
					_icon = new Icon(path);
				}
			}
			catch (Exception ex)
			{
				FileLogger.Log("[AppIcon] 아이콘 로드 실패: " + ex.Message, "Warning");
			}
			return _icon;
		}
	}
}
