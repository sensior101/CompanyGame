# 서버

## 지금 있는 것

`Server/`: Node.js 24 + Fastify + SQLite(`node:sqlite`) 한 프로세스입니다. 빌드 없이 TypeScript를 바로 실행합니다.

| 항목 | 내용 |
| --- | --- |
| 기능 | 회원가입, 로그인(JWT 7일), 내 정보, 닉네임·성별 설정, 플레이어 상태 저장(`/me/state`), 방·위치·월드 릴레이(WebSocket `/ws`) |
| API 계약 | [Server_API.md](Server_API.md) |
| 코드 | `src/app.ts` (API), `src/db.ts` (테이블), `src/rooms.ts` (방 로직, 전송과 무관), `src/protocol.ts` (WS 규칙: 크기·예산·검증, Node와 Cloudflare 공용), `src/ws.ts` (WebSocket), `src/index.ts` (시작) |
| Cloudflare | `worker/`: 같은 프로토콜을 Cloudflare Workers + Durable Objects로 옮긴 서버. 아래 "Cloudflare 배포" |
| DB | `DB_PATH` 파일 하나 (기본 `./data/game.db`, Git 제외). 테이블은 `players`, `player_state`(위치·`inventory_json`·`personal_json`) |
| 테스트 | `test/auth.test.ts`, `test/rooms.test.ts`(방 로직·방 코드), `test/ws.test.ts`(실제 WebSocket, `/me/state`), `test/limits.test.ts`(요청 제한), `test/jwt.test.ts`. `auth`·`ws`·`limits`·`jwt`는 `test/harness.ts`로 실제 HTTP/WebSocket을 쓰므로 Node 서버와 Cloudflare 워커에 똑같이 돌립니다. |

```powershell
cd Server
npm install
copy .env.example .env   # JWT_SECRET을 16자 이상 임의 문자열로 바꿉니다(예시 값 그대로면 서버가 시작을 거부합니다)
npm start                # http://localhost:3000
npm test
```

비밀번호는 scrypt로 해시하고, 실제 비밀값은 `.env`에만 둡니다. 가입·로그인·state·연결 수에는 메모리 내 요청 제한이 있습니다(자세히는 Server_API.md "요청 제한"). 터널 뒤에서 돌리면 `.env`에 `TRUST_PROXY=1`.

## Unity와의 연결

