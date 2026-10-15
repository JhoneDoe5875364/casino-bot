using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;
using Syncfusion.Windows.Forms.Tools;
using Syncfusion.WinForms.Controls;

namespace PragmaticBot;

public partial class LoginForm : Form
{
	private readonly AuthService _authService;

	private IContainer components;

	public LoginResponse? LoginResult { get; private set; }

	public LoginForm()
	{
		InitializeComponent();
		Icon = AppIcon.Value;
		_authService = new AuthService();
		SetupLogo();
		ShowVersionLabel();
	}

	/// <summary>로그인 화면 우하단에 봇 버전을 표시한다.</summary>
	private void ShowVersionLabel()
	{
		Label lblVersion = new Label
		{
			Name = "lblVersion",
			Text = "v" + AppVersion.Current,
			AutoSize = true,
			ForeColor = Color.FromArgb(120, 130, 150),
			Font = new Font("맑은 고딕", 8.5f),
			Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
			BackColor = Color.Transparent
		};
		Controls.Add(lblVersion);
		lblVersion.Location = new Point(ClientSize.Width - lblVersion.PreferredWidth - 10, ClientSize.Height - lblVersion.PreferredHeight - 6);
		lblVersion.BringToFront();
	}

	/// <summary>타이틀의 "KingTigerBet" 텍스트를 로고 이미지로 교체한다(로고를 못 불러오면 텍스트 유지).</summary>
	private void SetupLogo()
	{
		// 메인창과 동일한 와이드 엠블럼을 쓴다(못 불러오면 기존 로고로 폴백).
		Image img = AppImages.Emblem ?? AppImages.Logo;
		if (img == null || lblTitle == null)
		{
			return;
		}
		lblTitle.Text = string.Empty;
		lblTitle.ImageAlign = ContentAlignment.MiddleCenter;
		// Label 은 이미지를 원본 크기로 그리므로, 타이틀 높이에 맞춰 미리 축소한다.
		// 엠블럼은 가로형(≈3:1)이라 정사각으로 늘리면 찌그러지므로 가로세로비를 그대로 유지한다.
		int h = Math.Max(48, lblTitle.Height - 20);
		double ar = (img.Height > 0) ? ((double)img.Width / img.Height) : 1.0;
		int w = Math.Max(h, (int)Math.Round(h * ar));
		try
		{
			Bitmap scaled = new Bitmap(w, h);
			using (Graphics g = Graphics.FromImage(scaled))
			{
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
				g.DrawImage(img, 0, 0, w, h);
			}
			lblTitle.Image = scaled;
		}
		catch
		{
			lblTitle.Image = img; // 축소 실패 시 원본 이미지를 그대로 사용
		}
	}

	private void LoginForm_Load(object sender, EventArgs e)
	{
		txtUsername.Focus();
		base.AcceptButton = btnLogin;
		base.CancelButton = btnCancel;
	}

	private async void BtnLogin_Click(object sender, EventArgs e)
	{
		string username = txtUsername.Text.Trim();
		string password = txtPassword.Text;
		if (string.IsNullOrEmpty(username))
		{
			MessageBox.Show("사용자명을 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			txtUsername.Focus();
			return;
		}
		if (string.IsNullOrEmpty(password))
		{
			MessageBox.Show("비밀번호를 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			txtPassword.Focus();
			return;
		}
		SetControlsEnabled(enabled: false);
		lblStatus.Text = "로그인 중...";
		lblStatus.ForeColor = Color.Blue;
		try
		{
			LoginResponse result = await _authService.ValidateAsync(username, password);
			if (result != null)
			{
				LoginResult = result;
				base.DialogResult = DialogResult.OK;
				Close();
			}
			else
			{
				lblStatus.Text = "로그인 실패: 사용자명 또는 비밀번호가 올바르지 않습니다.";
				lblStatus.ForeColor = Color.Red;
				txtPassword.Clear();
				txtPassword.Focus();
			}
		}
		catch (ApiSessionConflictException ex)
		{
			lblStatus.Text = ex.Message;
			lblStatus.ForeColor = Color.Red;
			txtPassword.Clear();
			txtPassword.Focus();
		}
		catch (ApiVersionMismatchException ex)
		{
			// 서버가 정한 버전과 불일치 → 업데이트 필요 안내(일반 오류와 구분).
			lblStatus.Text = "봇 버전 불일치 — 업데이트 필요";
			lblStatus.ForeColor = Color.Red;
			MessageBox.Show(ex.Message, "봇 업데이트 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
		catch (Exception ex2)
		{
			// 서버 접속 실패(서버 다운/연결 거부/타임아웃)는 원시 소켓 메시지
			// ("No connection could be made ... 127.0.0.1:5080" 등) 대신 "서버 접속 오류"로 안내한다.
			bool isConnError = ex2 is System.Net.Http.HttpRequestException
				|| ex2 is System.Net.Sockets.SocketException
				|| ex2 is System.Threading.Tasks.TaskCanceledException
				|| ex2 is TimeoutException
				|| ex2.InnerException is System.Net.Sockets.SocketException
				|| (ex2.Message != null && (
					ex2.Message.Contains("refused", StringComparison.OrdinalIgnoreCase)
					|| ex2.Message.Contains("actively", StringComparison.OrdinalIgnoreCase)
					|| ex2.Message.Contains("target machine", StringComparison.OrdinalIgnoreCase)
					|| ex2.Message.Contains("connection could be made", StringComparison.OrdinalIgnoreCase)));
			if (isConnError)
			{
				lblStatus.Text = "서버 접속 오류";
				lblStatus.ForeColor = Color.Red;
				MessageBox.Show("서버에 접속할 수 없습니다.\n\n서버가 실행 중인지 확인해 주세요.", "서버 접속 오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
			else
			{
				lblStatus.Text = "오류 발생";
				lblStatus.ForeColor = Color.Red;
				MessageBox.Show("로그인 중 오류가 발생했습니다:\n" + ex2.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}
		finally
		{
			SetControlsEnabled(enabled: true);
		}
	}

	private void BtnCancel_Click(object sender, EventArgs e)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}

	private void SetControlsEnabled(bool enabled)
	{
		txtUsername.Enabled = enabled;
		txtPassword.Enabled = enabled;
		btnLogin.Enabled = enabled;
		btnCancel.Enabled = enabled;
	}
}
