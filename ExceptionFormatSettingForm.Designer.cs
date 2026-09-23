using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;

namespace PragmaticBot;

public partial class ExceptionFormatSettingForm
{
	private DataGridView dgvFormats;

	private DataGridViewTextBoxColumn colNumber;

	private DataGridViewTextBoxColumn colPattern;

	private Button btnSave;

	private Button btnCancel;

	private Label lblTitle;

	private Label lblExample;

	private Label lblDescription;

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
		this.colPattern = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.btnSave = new System.Windows.Forms.Button();
		this.btnCancel = new System.Windows.Forms.Button();
		this.lblTitle = new System.Windows.Forms.Label();
		this.lblExample = new System.Windows.Forms.Label();
		this.lblDescription = new System.Windows.Forms.Label();
		((System.ComponentModel.ISupportInitialize)this.dgvFormats).BeginInit();
		base.SuspendLayout();
		this.lblTitle.AutoSize = true;
		this.lblTitle.Font = new System.Drawing.Font("맑은 고딕", 14f, System.Drawing.FontStyle.Bold);
		this.lblTitle.ForeColor = System.Drawing.Color.FromArgb(255, 193, 7);
		this.lblTitle.Location = new System.Drawing.Point(12, 12);
		this.lblTitle.Name = "lblTitle";
		this.lblTitle.Size = new System.Drawing.Size(142, 25);
		this.lblTitle.TabIndex = 0;
		this.lblTitle.Text = "예외 서식 설정";
		this.lblExample.AutoSize = true;
		this.lblExample.Font = new System.Drawing.Font("맑은 고딕", 10f);
		this.lblExample.ForeColor = System.Drawing.Color.FromArgb(150, 150, 150);
		this.lblExample.Location = new System.Drawing.Point(160, 16);
		this.lblExample.Name = "lblExample";
		this.lblExample.Size = new System.Drawing.Size(100, 19);
		this.lblExample.TabIndex = 4;
		this.lblExample.Text = "예 : pppbb";
		this.lblDescription.AutoSize = true;
		this.lblDescription.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.lblDescription.ForeColor = System.Drawing.Color.FromArgb(255, 193, 7);
		this.lblDescription.Location = new System.Drawing.Point(12, 42);
		this.lblDescription.Name = "lblDescription";
		this.lblDescription.Size = new System.Drawing.Size(280, 15);
		this.lblDescription.TabIndex = 5;
		this.lblDescription.Text = "* 예외패턴 매칭 시 해당 턴에서 베팅을 스킵합니다";
		this.dgvFormats.AllowUserToAddRows = false;
		this.dgvFormats.AllowUserToDeleteRows = false;
		this.dgvFormats.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
		this.dgvFormats.BackgroundColor = System.Drawing.Color.FromArgb(50, 50, 55);
		this.dgvFormats.BorderStyle = System.Windows.Forms.BorderStyle.None;
		this.dgvFormats.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
		this.dgvFormats.Columns.AddRange(this.colNumber, this.colPattern);
		this.dgvFormats.GridColor = System.Drawing.Color.FromArgb(70, 70, 75);
		this.dgvFormats.Location = new System.Drawing.Point(12, 65);
		this.dgvFormats.Name = "dgvFormats";
		this.dgvFormats.RowHeadersVisible = false;
		this.dgvFormats.RowTemplate.Height = 25;
		this.dgvFormats.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
		this.dgvFormats.Size = new System.Drawing.Size(564, 380);
		this.dgvFormats.TabIndex = 1;
		this.colNumber.HeaderText = "번호";
		this.colNumber.Name = "colNumber";
		this.colNumber.ReadOnly = true;
		this.colNumber.Width = 60;
		this.colNumber.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
		this.colPattern.HeaderText = "예외 패턴 (p/b만 사용)";
		this.colPattern.Name = "colPattern";
		this.colPattern.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
		this.colPattern.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
		this.btnSave.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
		this.btnSave.BackColor = System.Drawing.Color.FromArgb(255, 193, 7);
		this.btnSave.FlatAppearance.BorderSize = 0;
		this.btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnSave.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnSave.ForeColor = System.Drawing.Color.FromArgb(30, 30, 30);
		this.btnSave.Location = new System.Drawing.Point(390, 460);
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
		this.btnCancel.Location = new System.Drawing.Point(486, 460);
		this.btnCancel.Name = "btnCancel";
		this.btnCancel.Size = new System.Drawing.Size(90, 35);
		this.btnCancel.TabIndex = 3;
		this.btnCancel.Text = "취소";
		this.btnCancel.UseVisualStyleBackColor = false;
		this.btnCancel.Click += new System.EventHandler(BtnCancel_Click);
		base.AutoScaleDimensions = new System.Drawing.SizeF(7f, 15f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		this.BackColor = System.Drawing.Color.FromArgb(45, 45, 50);
		base.ClientSize = new System.Drawing.Size(588, 510);
		base.Controls.Add(this.lblTitle);
		base.Controls.Add(this.lblExample);
		base.Controls.Add(this.lblDescription);
		base.Controls.Add(this.dgvFormats);
		base.Controls.Add(this.btnSave);
		base.Controls.Add(this.btnCancel);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.Name = "ExceptionFormatSettingForm";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
		this.Text = "예외 서식 설정";
		base.Load += new System.EventHandler(ExceptionFormatSettingForm_Load);
		((System.ComponentModel.ISupportInitialize)this.dgvFormats).EndInit();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