- 클라이언트: `CompanyGame/Assets/Scripts/Net/PlayerData.cs`(API 호출, 토큰은 `PlayerPrefs`), `Net/LocalAccount.cs`(이 PC·슬롯에 저장된 서버 주소와 계정), `Net/NetSession.cs`(방 연결). 화면은 `Scenes/Title.unity`의 시작 화면(`UI/Title/`)이고, 게임 시작은 이 씬이 빌드의 첫 씬입니다(`Scenes/daldongnaemap.unity`를 에디터에서 바로 실행하면 예전처럼 오프라인으로 시작).
- 계정은 플레이어가 아이디·비밀번호를 만들지 않습니다. 처음 방 만들기/참가하기를 누르면 **이름(닉네임)과 성별만 묻고**, 무작위 아이디·비밀번호로 서버 계정을 조용히 만들어 서버 주소와 함께 로컬(`PlayerPrefs`)에 저장합니다. 다음부터는 저장된 계정으로 자동 로그인하고, 서버에 저장된 개인 데이터(위치·인벤토리·은행·주식)를 불러옵니다.
- 시작 화면: **방 만들기**(신규 월드 / 기존 월드 이어하기, 서버가 꺼져 있으면 "오프라인으로 시작"), **참가하기**(방 코드 6자리 입력 + 참가, 보조로 "방 목록 보기"), **설정**(이름 변경, 프로필 슬롯, 계정 초기화, 접어 둔 **고급** 안에 서버 주소·연결 테스트). 신규 월드는 기존 `world.json`을 지우지 않고 `world.backup-날짜.json`으로 옮겨 둡니다.
- **방 코드**: 방 id(`rid`)가 6자리 코드(영문 대문자·숫자, 0 O 1 I 제외)입니다. 방 만들기가 끝나 게임 맵에 들어가면 화면 위에 코드 카드(큰 글씨 + "코드 복사" + "닫기")가 뜨고, ESC 메뉴에도 코드와 복사 버튼이 있습니다. 참가하기에서는 코드를 입력하거나 붙여넣으면 소문자·공백·하이픈을 알아서 정리합니다(`abc-def` → `ABCDEF`). 없는 코드는 "코드의 방을 찾을 수 없습니다", 가득 찬 방은 "방이 가득 찼습니다". 코드 처리: `Net/RoomCode.cs`, 화면: `UI/Title/TitleScreen.cs`(`ShowJoin`), `UI/Title/RoomCodeCard.cs`.
- **기본 서버 주소**: 플레이어는 서버 주소를 입력하지 않습니다. 주소는 `CompanyGame/Assets/Scripts/Net/ServerConfig.cs`의 `PublicUrl` 한 곳이며(서버를 배포한 뒤 `https://companygame-server.REPLACE.workers.dev`를 실제 주소로 바꾸고 다시 빌드), 우선순위는 실행 인자 `-server <주소>` > 설정 > 고급에 직접 넣은 주소(프로필 슬롯별) > `PublicUrl`입니다. 로컬 테스트 서버는 설정 > 고급 > "로컬 테스트 서버" 버튼으로 `http://localhost:3000`을 넣거나 `-server http://localhost:3000`으로 실행합니다.
- 게임 중 ESC: 일시정지 메뉴(계속하기, 방장 넘기기, 방 나가기=타이틀로, 게임 종료). 방에 다른 사람이 있는 방장은 먼저 방장 넘기기를 해야 나갈 수 있습니다.
- 서버에 저장하는 것은 플레이어 개인 상태(위치, 인벤토리, 개인 경제 `personal_json`)뿐입니다. 공용 월드(시계, 주가, 회사 등)는 방장 PC가 정본이고, 서버는 방별 최신 스냅샷 1개를 **메모리에만** 들고 있다가 새 방장에게 넘깁니다(서버 재시작 시 사라짐).
- 서버는 클라이언트가 보낸 인벤토리·스냅샷을 검증하지 않습니다(테스트 서버).

## 멀티 테스트 실행

```powershell
cd Server
npm start    # 기본 포트 3000, 같은 LAN이면 http://<내 IP>:3000
# 외부 친구와: 무료 터널, 예) cloudflared tunnel --url http://localhost:3000
```

접속은 로그인으로 받은 JWT가 필수입니다(`ws://<주소>/ws?token=...`, 터널은 `wss://`). 프로토콜은 [Server_API.md](Server_API.md)의 "실시간 프로토콜".

## Cloudflare 배포

항상 켜 둘 서버는 Cloudflare 무료 플랜의 Workers + Durable Objects(SQLite)에 올립니다. Node 서버는 로컬 테스트용으로 그대로 둡니다. 프로토콜은 같고 **방 번호(`rid`)만 6자리 방 코드**로 바뀌었습니다(Node 서버도 같은 코드를 만듭니다. [Server_API.md](Server_API.md) "방 코드").

### 구조 (`Server/worker/`)

| 파일 | 역할 |
| --- | --- |
| `src/index.ts` | 진입점 Worker. 경로 분기, 본문 검사(크기·JSON), JWT 발급/검증만 합니다. `JWT_SECRET`이 없거나 `change-me`로 시작하면 모든 요청을 `500 server_misconfigured`로 거부합니다. |
| `src/accounts.ts` | Durable Object `Accounts` 1개: 계정, `player_state`, 가입·로그인 요청 제한. 비밀번호 해시도 여기서 합니다. |
| `src/lobby.ts` | Durable Object `Lobby` 1개: 모든 방과 모든 WebSocket. Hibernation API 사용. |
| `src/jwt.ts`, `src/validate.ts` | HS256(WebCrypto), 요청 본문 규칙(Node의 Fastify 스키마와 같음) |
| `../src/rooms.ts`, `../src/protocol.ts`, `../src/limit.ts` | Node 서버와 **공용** 순수 모듈(방 규칙, 메시지 규칙, 요청 제한기). 복사본이 없으므로 규칙은 한 곳에서만 고칩니다. |

