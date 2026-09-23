using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PragmaticBot.Services;

namespace PragmaticBot;

/// <summary>
/// 가상베팅 시작 시 '시작 자금'을 입력받는 모달. 실제 돈이 아니라 모의 검증용 잔액이다.
/// 직전에 쓴 금액이 기본값으로 채워진다.
/// </summary>
public class VirtualStartPromptForm : Form
{
	private readonly TextBox _txt;

	private readonly Label _lblError;

	/// <summary>입력된 시작 자금(원).</summary>
	public decimal Amount { get; private set; }

	public VirtualStartPromptForm(decimal defaultAmount)
	{
		Text = "가상베팅 시작 자금";
		Icon = AppIcon.Value;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		StartPosition = FormStartPosition.CenterParent;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		ClientSize = new Size(420, 172);
		BackColor = Color.FromArgb(26, 26, 46);
		ForeColor = Color.White;
		Font = new Font("맑은 고딕", 9f);

		Label lblTitle = new Label
		{
			Text = "가상베팅 시작 자금을 입력하세요",
			Font = new Font("맑은 고딕", 11f, FontStyle.Bold),
			ForeColor = Color.White,
			AutoSize = true,
			Location = new Point(16, 16)
		};
		Controls.Add(lblTitle);

		Label lblHint = new Label
		{
			Text = "실제 돈이 아닌 모의 검증용 잔액입니다. 이 자금으로 지정한 서식이 수익이 나는지 확인합니다.",
			ForeColor = Color.FromArgb(170, 178, 200),
			AutoSize = false,
			Location = new Point(16, 44),
			Size = new Size(388, 34)
		};
		Controls.Add(lblHint);

		_txt = new TextBox
		{
			Location = new Point(16, 84),
			Size = new Size(388, 28),
			Font = new Font("맑은 고딕", 12f, FontStyle.Bold),
			BackColor = Color.FromArgb(15, 52, 96),
			ForeColor = Color.White,
			BorderStyle = BorderStyle.FixedSingle,
			TextAlign = HorizontalAlignment.Right,
			Text = ((long)(defaultAmount > 0m ? defaultAmount : 1000000m)).ToString("N0", CultureInfo.InvariantCulture)
		};
		Controls.Add(_txt);

		_lblError = new Label
		{
			ForeColor = Color.FromArgb(255, 120, 120),
			AutoSize = false,
			Location = new Point(16, 114),
			Size = new Size(388, 18),
			Text = string.Empty
		};
		Controls.Add(_lblError);

		Button ok = new Button
		{
			Text = "확인",
			Size = new Size(120, 34),
			Location = new Point(164, 128),
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(120, 80, 200),
			ForeColor = Color.White,
			Font = new Font("맑은 고딕", 10f, FontStyle.Bold),
			Cursor = Cursors.Hand
		};
		ok.FlatAppearance.BorderSize = 0;
		ok.Click += delegate { Accept(); };
		Controls.Add(ok);

		Button cancel = new Button
		{
			Text = "취소",
			Size = new Size(100, 34),
			Location = new Point(292, 128),
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(70, 76, 96),
			ForeColor = Color.White,
			Font = new Font("맑은 고딕", 10f, FontStyle.Bold),
			Cursor = Cursors.Hand
		};
		cancel.FlatAppearance.BorderSize = 0;
		cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
		Controls.Add(cancel);

		AcceptButton = ok;
		CancelButton = cancel;
		Shown += delegate { _txt.Focus(); _txt.SelectAll(); };
	}

	private void Accept()
	{
		string raw = (_txt.Text ?? string.Empty).Replace(",", string.Empty).Replace(" ", string.Empty).Trim();
		if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) || value < 1000m)
		{
			_lblError.Text = "1,000원 이상의 숫자를 입력하세요.";
			return;
		}
		Amount = decimal.Truncate(value);
		DialogResult = DialogResult.OK;
		Close();
	}
}
