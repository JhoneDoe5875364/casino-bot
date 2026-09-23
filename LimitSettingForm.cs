using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;

namespace PragmaticBot;

public partial class LimitSettingForm : Form
{
	private readonly LimitSettingService _limitSettingService;

	private readonly string _userId;

	private string? _keepAliveTableId;

	private IContainer components;

	public LimitSettingForm(string userId)
	{
		_limitSettingService = new LimitSettingService();
		_userId = userId;
		InitializeComponent();
	}

	private async void LimitSettingForm_Load(object? sender, EventArgs e)
	{
		await LoadSettingsAsync();
	}

	private async Task LoadSettingsAsync()
	{
		try
		{
			LimitSettingDto setting = await _limitSettingService.GetLimitsAsync(_userId);
			if (setting != null)
			{
				nudUpperLimit.Value = ClampToRange(nudUpperLimit, setting.UpperLimitPercent ?? 30m);
				nudUpperAmount.Value = ClampToRange(nudUpperAmount, setting.TargetProfitAmount ?? 1000000m);
				chkUpperLimit.Checked = setting.IsUpperLimitEnabled;
				if (setting.UpperLimitMode == 1)
				{
					rdoUpperAmount.Checked = true;
				}
				else
				{
					rdoUpperPercent.Checked = true;
				}
				nudLowerLimit.Value = ClampToRange(nudLowerLimit, setting.LowerLimitPercent ?? 20m);
				chkLowerLimit.Checked = setting.IsLowerLimitEnabled;
				nudRestInterval.Value = ClampToRange(nudRestInterval, setting.RestIntervalMinutes ?? 30);
				nudSessionCount.Value = ClampToRange(nudSessionCount, setting.SessionCount ?? 3);
				chkTargetCycle.Checked = setting.IsTargetCycleEnabled;
				int? maxActiveBettingRooms = setting.MaxActiveBettingRooms;
				if (maxActiveBettingRooms.HasValue && maxActiveBettingRooms.GetValueOrDefault() > 0)
				{
					chkMaxActiveRooms.Checked = true;
					nudMaxActiveRooms.Value = ClampToRange(nudMaxActiveRooms, setting.MaxActiveBettingRooms.Value);
				}
				else
				{
					chkMaxActiveRooms.Checked = false;
					nudMaxActiveRooms.Value = 0m;
				}
			}
			else
			{
				nudUpperLimit.Value = 30m;
				nudUpperAmount.Value = 1000000m;
				rdoUpperPercent.Checked = true;
				chkUpperLimit.Checked = false;
				nudLowerLimit.Value = 20m;
				chkLowerLimit.Checked = false;
				nudRestInterval.Value = 30m;
				nudSessionCount.Value = 3m;
				chkTargetCycle.Checked = false;
				chkMaxActiveRooms.Checked = false;
				nudMaxActiveRooms.Value = 0m;
			}
			_keepAliveTableId = setting?.KeepAliveTableId;
			UpdateUpperModeVisibility();
			UpdateControlStates();
		}
		catch (Exception ex)
		{
			MessageBox.Show("설정 로드 중 오류가 발생했습니다: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private void UpdateControlStates()
	{
		nudUpperLimit.Enabled = chkUpperLimit.Checked;
		nudUpperAmount.Enabled = chkUpperLimit.Checked;
		rdoUpperPercent.Enabled = chkUpperLimit.Checked;
		rdoUpperAmount.Enabled = chkUpperLimit.Checked;
		nudLowerLimit.Enabled = chkLowerLimit.Checked;
		nudMaxActiveRooms.Enabled = chkMaxActiveRooms.Checked;
		bool cycleUsable = chkTargetCycle.Checked && chkUpperLimit.Checked;
		nudRestInterval.Enabled = cycleUsable;
		nudSessionCount.Enabled = cycleUsable;
	}

	private void UpdateUpperModeVisibility()
	{
		bool amountMode = rdoUpperAmount.Checked;
		nudUpperLimit.Visible = !amountMode;
		lblUpperSuffix.Visible = !amountMode;
		nudUpperAmount.Visible = amountMode;
		lblUpperAmountSuffix.Visible = amountMode;
	}

	private static decimal ClampToRange(NumericUpDown nud, decimal value)
	{
		return Math.Clamp(value, nud.Minimum, nud.Maximum);
	}

	private void RdoUpperMode_CheckedChanged(object? sender, EventArgs e)
	{
		UpdateUpperModeVisibility();
	}

	private void ChkTargetCycle_CheckedChanged(object? sender, EventArgs e)
	{
		UpdateControlStates();
	}

	private void ChkUpperLimit_CheckedChanged(object? sender, EventArgs e)
	{
		UpdateControlStates();
	}

	private void ChkLowerLimit_CheckedChanged(object? sender, EventArgs e)
	{
		UpdateControlStates();
	}

	private void ChkMaxActiveRooms_CheckedChanged(object? sender, EventArgs e)
	{
		UpdateControlStates();
	}

	private async void BtnSave_Click(object? sender, EventArgs e)
	{
		try
		{
			btnSave.Enabled = false;
			int? maxRooms = ((chkMaxActiveRooms.Checked && nudMaxActiveRooms.Value > 0m) ? new int?((int)nudMaxActiveRooms.Value) : ((int?)null));
			int upperMode = (rdoUpperAmount.Checked ? 1 : 0);
			FileLogger.Log($"[LimitSettingForm] 저장 버튼 클릭 - 상한모드={((upperMode == 1) ? "금액" : "퍼센트")}, 상한={nudUpperLimit.Value}%/{nudUpperAmount.Value:N0}원 ({chkUpperLimit.Checked}), 하한={nudLowerLimit.Value}% ({chkLowerLimit.Checked}), 활성방제한={maxRooms?.ToString() ?? "무제한"}, 사이클={chkTargetCycle.Checked} (휴식 {nudRestInterval.Value}분 × {nudSessionCount.Value}회)");
			LimitSettingDto limits = new LimitSettingDto(chkUpperLimit.Checked ? new decimal?(nudUpperLimit.Value) : ((decimal?)null), chkUpperLimit.Checked, chkLowerLimit.Checked ? new decimal?(nudLowerLimit.Value) : ((decimal?)null), chkLowerLimit.Checked, maxRooms, upperMode, chkUpperLimit.Checked ? new decimal?(nudUpperAmount.Value) : ((decimal?)null), chkTargetCycle.Checked, chkTargetCycle.Checked ? new int?((int)nudRestInterval.Value) : ((int?)null), chkTargetCycle.Checked ? new int?((int)nudSessionCount.Value) : ((int?)null), _keepAliveTableId);
			await _limitSettingService.SaveLimitsAsync(_userId, limits);
			FileLogger.Log("[LimitSettingForm] 저장 성공");
			MessageBox.Show("상/하한 설정이 저장되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			base.DialogResult = DialogResult.OK;
			Close();
		}
		catch (Exception ex)
		{
			FileLogger.Log("[LimitSettingForm] 저장 실패: " + ex.Message);
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
