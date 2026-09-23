using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using PragmaticBot.Models;

namespace PragmaticBot;

/// <summary>
/// Full-size view of the betting history.
/// <para>
/// The panel on the main screen is only a few hundred pixels wide, so room names and the PP table
/// and round ids get cut off there. This window binds to the same live list, so it keeps updating
/// while it is open.
/// </para>
/// </summary>
public class BetHistoryForm : Form
{
	private readonly DataGridView _grid;
	private readonly Func<(string Start, string Current, string Profit, string Cumulative)>? _balanceProvider;
	private readonly Action? _onMainVersion;

	private readonly Label _valStart = MakeValueLabel();
	private readonly Label _valCurrent = MakeValueLabel();
	private readonly Label _valProfit = MakeValueLabel();
	private readonly Label _valCumulative = MakeValueLabel();
	private readonly System.Windows.Forms.Timer _headerTimer;

	public BetHistoryForm(BindingList<BetHistoryRow> rows,
		Func<(string Start, string Current, string Profit, string Cumulative)>? balanceProvider = null,
		Action? onMainVersion = null)
	{
		_balanceProvider = balanceProvider;
		_onMainVersion = onMainVersion;

		Text = "베팅 상세 이력";
		StartPosition = FormStartPosition.CenterParent;
		ClientSize = new Size(1180, 640);
		MinimumSize = new Size(760, 400);
		BackColor = Color.FromArgb(26, 26, 46);
		ForeColor = Color.FromArgb(234, 234, 234);
		Font = new Font("맑은 고딕", 9f);
		ShowInTaskbar = true;
		ShowIcon = false; // 타이틀바 로고 제거

		// ── 상단 잔액 헤더 (시작/현재/이익/누적) ──
		FlowLayoutPanel header = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 44,
			BackColor = Color.FromArgb(20, 26, 46),
			Padding = new Padding(14, 8, 14, 8),
			WrapContents = false,
			FlowDirection = FlowDirection.LeftToRight
		};
		header.Controls.Add(MakeCaption("시작 잔액"));
		header.Controls.Add(_valStart);
		header.Controls.Add(MakeCaption("현재 잔액"));
		header.Controls.Add(_valCurrent);
		header.Controls.Add(MakeCaption("이익"));
		header.Controls.Add(_valProfit);
		header.Controls.Add(MakeCaption("누적 베팅"));
		header.Controls.Add(_valCumulative);

