using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;

namespace PragmaticBot;

public partial class FormatSettingForm : Form
{
	private readonly FormatSettingService _formatSettingService;

	private readonly string _userId;

	private IContainer components;

	public bool PbCrossEnabled => chkPbCross.Checked;

	public bool BpCrossOnTwoLossEnabled
	{
		get
		{
			if (chkPbCross.Checked)
			{
				return chkBpCrossOnTwoLoss.Checked;
			}
			return false;
		}
	}

	public FormatSettingForm(string userId, bool pbCrossEnabled = false, bool bpCrossOnTwoLossEnabled = false)
	{
		_formatSettingService = new FormatSettingService();
		_userId = userId;
		InitializeComponent();
		chkPbCross.Checked = pbCrossEnabled;
		chkBpCrossOnTwoLoss.Checked = bpCrossOnTwoLossEnabled;
		chkPbCross.CheckedChanged += delegate
		{
			SyncBpCrossState();
		};
		SyncBpCrossState();
	}

	private void SyncBpCrossState()
	{
		chkBpCrossOnTwoLoss.Enabled = chkPbCross.Checked;
		if (!chkPbCross.Checked)
		{
			chkBpCrossOnTwoLoss.Checked = false;
		}
	}

	private async void FormatSettingForm_Load(object? sender, EventArgs e)
	{
		await LoadFormatsAsync();
	}

	private async Task LoadFormatsAsync()
	{
		try
		{
			dgvFormats.Rows.Clear();
			for (int i = 1; i <= 50; i++)
			{
				dgvFormats.Rows.Add(i, "");
			}
			foreach (FormatSettingDto format in await _formatSettingService.GetUserFormatsAsync(_userId))
			{
				int rowIndex = format.Number - 1;
				if (rowIndex >= 0 && rowIndex < dgvFormats.Rows.Count)
				{
					dgvFormats.Rows[rowIndex].Cells[1].Value = format.Text ?? "";
				}
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("서식 데이터 로드 중 오류가 발생했습니다: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private async void BtnSave_Click(object? sender, EventArgs e)
	{
		try
		{
			btnSave.Enabled = false;
			Dictionary<int, string> formats = new Dictionary<int, string>();
			for (int i = 0; i < dgvFormats.Rows.Count; i++)
			{
				if (dgvFormats.Rows[i].Cells[0].Value is int number)
				{
					string text = dgvFormats.Rows[i].Cells[1].Value?.ToString();
					formats[number] = text;
				}
			}
			await _formatSettingService.SaveUserFormatsAsync(_userId, formats);
			MessageBox.Show("서식 설정이 저장되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			base.DialogResult = DialogResult.OK;
			Close();
		}
		catch (Exception ex)
		{
			MessageBox.Show("저장 중 오류가 발생했습니다: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			btnSave.Enabled = true;
		}
	}

	private void BtnCancel_Click(object? sender, EventArgs e)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}
}
