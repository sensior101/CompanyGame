# 서버

이 저장소에는 서버가 두 종류 있습니다.

| 서버 | 하는 일 | 위치 |
| --- | --- | --- |
| **게임 서버**(동기화 계층) | 실시간 플레이 전부: 아바타, 월드(시계·주가·회사·소유권·SNS·신고), 개인 경제·인벤토리 장부, 드롭·탈것, 도서관. 월드와 캐릭터를 `world.dat`에 저장 | Unity 안. 서버 열기/혼자 하기(앱 안 서버), 서버 프로세스(빌드를 `-gameserver`로 실행), 전용 서버 묶음(아래 "전용 서버"). [Multiplayer_Redesign.md](Multiplayer_Redesign.md) 12장 |
| **계정 서버**(이 문서) | 회원가입, 로그인(JWT), 내 정보, 닉네임·성별. **2b-2부터 게임이 쓰지 않음** | `Server/`(Node, 로컬 테스트용), `Server/worker/`(Cloudflare Workers, 배포용) |

옛 방 서버(WebSocket `/ws`, `/rooms`, 방 코드, `/me/state` 월드별 저장, Cloudflare `Lobby` Durable Object)는 4e(2026-10-19)에서 지웠습니다. 2b-2(2026-10-19)에서 계정이 EOS PUID로 바뀌어 게임은 계정 서버를 쓰지 않습니다. `Server/` 코드와 배포된 Worker는 그대로 둡니다(결정 2026-10-19).

## 계정 서버 (`Server/`)

Node.js 24 + Fastify + SQLite(`node:sqlite`) 한 프로세스입니다. 빌드 없이 TypeScript를 바로 실행합니다.

| 항목 | 내용 |
| --- | --- |
| 기능 | 회원가입, 로그인(JWT 7일), 내 정보, 닉네임·성별 설정 |
| API 계약 | [Server_API.md](Server_API.md) |
| 코드 | `src/app.ts`(API), `src/db.ts`(테이블), `src/limit.ts`(요청 제한기, Node와 Worker 공용), `src/budget.ts`(Worker의 일일 예산, 공용), `src/index.ts`(시작) |
| DB | `DB_PATH` 파일 하나(기본 `./data/game.db`, Git 제외). 테이블은 `players`. 예전 DB에 남은 `player_state`·`character_state` 표는 그대로 두고 쓰지 않습니다 |
| 테스트 | `test/auth.test.ts`, `test/limits.test.ts`(요청 제한), `test/jwt.test.ts`, `test/budget.test.ts`. `test/harness.ts`로 실제 HTTP를 쓰므로 Node 서버와 Cloudflare 워커에 똑같이 돌립니다 |

```powershell
cd Server
npm install
copy .env.example .env   # JWT_SECRET을 16자 이상 임의 문자열로 바꿉니다(예시 값 그대로면 서버가 시작을 거부합니다)
npm start                # http://localhost:3000
npm test
```

비밀번호는 scrypt로 해시하고, 실제 비밀값은 `.env`에만 둡니다. 가입·로그인에는 메모리 내 요청 제한이 있습니다(Server_API.md "요청 제한"). 터널 뒤에서 돌리면 `.env`에 `TRUST_PROXY=1`. `start-test.cmd`는 개발용 비밀값과 `data\test.db`로 바로 켭니다.

## Unity와의 연결

- 없음. 계정 서버 클라이언트(`PlayerData`, `LocalAccount`, `ServerConfig`, `ServerBudget`)와 설정의 "계정 초기화"·서버 주소는 2b-2에서 지웠습니다.
- 계정은 EOS PUID입니다(`Net/Eos/EosAccount.cs`). 게임 서버는 Hello의 계정을 EOS로 확인합니다(EOS P2P 상대 PUID, 또는 TCP의 EOS ID 토큰. 127.0.0.1에서 온 서버 자신의 PUID만 토큰 없이). EOS 없이 도는 서버(설정 파일 없음, `-noeos`)는 Hello의 계정을 믿습니다.