		_grid = new DataGridView
		{
			Dock = DockStyle.Fill,
			AutoGenerateColumns = false,
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			AllowUserToResizeRows = false,
			ReadOnly = true,
			RowHeadersVisible = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			BackgroundColor = Color.FromArgb(22, 33, 62),
			BorderStyle = BorderStyle.None,
			GridColor = Color.FromArgb(45, 58, 92),
			EnableHeadersVisualStyles = false,
			ColumnHeadersHeight = 32,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
		};
		_grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 44, 82);
		_grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(234, 234, 234);
		_grid.ColumnHeadersDefaultCellStyle.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
		_grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(30, 44, 82);
		_grid.DefaultCellStyle.BackColor = Color.FromArgb(22, 33, 62);
		_grid.DefaultCellStyle.ForeColor = Color.FromArgb(222, 222, 222);
		_grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(45, 65, 110);
		_grid.DefaultCellStyle.SelectionForeColor = Color.White;
		_grid.RowTemplate.Height = 26;

		AddColumn("Time", "시간", 9, "HH:mm:ss");
		AddColumn("RoomName", "방", 26);
		AddColumn("TableId", "PP 테이블 ID", 20);
		AddColumn("GameId", "라운드 ID", 16);
		AddColumn("Step", "단계", 6);
		AddColumn("BetType", "베팅", 9);
		AddColumn("BetAmount", "금액", 11, "N0");
		AddColumn("GameResult", "결과", 6);
		AddColumn("Status", "판정", 9);
		AddColumn("Profit", "손익", 11, "+#,##0;-#,##0;0");

		_grid.CellFormatting += Grid_CellFormatting;
		_grid.DataSource = rows;

		Panel footer = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = 48,
			BackColor = Color.FromArgb(26, 26, 46)
		};
		Button close = new Button
		{
			Text = "닫기",
			Size = new Size(110, 32),
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(60, 70, 100),
			ForeColor = Color.White,
			Anchor = AnchorStyles.Top | AnchorStyles.Right
		};
		close.FlatAppearance.BorderSize = 0;
		close.Location = new Point(footer.Width - close.Width - 14, 8);
		close.Click += delegate
		{
			Close();
		};
		Button mainVersion = new Button
		{
			Text = "메인버젼",
			Size = new Size(120, 32),
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(40, 90, 160),
			ForeColor = Color.White,
			Anchor = AnchorStyles.Top | AnchorStyles.Right
		};
		mainVersion.FlatAppearance.BorderSize = 0;
		mainVersion.Location = new Point(footer.Width - close.Width - mainVersion.Width - 24, 8);
		mainVersion.Click += delegate
		{
			_onMainVersion?.Invoke();
			Close();
		};
		footer.Controls.Add(close);
		footer.Controls.Add(mainVersion);

		Controls.Add(_grid);
		Controls.Add(header);
		Controls.Add(footer);
		CancelButton = close;

		// 잔액 헤더 실시간 갱신 (읽기 전용 — 베팅 로직 무관)
		RefreshHeader();
		_headerTimer = new System.Windows.Forms.Timer { Interval = 250 };
		_headerTimer.Tick += delegate { RefreshHeader(); };
		_headerTimer.Start();
		FormClosed += delegate { _headerTimer.Stop(); _headerTimer.Dispose(); };
	}

	private void RefreshHeader()
	{
		if (_balanceProvider == null)
		{
			return;
		}
		try
		{
			var (start, current, profit, cumulative) = _balanceProvider();
			_valStart.Text = start;
			_valCurrent.Text = current;
			_valProfit.Text = profit;
			_valProfit.ForeColor = profit.StartsWith("-", StringComparison.Ordinal)
				? Color.OrangeRed
				: (profit == "0" ? Color.Silver : Color.LimeGreen);
			_valCumulative.Text = cumulative;
		}
		catch
		{
		}
	}

	private static Label MakeCaption(string text)
	{
		return new Label
		{
			Text = text,
			AutoSize = true,
			ForeColor = Color.FromArgb(150, 160, 190),
			Margin = new Padding(14, 6, 4, 0),
			Font = new Font("맑은 고딕", 9f)
		};
	}

	private static Label MakeValueLabel()
	{
		return new Label
		{
			Text = "-",
			AutoSize = true,
			ForeColor = Color.White,
			Margin = new Padding(0, 5, 0, 0),
			Font = new Font("맑은 고딕", 10f, FontStyle.Bold)
		};
	}

	private void AddColumn(string mapping, string header, int fillWeight, string? format = null)
	{
		DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn
		{
			DataPropertyName = mapping,
			HeaderText = header,
			Name = mapping,
			FillWeight = fillWeight,
			SortMode = DataGridViewColumnSortMode.NotSortable
		};
		if (format != null)
		{
			column.DefaultCellStyle.Format = format;
		}
		if (mapping == "BetAmount" || mapping == "Profit")
		{
			column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
		}
		_grid.Columns.Add(column);
	}

	private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
	{
		if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count)
		{
			return;
		}
		if (_grid.Rows[e.RowIndex].DataBoundItem is not BetHistoryRow row)
		{
			return;
		}
		string name = _grid.Columns[e.ColumnIndex].Name;
		if (name != "Status" && name != "Profit")
		{
			return;
		}
		e.CellStyle.ForeColor = row.Status switch
		{
			"당첨" => Color.LimeGreen,
			"미당첨" => Color.OrangeRed,
			"타이" => Color.Gold,
			"관망" => Color.Silver,
			"실패" => Color.OrangeRed,
			_ => Color.Orange
		};
	}
}