### 왜 이렇게 했나

- **저장소 = Durable Object의 SQLite (D1 아님).** 무료 한도가 같습니다(읽기 500만 행/일, 쓰기 10만 행/일, 저장 5GB). D1은 `d1 create`와 `database_id` 기입이 더 필요하지만, DO SQLite는 `wrangler deploy` 한 번에 마이그레이션까지 끝나서 배포 절차가 짧습니다. 계정과 방을 **DO 2개로 나눈** 이유: 로그인 해시(수십 ms)가 실시간 중계를 멈추지 않게 하려고.
- **쓰기 행 예산(하루 10만).** `PUT /me/state` 자동 저장(클라이언트 30초 주기)은 플레이어 1명당 시간당 120행, 방장 스냅샷(`state`, 약 10초 주기)은 방당 시간당 360행, `op`는 1건당 1행(링 버퍼 256칸에 덮어쓰기). 2명이 한 시간 놀면 약 600행이라 하루 10만 행은 160시간쯤에 해당합니다. 계정 가입은 3행(인덱스 포함). 방이 비면 방과 op 행을 지우는데 삭제도 쓰기로 셉니다(op가 많은 방이면 최대 257행).
- **요청 예산(하루 10만, 이쪽이 먼저 닳음).** 받은 WebSocket 메시지 20개가 DO 요청 1건입니다(보내는 메시지는 무료). 클라이언트가 `pos`를 초당 10개, 방장이 `world` 시계를 초당 5개 보내므로 2명 방은 약 25메시지/초 = 시간당 4,500요청, 4명 방은 약 8,100요청 → 하루 **2명 방 약 22시간, 4명 방 약 12시간**분. 넘으면 그날은 DO 요청이 실패하고 UTC 0시에 풀립니다. 아이디 가입/로그인/저장 HTTP 호출도 Worker 1건 + DO 1건씩 씁니다.
- **지속 시간 예산(하루 13,000 GB-s).** 아무도 말하지 않으면 DO가 메모리에서 내려가고 시간이 청구되지 않습니다. 방이 계속 활동 중이면 128MB 기준 시간당 약 460GB-s(하루 약 28시간분).
- **비밀번호 해시 = Node와 같은 scrypt(N=16384, r=8, p=1, 64바이트, `salt:hash` 16진수).** Workers 무료 플랜은 일반 Worker의 요청당 CPU가 10ms라서 scrypt(Node 기준 약 21ms)는 거기서 돌릴 수 없고, 같은 보안 수준의 PBKDF2(WebCrypto)도 CPU 사용량이 비슷하게 큽니다. **Durable Object는 무료 플랜에서도 요청/메시지당 CPU 30초**(Cloudflare 문서 "Durable Objects limits")라서 `Accounts` 안에서 `node:crypto`의 `scrypt`(`nodejs_compat`)를 그대로 씁니다. 보안 수준이 Node 서버와 같고 해시 형식도 같아 계정 이전이 가능합니다. 로컬 측정: 로그인 1회 약 28ms. **만약 실제 배포에서 CPU 한도 오류가 나면** `src/accounts.ts`의 `scryptAsync`에 `{ N: 4096 }` 옵션을 주면 약 4배 빨라집니다(이미 만들어진 계정 해시와 호환되지 않으니 그 경우 테스트 계정을 새로 만들 것).
- **JWT = HS256, WebCrypto.** 비밀값은 Worker secret `JWT_SECRET`(코드·`wrangler.toml`에 넣지 않음). 토큰 형식(`{id, iat, exp}`, 7일)이 Node의 `@fastify/jwt`와 같아서 같은 비밀값이면 서로의 토큰을 검증합니다(`test/jwt.test.ts`).
- **하이버네이션과 저장.** 소켓마다 `serializeAttachment`로 `{계정 id, 닉네임, IP, 방 코드, 입장 순서}`를 보관하고, 방은 SQLite에 보관합니다(이름·방장·정원·최신 스냅샷 1행 + 최근 op 256개 링). DO가 깨어나면 소켓 attachment와 SQLite로 방을 다시 만듭니다(방장 이관의 "먼저 들어온 사람" 순서도 attachment의 입장 순서로 복원). `test`가 13초 쉬었다 다시 쓰는 시나리오를 확인합니다(`worker/hibernate.test.ts`). 메모리에만 있어 **깨어나면 초기화되는 것**: 연결별 속도 예산(꽉 찬 상태로 시작), 마지막 `pos`(접속 종료 시 저장용, 자동 저장 PUT이 30초마다 위치를 따로 저장하므로 영향 작음), 가입·로그인 요청 제한 카운터(분당 창이라 10초 이상 쉬었다 오는 공격은 한도 아래). DO가 **재시작**(배포, 장애)되면 모든 연결이 끊기므로 방도 사라집니다(Node 서버를 껐다 켜는 것과 같음).
- **IP 제한**은 `CF-Connecting-IP`로 판단합니다(Cloudflare가 항상 덮어써서 위조 불가). Node의 `TRUST_PROXY`는 필요 없습니다.
- 클라이언트가 문자열 `ping`을 보내면 서버가 `pong`으로 자동 응답하며 DO를 깨우지 않고 요금도 없습니다(선택 사항).

