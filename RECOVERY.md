# 소스 복원 기록

`publish\PragmaticBot.exe`(단일 파일 자체 포함 빌드)와 함께 남아 있던
`PragmaticBot.pdb`로부터 복원한 소스입니다.

- 원본 프로젝트 경로: `D:\오토솔루션\AutoSolution2 - 피보나치행렬 2\PragmaticBot\`
- 원본 대상 프레임워크: `net10.0-windows` / `win-x64` / Release
- 복원 파일: 71개 `.cs`, 약 24,000줄

PDB에 기록된 파일 목록·폴더 구조·메서드별 소속 문서 정보를 그대로 사용했기 때문에
파일 배치와 `*.Designer.cs` 분리까지 원본과 동일합니다.

## 검증

복원된 소스를 다시 빌드해 나온 어셈블리를 원본 어셈블리와 같은 방식으로 디컴파일한 뒤
비교했습니다.

| 항목 | 결과 |
| --- | --- |
| 완전 일치 | 102 / 124 타입 파일 |
| 멤버 순서만 다름 | 4 |
| 코드젠 표현만 다름 | 17 |
| `PragmaticBot.exe` 크기 | 64,779,263 B (원본 64,916,601 B, 0.2% 차이) |

"코드젠 표현만 다름"은 임시 변수 이름(`item` → `item2`), 문자열 보간의 상수 폴딩,
`foreach` 대상 지역 변수 호이스팅, IL 라벨 번호처럼 컴파일러 버전 차이에서 오는
것들이며 동작에 영향을 주지 않습니다. exe 크기 차이는 번들된 런타임 패치 버전
(원본 10.0.11 / 현재 10.0.401) 차이입니다.

## 원본과 달라진 부분

복원 과정에서 손으로 고친 곳은 세 군데입니다. 모두 디컴파일러가 되살리지 못하는
문법 정보라서, 원본이 거의 확실히 이 형태였던 것으로 복구했습니다.

1. `Services\BetAllocationService.cs` — 컬렉션 식 `[...]`이 컴파일러 생성 타입
   `<>z__ReadOnlySingleElementList<T>`로 남아 있어 원래의 컬렉션 식으로 되돌렸습니다.
2. `Services\{DbLogService, PragmaticChromeDevToolsService, PragmaticWebSocketServer,
   SessionMonitorService}.cs` — `Timer`가 `System.Windows.Forms.Timer`와 모호해져
   각 파일 상단에 `using Timer = ...;` 별칭을 추가했습니다. 원본은 정규화된 이름을
   썼을 가능성이 큽니다.
3. `Services\{PragmaticAutoBettingService, RoomGateFormatSettingService}.cs` —
   튜플 요소 이름(`(int Number, string Text)` 등)이 지역 변수 선언에서 유실되어
   사용처에 맞게 복구했습니다.

## 복원되지 않는 것

- **주석**과 `#region`: 컴파일 시점에 사라지므로 복구 불가능합니다.
- 지역 변수 이름은 PDB에 있어 복원되었지만, `var` 사용 여부·람다 표현 방식·
  LINQ 쿼리식과 메서드 체인 중 어느 쪽을 썼는지 같은 표기 선택은 원본과 다를 수 있습니다.
- `AssemblyInformationalVersion`의 git 커밋 해시(`1.0.0+426e90ec...`).
- nullable 주석(`!`, 지역 변수의 `?`) 일부. 빌드 경고 약 180건이 여기서 나옵니다.

## 참고

`Program.cs`에 Syncfusion 라이선스 키가 하드코딩되어 있습니다(원본 그대로).
저장소를 공개할 계획이라면 분리하는 편이 좋습니다.

## 복원 이후 수정

아래는 복원과 무관하게, 원본에 있던 문제를 고친 내용입니다.

### 상단 버튼 잘림

레이아웃이 클라이언트 폭 2065px 기준으로 만들어져 있어 그보다 좁은 화면에서는
오른쪽 컨트롤이 잘렸습니다. `행렬통계`·타이머·`세컨로직`·사용자명에
`Anchor = Top | Right`를 지정했습니다. 사용자명(`lblUser`)은 원래 화면 밖에 있어
아예 보이지 않던 상태였습니다.

### 공지 없을 때 남는 NOTICE 배지

`StopNoticeMarquee()`가 텍스트만 지우고 `panelNotice`는 계속 띄워 두어, 공지가 없을 때
빈 노란 배지만 남았습니다. 패널을 숨기도록 고치고 디자이너 기본값도 `Visible = false`로
바꿨습니다.

### Chromium 다운로드가 한 번 끊기면 영구 실패

압축 해제가 중간에 끊기면 `chrome.exe` 없는 빌드 폴더가 남습니다. PuppeteerSharp는
그 폴더를 재사용하지도(실행 파일 없음), 덮어쓰지도(압축 해제가 기존 파일을 거부) 못해서
이후 모든 실행이 약 180MB를 새로 받은 뒤 `... already exists`로 실패했습니다.

- `RemoveIncompleteBuilds()`를 추가해 다운로드 전에 `chrome.exe` 없는 빌드 폴더와
  남은 `*.zip`을 정리합니다.
