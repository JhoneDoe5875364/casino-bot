using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;

namespace PragmaticBot;

public partial class RoomCooldownSettingForm : Form
{
	private readonly RoomCooldownSettingService _settingService = new RoomCooldownSettingService();

	private readonly string _userId;

	private IContainer components;

	public RoomCooldownSettingForm(string userId)
	{
		_userId = userId;
		InitializeComponent();
	}

	private async void RoomCooldownSettingForm_Load(object? sender, EventArgs e)
	{
		await LoadSettingAsync();
	}

	private async Task LoadSettingAsync()
	{
		try
		{
			RoomCooldownSettingDto dto = await _settingService.GetAsync();
			chkEnabled.Checked = dto.IsEnabled;
			numLossThreshold.Value = Math.Clamp(dto.LossThreshold, (int)numLossThreshold.Minimum, (int)numLossThreshold.Maximum);
			numCooldownMinutes.Value = Math.Clamp(dto.CooldownMinutes, (int)numCooldownMinutes.Minimum, (int)numCooldownMinutes.Maximum);
		}
		catch (Exception ex)
		{
			MessageBox.Show("설정 로드 중 오류가 발생했습니다: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private async void BtnSave_Click(object? sender, EventArgs e)
	{
		try
		{
			btnSave.Enabled = false;
			RoomCooldownSettingDto dto = new RoomCooldownSettingDto(chkEnabled.Checked, (int)numLossThreshold.Value, (int)numCooldownMinutes.Value);
			await _settingService.SaveAsync(dto);
			MessageBox.Show("연패 쿨다운 설정이 저장되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
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
