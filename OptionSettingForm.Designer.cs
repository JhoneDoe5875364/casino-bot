using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PragmaticBot.Models;

namespace PragmaticBot;

public partial class OptionSettingForm
{
	private Label lblTitle;

	private Label lblBettingGroup;

	private CheckBox chkPeakReset;

	private CheckBox chkStepReset;

	private ComboBox cmbBetStep;

	private CheckBox chkIgnorePendingBet;

	private Label lblRoomGroup;

	private CheckBox chkRoomGate;

	private Button btnRoomGateFormat;

	private Label lblRoomGateFormatNote;

	private Label lblRoomGateWarning;

	private CheckBox chkShoeControl;

	private ComboBox cmbShoeControl;

	private Label lblShoeSuffix;

	private Label lblEtcGroup;

	private CheckBox chkStepAlarm;

	private ComboBox cmbStepAlarm;

	private CheckBox chkVirtualBet;

	private Label lblVirtualBetInfo;

	private Label lblSplitGroup;

	private Label lblBetSplit;

	private ComboBox cmbBetSplit;

	private Label lblBetSplitSuffix;

	private Label lblPerSiteLimit;

	private System.Windows.Forms.NumericUpDown nudPerSiteLimit;

	private Label lblPerSiteLimitSuffix;

	private Label lblSplitStart;

	private System.Windows.Forms.NumericUpDown nudSplitStart;