- 번들을 끝내 준비하지 못하면 설치된 Chrome(`ChromeExecutableLocator`)으로 대체합니다.

### 오류 안내가 원인과 무관했던 문제

`EnsureChromeRunningAsync`가 `bool`만 돌려줘서, 다운로드 실패든 포트 문제든 같은
대화상자가 떴습니다. 실제로는 다운로드 단계에서 죽었는데 `netsh ... excludedportrange`를
안내해 엉뚱한 곳을 보게 했습니다. `ChromeShortcutHelper.LastFailure`(`StartFailure` 열거형)를
노출하고 `Form1.DescribeChromeFailure()`가 원인별 안내를 만들도록 했습니다.

### 익스텐션 자동 로드 (Chrome 137+ 문제)

봇은 원래부터 `--load-extension`으로 익스텐션을 자동으로 붙이려 했지만, **Chrome 137부터
이 스위치를 무시**합니다. 그래서 최신 빌드(146)를 받아 쓰는 동안에는 익스텐션이 전혀
로드되지 않고 CDP 경로로만 동작했습니다.

코드에 이미 있던 고정 빌드 경로를 켰습니다(`UsePinnedBuild = true`). 후보 빌드
(136.0.7103.113 등)는 모두 아직 내려받을 수 있고, 136에서 `--load-extension`이 정상
동작하는 것을 확인했습니다 — 페이지 콘솔에 `[PragmaticBot Content] 시작`이 찍히고
봇의 WebSocket 서버(29876)로 접속합니다. `content.js`는 익스텐션만 주입할 수 있으므로
(봇이 CDP로 넣는 것은 `inject.js`뿐) 이것이 로드 여부의 확실한 판정 기준입니다.

`--disable-features=DisableLoadExtensionCommandLineSwitch`, `--enable-unsafe-extension-debugging`,
CDP `Extensions.loadUnpacked`(파이프 연결 전용) 모두 146에서는 통하지 않았습니다.

되돌리려면 `UsePinnedBuild = false`로 두면 됩니다. 최신 빌드를 쓰는 대신 익스텐션 없이
CDP 경로로만 동작합니다.

### CDP 접속이 localhost로 나가던 문제

`PragmaticChromeDevToolsService`만 `http://localhost:{port}`를 쓰고 나머지 포트 점검은
전부 `127.0.0.1`을 썼습니다. Windows에서 `localhost`는 `::1`로 먼저 풀리는데 Chrome은
디버깅 포트를 IPv4에만 바인딩해서, 헬퍼는 "CDP 준비 완료"인데 PuppeteerSharp는
`Failed to fetch browser webSocket url`로 실패했습니다. `127.0.0.1`로 통일했습니다.

### Chrome 다운그레이드 시 프로필 거부

Chrome은 상위 버전이 쓰던 프로필을 거부하고 즉시 종료합니다(디버깅 포트가 열리지 않음).
146 → 136으로 내려갈 때 이 문제로 기동에 실패했습니다.

`ResetProfileIfDowngraded()`가 프로필을 마지막으로 쓴 버전을 두 군데에서 읽어 **큰 쪽**을
씁니다 — 봇이 남긴 `.pragmaticbot-build` 마커와 Chrome이 스스로 갱신하는 `Last Version`
입니다. 봇 밖에서 최신 Chrome이 같은 프로필을 열었다면 마커만으로는 잡히지 않기 때문입니다.
역행이 감지되면 프로필을 `C:\ChromeDebugProfile.v<버전>-<시각>`으로 **옮긴 뒤**(삭제 아님)
새로 시작합니다.

버전 비교로도 못 잡는 손상이 있을 수 있어, 기동 후 디버깅 포트가 끝내 응답하지 않으면
프로필을 `.launch-failed-<시각>`으로 옮기고 **한 번 더 시도**합니다.

### 테이블 카탈로그 자동 수집

방 패널은 서버의 게임 카탈로그에 등록되고 사용자가 방 설정에서 고른 테이블에 대해서만 만들어집니다
(`Form1.CreatePlayRoomPanelsAsync`). 카탈로그가 비어 있으면 봇이 게임 결과를 정상적으로 받아도
표시할 곳이 없어 화면이 비어 보입니다.

`GameCatalogSyncService`가 실시간 트래픽에서 본 테이블을 모아 5초 주기로
`POST /api/games/discovered` 에 보고합니다.

- `tableconfig` 메시지에서는 테이블 **이름**까지 얻을 수 있어 제목이 함께 등록됩니다.
- 게임 결과에서는 ID만 얻으므로 우선 ID로 등록하고, 나중에 이름을 알게 되면 보완합니다.
- 서버는 이미 있는 테이블을 중복 생성하지 않고, **운영자가 제목을 고친 행은 덮어쓰지 않습니다**
  (`Game.IsAutoDiscovered` 로 구분).
- 새 테이블이 등록되면 봇이 게임 목록을 다시 읽습니다. 화면의 방은 사용자가 방 설정에서
  고른 것만 그려지므로 저절로 바뀌지는 않습니다.
