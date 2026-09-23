using System;
using System.Drawing;
using System.Windows.Forms;
using PragmaticBot.Services;

namespace PragmaticBot;

/// <summary>
/// 상단 로고를 클릭하면 뜨는 이용설명서(모달). KingTigerBet 사용법을 화면 구성 순서대로 안내한다.
/// 스크롤 가능한 다크 테마 RichTextBox 한 장으로 구성한다.
/// </summary>
public class UserManualForm : Form
{
	private static readonly Color HeadColor = Color.FromArgb(120, 200, 255);
	private static readonly Color BodyColor = Color.FromArgb(224, 224, 224);
	private static readonly Color AccentColor = Color.FromArgb(255, 210, 100);

	public UserManualForm()
	{
		Text = "이용설명서";
		Icon = AppIcon.Value;
		FormBorderStyle = FormBorderStyle.Sizable;
		StartPosition = FormStartPosition.CenterParent;
		MaximizeBox = true;
		MinimizeBox = false;
		ShowInTaskbar = false;
		BackColor = Color.FromArgb(26, 26, 46);
		ForeColor = Color.White;
		Font = new Font("맑은 고딕", 10f);
		ClientSize = new Size(720, 620);
		MinimumSize = new Size(520, 420);

		PictureBox pic = new PictureBox
		{
			Image = AppImages.Logo,
			SizeMode = PictureBoxSizeMode.Zoom,
			Size = new Size(40, 40),
			Location = new Point(16, 12),
			BackColor = Color.White
		};
		Controls.Add(pic);

		Label lblTitle = new Label
		{
			Text = "KingTigerBet 이용설명서",
			Font = new Font("맑은 고딕", 14f, FontStyle.Bold),
			ForeColor = Color.White,
			AutoSize = true,
			Location = new Point(66, 20)
		};
		Controls.Add(lblTitle);

		RichTextBox rtb = new RichTextBox
		{
			ReadOnly = true,
			BorderStyle = BorderStyle.None,
			BackColor = Color.FromArgb(20, 24, 40),
			ForeColor = BodyColor,
			Location = new Point(16, 64),
			Size = new Size(ClientSize.Width - 32, ClientSize.Height - 64 - 56),
			Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
			ScrollBars = RichTextBoxScrollBars.Vertical,
			DetectUrls = false,
			TabStop = false
		};
		Controls.Add(rtb);
		BuildManual(rtb);
		rtb.SelectionStart = 0;
		rtb.ScrollToCaret();

		Button btnClose = new Button
		{
			Text = "닫기",
			Size = new Size(120, 38),
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(70, 76, 96),
			ForeColor = Color.White,
			Font = new Font("맑은 고딕", 10.5f, FontStyle.Bold),
			Cursor = Cursors.Hand,
			Anchor = AnchorStyles.Bottom | AnchorStyles.Right
		};
		btnClose.FlatAppearance.BorderSize = 0;
		btnClose.Location = new Point(ClientSize.Width - 120 - 16, ClientSize.Height - 38 - 12);
		btnClose.Click += delegate { Close(); };
		Controls.Add(btnClose);
		CancelButton = btnClose;
		AcceptButton = btnClose;
	}

