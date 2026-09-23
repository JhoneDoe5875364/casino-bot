using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;

namespace PragmaticBot;

public partial class ExceptionFormatSettingForm : Form
{
	private readonly ExceptionFormatSettingService _exceptionFormatSettingService;

	private readonly string _userId;

	private IContainer components;

	public ExceptionFormatSettingForm(string userId)
	{
		_exceptionFormatSettingService = new ExceptionFormatSettingService();
		_userId = userId;
		InitializeComponent();
	}

	private async void ExceptionFormatSettingForm_Load(object? sender, EventArgs e)
	{
		await LoadFormatsAsync();
	}

	private async Task LoadFormatsAsync()
	{
		try
		{
			dgvFormats.Rows.Clear();
			for (int i = 1; i <= 20; i++)
			{
				dgvFormats.Rows.Add(i, "");
			}
			foreach (ExceptionFormatDto format in await _exceptionFormatSettingService.GetExceptionFormatsAsync(_userId))
			{
				int rowIndex = format.Number - 1;
				if (rowIndex >= 0 && rowIndex < dgvFormats.Rows.Count)
				{
					dgvFormats.Rows[rowIndex].Cells[1].Value = format.Pattern ?? "";
				}
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("예외 서식 데이터 로드 중 오류가 발생했습니다: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
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
					string pattern = dgvFormats.Rows[i].Cells[1].Value?.ToString();
					formats[number] = pattern;
				}
			}
			await _exceptionFormatSettingService.SaveExceptionFormatsAsync(_userId, formats);
			MessageBox.Show("예외 서식 설정이 저장되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
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
