using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;

namespace PragmaticBot;

public partial class MatrixStatsForm : Form
{
	private readonly MatrixStatsService _statsService = new MatrixStatsService();

	private const int RowCount = 15;

	private const int FixedColumns = 1;

	private static readonly (string Name, int Type)[] MatrixTypes = new(string, int)[3]
	{
		("행렬", 11),
		("스위칭행렬", 13),
		("달랑행렬", 14)
	};

	private static readonly (string Name, int? Days)[] Periods = new(string, int?)[5]
	{
		("오늘", 0),
		("최근 3일", 3),
		("최근 7일", 7),
		("최근 30일", 30),
		("전체", null)
	};

	private IContainer components;

	public MatrixStatsForm()
	{
		InitializeComponent();
	}

	private async void MatrixStatsForm_Load(object? sender, EventArgs e)
	{
		cmbType.Items.AddRange(((IEnumerable<(string, int)>)MatrixTypes).Select((Func<(string, int), object>)(((string Name, int Type) t) => t.Name)).ToArray());
		cmbType.SelectedIndex = 0;
		cmbPeriod.Items.AddRange(((IEnumerable<(string, int?)>)Periods).Select((Func<(string, int?), object>)(((string Name, int? Days) p) => p.Name)).ToArray());
		cmbPeriod.SelectedIndex = 2;
		BuildGridLayout();
		cmbType.SelectedIndexChanged += Filter_Changed;
		cmbPeriod.SelectedIndexChanged += Filter_Changed;
		chkIncludeVirtual.CheckedChanged += Filter_Changed;
		btnRefresh.Click += Filter_Changed;
		await LoadStatsAsync();
	}

	private async void Filter_Changed(object? sender, EventArgs e)
	{
		await LoadStatsAsync();
	}

	private void BuildGridLayout()
	{
		dgvStats.Columns.Clear();
		dgvStats.Rows.Clear();
		dgvStats.BackgroundColor = Color.FromArgb(30, 41, 70);
		dgvStats.GridColor = Color.FromArgb(60, 72, 105);
		dgvStats.DefaultCellStyle.BackColor = Color.FromArgb(30, 41, 70);
		dgvStats.DefaultCellStyle.ForeColor = Color.FromArgb(234, 234, 234);
		dgvStats.DefaultCellStyle.SelectionBackColor = Color.FromArgb(60, 90, 140);
		dgvStats.DefaultCellStyle.SelectionForeColor = Color.White;
		dgvStats.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
		dgvStats.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(22, 33, 62);
		dgvStats.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(234, 234, 234);
		dgvStats.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
		dgvStats.ColumnHeadersDefaultCellStyle.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
		dgvStats.RowTemplate.Height = 34;
		DataGridViewTextBoxColumn rowHeader = new DataGridViewTextBoxColumn
		{
			Name = "colRow",
			HeaderText = "차",
			Width = 60,
			SortMode = DataGridViewColumnSortMode.NotSortable,
			Frozen = true
		};
		rowHeader.DefaultCellStyle.BackColor = Color.FromArgb(22, 33, 62);
		rowHeader.DefaultCellStyle.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
		dgvStats.Columns.Add(rowHeader);
		int maxSteps = AutoBettingContext.MatrixStepsPerRow.Max();
		for (int col = 1; col <= maxSteps; col++)
		{
			dgvStats.Columns.Add(new DataGridViewTextBoxColumn
			{
				Name = $"colStep{col}",
				HeaderText = $"{col}단계",
				Width = 78,
				SortMode = DataGridViewColumnSortMode.NotSortable
			});
		}
		DataGridViewTextBoxColumn totalCol = new DataGridViewTextBoxColumn
		{
			Name = "colTotal",
			HeaderText = "합계",
			Width = 90,
			SortMode = DataGridViewColumnSortMode.NotSortable
		};
		totalCol.DefaultCellStyle.BackColor = Color.FromArgb(22, 33, 62);
		totalCol.DefaultCellStyle.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
		dgvStats.Columns.Add(totalCol);
		DataGridViewTextBoxColumn rateCol = new DataGridViewTextBoxColumn
		{
			Name = "colRate",
			HeaderText = "승률",
			Width = 70,
			SortMode = DataGridViewColumnSortMode.NotSortable
		};
		rateCol.DefaultCellStyle.BackColor = Color.FromArgb(22, 33, 62);
		rateCol.DefaultCellStyle.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
		dgvStats.Columns.Add(rateCol);
		dgvStats.Rows.Add(16);
		for (int r = 0; r < 15; r++)
		{
			dgvStats.Rows[r].Cells[0].Value = $"{r + 1}차";
		}
		DataGridViewRow dataGridViewRow = dgvStats.Rows[15];
		dataGridViewRow.Cells[0].Value = "합계";
		dataGridViewRow.DefaultCellStyle.BackColor = Color.FromArgb(22, 33, 62);
		dataGridViewRow.DefaultCellStyle.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
	}