	private void BuildManual(RichTextBox rtb)
	{
		AppendPara(rtb,
			"KingTigerBet은 라이브 카지노(에볼루션·프라그마틱) 바카라 로드맵을 실시간으로 읽어, 설정한 패턴이 나오면 자동으로 실베팅하는 봇입니다. 아래는 화면 구성 순서대로 정리한 사용법입니다.",
			BodyColor);

		AppendHead(rtb, "1. 상단 바");
		AppendBullet(rtb, "로고(좌측 상단): 이 이용설명서를 엽니다.");
		AppendBullet(rtb, "플레이 방 / 방 설정: 베팅에 사용할 방을 고르고 방별 설정을 조정합니다.");
		AppendBullet(rtb, "전략 선택(마틴 등): 베팅 금액 전략을 선택합니다.");
		AppendBullet(rtb, "⚙ 설정: 형식·베팅금·상하한·옵션·세컨로직·행렬통계·쿨다운 등 모든 세부 설정이 모인 허브입니다. 항목을 누르면 그 설정 창이 뜨고, 닫으면 허브로 돌아옵니다.");
		AppendBullet(rtb, "우측 유저이름: 현재 로그인한 계정입니다.");

		AppendHead(rtb, "2. 방 목록 (가운데)");
		AppendBullet(rtb, "전체 ON / 전체 OFF: 모든 방을 한 번에 켜고 끕니다. OFF된 방은 베팅에서 제외되지만 로드맵·단계는 그대로 유지됩니다.");
		AppendBullet(rtb, "각 방 카드: 방 이름, TID(테이블 ID), 상태(BetsOpen/BetsClosed), GID(현재 라운드), 단계별 베팅금이 표시됩니다.");
		AppendBullet(rtb, "로드맵(구슬판): 실시간 결과가 갱신되며, 여기서 설정한 패턴을 찾습니다.");

		AppendHead(rtb, "3. 금액 정보 (우측 상단)");
		AppendBullet(rtb, "시작 금액 / 현재 금액 / 이익: 봇 가동 이후의 잔액과 손익입니다.");
		AppendBullet(rtb, "진행중 베팅: 아직 결과가 나오지 않은 베팅 금액입니다.");
		AppendBullet(rtb, "누적 베팅 금액: 지금까지 베팅한 총액입니다.");
		AppendBullet(rtb, "실시간 최고단계 / 최고단계: 현재 및 누적 최고 마틴 단계입니다.");
		AppendBullet(rtb, "세션유지 실베팅: 실제 사이트 세션으로 베팅이 나가는 상태(대기 중/진행 중)를 나타냅니다.");

		AppendHead(rtb, "4. 제어 버튼 (우측)");
		AppendBullet(rtb, "실행 중 / 일시정지: 자동 베팅을 시작·일시중지합니다.");
		AppendBullet(rtb, "베팅중지: 새 베팅을 멈춥니다(진행중 베팅 결과는 계속 정산).");
		AppendBullet(rtb, "축소버전: 창을 작게 줄여 모니터링용으로 사용합니다.");

		AppendHead(rtb, "5. 베팅 이력 (우측 하단 표)");
		AppendBullet(rtb, "단계 / 베팅(뱅커·플레이어) / 금액 / 결과(당첨·미당첨·베팅중) / 손익이 라운드마다 기록됩니다.");
		AppendBullet(rtb, "실제 사이트에 올라간 최종 금액과 그 결과를 그대로 반영합니다.");

		AppendHead(rtb, "6. 하단 바");
		AppendBullet(rtb, "Chrome 연결 / 연결 해제: 게임 브라우저와의 연결을 켜고 끕니다. 연결되어야 로드맵 수집·베팅이 가능합니다.");
		AppendBullet(rtb, "연결 상태(수신/저장): 실시간으로 들어오는 데이터 건수입니다.");
		AppendBullet(rtb, "게임: 현재 돌고 있는 게임사(에볼루션/프라그마틱)를 표시합니다.");
		AppendBullet(rtb, "가동시간: 봇이 켜진 뒤 경과 시간입니다.");
		AppendBullet(rtb, "종료: 봇과 연결된 브라우저를 정리하고 프로그램을 닫습니다.");

		AppendHead(rtb, "7. 동작 원리 (요약)");
		AppendBullet(rtb, "게임 소켓에서 로드맵·타이머·잔액을 실시간 수집합니다.");
		AppendBullet(rtb, "설정한 패턴이 방의 로드맵에 나타나면 그 방을 베팅 대상으로 잡습니다.");
		AppendBullet(rtb, "마감 약 10초 전에 실제 사이트에 칩을 올려 베팅합니다(여러 방이 겹치면 순서대로 처리).");
		AppendBullet(rtb, "결과가 확정되면 해당 라운드에 승패를 정산하고, 전략에 따라 다음 단계 금액을 정합니다.");

		AppendHead(rtb, "8. 자주 있는 상황");
		AppendBullet(rtb, "베팅이 안 나감: 방이 OFF거나, 최소 베팅액 미달, 또는 아직 패턴이 매칭되지 않은 경우입니다.");
		AppendBullet(rtb, "잔액이 안 맞음: 사이트 반영이 늦을 수 있으며, 잠시 후 소켓/지갑 조회로 실제 잔액에 맞춰집니다.");
		AppendBullet(rtb, "데이터가 멈춤: 게임 페이지가 끊기면 자동으로 페이지를 리로드해 복구를 시도합니다.");
		AppendBullet(rtb, "서버 접속 오류: 서버가 꺼져 있으면 약 90초 뒤 안전하게 세션을 종료합니다.");

		AppendPara(rtb, "\n※ 실제 자금이 걸리는 자동 베팅입니다. 설정(전략·상하한·최소 베팅)을 먼저 확인한 뒤 가동하세요.", AccentColor);
	}

	private void AppendHead(RichTextBox rtb, string text)
	{
		rtb.SelectionStart = rtb.TextLength;
		rtb.SelectionFont = new Font("맑은 고딕", 12f, FontStyle.Bold);
		rtb.SelectionColor = HeadColor;
		rtb.AppendText("\n" + text + "\n");
	}

	private void AppendBullet(RichTextBox rtb, string text)
	{
		rtb.SelectionStart = rtb.TextLength;
		rtb.SelectionFont = new Font("맑은 고딕", 10f, FontStyle.Regular);
		rtb.SelectionColor = BodyColor;
		rtb.AppendText("  • " + text + "\n");
	}

	private void AppendPara(RichTextBox rtb, string text, Color color)
	{
		rtb.SelectionStart = rtb.TextLength;
		rtb.SelectionFont = new Font("맑은 고딕", 10f, FontStyle.Regular);
		rtb.SelectionColor = color;
		rtb.AppendText(text + "\n");
	}
}
