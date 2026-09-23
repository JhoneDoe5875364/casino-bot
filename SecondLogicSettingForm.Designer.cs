using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PragmaticBot.Models;
using PragmaticBot.Services;

namespace PragmaticBot;

public partial class SecondLogicSettingForm
{
	private Label lblTitle;

	private CheckBox chkEnabled;

	private Label lblBendCount;

	private NumericUpDown numBendCount;

	private Label lblBendSuffix;

	private Label lblMissStreak;

	private NumericUpDown numMissStreak;

	private Label lblMissSuffix;

	private Label lblWinStreak;

	private NumericUpDown numWinStreak;

	private Label lblWinSuffix;

	private Label lblBetType;

	private ComboBox cmbBetAmountType;

	private CheckBox chkCombo;

	private Label lblDDMaxStep;

	private NumericUpDown numDDMaxStep;

	private Label lblDDMaxSuffix;

	private CheckBox chkStreakRiding;

	private CheckBox chkProfitCooldown;

	private Label lblCooldownAmount;

	private NumericUpDown numCooldownAmount;

	private Label lblCooldownAmountSuffix;

	private Label lblCooldownMinutes;

	private NumericUpDown numCooldownMinutes;

	private Label lblCooldownMinutesSuffix;

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
		this.lblBendCount = new System.Windows.Forms.Label();
		this.numBendCount = new System.Windows.Forms.NumericUpDown();
		this.lblBendSuffix = new System.Windows.Forms.Label();
		this.lblMissStreak = new System.Windows.Forms.Label();
		this.numMissStreak = new System.Windows.Forms.NumericUpDown();
		this.lblMissSuffix = new System.Windows.Forms.Label();
		this.lblWinStreak = new System.Windows.Forms.Label();
		this.numWinStreak = new System.Windows.Forms.NumericUpDown();
		this.lblWinSuffix = new System.Windows.Forms.Label();
		this.lblBetType = new System.Windows.Forms.Label();
		this.cmbBetAmountType = new System.Windows.Forms.ComboBox();
		this.chkCombo = new System.Windows.Forms.CheckBox();
		this.lblDDMaxStep = new System.Windows.Forms.Label();
		this.numDDMaxStep = new System.Windows.Forms.NumericUpDown();
		this.lblDDMaxSuffix = new System.Windows.Forms.Label();
		this.chkStreakRiding = new System.Windows.Forms.CheckBox();
		this.chkProfitCooldown = new System.Windows.Forms.CheckBox();
		this.lblCooldownAmount = new System.Windows.Forms.Label();
		this.numCooldownAmount = new System.Windows.Forms.NumericUpDown();
		this.lblCooldownAmountSuffix = new System.Windows.Forms.Label();
		this.lblCooldownMinutes = new System.Windows.Forms.Label();
		this.numCooldownMinutes = new System.Windows.Forms.NumericUpDown();
		this.lblCooldownMinutesSuffix = new System.Windows.Forms.Label();
		this.lblDescription = new System.Windows.Forms.Label();
		this.btnSave = new System.Windows.Forms.Button();
		this.btnCancel = new System.Windows.Forms.Button();
		((System.ComponentModel.ISupportInitialize)this.numBendCount).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numMissStreak).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numWinStreak).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numDDMaxStep).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numCooldownAmount).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numCooldownMinutes).BeginInit();
		base.SuspendLayout();
		this.lblTitle.AutoSize = true;
		this.lblTitle.Font = new System.Drawing.Font("맑은 고딕", 12f, System.Drawing.FontStyle.Bold);
		this.lblTitle.Location = new System.Drawing.Point(20, 15);
		this.lblTitle.Name = "lblTitle";
		this.lblTitle.Size = new System.Drawing.Size(160, 21);
		this.lblTitle.Text = "세컨로직 설정";
		this.chkEnabled.AutoSize = true;
		this.chkEnabled.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.chkEnabled.Location = new System.Drawing.Point(22, 52);
		this.chkEnabled.Name = "chkEnabled";
		this.chkEnabled.Size = new System.Drawing.Size(120, 23);
		this.chkEnabled.Text = "세컨로직 사용";
		this.chkEnabled.UseVisualStyleBackColor = true;
		this.lblBendCount.AutoSize = true;
		this.lblBendCount.Enabled = false;
		this.lblBendCount.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblBendCount.Location = new System.Drawing.Point(22, 95);
		this.lblBendCount.Name = "lblBendCount";
		this.lblBendCount.Size = new System.Drawing.Size(150, 19);
		this.lblBendCount.Text = "꺾임 누적 횟수 :";
		this.numBendCount.Enabled = false;
		this.numBendCount.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.numBendCount.Location = new System.Drawing.Point(180, 92);
		this.numBendCount.Minimum = 1m;
		this.numBendCount.Maximum = 50m;
		this.numBendCount.Value = 1m;
		this.numBendCount.Name = "numBendCount";
		this.numBendCount.Size = new System.Drawing.Size(80, 25);
		this.lblBendSuffix.AutoSize = true;
		this.lblBendSuffix.Enabled = false;
		this.lblBendSuffix.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblBendSuffix.Location = new System.Drawing.Point(265, 95);
		this.lblBendSuffix.Name = "lblBendSuffix";
		this.lblBendSuffix.Size = new System.Drawing.Size(160, 19);
		this.lblBendSuffix.Text = "번 (미사용 — 서식 설정 적용)";
		this.lblMissStreak.AutoSize = true;
		this.lblMissStreak.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblMissStreak.Location = new System.Drawing.Point(22, 135);
		this.lblMissStreak.Name = "lblMissStreak";
		this.lblMissStreak.Size = new System.Drawing.Size(120, 19);
		this.lblMissStreak.Text = "연속 미당첨 방이동 :";
		this.numMissStreak.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.numMissStreak.Location = new System.Drawing.Point(180, 132);
		this.numMissStreak.Minimum = 1m;
		this.numMissStreak.Maximum = 50m;
		this.numMissStreak.Value = 5m;
		this.numMissStreak.Name = "numMissStreak";
		this.numMissStreak.Size = new System.Drawing.Size(80, 25);
		this.lblMissSuffix.AutoSize = true;
		this.lblMissSuffix.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblMissSuffix.Location = new System.Drawing.Point(265, 135);
		this.lblMissSuffix.Name = "lblMissSuffix";
		this.lblMissSuffix.Size = new System.Drawing.Size(80, 19);
		this.lblMissSuffix.Text = "회 (1~50)";
		this.lblWinStreak.AutoSize = true;
		this.lblWinStreak.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblWinStreak.Location = new System.Drawing.Point(22, 175);
		this.lblWinStreak.Name = "lblWinStreak";
		this.lblWinStreak.Size = new System.Drawing.Size(120, 19);
		this.lblWinStreak.Text = "연속 당첨 방이동 :";
		this.numWinStreak.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.numWinStreak.Location = new System.Drawing.Point(180, 172);
		this.numWinStreak.Minimum = 1m;
		this.numWinStreak.Maximum = 50m;
		this.numWinStreak.Value = 1m;
		this.numWinStreak.Name = "numWinStreak";
		this.numWinStreak.Size = new System.Drawing.Size(80, 25);
		this.lblWinSuffix.AutoSize = true;
		this.lblWinSuffix.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblWinSuffix.Location = new System.Drawing.Point(265, 175);
		this.lblWinSuffix.Name = "lblWinSuffix";
		this.lblWinSuffix.Size = new System.Drawing.Size(120, 19);
		this.lblWinSuffix.Text = "회 (1=즉시)";
		this.lblBetType.AutoSize = true;
		this.lblBetType.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblBetType.Location = new System.Drawing.Point(22, 215);
		this.lblBetType.Name = "lblBetType";
		this.lblBetType.Size = new System.Drawing.Size(100, 19);
		this.lblBetType.Text = "베팅금 단계 :";
		this.cmbBetAmountType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.cmbBetAmountType.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.cmbBetAmountType.Location = new System.Drawing.Point(180, 212);
		this.cmbBetAmountType.Name = "cmbBetAmountType";
		this.cmbBetAmountType.Size = new System.Drawing.Size(160, 25);
		this.chkCombo.AutoSize = true;
		this.chkCombo.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.chkCombo.Location = new System.Drawing.Point(22, 245);
		this.chkCombo.Name = "chkCombo";
		this.chkCombo.Size = new System.Drawing.Size(280, 23);
		this.chkCombo.Text = "마틴4→달랑 조합 사용 (위 단계 대신)";
		this.chkCombo.UseVisualStyleBackColor = true;
		this.lblDDMaxStep.AutoSize = true;
		this.lblDDMaxStep.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblDDMaxStep.Location = new System.Drawing.Point(22, 281);
		this.lblDDMaxStep.Name = "lblDDMaxStep";
		this.lblDDMaxStep.Size = new System.Drawing.Size(150, 19);
		this.lblDDMaxStep.Text = "달랑베르 최대단계 :";
		this.numDDMaxStep.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.numDDMaxStep.Location = new System.Drawing.Point(180, 278);
		this.numDDMaxStep.Minimum = 0m;
		this.numDDMaxStep.Maximum = 50m;
		this.numDDMaxStep.Value = 0m;
		this.numDDMaxStep.Name = "numDDMaxStep";
		this.numDDMaxStep.Size = new System.Drawing.Size(80, 25);
		this.lblDDMaxSuffix.AutoSize = true;
		this.lblDDMaxSuffix.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblDDMaxSuffix.Location = new System.Drawing.Point(265, 281);
		this.lblDDMaxSuffix.Name = "lblDDMaxSuffix";
		this.lblDDMaxSuffix.Size = new System.Drawing.Size(160, 19);
		this.lblDDMaxSuffix.Text = "단계 (0 = 제한 없음)";
		this.chkStreakRiding.AutoSize = true;
		this.chkStreakRiding.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.chkStreakRiding.Location = new System.Drawing.Point(22, 311);
		this.chkStreakRiding.Name = "chkStreakRiding";
		this.chkStreakRiding.Size = new System.Drawing.Size(330, 23);
		this.chkStreakRiding.Text = "줄타기 사용 (당첨 시 틀릴 때까지 같은 방·같은 곳)";
		this.chkStreakRiding.UseVisualStyleBackColor = true;
		this.chkProfitCooldown.AutoSize = true;
		this.chkProfitCooldown.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.chkProfitCooldown.Location = new System.Drawing.Point(22, 341);
		this.chkProfitCooldown.Name = "chkProfitCooldown";
		this.chkProfitCooldown.Size = new System.Drawing.Size(340, 23);
		this.chkProfitCooldown.Text = "수익 쿨다운 사용 (목표 금액을 따면 일정 시간 베팅 정지)";
		this.chkProfitCooldown.UseVisualStyleBackColor = true;
		this.lblCooldownAmount.AutoSize = true;
		this.lblCooldownAmount.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblCooldownAmount.Location = new System.Drawing.Point(22, 375);
		this.lblCooldownAmount.Name = "lblCooldownAmount";
		this.lblCooldownAmount.Size = new System.Drawing.Size(150, 19);
		this.lblCooldownAmount.Text = "쿨다운 기준 수익 :";
		this.numCooldownAmount.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.numCooldownAmount.Location = new System.Drawing.Point(180, 372);
		this.numCooldownAmount.Minimum = 1m;
		this.numCooldownAmount.Maximum = 100000m;
		this.numCooldownAmount.Increment = 10m;
		this.numCooldownAmount.Value = 100m;
		this.numCooldownAmount.Name = "numCooldownAmount";
		this.numCooldownAmount.Size = new System.Drawing.Size(80, 25);
		this.lblCooldownAmountSuffix.AutoSize = true;
		this.lblCooldownAmountSuffix.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblCooldownAmountSuffix.Location = new System.Drawing.Point(265, 375);
		this.lblCooldownAmountSuffix.Name = "lblCooldownAmountSuffix";
		this.lblCooldownAmountSuffix.Size = new System.Drawing.Size(160, 19);
		this.lblCooldownAmountSuffix.Text = "만원 (100 = 100만원)";
		this.lblCooldownMinutes.AutoSize = true;
		this.lblCooldownMinutes.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblCooldownMinutes.Location = new System.Drawing.Point(22, 408);
		this.lblCooldownMinutes.Name = "lblCooldownMinutes";
		this.lblCooldownMinutes.Size = new System.Drawing.Size(150, 19);
		this.lblCooldownMinutes.Text = "쿨다운 정지 시간 :";
		this.numCooldownMinutes.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.numCooldownMinutes.Location = new System.Drawing.Point(180, 405);
		this.numCooldownMinutes.Minimum = 1m;
		this.numCooldownMinutes.Maximum = 600m;
		this.numCooldownMinutes.Value = 10m;
		this.numCooldownMinutes.Name = "numCooldownMinutes";
		this.numCooldownMinutes.Size = new System.Drawing.Size(80, 25);
		this.lblCooldownMinutesSuffix.AutoSize = true;
		this.lblCooldownMinutesSuffix.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblCooldownMinutesSuffix.Location = new System.Drawing.Point(265, 408);
		this.lblCooldownMinutesSuffix.Name = "lblCooldownMinutesSuffix";
		this.lblCooldownMinutesSuffix.Size = new System.Drawing.Size(160, 19);
		this.lblCooldownMinutesSuffix.Text = "분 (1~600)";
		this.lblDescription.AutoSize = false;
		this.lblDescription.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.lblDescription.ForeColor = System.Drawing.Color.Gray;
		this.lblDescription.Location = new System.Drawing.Point(22, 442);
		this.lblDescription.Name = "lblDescription";
		this.lblDescription.Size = new System.Drawing.Size(400, 195);
		this.lblDescription.Text = "베팅 시점·타겟은 메인 화면의 서식 설정(예외 서식 포함)을 그대로 따릅니다.\n서식이 매칭된 방 하나를 잡고, 그 방에서만 베팅합니다. (꺾임 누적 횟수 미사용)\n미당첨 시 같은 방 재대기(단계 상승), 지정 횟수 연속 미당첨 시 방이동(단계 유지).\n당첨 시 단계 초기화(달랑베르는 1단계 후퇴), 지정 횟수 연속 당첨 시 방이동(1 = 당첨 즉시 이동).\n베팅금 단계는 마틴/홍콩크루즈/달랑베르/행렬을 지원하며, 방이동 시 진행 상태를 그대로 이월합니다.\n홍콩크루즈는 1판 승 → 2배 2판 도전이 한 사이클이며, 2연승 시 이동할 단계는 베팅금 설정 '홍콩크루즈' 탭의 이동단계 컬럼을 따릅니다.\n달랑베르는 베팅금 설정 '달랑베르' 탭의 기본금액 × 단계로 베팅합니다.\n달랑베르 최대단계를 지정하면 그 단계에서 미당첨 시 다음 단계로 올리지 않고 1단계부터 다시 시작하며,\n이때 선현재금액이 그 시점의 현재금액으로 내려갑니다(일시정지·모든방 초기화는 없습니다).\n\n[줄타기] 당첨한 순간 그 자리(P/B)를 고정해 틀릴 때까지 같은 방에 계속 베팅합니다.\n줄타기 중에는 서식·예외 서식을 건너뛰며(줄타기 우선), 타이(무)는 타겟을 그대로 유지합니다.\n미당첨이 나오면 줄타기를 끝내고 즉시 다른 방으로 이동합니다(단계 유지). 연승 제한은 없습니다.\n\n[수익 쿨다운] 기준 잔액 대비 지정 금액을 따면 지정 시간 동안 모든 베팅을 멈춥니다.\n기준 잔액은 시작 잔액에서 출발해 쿨다운이 풀릴 때마다 그 시점 잔액으로 갱신됩니다(롤링).\n즉 100만원을 따서 쉬고, 재개 후 거기서 다시 100만원을 따면 또 쉬는 식으로 반복됩니다.\n쿨다운에 들어가면 고점 갱신과 똑같이 판을 새로 짭니다 — 전 방 1차 1단계, 세컨로직의\n활성 방·연속 카운터·단계 이월값·줄타기 고정이 모두 초기화됩니다.\n상한(익절)·하한(손절)이 먼저 걸리면 쿨다운 대신 자동 베팅이 종료됩니다.";
		this.btnSave.BackColor = System.Drawing.Color.FromArgb(40, 167, 69);
		this.btnSave.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnSave.ForeColor = System.Drawing.Color.White;
		this.btnSave.Location = new System.Drawing.Point(235, 645);
		this.btnSave.Name = "btnSave";
		this.btnSave.Size = new System.Drawing.Size(90, 32);
		this.btnSave.Text = "저장";
		this.btnSave.UseVisualStyleBackColor = false;
		this.btnSave.Click += new System.EventHandler(BtnSave_Click);
		this.btnCancel.BackColor = System.Drawing.Color.FromArgb(108, 117, 125);
		this.btnCancel.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnCancel.ForeColor = System.Drawing.Color.White;
		this.btnCancel.Location = new System.Drawing.Point(332, 645);
		this.btnCancel.Name = "btnCancel";
		this.btnCancel.Size = new System.Drawing.Size(90, 32);
		this.btnCancel.Text = "취소";
		this.btnCancel.UseVisualStyleBackColor = false;
		this.btnCancel.Click += new System.EventHandler(BtnCancel_Click);
		base.AutoScaleDimensions = new System.Drawing.SizeF(7f, 15f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		base.ClientSize = new System.Drawing.Size(444, 695);
		base.Controls.Add(this.lblTitle);
		base.Controls.Add(this.chkEnabled);
		base.Controls.Add(this.lblBendCount);
		base.Controls.Add(this.numBendCount);
		base.Controls.Add(this.lblBendSuffix);
		base.Controls.Add(this.lblMissStreak);
		base.Controls.Add(this.numMissStreak);
		base.Controls.Add(this.lblMissSuffix);
		base.Controls.Add(this.lblWinStreak);
		base.Controls.Add(this.numWinStreak);
		base.Controls.Add(this.lblWinSuffix);
		base.Controls.Add(this.lblBetType);
		base.Controls.Add(this.cmbBetAmountType);
		base.Controls.Add(this.chkCombo);
		base.Controls.Add(this.lblDDMaxStep);
		base.Controls.Add(this.numDDMaxStep);
		base.Controls.Add(this.lblDDMaxSuffix);
		base.Controls.Add(this.chkStreakRiding);
		base.Controls.Add(this.chkProfitCooldown);
		base.Controls.Add(this.lblCooldownAmount);
		base.Controls.Add(this.numCooldownAmount);
		base.Controls.Add(this.lblCooldownAmountSuffix);
		base.Controls.Add(this.lblCooldownMinutes);
		base.Controls.Add(this.numCooldownMinutes);
		base.Controls.Add(this.lblCooldownMinutesSuffix);
		base.Controls.Add(this.lblDescription);
		base.Controls.Add(this.btnSave);
		base.Controls.Add(this.btnCancel);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.Name = "SecondLogicSettingForm";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
		this.Text = "세컨로직 설정";
		base.Load += new System.EventHandler(SecondLogicSettingForm_Load);
		((System.ComponentModel.ISupportInitialize)this.numBendCount).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numMissStreak).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numWinStreak).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numDDMaxStep).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numCooldownAmount).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numCooldownMinutes).EndInit();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