	private async Task LoadStatsAsync()
	{
		int betAmountType = MatrixTypes[Math.Max(cmbType.SelectedIndex, 0)].Type;
		int? days = Periods[Math.Max(cmbPeriod.SelectedIndex, 0)].Days;
		DateTime? fromLocal = (days.HasValue ? new DateTime?(DateTime.Today.AddDays(-days.Value)) : ((DateTime?)null));
		try
		{
			btnRefresh.Enabled = false;
			Cursor = Cursors.WaitCursor;
			List<MatrixStatDto> stats = await _statsService.GetAsync(fromLocal, null, betAmountType, chkIncludeVirtual.Checked);
			List<MatrixCellDto> cells;
			try
			{
				cells = await (AuthService.Api?.GetMatrixCellsAsync(betAmountType) ?? Task.FromResult(new List<MatrixCellDto>()));
			}
			catch
			{
				cells = new List<MatrixCellDto>();
			}
			FillGrid(stats, cells);
		}
		catch (Exception ex)
		{
			MessageBox.Show("통계를 불러오지 못했습니다: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			Cursor = Cursors.Default;
			btnRefresh.Enabled = true;
		}
	}

	private void FillGrid(List<MatrixStatDto> stats, List<MatrixCellDto> cells)
	{
		Dictionary<(int, int), MatrixStatDto> byCoord = stats.ToDictionary((MatrixStatDto s) => (MatrixRow: s.MatrixRow, MatrixCol: s.MatrixCol));
		HashSet<(int, int)> usedCoords = (from c in cells
			where c.Amount > 0m
			select (RowIndex: c.RowIndex, ColIndex: c.ColIndex)).ToHashSet();
		int maxSteps = AutoBettingContext.MatrixStepsPerRow.Max();
		int[] colWin = new int[maxSteps + 1];
		int[] colLoss = new int[maxSteps + 1];
		int[] colTie = new int[maxSteps + 1];
		for (int row = 1; row <= 15; row++)
		{
			DataGridViewRow gridRow = dgvStats.Rows[row - 1];
			int rowWin = 0;
			int rowLoss = 0;
			int rowTie = 0;
			for (int col = 1; col <= maxSteps; col++)
			{
				DataGridViewCell cell = gridRow.Cells[1 + col - 1];
				bool isUsed = usedCoords.Count == 0 || usedCoords.Contains((row, col));
				cell.Style.BackColor = (isUsed ? Color.FromArgb(30, 41, 70) : Color.FromArgb(24, 30, 46));
				if (!byCoord.TryGetValue((row, col), out var stat))
				{
					cell.Value = (isUsed ? "-" : "");
					cell.Style.ForeColor = Color.FromArgb(90, 100, 120);
					cell.ToolTipText = string.Empty;
					continue;
				}
				rowWin += stat.WinCount;
				rowLoss += stat.LossCount;
				rowTie += stat.TieCount;
				colWin[col] += stat.WinCount;
				colLoss[col] += stat.LossCount;
				colTie[col] += stat.TieCount;
				cell.Value = $"{stat.WinCount} / {stat.LossCount}";
				cell.Style.ForeColor = GetRateColor(stat.WinCount, stat.LossCount);
				cell.ToolTipText = $"{row}차 {col}단계\n당첨 {stat.WinCount}회 / 미당첨 {stat.LossCount}회 / 타이 {stat.TieCount}회\n승률 {FormatRate(stat.WinCount, stat.LossCount)}\n베팅금 합계 {stat.BetAmountSum:N0}원\n손익 합계 {stat.ProfitSum:+#,##0;-#,##0;0}원";
			}
			SetSummaryCells(gridRow, rowWin, rowLoss, rowTie);
		}
		DataGridViewRow totalRow = dgvStats.Rows[15];
		int allWin = 0;
		int allLoss = 0;
		int allTie = 0;
		for (int col2 = 1; col2 <= maxSteps; col2++)
		{
			DataGridViewCell cell2 = totalRow.Cells[1 + col2 - 1];
			if (colWin[col2] + colLoss[col2] + colTie[col2] == 0)
			{
				cell2.Value = "-";
				cell2.Style.ForeColor = Color.FromArgb(90, 100, 120);
				cell2.ToolTipText = string.Empty;
			}
			else
			{
				cell2.Value = $"{colWin[col2]} / {colLoss[col2]}";
				cell2.Style.ForeColor = GetRateColor(colWin[col2], colLoss[col2]);
				cell2.ToolTipText = $"{col2}단계 전체 차 합계\n당첨 {colWin[col2]}회 / 미당첨 {colLoss[col2]}회 / 타이 {colTie[col2]}회\n승률 {FormatRate(colWin[col2], colLoss[col2])}";
			}
			allWin += colWin[col2];
			allLoss += colLoss[col2];
			allTie += colTie[col2];
		}
		SetSummaryCells(totalRow, allWin, allLoss, allTie);
		int settled = allWin + allLoss + allTie;
		decimal profitSum = stats.Sum((MatrixStatDto s) => s.ProfitSum);
		lblSummary.Text = ((settled == 0) ? "이 조건에 해당하는 기록이 없습니다." : $"판정 완료 {settled:N0}건 · 당첨 {allWin:N0} / 미당첨 {allLoss:N0} / 타이 {allTie:N0} · 승률 {FormatRate(allWin, allLoss)} · 손익 {profitSum:+#,##0;-#,##0;0}원");
	}

	private void SetSummaryCells(DataGridViewRow gridRow, int win, int loss, int tie)
	{
		DataGridViewCell totalCell = gridRow.Cells[dgvStats.Columns["colTotal"].Index];
		DataGridViewCell rateCell = gridRow.Cells[dgvStats.Columns["colRate"].Index];
		if (win + loss + tie == 0)
		{
			totalCell.Value = "-";
			rateCell.Value = "-";
			totalCell.Style.ForeColor = Color.FromArgb(90, 100, 120);
			rateCell.Style.ForeColor = Color.FromArgb(90, 100, 120);
			totalCell.ToolTipText = string.Empty;
			return;
		}
		totalCell.Value = $"{win} / {loss}";
		totalCell.Style.ForeColor = Color.FromArgb(234, 234, 234);
		totalCell.ToolTipText = $"당첨 {win}회 / 미당첨 {loss}회 / 타이 {tie}회";
		rateCell.Value = FormatRate(win, loss);
		rateCell.Style.ForeColor = GetRateColor(win, loss);
	}

	private static string FormatRate(int win, int loss)
	{
		int decided = win + loss;
		if (decided != 0)
		{
			return $"{(double)win / (double)decided * 100.0:F1}%";
		}
		return "-";
	}

	private static Color GetRateColor(int win, int loss)
	{
		int decided = win + loss;
		if (decided == 0)
		{
			return Color.FromArgb(90, 100, 120);
		}
		double rate = (double)win / (double)decided;
		if (rate >= 0.55)
		{
			return Color.FromArgb(80, 220, 140);
		}
		if (rate >= 0.45)
		{
			return Color.FromArgb(234, 234, 234);
		}
		return Color.FromArgb(255, 120, 120);
	}
}
