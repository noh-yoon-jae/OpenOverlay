# OpenOverlay

[English](README.md) · [한국어](README.ko.md)

OpenOverlay는 Windows용 Spotify 가사 오버레이입니다. 데스크톱 셸은 Electron, 화면은 Next.js와 Tailwind CSS, Spotify 및 가사 공급원 연동은 .NET 10 서비스가 담당합니다.

## 기능

### 플레이어 모드와 시작 화면

- **전체 플레이어:** 앨범 아트와 곡 정보, 가사, 재생 진행 표시, 곡 조작 버튼, Spotify 볼륨 조절 기능을 제공합니다. 재생 바를 드래그해 곡 내 위치를 이동할 수 있습니다.
- **가사 전용:** 동기화 가사, 재생 진행 표시, 이전 곡·재생/일시정지·다음 곡 버튼을 표시하며 재생 바에서 위치를 이동할 수 있습니다.
- **미니 플레이어:** 앨범 아트와 곡·가사 정보를 간결하게 표시하고 재생을 조작합니다. 확장 버튼으로 전체 플레이어로 돌아갈 수 있습니다.
- 상단의 모드 버튼으로 화면을 전환합니다. 각 모드는 화면 구성에 맞춘 고정 창 크기를 사용합니다.
- 앱을 시작하면 작은 시작 화면에서 설정과 첫 Spotify 재생 정보 요청이 끝날 때까지 기다립니다. 이후 저장된 플레이어 모드와 크기로 전환합니다.

### 가사 표시

- 현재 Spotify 곡을 활성화된 가사 공급원에서 검색하고, 플레이어 상단에서 사용된 공급원을 확인할 수 있습니다.
- 타임스탬프가 있는 동기화 가사는 현재 줄을 강조하고 재생 위치를 따라갑니다. 전체 가사를 스크롤할 수 있으며, 화면에는 한 번에 다섯 줄이 보입니다.
- 마우스 휠로 가사를 움직여 다른 부분을 확인할 수 있습니다. 스크롤을 멈추고 3초가 지나면 자동 추적이 다시 시작됩니다.
- 타임스탬프가 없는 일반 가사는 재생 위치에 맞춰 움직이거나 현재 줄을 자동으로 강조할 수 없습니다.
- 설정의 **공급원 비교**로 활성화된 공급원에 현재 곡을 요청하고 결과와 품질 통계를 확인할 수 있습니다.

### Spotify 재생 제어

- Spotify Developer 앱을 Authorization Code + PKCE 방식으로 연결합니다.
- 현재 Spotify 세션에서 이전 곡, 재생/일시정지, 다음 곡을 조작할 수 있습니다. 전체 플레이어에는 볼륨 슬라이더도 있습니다.
- 재생 바를 클릭하거나 드래그해 현재 곡의 위치를 지정할 수 있습니다. Spotify Seek API를 사용하므로 Premium 계정과 활성 재생 장치가 필요합니다.
- 로컬 서비스가 재생 상태를 주기적으로 확인하며, UI는 업데이트 사이의 재생 위치를 보간합니다.

### 가사 공급원과 품질 확인

- **LRCLIB**는 기본 활성화되어 있으며 동기화 가사와 일반 가사를 제공할 수 있습니다.
- **Spotify 가사(실험적)** 는 기본 비활성화되어 있습니다. 설정에서 켜고 별도의 동의와 Spotify 웹 로그인을 완료해야 사용할 수 있습니다.
- 공급원을 개별적으로 켜거나 끄고 검색 우선순위를 바꿀 수 있습니다. 설정의 공급원 비교와 통계에서 요청 결과와 동기화 가사 품질을 확인할 수 있습니다.
- Spotify 웹 세션 가사는 비공개·비공식 엔드포인트를 사용하므로 Spotify가 서비스를 바꾸면 작동하지 않을 수 있습니다. Spotify OAuth 재생 연동과는 별개입니다.

### 설정과 오버레이 조작

- 가사 글꼴, 글자 크기, 현재 줄 강조를 조절하고 재생 진행 표시를 켜거나 끌 수 있습니다.
- 오버레이 항상 위 표시, 마우스 입력 무시, 배경 투명도를 설정할 수 있습니다.
- 음악에 반응하는 테두리 글로우를 켜거나 끌 수 있습니다.
- 제목 표시줄을 드래그해 창을 옮길 수 있으며, 위치는 다음 실행을 위해 로컬에 저장됩니다.
- 전역 단축키: **Ctrl+Alt+O** 오버레이 표시/숨김, **Ctrl+Alt+P** 클릭 통과 전환, **Ctrl+Alt+Left/Right** 가사 타이밍을 250ms씩 보정합니다.
- 플레이어와 별도 창에서 설정을 열 수 있습니다.