## 게임 서버 테스트 (한 PC에서 게임 2개)

같은 계정으로 두 번 접속하면 두 번째가 거절되므로(`AlreadyConnected`), 창마다 **다른 프로필 슬롯**을 씁니다. 2b-2부터 한 PC(OS 사용자)의 게임은 모두 같은 EOS 계정이라, 한 PC 두 창 시험은 **서버와 두 창 모두 `-noeos`**(또는 환경 변수 `COMPANYGAME_NOEOS=1`)로 실행합니다. 다른 PC와는 참가하기의 공개 목록이나 서버 코드(EOS P2P)로 들어갑니다.

1. 창 1: 빌드를 `-profile 1`로(또는 에디터 Play) 실행 → **서버 열기** → 이름·비밀번호 → 서버 시작 → 캐릭터를 만들면 게임에 들어갑니다.
2. 창 2: 빌드를 `-profile 2`로 실행 → **참가하기** → `127.0.0.1`(포트를 바꿨으면 `127.0.0.1:포트`)와 비밀번호 → 캐릭터를 만듭니다.
3. 다른 PC와 할 때는 참가하기의 공개 서버 목록이나 서버 코드(EOS P2P, 포트 열기 불필요)로 들어갑니다. 직접 주소(IP·포트, 기본 7777)는 "직접 주소 입력" 아래에 있고 방화벽·공유기 포트 열기가 필요합니다.
4. 서버 프로세스로 돌릴 때는 빌드를 `-gameserver [포트] -serversettings <settings.json>`으로 실행합니다(창 없음, 포트를 빼면 설정 파일의 포트, `-nightskip` 등은 [Multiplayer_Redesign.md](Multiplayer_Redesign.md) 12.3·12.9). 서버 컴퓨터에 둘 때는 아래 "전용 서버" 묶음을 씁니다.

슬롯별로 나뉘는 것: `PlayerPrefs`(계정 id·서버 설정), 오프라인 `world/world.json`, `player/<계정 id>.json`(슬롯 2~4는 `profile2/`~`profile4/` 폴더). 서버의 월드는 `worlds/<월드 id>/world.dat`입니다.

## 전용 서버 (6단계, [Multiplayer_Redesign.md](Multiplayer_Redesign.md) 12.15)

서버 컴퓨터에서 창 없이 도는 게임 서버입니다. Windows만 있습니다(리눅스는 모듈 설치 뒤).

| 항목 | 내용 |
| --- | --- |
| 만들기 | 에디터에서 `-executeMethod DedicatedServerQA.RunBundle`(`-quit`) → `CompanyGame/Builds/DedicatedServer/`와 `DedicatedServer.zip`. 일반 Windows 플레이어(타이틀 씬 하나)를 `-batchmode -nographics`로 돌립니다. Dedicated Server 빌드 대상은 쓰지 않습니다(모듈 없음) |
| 묶음 | `CompanyGameServer.exe`, `StartServer.bat`, `server/settings.json`, `README.txt`(한국어 사용 안내). 묶음 원본은 `Assets/Scripts/Editor/Net/DedicatedServerBundle~/` |
| 켜기·끄기 | `StartServer.bat` 더블클릭. 그 창이 서버 콘솔입니다(명령 입력, 주요 로그). 끌 때는 `stop`(또는 Ctrl+C): 저장하고 종료. 전체 로그는 `server/server.log` |
| 설정 파일 | `server/settings.json`(`ServerSettings` JSON): `name`, `maxPlayers`, `isPublic`, `password`, `port`, `world`(재시작해야 적용), `admins`, `bans`, `nightSkip`, `trafficCars`(최대 10, 저장하면 1초 안에 적용) |
| 관리자 | 설정의 `admins` 계정 + 서버 연 사람(PC "서버 열기"는 `-owner`로 넘김) + 콘솔. 명령: `help`, `players`, `kick`, `ban`, `unban`, `admin`/`unadmin`(콘솔·서버 연 사람만), `say`, `save`, `time`, `stop`. 게임 채팅에서는 `/kick 이름`처럼 앞에 `/` (`Player/ServerAdmin.cs`) |
| 목록 | EOS가 되면 스스로 공개 목록에 등록하고 서버 코드를 냅니다(2b-2 코드 그대로). 직접 주소는 TCP `port`(공유기 포트 포워딩 필요) |
| QA | `DedicatedServerQA`(`RunBundle`, `RunConsole`, `RunPlay`, `RunEos`, `RunLoad`), 러너 `_temp/qa6/regress.sh` |

