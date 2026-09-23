using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PragmaticBot.Services;

namespace PragmaticBot;

/// <summary>
/// 흩어져 있던 설정 버튼들을 한 곳에 모은 설정 허브(모달). 항목을 누르면 그 설정이 <b>허브 위에 새 모달로</b>
/// 뜨고, 그 모달을 닫으면 다시 이 허브로 돌아온다(중첩 모달). 그래서 설정들을 하나씩 다 진행한 뒤
/// 마지막에 허브를 닫으면 된다. 실제 설정 동작은 호출측(Form1)이 넘긴 Action(기존 핸들러 재사용)이다.
/// <para>
/// 플레이 방·방 설정처럼 "모달이 아니라 메인 화면을 바꾸는" 항목은 CloseHubAfter=true 로 지정하면
/// 실행 후 허브가 닫혀 바뀐 메인 화면이 보이게 한다.
/// </para>
/// </summary>
public class SettingsHubForm : Form
{
	public SettingsHubForm(IReadOnlyList<(string Label, Action Action, bool CloseHubAfter)> items)
	{
		Text = "설정";
		Icon = AppIcon.Value;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		StartPosition = FormStartPosition.CenterParent;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		BackColor = Color.FromArgb(26, 26, 46);
		ForeColor = Color.White;
		Font = new Font("맑은 고딕", 10f);

		const int pad = 16;
		const int btnW = 320;
		const int btnH = 46;
		const int gap = 10;

		Label lblTitle = new Label
		{
			Text = "설정",
			Font = new Font("맑은 고딕", 13f, FontStyle.Bold),
			ForeColor = Color.White,
			AutoSize = true,
			Location = new Point(pad, pad)
		};
		Controls.Add(lblTitle);

		int top = pad + 40;
		for (int i = 0; i < items.Count; i++)
		{
			(string label, Action action, bool closeAfter) = items[i];
			Action capturedAction = action;
			bool capturedClose = closeAfter;
			Button b = new Button
			{
				Text = "   " + label,
				Size = new Size(btnW, btnH),
				Location = new Point(pad, top + i * (btnH + gap)),
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(15, 52, 96),
				ForeColor = Color.FromArgb(234, 234, 234),
				Font = new Font("맑은 고딕", 11f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft,
				Cursor = Cursors.Hand,
				TabIndex = i
			};
			b.FlatAppearance.BorderSize = 0;
			b.FlatAppearance.MouseOverBackColor = Color.FromArgb(34, 110, 58);
			b.Click += delegate
			{
				// 설정 실행: 모달 설정은 이 허브 위에 뜨고, 닫히면 자연히 허브로 복귀한다(중첩 모달).
				try { capturedAction(); }
				catch { }
				// 메인 화면을 바꾸는 항목(플레이 방/방 설정)은 실행 후 허브를 닫아 바뀐 화면이 보이게 한다.
				if (capturedClose)
				{
					DialogResult = DialogResult.OK;
					Close();
				}
			};
			Controls.Add(b);
		}

		int closeTop = top + items.Count * (btnH + gap) + 4;
		Button btnClose = new Button
		{
			Text = "닫기",
			Size = new Size(btnW, 40),
			Location = new Point(pad, closeTop),
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(70, 76, 96),
			ForeColor = Color.White,
			Font = new Font("맑은 고딕", 10.5f, FontStyle.Bold),
			Cursor = Cursors.Hand
		};
		btnClose.FlatAppearance.BorderSize = 0;
		btnClose.Click += delegate
		{
			DialogResult = DialogResult.Cancel;
			Close();
		};
		Controls.Add(btnClose);
		CancelButton = btnClose;

		ClientSize = new Size(pad * 2 + btnW, closeTop + 40 + pad);
	}
}
