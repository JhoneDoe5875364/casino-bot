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

public partial class RoomGateFormatSettingForm : Form
{
	private readonly RoomGateFormatSettingService _service;

	private readonly string _userId;

	private IContainer components;

	public RoomGateFormatSettingForm(string userId)
	{
		_service = new RoomGateFormatSettingService();
		_userId = userId;
		InitializeComponent();
	}

	private async void RoomGateFormatSettingForm_Load(object? sender, EventArgs e)
	{
		await LoadFormatsAsync();
	}

	private async Task LoadFormatsAsync()
	{
		try
		{
			FillEmptyRows(dgvOnFormats);
			FillEmptyRows(dgvOffFormats);
			foreach (RoomGateFormatDto format in await _service.GetRoomGateFormatsAsync(_userId))
			{
				DataGridView grid = ((format.GateType == 0) ? dgvOnFormats : dgvOffFormats);
				int rowIndex = format.Number - 1;
				if (rowIndex >= 0 && rowIndex < grid.Rows.Count)
				{
					grid.Rows[rowIndex].Cells[1].Value = format.Pattern ?? "";
				}
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("방 On/Off 서식 로드 중 오류가 발생했습니다: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private static void FillEmptyRows(DataGridView grid)
	{
		grid.Rows.Clear();
		for (int i = 1; i <= 20; i++)
		{
			grid.Rows.Add(i, "");
		}
	}

	private static Dictionary<int, string?> ReadGrid(DataGridView grid)
	{
		return (from DataGridViewRow row in grid.Rows
			where row.Cells[0].Value is int
			select row).ToDictionary((DataGridViewRow row) => (int)row.Cells[0].Value, (DataGridViewRow row) => row.Cells[1].Value?.ToString());
	}

	private static string? FindInvalidPattern(Dictionary<int, string?> formats, string label)
	{
		(int, string) invalid = (from kv in formats
			where !string.IsNullOrWhiteSpace(kv.Value)
			select (Key: kv.Key, Text: kv.Value.Trim().ToLower())).FirstOrDefault<(int, string)>(((int Key, string Text) f) => f.Text.Any(delegate(char c)
		{
			bool flag = ((c == 'b' || c == 'p') ? true : false);
			return !flag;
		}));
		if (invalid.Item2 != null)
		{
			return $"{label} {invalid.Item1}번 '{invalid.Item2}' — p/b만 사용할 수 있습니다.";
		}
		return null;
	}

	private async void BtnSave_Click(object? sender, EventArgs e)
	{
		try
		{
			btnSave.Enabled = false;
			Dictionary<int, string> onFormats = ReadGrid(dgvOnFormats);
			Dictionary<int, string> offFormats = ReadGrid(dgvOffFormats);
			string error = FindInvalidPattern(onFormats, "On 서식") ?? FindInvalidPattern(offFormats, "Off 서식");
			if (error != null)
			{
				MessageBox.Show(error, "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return;
			}
			await _service.SaveRoomGateFormatsAsync(_userId, onFormats, offFormats);
			MessageBox.Show("방 On/Off 서식이 저장되었습니다.\n\n※ 실행 중이면 즉시 적용되며, 모든 방이 닫힌 상태에서 새 서식으로 다시 판정합니다.\n   (마틴/행렬 단계는 그대로 유지됩니다)", "알림", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
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