### 로컬에서 돌려 보기 / 테스트

```powershell
cd Server\worker
npm install
npm run dev     # http://localhost:8787 (wrangler dev --local, 개발용 비밀값. 데이터는 .wrangler/ 에 저장)
npm run check   # 타입 검사(tsc)
npm test        # Server/test 의 같은 시나리오를 wrangler dev --local 위에서 실행(파일마다 서버를 따로 띄움, 약 1분)
```

`npm test`는 `auth`, `ws`, `limits`, `jwt` 시나리오와 `worker/hibernate.test.ts`(13초 유휴 뒤 방 복구)를 돌립니다. Node 서버는 `cd Server; npm test`(순수 방 로직 `rooms.test.ts` 포함)입니다. 로컬 `wrangler dev`에서는 서버가 먼저 끊는 소켓(4401, 4409, 4429)의 TCP 정리가 10초쯤 늦어 Node 테스트 클라이언트의 close 이벤트가 늦게 옵니다(close 프레임과 코드는 즉시 도착. 테스트가 그 10초를 기다림).

### 배포 순서 (팀장이 사용자 Cloudflare 계정으로 실행)

```powershell
cd D:\01_FIX\_Proj\CompanyGame\Server\worker
npm install
npx wrangler whoami            # 로그인 확인. 안 되어 있으면: npx wrangler login
npx wrangler deploy            # Worker + Durable Object 2개(마이그레이션 v1) 생성. 출력에 주소가 나옴
# 긴 임의 비밀값을 만들어 Worker secret으로 등록(화면에 출력하지 않고 바로 전달)
node -e "console.log(require('crypto').randomBytes(48).toString('base64url'))" | npx wrangler secret put JWT_SECRET
curl https://companygame-server.<계정-서브도메인>.workers.dev/      # {"ok":true,"service":"companygame-server"}
```

