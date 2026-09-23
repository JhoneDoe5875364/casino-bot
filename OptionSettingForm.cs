using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PragmaticBot.Models;

namespace PragmaticBot;

public partial class OptionSettingForm : Form
{
	private readonly BotOptionState _state;

	private readonly IReadOnlyList<string> _betStepItems;

	private readonly IReadOnlyList<string> _alarmStepItems;

	private readonly IReadOnlyList<string> _shoeThresholds;

	private readonly bool _isRunning;

	private readonly string? _roomGateWarning;

	private readonly string _userId;

	private const int SplitDisabledIndex = 0;

	private IContainer components;

	public BotOptionState Result { get; private set; }

	public bool RoomGateFormatChanged { get; private set; }

	public OptionSettingForm(BotOptionState state, IReadOnlyList<string> betStepItems, IReadOnlyList<string> alarmStepItems, IReadOnlyList<string> shoeThresholds, bool isRunning, string? roomGateWarning, string userId)
	{
		_state = state.Clone();
		_userId = userId;
		_betStepItems = betStepItems;
		_alarmStepItems = alarmStepItems;
		_shoeThresholds = shoeThresholds;
		_isRunning = isRunning;
		_roomGateWarning = roomGateWarning;
		Result = state;
		InitializeComponent();
		chkStepAlarm.CheckedChanged += delegate
		{
			ApplyControlAvailability();
		};
		chkShoeControl.CheckedChanged += delegate
		{
			ApplyControlAvailability();
		};
		chkStepReset.CheckedChanged += delegate
		{
			ApplyControlAvailability();
		};
		chkRoomGate.CheckedChanged += delegate
		{
			ApplyControlAvailability();
		};
	}

	private void OptionSettingForm_Load(object? sender, EventArgs e)
	{
		cmbBetStep.Items.AddRange(_betStepItems.Cast<object>().ToArray());
		cmbStepAlarm.Items.AddRange(_alarmStepItems.Cast<object>().ToArray());
		cmbShoeControl.Items.AddRange(_shoeThresholds.Cast<object>().ToArray());
		chkPeakReset.Checked = _state.PeakReset;
		chkStepReset.Checked = _state.StepReset;
		chkIgnorePendingBet.Checked = _state.IgnorePendingBet;
		chkRoomGate.Checked = _state.RoomGate;
		chkShoeControl.Checked = _state.ShoeControl;
		chkStepAlarm.Checked = _state.StepAlarm;
		SelectIndex(cmbBetStep, _state.StepResetIndex);
		SelectIndex(cmbStepAlarm, _state.StepAlarmIndex);
		SelectIndex(cmbShoeControl, _state.ShoeControlIndex);
		string splitText = ((_state.SplitCount <= 1) ? null : _state.SplitCount.ToString());
		int splitIndex = ((splitText != null) ? cmbBetSplit.Items.IndexOf(splitText) : 0);
		cmbBetSplit.SelectedIndex = ((splitIndex >= 0) ? splitIndex : 0);
		ApplyControlAvailability();
	}

	private void ApplyControlAvailability()
	{
		cmbBetStep.Enabled = chkStepReset.Checked;
		cmbShoeControl.Enabled = chkShoeControl.Checked;
		cmbStepAlarm.Enabled = chkStepAlarm.Checked;
		if (RoomGateFormatChanged)
		{
			lblRoomGateWarning.Visible = true;
			lblRoomGateWarning.ForeColor = Color.FromArgb(15, 82, 186);
			lblRoomGateWarning.Text = "서식을 변경했습니다. 이 창을 닫으면 새 서식으로 다시 판정합니다.";
		}
		else
		{
			bool showWarning = chkRoomGate.Checked && _roomGateWarning != null;
			lblRoomGateWarning.Visible = showWarning;
			lblRoomGateWarning.Text = (showWarning ? ("⚠ " + _roomGateWarning) : string.Empty);
		}
	}

	private void BtnRoomGateFormat_Click(object? sender, EventArgs e)
	{
		using RoomGateFormatSettingForm form = new RoomGateFormatSettingForm(_userId);
		if (form.ShowDialog(this) == DialogResult.OK)
		{
			RoomGateFormatChanged = true;
			ApplyControlAvailability();
		}
	}

	private static void SelectIndex(ComboBox combo, int index)
	{
		if (combo.Items.Count != 0)
		{
			combo.SelectedIndex = ((index >= 0 && index < combo.Items.Count) ? index : 0);
		}
	}

	private void BtnSave_Click(object? sender, EventArgs e)
	{
		Result = new BotOptionState
		{
			PeakReset = chkPeakReset.Checked,
			StepReset = chkStepReset.Checked,
			StepResetIndex = cmbBetStep.SelectedIndex,
			RoomGate = chkRoomGate.Checked,
			VirtualBet = _state.VirtualBet,  // 가상/실전은 전용 버튼이 결정 — 옵션창은 관여하지 않음
			IgnorePendingBet = chkIgnorePendingBet.Checked,
			ShoeControl = chkShoeControl.Checked,
			ShoeControlIndex = cmbShoeControl.SelectedIndex,
			StepAlarm = chkStepAlarm.Checked,
			StepAlarmIndex = cmbStepAlarm.SelectedIndex,
			SplitCount = ParseSplitCount()
		};
		base.DialogResult = DialogResult.OK;
		Close();
	}

	private int ParseSplitCount()
	{
		if (!int.TryParse(cmbBetSplit.SelectedItem?.ToString(), out var n) || n <= 0)
		{
			return 1;
		}
		return n;
	}

	private void BtnCancel_Click(object? sender, EventArgs e)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}
}
