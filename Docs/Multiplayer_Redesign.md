# 멀티플레이 재설계

2026-10-13 세션에서 정한 내용입니다. 이후 작업은 이 문서를 기준으로 합니다. 아직 코드는 이 설계대로 바뀌지 않았습니다(맨 아래 "현재 작업 트리 상태" 참고).

## 1. 왜 바꾸나

멀티 테스트에서 이런 문제가 나왔습니다. 공격 모션이 안 보이고, A가 차를 타고 가도 B 화면에서는 차가 그대로 있고 A만 움직이며, 그 차를 B도 탈 수 있었습니다. 서로 이름도 안 보였습니다.

원인은 구조에 있습니다. 지금 통신은 정해진 몇 가지 메시지만 방 전원에게 중계합니다.

| 메시지 | 담는 것 |
| --- | --- |
| `pos` | 위치·회전·속도 |
| `state` | 방장이 10초마다 보내는 월드 저장본 (시계, 주식, 회사, 소유권, 땅, 신고, SNS) |
| `op` | 땅 구매·회사 설립처럼 기능마다 하나씩 정의한 공유 변경 |
| `world` | 시민(`CitizenSync`)·교통(`TrafficSync`) 전용 동기화 |

이 목록에 넣지 않은 기능(공격, 든 물건, 앉기, 드롭 아이템, 설치한 차, 이름 등)은 내 PC에서만 일어납니다. 새 기능을 만들 때마다 동기화를 손으로 붙여야 하고, 빠뜨리면 상대에게 안 보입니다.

**목표: 공유 대상 오브젝트에 컴포넌트를 붙이고 공유할 값만 표시하면 방 전원에게 자동으로 보이는 공통 동기화 계층.** 새 기능 때문에 프로토콜이나 서버를 고치는 일이 없어야 합니다.

## 2. 결정 사항 요약

| 항목 | 결정 |
| --- | --- |
| 유료 서버 | 쓰지 않음. Cloudflare 서버는 테스트용으로만 남김 |
| 멀티 모드 | **"서버 열기" 하나로 통합.** 친구 모드와 커뮤니티 서버를 나누지 않음 |
| 공개 설정 | 공개면 모든 유저의 서버 목록에 보임(비밀번호 선택), 비공개면 목록에 안 뜨고 서버 코드로만 입장 |
| 서버가 도는 곳 | PC: 별도 서버 프로세스(백그라운드, 게임을 꺼도 계속 돎). 모바일: 게임 앱 안 |
| 24시간 서버 | 같은 서버 프로그램을 빌린 컴퓨터에서 실행. 개발 측은 운영하지 않음(커뮤니티가 연다) |
| 인원 | PC 서버 2~50명, 모바일 서버 2~10명 |
| 모바일 서버 주인이 나가면 | **서버 종료.** 월드는 그 폰에 저장되고 다시 열면 이어짐. 방장 넘기기(핸드오버)는 하지 않음 |
| 연결 통로 | EOS(Epic Online Services, 무료, PC·Android·iOS). 데디케이티드 서버는 직접 IP·포트도 지원 |
| 크로스플레이 | PC·모바일이 같은 서버에 들어옴. Steam은 PC 전용이라 크로스플레이에서는 빠짐(PC 스팀판용으로는 남길 수 있음) |
| 캐릭터 | **서버(월드)마다 따로.** 이름·성별·인벤토리·은행돈·주식·위치 모두 그 월드 전용. 같은 월드에 다시 오면 그 캐릭터로 이어서 함 |
| 캐릭터 저장 위치 | 서버의 월드 파일 (계정 ID별). 별도 DB 서버 없음 |
| 시계 | 월드 시계(서버가 켜진 동안 계속)와 캐릭터 시계(그 캐릭터가 접속한 동안만) 두 개 |
| 시민·교통 | 시간 기반 결정적 계산으로 재작성. 각 기기가 같은 장면을 직접 계산 |
| 그래픽 품질 | 기기별로 정함. 월드 규모(시민 수, 교통량)는 서버 설정(방 전체 공유) |

## 3. 서버 모델

### 3.1 서버 열기 / 참가하기

타이틀에는 **서버 열기**와 **참가하기**만 둡니다.

- 서버 열기 설정: 서버 이름, 인원, 월드(새로/기존), 공개/비공개, 비밀번호(선택)
- 참가하기: 서버 목록(이름, 인원, 핑, 비밀번호 여부)에서 고르거나, 서버 코드나 주소를 입력

| | PC에서 서버 열기 | 모바일에서 서버 열기 | 빌린 24시간 서버 |
| --- | --- | --- | --- |
| 여는 곳 | 게임 안 "서버 열기" | 게임 안 "서버 열기" | 서버 컴퓨터에서 서버 프로그램 실행 + 설정 파일 |
| 실행 형태 | 별도 프로세스(화면 없음, 트레이 아이콘이나 작은 창) | 앱 안 | 별도 프로세스(리눅스·윈도우) |
| 꺼지는 때 | 그 PC가 꺼질 때 | 주인이 앱을 나갈 때(서버 종료) | 운영자가 끌 때 |
| 인원 | 2~50 | 2~10 | 2~50 |

서버가 켜지면 스스로 서버 목록에 등록합니다(공개일 때만). 서버 연 사람이 주소를 어디에 입력할 필요는 없습니다.

### 3.2 판정 주체는 "서버"

- 판정과 저장은 항상 서버 모듈이 합니다. 서버 모듈은 플레이어가 없는 프로세스일 수도 있고(PC, 데디케이티드), 플레이어 앱 안일 수도 있습니다(모바일).
- 게임 내용 코드는 이 둘을 구분하지 않습니다. 지금 코드는 "방장 = 화면 속 플레이어"라고 가정하므로(방장 캐릭터, 방장 PC 저장, `GameSession.IsAuthority`) 이 가정을 걷어내야 합니다.
- 지금의 방장 넘기기 흐름(`NetWorldBridge`의 핸드오버, `WorldSave` 잠금·세대 번호)은 PC·데디케이티드에는 필요 없고, 모바일은 서버 종료로 정했으므로 **삭제 대상**입니다.
- 지금의 C# 방 로직(`HostRoom`, `RoomNode`)이 서버 모듈의 출발점입니다.

### 3.3 운영 기능 (공개 서버라 필요)

- 서버 설정 파일: 이름, 인원, 공개 여부, 비밀번호, 관리자 계정, 월드 규모(시민 수·교통량), 포트
- 관리자 명령: 추방, 차단, 공지, 저장, 종료
- 자동 저장과 백업(주기 저장, 이전 저장본 몇 개 보관)

## 4. 연결 통로

| 통로 | 용도 | 비고 |
| --- | --- | --- |
| EOS | 모든 서버의 목록·코드 입장, 기기 연결, 직접 연결이 안 되면 중계 | 무료. 기기 ID로 로그인(Device ID)해서 플레이어에게 Epic 계정이 필요 없음 |
| 직접 IP·포트 | 데디케이티드 서버 | 서버 운영자가 포트를 엶 |
| Steam | PC 스팀판(선택) | 크로스플레이 안 됨 |
| Cloudflare(지금 서버) | 개발 테스트용 | 출시에 쓰지 않음 |

- 기존 `INetTransport`(지금 `NetClient`, `SteamTransport`) 아래에 `EosTransport`를 추가하는 형태로 갑니다.
- 연결 형태는 **서버 중심(스타형)** 입니다. 모든 클라이언트는 서버에만 연결하고, 서버가 관심 범위(4.2) 안의 사람에게만 중계합니다. 세션 중간에 기기끼리 직접 잇는 메시 구조를 검토했지만, 서버가 별도 프로세스이고 인원이 50명이라 스타형이 단순하고 충분하다고 정했습니다.
- 계정 ID는 EOS ProductUserId(Steam 모드는 SteamID)를 씁니다. 캐릭터는 이 ID를 키로 월드 파일에 저장합니다.

### 4.1 확인 결과 (2026-10-13, 공식 문서 기준)

문서만 읽고 확인한 것입니다. 실제로 돌려 본 것은 없습니다. "확인 못 함"은 문서에서 답을 찾지 못한 항목입니다.