- 접속 주소: `https://companygame-server.<계정-서브도메인>.workers.dev` (처음 `workers.dev` 서브도메인을 만든 계정이면 배포 중 이름을 묻습니다). WebSocket은 같은 주소의 `wss://.../ws?token=...`(클라이언트가 http/https 주소를 받으면 자동으로 바꿉니다).
- 비밀값을 등록하기 전에는 모든 요청이 `500 {"error":"server_misconfigured"}`입니다(의도된 동작). 비밀값을 바꾸면 기존 로그인 토큰이 모두 무효가 됩니다.
- 로그 보기: `npx wrangler tail`. 배포 되돌리기: `npx wrangler rollback`. 서버와 모든 데이터 삭제: `npx wrangler delete`.
- 코드만 고쳐 다시 올릴 때는 `npx wrangler deploy`만 하면 됩니다(데이터 유지, 연결 중이던 방은 끊김).
- 무료 한도 현황은 Cloudflare 대시보드 > Workers & Pages > companygame-server > Metrics, 그리고 Durable Objects 사용량 탭.

### 한도 (같은 계정의 다른 사이트를 지키는 일일 예산)

무료 계정의 Pages 프로젝트 `companygame-admin`(Pages Functions)은 **같은 Workers 무료 한도(하루 요청 10만 건)**를 씁니다(Cloudflare 문서: "All Pages Functions are billed as Workers"). Durable Object 요청(하루 10만 건)은 이 서버만 씁니다. 그래서 서버 스스로 UTC 하루 예산을 두고, 넘으면 일을 거절합니다. `wrangler.toml [vars]`에서 바꿉니다.

| 변수 | 기본값 | 근거 |
| --- | --- | --- |
| `WORKER_DAILY_BUDGET` | 20000 | Workers 무료 10만의 20%. 이 서버가 최대로 써도 admin 사이트 몫 80%가 남음. 75%는 REST(Accounts), 25%는 WS 접속(upgrade)과 `GET /rooms`(Lobby)에 배정 |
| `DO_DAILY_BUDGET` | 60000 | DO 무료 10만의 60%. 25%는 Accounts, 75%는 Lobby에 배정. 받은 WS 메시지는 20개 = 1건으로 셈 |

- 숫자는 2026-10 기준 문서값입니다: Workers Free "100,000 per day"(Pages Functions 포함), Durable Objects Free 요청 100,000/일, 지속 시간 13,000 GB-s/일, SQLite 쓰기 100,000행/일.
- 세는 곳은 요청이 어차피 도착하는 DO입니다(추가 호출 없음). REST는 Accounts, WS 접속·`/rooms`는 Lobby. Lobby→Accounts 호출(`playerName`, `savePos`)은 Accounts의 DO 예산에만 들어갑니다. 카운터는 메모리에 두고 10건(Lobby의 메시지는 500건)마다 / 연결이 끝날 때 SQLite 한 행에 저장하므로 쓰기 행 부담은 시간당 수십 행입니다. 재시작 시 최대 그 건수만큼 덜 셉니다(보호용이지 청구용이 아님).
- **넘으면**: HTTP는 `503 {"error":"daily_budget"}` + `Retry-After`(UTC 0시까지 남은 초). 새 WS 접속은 닫기 코드 **4503**. 이미 접속한 세션은 DO 예산의 125%(하드 스톱)까지 계속되고, 그 뒤 다음 메시지에서 4503으로 끊습니다. 한 서버의 Lobby 몫이 4503을 낼 때 Accounts 몫은 따로 남아 있을 수 있습니다(몫별 판정).
- 거절한 요청도 Cloudflare 요청으로 셉니다. 그래서 **클라이언트가 멈춰야** 합니다(NOTES의 Unity 요청 참고).
- Worker 단계에서 이미 거절하는 요청(JSON 오류 400, 잘못된 토큰 401, 404)은 DO에 닿지 않아 세지 않습니다. 이 서버 도메인으로 대량 공격이 오면 예산이 못 막으므로 Cloudflare 대시보드의 WAF 속도 제한을 쓰세요.
- `GET /health`(와 `GET /`)는 DO를 건드리지 않고 항상 200입니다. 감시 용도로 자주 부르지 마세요(Worker 요청으로는 셉니다).
- 하루 사용량으로 환산: 예산 6만 DO 요청 중 Lobby 4.5만 = 메시지 90만 개. 2명 방(약 25메시지/초) 약 10시간, 4명 방 약 5.5시간분입니다. 모자라면 `DO_DAILY_BUDGET`을 올리되 100000을 넘기지 마세요.

