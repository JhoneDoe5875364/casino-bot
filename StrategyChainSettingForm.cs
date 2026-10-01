using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;

namespace PragmaticBot;

public class StrategyChainSettingForm : Form
{
	private readonly ApiClient _apiClient;

	private TextBox txtChainName;

	private ComboBox cmbCompletionAction;

	private DataGridView dgvSteps;

	private Button btnAddStep;

	private Button btnRemoveStep;

	private Button btnSave;

	private Button btnCancel;

	private ComboBox cmbChainList;

	private Button btnActivate;

	private Button btnDelete;

	private List<StrategyChainDto> _chains = new List<StrategyChainDto>();

	private long _currentChainId;

	private static readonly string[] StrategyNames = new string[10] { "마틴", "홍콩크루즈", "피보나치", "혼합", "달랑베르", "마틴+달랑", "1-3-2-6", "오스카 그라인드", "플랫", "라부셰르" };

	public StrategyChainSettingForm(ApiClient apiClient)
	{
		_apiClient = apiClient;
		InitializeComponents();
		LoadChainsAsync();
	}

	private void InitializeComponents()
	{
		Text = "전략 체인 설정";
		base.Size = new Size(750, 500);
		base.StartPosition = FormStartPosition.CenterParent;
		BackColor = Color.FromArgb(22, 33, 62);
		ForeColor = Color.FromArgb(234, 234, 234);
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		Label lblChainList = new Label
		{
			Text = "저장된 체인:",
			Location = new Point(12, 15),
			AutoSize = true
		};
		cmbChainList = new ComboBox
		{
			Location = new Point(90, 12),
			Size = new Size(300, 23),
			DropDownStyle = ComboBoxStyle.DropDownList,
			BackColor = Color.FromArgb(15, 52, 96),
			ForeColor = Color.FromArgb(234, 234, 234)
		};
		cmbChainList.SelectedIndexChanged += CmbChainList_SelectedIndexChanged;
		btnActivate = new Button
		{
			Text = "활성화",
			Location = new Point(400, 11),
			Size = new Size(60, 25),
			BackColor = Color.FromArgb(39, 174, 96),
			ForeColor = Color.White
		};
		btnActivate.Click += BtnActivate_Click;
		btnDelete = new Button
		{
			Text = "삭제",
			Location = new Point(465, 11),
			Size = new Size(50, 25),
			BackColor = Color.FromArgb(192, 57, 43),
			ForeColor = Color.White
		};
		btnDelete.Click += BtnDelete_Click;
		Button btnNew = new Button
		{
			Text = "새 체인",
			Location = new Point(520, 11),
			Size = new Size(60, 25),
			BackColor = Color.FromArgb(52, 73, 94),
			ForeColor = Color.White
		};
		btnNew.Click += delegate
		{
			ClearForm();
		};
		Label lblName = new Label
		{
			Text = "체인 이름:",
			Location = new Point(12, 50),
			AutoSize = true
		};
		txtChainName = new TextBox
		{
			Location = new Point(90, 47),
			Size = new Size(200, 23),
			BackColor = Color.FromArgb(15, 52, 96),
			ForeColor = Color.FromArgb(234, 234, 234)
		};
		Label lblCompletion = new Label
		{
			Text = "완료 동작:",
			Location = new Point(310, 50),
			AutoSize = true
		};
		cmbCompletionAction = new ComboBox
		{
			Location = new Point(388, 47),
			Size = new Size(100, 23),
			DropDownStyle = ComboBoxStyle.DropDownList,
			BackColor = Color.FromArgb(15, 52, 96),
			ForeColor = Color.FromArgb(234, 234, 234)
		};
		cmbCompletionAction.Items.AddRange("정지", "순환반복");
		cmbCompletionAction.SelectedIndex = 0;
		dgvSteps = new DataGridView
		{
			Location = new Point(12, 80),
			Size = new Size(710, 300),
			BackgroundColor = Color.FromArgb(15, 52, 96),
			ForeColor = Color.Black,
			GridColor = Color.FromArgb(52, 73, 94),
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			RowHeadersVisible = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
		};
		dgvSteps.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "Order",
			HeaderText = "순서",
			Width = 40,
			ReadOnly = true
		});
		dgvSteps.Columns.Add(new DataGridViewComboBoxColumn
		{
			Name = "StrategyType",
			HeaderText = "전략",
			Width = 110,
			Items = 
			{
				(object)"마틴",
				(object)"홍콩크루즈",
				(object)"피보나치",
				(object)"혼합",
				(object)"달랑베르",
				(object)"마틴+달랑",
				(object)"1-3-2-6",
				(object)"오스카 그라인드",
				(object)"플랫",
				(object)"라부셰르"
			}
		});
		dgvSteps.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "MaxStep",
			HeaderText = "N단계 미당첨 전환",
			Width = 100
		});
		dgvSteps.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "ConsecutiveLoss",
			HeaderText = "N연패 전환",
			Width = 80
		});
		dgvSteps.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "LossAmount",
			HeaderText = "손실액 전환",
			Width = 90
		});
		dgvSteps.Columns.Add(new DataGridViewCheckBoxColumn
		{
			Name = "StrategyComplete",
			HeaderText = "완주 전환",
			Width = 65
		});
		btnAddStep = new Button
		{
			Text = "단계 추가",
			Location = new Point(12, 390),
			Size = new Size(80, 28),
			BackColor = Color.FromArgb(52, 73, 94),
			ForeColor = Color.White
		};
		btnAddStep.Click += BtnAddStep_Click;
		btnRemoveStep = new Button
		{
			Text = "단계 삭제",
			Location = new Point(100, 390),
			Size = new Size(80, 28),
			BackColor = Color.FromArgb(52, 73, 94),
			ForeColor = Color.White
		};
		btnRemoveStep.Click += BtnRemoveStep_Click;
		btnSave = new Button
		{
			Text = "저장",
			Location = new Point(530, 390),
			Size = new Size(90, 30),
			BackColor = Color.FromArgb(39, 174, 96),
			ForeColor = Color.White,
			Font = new Font(Font, FontStyle.Bold)
		};
		btnSave.Click += BtnSave_Click;
		btnCancel = new Button
		{
			Text = "닫기",
			Location = new Point(630, 390),
			Size = new Size(90, 30),
			BackColor = Color.FromArgb(192, 57, 43),
			ForeColor = Color.White
		};
		btnCancel.Click += delegate
		{
			Close();
		};
		base.Controls.AddRange(lblChainList, cmbChainList, btnActivate, btnDelete, btnNew, lblName, txtChainName, lblCompletion, cmbCompletionAction, dgvSteps, btnAddStep, btnRemoveStep, btnSave, btnCancel);
	}

	private async Task LoadChainsAsync()
	{
		try
		{
			_chains = await _apiClient.GetStrategyChainsAsync();
			cmbChainList.Items.Clear();
			foreach (StrategyChainDto chain in _chains)
			{
				string suffix = (chain.IsActive ? " [활성]" : "");
				cmbChainList.Items.Add(chain.Name + suffix);
			}
			if (cmbChainList.Items.Count > 0)
			{
				cmbChainList.SelectedIndex = 0;
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("체인 목록 로드 실패: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private void CmbChainList_SelectedIndexChanged(object? sender, EventArgs e)
	{
		if (cmbChainList.SelectedIndex < 0 || cmbChainList.SelectedIndex >= _chains.Count)
		{
			return;
		}
		StrategyChainDto chain = _chains[cmbChainList.SelectedIndex];
		_currentChainId = chain.Id;
		txtChainName.Text = chain.Name;
		cmbCompletionAction.SelectedIndex = chain.CompletionAction;
		dgvSteps.Rows.Clear();
		foreach (StrategyChainStepDto step in chain.Steps.OrderBy((StrategyChainStepDto s) => s.Order))
		{
			int rowIndex = dgvSteps.Rows.Add();
			DataGridViewRow dataGridViewRow = dgvSteps.Rows[rowIndex];
			dataGridViewRow.Cells["Order"].Value = step.Order;
			dataGridViewRow.Cells["StrategyType"].Value = ((step.StrategyType < StrategyNames.Length) ? StrategyNames[step.StrategyType] : $"타입{step.StrategyType}");
			dataGridViewRow.Cells["MaxStep"].Value = step.MaxStepTransition?.ToString() ?? "";
			dataGridViewRow.Cells["ConsecutiveLoss"].Value = step.ConsecutiveLossTransition?.ToString() ?? "";
			dataGridViewRow.Cells["LossAmount"].Value = step.LossAmountTransition?.ToString("N0") ?? "";
			dataGridViewRow.Cells["StrategyComplete"].Value = step.StrategyCompleteTransition;
		}
	}

	private void ClearForm()
	{
		_currentChainId = 0L;
		txtChainName.Text = "";
		cmbCompletionAction.SelectedIndex = 0;
		dgvSteps.Rows.Clear();
		cmbChainList.SelectedIndex = -1;
	}

	private void BtnAddStep_Click(object? sender, EventArgs e)
	{
		if (dgvSteps.Rows.Count >= 10)
		{
			MessageBox.Show("최대 10개 단계까지 추가 가능합니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		int rowIndex = dgvSteps.Rows.Add();
		dgvSteps.Rows[rowIndex].Cells["Order"].Value = dgvSteps.Rows.Count;
		dgvSteps.Rows[rowIndex].Cells["StrategyType"].Value = "마틴";
		dgvSteps.Rows[rowIndex].Cells["StrategyComplete"].Value = false;
	}

	private void BtnRemoveStep_Click(object? sender, EventArgs e)
	{
		if (dgvSteps.SelectedRows.Count > 0)
		{
			dgvSteps.Rows.RemoveAt(dgvSteps.SelectedRows[0].Index);
			for (int i = 0; i < dgvSteps.Rows.Count; i++)
			{
				dgvSteps.Rows[i].Cells["Order"].Value = i + 1;
			}
		}
	}

	private async void BtnSave_Click(object? sender, EventArgs e)
	{
		if (string.IsNullOrWhiteSpace(txtChainName.Text))
		{
			MessageBox.Show("체인 이름을 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		if (dgvSteps.Rows.Count < 2)
		{
			MessageBox.Show("최소 2개 단계가 필요합니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		try
		{
			List<StrategyChainStepDto> steps = new List<StrategyChainStepDto>();
			for (int i = 0; i < dgvSteps.Rows.Count; i++)
			{
				DataGridViewRow dataGridViewRow = dgvSteps.Rows[i];
				int strategyType = Array.IndexOf<string>(value: dataGridViewRow.Cells["StrategyType"].Value?.ToString() ?? "마틴", array: StrategyNames);
				if (strategyType < 0)
				{
					strategyType = 0;
				}
				int? maxStep = (int.TryParse(dataGridViewRow.Cells["MaxStep"].Value?.ToString(), out var ms) ? new int?(ms) : ((int?)null));
				int? consLoss = (int.TryParse(dataGridViewRow.Cells["ConsecutiveLoss"].Value?.ToString(), out var cl) ? new int?(cl) : ((int?)null));
				decimal? lossAmt = (decimal.TryParse(dataGridViewRow.Cells["LossAmount"].Value?.ToString()?.Replace(",", ""), out var la) ? new decimal?(la) : ((decimal?)null));
				object value = dataGridViewRow.Cells["StrategyComplete"].Value;
				bool stratComplete = value is bool && (bool)value;
				steps.Add(new StrategyChainStepDto(i + 1, strategyType, maxStep, consLoss, lossAmt, stratComplete));
			}
			StrategyChainDto dto = new StrategyChainDto(_currentChainId, txtChainName.Text.Trim(), cmbCompletionAction.SelectedIndex, IsActive: false, steps);
			await _apiClient.SaveStrategyChainAsync(dto);
			// 저장하면 곧바로 이 체인을 활성화한다(관리자 페이지에서 따로 켤 필요 없음).
			// 활성화는 서버의 단일-활성 endpoint를 쓰므로 다른 체인은 자동으로 비활성화된다.
			// 신규 체인은 저장 시 서버가 Id를 부여하므로, 목록을 다시 받아 방금 저장한 이름으로 Id를 찾는다.
			bool activated = false;
			try
			{
				long activateId = _currentChainId;
				if (activateId <= 0L)
				{
					List<StrategyChainDto> saved = await _apiClient.GetStrategyChainsAsync();
					activateId = saved
						.Where((StrategyChainDto c) => string.Equals(c.Name, dto.Name, StringComparison.Ordinal))
						.OrderByDescending((StrategyChainDto c) => c.Id)
						.Select((StrategyChainDto c) => c.Id)
						.FirstOrDefault();
				}
				if (activateId > 0L)
				{
					await _apiClient.ActivateStrategyChainAsync(activateId);
					activated = true;
				}
			}
			catch
			{
				// 저장은 성공했으므로 활성화 실패는 치명적이지 않다 — 아래 안내로 대체한다.
			}
			MessageBox.Show(activated
				? "전략 체인이 저장되고 활성화되었습니다."
				: "전략 체인이 저장되었습니다. (자동 활성화 실패 — 목록에서 '활성화'를 눌러주세요)",
				"성공", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			// 저장 성공 후 창을 닫아 설정 허브로 복귀(다른 설정창과 동일). 예전엔 목록만 갱신하고 창에 머물렀다.
			Close();
		}
		catch (Exception ex)
		{
			MessageBox.Show("저장 실패: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private async void BtnActivate_Click(object? sender, EventArgs e)
	{
		if (cmbChainList.SelectedIndex < 0 || cmbChainList.SelectedIndex >= _chains.Count)
		{
			return;
		}
		StrategyChainDto chain = _chains[cmbChainList.SelectedIndex];
		try
		{
			await _apiClient.ActivateStrategyChainAsync(chain.Id);
			MessageBox.Show("'" + chain.Name + "' 체인이 활성화되었습니다.", "성공", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			await LoadChainsAsync();
		}
		catch (Exception ex)
		{
			MessageBox.Show("활성화 실패: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private async void BtnDelete_Click(object? sender, EventArgs e)
	{
		if (cmbChainList.SelectedIndex < 0 || cmbChainList.SelectedIndex >= _chains.Count)
		{
			return;
		}
		StrategyChainDto chain = _chains[cmbChainList.SelectedIndex];
		if (MessageBox.Show("'" + chain.Name + "' 체인을 삭제하시겠습니까?", "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			return;
		}
		try
		{
			await _apiClient.DeleteStrategyChainAsync(chain.Id);
			ClearForm();
			await LoadChainsAsync();
		}
		catch (Exception ex)
		{
			MessageBox.Show("삭제 실패: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}
}
