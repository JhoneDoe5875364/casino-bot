using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;

namespace PragmaticBot;

public partial class BetAmountSettingForm : Form
{
	private readonly BetAmountSettingService _betAmountSettingService;

	private readonly string _userId;

	private int _currentBetAmountType;

	private readonly Dictionary<int, Dictionary<int, decimal?>> _typeDataCache = new Dictionary<int, Dictionary<int, decimal?>>();

	private readonly Dictionary<int, int?> _mixedMethodCache = new Dictionary<int, int?>();

	private readonly Dictionary<int, int> _hongKongTargetCache = new Dictionary<int, int>();

	private Rectangle _gridDefaultBounds;

	private Rectangle _matrixGridDefaultBounds;

	private int _martinMaxStepCache;

	private int _oscarTargetUnitsCache = 1;

	private decimal _labouchereMultiplierCache = 1000m;

	private int _dalmenzingMaxStepCache;

	private int _dalmenzingRecoveryMethodCache;

	// 업치기(파레이) — 단계수 캐시 + 프로그램 생성 패널/입력들(Designer 무수정)
	private int _upchigiStepCountCache = 1;

	private Panel? _pnlUpchigi;

	private TextBox? _txtUpchigiStep;

	private TextBox? _txtUpchigiStart;

	private TextBox? _txtUpchigiFlat;

	private TextBox? _txtUpchigiThreshold;

	private TextBox? _txtUpchigiFailThreshold;

	private TextBox? _txtUpchigiMultiplier;

	private TextBox? _txtUpchigiMaxMultiplier;

	private static readonly int[] MatrixStepsPerRow = new int[15]
	{
		20, 20, 20, 20, 20, 20, 20, 20, 20, 20,
		20, 20, 20, 20, 20
	};

	private static readonly decimal[][] MatrixDefaults = new decimal[5][]
	{
		new decimal[11]
		{
			1000m, 2000m, 4000m, 8000m, 16000m, 32000m, 64000m, 128000m, 256000m, 512000m,
			1024000m
		},
		new decimal[11]
		{
			3000m, 6000m, 12000m, 24000m, 48000m, 96000m, 192000m, 384000m, 768000m, 1536000m,
			3072000m
		},
		new decimal[8] { 5000m, 10000m, 20000m, 40000m, 80000m, 160000m, 320000m, 640000m },
		new decimal[10] { 8000m, 16000m, 32000m, 64000m, 128000m, 256000m, 512000m, 1024000m, 2048000m, 4096000m },
		new decimal[11]
		{
			15000m, 30000m, 60000m, 120000m, 240000m, 480000m, 960000m, 1920000m, 3840000m, 7680000m,
			15360000m
		}
	};

	private const int SwitchingMatrixType = 13;

	private const int StairMatrixType = 14;

	private static readonly int[] MatrixBetAmountTypes = new int[4] { 11, 12, 13, 14 };

	/// <summary>
	/// Kept in step with Form1.BetAmountOptions — a strategy the main screen can select but this form
	/// cannot edit would be unusable, and vice versa.
	/// </summary>
	private static readonly (string Name, int Type)[] BetAmountOptions = new(string, int)[16]
	{
		("마틴", 0),
		("홍콩크루즈", 1),
		("피보나치", 2),
		("혼합(마틴+홍콩)", 3),
		("달랑베르", 4),
		("마틴+달랑 혼합", 5),
		("1-3-2-6", 6),
		("오스카 그라인드", 7),
		("플랫 베팅", 8),
		("라부셰르", 9),
		("달랑멘징", 10),
		("행렬", 11),
		("행렬달랑베르", 12),
		("스위칭행렬(마틴+달랑)", 13),
		("달랑행렬", 14),
		("업치기", 15)
	};

	private readonly Dictionary<int, Dictionary<(int Row, int Col), decimal>> _matrixCellsCacheByType = MatrixBetAmountTypes.ToDictionary((int t) => t, (int _) => new Dictionary<(int, int), decimal>());

	private readonly Dictionary<int, Dictionary<int, (int WinCount, int TargetRow, int WinMode, int RestMinutes, decimal FirstRowCapAmount)>> _matrixRowRulesCacheByType = MatrixBetAmountTypes.ToDictionary((int t) => t, (int _) => new Dictionary<int, (int, int, int, int, decimal)>());

	private const string MatrixRecoveryInfo = "※ 당첨갯수·이동차수를 비워 둔 차는 금액으로 판정합니다 — 그때까지 잃은 돈을 전부 회수하는 순간 1차 1단계로 복귀합니다 (예: 1차 손실을 2차에서 회수, 1·2차 손실을 3차에서 회수). 뱅커 커미션 5%를 반영한 실수령액 기준입니다.\r\n※ 당첨갯수와 이동차수는 둘 다 채워야 규칙이 저장됩니다. 하나만 입력하면 규칙 없는 차로 처리됩니다.";

	private const string StairMatrixRecoveryInfo = "※ 달랑행렬: 표는 행렬과 같고 당첨갯수·이동차수는 쓰지 않습니다. 오직 금액으로만 판정합니다.\r\n※ [단계달랑]을 체크하면 계단을 올라가지 못한 당첨에서 한 단계만 후퇴하고(1단계면 유지), 비우면 행렬처럼 그 차의 1단계로 복귀합니다. 차(세로)의 계단 복귀와는 무관하며 봇 전체에 적용됩니다.\r\n※ 직전 차에서 잃은 금액을 회수하면 그 직전 차의 1단계로 한 계단 올라갑니다 (2차에서 1차 손실을 찾으면 1차 1단계, 3차에서 2차 손실을 찾으면 2차 1단계). 1차까지 올라오면 사이클이 끝나고 장부가 0으로 닫힙니다. 뱅커 커미션 5%를 반영한 실수령액 기준입니다.\r\n※ 0을 넣으면 그 단계는 베팅하지 않고 결과만 보고 단계를 진행합니다 (빈칸 = 단계 없음).\r\n※ 쉬는타임(분): 그 차의 마지막 단계까지 지고 다음 차로 내려가는 순간 봇 전체가 그 시간만큼 베팅을 멈춥니다 (15차를 소진해 1차로 되돌아갈 때도 동일). 차수·단계는 그대로 보존되어 휴식이 끝나면 이어서 진행합니다. 0/빈칸이면 쉬지 않습니다 (최대 120분).";

	private const string MatrixFirstRowCapInfo = "\r\n※ 1차 상한금액: 1차의 마지막 단계까지 지고 2차로 내려가려는 순간에만 판정합니다. 그때 계좌 잔액이 [기준 잔액 + 이 금액]을 초과하면 2차로 내려가지 않고 1차 1단계로 복귀하며, 누적 금액은 0으로 초기화됩니다. 이하이면 기존대로 2차에 진입합니다.\r\n※ 기준 잔액은 봇 시작 잔액에서 출발해 1차를 모두 실패할 때마다 그 시점 잔액으로 새로 잡힙니다 (1차로 복귀하든 2차로 내려가든 동일). 즉 직전 1차 실패 이후로 이 금액만큼 다시 벌어야 발동합니다.\r\n※ 예) 시작 100만원 + 상한 10만원 → 1차 전패 시 잔액이 110만원을 넘으면 1차 복귀하고 기준 잔액이 그 잔액으로 갱신됩니다. 110만원 이하이면 2차로 진입합니다.\r\n※ 방마다 따로 보지 않고 계좌 잔액 하나로 판정하므로 세컨로직에서도 동작합니다. 빈칸/0이면 사용하지 않으며, 이 복귀에서는 쉬는타임이 발동하지 않습니다.";

	private Label? _lblMixedInfo;

	private IContainer components;

	private Dictionary<(int Row, int Col), decimal> CurrentMatrixCells => _matrixCellsCacheByType[IsMatrixType(_currentBetAmountType) ? _currentBetAmountType : 11];

	private Dictionary<int, (int WinCount, int TargetRow, int WinMode, int RestMinutes, decimal FirstRowCapAmount)> CurrentMatrixRowRules => _matrixRowRulesCacheByType[IsMatrixType(_currentBetAmountType) ? _currentBetAmountType : 11];

	private static bool IsMatrixType(int betAmountType)
	{
		if ((uint)(betAmountType - 11) <= 3u)
		{
			return true;
		}
		return false;
	}

	private static bool UsesMatrixRowRules(int betAmountType)
	{
		if (IsMatrixType(betAmountType))
		{
			return betAmountType != 14;
		}
		return false;
	}

	private static bool SupportsMatrixRest(int betAmountType)
	{
		if ((uint)(betAmountType - 13) <= 1u)
		{
			return true;
		}
		return false;
	}

	public BetAmountSettingForm(string userId)
	{
		_betAmountSettingService = new BetAmountSettingService();
		_userId = userId;
		InitializeComponent();
		cmbBetAmountType.Items.AddRange(((IEnumerable<(string, int)>)BetAmountOptions).Select((Func<(string, int), object>)(((string Name, int Type) o) => o.Name)).ToArray());
		cmbBetAmountType.SelectedIndex = 0;
		cmbBetAmountType.SelectedIndexChanged += CmbBetAmountType_SelectedIndexChanged;
		_gridDefaultBounds = dgvBetAmounts.Bounds;
		_matrixGridDefaultBounds = dgvMatrix.Bounds;
	}

