using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;

namespace PragmaticBot;

public partial class FormatSettingForm
{
	private DataGridView dgvFormats;

	private DataGridViewTextBoxColumn colNumber;

	private DataGridViewTextBoxColumn colText;

	private Button btnSave;

	private Button btnCancel;

	private Label lblTitle;

	private Label lblExample;

	private CheckBox chkPbCross;

	private CheckBox chkBpCrossOnTwoLoss;

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
		this.dgvFormats = new System.Windows.Forms.DataGridView();
		this.colNumber = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.colText = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.btnSave = new System.Windows.Forms.Button();
		this.btnCancel = new System.Windows.Forms.Button();
		this.lblTitle = new System.Windows.Forms.Label();
		this.lblExample = new System.Windows.Forms.Label();
		this.chkPbCross = new System.Windows.Forms.CheckBox();
		this.chkBpCrossOnTwoLoss = new System.Windows.Forms.CheckBox();
		((System.ComponentModel.ISupportInitialize)this.dgvFormats).BeginInit();
		base.SuspendLayout();
		this.lblTitle.AutoSize = true;
		this.lblTitle.Font = new System.Drawing.Font("맑은 고딕", 14f, System.Drawing.FontStyle.Bold);
		this.lblTitle.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
		this.lblTitle.Location = new System.Drawing.Point(12, 12);
		this.lblTitle.Name = "lblTitle";
		this.lblTitle.Size = new System.Drawing.Size(98, 25);
		this.lblTitle.TabIndex = 0;
		this.lblTitle.Text = "서식 설정";
		this.lblExample.AutoSize = true;
		this.lblExample.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblExample.ForeColor = System.Drawing.Color.FromArgb(150, 150, 150);
		this.lblExample.Location = new System.Drawing.Point(115, 16);
		this.lblExample.Name = "lblExample";
		this.lblExample.Size = new System.Drawing.Size(100, 19);
		this.lblExample.TabIndex = 4;
		this.lblExample.Text = "예 : bbpp-p";
		this.chkPbCross.AutoSize = true;
		this.chkPbCross.Font = new System.Drawing.Font("맑은 고딕", 9.5f, System.Drawing.FontStyle.Bold);
		this.chkPbCross.ForeColor = System.Drawing.Color.FromArgb(220, 245, 230);
		this.chkPbCross.Location = new System.Drawing.Point(400, 16);
		this.chkPbCross.Name = "chkPbCross";
		this.chkPbCross.Size = new System.Drawing.Size(176, 22);
		this.chkPbCross.TabIndex = 5;
		this.chkPbCross.Text = "PB교차베팅 (서식 무시)";
		this.chkPbCross.UseVisualStyleBackColor = true;
		this.chkBpCrossOnTwoLoss.AutoSize = true;
		this.chkBpCrossOnTwoLoss.Font = new System.Drawing.Font("맑은 고딕", 9.5f, System.Drawing.FontStyle.Bold);
		this.chkBpCrossOnTwoLoss.ForeColor = System.Drawing.Color.FromArgb(245, 230, 220);
		this.chkBpCrossOnTwoLoss.Location = new System.Drawing.Point(586, 16);
		this.chkBpCrossOnTwoLoss.Name = "chkBpCrossOnTwoLoss";
		this.chkBpCrossOnTwoLoss.Size = new System.Drawing.Size(176, 22);
		this.chkBpCrossOnTwoLoss.TabIndex = 6;
		this.chkBpCrossOnTwoLoss.Text = "2연패시BP교차베팅";
		this.chkBpCrossOnTwoLoss.UseVisualStyleBackColor = true;
		this.dgvFormats.AllowUserToAddRows = false;
		this.dgvFormats.AllowUserToDeleteRows = false;
		this.dgvFormats.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
		this.dgvFormats.BackgroundColor = System.Drawing.Color.FromArgb(50, 50, 55);
		this.dgvFormats.BorderStyle = System.Windows.Forms.BorderStyle.None;
		this.dgvFormats.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
		this.dgvFormats.Columns.AddRange(this.colNumber, this.colText);
		this.dgvFormats.GridColor = System.Drawing.Color.FromArgb(70, 70, 75);
		this.dgvFormats.Location = new System.Drawing.Point(12, 50);
		this.dgvFormats.Name = "dgvFormats";
		this.dgvFormats.RowHeadersVisible = false;
		this.dgvFormats.RowTemplate.Height = 25;
		this.dgvFormats.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
		this.dgvFormats.Size = new System.Drawing.Size(756, 480);
		this.dgvFormats.TabIndex = 1;
		this.colNumber.HeaderText = "번호";
		this.colNumber.Name = "colNumber";
		this.colNumber.ReadOnly = true;
		this.colNumber.Width = 60;
		this.colNumber.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
		this.colText.HeaderText = "텍스트";
		this.colText.Name = "colText";
		this.colText.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
		this.colText.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
		this.btnSave.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
		this.btnSave.BackColor = System.Drawing.Color.FromArgb(15, 52, 96);
		this.btnSave.FlatAppearance.BorderSize = 0;
		this.btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnSave.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnSave.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
		this.btnSave.Location = new System.Drawing.Point(582, 545);
		this.btnSave.Name = "btnSave";
		this.btnSave.Size = new System.Drawing.Size(90, 35);
		this.btnSave.TabIndex = 2;
		this.btnSave.Text = "저장";
		this.btnSave.UseVisualStyleBackColor = false;
		this.btnSave.Click += new System.EventHandler(BtnSave_Click);
		this.btnCancel.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
		this.btnCancel.BackColor = System.Drawing.Color.FromArgb(80, 80, 85);
		this.btnCancel.FlatAppearance.BorderSize = 0;
		this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnCancel.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.btnCancel.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
		this.btnCancel.Location = new System.Drawing.Point(678, 545);
		this.btnCancel.Name = "btnCancel";
		this.btnCancel.Size = new System.Drawing.Size(90, 35);
		this.btnCancel.TabIndex = 3;
		this.btnCancel.Text = "취소";
		this.btnCancel.UseVisualStyleBackColor = false;
		this.btnCancel.Click += new System.EventHandler(BtnCancel_Click);
		base.AutoScaleDimensions = new System.Drawing.SizeF(7f, 15f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		this.BackColor = System.Drawing.Color.FromArgb(45, 45, 50);
		base.ClientSize = new System.Drawing.Size(780, 591);
		base.Controls.Add(this.lblTitle);
		base.Controls.Add(this.lblExample);
		base.Controls.Add(this.chkPbCross);
		base.Controls.Add(this.chkBpCrossOnTwoLoss);
		base.Controls.Add(this.dgvFormats);
		base.Controls.Add(this.btnSave);
		base.Controls.Add(this.btnCancel);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.Name = "FormatSettingForm";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
		this.Text = "서식 설정";
		base.Load += new System.EventHandler(FormatSettingForm_Load);
		((System.ComponentModel.ISupportInitialize)this.dgvFormats).EndInit();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
