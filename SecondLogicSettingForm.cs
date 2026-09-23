using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PragmaticBot.Models;
using PragmaticBot.Services;

namespace PragmaticBot;

public partial class SecondLogicSettingForm : Form
{
	private readonly SecondLogicSettingService _settingService = new SecondLogicSettingService();

	private readonly string _userId;

	private static readonly (string Name, int Type)[] BetAmountOptions = new(string, int)[5]
	{
		("마틴", 0),
		("홍콩크루즈", 1),
		("행렬", 11),
		("스위칭행렬(마틴+달랑)", 13),
		("달랑행렬", 14)
	};

	private const int DAlembertBetAmountType = 4;

	private const int MatrixBetAmountType = 11;

	private const int MatrixDAlembertBetAmountType = 12;

	private const int SwitchingMatrixBetAmountType = 13;

	private const int StairMatrixBetAmountType = 14;

	private const int HongKongBetAmountType = 1;

	private const decimal CooldownAmountUnit = 10000m;

	private IContainer components;

	public SecondLogicSettingForm(string userId)
	{
		_userId = userId;
		InitializeComponent();
		cmbBetAmountType.Items.AddRange(((IEnumerable<(string, int)>)BetAmountOptions).Select((Func<(string, int), object>)(((string Name, int Type) o) => o.Name)).ToArray());
		cmbBetAmountType.SelectedIndex = 0;
		cmbBetAmountType.SelectedIndexChanged += delegate
		{
			ApplyControlAvailability();
		};
		chkStreakRiding.CheckedChanged += delegate
		{
			ApplyControlAvailability();
		};
		chkProfitCooldown.CheckedChanged += delegate
		{
			ApplyControlAvailability();
		};
	}

	private void ApplyControlAvailability()
	{
		int selectedType = BetAmountOptions[Math.Max(0, cmbBetAmountType.SelectedIndex)].Type;
		bool flag = ((selectedType == 1 || selectedType == 4 || (uint)(selectedType - 11) <= 3u) ? true : false);
		bool isComboConflict = flag;
		if (isComboConflict)
		{
			chkCombo.Checked = false;
		}
		chkCombo.Enabled = !isComboConflict;
		bool isDAlembert = selectedType == 4;
		lblDDMaxStep.Visible = isDAlembert;
		numDDMaxStep.Visible = isDAlembert;
		lblDDMaxSuffix.Visible = isDAlembert;
		lblDDMaxStep.Enabled = isDAlembert;
		numDDMaxStep.Enabled = isDAlembert;
		lblDDMaxSuffix.Enabled = isDAlembert;
		bool isRiding = chkStreakRiding.Checked;
		lblWinStreak.Enabled = !isRiding;
		numWinStreak.Enabled = !isRiding;
		lblWinSuffix.Enabled = !isRiding;
		bool useCooldown = chkProfitCooldown.Checked;
		lblCooldownAmount.Enabled = useCooldown;
		numCooldownAmount.Enabled = useCooldown;
		lblCooldownAmountSuffix.Enabled = useCooldown;
		lblCooldownMinutes.Enabled = useCooldown;
		numCooldownMinutes.Enabled = useCooldown;
		lblCooldownMinutesSuffix.Enabled = useCooldown;
	}

	private void SecondLogicSettingForm_Load(object? sender, EventArgs e)
	{
		SecondLogicSetting setting = _settingService.Get(_userId);
		chkEnabled.Checked = setting.IsEnabled;
		numBendCount.Value = Math.Clamp(setting.BendCountToBet, (int)numBendCount.Minimum, (int)numBendCount.Maximum);
		numMissStreak.Value = Math.Clamp(setting.MissStreakToSwitchRoom, (int)numMissStreak.Minimum, (int)numMissStreak.Maximum);
		numWinStreak.Value = Math.Clamp(setting.WinStreakToSwitchRoom, (int)numWinStreak.Minimum, (int)numWinStreak.Maximum);
		int idx = Array.FindIndex(BetAmountOptions, ((string Name, int Type) o) => o.Type == setting.BetAmountType);
		cmbBetAmountType.SelectedIndex = ((idx >= 0) ? idx : 0);
		numDDMaxStep.Value = Math.Clamp(setting.DAlembertMaxStep, (int)numDDMaxStep.Minimum, (int)numDDMaxStep.Maximum);
		chkCombo.Checked = setting.UseMartinDalembertCombo;
		chkStreakRiding.Checked = setting.UseStreakRiding;
		decimal cooldownManwon = ((setting.CooldownProfitAmount > 0m) ? Math.Round(setting.CooldownProfitAmount / 10000m, MidpointRounding.AwayFromZero) : 100m);
		numCooldownAmount.Value = Math.Clamp(cooldownManwon, numCooldownAmount.Minimum, numCooldownAmount.Maximum);
		numCooldownMinutes.Value = Math.Clamp((setting.CooldownMinutes > 0) ? setting.CooldownMinutes : 10, (int)numCooldownMinutes.Minimum, (int)numCooldownMinutes.Maximum);
		chkProfitCooldown.Checked = setting.UseProfitCooldown;
		ApplyControlAvailability();
	}

	private void BtnSave_Click(object? sender, EventArgs e)
	{
		try
		{
			btnSave.Enabled = false;
			SecondLogicSetting setting = new SecondLogicSetting
			{
				IsEnabled = chkEnabled.Checked,
				BendCountToBet = (int)numBendCount.Value,
				MissStreakToSwitchRoom = (int)numMissStreak.Value,
				WinStreakToSwitchRoom = (int)numWinStreak.Value,
				BetAmountType = BetAmountOptions[Math.Max(0, cmbBetAmountType.SelectedIndex)].Type,
				DAlembertMaxStep = (int)numDDMaxStep.Value,
				UseMartinDalembertCombo = (chkCombo.Enabled && chkCombo.Checked),
				UseStreakRiding = chkStreakRiding.Checked,
				UseProfitCooldown = chkProfitCooldown.Checked,
				CooldownProfitAmount = numCooldownAmount.Value * 10000m,
				CooldownMinutes = (int)numCooldownMinutes.Value
			};
			_settingService.Save(_userId, setting);
			MessageBox.Show("세컨로직 설정이 저장되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
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