	private async void BetAmountSettingForm_Load(object? sender, EventArgs e)
	{
		await LoadAllTypesAsync();
	}

	private async Task LoadAllTypesAsync()
	{
		_ = 1;
		try
		{
			int[] allTypes = new int[12]
			{
				0, 1, 2, 3, 4, 5, 6, 7, 8, 9,
				10, 15
			};
			int[] array = allTypes;
			foreach (int type in array)
			{
				List<BetAmountSettingDto> obj = await _betAmountSettingService.GetUserBetAmountsAsync(_userId, type);
				Dictionary<int, decimal?> dict = new Dictionary<int, decimal?>();
				foreach (BetAmountSettingDto setting in obj)
				{
					if (setting.Number < 1 || setting.Number > 50)
					{
						continue;
					}
					dict[setting.Number] = setting.Amount;
					if (type == 3)
					{
						_mixedMethodCache[setting.Number] = setting.BettingMethod;
					}
					if (type == 1)
					{
						switch (setting.BettingMethod)
						{
						case 1:
						case 2:
						case 3:
						case 4:
						case 5:
						case 6:
						case 7:
						case 8:
						case 9:
						case 10:
						case 11:
						case 12:
						case 13:
						case 14:
						case 15:
						case 16:
						case 17:
						case 18:
						case 19:
						case 20:
						case 21:
						case 22:
						case 23:
						case 24:
						case 25:
						case 26:
						case 27:
						case 28:
						case 29:
						case 30:
						case 31:
						case 32:
						case 33:
						case 34:
						case 35:
						case 36:
						case 37:
						case 38:
						case 39:
						case 40:
						case 41:
						case 42:
						case 43:
						case 44:
						case 45:
						case 46:
						case 47:
						case 48:
						case 49:
						case 50:
							_hongKongTargetCache[setting.Number] = setting.BettingMethod.Value;
							break;
						}
					}
					if (type == 5 && setting.Number == 1)
					{
						switch (setting.BettingMethod)
						{
						case 1:
						case 2:
						case 3:
						case 4:
						case 5:
						case 6:
						case 7:
						case 8:
						case 9:
						case 10:
						case 11:
						case 12:
						case 13:
						case 14:
						case 15:
						case 16:
						case 17:
						case 18:
						case 19:
						case 20:
						case 21:
						case 22:
						case 23:
						case 24:
						case 25:
						case 26:
						case 27:
						case 28:
						case 29:
						case 30:
						case 31:
						case 32:
						case 33:
						case 34:
						case 35:
						case 36:
						case 37:
						case 38:
						case 39:
						case 40:
						case 41:
						case 42:
						case 43:
						case 44:
						case 45:
						case 46:
						case 47:
						case 48:
						case 49:
						case 50:
							_martinMaxStepCache = setting.BettingMethod.Value;
							break;
						}
					}
					if (type == 7 && setting.Number == 1)
					{
						int? bettingMethod = setting.BettingMethod;
						if (bettingMethod.HasValue && bettingMethod.GetValueOrDefault() > 0)
						{
							_oscarTargetUnitsCache = setting.BettingMethod.Value;
						}
					}
					if (type == 15 && setting.Number == 1)
					{
						int? upBm = setting.BettingMethod;
						if (upBm.HasValue && upBm.GetValueOrDefault() > 0)
						{
							_upchigiStepCountCache = setting.BettingMethod.Value;
						}
					}
					if (type == 9 && setting.Number == 1)
					{
						int? bettingMethod = setting.BettingMethod;
						if (bettingMethod.HasValue && bettingMethod.GetValueOrDefault() > 0)
						{
							_labouchereMultiplierCache = setting.BettingMethod.Value;
						}
					}
					if (type != 10)
					{
						continue;
					}
					if (setting.Number == 1)
					{
						switch (setting.BettingMethod)
						{
						case 1:
						case 2:
						case 3:
						case 4:
						case 5:
						case 6:
						case 7:
						case 8:
						case 9:
						case 10:
						case 11:
						case 12:
						case 13:
						case 14:
						case 15:
						case 16:
						case 17:
						case 18:
						case 19:
						case 20:
						case 21:
						case 22:
						case 23:
						case 24:
						case 25:
						case 26:
						case 27:
						case 28:
						case 29:
						case 30:
						case 31:
						case 32:
						case 33:
						case 34:
						case 35:
						case 36:
						case 37:
						case 38:
						case 39:
						case 40:
						case 41:
						case 42:
						case 43:
						case 44:
						case 45:
						case 46:
						case 47:
						case 48:
						case 49:
						case 50:
							_dalmenzingMaxStepCache = setting.BettingMethod.Value;
							continue;
						}
					}
					bool flag = setting.Number == 2;
					bool flag2;
					if (flag)
					{
						int? bettingMethod = setting.BettingMethod;
						if (bettingMethod.HasValue)
						{
							int valueOrDefault = bettingMethod.GetValueOrDefault();
							if ((uint)valueOrDefault <= 1u)
							{
								flag2 = true;
								goto IL_02f1;
							}
						}
						flag2 = false;
						goto IL_02f1;
					}
					goto IL_02f5;
					IL_02f1:
					flag = flag2;
					goto IL_02f5;
					IL_02f5:
					if (flag)
					{
						_dalmenzingRecoveryMethodCache = setting.BettingMethod.Value;
					}
				}
				_typeDataCache[type] = dict;
			}
			await LoadMatrixCellsAsync();
			DisplayTypeData(_currentBetAmountType);
		}
		catch (Exception ex)
		{
			MessageBox.Show("베팅금 데이터 로드 중 오류가 발생했습니다: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private async Task LoadMatrixCellsAsync()
	{
		int[] matrixBetAmountTypes = MatrixBetAmountTypes;
		foreach (int type in matrixBetAmountTypes)
		{
			Dictionary<(int Row, int Col), decimal> cellCache = _matrixCellsCacheByType[type];
			Dictionary<int, (int WinCount, int TargetRow, int WinMode, int RestMinutes, decimal FirstRowCapAmount)> ruleCache = _matrixRowRulesCacheByType[type];
			cellCache.Clear();
			ruleCache.Clear();
			try
			{
				ApiClient api = AuthService.Api;
				if (api == null)
				{
					break;
				}
				// [성능] 셀·행규칙은 서로 독립적 → 동시에 요청해 원격 서버 왕복을 반으로 줄인다.
				Task<List<MatrixCellDto>> _cellsTask = api.GetMatrixCellsAsync(type);
				Task<List<MatrixRowRuleDto>> _rulesTask = api.GetMatrixRowRulesAsync(type);
				await Task.WhenAll(_cellsTask, _rulesTask);
				foreach (MatrixCellDto c in _cellsTask.Result.Where(delegate(MatrixCellDto matrixCellDto)
				{
					if (matrixCellDto.RowIndex >= 1 && matrixCellDto.RowIndex <= MatrixStepsPerRow.Length)
					{
						int colIndex = matrixCellDto.ColIndex;
						if (colIndex >= 1 && colIndex <= 20)
						{
							return matrixCellDto.Amount >= 0m;
						}
					}
					return false;
				}))
				{
					cellCache[(c.RowIndex, c.ColIndex)] = c.Amount;
				}
				foreach (MatrixRowRuleDto r in _rulesTask.Result.Where((MatrixRowRuleDto matrixRowRuleDto) => matrixRowRuleDto.RowIndex >= 1 && matrixRowRuleDto.RowIndex <= MatrixStepsPerRow.Length))
				{
					int num;
					if (UsesMatrixRowRules(type) && r.WinCount >= 1)
					{
						int targetRow = r.TargetRow;
						num = ((targetRow >= 1 && targetRow <= 15) ? 1 : 0);
					}
					else
					{
						num = 0;
					}
					bool hasMoveRule = (byte)num != 0;
					int winMode = (((type == 13 || (type == 14 && r.RowIndex == 1)) && r.WinMode == 1) ? 1 : 0);
					int restMinutes = (SupportsMatrixRest(type) ? Math.Clamp(r.RestMinutes, 0, 120) : 0);
					decimal capAmount = ((r.RowIndex == 1 && r.FirstRowCapAmount >= 1m) ? decimal.Truncate(r.FirstRowCapAmount) : 0m);
					if (hasMoveRule || winMode != 0 || restMinutes != 0 || !(capAmount == 0m))
					{
						ruleCache[r.RowIndex] = (hasMoveRule ? r.WinCount : 0, hasMoveRule ? r.TargetRow : 0, winMode, restMinutes, capAmount);
					}
				}
			}
			catch (Exception ex)
			{
				FileLogger.Log($"[BetAmountSettingForm] 행렬 계열(타입 {type}) 로드 실패: {ex.Message}", "Warning", "Init");
			}
		}
	}

	private void LayoutGridBelow(Control? panel)
	{
		if (panel == null)
		{
			dgvBetAmounts.Bounds = _gridDefaultBounds;
			return;
		}
		int top = panel.Bottom + 6;
		int height = Math.Max(100, btnSave.Top - top - 6);
		dgvBetAmounts.Bounds = new Rectangle(_gridDefaultBounds.Left, top, _gridDefaultBounds.Width, height);
	}

	// 업치기 설정 패널(단계수/시작금액/일정금액/합산수익)을 Designer 수정 없이 런타임 생성.
	// pnlFlatBetting과 같은 자리(12,50)에 올리고, 다른 스칼라 패널들처럼 보이기/숨기기로 전환한다.
	private void EnsureUpchigiPanel()
	{
		if (_pnlUpchigi != null)
		{
			return;
		}
		_pnlUpchigi = new Panel
		{
			Name = "pnlUpchigi",
			Location = new Point(12, 50),
			Size = new Size(564, 475),
			Visible = false,
			BackColor = pnlFlatBetting.BackColor,
			ForeColor = pnlFlatBetting.ForeColor
		};
		int y = 10;
		_txtUpchigiStep = AddUpchigiRow("단계 수(N)", ref y);
		_txtUpchigiStart = AddUpchigiRow("시작금액", ref y);
		_txtUpchigiFlat = AddUpchigiRow("일정금액", ref y);
		_txtUpchigiThreshold = AddUpchigiRow("합산수익", ref y);
		_txtUpchigiFailThreshold = AddUpchigiRow("누적실패 임계", ref y);
		_txtUpchigiMultiplier = AddUpchigiRow("실패 시 배수", ref y);
		_txtUpchigiMaxMultiplier = AddUpchigiRow("배수 상한", ref y);
		Label info = new Label
		{
			AutoSize = false,
			Location = new Point(8, y + 8),
			Size = new Size(548, 210),
			ForeColor = pnlFlatBetting.ForeColor,
			Text = "※ 서식 매칭 시 시작금액으로 시작 → 당첨하면 그 금액의 ×2로 자동 업치기(대상 동일, 서식 재확인 없음). 업치기 중 미당첨이면 1단계(시작금액)로 초기화.\r\n※ N단계 완주 후: 일정금액이 설정돼 있으면 일정금액으로 베팅하며, 당첨이면 일정베팅 유지, 미당첨(실패)이면 1단계로 초기화해 업치기를 다시 시작합니다. 일정금액을 비우면 일정베팅 없이 바로 1단계부터 업치기 재시작.\r\n※ 합산수익(=전체 목표): 돌리는 모든 방의 합산 순손익이 이 값을 넘으면 — 즉 전체 실패금액을 다 메우고 합산수익만큼 더 벌면 — 단계와 상관없이 모든 방을 1단계로 초기화합니다. (0=미사용)\r\n※ 누적 미당첨(업치기 단계·일정베팅 모두 포함)이 임계에 도달하면 그 방의 시작금액과 일정금액을 함께 '실패 시 배수'만큼 올립니다(×2→×4…, 방마다 따로). '배수 상한'까지만 오르고, 전체 합산수익 리셋 시 ×1로 복귀. (임계 0 또는 배수 1 이하=미사용, 배수 상한 0/빈칸=무제한)"
		};
		_pnlUpchigi.Controls.Add(info);
		base.Controls.Add(_pnlUpchigi);
		_pnlUpchigi.BringToFront();
	}

	private TextBox AddUpchigiRow(string label, ref int y)
	{
		Label lbl = new Label
		{
			Text = label,
			AutoSize = false,
			Location = new Point(8, y + 4),
			Size = new Size(95, 24),
			ForeColor = pnlFlatBetting.ForeColor
		};
		TextBox box = new TextBox
		{
			Location = new Point(108, y),
			Size = new Size(200, 27),
			BackColor = txtFlatAmount.BackColor,
			ForeColor = txtFlatAmount.ForeColor,
			BorderStyle = txtFlatAmount.BorderStyle,
			Font = txtFlatAmount.Font
		};
		_pnlUpchigi!.Controls.Add(lbl);
		_pnlUpchigi!.Controls.Add(box);
		y += 34;
		return box;
	}

	private void DisplayTypeData(int betAmountType)
	{
		dgvBetAmounts.Rows.Clear();
		pnlDAlembert.Visible = false;
		pnlMartinDAlembert.Visible = false;
		pnl1326.Visible = false;
		pnlOscarGrind.Visible = false;
		pnlFlatBetting.Visible = false;
		pnlLabouchere.Visible = false;
		pnlDalmenzing.Visible = false;
		pnlMatrix.Visible = false;
		pnlHkAutoFill.Visible = false;
		if (_pnlUpchigi != null)
		{
			_pnlUpchigi.Visible = false;
		}

		// The mixed-mode overlay, the save button and the caption used to be reset only inside the
		// default branch. Every other strategy has its own case (and the matrix types return early),
		// so picking 혼합 and then any of them left the overlay sitting on top of the dialog with the
		// save button still disabled and the 혼합 caption still showing.
		EnsureMixedInfoLabel();
		_lblMixedInfo.Visible = false;
		if (btnSave != null)
		{
			btnSave.Enabled = true;
		}
		Text = "베팅금 설정";
		colMethod.Visible = false;
		colHkTarget.Visible = false;

		if (IsMatrixType(betAmountType))
		{
			dgvBetAmounts.Visible = false;
			pnlBulkSet.Visible = false;
			pnlMatrix.Visible = true;
			Label label = lblMatrixInfo;
			label.Text = betAmountType switch
			{
				12 => "※ 행렬달랑베르: 표는 행렬과 같고 금액은 별도로 저장됩니다. 당첨 시 그 차에서 한 단계만 후퇴합니다(1단계면 유지).\r\n※ 0을 넣으면 그 단계는 베팅하지 않고 결과만 보고 단계를 진행합니다 (빈칸 = 단계 없음).\r\n※ 당첨갯수·이동차수를 비워 둔 차는 금액으로 판정합니다 — 그때까지 잃은 돈을 전부 회수하는 순간 1차 1단계로 복귀합니다 (예: 1차 손실을 2차에서 회수, 1·2차 손실을 3차에서 회수). 뱅커 커미션 5%를 반영한 실수령액 기준입니다.\r\n※ 당첨갯수와 이동차수는 둘 다 채워야 규칙이 저장됩니다. 하나만 입력하면 규칙 없는 차로 처리됩니다.", 
				14 => "※ 달랑행렬: 표는 행렬과 같고 당첨갯수·이동차수는 쓰지 않습니다. 오직 금액으로만 판정합니다.\r\n※ [단계달랑]을 체크하면 계단을 올라가지 못한 당첨에서 한 단계만 후퇴하고(1단계면 유지), 비우면 행렬처럼 그 차의 1단계로 복귀합니다. 차(세로)의 계단 복귀와는 무관하며 봇 전체에 적용됩니다.\r\n※ 직전 차에서 잃은 금액을 회수하면 그 직전 차의 1단계로 한 계단 올라갑니다 (2차에서 1차 손실을 찾으면 1차 1단계, 3차에서 2차 손실을 찾으면 2차 1단계). 1차까지 올라오면 사이클이 끝나고 장부가 0으로 닫힙니다. 뱅커 커미션 5%를 반영한 실수령액 기준입니다.\r\n※ 0을 넣으면 그 단계는 베팅하지 않고 결과만 보고 단계를 진행합니다 (빈칸 = 단계 없음).\r\n※ 쉬는타임(분): 그 차의 마지막 단계까지 지고 다음 차로 내려가는 순간 봇 전체가 그 시간만큼 베팅을 멈춥니다 (15차를 소진해 1차로 되돌아갈 때도 동일). 차수·단계는 그대로 보존되어 휴식이 끝나면 이어서 진행합니다. 0/빈칸이면 쉬지 않습니다 (최대 120분).", 
				13 => "※ 스위칭행렬(마틴+달랑): 표·당첨갯수·이동차수는 행렬과 같고 금액은 별도로 저장됩니다. 차마다 [달랑]을 체크하면 그 차는 당첨 시 한 단계만 후퇴하고, 비우면 마틴처럼 1단계로 복귀합니다.\r\n※ 이동차수 규칙 없이 [달랑]만 지정해도 됩니다. 0을 넣으면 그 단계는 베팅하지 않고 결과만 보고 단계를 진행합니다 (빈칸 = 단계 없음).\r\n※ 쉬는타임(분): 그 차의 마지막 단계까지 지고 다음 차로 내려가는 순간 봇 전체가 그 시간만큼 베팅을 멈춥니다 (15차를 소진해 1차로 되돌아갈 때도 동일). 차수·단계는 그대로 보존됩니다. 0/빈칸이면 쉬지 않습니다 (최대 120분).\r\n※ 당첨갯수·이동차수를 비워 둔 차는 금액으로 판정합니다 — 그때까지 잃은 돈을 전부 회수하는 순간 1차 1단계로 복귀합니다 (예: 1차 손실을 2차에서 회수, 1·2차 손실을 3차에서 회수). 뱅커 커미션 5%를 반영한 실수령액 기준입니다.\r\n※ 당첨갯수와 이동차수는 둘 다 채워야 규칙이 저장됩니다. 하나만 입력하면 규칙 없는 차로 처리됩니다.", 
				_ => "※ 행렬 베팅: 차별로 20단계까지 입력할 수 있습니다. 마지막 입력 칸이 그 차의 끝단입니다. 당첨 시 그 차의 1단계로 복귀합니다.\r\n※ 0을 넣으면 그 단계는 베팅하지 않고 결과만 보고 단계를 진행합니다 (빈칸 = 단계 없음).\r\n※ 당첨갯수·이동차수를 비워 둔 차는 금액으로 판정합니다 — 그때까지 잃은 돈을 전부 회수하는 순간 1차 1단계로 복귀합니다 (예: 1차 손실을 2차에서 회수, 1·2차 손실을 3차에서 회수). 뱅커 커미션 5%를 반영한 실수령액 기준입니다.\r\n※ 당첨갯수와 이동차수는 둘 다 채워야 규칙이 저장됩니다. 하나만 입력하면 규칙 없는 차로 처리됩니다.", 
			} + "\r\n※ 1차 상한금액: 1차의 마지막 단계까지 지고 2차로 내려가려는 순간에만 판정합니다. 그때 계좌 잔액이 [기준 잔액 + 이 금액]을 초과하면 2차로 내려가지 않고 1차 1단계로 복귀하며, 누적 금액은 0으로 초기화됩니다. 이하이면 기존대로 2차에 진입합니다.\r\n※ 기준 잔액은 봇 시작 잔액에서 출발해 1차를 모두 실패할 때마다 그 시점 잔액으로 새로 잡힙니다 (1차로 복귀하든 2차로 내려가든 동일). 즉 직전 1차 실패 이후로 이 금액만큼 다시 벌어야 발동합니다.\r\n※ 예) 시작 100만원 + 상한 10만원 → 1차 전패 시 잔액이 110만원을 넘으면 1차 복귀하고 기준 잔액이 그 잔액으로 갱신됩니다. 110만원 이하이면 2차로 진입합니다.\r\n※ 방마다 따로 보지 않고 계좌 잔액 하나로 판정하므로 세컨로직에서도 동작합니다. 빈칸/0이면 사용하지 않으며, 이 복귀에서는 쉬는타임이 발동하지 않습니다.";
			chkStairStepBack.Visible = betAmountType == 14;
			if (chkStairStepBack.Visible)
			{
				chkStairStepBack.Checked = CurrentMatrixRowRules.TryGetValue(1, out (int, int, int, int, decimal) firstRowRule) && firstRowRule.Item3 == 1;
				int top = chkStairStepBack.Bottom + 6;
				dgvMatrix.Bounds = new Rectangle(_matrixGridDefaultBounds.Left, top, _matrixGridDefaultBounds.Width, Math.Max(100, _matrixGridDefaultBounds.Bottom - top));
			}
			else
			{
				dgvMatrix.Bounds = _matrixGridDefaultBounds;
			}
			BuildMatrixGrid();
			return;
		}
		switch (betAmountType)
		{
		case 10:
		{
			dgvBetAmounts.Visible = false;
			pnlBulkSet.Visible = false;
			pnlDalmenzing.Visible = true;
			decimal? baseDAlembert = _typeDataCache.GetValueOrDefault(4)?.GetValueOrDefault(1);
			cmbDalmenzingMaxStep.Items.Clear();
			cmbDalmenzingMaxStep.Items.Add(0);
			if (baseDAlembert > (decimal?)0m)
			{
				for (int n = 1; n <= 50; n++)
				{
					cmbDalmenzingMaxStep.Items.Add(n);
				}
			}
			int idxMax = cmbDalmenzingMaxStep.Items.IndexOf(_dalmenzingMaxStepCache);
			cmbDalmenzingMaxStep.SelectedIndex = ((idxMax >= 0) ? idxMax : 0);
			cmbDalmenzingRecoveryMethod.SelectedIndex = ((_dalmenzingRecoveryMethodCache == 1) ? 1 : 0);
			lblDalmenzingBaseInfo.Text = ((baseDAlembert > (decimal?)0m) ? $"※ 달랑 기본금액: {baseDAlembert.Value:N0}원 ('달랑베르' 탭에서 관리)" : "※ '달랑베르' 탭에서 기본금액을 먼저 설정하세요. (설정 전에는 단계 선택이 비활성화됩니다)");
			break;
		}
		case 4:
		{
			dgvBetAmounts.Visible = false;
			pnlBulkSet.Visible = false;
			pnlDAlembert.Visible = true;
			decimal? baseAmount4 = _typeDataCache.GetValueOrDefault(4)?.GetValueOrDefault(1);
			txtDAlembertBaseAmount.Text = ((baseAmount4.HasValue && baseAmount4.Value > 0m) ? baseAmount4.Value.ToString("N0") : "");
			break;
		}
		case 5:
		{
			dgvBetAmounts.Visible = true;
			colMethod.Visible = false;
			pnlBulkSet.Visible = false;
			pnlMartinDAlembert.Visible = true;
			LayoutGridBelow(pnlMartinDAlembert);
			Dictionary<int, decimal?> cache10 = _typeDataCache.GetValueOrDefault(5) ?? new Dictionary<int, decimal?>();
			Dictionary<int, decimal?>? source = _typeDataCache.GetValueOrDefault(0) ?? new Dictionary<int, decimal?>();
			cmbMartinMaxStep.Items.Clear();
			cmbMartinMaxStep.Items.Add(0);
			foreach (KeyValuePair<int, decimal?> kv in source.OrderBy((KeyValuePair<int, decimal?> x) => x.Key))
			{
				if (kv.Value.HasValue)
				{
					decimal? value = kv.Value;
					if ((value.GetValueOrDefault() > 0m) & value.HasValue)
					{
						cmbMartinMaxStep.Items.Add(kv.Key);
					}
				}
			}
			int idx = cmbMartinMaxStep.Items.IndexOf(_martinMaxStepCache);
			cmbMartinMaxStep.SelectedIndex = ((idx >= 0) ? idx : 0);
			for (int i2 = 1; i2 <= 50; i2++)
			{
				decimal? amount2 = cache10.GetValueOrDefault(i2);
				dgvBetAmounts.Rows.Add(i2, DBNull.Value, amount2.HasValue ? ((object)amount2.Value) : DBNull.Value);
			}
			break;
		}
		case 6:
		{
			dgvBetAmounts.Visible = false;
			pnlBulkSet.Visible = false;
			pnl1326.Visible = true;
			decimal? baseAmount2 = _typeDataCache.GetValueOrDefault(6)?.GetValueOrDefault(1);
			txt1326BaseAmount.Text = ((baseAmount2.HasValue && baseAmount2.Value > 0m) ? baseAmount2.Value.ToString("N0") : "");
			break;
		}
		case 7:
		{
			dgvBetAmounts.Visible = false;
			pnlBulkSet.Visible = false;
			pnlOscarGrind.Visible = true;
			decimal? baseAmount3 = _typeDataCache.GetValueOrDefault(7)?.GetValueOrDefault(1);
			txtOscarBaseAmount.Text = ((baseAmount3.HasValue && baseAmount3.Value > 0m) ? baseAmount3.Value.ToString("N0") : "");
			nudOscarTarget.Value = Math.Clamp(_oscarTargetUnitsCache, 1, 50);
			break;
		}
		case 8:
		{
			dgvBetAmounts.Visible = false;
			pnlBulkSet.Visible = false;
			pnlFlatBetting.Visible = true;
			decimal? baseAmount = _typeDataCache.GetValueOrDefault(8)?.GetValueOrDefault(1);
			txtFlatAmount.Text = ((baseAmount.HasValue && baseAmount.Value > 0m) ? baseAmount.Value.ToString("N0") : "");
			break;
		}
		case 15:
		{
			dgvBetAmounts.Visible = false;
			pnlBulkSet.Visible = false;
			EnsureUpchigiPanel();
			_pnlUpchigi.Visible = true;
			_pnlUpchigi.BringToFront();
			Dictionary<int, decimal?> up = _typeDataCache.GetValueOrDefault(15) ?? new Dictionary<int, decimal?>();
			_txtUpchigiStep.Text = ((_upchigiStepCountCache > 0) ? _upchigiStepCountCache.ToString() : "");
			decimal? upStart = up.GetValueOrDefault(1);
			decimal? upFlat = up.GetValueOrDefault(2);
			decimal? upThreshold = up.GetValueOrDefault(3);
			_txtUpchigiStart.Text = ((upStart.HasValue && upStart.Value > 0m) ? upStart.Value.ToString("N0") : "");
			_txtUpchigiFlat.Text = ((upFlat.HasValue && upFlat.Value > 0m) ? upFlat.Value.ToString("N0") : "");
			_txtUpchigiThreshold.Text = ((upThreshold.HasValue && upThreshold.Value > 0m) ? upThreshold.Value.ToString("N0") : "");
			decimal? upFailThr = up.GetValueOrDefault(4);
			decimal? upMult = up.GetValueOrDefault(5);
			decimal? upMaxMult = up.GetValueOrDefault(6);
			_txtUpchigiFailThreshold.Text = ((upFailThr.HasValue && upFailThr.Value > 0m) ? upFailThr.Value.ToString("N0") : "");
			_txtUpchigiMultiplier.Text = ((upMult.HasValue && upMult.Value > 1m) ? upMult.Value.ToString("0.##") : "");
			_txtUpchigiMaxMultiplier.Text = ((upMaxMult.HasValue && upMaxMult.Value > 1m) ? upMaxMult.Value.ToString("0.##") : "");
			break;
		}
		case 9:
		{
			dgvBetAmounts.Visible = false;
			pnlBulkSet.Visible = false;
			pnlLabouchere.Visible = true;
			Dictionary<int, decimal?> cache9 = _typeDataCache.GetValueOrDefault(9);
			if (cache9 != null && cache9.Count > 0)
			{
				IEnumerable<string> seqValues = from keyValuePair in cache9
					where keyValuePair.Value.HasValue && keyValuePair.Value.Value > 0m
					orderby keyValuePair.Key
					select keyValuePair.Value.Value.ToString("0");
				txtLabouchereSequence.Text = string.Join(",", seqValues);
			}
			else
			{
				txtLabouchereSequence.Text = "";
			}
			txtLabouchereMultiplier.Text = ((_labouchereMultiplierCache > 0m) ? _labouchereMultiplierCache.ToString("N0") : "");
			break;
		}
		default:
		{
			bool isMixed = betAmountType == 3;
			colMethod.Visible = false;
			pnlBulkSet.Visible = false;
			EnsureMixedInfoLabel();
			_lblMixedInfo.Visible = isMixed;
			dgvBetAmounts.Visible = !isMixed;
			if (btnSave != null)
			{
				btnSave.Enabled = !isMixed;
			}
			if (isMixed)
			{
				Text = "베팅금 설정 - 혼합 (방별 자동: 1번방=마틴, 2번방=홍콩크루즈, ...)";
				break;
			}
			Text = "베팅금 설정";
			bool isHongKong = betAmountType == 1;
			colHkTarget.Visible = isHongKong;
			pnlHkAutoFill.Visible = isHongKong;
			LayoutGridBelow(isHongKong ? pnlHkAutoFill : null);
			Dictionary<int, decimal?> cache = _typeDataCache.GetValueOrDefault(betAmountType) ?? new Dictionary<int, decimal?>();
			for (int i = 1; i <= 50; i++)
			{
				decimal? amount = cache.GetValueOrDefault(i);
				object target = ((isHongKong && _hongKongTargetCache.TryGetValue(i, out var t)) ? ((object)t) : DBNull.Value);
				dgvBetAmounts.Rows.Add(i, DBNull.Value, amount.HasValue ? ((object)amount.Value) : DBNull.Value, target);
			}
			break;
		}
		}
	}

	private void EnsureMixedInfoLabel()
	{
		if (_lblMixedInfo == null)
		{
			_lblMixedInfo = new Label
			{
				Name = "lblMixedInfo",
				AutoSize = false,
				Location = new Point(12, 50),
				Size = new Size(dgvBetAmounts.Width, dgvBetAmounts.Height),
				Font = new Font("맑은 고딕", 10f),
				ForeColor = Color.DarkSlateGray,
				TextAlign = ContentAlignment.TopLeft,
				Padding = new Padding(16),
				BorderStyle = BorderStyle.FixedSingle,
				Text = "※ 혼합 모드 안내\r\n\r\n방 인덱스에 따라 방식이 자동으로 정해집니다.\r\n  • 방 1, 3, 5... = 마틴 (위 '마틴' 탭에서 금액 설정)\r\n  • 방 2, 4, 6... = 홍콩크루즈 (위 '홍콩크루즈' 탭에서 금액 설정)\r\n\r\n어느 한 쪽(마틴 또는 홍콩크루즈)을 비워두면\r\n해당 방들은 휴업(0원 베팅)으로 동작합니다.\r\n\r\n혼합 탭에서는 별도 저장이 필요하지 않습니다."
			};
			base.Controls.Add(_lblMixedInfo);
			_lblMixedInfo.BringToFront();
		}
	}

	private void SaveGridToCache()
	{
		if (_currentBetAmountType == 3)
		{
			return;
		}
		if (IsMatrixType(_currentBetAmountType))
		{
			SaveMatrixGridToCache();
			return;
		}
		if (_currentBetAmountType == 10)
		{
			_dalmenzingMaxStepCache = ((cmbDalmenzingMaxStep.SelectedItem is int step) ? step : 0);
			_dalmenzingRecoveryMethodCache = ((cmbDalmenzingRecoveryMethod.SelectedIndex == 1) ? 1 : 0);
			_typeDataCache[10] = new Dictionary<int, decimal?>
			{
				[1] = null,
				[2] = null
			};
			return;
		}
		if (_currentBetAmountType == 4)
		{
			Dictionary<int, decimal?> dalembertDict = new Dictionary<int, decimal?>();
			if (decimal.TryParse(txtDAlembertBaseAmount.Text.Replace(",", "").Trim(), out var baseAmount) && baseAmount > 0m)
			{
				dalembertDict[1] = baseAmount;
			}
			_typeDataCache[4] = dalembertDict;
			return;
		}
		if (_currentBetAmountType == 5)
		{
			Dictionary<int, decimal?> dict5 = new Dictionary<int, decimal?>();
			for (int i = 0; i < dgvBetAmounts.Rows.Count; i++)
			{
				if (!(dgvBetAmounts.Rows[i].Cells[0].Value is int number))
				{
					continue;
				}
				object amountValue = dgvBetAmounts.Rows[i].Cells[colAmount.Index].Value;
				decimal? amt = null;
				if (amountValue != null && amountValue != DBNull.Value)
				{
					decimal parsed;
					if (amountValue is decimal decAmt)
					{
						amt = decAmt;
					}
					else if (decimal.TryParse(amountValue.ToString()?.Replace(",", ""), out parsed))
					{
						amt = parsed;
					}
				}
				dict5[number] = amt;
			}
			_typeDataCache[5] = dict5;
			_martinMaxStepCache = ((cmbMartinMaxStep.SelectedItem is int step2) ? step2 : 0);
			return;
		}
		if (_currentBetAmountType == 6)
		{
			Dictionary<int, decimal?> dict6 = new Dictionary<int, decimal?>();
			if (decimal.TryParse(txt1326BaseAmount.Text.Replace(",", "").Trim(), out var baseAmount2) && baseAmount2 > 0m)
			{
				dict6[1] = baseAmount2;
			}
			_typeDataCache[6] = dict6;
			return;
		}
		if (_currentBetAmountType == 7)
		{
			Dictionary<int, decimal?> dict7 = new Dictionary<int, decimal?>();
			if (decimal.TryParse(txtOscarBaseAmount.Text.Replace(",", "").Trim(), out var baseAmount3) && baseAmount3 > 0m)
			{
				dict7[1] = baseAmount3;
			}
			_typeDataCache[7] = dict7;
			_oscarTargetUnitsCache = (int)nudOscarTarget.Value;
			return;
		}
		if (_currentBetAmountType == 8)
		{
			Dictionary<int, decimal?> dict8 = new Dictionary<int, decimal?>();
			if (decimal.TryParse(txtFlatAmount.Text.Replace(",", "").Trim(), out var baseAmount4) && baseAmount4 > 0m)
			{
				dict8[1] = baseAmount4;
			}
			_typeDataCache[8] = dict8;
			return;
		}
		if (_currentBetAmountType == 15)
		{
			// Number 1=시작금액, 2=일정금액, 3=합산수익; 단계수 N은 _upchigiStepCountCache(저장 시 Number1의 BettingMethod로).
			Dictionary<int, decimal?> dict15 = new Dictionary<int, decimal?>();
			if (_txtUpchigiStart != null && decimal.TryParse(_txtUpchigiStart.Text.Replace(",", "").Trim(), out var upStartVal) && upStartVal > 0m)
			{
				dict15[1] = upStartVal;
			}
			if (_txtUpchigiFlat != null && decimal.TryParse(_txtUpchigiFlat.Text.Replace(",", "").Trim(), out var upFlatVal) && upFlatVal > 0m)
			{
				dict15[2] = upFlatVal;
			}
			if (_txtUpchigiThreshold != null && decimal.TryParse(_txtUpchigiThreshold.Text.Replace(",", "").Trim(), out var upThrVal) && upThrVal > 0m)
			{
				dict15[3] = upThrVal;
			}
			if (_txtUpchigiFailThreshold != null && decimal.TryParse(_txtUpchigiFailThreshold.Text.Replace(",", "").Trim(), out var upFailThrVal) && upFailThrVal > 0m)
			{
				dict15[4] = upFailThrVal;
			}
			if (_txtUpchigiMultiplier != null && decimal.TryParse(_txtUpchigiMultiplier.Text.Replace(",", "").Trim(), out var upMultVal) && upMultVal > 1m)
			{
				dict15[5] = upMultVal;
			}
			if (_txtUpchigiMaxMultiplier != null && decimal.TryParse(_txtUpchigiMaxMultiplier.Text.Replace(",", "").Trim(), out var upMaxMultVal) && upMaxMultVal > 1m)
			{
				dict15[6] = upMaxMultVal;
			}
			_typeDataCache[15] = dict15;
			_upchigiStepCountCache = ((_txtUpchigiStep != null && int.TryParse(_txtUpchigiStep.Text.Trim(), out var upN) && upN > 0) ? upN : 1);
			return;
		}
		if (_currentBetAmountType == 9)
		{
			Dictionary<int, decimal?> dict9 = new Dictionary<int, decimal?>();
			string seqText = txtLabouchereSequence.Text.Trim();
			if (!string.IsNullOrEmpty(seqText))
			{
				string[] parts = seqText.Split(',', StringSplitOptions.RemoveEmptyEntries);
				for (int j = 0; j < parts.Length; j++)
				{
					if (decimal.TryParse(parts[j].Trim(), out var val) && val > 0m)
					{
						dict9[j + 1] = val;
					}
				}
			}
			_typeDataCache[9] = dict9;
			if (decimal.TryParse(txtLabouchereMultiplier.Text.Replace(",", "").Trim(), out var mult) && mult > 0m)
			{
				_labouchereMultiplierCache = mult;
			}
			return;
		}
		Dictionary<int, decimal?> dict10 = new Dictionary<int, decimal?>();
		bool isMixed = _currentBetAmountType == 3;
		bool isHongKong = _currentBetAmountType == 1;
		int amountColIndex = colAmount.Index;
		for (int k = 0; k < dgvBetAmounts.Rows.Count; k++)
		{
			if (!(dgvBetAmounts.Rows[k].Cells[0].Value is int number2))
			{
				continue;
			}
			if (isMixed)
			{
				object methodValue = dgvBetAmounts.Rows[k].Cells[colMethod.Index].Value;
				_mixedMethodCache[number2] = ((methodValue?.ToString() == "홍콩크루즈") ? 1 : 0);
			}
			if (isHongKong)
			{
				object targetValue = dgvBetAmounts.Rows[k].Cells[colHkTarget.Index].Value;
				if (targetValue != null && targetValue != DBNull.Value && int.TryParse(targetValue.ToString()?.Trim(), out var targetStep) && targetStep >= 1 && targetStep <= 50)
				{
					_hongKongTargetCache[number2] = targetStep;
				}
				else
				{
					_hongKongTargetCache.Remove(number2);
				}
			}
			object amountValue2 = dgvBetAmounts.Rows[k].Cells[amountColIndex].Value;
			decimal? amount = null;
			if (amountValue2 != null && amountValue2 != DBNull.Value)
			{
				decimal parsedAmount;
				if (amountValue2 is decimal decimalAmount)
				{
					amount = decimalAmount;
				}
				else if (decimal.TryParse(amountValue2.ToString()?.Replace(",", ""), out parsedAmount))
				{
					amount = parsedAmount;
				}
			}
			dict10[number2] = amount;
		}
		_typeDataCache[_currentBetAmountType] = dict10;
	}

	private void CmbBetAmountType_SelectedIndexChanged(object? sender, EventArgs e)
	{
		SaveGridToCache();
		_currentBetAmountType = ComboIndexToType(cmbBetAmountType.SelectedIndex);
		DisplayTypeData(_currentBetAmountType);
	}

	private static int ComboIndexToType(int idx)
	{
		if (idx < 0 || idx >= BetAmountOptions.Length)
		{
			return BetAmountOptions[0].Type;
		}
		return BetAmountOptions[idx].Type;
	}

	private void BuildMatrixGrid()
	{
		dgvMatrix.Rows.Clear();
		dgvMatrix.Columns.Clear();
		for (int col = 1; col <= 20; col++)
		{
			DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn
			{
				HeaderText = $"{col}단계",
				Name = $"colMatrix{col}",
				Width = 80,
				SortMode = DataGridViewColumnSortMode.NotSortable,
				DefaultCellStyle = new DataGridViewCellStyle
				{
					Alignment = DataGridViewContentAlignment.MiddleRight,
					BackColor = Color.FromArgb(60, 60, 65),
					ForeColor = Color.FromArgb(234, 234, 234),
					SelectionBackColor = Color.FromArgb(80, 80, 90),
					SelectionForeColor = Color.FromArgb(234, 234, 234)
				}
			};
			dgvMatrix.Columns.Add(c);
		}
		DataGridViewCellStyle ruleCellStyle = new DataGridViewCellStyle
		{
			Alignment = DataGridViewContentAlignment.MiddleCenter,
			BackColor = Color.FromArgb(70, 65, 45),
			ForeColor = Color.FromArgb(245, 245, 220),
			SelectionBackColor = Color.FromArgb(95, 88, 60),
			SelectionForeColor = Color.FromArgb(245, 245, 220)
		};
		bool usesRowRules = UsesMatrixRowRules(_currentBetAmountType);
		dgvMatrix.Columns.Add(new DataGridViewTextBoxColumn
		{
			HeaderText = "당첨갯수",
			Name = "colMatrixWin",
			Width = 80,
			SortMode = DataGridViewColumnSortMode.NotSortable,
			Visible = usesRowRules,
			DefaultCellStyle = ruleCellStyle
		});
		dgvMatrix.Columns.Add(new DataGridViewTextBoxColumn
		{
			HeaderText = "이동차수(1~15)",
			Name = "colMatrixTarget",
			Width = 100,
			SortMode = DataGridViewColumnSortMode.NotSortable,
			Visible = usesRowRules,
			DefaultCellStyle = ruleCellStyle
		});
		dgvMatrix.Columns.Add(new DataGridViewCheckBoxColumn
		{
			HeaderText = "달랑",
			Name = "colMatrixWinMode",
			Width = 60,
			SortMode = DataGridViewColumnSortMode.NotSortable,
			Visible = (_currentBetAmountType == 13),
			DefaultCellStyle = new DataGridViewCellStyle
			{
				Alignment = DataGridViewContentAlignment.MiddleCenter,
				BackColor = Color.FromArgb(45, 70, 60),
				ForeColor = Color.FromArgb(220, 245, 230),
				SelectionBackColor = Color.FromArgb(65, 95, 82),
				SelectionForeColor = Color.FromArgb(220, 245, 230)
			}
		});
		dgvMatrix.Columns.Add(new DataGridViewTextBoxColumn
		{
			HeaderText = "쉬는타임(분)",
			Name = "colMatrixRest",
			Width = 90,
			SortMode = DataGridViewColumnSortMode.NotSortable,
			Visible = SupportsMatrixRest(_currentBetAmountType),
			DefaultCellStyle = new DataGridViewCellStyle
			{
				Alignment = DataGridViewContentAlignment.MiddleCenter,
				BackColor = Color.FromArgb(70, 50, 65),
				ForeColor = Color.FromArgb(245, 225, 240),
				SelectionBackColor = Color.FromArgb(95, 70, 88),
				SelectionForeColor = Color.FromArgb(245, 225, 240)
			}
		});
		dgvMatrix.Columns.Add(new DataGridViewTextBoxColumn
		{
			HeaderText = "1차 상한금액",
			Name = "colMatrixFirstRowCap",
			Width = 110,
			SortMode = DataGridViewColumnSortMode.NotSortable,
			DefaultCellStyle = new DataGridViewCellStyle
			{
				Alignment = DataGridViewContentAlignment.MiddleRight,
				BackColor = Color.FromArgb(72, 58, 40),
				ForeColor = Color.FromArgb(248, 232, 205),
				SelectionBackColor = Color.FromArgb(98, 80, 56),
				SelectionForeColor = Color.FromArgb(248, 232, 205)
			}
		});
		DataGridViewCellStyle inactiveStyle = new DataGridViewCellStyle
		{
			BackColor = Color.FromArgb(40, 40, 45),
			ForeColor = Color.FromArgb(80, 80, 85),
			SelectionBackColor = Color.FromArgb(40, 40, 45),
			SelectionForeColor = Color.FromArgb(80, 80, 85)
		};
		for (int row = 1; row <= MatrixStepsPerRow.Length; row++)
		{
			int rowIdx = dgvMatrix.Rows.Add();
			DataGridViewRow dgvRow = dgvMatrix.Rows[rowIdx];
			dgvRow.HeaderCell.Value = $"{row}차";
			int maxStepForRow = MatrixStepsPerRow[row - 1];
			for (int i = 1; i <= 20; i++)
			{
				DataGridViewCell cell = dgvRow.Cells[i - 1];
				decimal amt;
				if (i > maxStepForRow)
				{
					cell.Style = inactiveStyle;
					cell.ReadOnly = true;
					cell.Value = null;
				}
				else if (CurrentMatrixCells.TryGetValue((row, i), out amt) && amt >= 0m)
				{
					cell.Value = amt;
				}
			}
			dgvRow.Cells[22].Value = false;
			if (row != 1)
			{
				dgvRow.Cells[24].Style = inactiveStyle;
				dgvRow.Cells[24].ReadOnly = true;
				dgvRow.Cells[24].Value = null;
			}
			if (CurrentMatrixRowRules.TryGetValue(row, out (int, int, int, int, decimal) rule))
			{
				if (rule.Item1 >= 1 && rule.Item2 >= 1)
				{
					dgvRow.Cells[20].Value = rule.Item1;
					dgvRow.Cells[21].Value = rule.Item2;
				}
				dgvRow.Cells[22].Value = rule.Item3 == 1;
				if (rule.Item4 >= 1)
				{
					dgvRow.Cells[23].Value = rule.Item4;
				}
				if (row == 1 && rule.Item5 >= 1m)
				{
					dgvRow.Cells[24].Value = rule.Item5;
				}
			}
		}
	}

	private void DgvMatrix_DataError(object? sender, DataGridViewDataErrorEventArgs e)
	{
		e.ThrowException = false;
		e.Cancel = true;
		FileLogger.Log($"[BetAmountSettingForm] 행렬 그리드 DataError (행={e.RowIndex}, 열={e.ColumnIndex}): {e.Exception?.Message}", "Warning", "UI");
	}

	private void DgvMatrix_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
	{
		int columnIndex = e.ColumnIndex;
		if ((columnIndex < 20 || columnIndex > 23) && e.Value is decimal amount)
		{
			e.Value = amount.ToString("N0");
			e.FormattingApplied = true;
		}
	}

	private void DgvMatrix_CellParsing(object? sender, DataGridViewCellParsingEventArgs e)
	{
		int columnIndex = e.ColumnIndex;
		if (columnIndex >= 20 && columnIndex <= 23)
		{
			return;
		}
		if (e.Value == null)
		{
			e.ParsingApplied = true;
			return;
		}
		string input = e.Value.ToString()?.Replace(",", "").Trim();
		decimal amount;
		if (string.IsNullOrEmpty(input))
		{
			e.Value = null;
		}
		else if (decimal.TryParse(input, out amount) && amount >= 0m)
		{
			e.Value = amount;
		}
		else
		{
			e.Value = null;
		}
		e.ParsingApplied = true;
	}

	private void DgvMatrix_EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
	{
		if (e.Control is TextBox textBox)
		{
			textBox.KeyPress -= TextBox_KeyPress;
			textBox.KeyPress += TextBox_KeyPress;
		}
	}

	private void BtnMatrixDefaults_Click(object? sender, EventArgs e)
	{
		for (int row = 0; row < MatrixDefaults.Length; row++)
		{
			decimal[] rowDefaults = MatrixDefaults[row];
			for (int col = 0; col < rowDefaults.Length; col++)
			{
				dgvMatrix.Rows[row].Cells[col].Value = rowDefaults[col];
			}
		}
	}

	private void SaveMatrixGridToCache()
	{
		dgvMatrix.EndEdit();
		CurrentMatrixCells.Clear();
		CurrentMatrixRowRules.Clear();
		bool isSwitching = _currentBetAmountType == 13;
		bool isStair = _currentBetAmountType == 14;
		bool supportsRest = SupportsMatrixRest(_currentBetAmountType);
		bool usesRowRules = UsesMatrixRowRules(_currentBetAmountType);
		bool checkedMode = default(bool);
		for (int row = 1; row <= MatrixStepsPerRow.Length; row++)
		{
			DataGridViewRow dgvRow = dgvMatrix.Rows[row - 1];
			int maxStepForRow = MatrixStepsPerRow[row - 1];
			for (int col = 1; col <= maxStepForRow; col++)
			{
				object raw = dgvRow.Cells[col - 1].Value;
				decimal? amt = null;
				decimal p;
				if (raw is decimal d)
				{
					amt = d;
				}
				else if (raw != null && decimal.TryParse(raw.ToString()?.Replace(",", ""), out p))
				{
					amt = p;
				}
				if (amt >= (decimal?)0m)
				{
					CurrentMatrixCells[(row, col)] = amt.Value;
				}
			}
			int? winCount = null;
			int? targetRow = null;
			object winRaw = dgvRow.Cells[20].Value;
			int wp;
			if (winRaw is int wi && wi >= 1)
			{
				winCount = wi;
			}
			else if (winRaw != null && int.TryParse(winRaw.ToString()?.Replace(",", "").Trim(), out wp) && wp >= 1)
			{
				winCount = wp;
			}
			object targetRaw = dgvRow.Cells[21].Value;
			int tp;
			if (targetRaw is int ti && ti >= 1 && ti <= 15)
			{
				targetRow = ti;
			}
			else if (targetRaw != null && int.TryParse(targetRaw.ToString()?.Trim(), out tp) && tp >= 1 && tp <= 15)
			{
				targetRow = tp;
			}
			int num;
			if (isSwitching)
			{
				object value = dgvRow.Cells[22].Value;
				if (value is bool)
				{
					checkedMode = (bool)value;
					num = 1;
				}
				else
				{
					num = 0;
				}
			}
			else
			{
				num = 0;
			}
			int winMode = ((((uint)num & (checkedMode ? 1u : 0u)) != 0) ? 1 : 0);
			if (isStair && row == 1 && chkStairStepBack.Checked)
			{
				winMode = 1;
			}
			int restMinutes = 0;
			if (supportsRest)
			{
				object restRaw = dgvRow.Cells[23].Value;
				int rp;
				if (restRaw is int ri)
				{
					restMinutes = ri;
				}
				else if (restRaw != null && int.TryParse(restRaw.ToString()?.Replace(",", "").Trim(), out rp))
				{
					restMinutes = rp;
				}
				restMinutes = Math.Clamp(restMinutes, 0, 120);
			}
			decimal firstRowCapAmount = 0m;
			if (row == 1)
			{
				object capRaw = dgvRow.Cells[24].Value;
				decimal cp;
				if (capRaw is decimal cd)
				{
					firstRowCapAmount = cd;
				}
				else if (capRaw != null && decimal.TryParse(capRaw.ToString()?.Replace(",", "").Trim(), out cp))
				{
					firstRowCapAmount = cp;
				}
				firstRowCapAmount = ((firstRowCapAmount >= 1m) ? decimal.Truncate(firstRowCapAmount) : 0m);
			}
			bool hasMoveRule = usesRowRules && winCount.HasValue && targetRow.HasValue;
			if (hasMoveRule || winMode == 1 || restMinutes >= 1 || firstRowCapAmount >= 1m)
			{
				CurrentMatrixRowRules[row] = (hasMoveRule ? winCount.Value : 0, hasMoveRule ? targetRow.Value : 0, winMode, restMinutes, firstRowCapAmount);
			}
		}
	}

	private void DgvBetAmounts_EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
	{
		if (dgvBetAmounts.CurrentCell?.ColumnIndex == colAmount.Index && e.Control is TextBox textBox)
		{
			textBox.KeyPress -= TextBox_KeyPress;
			textBox.KeyPress += TextBox_KeyPress;
		}
	}

	private void TextBox_KeyPress(object? sender, KeyPressEventArgs e)
	{
		if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar))
		{
			e.Handled = true;
		}
	}

	private void DgvBetAmounts_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
	{
		if (e.ColumnIndex == colAmount.Index && e.Value != null && e.Value is decimal amount)
		{
			e.Value = amount.ToString("N0");
			e.FormattingApplied = true;
		}
	}

	private void DgvBetAmounts_CellParsing(object? sender, DataGridViewCellParsingEventArgs e)
	{
		if (e.ColumnIndex == colAmount.Index && e.Value != null)
		{
			string input = e.Value.ToString()?.Replace(",", "").Trim();
			decimal amount;
			if (string.IsNullOrEmpty(input))
			{
				e.Value = null;
			}
			else if (decimal.TryParse(input, out amount))
			{
				e.Value = amount;
			}
			else
			{
				e.Value = null;
			}
			e.ParsingApplied = true;
		}
	}

	private void BtnApplyBulk_Click(object? sender, EventArgs e)
	{
		int start = (int)nudStartStep.Value;
		int end = (int)nudEndStep.Value;
		if (start > end)
		{
			MessageBox.Show("시작단계가 끝단계보다 클 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		string methodText = ((cmbBulkMethod.SelectedIndex == 1) ? "홍콩크루즈" : "마틴");
		for (int i = 0; i < dgvBetAmounts.Rows.Count; i++)
		{
			if (dgvBetAmounts.Rows[i].Cells[0].Value is int number && number >= start && number <= end)
			{
				dgvBetAmounts.Rows[i].Cells[colMethod.Index].Value = methodText;
			}
		}
	}

	private static List<decimal> BuildHongKongAmounts(decimal startAmount, int steps, decimal profit, decimal recoveryMultiplier, decimal roundUnit)
	{
		List<decimal> amounts = new List<decimal>(steps);
		decimal cumulativeLoss = 0m;
		for (int step = 1; step <= steps; step++)
		{
			decimal amount;
			if (step == 1)
			{
				amount = startAmount;
			}
			else
			{
				amount = Math.Ceiling((cumulativeLoss + profit) / recoveryMultiplier / roundUnit) * roundUnit;
				amount = Math.Max(amounts[step - 2], amount);
			}
			amounts.Add(amount);
			cumulativeLoss += amount;
		}
		return amounts;
	}

	private void BtnHkAutoFill_Click(object? sender, EventArgs e)
	{
		if (!decimal.TryParse(txtHkStartAmount.Text.Replace(",", "").Trim(), out var startAmount) || startAmount <= 0m)
		{
			MessageBox.Show("시작금액을 1원 이상으로 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			txtHkStartAmount.Focus();
			return;
		}
		decimal profit = startAmount;
		int steps = (int)nudHkSteps.Value;
		decimal roundUnit = Math.Min(1000m, startAmount);
		decimal multiplier = (chkHkCommission.Checked ? 2.85m : 3.0m);
		if (dgvBetAmounts.Rows.Cast<DataGridViewRow>().Any((DataGridViewRow r) => r.Cells[colAmount.Index].Value is decimal num && num > 0m) && MessageBox.Show($"1~{steps}단계 금액을 새로 계산한 값으로 덮어쓰고, {steps + 1}단계부터는 비웁니다.\n계속할까요?", "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			return;
		}
		List<decimal> amounts = BuildHongKongAmounts(startAmount, steps, profit, multiplier, roundUnit);
		foreach (DataGridViewRow row in (IEnumerable)dgvBetAmounts.Rows)
		{
			if (row.Cells[0].Value is int number)
			{
				row.Cells[colAmount.Index].Value = ((number <= steps) ? ((object)amounts[number - 1]) : DBNull.Value);
			}
		}
		decimal totalNeeded = amounts.Sum();
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(78, 4);
		defaultInterpolatedStringHandler.AppendFormatted(steps);
		defaultInterpolatedStringHandler.AppendLiteral("단계까지 채웠습니다.\n\n");
		defaultInterpolatedStringHandler.AppendLiteral("• 마지막 단계 베팅금: ");
		defaultInterpolatedStringHandler.AppendFormatted(amounts[amounts.Count - 1], "N0");
		defaultInterpolatedStringHandler.AppendLiteral("원\n");
		defaultInterpolatedStringHandler.AppendLiteral("• 전 단계 소진 시 필요자금: ");
		defaultInterpolatedStringHandler.AppendFormatted(totalNeeded, "N0");
		defaultInterpolatedStringHandler.AppendLiteral("원\n");
		defaultInterpolatedStringHandler.AppendLiteral("• 어느 단계에서 2연승해도 순이익 ");
		defaultInterpolatedStringHandler.AppendFormatted(profit, "N0");
		defaultInterpolatedStringHandler.AppendLiteral("원 이상 확보\n\n");
		MessageBox.Show(defaultInterpolatedStringHandler.ToStringAndClear() + "저장 버튼을 눌러야 서버에 반영됩니다.", "자동 채움 완료", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private async void BtnSave_Click(object? sender, EventArgs e)
	{
		_ = 8;
		try
		{
			btnSave.Enabled = false;
			SaveGridToCache();
			List<Task> saveTasks = new List<Task>();
			ApiClient api = AuthService.Api;
			int[] matrixBetAmountTypes;
			if (api != null)
			{
				matrixBetAmountTypes = MatrixBetAmountTypes;
				foreach (int type in matrixBetAmountTypes)
				{
					List<MatrixCellDto> matrixDtos = (from kv in _matrixCellsCacheByType[type]
						where kv.Value >= 0m
						select new MatrixCellDto(kv.Key.Row, kv.Key.Col, kv.Value)).ToList();
					saveTasks.Add(api.SaveMatrixCellsAsync(matrixDtos, type));
					List<MatrixRowRuleDto> matrixRowRuleDtos = (from kv in _matrixRowRulesCacheByType[type].Where(delegate(KeyValuePair<int, (int WinCount, int TargetRow, int WinMode, int RestMinutes, decimal FirstRowCapAmount)> kv)
						{
							if (kv.Value.WinCount >= 1)
							{
								int item = kv.Value.TargetRow;
								if (item >= 1 && item <= 15)
								{
									goto IL_005a;
								}
							}
							if (kv.Value.WinMode != 1 && kv.Value.RestMinutes < 1)
							{
								return kv.Value.FirstRowCapAmount >= 1m;
							}
							goto IL_005a;
							IL_005a:
							return true;
						})
						select new MatrixRowRuleDto(kv.Key, kv.Value.WinCount, kv.Value.TargetRow, kv.Value.WinMode, kv.Value.RestMinutes, kv.Value.FirstRowCapAmount)).ToList();
					saveTasks.Add(api.SaveMatrixRowRulesAsync(matrixRowRuleDtos, type));
				}
			}
			int[] saveTypes = new int[12]
			{
				0, 1, 2, 3, 4, 5, 6, 7, 8, 9,
				10, 15
			};
			matrixBetAmountTypes = saveTypes;
			foreach (int type2 in matrixBetAmountTypes)
			{
				if (!_typeDataCache.TryGetValue(type2, out Dictionary<int, decimal?> data))
				{
					continue;
				}
				switch (type2)
				{
				case 3:
				{
					Dictionary<int, (decimal?, int?)> mixedData = data.ToDictionary((KeyValuePair<int, decimal?> kv) => kv.Key, (KeyValuePair<int, decimal?> kv) => (Value: kv.Value, _mixedMethodCache.GetValueOrDefault(kv.Key)));
					saveTasks.Add(_betAmountSettingService.SaveMixedBetAmountsAsync(_userId, mixedData));
					break;
				}
				case 1:
				{
					Dictionary<int, (decimal?, int?)> hongKongData = data.ToDictionary((KeyValuePair<int, decimal?> kv) => kv.Key, (KeyValuePair<int, decimal?> kv) => (Value: kv.Value, _hongKongTargetCache.TryGetValue(kv.Key, out var value) ? new int?(value) : ((int?)null)));
					saveTasks.Add(_betAmountSettingService.SaveMartinDAlembertBetAmountsAsync(_userId, hongKongData, 1));
					break;
				}
				case 5:
				{
					Dictionary<int, (decimal?, int?)> martinDAlembertData = data.ToDictionary((KeyValuePair<int, decimal?> kv) => kv.Key, (KeyValuePair<int, decimal?> kv) => (Value: kv.Value, (kv.Key == 1) ? new int?(_martinMaxStepCache) : ((int?)null)));
					saveTasks.Add(_betAmountSettingService.SaveMartinDAlembertBetAmountsAsync(_userId, martinDAlembertData));
					break;
				}
				case 7:
				{
					Dictionary<int, (decimal?, int?)> oscarData = data.ToDictionary((KeyValuePair<int, decimal?> kv) => kv.Key, (KeyValuePair<int, decimal?> kv) => (Value: kv.Value, (kv.Key == 1) ? new int?(_oscarTargetUnitsCache) : ((int?)null)));
					saveTasks.Add(_betAmountSettingService.SaveMartinDAlembertBetAmountsAsync(_userId, oscarData, 7));
					break;
				}
				case 9:
				{
					Dictionary<int, (decimal?, int?)> labData = data.ToDictionary((KeyValuePair<int, decimal?> kv) => kv.Key, (KeyValuePair<int, decimal?> kv) => (Value: kv.Value, (kv.Key == 1) ? new int?((int)_labouchereMultiplierCache) : ((int?)null)));
					saveTasks.Add(_betAmountSettingService.SaveMartinDAlembertBetAmountsAsync(_userId, labData, 9));
					break;
				}
				case 10:
				{
					Dictionary<int, (decimal?, int?)> dalmenzingData = new Dictionary<int, (decimal?, int?)>
					{
						[1] = (0m, _dalmenzingMaxStepCache),
						[2] = (0m, _dalmenzingRecoveryMethodCache)
					};
					saveTasks.Add(_betAmountSettingService.SaveMartinDAlembertBetAmountsAsync(_userId, dalmenzingData, 10));
					break;
				}
				case 15:
				{
					// Number1=시작금액(+ BettingMethod=단계수 N), 2=일정금액, 3=합산수익, 4=누적실패 임계, 5=실패 시 배수, 6=배수 상한
					Dictionary<int, (decimal?, int?)> upchigiData = data.ToDictionary((KeyValuePair<int, decimal?> kv) => kv.Key, (KeyValuePair<int, decimal?> kv) => (Value: kv.Value, (kv.Key == 1) ? new int?(_upchigiStepCountCache) : ((int?)null)));
					saveTasks.Add(_betAmountSettingService.SaveMartinDAlembertBetAmountsAsync(_userId, upchigiData, 15));
					break;
				}
				default:
					saveTasks.Add(_betAmountSettingService.SaveUserBetAmountsAsync(_userId, data, type2));
					break;
				}
			}
			// [성능] 위에서 모은 저장 요청을 동시에 처리(순차 최대 19왕복 -> 겹쳐서 처리).
			await Task.WhenAll(saveTasks);
			MessageBox.Show("베팅금 설정이 저장되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
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