## Cloudflare 배포 (계정 서버)

계정 서버의 배포용 사본이 `Server/worker/`(Cloudflare Workers + Durable Object SQLite)입니다. **4e 이후 아직 배포하지 않았습니다.** 배포된 Worker는 요청이 있을 때 바꿉니다.

### 구조 (`Server/worker/`)

| 파일 | 역할 |
| --- | --- |
| `src/index.ts` | 진입점 Worker. 경로 분기, 본문 검사(크기·JSON), JWT 발급/검증. `JWT_SECRET`이 없거나 `change-me`로 시작하면 모든 요청을 `500 server_misconfigured`로 거부 |
| `src/accounts.ts` | Durable Object `Accounts` 1개: 계정, 가입·로그인 요청 제한, 일일 예산. 비밀번호 해시도 여기서 함 |
| `src/budget.ts`, `../src/budget.ts` | 일일 예산 카운터(Worker 요청·DO 요청) |
| `src/jwt.ts`, `src/validate.ts` | HS256(WebCrypto), 요청 본문 규칙(Node의 Fastify 스키마와 같음) |
| `../src/limit.ts` | Node 서버와 공용 요청 제한기 |
| `wrangler.toml` | migration `v1`(Accounts, Lobby 생성), `v2`(`deleted_classes = ["Lobby"]`: 옛 방 DO 삭제) |

### 왜 이렇게 했나

- **저장소 = Durable Object의 SQLite(D1 아님).** 무료 한도가 같고, `wrangler deploy` 한 번에 마이그레이션까지 끝나 배포 절차가 짧습니다.
- **비밀번호 해시 = Node와 같은 scrypt(N=16384, r=8, p=1, 64바이트, `salt:hash` 16진수).** 일반 Worker는 무료 플랜 CPU가 요청당 10ms라 scrypt(약 21ms)를 못 돌리지만, Durable Object는 요청당 CPU 30초라 `Accounts` 안에서 `node:crypto`의 `scrypt`(`nodejs_compat`)를 그대로 씁니다. 해시 형식이 같아 계정 이전이 가능합니다. 배포에서 CPU 한도 오류가 나면 `src/accounts.ts`의 `scryptAsync`에 `{ N: 4096 }`을 줍니다(기존 해시와 호환 안 됨).
- **JWT = HS256, WebCrypto.** 비밀값은 Worker secret `JWT_SECRET`. 토큰 형식(`{id, iat, exp}`, 7일)이 Node의 `@fastify/jwt`와 같아 같은 비밀값이면 서로의 토큰을 검증합니다(`test/jwt.test.ts`).
- **IP 제한**은 `CF-Connecting-IP`로 판단합니다(Cloudflare가 덮어써서 위조 불가). `TRUST_PROXY`는 필요 없습니다.

### 로컬에서 돌려 보기 / 테스트

```powershell
cd Server\worker
npm install
npm run dev     # http://localhost:8787 (wrangler dev --local, 개발용 비밀값. 데이터는 .wrangler/ 에 저장)
npm run check   # 타입 검사(tsc)
npm test        # Server/test 의 같은 시나리오를 wrangler dev --local 위에서 실행(파일마다 서버를 따로 띄움)
```

### 배포 순서 (팀장이 사용자 Cloudflare 계정으로 실행)