### Node 서버와 다른 점

- `rid`는 6자리 방 코드(양쪽 서버 공통).
- 입력 검증 실패(400)의 본문이 `{ "error": "bad_request" }` 입니다(Fastify의 검증 오류 본문이 아님. 클라이언트는 상태 코드만 봅니다). 숫자 필드를 문자열로 보내면 Node는 숫자로 바꿔 받지만 워커는 400입니다.
- 모든 JSON 본문은 64KB 이하(초과 `413`).
- 512KB를 넘는 WebSocket 프레임은 코드 1009로 닫습니다(Node는 1006/1009).
- 이 서버가 지킨 규칙은 그대로입니다: 크기 한도는 바이트 기준(state 512KB, 그 외 8KB), 연결당 `op` 초당 10개와 `state` 버짓, 모든 메시지 초당 60개 초과 시 1008, IP당 동시 연결 20개(4429), 가입/로그인 제한(429), 4401/4409, 방장만 `state`/`world`/`transfer`, 비방장의 `msg`는 현재 방장에게만, `op` 순번과 최근 256개, 방장 이관, 정원, 방 50개 상한.

## 2인 테스트 (한 PC에서 게임 2개)

같은 계정으로 두 번 로그인하면 먼저 접속한 쪽이 끊기므로(4409), 창마다 **다른 프로필 슬롯**(= 별도 계정과 별도 저장 폴더)을 씁니다.

1. 서버를 켭니다: `Server\start-test.cmd` 더블클릭. 개발용 비밀값과 로컬 DB(`Server\data\test.db`)로 `http://localhost:3000`에서 시작합니다. DB 파일을 지우면 모든 계정이 초기화됩니다. 인터넷에 열려면 이 스크립트 대신 `.env`에 진짜 비밀값을 넣고 `npm start`.
2. 창 1: 빌드 실행 파일을 `-profile 1`로(또는 에디터의 Play. 설정의 프로필 슬롯 1) 실행합니다.
3. 창 2: 빌드를 `-profile 2`로 한 번 더 실행합니다(`CompanyGame.exe -profile 2`). 에디터는 한 프로젝트에 하나만 열리므로, 창 하나는 빌드, 하나는 에디터로 하거나, 에디터 쪽 설정에서 슬롯을 2로 바꿉니다. 빌드끼리는 `-profile`로만 구분됩니다.
4. 창 1: **방 만들기** > 신규 월드(또는 기존) > 처음이면 이름을 입력 > 게임이 열립니다.
5. 창 2: **참가하기** > 처음이면 다른 이름을 입력 > 창 1에 뜬 **방 코드**를 입력(또는 붙여넣기) > **참가**. (코드 대신 "방 목록 보기"에서 고를 수도 있습니다.)
6. 확인: 서로 보이는지, 시계·주가가 같은지, 창 2에서 땅을 사면 창 1에도 반영되는지. 창 1 ESC > 방장 넘기기로 월드를 창 2로 옮길 수 있습니다.
7. 다른 PC와 할 때는 설정 > **고급**에서 서버 주소를 `http://<서버 PC의 IP>:3000`으로 바꾸고 **연결 테스트**를 누릅니다(또는 `-server http://<서버 PC의 IP>:3000`으로 실행).

슬롯별로 나뉘는 것: 서버 계정·토큰·서버 주소(`PlayerPrefs`), `world/world.json`, `player/<계정 id>.json`(슬롯 2~4는 `profile2/`~`profile4/` 폴더). 슬롯과 무관하게 공유되는 것: 책·도서관(`books.json`, `library.json`)과 게임 시계 `PlayerPrefs` 한 값(방 안에서는 방장 시계를 따르므로 영향 없음).

