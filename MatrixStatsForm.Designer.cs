using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;

namespace PragmaticBot;

public partial class MatrixStatsForm
{
	private Panel panelTop;

	private Label lblTypeCaption;

	private ComboBox cmbType;

	private Label lblPeriodCaption;

	private ComboBox cmbPeriod;

	private CheckBox chkIncludeVirtual;

	private Button btnRefresh;

	private Label lblSummary;

	private Label lblLegend;

	private DataGridView dgvStats;

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
		this.panelTop = new System.Windows.Forms.Panel();
		this.lblTypeCaption = new System.Windows.Forms.Label();
		this.cmbType = new System.Windows.Forms.ComboBox();
		this.lblPeriodCaption = new System.Windows.Forms.Label();
		this.cmbPeriod = new System.Windows.Forms.ComboBox();
		this.chkIncludeVirtual = new System.Windows.Forms.CheckBox();
		this.btnRefresh = new System.Windows.Forms.Button();
		this.lblSummary = new System.Windows.Forms.Label();
		this.lblLegend = new System.Windows.Forms.Label();
		this.dgvStats = new System.Windows.Forms.DataGridView();
		this.panelTop.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.dgvStats).BeginInit();
		base.SuspendLayout();
		this.panelTop.BackColor = System.Drawing.Color.FromArgb(22, 33, 62);
		this.panelTop.Controls.Add(this.lblLegend);
		this.panelTop.Controls.Add(this.lblSummary);
		this.panelTop.Controls.Add(this.btnRefresh);
		this.panelTop.Controls.Add(this.chkIncludeVirtual);
		this.panelTop.Controls.Add(this.cmbPeriod);
		this.panelTop.Controls.Add(this.lblPeriodCaption);
		this.panelTop.Controls.Add(this.cmbType);
		this.panelTop.Controls.Add(this.lblTypeCaption);
		this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelTop.Location = new System.Drawing.Point(0, 0);
		this.panelTop.Name = "panelTop";
		this.panelTop.Size = new System.Drawing.Size(1500, 96);
		this.panelTop.TabIndex = 0;
		this.lblTypeCaption.AutoSize = true;
		this.lblTypeCaption.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.lblTypeCaption.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
		this.lblTypeCaption.Location = new System.Drawing.Point(18, 20);
		this.lblTypeCaption.Name = "lblTypeCaption";
		this.lblTypeCaption.Size = new System.Drawing.Size(43, 20);
		this.lblTypeCaption.TabIndex = 0;
		this.lblTypeCaption.Text = "전략";
		this.cmbType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.cmbType.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.cmbType.Location = new System.Drawing.Point(67, 16);
		this.cmbType.Name = "cmbType";
		this.cmbType.Size = new System.Drawing.Size(170, 28);
		this.cmbType.TabIndex = 1;
		this.lblPeriodCaption.AutoSize = true;
		this.lblPeriodCaption.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.lblPeriodCaption.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
		this.lblPeriodCaption.Location = new System.Drawing.Point(259, 20);
		this.lblPeriodCaption.Name = "lblPeriodCaption";
		this.lblPeriodCaption.Size = new System.Drawing.Size(43, 20);
		this.lblPeriodCaption.TabIndex = 2;
		this.lblPeriodCaption.Text = "기간";
		this.cmbPeriod.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.cmbPeriod.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.cmbPeriod.Location = new System.Drawing.Point(308, 16);
		this.cmbPeriod.Name = "cmbPeriod";
		this.cmbPeriod.Size = new System.Drawing.Size(140, 28);
		this.cmbPeriod.TabIndex = 3;
		this.chkIncludeVirtual.AutoSize = true;
		this.chkIncludeVirtual.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.chkIncludeVirtual.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
		this.chkIncludeVirtual.Location = new System.Drawing.Point(470, 19);
		this.chkIncludeVirtual.Name = "chkIncludeVirtual";
		this.chkIncludeVirtual.Size = new System.Drawing.Size(140, 24);
		this.chkIncludeVirtual.TabIndex = 4;
		this.chkIncludeVirtual.Text = "가상 베팅 포함";
		this.chkIncludeVirtual.UseVisualStyleBackColor = true;
		this.btnRefresh.BackColor = System.Drawing.Color.FromArgb(13, 110, 253);
		this.btnRefresh.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnRefresh.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnRefresh.ForeColor = System.Drawing.Color.White;
		this.btnRefresh.Location = new System.Drawing.Point(630, 14);
		this.btnRefresh.Name = "btnRefresh";
		this.btnRefresh.Size = new System.Drawing.Size(110, 32);
		this.btnRefresh.TabIndex = 5;
		this.btnRefresh.Text = "새로고침";
		this.btnRefresh.UseVisualStyleBackColor = false;
		this.lblSummary.AutoSize = true;
		this.lblSummary.Font = new System.Drawing.Font("맑은 고딕", 10f, System.Drawing.FontStyle.Bold);
		this.lblSummary.ForeColor = System.Drawing.Color.FromArgb(0, 200, 255);
		this.lblSummary.Location = new System.Drawing.Point(18, 58);
		this.lblSummary.Name = "lblSummary";
		this.lblSummary.Size = new System.Drawing.Size(0, 23);
		this.lblSummary.TabIndex = 6;
		this.lblLegend.AutoSize = true;
		this.lblLegend.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.lblLegend.ForeColor = System.Drawing.Color.FromArgb(150, 150, 160);
		this.lblLegend.Location = new System.Drawing.Point(770, 22);
		this.lblLegend.Name = "lblLegend";
		this.lblLegend.Size = new System.Drawing.Size(0, 20);
		this.lblLegend.TabIndex = 7;
		this.lblLegend.Text = "셀 표기: 당첨 / 미당첨 · 셀에 마우스를 올리면 타이·베팅금·손익 상세";
		this.dgvStats.AllowUserToAddRows = false;
		this.dgvStats.AllowUserToDeleteRows = false;
		this.dgvStats.AllowUserToResizeRows = false;
		this.dgvStats.BackgroundColor = System.Drawing.Color.FromArgb(30, 41, 70);
		this.dgvStats.BorderStyle = System.Windows.Forms.BorderStyle.None;
		this.dgvStats.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
		this.dgvStats.Dock = System.Windows.Forms.DockStyle.Fill;
		this.dgvStats.EnableHeadersVisualStyles = false;
		this.dgvStats.Location = new System.Drawing.Point(0, 96);
		this.dgvStats.Name = "dgvStats";
		this.dgvStats.ReadOnly = true;
		this.dgvStats.RowHeadersVisible = false;
		this.dgvStats.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
		this.dgvStats.ShowCellToolTips = true;
		this.dgvStats.Size = new System.Drawing.Size(1500, 704);
		this.dgvStats.TabIndex = 1;
		base.AutoScaleDimensions = new System.Drawing.SizeF(8f, 20f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		this.BackColor = System.Drawing.Color.FromArgb(30, 41, 70);
		base.ClientSize = new System.Drawing.Size(1500, 800);
		base.Controls.Add(this.dgvStats);
		base.Controls.Add(this.panelTop);
		this.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.MinimumSize = new System.Drawing.Size(1000, 500);
		base.Name = "MatrixStatsForm";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
		this.Text = "행렬 단계별 당첨/미당첨 통계";
		base.Load += new System.EventHandler(MatrixStatsForm_Load);
		this.panelTop.ResumeLayout(false);
		this.panelTop.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.dgvStats).EndInit();
		base.ResumeLayout(false);
	}
}
