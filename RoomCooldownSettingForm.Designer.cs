using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;

namespace PragmaticBot;

public partial class RoomCooldownSettingForm
{
	private Label lblTitle;

	private CheckBox chkEnabled;

	private Label lblLossThreshold;

	private NumericUpDown numLossThreshold;

	private Label lblLossSuffix;

	private Label lblCooldownMinutes;

	private NumericUpDown numCooldownMinutes;

	private Label lblMinuteSuffix;

	private Label lblDescription;

	private Button btnSave;

	private Button btnCancel;

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
		this.lblTitle = new System.Windows.Forms.Label();
		this.chkEnabled = new System.Windows.Forms.CheckBox();
		this.lblLossThreshold = new System.Windows.Forms.Label();
		this.numLossThreshold = new System.Windows.Forms.NumericUpDown();
		this.lblLossSuffix = new System.Windows.Forms.Label();
		this.lblCooldownMinutes = new System.Windows.Forms.Label();
		this.numCooldownMinutes = new System.Windows.Forms.NumericUpDown();
		this.lblMinuteSuffix = new System.Windows.Forms.Label();
		this.lblDescription = new System.Windows.Forms.Label();
		this.btnSave = new System.Windows.Forms.Button();
		this.btnCancel = new System.Windows.Forms.Button();
		((System.ComponentModel.ISupportInitialize)this.numLossThreshold).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numCooldownMinutes).BeginInit();
		base.SuspendLayout();
		this.lblTitle.AutoSize = true;
		this.lblTitle.Font = new System.Drawing.Font("맑은 고딕", 12f, System.Drawing.FontStyle.Bold);
		this.lblTitle.Location = new System.Drawing.Point(20, 15);
		this.lblTitle.Name = "lblTitle";
		this.lblTitle.Size = new System.Drawing.Size(200, 21);
		this.lblTitle.Text = "방 연패 쿨다운 설정";
		this.chkEnabled.AutoSize = true;
		this.chkEnabled.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.chkEnabled.Location = new System.Drawing.Point(22, 52);
		this.chkEnabled.Name = "chkEnabled";
		this.chkEnabled.Size = new System.Drawing.Size(100, 23);
		this.chkEnabled.Text = "기능 활성화";
		this.chkEnabled.UseVisualStyleBackColor = true;
		this.lblLossThreshold.AutoSize = true;
		this.lblLossThreshold.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblLossThreshold.Location = new System.Drawing.Point(22, 95);
		this.lblLossThreshold.Name = "lblLossThreshold";
		this.lblLossThreshold.Size = new System.Drawing.Size(100, 19);
		this.lblLossThreshold.Text = "연패 임계값 :";
		this.numLossThreshold.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.numLossThreshold.Location = new System.Drawing.Point(140, 92);
		this.numLossThreshold.Minimum = 2m;
		this.numLossThreshold.Maximum = 20m;
		this.numLossThreshold.Value = 5m;
		this.numLossThreshold.Name = "numLossThreshold";
		this.numLossThreshold.Size = new System.Drawing.Size(80, 25);
		this.lblLossSuffix.AutoSize = true;
		this.lblLossSuffix.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblLossSuffix.Location = new System.Drawing.Point(225, 95);
		this.lblLossSuffix.Name = "lblLossSuffix";
		this.lblLossSuffix.Size = new System.Drawing.Size(60, 19);
		this.lblLossSuffix.Text = "연패 시 (2~20)";
		this.lblCooldownMinutes.AutoSize = true;
		this.lblCooldownMinutes.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblCooldownMinutes.Location = new System.Drawing.Point(22, 135);
		this.lblCooldownMinutes.Name = "lblCooldownMinutes";
		this.lblCooldownMinutes.Size = new System.Drawing.Size(100, 19);
		this.lblCooldownMinutes.Text = "쿨다운 시간 :";
		this.numCooldownMinutes.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.numCooldownMinutes.Location = new System.Drawing.Point(140, 132);
		this.numCooldownMinutes.Minimum = 1m;
		this.numCooldownMinutes.Maximum = 120m;
		this.numCooldownMinutes.Value = 3m;
		this.numCooldownMinutes.Name = "numCooldownMinutes";
		this.numCooldownMinutes.Size = new System.Drawing.Size(80, 25);
		this.lblMinuteSuffix.AutoSize = true;
		this.lblMinuteSuffix.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblMinuteSuffix.Location = new System.Drawing.Point(225, 135);
		this.lblMinuteSuffix.Name = "lblMinuteSuffix";
		this.lblMinuteSuffix.Size = new System.Drawing.Size(60, 19);
		this.lblMinuteSuffix.Text = "분 (1~120)";
		this.lblDescription.AutoSize = false;
		this.lblDescription.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.lblDescription.ForeColor = System.Drawing.Color.Gray;
		this.lblDescription.Location = new System.Drawing.Point(22, 175);
		this.lblDescription.Name = "lblDescription";
		this.lblDescription.Size = new System.Drawing.Size(380, 50);
		this.lblDescription.Text = "해당 테이블에서 N연패 시 그 테이블만 M분간 쉽니다.\n다른 테이블은 정상 동작합니다. (타이는 카운터 유지)";
		this.btnSave.BackColor = System.Drawing.Color.FromArgb(40, 167, 69);
		this.btnSave.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnSave.ForeColor = System.Drawing.Color.White;
		this.btnSave.Location = new System.Drawing.Point(215, 240);
		this.btnSave.Name = "btnSave";
		this.btnSave.Size = new System.Drawing.Size(90, 32);
		this.btnSave.Text = "저장";
		this.btnSave.UseVisualStyleBackColor = false;
		this.btnSave.Click += new System.EventHandler(BtnSave_Click);
		this.btnCancel.BackColor = System.Drawing.Color.FromArgb(108, 117, 125);
		this.btnCancel.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnCancel.ForeColor = System.Drawing.Color.White;
		this.btnCancel.Location = new System.Drawing.Point(312, 240);
		this.btnCancel.Name = "btnCancel";
		this.btnCancel.Size = new System.Drawing.Size(90, 32);
		this.btnCancel.Text = "취소";
		this.btnCancel.UseVisualStyleBackColor = false;
		this.btnCancel.Click += new System.EventHandler(BtnCancel_Click);
		base.AutoScaleDimensions = new System.Drawing.SizeF(7f, 15f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		base.ClientSize = new System.Drawing.Size(420, 290);
		base.Controls.Add(this.lblTitle);
		base.Controls.Add(this.chkEnabled);
		base.Controls.Add(this.lblLossThreshold);
		base.Controls.Add(this.numLossThreshold);
		base.Controls.Add(this.lblLossSuffix);
		base.Controls.Add(this.lblCooldownMinutes);
		base.Controls.Add(this.numCooldownMinutes);
		base.Controls.Add(this.lblMinuteSuffix);
		base.Controls.Add(this.lblDescription);
		base.Controls.Add(this.btnSave);
		base.Controls.Add(this.btnCancel);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.Name = "RoomCooldownSettingForm";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
		this.Text = "연패 쿨다운 설정";
		base.Load += new System.EventHandler(RoomCooldownSettingForm_Load);
		((System.ComponentModel.ISupportInitialize)this.numLossThreshold).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numCooldownMinutes).EndInit();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
