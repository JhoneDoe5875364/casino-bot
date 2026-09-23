using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PragmaticBot.Services;

namespace PragmaticBot;

/// <summary>
/// Asks for the site to open when Chrome is connected. The last address is remembered, so the
/// usual case is just pressing Enter.
/// </summary>
public class SiteUrlPromptForm : Form
{
	private readonly TextBox _txtUrl;

	private readonly Label _lblError;

	/// <summary>The normalised address, or empty when the user chose to connect without opening a page.</summary>
	public string SiteUrl { get; private set; } = string.Empty;

	public SiteUrlPromptForm()
	{
		Text = "사이트 주소";
		Icon = AppIcon.Value;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		StartPosition = FormStartPosition.CenterParent;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		ClientSize = new Size(460, 150);
		BackColor = Color.FromArgb(26, 26, 46);
		ForeColor = Color.White;
		Font = new Font("맑은 고딕", 9f);

		Label lblTitle = new Label
		{
			Text = "사이트 주소를 입력하세요",
			Font = new Font("맑은 고딕", 10f, FontStyle.Bold),
			ForeColor = Color.White,
			AutoSize = true,
			Location = new Point(16, 14)
		};
		Controls.Add(lblTitle);

		_txtUrl = new TextBox
		{
			Location = new Point(16, 44),
			Width = 428,
			Font = new Font("맑은 고딕", 10f),
			Text = LastSiteUrlStore.Load()
		};
		Controls.Add(_txtUrl);

		_lblError = new Label
		{
			ForeColor = Color.FromArgb(255, 120, 120),
			AutoSize = true,
			Location = new Point(16, 76)
		};
		Controls.Add(_lblError);

		Button btnOk = new Button
		{
			Text = "열기",
			Size = new Size(90, 30),
			Location = new Point(258, 106),
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(34, 110, 58),
			ForeColor = Color.White,
			Cursor = Cursors.Hand
		};
		btnOk.FlatAppearance.BorderSize = 0;
		btnOk.Click += BtnOk_Click;
		Controls.Add(btnOk);

		Button btnCancel = new Button
		{
			Text = "취소",
			Size = new Size(90, 30),
			Location = new Point(354, 106),
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(70, 76, 96),
			ForeColor = Color.White,
			Cursor = Cursors.Hand,
			DialogResult = DialogResult.Cancel
		};
		btnCancel.FlatAppearance.BorderSize = 0;
		Controls.Add(btnCancel);

		AcceptButton = btnOk;
		CancelButton = btnCancel;
		Shown += delegate
		{
			_txtUrl.Focus();
			_txtUrl.SelectAll();
		};
	}

	private void BtnOk_Click(object? sender, EventArgs e)
	{
		string raw = _txtUrl.Text.Trim();
		if (raw.Length == 0)
		{
			// Allowed on purpose: connect to Chrome without opening anything, as before.
			SiteUrl = string.Empty;
			DialogResult = DialogResult.OK;
			return;
		}
		string? url = Normalize(raw);
		if (url == null)
		{
			_lblError.Text = "주소 형식이 올바르지 않습니다. 예: https://example.com";
			return;
		}
		SiteUrl = url;
		LastSiteUrlStore.Save(url);
		DialogResult = DialogResult.OK;
	}

	/// <summary>Adds https:// when the scheme is missing; rejects anything that is not http(s).</summary>
	internal static string? Normalize(string raw)
	{
		string candidate = raw.Contains("://", StringComparison.Ordinal) ? raw : "https://" + raw;
		if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
		{
			return null;
		}
		if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
		{
			return null;
		}
		return string.IsNullOrEmpty(uri.Host) ? null : uri.AbsoluteUri;
	}
}

/// <summary>
/// Remembers the last site address under %LOCALAPPDATA%, outside the program folder, so it
/// survives redeploying the bot.
/// </summary>
internal static class LastSiteUrlStore
{
	private static string FilePath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PragmaticBot", "last_site_url.txt");

	public static string Load()
	{
		try
		{
			return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : string.Empty;
		}
		catch (Exception)
		{
			return string.Empty;
		}
	}

	public static void Save(string url)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
			File.WriteAllText(FilePath, url);
		}
		catch (Exception)
		{
		}
	}
}