```powershell
cd D:\01_FIX\_Proj\CompanyGame\Server\worker
npm install
npx wrangler whoami            # 로그인 확인. 안 되어 있으면: npx wrangler login
npx wrangler deploy            # Worker + Accounts. 이미 배포된 곳이면 migration v2가 Lobby와 그 데이터를 지웁니다
# 처음 배포라면 긴 임의 비밀값을 Worker secret으로 등록(화면에 출력하지 않고 바로 전달)
node -e "console.log(require('crypto').randomBytes(48).toString('base64url'))" | npx wrangler secret put JWT_SECRET
curl https://companygame-server.<계정-서브도메인>.workers.dev/      # {"ok":true,"service":"companygame-server"}
```

- 비밀값을 등록하기 전에는 모든 요청이 `500 {"error":"server_misconfigured"}`입니다. 비밀값을 바꾸면 기존 토큰이 모두 무효가 됩니다.
- 로그: `npx wrangler tail`. 되돌리기: `npx wrangler rollback`(v2 migration으로 지운 Lobby 데이터는 돌아오지 않음). 전부 삭제: `npx wrangler delete`.

### 한도 (같은 계정의 다른 사이트를 지키는 일일 예산)

무료 계정의 Pages 프로젝트 `companygame-admin`(Pages Functions)은 **같은 Workers 무료 한도(하루 요청 10만 건)**를 씁니다. 그래서 서버 스스로 UTC 하루 예산을 두고, 넘으면 거절합니다. `wrangler.toml [vars]`에서 바꿉니다.

| 변수 | 기본값 | 근거 |
| --- | --- | --- |
| `WORKER_DAILY_BUDGET` | 20000 | Workers 무료 10만의 20%. admin 사이트 몫 80%가 남음. 전부 계정 API(Accounts) 몫 |
| `DO_DAILY_BUDGET` | 60000 | DO 무료 10만의 60%. 전부 Accounts 몫 |

- 세는 곳은 요청이 어차피 도착하는 `Accounts` DO입니다(추가 호출 없음). 카운터는 메모리에 두고 몇 건마다 SQLite 한 행에 저장합니다(재시작 시 그만큼 덜 셈. 보호용이지 청구용이 아님).
- **넘으면** `503 {"error":"daily_budget"}` + `Retry-After`(UTC 0시까지 남은 초). (게임 쪽 `ServerBudget`은 2b-2에서 계정 서버 클라이언트와 함께 지웠습니다.)
- Worker 단계에서 거절하는 요청(JSON 오류 400, 잘못된 토큰 401, 404)은 DO에 닿지 않아 세지 않습니다. 대량 공격은 Cloudflare 대시보드의 WAF 속도 제한으로 막습니다.
- `GET /health`(와 `GET /`)는 DO를 건드리지 않고 항상 200입니다(Worker 요청으로는 셈).

### Node 서버와 다른 점

- 입력 검증 실패(400)의 본문이 `{ "error": "bad_request" }`입니다. 숫자 필드를 문자열로 보내면 Node는 숫자로 바꿔 받지만 워커는 400입니다.
- 모든 JSON 본문은 64KB 이하(초과 `413`).
- 일일 예산(503)은 워커에만 있습니다.

## DB 설계 문서와의 관계

[Database/](Database/README.md)의 설계 초안(2026-09-23)은 "온라인 상태는 백엔드와 MySQL이 소유한다"는 전용 서버 방식을 전제로 썼습니다. 지금은 게임 서버(Unity)가 월드와 캐릭터를 `world.dat`에 저장하고, 오프라인은 PC 파일(`world.json`, `player/*.json`)에 저장합니다. `MYSQL.md`의 계정 테이블(`accounts`)만 이 계정 서버와 겹칩니다. 문서의 `PropertyManager`는 지금 `BankManager`입니다.

## 규칙

- 서버는 Unity 아트 에셋을 빌드 의존성으로 쓰지 않습니다.
- 클라이언트·서버가 함께 쓰는 요청·응답 형식은 [Server_API.md](Server_API.md)에 먼저 적고 구현합니다.
- 표시용 이름과 저장·통신용 ID를 구분하고, ID는 팀이 합의해 고정합니다.