## Steam 개발 모드 테스트

서버 없이, Valve의 테스트 AppID **480 (Spacewar)** 로 Steam 로비를 방으로 쓰는 개발용 연결 방식입니다. 무료이고 팀원에게 빌드를 zip으로 직접 주면 됩니다.

**방식**
- 방 = Steam 로비. 방장 PC가 서버 역할(op 순서 번호, 방장 전용 규칙, 입장 시 스냅샷, 최근 op 256개)을 하고, 통신은 Steam P2P(SteamNetworkingMessages, 릴레이 경유)입니다. 서버 `rooms.ts`와 같은 규칙을 `Net/HostRoom.cs`로 옮겼고 메시지 JSON은 같습니다. 게임 코드(NetSession 위)는 모드와 무관합니다.
- 방 코드 6자(서버와 같은 문자 집합)는 로비 데이터에 들어 있고, 참가 시 `game=companygame` + 코드로 찾습니다. 480은 전 세계 개발자가 같이 쓰므로 이 `game` 키가 꼭 필요합니다. 게임 버전이 다르면 `version_mismatch`로 거절됩니다.
- 로그인은 Steam 계정으로 자동입니다. 이름 가입이 없고 닉네임은 Steam 프로필 이름, 계정 ID는 SteamID64입니다.
- 방장 넘기기는 로비 소유자 변경(`SetLobbyOwner`)과 기존 이관 절차(state → transfer → 확인)를 같이 씁니다. 방장 PC가 사라지면 Steam이 정한 새 로비 소유자가 방장이 되고(서버의 방장 승계와 같은 `host` 알림), 모든 참가자가 가지고 있던 방 상태(스냅샷, op 기록, 멤버)로 방을 이어갑니다. 사라지는 순간에 방장에게 가던 op는 유실될 수 있습니다.
- 개인 데이터(위치, 인벤토리, 은행 잔액+주식)는 서버 DB가 없어 `persistentDataPath/steam/<SteamID>/state.json`에 로컬 저장합니다. 은행 계좌와 지갑 현금은 서버 모드와 같이 따로 저장됩니다(합치지 않음). 나중에 자체 AppID를 받으면 이 파일을 Steam Cloud로 옮깁니다.

**테스트 순서**
1. 빌드: 평소처럼 Windows 빌드. 빌드 후처리가 `steam_appid.txt`(내용 `480`)를 exe 옆에 자동 복사합니다. 에디터 실행용 사본은 프로젝트 루트(`CompanyGame/steam_appid.txt`)에 있습니다.
2. zip: 빌드 폴더 전체(`steam_appid.txt`, `steam_api64.dll` 포함)를 압축해 전달합니다. 받은 사람은 압축을 풀고 `CompanyGame.exe`를 실행합니다.
3. 두 사람 모두 **Steam을 켜고 로그인**한 상태로 게임을 켭니다. Steam이 꺼져 있으면 "Steam에 연결할 수 없습니다…" 안내가 나오고 Cloudflare 모드와 오프라인은 그대로 됩니다.
4. 시작 화면 아래 "연결 방식" 버튼(또는 설정)에서 **Steam (개발용)** 을 고릅니다. 한 번 고르면 기억됩니다.
5. 한 사람이 "방 만들기"로 방을 만들면 방 코드 카드가 뜹니다. 다른 사람은 "참가하기"에서 코드를 입력합니다.
6. 또는 방 안에서 ESC 메뉴의 "친구 초대"(Steam 오버레이)로 친구를 초대하면, 친구가 Steam 알림에서 수락하는 것만으로 시작 화면에서도 바로 입장합니다. 게임이 꺼져 있을 때 초대를 받으면 `+connect_lobby` 인자로 실행되어 같은 방식으로 들어갑니다.
7. 한 PC에서 Steam 클라이언트 2개는 못 켜므로 이 모드의 2인 테스트는 PC 2대가 필요합니다. (한 PC 2개 창 테스트는 위 "2인 테스트"의 Cloudflare/Node 모드로 합니다.)