	private Label lblSplitStartSuffix;

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
		this.lblBettingGroup = new System.Windows.Forms.Label();
		this.chkPeakReset = new System.Windows.Forms.CheckBox();
		this.chkStepReset = new System.Windows.Forms.CheckBox();
		this.cmbBetStep = new System.Windows.Forms.ComboBox();
		this.chkIgnorePendingBet = new System.Windows.Forms.CheckBox();
		this.lblRoomGroup = new System.Windows.Forms.Label();
		this.chkRoomGate = new System.Windows.Forms.CheckBox();
		this.btnRoomGateFormat = new System.Windows.Forms.Button();
		this.lblRoomGateFormatNote = new System.Windows.Forms.Label();
		this.lblRoomGateWarning = new System.Windows.Forms.Label();
		this.chkShoeControl = new System.Windows.Forms.CheckBox();
		this.cmbShoeControl = new System.Windows.Forms.ComboBox();
		this.lblShoeSuffix = new System.Windows.Forms.Label();
		this.lblEtcGroup = new System.Windows.Forms.Label();
		this.chkStepAlarm = new System.Windows.Forms.CheckBox();
		this.cmbStepAlarm = new System.Windows.Forms.ComboBox();
		this.chkVirtualBet = new System.Windows.Forms.CheckBox();
		this.lblVirtualBetInfo = new System.Windows.Forms.Label();
		this.lblSplitGroup = new System.Windows.Forms.Label();
		this.lblBetSplit = new System.Windows.Forms.Label();
		this.cmbBetSplit = new System.Windows.Forms.ComboBox();
		this.lblBetSplitSuffix = new System.Windows.Forms.Label();
		this.lblPerSiteLimit = new System.Windows.Forms.Label();
		this.nudPerSiteLimit = new System.Windows.Forms.NumericUpDown();
		this.lblPerSiteLimitSuffix = new System.Windows.Forms.Label();
		this.lblSplitStart = new System.Windows.Forms.Label();
		this.nudSplitStart = new System.Windows.Forms.NumericUpDown();
		this.lblSplitStartSuffix = new System.Windows.Forms.Label();
		this.btnSave = new System.Windows.Forms.Button();
		this.btnCancel = new System.Windows.Forms.Button();
		((System.ComponentModel.ISupportInitialize)this.nudPerSiteLimit).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.nudSplitStart).BeginInit();
		base.SuspendLayout();
		this.lblTitle.AutoSize = true;
		this.lblTitle.Font = new System.Drawing.Font("맑은 고딕", 12f, System.Drawing.FontStyle.Bold);
		this.lblTitle.Location = new System.Drawing.Point(20, 15);
		this.lblTitle.Name = "lblTitle";
		this.lblTitle.Size = new System.Drawing.Size(120, 21);
		this.lblTitle.Text = "옵션 설정";
		this.lblBettingGroup.AutoSize = true;
		this.lblBettingGroup.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.lblBettingGroup.ForeColor = System.Drawing.Color.FromArgb(15, 82, 186);
		this.lblBettingGroup.Location = new System.Drawing.Point(22, 55);
		this.lblBettingGroup.Name = "lblBettingGroup";
		this.lblBettingGroup.Size = new System.Drawing.Size(80, 15);
		this.lblBettingGroup.Text = "단계 초기화";
		this.chkPeakReset.AutoSize = true;
		this.chkPeakReset.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.chkPeakReset.Location = new System.Drawing.Point(30, 78);
		this.chkPeakReset.Name = "chkPeakReset";
		this.chkPeakReset.Size = new System.Drawing.Size(140, 23);
		this.chkPeakReset.Text = "모도 초기화 버전";
		this.chkPeakReset.UseVisualStyleBackColor = true;
		this.chkStepReset.AutoSize = true;
		this.chkStepReset.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.chkStepReset.Location = new System.Drawing.Point(30, 108);
		this.chkStepReset.Name = "chkStepReset";
		this.chkStepReset.Size = new System.Drawing.Size(370, 23);
		this.chkStepReset.Text = "미당첨시 모든방 초기화(단, \"후 현재금액>시작금액\")";
		this.chkStepReset.UseVisualStyleBackColor = true;
		this.cmbBetStep.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.cmbBetStep.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.cmbBetStep.Location = new System.Drawing.Point(410, 106);
		this.cmbBetStep.Name = "cmbBetStep";
		this.cmbBetStep.Size = new System.Drawing.Size(120, 25);
		this.chkIgnorePendingBet.AutoSize = true;
		this.chkIgnorePendingBet.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.chkIgnorePendingBet.Location = new System.Drawing.Point(30, 138);
		this.chkIgnorePendingBet.Name = "chkIgnorePendingBet";
		this.chkIgnorePendingBet.Size = new System.Drawing.Size(150, 23);
		this.chkIgnorePendingBet.Text = "진행중 베팅금무시";
		this.chkIgnorePendingBet.UseVisualStyleBackColor = true;
		this.lblRoomGroup.AutoSize = true;
		this.lblRoomGroup.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.lblRoomGroup.ForeColor = System.Drawing.Color.FromArgb(15, 82, 186);
		this.lblRoomGroup.Location = new System.Drawing.Point(22, 180);
		this.lblRoomGroup.Name = "lblRoomGroup";
		this.lblRoomGroup.Size = new System.Drawing.Size(80, 15);
		this.lblRoomGroup.Text = "방 운용";
		this.chkRoomGate.AutoSize = true;
		this.chkRoomGate.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.chkRoomGate.Location = new System.Drawing.Point(30, 206);
		this.chkRoomGate.Name = "chkRoomGate";
		this.chkRoomGate.Size = new System.Drawing.Size(140, 23);
		this.chkRoomGate.Text = "방 On/Off 서식";
		this.chkRoomGate.UseVisualStyleBackColor = true;
		this.btnRoomGateFormat.BackColor = System.Drawing.Color.FromArgb(23, 105, 170);
		this.btnRoomGateFormat.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnRoomGateFormat.ForeColor = System.Drawing.Color.White;
		this.btnRoomGateFormat.Location = new System.Drawing.Point(180, 201);
		this.btnRoomGateFormat.Name = "btnRoomGateFormat";
		this.btnRoomGateFormat.Size = new System.Drawing.Size(130, 32);
		this.btnRoomGateFormat.Text = "방On/Off서식";
		this.btnRoomGateFormat.UseVisualStyleBackColor = false;
		this.btnRoomGateFormat.Click += new System.EventHandler(BtnRoomGateFormat_Click);
		this.lblRoomGateFormatNote.AutoSize = true;
		this.lblRoomGateFormatNote.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.lblRoomGateFormatNote.ForeColor = System.Drawing.Color.Gray;
		this.lblRoomGateFormatNote.Location = new System.Drawing.Point(320, 209);
		this.lblRoomGateFormatNote.Name = "lblRoomGateFormatNote";
		this.lblRoomGateFormatNote.Size = new System.Drawing.Size(280, 15);
		this.lblRoomGateFormatNote.Text = "※ 서식은 그 창의 [저장] 즉시 서버에 반영됩니다";
		this.lblRoomGateWarning.AutoSize = false;
		this.lblRoomGateWarning.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.lblRoomGateWarning.ForeColor = System.Drawing.Color.FromArgb(200, 60, 20);
		this.lblRoomGateWarning.Location = new System.Drawing.Point(30, 239);
		this.lblRoomGateWarning.Name = "lblRoomGateWarning";
		this.lblRoomGateWarning.Size = new System.Drawing.Size(560, 18);
		this.lblRoomGateWarning.Text = string.Empty;
		this.lblRoomGateWarning.Visible = false;
		this.chkShoeControl.AutoSize = true;
		this.chkShoeControl.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.chkShoeControl.Location = new System.Drawing.Point(30, 265);
		this.chkShoeControl.Name = "chkShoeControl";
		this.chkShoeControl.Size = new System.Drawing.Size(100, 23);
		this.chkShoeControl.Text = "슈 컨트롤";
		this.chkShoeControl.UseVisualStyleBackColor = true;
		this.cmbShoeControl.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.cmbShoeControl.Enabled = false;
		this.cmbShoeControl.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.cmbShoeControl.Location = new System.Drawing.Point(180, 263);
		this.cmbShoeControl.Name = "cmbShoeControl";
		this.cmbShoeControl.Size = new System.Drawing.Size(90, 25);
		this.lblShoeSuffix.AutoSize = true;
		this.lblShoeSuffix.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.lblShoeSuffix.ForeColor = System.Drawing.Color.Gray;
		this.lblShoeSuffix.Location = new System.Drawing.Point(278, 267);
		this.lblShoeSuffix.Name = "lblShoeSuffix";
		this.lblShoeSuffix.Size = new System.Drawing.Size(240, 15);
		this.lblShoeSuffix.Text = "판 이상이면 그 방은 사이클 종료 후 베팅하지 않음";
		this.lblEtcGroup.AutoSize = true;
		this.lblEtcGroup.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.lblEtcGroup.ForeColor = System.Drawing.Color.FromArgb(15, 82, 186);
		this.lblEtcGroup.Location = new System.Drawing.Point(22, 307);
		this.lblEtcGroup.Name = "lblEtcGroup";
		this.lblEtcGroup.Size = new System.Drawing.Size(80, 15);
		this.lblEtcGroup.Text = "알람 · 실행";
		this.chkStepAlarm.AutoSize = true;
		this.chkStepAlarm.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.chkStepAlarm.Location = new System.Drawing.Point(30, 330);
		this.chkStepAlarm.Name = "chkStepAlarm";
		this.chkStepAlarm.Size = new System.Drawing.Size(130, 23);
		this.chkStepAlarm.Text = "선택 단계 알람";
		this.chkStepAlarm.UseVisualStyleBackColor = true;
		this.cmbStepAlarm.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.cmbStepAlarm.Enabled = false;
		this.cmbStepAlarm.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.cmbStepAlarm.Location = new System.Drawing.Point(180, 328);
		this.cmbStepAlarm.Name = "cmbStepAlarm";
		this.cmbStepAlarm.Size = new System.Drawing.Size(140, 25);
		this.chkVirtualBet.AutoSize = true;
		this.chkVirtualBet.Font = new System.Drawing.Font("맑은 고딕", 10f, System.Drawing.FontStyle.Bold);
		this.chkVirtualBet.ForeColor = System.Drawing.Color.FromArgb(0, 120, 160);
		this.chkVirtualBet.Location = new System.Drawing.Point(30, 360);
		this.chkVirtualBet.Name = "chkVirtualBet";
		this.chkVirtualBet.Size = new System.Drawing.Size(100, 23);
		this.chkVirtualBet.Text = "가상 베팅";
		this.chkVirtualBet.UseVisualStyleBackColor = true;
		this.lblVirtualBetInfo.AutoSize = false;
		this.lblVirtualBetInfo.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.lblVirtualBetInfo.ForeColor = System.Drawing.Color.Gray;
		this.lblVirtualBetInfo.Location = new System.Drawing.Point(180, 362);
		this.lblVirtualBetInfo.Name = "lblVirtualBetInfo";
		this.lblVirtualBetInfo.Size = new System.Drawing.Size(400, 18);
		this.lblVirtualBetInfo.Text = "실제 베팅을 보내지 않고 결과만 모의로 판정합니다.";
		this.lblSplitGroup.AutoSize = true;
		this.lblSplitGroup.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.lblSplitGroup.ForeColor = System.Drawing.Color.FromArgb(15, 82, 186);
		this.lblSplitGroup.Location = new System.Drawing.Point(22, 362);
		this.lblSplitGroup.Name = "lblSplitGroup";
		this.lblSplitGroup.Size = new System.Drawing.Size(90, 15);
		this.lblSplitGroup.Text = "찢어베팅 설정";
		this.lblBetSplit.AutoSize = true;
		this.lblBetSplit.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblBetSplit.Location = new System.Drawing.Point(30, 388);
		this.lblBetSplit.Name = "lblBetSplit";
		this.lblBetSplit.Size = new System.Drawing.Size(80, 19);
		this.lblBetSplit.Text = "분할개수";
		this.cmbBetSplit.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.cmbBetSplit.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.cmbBetSplit.Items.AddRange("사용안함", "2", "4", "6");
		this.cmbBetSplit.Location = new System.Drawing.Point(180, 385);
		this.cmbBetSplit.Name = "cmbBetSplit";
		this.cmbBetSplit.Size = new System.Drawing.Size(90, 25);
		this.lblBetSplitSuffix.AutoSize = true;
		this.lblBetSplitSuffix.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.lblBetSplitSuffix.ForeColor = System.Drawing.Color.Gray;
		this.lblBetSplitSuffix.Location = new System.Drawing.Point(278, 389);
		this.lblBetSplitSuffix.Name = "lblBetSplitSuffix";
		this.lblBetSplitSuffix.Size = new System.Drawing.Size(240, 15);
		this.lblBetSplitSuffix.Text = "찢어베팅에 쓸 사이트 수 (사용안함 = 1대 단독)";
		this.lblPerSiteLimit.AutoSize = true;
		this.lblPerSiteLimit.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblPerSiteLimit.Location = new System.Drawing.Point(30, 418);
		this.lblPerSiteLimit.Name = "lblPerSiteLimit";
		this.lblPerSiteLimit.Size = new System.Drawing.Size(100, 19);
		this.lblPerSiteLimit.Text = "사이트당 한도";
		this.nudPerSiteLimit.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.nudPerSiteLimit.Location = new System.Drawing.Point(180, 415);
		this.nudPerSiteLimit.Name = "nudPerSiteLimit";
		this.nudPerSiteLimit.Size = new System.Drawing.Size(120, 25);
		this.nudPerSiteLimit.Maximum = new decimal(new int[] { 100000000, 0, 0, 0 });
		this.nudPerSiteLimit.Minimum = new decimal(new int[] { 10000, 0, 0, 0 });
		this.nudPerSiteLimit.Increment = new decimal(new int[] { 10000, 0, 0, 0 });
		this.nudPerSiteLimit.ThousandsSeparator = true;
		this.nudPerSiteLimit.Value = new decimal(new int[] { 5000000, 0, 0, 0 });
		this.lblPerSiteLimitSuffix.AutoSize = true;
		this.lblPerSiteLimitSuffix.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.lblPerSiteLimitSuffix.ForeColor = System.Drawing.Color.Gray;
		this.lblPerSiteLimitSuffix.Location = new System.Drawing.Point(310, 419);
		this.lblPerSiteLimitSuffix.Name = "lblPerSiteLimitSuffix";
		this.lblPerSiteLimitSuffix.Size = new System.Drawing.Size(300, 15);
		this.lblPerSiteLimitSuffix.Text = "원 — 한 사이트 최대(넘으면 다음 사이트로 분할)";
		this.lblSplitStart.AutoSize = true;
		this.lblSplitStart.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblSplitStart.Location = new System.Drawing.Point(30, 451);
		this.lblSplitStart.Name = "lblSplitStart";
		this.lblSplitStart.Size = new System.Drawing.Size(100, 19);
		this.lblSplitStart.Text = "찢어베팅 시작";
		this.nudSplitStart.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.nudSplitStart.Location = new System.Drawing.Point(180, 448);
		this.nudSplitStart.Name = "nudSplitStart";
		this.nudSplitStart.Size = new System.Drawing.Size(120, 25);
		this.nudSplitStart.Maximum = new decimal(new int[] { 100000000, 0, 0, 0 });
		this.nudSplitStart.Minimum = new decimal(new int[] { 10000, 0, 0, 0 });
		this.nudSplitStart.Increment = new decimal(new int[] { 10000, 0, 0, 0 });
		this.nudSplitStart.ThousandsSeparator = true;
		this.nudSplitStart.Value = new decimal(new int[] { 40000, 0, 0, 0 });
		this.lblSplitStartSuffix.AutoSize = true;
		this.lblSplitStartSuffix.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.lblSplitStartSuffix.ForeColor = System.Drawing.Color.Gray;
		this.lblSplitStartSuffix.Location = new System.Drawing.Point(310, 452);
		this.lblSplitStartSuffix.Name = "lblSplitStartSuffix";
		this.lblSplitStartSuffix.Size = new System.Drawing.Size(300, 15);
		this.lblSplitStartSuffix.Text = "원 이상일 때만 분할(미만은 1사이트 단독)";
		this.btnSave.BackColor = System.Drawing.Color.FromArgb(40, 167, 69);
		this.btnSave.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnSave.ForeColor = System.Drawing.Color.White;
		this.btnSave.Location = new System.Drawing.Point(413, 492);
		this.btnSave.Name = "btnSave";
		this.btnSave.Size = new System.Drawing.Size(90, 32);
		this.btnSave.Text = "저장";
		this.btnSave.UseVisualStyleBackColor = false;
		this.btnSave.Click += new System.EventHandler(BtnSave_Click);
		this.btnCancel.BackColor = System.Drawing.Color.FromArgb(108, 117, 125);
		this.btnCancel.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnCancel.ForeColor = System.Drawing.Color.White;
		this.btnCancel.Location = new System.Drawing.Point(510, 492);
		this.btnCancel.Name = "btnCancel";
		this.btnCancel.Size = new System.Drawing.Size(90, 32);
		this.btnCancel.Text = "취소";
		this.btnCancel.UseVisualStyleBackColor = false;
		this.btnCancel.Click += new System.EventHandler(BtnCancel_Click);
		base.AutoScaleDimensions = new System.Drawing.SizeF(7f, 15f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		base.ClientSize = new System.Drawing.Size(624, 544);
		base.Controls.Add(this.lblTitle);
		base.Controls.Add(this.lblBettingGroup);
		base.Controls.Add(this.chkPeakReset);
		base.Controls.Add(this.chkStepReset);
		base.Controls.Add(this.cmbBetStep);
		base.Controls.Add(this.chkIgnorePendingBet);
		base.Controls.Add(this.lblRoomGroup);
		base.Controls.Add(this.chkRoomGate);
		base.Controls.Add(this.btnRoomGateFormat);
		base.Controls.Add(this.lblRoomGateFormatNote);
		base.Controls.Add(this.lblRoomGateWarning);
		base.Controls.Add(this.chkShoeControl);
		base.Controls.Add(this.cmbShoeControl);
		base.Controls.Add(this.lblShoeSuffix);
		base.Controls.Add(this.lblEtcGroup);
		base.Controls.Add(this.chkStepAlarm);
		base.Controls.Add(this.cmbStepAlarm);
		base.Controls.Add(this.lblBetSplit);
		base.Controls.Add(this.cmbBetSplit);
		base.Controls.Add(this.lblSplitGroup);
		base.Controls.Add(this.lblBetSplitSuffix);
		base.Controls.Add(this.lblPerSiteLimit);
		base.Controls.Add(this.nudPerSiteLimit);
		base.Controls.Add(this.lblPerSiteLimitSuffix);
		base.Controls.Add(this.lblSplitStart);
		base.Controls.Add(this.nudSplitStart);
		base.Controls.Add(this.lblSplitStartSuffix);
		base.Controls.Add(this.btnSave);
		base.Controls.Add(this.btnCancel);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.Name = "OptionSettingForm";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
		this.Text = "옵션 설정";
		base.Load += new System.EventHandler(OptionSettingForm_Load);
		((System.ComponentModel.ISupportInitialize)this.nudPerSiteLimit).EndInit();
		((System.ComponentModel.ISupportInitialize)this.nudSplitStart).EndInit();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
