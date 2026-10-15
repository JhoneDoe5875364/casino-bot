using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;
using Syncfusion.Windows.Forms.Tools;
using Syncfusion.WinForms.Controls;

namespace PragmaticBot;

public partial class LoginForm
{
	private Panel panelOuter;

	private Panel panelMain;

	private Label lblTitle;

	private Label lblUsername;

	private Label lblPassword;

	private TextBoxExt txtUsername;

	private TextBoxExt txtPassword;

	private SfButton btnLogin;

	private SfButton btnCancel;

	private Label lblStatus;

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		this.panelOuter = new System.Windows.Forms.Panel();
		this.lblStatus = new System.Windows.Forms.Label();
		this.panelMain = new System.Windows.Forms.Panel();
		this.btnCancel = new Syncfusion.WinForms.Controls.SfButton();
		this.btnLogin = new Syncfusion.WinForms.Controls.SfButton();
		this.txtPassword = new Syncfusion.Windows.Forms.Tools.TextBoxExt();
		this.txtUsername = new Syncfusion.Windows.Forms.Tools.TextBoxExt();
		this.lblPassword = new System.Windows.Forms.Label();
		this.lblUsername = new System.Windows.Forms.Label();
		this.lblTitle = new System.Windows.Forms.Label();
		this.panelOuter.SuspendLayout();
		this.panelMain.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.txtPassword).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.txtUsername).BeginInit();
		base.SuspendLayout();
		this.panelOuter.BackColor = System.Drawing.Color.FromArgb(22, 33, 62);
		this.panelOuter.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
		this.panelOuter.Controls.Add(this.lblStatus);
		this.panelOuter.Controls.Add(this.panelMain);
		this.panelOuter.Controls.Add(this.lblTitle);
		this.panelOuter.Dock = System.Windows.Forms.DockStyle.Fill;
		this.panelOuter.Location = new System.Drawing.Point(0, 0);
		this.panelOuter.Name = "panelOuter";
		this.panelOuter.Size = new System.Drawing.Size(449, 399);
		this.panelOuter.TabIndex = 0;
		this.lblStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.lblStatus.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.lblStatus.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
		this.lblStatus.Location = new System.Drawing.Point(0, 350);
		this.lblStatus.Name = "lblStatus";
		this.lblStatus.Size = new System.Drawing.Size(447, 47);
		this.lblStatus.TabIndex = 5;
		this.lblStatus.Text = "사용자명과 비밀번호를 입력하세요.";
		this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
		this.panelMain.BackColor = System.Drawing.Color.FromArgb(15, 52, 96);
		this.panelMain.Controls.Add(this.btnCancel);
		this.panelMain.Controls.Add(this.btnLogin);
		this.panelMain.Controls.Add(this.txtPassword);
		this.panelMain.Controls.Add(this.txtUsername);
		this.panelMain.Controls.Add(this.lblPassword);
		this.panelMain.Controls.Add(this.lblUsername);
		this.panelMain.Location = new System.Drawing.Point(32, 139);
		this.panelMain.Name = "panelMain";
		this.panelMain.Size = new System.Drawing.Size(386, 240);
		this.panelMain.TabIndex = 6;
		this.btnCancel.BackColor = System.Drawing.Color.FromArgb(220, 53, 69);
		this.btnCancel.Font = new System.Drawing.Font("맑은 고딕", 11f, System.Drawing.FontStyle.Bold);
		this.btnCancel.ForeColor = System.Drawing.Color.White;
		this.btnCancel.Location = new System.Drawing.Point(199, 184);
		this.btnCancel.Name = "btnCancel";
		this.btnCancel.Size = new System.Drawing.Size(167, 47);
		this.btnCancel.Style.BackColor = System.Drawing.Color.FromArgb(220, 53, 69);
		this.btnCancel.Style.ForeColor = System.Drawing.Color.White;
		this.btnCancel.TabIndex = 4;
		this.btnCancel.Text = "취소";
		this.btnCancel.UseVisualStyleBackColor = false;
		this.btnCancel.Click += new System.EventHandler(BtnCancel_Click);
		this.btnLogin.BackColor = System.Drawing.Color.FromArgb(40, 167, 69);
		this.btnLogin.Font = new System.Drawing.Font("맑은 고딕", 11f, System.Drawing.FontStyle.Bold);
		this.btnLogin.ForeColor = System.Drawing.Color.White;
		this.btnLogin.Location = new System.Drawing.Point(19, 184);
		this.btnLogin.Name = "btnLogin";
		this.btnLogin.Size = new System.Drawing.Size(167, 47);
		this.btnLogin.Style.BackColor = System.Drawing.Color.FromArgb(40, 167, 69);
		this.btnLogin.Style.ForeColor = System.Drawing.Color.White;
		this.btnLogin.TabIndex = 3;
		this.btnLogin.Text = "로그인";
		this.btnLogin.UseVisualStyleBackColor = false;
		this.btnLogin.Click += new System.EventHandler(BtnLogin_Click);
		this.txtPassword.BackColor = System.Drawing.Color.FromArgb(22, 33, 62);
		this.txtPassword.BeforeTouchSize = new System.Drawing.Size(346, 32);
		this.txtPassword.BorderColor = System.Drawing.Color.FromArgb(234, 234, 234);
		this.txtPassword.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
		this.txtPassword.Font = new System.Drawing.Font("맑은 고딕", 11f);
		this.txtPassword.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
		this.txtPassword.Location = new System.Drawing.Point(19, 131);
		this.txtPassword.Name = "txtPassword";
		this.txtPassword.PasswordChar = '●';
		this.txtPassword.Size = new System.Drawing.Size(346, 32);
		this.txtPassword.TabIndex = 2;
		this.txtUsername.BackColor = System.Drawing.Color.FromArgb(22, 33, 62);
		this.txtUsername.BeforeTouchSize = new System.Drawing.Size(346, 32);
		this.txtUsername.BorderColor = System.Drawing.Color.FromArgb(234, 234, 234);
		this.txtUsername.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
		this.txtUsername.Font = new System.Drawing.Font("맑은 고딕", 11f);
		this.txtUsername.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
		this.txtUsername.Location = new System.Drawing.Point(19, 51);
		this.txtUsername.Name = "txtUsername";
		this.txtUsername.Size = new System.Drawing.Size(346, 32);
		this.txtUsername.TabIndex = 1;
		this.lblPassword.AutoSize = true;
		this.lblPassword.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblPassword.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
		this.lblPassword.Location = new System.Drawing.Point(19, 100);
		this.lblPassword.Name = "lblPassword";
		this.lblPassword.Size = new System.Drawing.Size(78, 23);
		this.lblPassword.TabIndex = 5;
		this.lblPassword.Text = "비밀번호";
		this.lblUsername.AutoSize = true;
		this.lblUsername.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblUsername.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
		this.lblUsername.Location = new System.Drawing.Point(19, 20);
		this.lblUsername.Name = "lblUsername";
		this.lblUsername.Size = new System.Drawing.Size(78, 23);
		this.lblUsername.TabIndex = 6;
		this.lblUsername.Text = "사용자명";
		this.lblTitle.Dock = System.Windows.Forms.DockStyle.Top;
		this.lblTitle.Font = new System.Drawing.Font("맑은 고딕", 20f, System.Drawing.FontStyle.Bold);
		this.lblTitle.ForeColor = System.Drawing.Color.FromArgb(255, 165, 0);
		this.lblTitle.Location = new System.Drawing.Point(0, 0);
		this.lblTitle.Name = "lblTitle";
		this.lblTitle.Size = new System.Drawing.Size(447, 132);
		this.lblTitle.TabIndex = 0;
		this.lblTitle.Text = "KingTigerBet";
		this.lblTitle.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
		base.AutoScaleDimensions = new System.Drawing.SizeF(9f, 20f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		this.BackColor = System.Drawing.Color.FromArgb(26, 26, 46);
		base.ClientSize = new System.Drawing.Size(449, 438);
		base.Controls.Add(this.panelOuter);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.Name = "LoginForm";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "KingTigerBet 로그인";
		base.Load += new System.EventHandler(LoginForm_Load);
		this.panelOuter.ResumeLayout(false);
		this.panelMain.ResumeLayout(false);
		this.panelMain.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.txtPassword).EndInit();
		((System.ComponentModel.ISupportInitialize)this.txtUsername).EndInit();
		base.ResumeLayout(false);
	}
}
