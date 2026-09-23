using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;

namespace PragmaticBot;

public partial class RoomGateFormatSettingForm
{
	private Label lblTitle;

	private Label lblDescription;

	private Label lblOnTitle;

	private Label lblOffTitle;

	private DataGridView dgvOnFormats;

	private DataGridViewTextBoxColumn colOnNumber;

	private DataGridViewTextBoxColumn colOnPattern;

	private DataGridView dgvOffFormats;

	private DataGridViewTextBoxColumn colOffNumber;

	private DataGridViewTextBoxColumn colOffPattern;

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
		this.lblDescription = new System.Windows.Forms.Label();
		this.lblOnTitle = new System.Windows.Forms.Label();
		this.lblOffTitle = new System.Windows.Forms.Label();
		this.dgvOnFormats = new System.Windows.Forms.DataGridView();
		this.colOnNumber = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.colOnPattern = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dgvOffFormats = new System.Windows.Forms.DataGridView();
		this.colOffNumber = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.colOffPattern = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.btnSave = new System.Windows.Forms.Button();
		this.btnCancel = new System.Windows.Forms.Button();
		((System.ComponentModel.ISupportInitialize)this.dgvOnFormats).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.dgvOffFormats).BeginInit();
		base.SuspendLayout();
		this.lblTitle.AutoSize = true;
		this.lblTitle.Font = new System.Drawing.Font("맑은 고딕", 14f, System.Drawing.FontStyle.Bold);
		this.lblTitle.ForeColor = System.Drawing.Color.FromArgb(120, 200, 255);
		this.lblTitle.Location = new System.Drawing.Point(12, 12);
		this.lblTitle.Name = "lblTitle";
		this.lblTitle.Size = new System.Drawing.Size(210, 25);
		this.lblTitle.TabIndex = 0;
		this.lblTitle.Text = "방 On/Off 서식 설정";
		this.lblDescription.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.lblDescription.ForeColor = System.Drawing.Color.FromArgb(160, 175, 195);
		this.lblDescription.Location = new System.Drawing.Point(14, 44);
		this.lblDescription.Name = "lblDescription";
		this.lblDescription.Size = new System.Drawing.Size(760, 40);
		this.lblDescription.TabIndex = 1;
		this.lblDescription.Text = "* 슈가 교체되면 모든 방이 Off로 시작하고, On 서식이 걸려야 그 방에 베팅합니다. (예: ppbbppp → On, bbbbppp → Off)\r\n* On·Off가 동시에 걸리면 더 긴 서식이 우선하고, 길이가 같으면 Off가 우선합니다. Off된 방의 마틴/행렬 단계는 그대로 보존되어, 다시 On되면 이어서 진행합니다.";
		this.lblOnTitle.AutoSize = true;
		this.lblOnTitle.Font = new System.Drawing.Font("맑은 고딕", 10f, System.Drawing.FontStyle.Bold);
		this.lblOnTitle.ForeColor = System.Drawing.Color.FromArgb(120, 210, 140);
		this.lblOnTitle.Location = new System.Drawing.Point(14, 92);
		this.lblOnTitle.Name = "lblOnTitle";
		this.lblOnTitle.Size = new System.Drawing.Size(180, 19);
		this.lblOnTitle.TabIndex = 2;
		this.lblOnTitle.Text = "On 서식 (방 열기)";
		this.lblOffTitle.AutoSize = true;
		this.lblOffTitle.Font = new System.Drawing.Font("맑은 고딕", 10f, System.Drawing.FontStyle.Bold);
		this.lblOffTitle.ForeColor = System.Drawing.Color.FromArgb(255, 130, 130);
		this.lblOffTitle.Location = new System.Drawing.Point(400, 92);
		this.lblOffTitle.Name = "lblOffTitle";
		this.lblOffTitle.Size = new System.Drawing.Size(180, 19);
		this.lblOffTitle.TabIndex = 3;
		this.lblOffTitle.Text = "Off 서식 (방 닫기)";
		this.dgvOnFormats.AllowUserToAddRows = false;
		this.dgvOnFormats.AllowUserToDeleteRows = false;
		this.dgvOnFormats.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
		this.dgvOnFormats.BackgroundColor = System.Drawing.Color.FromArgb(50, 50, 55);
		this.dgvOnFormats.BorderStyle = System.Windows.Forms.BorderStyle.None;
		this.dgvOnFormats.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
		this.dgvOnFormats.Columns.AddRange(this.colOnNumber, this.colOnPattern);
		this.dgvOnFormats.GridColor = System.Drawing.Color.FromArgb(70, 70, 75);
		this.dgvOnFormats.Location = new System.Drawing.Point(14, 116);
		this.dgvOnFormats.Name = "dgvOnFormats";
		this.dgvOnFormats.RowHeadersVisible = false;
		this.dgvOnFormats.RowTemplate.Height = 25;
		this.dgvOnFormats.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
		this.dgvOnFormats.Size = new System.Drawing.Size(370, 380);
		this.dgvOnFormats.TabIndex = 4;
		this.colOnNumber.HeaderText = "번호";
		this.colOnNumber.Name = "colOnNumber";
		this.colOnNumber.ReadOnly = true;
		this.colOnNumber.Width = 60;
		this.colOnNumber.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
		this.colOnPattern.HeaderText = "On 패턴 (p/b만 사용)";
		this.colOnPattern.Name = "colOnPattern";
		this.colOnPattern.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
		this.colOnPattern.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
		this.dgvOffFormats.AllowUserToAddRows = false;
		this.dgvOffFormats.AllowUserToDeleteRows = false;
		this.dgvOffFormats.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
		this.dgvOffFormats.BackgroundColor = System.Drawing.Color.FromArgb(50, 50, 55);
		this.dgvOffFormats.BorderStyle = System.Windows.Forms.BorderStyle.None;
		this.dgvOffFormats.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
		this.dgvOffFormats.Columns.AddRange(this.colOffNumber, this.colOffPattern);
		this.dgvOffFormats.GridColor = System.Drawing.Color.FromArgb(70, 70, 75);
		this.dgvOffFormats.Location = new System.Drawing.Point(400, 116);
		this.dgvOffFormats.Name = "dgvOffFormats";
		this.dgvOffFormats.RowHeadersVisible = false;
		this.dgvOffFormats.RowTemplate.Height = 25;
		this.dgvOffFormats.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
		this.dgvOffFormats.Size = new System.Drawing.Size(370, 380);
		this.dgvOffFormats.TabIndex = 5;
		this.colOffNumber.HeaderText = "번호";
		this.colOffNumber.Name = "colOffNumber";
		this.colOffNumber.ReadOnly = true;
		this.colOffNumber.Width = 60;
		this.colOffNumber.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
		this.colOffPattern.HeaderText = "Off 패턴 (p/b만 사용)";
		this.colOffPattern.Name = "colOffPattern";
		this.colOffPattern.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
		this.colOffPattern.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
		this.btnSave.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
		this.btnSave.BackColor = System.Drawing.Color.FromArgb(120, 200, 255);
		this.btnSave.FlatAppearance.BorderSize = 0;
		this.btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnSave.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnSave.ForeColor = System.Drawing.Color.FromArgb(20, 30, 45);
		this.btnSave.Location = new System.Drawing.Point(584, 511);
		this.btnSave.Name = "btnSave";
		this.btnSave.Size = new System.Drawing.Size(90, 35);
		this.btnSave.TabIndex = 6;
		this.btnSave.Text = "저장";
		this.btnSave.UseVisualStyleBackColor = false;
		this.btnSave.Click += new System.EventHandler(BtnSave_Click);
		this.btnCancel.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
		this.btnCancel.BackColor = System.Drawing.Color.FromArgb(80, 80, 85);
		this.btnCancel.FlatAppearance.BorderSize = 0;
		this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnCancel.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.btnCancel.ForeColor = System.Drawing.Color.FromArgb(234, 234, 234);
		this.btnCancel.Location = new System.Drawing.Point(680, 511);
		this.btnCancel.Name = "btnCancel";
		this.btnCancel.Size = new System.Drawing.Size(90, 35);
		this.btnCancel.TabIndex = 7;
		this.btnCancel.Text = "취소";
		this.btnCancel.UseVisualStyleBackColor = false;
		this.btnCancel.Click += new System.EventHandler(BtnCancel_Click);
		base.AutoScaleDimensions = new System.Drawing.SizeF(7f, 15f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		this.BackColor = System.Drawing.Color.FromArgb(45, 45, 50);
		base.ClientSize = new System.Drawing.Size(784, 561);
		base.Controls.Add(this.lblTitle);
		base.Controls.Add(this.lblDescription);
		base.Controls.Add(this.lblOnTitle);
		base.Controls.Add(this.lblOffTitle);
		base.Controls.Add(this.dgvOnFormats);
		base.Controls.Add(this.dgvOffFormats);
		base.Controls.Add(this.btnSave);
		base.Controls.Add(this.btnCancel);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.Name = "RoomGateFormatSettingForm";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
		this.Text = "방 On/Off 서식 설정";
		base.Load += new System.EventHandler(RoomGateFormatSettingForm_Load);
		((System.ComponentModel.ISupportInitialize)this.dgvOnFormats).EndInit();
		((System.ComponentModel.ISupportInitialize)this.dgvOffFormats).EndInit();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