## 구조와 개인정보 처리

- **계층형 .NET 솔루션:** `OpenOverlay.Core`는 공통 모델·추상화·정책을, `OpenOverlay.Infrastructure`는 Spotify·가사 공급원·저장소·자격 증명 보안을, `OpenOverlay.Service`는 명령 처리를 담당합니다. `OpenOverlay.Tests`에는 백엔드 테스트가 있습니다.
- **Next.js와 Tailwind CSS**가 화면을 제공하며 배포용으로 정적 내보내기됩니다. UI 경로와 컴포넌트는 `web/app`, `web/components`에 있고 공통 클라이언트 타입과 데스크톱 브리지 도우미는 `web/lib`에 있습니다.
- **Electron**이 오버레이와 별도 설정 창, 창 모드, 전역 단축키, 앱 프로토콜을 관리합니다. `desktop/preload.cjs`는 허용 목록에 있는 제한된 API만 화면에 제공하며, 화면에서 Node.js에 직접 접근할 수 없습니다.
- **프로세스 통신:** 화면은 preload API를 호출하고, preload는 허용된 IPC 요청을 Electron에 전달합니다. Electron은 .NET 프로세스의 표준 입력으로 JSON Lines 명령을 보내고 표준 출력에서 응답과 이벤트를 받습니다. 백엔드는 상시 실행되는 로컬 HTTP API 서버를 운영하지 않습니다.
- **Spotify 로그인 시 예외:** PKCE 인증 중에는 Spotify OAuth 리디렉션을 받기 위해 .NET이 루프백 주소 `127.0.0.1:43821`에서 임시로 대기한 뒤 리스너를 닫습니다. 이 포트는 앱 API 엔드포인트가 아닙니다.
- Spotify OAuth 토큰은 Windows 자격 증명 관리자에 저장됩니다. 선택 기능인 Spotify 가사 세션도 별도 동의를 받은 뒤 그곳에 저장합니다. `sp_dc` 쿠키는 시스템 브라우저 프로필이 아니라 앱 전용 WebView2 프로필에서만 읽습니다.
- `sp_dc` 쿠키는 실험적 Spotify 가사 기능에서만 사용합니다. `open.spotify.com`에 보내 짧은 수명의 플레이어 토큰을 얻고, 그 토큰은 `spclient.wg.spotify.com`에 전달합니다. 쿠키 값은 화면에 전달되지 않습니다. 공급원을 끄면 쿠키 사용이 중단됩니다. **세션 삭제**는 저장된 세션과 앱 전용 WebView2 프로필 데이터를 제거합니다.
- 가사 캐시: `%LOCALAPPDATA%\OpenOverlay\LyricsCache`
- 비밀 정보가 아닌 설정: `%LOCALAPPDATA%\OpenOverlay\settings.json`

## 요구 사항

- Windows 10 이상
- Node.js 22 이상
- pnpm 11.25 이상 (Corepack으로 설치 가능)
- .NET 10 SDK
- 선택 기능인 Spotify 가사 로그인 창을 위한 Microsoft Edge WebView2 Runtime

## 개발 및 실행

의존성을 설치하고 Next.js 개발 서버와 데스크톱 앱을 실행합니다.

```powershell
corepack pnpm install
corepack pnpm dev
```

자주 쓰는 명령:

```powershell
corepack pnpm typecheck
corepack pnpm build:web
corepack pnpm build:backend
corepack pnpm build
```

`corepack pnpm dev`는 웹 개발 서버, Electron, .NET 서비스를 함께 실행합니다. `corepack pnpm build`는 웹 UI와 Windows 백엔드를 빌드한 뒤 Windows 설치 파일을 패키징합니다.


## Spotify Developer 설정

Spotify Developer 앱을 만들고 다음 리디렉션 URI를 등록합니다.

```text
http://127.0.0.1:43821/callback/
```

OpenOverlay 설정에 앱의 Client ID를 입력합니다. 앱은 Authorization Code + PKCE와 재생 상태 조회 및 제어에 필요한 범위를 사용합니다. Spotify Development Mode는 현재 개발자에게 적격한 Premium 계정을 요구하고 앱당 허용 사용자를 5명으로 제한합니다. 최신 내용은 Spotify의 [할당량 모드 안내](https://developer.spotify.com/documentation/web-api/concepts/quota-modes)와 [개발자 약관](https://developer.spotify.com/terms)을 확인하세요.

## 라이선스

외부 구성 요소의 고지와 라이선스는 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) 및 `src/OpenOverlay.Infrastructure/LICENSES`에서 확인할 수 있습니다.