**한계**
- Steam에서 게임이 "Spacewar"로 보입니다(AppID 480 공용).
- 자체 AppID는 Steamworks 파트너 가입비 100달러(앱당)가 필요합니다. 비공개 Steam 배포(친구에게만 키/베타 배포)도 자체 AppID가 있어야 합니다. 자체 AppID를 받은 뒤 Steam Playtest는 무료입니다.
- 480은 서버에서 인증 티켓을 검증할 수 없어서 계정을 믿고 쓰는 개발용입니다. 정식 배포 전에 SteamID 검증, Steam Cloud 저장, 속도 제한(서버 모드에 있는 op/pos 제한은 이 모드에 아직 없음)을 넣어야 합니다.
- 로비 검색은 전 세계 Spacewar 로비 중에서 고르므로, 방 목록 화면에는 우리 게임(`game=companygame`)만 나오지만 막 만든 방은 코드 검색에 1~3초 늦게 나타날 수 있습니다(자동으로 몇 번 다시 찾습니다).

**Cloudflare 서버 일일 한도**: 서버가 하루 예산을 다 쓰면 HTTP 503 `daily_budget`(Retry-After) 또는 WebSocket 닫힘 코드 4503을 돌려줍니다. 게임은 그때부터 다음 UTC 날(한국 시간 오전 9시)까지 로그인·자동 저장·재접속 요청을 하지 않고, "오늘 서버 사용 한도에 도달했어요…" 안내와 함께 오프라인/Steam 모드를 권합니다. 자동 저장 간격은 120초입니다(방 나갈 때, 맵 이동 시작 때, 종료 때도 저장).

## 멀티 방향과 서버 역할

기본 방식이 방장 호스트로 정해졌으므로 게임 상태(시간, 경제, 이벤트)는 방장의 게임이 계산합니다. 이 Node 서버가 게임 상태를 계산하지 않습니다.

남는 서버 역할 후보:

| 역할 | 필요 시점 |
| --- | --- |
| 계정·프로필 (지금 있음) | 온라인 기능을 쓸 때 |
| 방 목록·랜덤 매칭 | 온라인 랜덤 매칭을 할 때. Unity Relay/Lobby 같은 외부 서비스로 대신할 수도 있음 |
| 서버(월드) 저장 | 방장이 나가도 월드를 이어가야 할 때 |

## DB 설계 문서와의 관계

[Database/](Database/README.md)의 설계 초안(2026-09-23)은 "온라인 상태는 백엔드와 MySQL이 소유한다"는 전용 서버 방식을 전제로 썼습니다. 방장 호스트 방식에서는 다음처럼 바뀝니다.

- 싱글·방장의 세이브: 방장 PC의 로컬 저장(SQLite 또는 파일)이 정본입니다. `SQLITE.md`가 그대로 쓰입니다.
- `MYSQL.md`의 서버 전용 테이블 중 계정·세션(`accounts`, `auth_sessions`)만 지금 서버 역할과 겹칩니다. 월드 상태 테이블은 전용 서버를 도입할 때만 필요합니다.
- 테이블 이름·필드(`TABLES.md`)는 로컬 저장에도 그대로 쓸 수 있습니다. 문서의 코드 근거 중 `PropertyManager`는 지금 `BankManager`입니다.

## 규칙

- 서버는 Unity 아트 에셋을 빌드 의존성으로 쓰지 않습니다.
- 클라이언트·서버가 함께 쓰는 요청·응답 형식은 [Server_API.md](Server_API.md)에 먼저 적고 구현합니다.
- 표시용 이름과 저장·통신용 ID를 구분하고, ID는 팀이 합의해 고정합니다.
- 동시 접속이 수백 명을 넘으면 SQLite에서 Postgres로 옮깁니다.
