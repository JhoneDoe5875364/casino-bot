using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using PragmaticBot.Models.Api;
using PragmaticBot.Services;

namespace PragmaticBot;

public partial class FormatSettingForm : Form
{
	private readonly FormatSettingService _formatSettingService;

	private readonly string _userId;

	private IContainer components;

	public bool PbCrossEnabled => chkPbCross.Checked;

	public bool BpCrossOnTwoLossEnabled
	{
		get
		{
			if (chkPbCross.Checked)
			{
				return chkBpCrossOnTwoLoss.Checked;
			}
			return false;
		}
	}

	public FormatSettingForm(string userId, bool pbCrossEnabled = false, bool bpCrossOnTwoLossEnabled = false)
	{
		_formatSettingService = new FormatSettingService();
		_userId = userId;
		InitializeComponent();
		chkPbCross.Checked = pbCrossEnabled;
		chkBpCrossOnTwoLoss.Checked = bpCrossOnTwoLossEnabled;
		chkPbCross.CheckedChanged += delegate
		{
			SyncBpCrossState();
		};
		SyncBpCrossState();
		// [서식 txt] 그리드 아래 왼쪽에 불러오기/내보내기 버튼을 코드로 추가(디자이너 미변경).
		Button btnImport = new Button
		{
			Text = "txt 불러오기",
			Location = new Point(12, 545),
			Size = new Size(120, 35),
			Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(45, 55, 72),
			ForeColor = Color.FromArgb(234, 234, 234),
			Font = new Font("맑은 고딕", 9f, FontStyle.Bold)
		};
		btnImport.FlatAppearance.BorderSize = 0;
		btnImport.Click += BtnImport_Click;
		Button btnExport = new Button
		{
			Text = "txt 내보내기",
			Location = new Point(140, 545),
			Size = new Size(120, 35),
			Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(45, 55, 72),
			ForeColor = Color.FromArgb(234, 234, 234),
			Font = new Font("맑은 고딕", 9f, FontStyle.Bold)
		};
		btnExport.FlatAppearance.BorderSize = 0;
		btnExport.Click += BtnExport_Click;
		Controls.Add(btnImport);
		Controls.Add(btnExport);
		btnImport.BringToFront();
		btnExport.BringToFront();
	}

	private void SyncBpCrossState()
	{
		chkBpCrossOnTwoLoss.Enabled = chkPbCross.Checked;
		if (!chkPbCross.Checked)
		{
			chkBpCrossOnTwoLoss.Checked = false;
		}
	}

	private async void FormatSettingForm_Load(object? sender, EventArgs e)
	{
		await LoadFormatsAsync();
	}

	private async Task LoadFormatsAsync()
	{
		try
		{
			dgvFormats.Rows.Clear();
			for (int i = 1; i <= 50; i++)
			{
				dgvFormats.Rows.Add(i, "");
			}
			foreach (FormatSettingDto format in await _formatSettingService.GetUserFormatsAsync(_userId))
			{
				int rowIndex = format.Number - 1;
				if (rowIndex >= 0 && rowIndex < dgvFormats.Rows.Count)
				{
					dgvFormats.Rows[rowIndex].Cells[1].Value = format.Text ?? "";
				}
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("서식 데이터 로드 중 오류가 발생했습니다: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
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
					string text = dgvFormats.Rows[i].Cells[1].Value?.ToString();
					formats[number] = text;
				}
			}
			await _formatSettingService.SaveUserFormatsAsync(_userId, formats);
			MessageBox.Show("서식 설정이 저장되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
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

	private void BtnImport_Click(object? sender, EventArgs e)
	{
		using OpenFileDialog dlg = new OpenFileDialog
		{
			Filter = "텍스트 파일 (*.txt)|*.txt|모든 파일 (*.*)|*.*",
			Title = "서식 txt 불러오기"
		};
		if (dlg.ShowDialog(this) != DialogResult.OK)
			return;
		try
		{
			string[] lines = File.ReadAllLines(dlg.FileName);
			int count = 0;
			for (int i = 0; i < dgvFormats.Rows.Count; i++)
			{
				string value = (i < lines.Length) ? lines[i].Trim() : "";
				dgvFormats.Rows[i].Cells[1].Value = value;
				if (value.Length > 0)
					count++;
			}
			MessageBox.Show($"{count}개 서식을 불러왔습니다. 확인 후 [저장]을 눌러야 서버에 반영됩니다.", "불러오기 완료", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		catch (Exception ex)
		{
			MessageBox.Show("불러오기 중 오류가 발생했습니다: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private void BtnExport_Click(object? sender, EventArgs e)
	{
		using SaveFileDialog dlg = new SaveFileDialog
		{
			Filter = "텍스트 파일 (*.txt)|*.txt",
			Title = "서식 txt 내보내기",
			FileName = "서식.txt"
		};
		if (dlg.ShowDialog(this) != DialogResult.OK)
			return;
		try
		{
			List<string> lines = new List<string>();
			int lastNonEmpty = -1;
			for (int i = 0; i < dgvFormats.Rows.Count; i++)
			{
				string text = dgvFormats.Rows[i].Cells[1].Value?.ToString() ?? "";
				lines.Add(text);
				if (text.Trim().Length > 0)
					lastNonEmpty = i;
			}
			if (lastNonEmpty < 0)
			{
				MessageBox.Show("내보낼 서식이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			File.WriteAllLines(dlg.FileName, lines.GetRange(0, lastNonEmpty + 1));
			MessageBox.Show($"{lastNonEmpty + 1}줄을 저장했습니다.", "내보내기 완료", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		catch (Exception ex)
		{
			MessageBox.Show("내보내기 중 오류가 발생했습니다: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private void BtnCancel_Click(object? sender, EventArgs e)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}
}