| 항목 | 결과 | 출처 |
| --- | --- | --- |
| EOS 무료 조건 | **무료 확인.** 로열티·호스팅 비용 없음, 타이틀 수와 플레이어 계정 수 제한 없음. 유료는 상위 안티치트(엔터프라이즈 계약)뿐. 게임 용도로만 써야 함. Epic 스토어 출시 의무 없음 | [EOS Licensing](https://onlineservices.epicgames.com/licensing), [onlineservices llms.txt](https://onlineservices.epicgames.com/llms.txt), [Developer Agreement](https://onlineservices.epicgames.com/services/terms/agreements) (약관 전문은 읽지 않음) |
| Unity용 플러그인 | PlayEveryWare 플러그인(MIT, `EOS-Contrib/eos_plugin_for_unity`)이 활발히 관리됨(최신 v6.2.0, 2026-09-22, EOS SDK 1.19.2.1). **공식 대상 버전은 Unity 2021.3.16f1이고 Unity 6 지원은 문서에 없음.** Unity 6.6에서 Fast Enter Play Mode 사용 시 에디터가 멈춘다는 사용자 보고가 있음 → **Unity 6000.4에서 되는지는 직접 시험해야 함** | [README](https://github.com/EOS-Contrib/eos_plugin_for_unity/blob/stable/com.playeveryware.eos/README.md), [supported_platforms.md](https://github.com/EOS-Contrib/eos_plugin_for_unity/blob/stable/com.playeveryware.eos/Documentation~/supported_platforms.md), [releases](https://github.com/EOS-Contrib/eos_plugin_for_unity/releases), [포럼 보고](https://forums.unrealengine.com/t/playeveryware-eos-sdk-causing-editor-crashes-when-entering-play-mode-in-unity-6-6-fast-enter-play-mode/2833634) |
| Android/iOS | 플러그인 지원 플랫폼에 Windows, Linux, macOS, Android, iOS 포함(소셜 오버레이만 모바일·리눅스 미지원). EOS SDK 요구사항: Android 6.0(API 23) 이상, iOS 11 이상. 실기기 동작은 확인 못 함 | 위 supported_platforms.md, [EOS System Requirements](https://dev.epicgames.com/docs/epic-online-services/eos-get-started/system-requirements) |
| 플레이어 로그인 | Device ID 로그인(`EOS_Connect_CreateDeviceId` → `EOS_ECT_DEVICEID_ACCESS_TOKEN`)은 "모바일·PC 게임이 계정 없이 시작"하는 용도. Device ID는 **OS 사용자 프로필 단위**로 정해짐 | [EOS_Connect_CreateDeviceId](https://dev.epicgames.com/docs/api-ref/functions/eos-connect-create-device-id), [EOS_EExternalCredentialType](https://dev.epicgames.com/docs/api-ref/enums/eos-e-external-credential-type) |
| 서버 프로세스의 EOS 중계 | EOS P2P는 보내는 쪽 로컬 ProductUserId가 **필수**(`SendPacketOptions.LocalUserId`). 직접 연결이 안 되면 TURN 중계로 넘어감(기본값 `AllowRelays`). **플레이어가 아닌 서버 프로세스가 Device ID로 PUID를 받아 P2P를 열 수 있는지는 문서에 허용도 금지도 없어 확인 못 함.** 서드파티(Redpoint) 문서는 "로그인하지 않은 데디케이티드 서버는 P2P를 못 엶"이라고 씀 | [P2P Intro](https://dev.epicgames.com/docs/epic-online-services/multiplayer/nat-p2p-interface/p2p-guide/p2p-intro), [P2P Reference](https://dev.epicgames.com/docs/epic-online-services/multiplayer/nat-p2p-interface/p2p-reference), [SendPacketOptions](https://dev.epicgames.com/docs/api-ref/structs/eos-p-2-p-send-packet-options), [Redpoint](https://docs.redpoint.games/docs/setup/networking/p2p_vs_ip) |
| 서버 목록(Sessions) | 데디케이티드 서버도 세션 생성 가능("A user or a dedicated server creates the session"). 한도: 세션당 인원 1000, 속성 64개(헤더 기준, 문서 표는 100), **검색 1회 최대 200개**, 검색에 페이지 넘김 기능 없음. 사용자별 생성·수정·검색 **분당 30회**, 배포 전체는 동접 1명당 분당 30회. 세션 ID를 직접 정할 수 있음(16~64자) → 비공개 서버 코드로 쓸 수 있음. 검색하려면 검색하는 쪽 PUID 필요 | [Sessions Intro](https://dev.epicgames.com/docs/epic-online-services/multiplayer/lobbies-and-sessions/sessions-interface/sessions-intro), [CreateSessionSearch](https://dev.epicgames.com/docs/api-ref/functions/eos-sessions-create-session-search), 플러그인의 `SessionsInterface.cs`(`MAX_SEARCH_RESULTS = 200`) |
| ARM 리눅스 서버 빌드 | Unity 6.4는 Linux ARM64 데디케이티드 서버 빌드를 지원함(**IL2CPP 필수**. 빌드 오류 버그는 6000.4.0a5에서 고침. 우리는 6000.4.5f1). EOS SDK 자체는 LinuxARM64를 지원하지만(릴리스 노트) **플러그인에는 리눅스 x64 라이브러리만 들어 있음** → ARM64용 `libEOSSDK`를 SDK에서 직접 넣어야 함. Oracle Always Free: Arm 2 OCPU·12GB, 송신 월 10TB. 실제 실행은 확인 못 함 | [Unity Issue Tracker](https://issuetracker.unity.com/issues/18979/internal-build-system-error-when-building-a-linux-server-for-arm), [Sysroot Linux Arm64](https://docs.unity3d.com/6000.4/Documentation/Manual/com.unity.sdk.linux-arm64.html), [EOS Release Notes](https://dev.epicgames.com/docs/epic-online-services/release-notes), [Oracle Always Free](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm) |
| 이 PC의 Unity 모듈 | 6000.4.5f1에 **Windows 빌드 모듈만 설치됨.** Linux(Server)·Android·iOS 모듈은 설치해야 함 | `Editor/Data/PlaybackEngines` 확인 |

### 4.2 결과로 설계에 반영할 것

1. **PC 서버 프로세스와 같은 PC의 게임은 Device ID가 같다**(같은 OS 사용자). 그래서 내 PC 서버에 붙는 내 게임은 EOS가 아니라 **로컬 연결(127.0.0.1)** 로 붙고, EOS 로그인은 서버 프로세스가 맡습니다. 내 캐릭터의 계정 ID는 그 PUID를 그대로 씁니다.
2. **서버 프로세스가 Device ID로 PUID를 받아 P2P를 여는 것은 2단계 첫 작업으로 시험합니다.** 안 되면 PC 서버는 데디케이티드처럼 직접 IP·포트(포트 열기 안내)로 가고, EOS는 목록 등록에만 씁니다. 모바일 서버는 플레이어 앱 안이라 문제없습니다.
3. 서버 목록: 서버는 인원 변경을 30초에 한 번 이하로 묶어 갱신하고, 클라이언트는 목록 새로고침을 수동·간격 제한으로 합니다. 공개 서버가 200개를 넘으면 지역·이름 조건으로 나눠 검색합니다.
4. 플러그인의 Unity 6000.4 동작(에디터 플레이 모드, Windows·Android 빌드)도 2단계 첫 시험에 넣습니다.

사용자가 직접 해야 하는 것(변동 없음): Epic 개발자 포털 제품 등록, Unity Hub에서 Linux Dedicated Server·Android·iOS 모듈 설치(필요한 단계에서).

사용자가 직접 해야 하는 것: Epic 개발자 포털에서 제품 등록(무료)을 하고 ID 값(ProductId, SandboxId, DeploymentId, ClientId/Secret)을 받기.

## 5. 공통 동기화 계층

### 5.1 구성

| 요소 | 역할 |
| --- | --- |
| `NetEntity` 컴포넌트 | 공유 오브젝트에 붙임. 고유 ID, 소유자(서버 또는 플레이어), 소속 맵. 생성·삭제가 자동으로 전원에게 전달됨 |
| 복제 상태 | 컴포넌트가 공유할 값을 표시(속성 또는 등록 함수). 바뀐 값만 보냄 |
| 이벤트 | 공격, 탑승, 앉기, 상호작용 같은 순간 동작. 같은 엔티티의 같은 컴포넌트가 받아서 실행 |

**메시지는 이 두 종류로 고정합니다:** 엔티티 상태(생성/변경/삭제), 엔티티 이벤트. 기능을 추가해도 메시지 종류와 서버 코드는 바뀌지 않습니다.

### 5.2 권한 규칙

| 대상 | 판정하는 쪽 |
| --- | --- |
| 내 캐릭터 이동, 애니메이션, 든 물건 | 그 플레이어 |
| 내가 모는 차, 내가 던진 물건의 물리 | 그 플레이어(주인). 결과만 보냄 |
| 돈, 소유권, 땅, 회사, 아이템 생성·삭제, 드롭 줍기, 탑승 자리 | 서버 (먼저 온 요청만 성공. 예: 한 차에 한 명만 탐) |
| 데미지 | 서버 판정(치는 쪽이 요청, 서버가 적용) |
| 시민·교통 | 각 기기가 같은 계산(7장). 플레이어가 개입한 반응만 서버 판정 |

클라이언트가 보낸 요청은 서버가 검증합니다. 공개 서버이므로 클라이언트를 믿지 않습니다.

### 5.3 부하 줄이기

- 관심 범위: 같은 맵(씬)에 있는 사람에게만 보냅니다. 넓은 맵은 거리 기준을 추가합니다.
- 이진 메시지: JSON 대신 이진 형식으로 보냅니다(위치 하나에 20~30바이트). 움직이지 않으면 보내지 않습니다.
- 위치 갱신은 초당 10회 정도, 멀리 있는 엔티티는 더 드물게 보냅니다.
- 늦게 들어온 사람에게는 그 맵의 엔티티 전체 상태를 한 번 보냅니다.
- 모바일 클라이언트는 멀리 있는 플레이어를 단순 모델로 그립니다(LOD).

부하 테스트로 실제 수치를 확인합니다. 방법은 가짜 클라이언트 49명(PC 서버)과 9명(모바일 서버)을 붙이고 서버 기기의 CPU와 업로드량을 재는 것입니다.

### 5.4 옮길 기존 기능 (설계 확정 후 목록으로 뽑아 확인)

플레이어(위치, 외형·성별, 이름표, 공격 동작, 든 물건, 앉기, 탑승), 설치한 탈것(설치·탑승·하차·해체), 드롭 아이템, 공유 경제(지금 `op`·`state`로 하는 것), 시민·교통(7장 전까지는 기존 방식 유지). 지금의 `pos`, `op`, `state`, `world`, `msg` 전용 경로는 옮긴 뒤 지웁니다.

## 6. 저장과 시계

### 6.1 저장

- 서버의 월드 파일 하나에 월드 상태와 그 월드의 캐릭터 전부(계정 ID별: 이름, 성별, 인벤토리, 은행돈, 주식, 위치, 캐릭터 시계)를 넣습니다.
- 처음 들어온 계정은 "캐릭터 만들기"(이름, 성별)를 거치고, 기본 인벤토리·돈으로 시작합니다. 다른 월드의 돈이나 아이템은 섞이지 않습니다.
- 서버 밖(개인 PC)에는 캐릭터 진행을 저장하지 않습니다. 오프라인 혼자 하기도 "내 PC 서버"로 통일할 수 있습니다(결정 필요, 9장).

### 6.2 시계 두 개

지금 시계는 게임 1시간 = 현실 5분(`SimpleGameClock.realSecondsPerGameHour = 300`)입니다. 게임 하루가 현실 2시간이라 24시간 서버에서 일주일이면 게임으로 84일이 흐릅니다.

| 시계 | 흐르는 때 | 따르는 것 |
| --- | --- | --- |
| 월드 시계 | 서버가 켜진 동안 계속 | 낮밤, 날씨, 주가, 시민·교통, 가게 영업시간 |
| 캐릭터 시계 | 그 캐릭터가 접속한 동안만 | 나이, 배고픔·피로, 월급·월세·이자 같은 개인 정산 |

접속을 끊으면 캐릭터는 "쉬는 중"으로 멈춥니다. 다시 들어오면 나이와 상태는 나간 시점 그대로이고 세상만 흘러 있습니다. 비운 동안의 개인 정산은 받지 않거나 상한을 둡니다(9장에서 결정).

## 7. 시민·교통 재작성 (시간 기반 결정적 계산)

지금 코드를 그대로 각 기기에서 돌리면 금방 어긋납니다. 원인은 이렇습니다.

- `CitizenWalker`: `NavMeshAgent`가 서로 피해 가는 움직임을 매 프레임 계산해서, 프레임 속도가 다른 기기마다 결과가 다름
- `TrafficCar`, `TrafficSpawner`: 시드 없는 `Random`으로 차종·출발 위치·갈림길을 정함
- 앞에 사람이나 시민이 있으면 멈추는 판정(`Physics.OverlapBox`)이 기기마다 다르게 보이는 위치에 반응함

새 방식은 이렇습니다.

1. 위치를 "경로 + 시작 시각 + 월드 시계"로 바로 계산합니다(매 프레임 누적 금지). 오차가 쌓이지 않습니다.
2. 차종, 경로, 갈림길, 신호 주기를 서버 시드와 월드 시계로 정합니다.
3. 플레이어가 개입한 반응(차가 플레이어 때문에 멈춤, 시민과 대화)은 서버가 판정하고 이벤트로 알립니다.
4. 서버가 2~5초마다 작은 보정값을 보냅니다.
5. 늦게 들어온 사람은 시드와 월드 시계만 받으면 같은 장면을 계산합니다.

시민끼리 비켜 가는 움직임은 정해진 길(인도 차선) 위를 시간차를 두고 걷는 방식으로 바뀝니다. 재작성이 끝나기 전까지는 기존 시민·교통 동기화를 유지합니다.

## 8. 모바일

- 터치 조작: 지금 코드에는 터치 입력이 없습니다. 이동 조이스틱, 카메라 드래그, 공격·상호작용·탑승 버튼이 필요합니다.
- 성능: 품질 설정 분리(그림자, 그리기 거리, 시민 표시 수), 멀리 있는 플레이어 LOD
- 재접속: 앱 전환이나 화면 꺼짐 뒤 같은 서버, 같은 캐릭터로 자동 복귀
- UI: 타이틀, 인벤토리, 폰 UI를 작은 화면과 터치에 맞춤
- 모바일 서버: 앱이 백그라운드로 가면 곧 끊긴다는 안내, 주인이 나가면 서버 종료(저장 후)

## 9. 남은 기획 결정 (사용자가 정할 것)

1. 나이·수명 시스템을 만드는가. 만든다면 플레이 시간 몇 시간이 1살인가 (지금 코드와 문서에는 없음)
2. 비운 동안의 월세·이자·세금: 받지 않음 / 상한(며칠치)
3. 오프라인 캐릭터: 사라짐 / 집에서 자는 모습으로 남김
4. 오프라인 혼자 하기를 "내 PC 서버(비공개, 1명)"로 통일할지
5. 캐릭터 이름 중복을 같은 월드 안에서 막을지 (지금은 막지 않음)

## 10. 작업 순서

1. **[완료 2026-10-13, 12.1]** 공통 동기화 계층 (서버 모듈 분리, `NetEntity`, 상태·이벤트 메시지, 권한 규칙, 관심 범위, 이진 메시지). 판정 주체는 처음부터 별도 프로세스·앱 안 겸용
2. EOS 연결 + 서버 열기/참가하기 통합 UI + 공개/비공개 + 인원 제한(PC 50 / 모바일 10) + 모바일 주인 퇴장 시 서버 종료. **EOS 시험부터 시작합니다(Epic 포털 등록 후, 12.2).** 옛 방식(Cloudflare·Steam 방) 메뉴는 4단계까지 '개발용'으로 남깁니다 (2026-10-13 결정)
3. 캐릭터 저장을 서버 월드 파일로 이전, 캐릭터 시계
4. 기존 기능을 동기화 계층으로 이전(5.4), 옛 전용 경로 삭제. **방장 넘기기 코드(`NetWorldBridge` 핸드오버, `WorldSave` 잠금·세대 번호, `TransferHost`, `RoomNode`/`RoomMirror` 승격, Node·Worker의 transfer·이관)도 여기서 옛 경로와 함께 지웁니다** (2026-10-13 결정: 곧 지울 코드를 두 번 고치지 않음)
5. 시민·교통 시간 기반 재작성
6. 데디케이티드 서버 빌드, 설정 파일, 서버 목록, 관리자 기능, 부하 테스트
7. 모바일 터치 조작, 성능, 재접속

각 단계는 시작 전에 이 문서에 세부 설계를 덧붙이고 확인받습니다.

## 11. 현재 작업 트리 상태 (2026-10-13, 미커밋)

같은 날 이 설계를 정하기 전에, 지금 구조 위에 손으로 동기화를 붙이는 작업을 했습니다. Cloudflare Worker에는 배포하지 않았습니다.

**정리 완료 (2026-10-13):** 아래 변경은 버리지 않고 Dev에 그대로 커밋했습니다(`27583f5`). 대체 대상은 표의 "새 설계에서" 칸에 적은 단계에서 지웁니다(`pos` 확장·`veh.*` op → 4단계, 서버 `character_state`·월드별 개인 저장 → 3단계). 무관한 변경은 커밋하지 않았습니다.

| 변경 | 내용 | 새 설계에서 |
| --- | --- | --- |
| `pos` 확장 | `NetMessages.cs`, `NetSession.SendPos`, `HostRoom.Pos`, `Server/src/protocol.ts`·`rooms.ts`에 `nm`(이름), `g`(성별), `veh`(탄 탈것), `act`(공격 횟수) | 동기화 계층으로 대체, 삭제 |
| 원격 아바타 | `NetPlayerSync.cs`: `NameTag` 이름표, 남녀 외형, 탄 차 모델, 공격 팔 휘두르기 | 표시 코드(외형, 차 모델, 팔 동작)는 플레이어 엔티티 컴포넌트로 옮겨 재사용 가능 |
| 설치한 차 공유 | `WorldDroppedItem.cs`: `veh.put/ride/park/del` op, 월드 저장에 `vehicles`, `WorldOps.VehicleOp`·`WorldSave.CaptureVehicles/RestoreVehicles` 훅, `SharedWriteGate.SetSendFailed` | 탈것 엔티티로 대체. 월드 저장에 설치한 차를 넣는 생각은 유지 |
| 월드별 캐릭터 | 서버 `character_state` 표와 `/me/state?world=`(Node·Worker), `PlayerData`/`SteamLocalState`/`NetPlayerSave` 월드별 저장, `GameSession.SetCharacter`, 타이틀 "캐릭터 만들기" 화면, 계정 닉네임 자동 생성 | 개념(월드별 캐릭터, 캐릭터 만들기 화면)은 유지. 저장은 서버 월드 파일로 이전(6.1) |
| 내 이름표 | `PlayerInteraction`: 계정 닉네임 대신 `GameSession.LocalPlayerName`(캐릭터 이름) | 유지 |
| 방에서 개인 저장 막기 | `WorldSaveRunner.SavePersonal`이 방 안에서는 PC 파일에 쓰지 않음 | 6.1 기준으로 다시 정리 |
| QA | `RemoteAvatarQA.cs`(신규), `WorldSaveQA`(탈것 11개), `TitleFlowQA`·`ServerBudgetQA` 수정, `Server/test/ws.test.ts` | 동기화 계층 QA로 교체 |
| 문서 | `Docs/Server_API.md`에 `?world=`, `pos` 새 필드, `veh.*` op | 새 설계에 맞춰 다시 씀 |

이 변경으로 통과한 테스트: Node 서버 42개, Worker 31개, WorldSaveQA 68개, NetSessionQA, TitleFlowQA 108개, RemoteAvatarQA 18개.

작업 트리에는 이번 작업과 무관한 기존 변경도 있습니다(`Docs/README.md`, 폰트·렌더 설정 에셋, `CompanyGame/Assets/_Recovery/`, `Docs/AI_Player_Plan.md`, `.wrangler/`). 커밋할 때 섞지 않습니다.

## 12. 단계별 세부 설계

### 12.1 1단계: 공통 동기화 계층 (완료 2026-10-13)

**범위.** 새 계층을 기존 경로 옆에 따로 만듭니다. 기존 `NetSession`(`pos`/`op`/`state`/`world`/`msg`), Cloudflare·Steam 방, 경제 판정(`GameSession.IsAuthority`)은 이 단계에서 건드리지 않습니다. 기능 이전은 4단계, 방장 넘기기 삭제와 EOS는 2단계에서 합니다. 이 단계의 결과물은 "서버 모듈 + 엔티티 동기화 API + TCP 직접 연결"이고, 실제 게임 기능은 아직 옮기지 않습니다.

**어셈블리.** 서버 모듈과 메시지 코덱은 Unity를 참조하지 않는 새 어셈블리 `CompanyGame.NetCore`(`noEngineReferences: true`, 참조 없음)에 둡니다. 그래서 같은 코드를 앱 안에서도, 화면 없는 별도 프로세스에서도, 메모리 안 QA에서도 그대로 돌릴 수 있습니다. `CompanyGame.Net`이 이것을 참조합니다.

```
NetCore (Unity 없음) → Net → Player/UI
```

**새로 만들 파일**

| 파일 | 내용 |
| --- | --- |
| `Scripts/NetCore/CompanyGame.NetCore.asmdef` | 참조 없음, noEngineReferences |
| `NetCore/SyncProtocol.cs` | 메시지 종류 상수, 프로토콜 버전, 한도, 읽기·쓰기 도우미(`BinaryWriter`/`BinaryReader` 재사용, little-endian, 문자열 = u16 길이 + UTF-8) |
| `NetCore/SyncTransport.cs` | `IServerTransport`, `IClientTransport` 인터페이스와 메모리 안 `LoopbackTransport`(QA용) |
| `NetCore/TcpTransport.cs` | `TcpServerTransport`, `TcpClientTransport`(`System.Net.Sockets`, 4바이트 길이 + 본문 프레임, 송수신은 스레드, 처리는 `Tick`/`TryReceive`를 부른 스레드). 직접 IP 접속과 4.2의 "내 PC 서버 = 127.0.0.1" 연결에 씀 |
| `NetCore/GameServer.cs` | 서버 모듈: 접속자 목록, 엔티티 등록부, 권한 검사, 관심 범위(맵), 늦은 입장 스냅샷, 퇴장 처리, 소유권 요청, 한도·속도 제한. `Tick()`으로 돌고, 서버 쪽 게임 로직은 `ServerEvent` 콜백으로 붙임(이 단계에선 비어 있음) |
| `Net/SyncClient.cs` | 클라이언트 쪽 단일 객체(`NetSession`처럼 자동 생성): 접속·인사, 현재 맵 알림, 원격 엔티티 생성·삭제, 상태 적용, 바뀐 상태 0.1초마다 전송, 이벤트 전달 |
| `Net/NetEntity.cs` | 공유 오브젝트에 붙이는 컴포넌트. Id, 소유자, `IsMine`, 프리팹 키, 플래그. 같은 오브젝트의 `NetBehaviour`들을 `GetComponents` 순서로 번호 매김(양쪽 프리팹이 같으니 번호도 같음) |
| `Net/NetBehaviour.cs` | 공유할 값을 가진 컴포넌트의 부모 클래스. `Write(BinaryWriter)`/`Read(BinaryReader)`(그 컴포넌트의 **전체 상태**), `MarkDirty()`, `SendEvent(id, target, write)`, `OnEvent(id, sender, reader)`, `IsOwner` |
| `Net/NetTransform.cs` | 위치·회전 공유의 표준 컴포넌트(위치 float 3개 + 회전 u16). 받는 쪽은 보간. 움직이지 않으면 안 보냄 |
| `Net/SyncHost.cs` | 서버 모듈을 Unity에서 돌리는 MonoBehaviour. 앱 안 서버(`StartInApp(port, max)`, 내 클라이언트는 127.0.0.1로 붙음)와 별도 프로세스 서버(명령줄 `-gameserver <port> [-maxplayers N]`, 배치 모드에서 플레이어 없이 서버만)를 같은 코드로 처리. (`-server`는 이미 Cloudflare 주소 인자라 쓰지 않음) |
| `Player/SyncMapBinder.cs` | `SceneLoadManager.CurrentMap.path`를 `SyncClient.SetMap`에 알림(Net은 World를 참조할 수 없어서 `NetPlayerSync`처럼 Player에 둠). 몇 줄짜리 |
| `Editor/Net/SyncLayerQA.cs` | 아래 검수 방법의 QA |

**바꿀 파일:** `Net/CompanyGame.Net.asmdef`(NetCore 참조 추가)뿐입니다. `Player` asmdef는 이미 Net을 참조합니다.

**지울 기존 코드: 없음.** 옛 경로는 4단계에서 기능을 옮긴 뒤, 방장 넘기기는 2단계에서 지웁니다. 새 계층에는 기존 `INetTransport`를 쓰지 않습니다(그것은 JSON `NetMsg`용이고 옛 경로와 함께 지워짐). `HostRoom`의 검증 방식(오류 코드, 크기 제한, 가입 순서)은 참고만 하고 고치지 않습니다.

**메시지 형식 (이진, little-endian).** 전송 단위 하나 = `u8 종류` + 본문. `str` = u16 바이트 길이 + UTF-8, `blob` = u16 길이 + 바이트. 엔티티 ID는 `u64` = (접속 번호 u32 << 32) | 그 접속 안 순번 u32. 접속 번호는 서버가 접속마다 새로 주는 번호(서버가 켜져 있는 동안 재사용하지 않음, 0 = 서버)라서 클라이언트가 왕복 없이 ID를 만들어도 겹치지 않습니다.

| 방향 | 종류 | 본문 |
| --- | --- | --- |
| C→S | 1 Hello | u16 프로토콜 버전, str 계정 ID, str 이름, str 맵 |
| C→S | 2 Spawn | u64 id, str 프리팹 키, str 맵, u8 플래그, u8 컴포넌트 수, blob × 수 |
| C→S | 3 State | u64 id, u8 컴포넌트 번호, blob 전체 상태 |
| C→S | 4 Despawn | u64 id |
| C→S | 5 Event | u64 id, u8 컴포넌트 번호, u8 이벤트 번호, u8 대상(0 주인, 1 맵 전원, 2 서버), blob |
| C→S | 6 Map | str 맵 (내 관심 범위가 바뀜) |
| C→S | 7 Claim | u64 id (소유권 요청) |
| S→C | 101 Welcome | u32 내 접속 번호, u16 최대 인원 / 거절이면 102 |
| S→C | 102 Reject | u8 이유(버전, 꽉 참, 형식 오류, 속도 초과) 후 연결 끊음 |
| S→C | 103 Spawn | u64 id, u32 주인, str 프리팹 키, str 맵, u8 플래그, 컴포넌트 blob들 |
| S→C | 104 State | u64 id, u8 컴포넌트 번호, blob |
| S→C | 105 Despawn | u64 id |
| S→C | 106 Event | u64 id, u8 컴포넌트 번호, u8 이벤트 번호, u32 보낸 사람, blob |
| S→C | 107 Owner | u64 id, u32 새 주인 (소유권 변경) |
| S→C | 108 Peer | u8 들어옴/나감, u32 접속 번호, str 계정 ID, str 이름 |

엔티티 플래그: bit0 `PersistOnLeave`(주인이 나가면 지우지 않고 서버 소유로 넘김, 예: 설치한 차), bit1 `Claimable`(서버 소유일 때 소유권 요청을 받음, 예: 빈 차에 타기), bit2 `FollowsOwnerMap`(주인이 맵을 옮기면 같이 옮김, 예: 플레이어 아바타).

연결 수준 메시지(Hello, Map, Claim, Welcome, Reject, Owner, Peer)는 고정된 목록입니다. 기능을 더해도 늘지 않습니다. 기능이 쓰는 것은 상태(Spawn/State/Despawn)와 이벤트 두 가지뿐입니다(5.1).

State는 항상 그 컴포넌트의 **전체 상태**입니다. 그래서 하나가 빠져도 다음 것으로 맞춰지고, 2단계 EOS에서 위치를 비신뢰 채널로 보내도 됩니다. 크기: 위치 State = 1 + 8 + 1 + 2(blob 길이) + 12 + 2 = 26바이트(+TCP 길이 4).

**권한 규칙 (서버가 검사, 5.2)**

| 요청 | 허용 조건 | 어기면 |
| --- | --- | --- |
| Spawn | ID의 접속 번호가 보낸 사람 것, ID 중복 없음, 접속당 엔티티 수 한도(256) 안 | 무시 + 경고 카운트 |
| State / Despawn | 보낸 사람이 현재 주인 | 무시 |
| Event 대상 0/1 | 엔티티가 보낸 사람의 맵에 있음 | 무시 |
| Event 대상 2(서버) | 위와 같음, `ServerEvent` 콜백으로 넘김 | — |
| Claim | 엔티티가 `Claimable`이고 지금 서버 소유. **먼저 온 요청만 성공**, 결과는 맵 전원에게 Owner로 | 요청자에게만 Owner(그대로) |
| 프레임 | 크기 ≤ 16KB, blob ≤ 4KB, 초당 300개 이하, 형식이 맞음 | Reject 후 연결 끊음 (예외로 서버가 죽지 않음) |

이 단계에서는 Hello의 계정 ID를 그대로 믿습니다. 직접 IP 서버에서 남의 계정 ID를 사칭하는 문제는 2단계(EOS ID 토큰 검증)와 6단계에서 막습니다.

**관심 범위.** 서버는 엔티티의 맵과 같은 맵에 있는 접속자에게만 Spawn/State/Despawn/Event를 보냅니다. 접속자가 Map으로 맵을 바꾸면, 이전 맵 엔티티는 그 접속자에게 Despawn으로, 새 맵 엔티티는 Spawn(전체 상태 포함)으로 보냅니다. 늦게 들어온 사람도 같은 방식으로 받습니다. 거리 기준과 "멀리 있으면 드물게 보내기"는 넓은 맵이 생길 때 추가합니다(이 단계에서 하지 않음).

**퇴장.** 접속이 끊기면 그 사람 엔티티 중 `PersistOnLeave`는 서버 소유로 넘기고(Owner 0), 나머지는 Despawn합니다. Peer(나감)를 보냅니다.

**Unity 쪽 사용법 (기능 코드가 하는 일)**

```csharp
// 공유할 컴포넌트
public sealed class DoorState : NetBehaviour {
    public bool open;
    public override void Write(BinaryWriter w) => w.Write(open);
    public override void Read(BinaryReader r) => open = r.ReadBoolean();
    public void Toggle() { open = !open; MarkDirty(); }          // 주인일 때만 보냄
}
// 생성: 주인이 만들면 같은 맵 사람에게 자동으로 생김
SyncClient.Instance.Spawn(prefabKey: "Net/Door", instance, flags);
```

원격 생성은 `Resources.Load(프리팹 키)`로 합니다. QA가 에셋 없이 시험할 수 있게 `SyncClient.PrefabFactory`(키 → GameObject, 기본값 Resources.Load)를 바꿀 수 있게 둡니다.

**완료 기준**

1. `CompanyGame.NetCore`가 Unity 참조 없이 컴파일되고, 프로젝트 전체가 배치 모드에서 오류 없이 컴파일됨
2. 메모리 QA(`SyncLayerQA.Run`)가 아래 검수 목록 1~12를 모두 통과
3. 위치 State 하나가 전송 바이트 30 이하임을 QA가 측정해서 출력
4. 플레이 모드 QA(`SyncLayerQA.RunPlay`): 앱 안 서버(TCP 127.0.0.1)에 실제 클라이언트가 붙고, 가짜 TCP 클라이언트가 만든 테스트 엔티티가 씬에 나타나고 움직이고 이벤트를 받고, 가짜 클라이언트가 끊기면 사라짐. 반대로 Unity 쪽이 만든 엔티티를 가짜 클라이언트가 받음. QA_SHOTS로 나타난 장면을 찍음
5. 별도 프로세스 QA(`SyncLayerQA.RunProcess`): Windows 플레이어를 빌드해 `-batchmode -nographics -gameserver <port>`로 띄우고, 가짜 TCP 클라이언트 두 개가 서로의 엔티티를 받음. 서버 프로세스를 끄면 클라이언트가 끊김을 알아챔
6. 부하 측정(`SyncLayerQA.RunLoad`, 판정 없이 수치만): 메모리 안 가짜 클라이언트 49명이 같은 맵에서 10Hz로 위치를 10초 보냄 → 서버 Tick 평균·최대 시간, 서버 송신 바이트/초를 출력. 같은 것을 9명으로도
7. 기존 QA 회귀 없음: `NetSessionQA.Run`, `WorldSaveQA.RunCli`, `TitleFlowQA.Run`, `RemoteAvatarQA.Run`, 서버 `npm test`(Server, Server/worker; 서버 코드는 안 바뀌지만 확인)

**검수 목록 (메모리 QA)**

1. Hello → Welcome, 접속 번호가 서로 다름. 버전이 다르면 Reject
2. 최대 인원을 넘으면 Reject
3. A가 만든 엔티티를 같은 맵의 B는 받고, 다른 맵의 C는 받지 않음
4. A의 State가 B에게 감. B가 A의 엔티티에 보낸 State·Despawn은 무시됨
5. 남의 접속 번호로 만든 Spawn, 중복 ID Spawn은 무시됨
6. 이벤트 대상 0은 주인에게만, 1은 맵 전원에게, 2는 `ServerEvent` 콜백으로 감. 받은 쪽에 보낸 사람 번호가 맞게 들어 있음
7. C가 A의 맵으로 옮기면 A의 엔티티를 전체 상태와 함께 받고, 다시 떠나면 Despawn을 받음. 늦게 들어온 D도 같음
8. `FollowsOwnerMap` 엔티티는 주인이 맵을 옮기면 같이 옮겨짐(옛 맵 사람에게 Despawn, 새 맵 사람에게 Spawn)
9. A가 끊기면 일반 엔티티는 Despawn, `PersistOnLeave` 엔티티는 Owner 0으로 남음. Peer(나감)가 감
10. 서버 소유 `Claimable` 엔티티에 B와 C가 같은 Tick에 Claim → 먼저 온 B만 주인이 됨. 이후 B의 State는 통과, C의 State는 무시
11. 형식이 깨진 프레임, 16KB 넘는 프레임, 초당 300개 초과 → 그 접속만 Reject·끊김, 서버와 다른 접속은 계속 돎
12. 엔티티 상태 코덱: `NetTransform` 쓰기→읽기 값이 같음(회전 u16 오차 0.01도 이하)

**총괄 검수 방법:** diff 전체를 읽고(특히 권한 검사 누락, 스레드에서 Unity API 호출, 예외로 서버가 죽는 경로), 컴파일과 QA 4종을 직접 다시 돌려 수치를 확인하고, 플레이 모드 스크린샷을 직접 봅니다.

**이 단계에서 하지 않는 것:** EOS, 서버 열기 UI, 계정 검증(2단계) / 캐릭터 저장(3단계) / 플레이어·탈것·드롭 아이템·경제 이전(4단계) / 시민·교통(5단계) / 거리 기반 관심 범위, 서버 설정 파일, 관리자 명령(6단계).

#### 12.1 결과 (총괄 검수 완료)

**테스트 수치** (총괄이 직접 다시 돌림)
- `SyncLayerQA.Run`: 53개 통과. 위치 State는 26바이트이고, TCP 길이를 더하면 30바이트
- `SyncLayerQA.RunPlay`: 13개 통과. Welcome 전 맵 변경 확인을 포함하고, 스크린샷도 확인
- `SyncLayerQA.RunProcess`: 5개 통과. 빌드한 `-gameserver` 프로세스를 썼고, 빌드는 `CompanyGame/Builds/`(git 무시)에 들어감
- `SyncLayerQA.RunLoad`(메모리 안, 10Hz, 10초):

| 인원 | Tick 평균 | Tick 최대 | 송신(TCP 길이 포함) | 메시지/초 |
| --- | --- | --- | --- | --- |
| 49명 | 0.153ms | 3.1ms | 약 705KB/s (약 5.6Mbps) | 23,492 |
| 9명 | 0.016ms | 0.27ms | 약 21.6KB/s | 719 |

- 회귀: NetSessionQA(서버 흐름 포함), WorldSaveQA 68개, TitleFlowQA 108개, RemoteAvatarQA 18개 모두 통과

**설계와 달라진 점**
- 주인에게는 자기 엔티티의 Spawn·State·Despawn을 되돌려 보내지 않음
- 이벤트 대상 1(맵 전원)은 보낸 사람도 받음
- Claim은 요청자가 엔티티와 같은 맵에 있어야 함
- S→C Spawn에도 컴포넌트 수(u8)를 넣음
- 새로 들어온 사람은 이미 있는 사람들의 Peer(들어옴)를 받음
- 명령줄 `-bind <주소>`를 추가함. 기본값은 모든 주소
- 추가 보호 장치: Hello 없이 10초가 지나면 끊음, TCP 동시 소켓 256개, 접속당 송신 큐 8192개
- QA용 컴포넌트는 `Editor/Net/SyncQaRuntime`(런타임 어셈블리, `UNITY_EDITOR` 조건)에 둠. 빌드에는 들어가지 않음
- 서버가 직접 엔티티를 만드는 API는 아직 없음. 서버 소유 엔티티는 지금은 `PersistOnLeave`로만 생김(4단계에서 추가)

**다음 단계로 넘기는 한계**
- 4단계: 초당 300프레임 제한이 있어서, 움직이는 엔티티를 많이 가진 클라이언트는 끊길 수 있음 → 보낼 양을 묶어야 함
- 4단계: 이름(Hello·Peer)을 서버가 검사하지 않음 → 이름표를 옮길 때 길이와 `<>`를 거름
- 4단계: 장면을 내릴 때 남아야 하는 내 엔티티(설치한 차)의 처리
- 4단계: 이벤트 대상 0(주인)이 서버 소유 엔티티면 아무 데도 가지 않음
- 2단계: Hello의 계정 ID를 그대로 믿음 → EOS ID 토큰으로 검증
- 6단계: 서버 프로세스에 기존 자동 생성 객체(`NetSession` 등)가 함께 뜸. 앱 안 서버는 모든 주소에서 들어서, 처음 쓸 때 Windows 방화벽 창이 뜰 수 있음
- 49명이 한 맵에 있으면 송신이 약 5.6Mbps → 거리 기반 관심 범위와 갱신 빈도 낮추기가 필요함(6단계 부하 테스트에서 다시 잼)

### 12.2 2단계 시작: EOS 시험 (포털 등록 대기)

2단계는 EOS 시험부터 합니다(2026-10-13 결정). 4.2의 확인 못 한 항목을 실제로 돌려 보고, 결과에 따라 2단계 나머지(EosTransport, 서버 열기/참가하기 UI, 공개 목록, 서버 코드, ID 토큰 검증)의 세부 설계를 씁니다.

**사용자가 먼저 할 것**
- Epic 개발자 포털(dev.epicgames.com/portal)에서 제품 등록(무료)
- 아래 값 전달:
  - ProductId, SandboxId, DeploymentId
  - 클라이언트 정책이 "Peer2Peer" 또는 그에 준하는 ClientId/ClientSecret (Connect, P2P, Sessions 권한)
- 비밀값(ClientSecret)은 저장소에 커밋하지 않습니다. 로컬 설정 파일로 둡니다(.gitignore).

**시험 항목 (값을 받은 뒤 구현 에이전트가 수행, 판정 기준)**
1. PlayEveryWare 플러그인(최신 stable)을 Unity 6000.4.5f1에 넣었을 때 컴파일이 되고, 에디터 플레이 모드에 두 번 연속 들어가도 멈추지 않음
2. Device ID 로그인으로 PUID를 받음(에디터 + Windows 빌드)
3. 화면 없는 서버 프로세스(`-batchmode -nographics`)가 Device ID로 PUID를 받고 P2P 연결을 받아들임. 같은 PC의 다른 OS 사용자 없이 시험하려면 클라이언트는 프로필 슬롯이 아니라 **다른 PC나 다른 OS 사용자**여야 함(Device ID가 OS 사용자 단위라서). 안 되면 4.2-2의 대안(직접 IP)으로 감
4. 세션 생성(서버 이름·인원·비밀번호 여부 속성)과 검색이 되고, 세션 ID를 직접 정해 코드로 찾을 수 있음
5. Android 빌드에서 로그인·검색이 됨(Android 모듈 설치 필요. 실기기가 없으면 "못 함"으로 보고)

시험 결과에 따라 설계를 바꿔야 하면 사용자에게 먼저 확인받습니다.

**2026-10-13 진행 상황:** 사용자 계정이 Epic 개발자 포털 연령 확인에서 막혀 등록하지 못했습니다(27세인데도 "연령 미충족"으로 나옴). 등록이 될 때까지 EOS 시험은 미루고, 2단계 중 EOS 없이 할 수 있는 부분(12.3)을 먼저 합니다. → 같은 날 등록 완료. 값은 `CompanyGame/eos.local.json`(git 무시)에 있고, EOS 시험은 2a가 끝난 뒤 진행합니다(구현 에이전트는 한 번에 하나).

### 12.3 2a단계: 서버 열기/참가하기 (EOS 없이, 완료 2026-10-13)

**범위.** 타이틀에 새 흐름(서버 열기, 참가하기)을 만들고, 그 아래를 1단계 동기화 계층(TCP 직접 연결)으로 돌립니다.
- PC는 별도 서버 프로세스, 모바일과 에디터는 앱 안 서버를 씁니다.
- 비밀번호와 인원 제한을 넣고, 모바일은 주인이 나가면 서버를 끕니다.
- EOS(공개 목록, 서버 코드, 중계, ID 검증)는 2b로 미룹니다.

**전환 기간에 대한 결정.** 4단계 전까지 게임 내용(위치, 경제)은 옛 경로로 돕니다. 그래서 새 서버에 들어가도 서로 보이지 않고, 각자 자기 PC의 월드로 들어갑니다. 서버의 월드 파일은 3단계, 기능 이전은 4단계에서 합니다. 옛 방식 메뉴는 메인의 "옛 방식(개발용)" 버튼 아래로 옮겨 4단계까지 그대로 씁니다.

**새로 만들 파일**

| 파일 | 내용 |
| --- | --- |
| `Net/ServerSettings.cs` | 서버 설정(이름, 인원, 공개 여부, 비밀번호, 포트) 직렬화. `ProfileSlot.DataRoot/server/settings.json`에 읽고 씀. 인원 범위는 PC 2~50, 모바일 2~10(`Application.isMobilePlatform`)으로 맞춤. 이름 1~30자에 `<>` 금지, 비밀번호 0~32자, 포트 1024~65535(기본 7777) |
| `Net/LocalServerProcess.cs` | PC에서 내 서버 프로세스 실행·확인·종료. 자기 실행 파일을 `-batchmode -nographics -gameserver <port> -serversettings <설정 경로>`로 띄움. 서버가 쓴 `server/running.json`(pid, port, 시작 시각)으로 실행 중인지 확인하고, 끄기는 pid의 프로세스 이름이 같은지 확인한 뒤 종료. 에디터에서는 실행 파일이 없으니 앱 안 서버로 대신함(QA는 빌드한 exe 경로를 넘겨 실제 프로세스를 시험) |
| `Editor/Title/ServerOpenQA.cs` | 아래 검수 QA |

**바꿀 파일**

| 파일 | 바꿀 것 |
| --- | --- |
| `NetCore/SyncProtocol.cs` | 프로토콜 버전 2. Hello 끝에 `str 비밀번호`, Welcome 끝에 `str 서버 이름`. `RejectReason.Password = 5` |
| `NetCore/GameServer.cs` | 생성자에 서버 이름과 비밀번호. 비밀번호가 있으면 Hello의 비밀번호가 같아야 Welcome(다르면 Reject Password). 비교는 길이에 따라 시간이 달라지지 않게 |
| `Net/SyncHost.cs` | `-serversettings <path>`를 읽어 이름·인원·비밀번호·포트를 씀(명령줄 `-gameserver`·`-maxplayers`가 있으면 그것이 우선). 별도 프로세스 모드에서 `running.json`을 쓰고 종료할 때 지움. `StartInApp`도 ServerSettings를 받음 |
| `Net/SyncClient.cs` | `Connect(host, port, password)`. Welcome의 서버 이름을 `ServerName`에 둠 |
| `UI/Title/TitleScreen.cs` | 메인: 서버 열기 / 참가하기 / 설정 / 종료 / 옛 방식(개발용). 옛 방식 버튼은 지금의 방 만들기·참가하기·연결 방식 화면을 그대로 엶 |
| | 서버 열기 화면: 서버 이름, 인원(◀ N ▶), 공개·비공개(공개 목록은 EOS 뒤에 동작한다는 안내), 비밀번호, 포트(고급), "서버 시작". 내 서버가 이미 돌고 있으면 "내 서버 실행 중 (포트 N): 접속 / 끄기" |
| | 참가하기 화면: 주소(`host` 또는 `host:port`), 비밀번호, 최근 접속 주소 5개(PlayerPrefs, 슬롯별) |
| `UI/Title/TitleFlow.cs` | `OpenServer(settings)`(PC: 프로세스 시작 후 127.0.0.1로 접속 / 모바일·에디터: 앱 안 서버 + 접속), `JoinServer(address, password)`. 접속되면 지금의 오프라인 시작처럼 내 월드로 게임에 들어감. 오류 문구 추가: Version, Full, Password, Malformed, Rate, server_start_failed, bad_address |
| `UI/Title/PauseMenu.cs` | 새 서버 접속 중이면 "서버: 이름" 표시. 나가기 → SyncClient 끊고 타이틀로. 앱 안 서버의 주인(모바일)이면 서버도 끔. PC 서버 프로세스는 끄지 않음(게임을 꺼도 계속 돎) |
| `Editor/Title/TitleFlowQA.cs` | 옛 흐름 시험이 "옛 방식(개발용)"을 거쳐 들어가게 탐색만 고침(판정은 그대로) |

**지울 기존 코드: 없음** (방장 넘기기는 4단계, 2026-10-13 결정).

**하지 않는 것:** 트레이 아이콘·서버 창(서버는 화면 없이 돌고 타이틀에서 관리), 서버 월드 선택(3단계), 공개 목록·서버 코드(2b), 정상 종료 신호. 지금 서버는 저장할 것이 없어 프로세스를 바로 끄지만, 3단계에서 저장 후 종료로 바꿈.

**완료 기준**
1. 컴파일 오류 없음, 기존 QA 회귀 없음(SyncLayerQA 4종, NetSessionQA, WorldSaveQA, TitleFlowQA, RemoteAvatarQA)
2. SyncLayerQA.Run에 추가: 비밀번호가 틀리면 Reject(Password), 맞으면 Welcome, 비밀번호 없는 서버는 아무 값이나 통과, Welcome에 서버 이름이 옴, 버전 1 Hello는 Reject(Version)
3. ServerSettings: 범위 밖 값이 맞춰짐(PC 1 → 2, 99 → 50, 모바일 20 → 10), 저장 후 다시 읽어도 같음, 잘못된 JSON이면 기본값
4. ServerOpenQA(플레이 모드):
   - 서버 열기 화면에서 시작하면(에디터라 앱 안 서버) 접속되고 게임 맵에 들어감
   - 가짜 TCP 클라이언트가 틀린 비밀번호로는 거절되고 맞는 비밀번호로는 들어옴
   - 일시정지 메뉴에 서버 이름이 보임. 나가기 → 타이틀, 앱 안 서버가 꺼짐(모바일 경로를 강제해 확인)
5. ServerOpenQA(별도 프로세스):
   - 빌드한 exe를 `LocalServerProcess`로 띄움 → `running.json`이 생김 → 참가하기 화면에서 `127.0.0.1:port`와 비밀번호로 접속해 게임 맵에 들어감
   - 게임을 타이틀로 내려도 서버 프로세스는 남음
   - "끄기"로 프로세스가 끝나고 `running.json`이 지워짐
6. 화면 스크린샷(QA_SHOTS): 메인, 서버 열기, 서버 실행 중, 참가하기, 비밀번호 오류 화면

**총괄 검수 방법:** diff를 읽고(특히 프로세스 종료 시 pid 확인, 비밀번호 비교, 설정 파일 경로가 슬롯별인지), QA를 다시 돌리고, 스크린샷 다섯 장을 직접 봅니다.

#### 12.3 결과 (총괄 검수 완료)

**테스트 수치** (총괄이 다시 돌린 것)
- `SyncLayerQA.Run` 59개 (비밀번호 6개 포함)
- `ServerOpenQA.Run` 59개
- `ServerOpenQA.RunProcess` 38개 (UI에서 "서버 시작"을 눌러 실제 프로세스를 띄우고 → 접속 → 나가기 → 다시 접속 → 끄기까지)
- 스크린샷 5장 확인

**에이전트 보고 수치** (총괄이 다시 돌리지 않음)
- `TitleFlowQA` 113개, `NetSessionQA`, `WorldSaveQA` 68개, `RemoteAvatarQA` 18개, `npm test` 두 개 모두 통과

**설계와 달라진 점**
- 오류 코드 `server_unreachable`을 추가함. 직접 TCP 접속용 문구로, Cloudflare 주소 안내를 띄우지 않음
- 앱 안 서버는 에디터에서도 주인이 나가면 꺼짐
- 비밀번호 오류 화면에는 "뒤로"만 둠
- 새 화면 아래쪽 글줄에는 프로필 슬롯만 보이고, 옛 방식 화면에서는 지금처럼 보임

**남은 것**
- 게임을 완전히 종료한 뒤에도 PC 서버 프로세스가 살아 있는지는 QA로 확인할 수 없음. 빌드에서 직접 확인 필요
- 공개/비공개 설정은 저장만 되고 아직 쓰이지 않음(2b)
- 서버 끄기는 프로세스를 바로 종료하는 방식임. 저장 후 정상 종료는 3단계

### 12.4 2b-1단계: EOS 시험 (1~5 완료 2026-10-13, 6은 팀원 PC 대기)

**목적.** 4.1에서 확인하지 못한 것을 실제로 돌려 봅니다. 결과에 따라 2b-2(EosTransport, 공개 목록, 서버 코드, ID 토큰 검증) 설계를 정합니다. 시험이 목적이라 게임 흐름은 바꾸지 않습니다.

**새로 넣을 것**

| 대상 | 내용 |
| --- | --- |
| PlayEveryWare EOS 플러그인 | UPM으로 **버전을 고정**해 추가(v6.2.0 stable). git URL이나 릴리스 tgz 중 이 PC에서 되는 쪽(LFS 필요 여부 확인) |
| EOS 설정 | 플러그인 설정 파일은 `CompanyGame/eos.local.json`에서 만들고, ClientSecret이 든 생성 파일은 git이 무시하게 함. 저장소에는 비밀값이 없는 틀만 둠 |
| `Net/Eos/EosProbe.cs` | 명령줄 `-eostest` 시험 모드(게임 흐름과 분리, 인자가 없으면 아무것도 안 함). 결과를 로그와 `eos_test_log.txt`에 남김 |
| | `-eostest server`: Device ID 로그인 → PUID 출력 → P2P 소켓 `eostest`에서 연결을 받아 메시지를 되돌려 보냄 → 세션 생성(이름·인원·비밀번호 여부 속성, 세션 ID 직접 지정) |
| | `-eostest client <서버 PUID>`: Device ID 로그인 → 서버로 P2P 연결 → 핑 20번(왕복 시간) → 연결 종류(직접/중계) 기록 → 세션 검색(속성으로, 세션 ID로) |
| `Editor/Net/EosTestQA.cs` | 에디터에서 할 수 있는 시험과 팀원용 묶음 만들기 |
| 팀원용 묶음 | `CompanyGame/Builds/EosTest/`(git 무시): Windows 빌드, `eos_test.bat`(PUID를 물어보고 client로 실행), `eos_test_server.bat`, `README_팀원.txt`(한국어 3줄 안내). 압축 파일 하나로 만듦 |

**판정 (시험 항목)**

| # | 항목 | 이 PC에서 | 팀원 PC 필요 |
| --- | --- | --- | --- |
| 1 | 플러그인을 넣어도 컴파일 오류가 없고, 에디터 플레이 모드에 두 번 연속 들어가도 멈추지 않음. 기존 QA 회귀 없음 | O | |
| 2 | Device ID 로그인으로 PUID를 받음(에디터, Windows 빌드) | O | |
| 3 | 화면 없는 서버 프로세스(`-batchmode -nographics -eostest server`)가 PUID를 받음 | O | |
| 4 | 같은 OS 사용자로 두 프로세스가 동시에 로그인하면 PUID가 같은지, 둘 다 살아 있는지(4.2-1 확인) | O | |
| 5 | 세션 생성, 속성 검색, 세션 ID 검색 | O | |
| 6 | 서버 프로세스와 다른 PC의 클라이언트가 P2P로 연결되고, 핑이 오가며, 연결 종류가 기록됨 | | O |
| 7 | Android | Android 모듈이 없어서 못 함으로 보고 | |

**사용자가 할 것:** 6번만 해당합니다. 묶음 파일을 팀원에게 전달하고, 사용자 PC에서 `eos_test_server.bat`를 실행한 뒤 PUID를 팀원에게 알려 줍니다. 팀원이 `eos_test.bat`를 실행하면, 양쪽 `eos_test_log.txt`를 총괄에게 줍니다.

**지울 코드: 없음.** 기존 흐름은 고치지 않습니다.

**2026-10-13 결정:** 플러그인 v6.2.0의 에디터 전용 Steam 유틸리티 어셈블리가 Steamworks.NET 2025.163.0 이후에 없어진 상수(`STEAMGAMESEARCH_INTERFACE_VERSION`, `STEAMMUSICREMOTE_INTERFACE_VERSION`)를 참조해서 컴파일되지 않습니다. 그래서 Steamworks.NET을 2025.164.1에서 **2025.162.1로 내립니다**. 플러그인 수정판이 나오면 다시 올립니다.

**완료 기준:** 1~5 결과(수치·PUID 앞자리·오류 메시지)가 문서에 기록되고, 팀원용 묶음이 만들어짐. 6은 사용자가 팀원과 돌린 뒤 총괄이 로그를 읽고 기록함.

#### 12.4 결과 (총괄 검수, 2026-10-13)

| # | 결과 |
| --- | --- |
| 1 | 통과. 컴파일 오류 0. 플레이 모드 연속 2회 진입 정상(Device ID 로그인 약 9초). 기존 QA 전부 회귀 없음. SteamHostRoomQA 42개는 Steam 클라이언트 없이 도는 시험이라, Steam 실기 확인은 안 함 |
| 2 | 통과. 에디터와 Windows 빌드 모두 Device ID로 PUID를 받음 |
| 3 | 통과. **화면 없는 서버 프로세스(`-batchmode -nographics`)도 Device ID로 PUID를 받고 세션을 만듦** |
| 4 | 같은 OS 사용자의 두 프로세스는 **PUID가 같고**, 둘 다 로그인 상태를 유지함. 같은 PUID끼리 P2P는 성립하지 않음(핑 0/20, ConnectionFailed) → 4.2-1(내 PC 서버에는 로컬 127.0.0.1로 붙음)이 맞음 |
| 5 | 통과. 세션 생성, 세션 ID 직접 지정, 속성 검색, 세션 ID 검색 모두 Success. 속성(이름, 최대 인원, 비밀번호 여부, 빈자리)이 맞게 옴 |
| 6 | 대기. 팀원 PC 필요. 묶음은 `CompanyGame/Builds/EosTest.zip`(약 103MB, git 무시) |
| 7 | 못 함. Android 모듈이 없음 |

**2b-2에 반영할 것**
- 강제 종료된 서버의 세션이 2~5분 동안 검색에 남음 → 목록에서 오래된 세션을 거르는 장치가 필요함(예: 서버가 주기적으로 속성을 갱신하고, 클라이언트는 오래된 것을 숨김)
- 이 PC의 NAT 종류는 Moderate
- 플러그인 설정 파일은 `StreamingAssets/EOS/`에 있어야 함. Windows 네이티브가 Unity보다 먼저 읽기 때문이고, git은 이 폴더를 무시함. `EosTestQA.WriteConfig`가 `eos.local.json`에서 이 파일을 만듦
- Steamworks.NET 2025.162.1 고정(플러그인 수정판이 나오면 다시 올림). git URL로 설치하려면 git-lfs가 필요함
