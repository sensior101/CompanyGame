# 서버

## 지금 있는 것

`Server/`: Node.js 24 + Fastify + SQLite(`node:sqlite`) 한 프로세스입니다. 빌드 없이 TypeScript를 바로 실행합니다.

| 항목 | 내용 |
| --- | --- |
| 기능 | 회원가입, 로그인(JWT 7일), 내 정보, 닉네임·성별 설정 |
| API 계약 | [Server_API.md](Server_API.md) |
| 코드 | `src/app.ts` (API), `src/db.ts` (테이블), `src/index.ts` (시작) |
| DB | `DB_PATH` 파일 하나 (기본 `./data/game.db`, Git 제외). 테이블은 `players` 하나 |
| 테스트 | `test/auth.test.ts` (가입 → 로그인 → 프로필 흐름) |

```powershell
cd Server
npm install
copy .env.example .env   # JWT_SECRET을 16자 이상 임의 문자열로 바꿉니다
npm start                # http://localhost:3000
npm test
```

비밀번호는 scrypt로 해시하고, 실제 비밀값은 `.env`에만 둡니다.

## Unity와의 연결

- 클라이언트: `CompanyGame/Assets/Scripts/Net/PlayerData.cs`. `UnityWebRequest`로 위 API를 부르고 토큰을 `PlayerPrefs`에 저장합니다. 주소는 Inspector의 `baseUrl` (기본 `http://localhost:3000`).
- **아직 어떤 게임 코드도 `PlayerData`를 호출하지 않습니다.** 로그인 화면(튜토리얼 씬)과 함께 연결해야 합니다.
- 게임 진행 데이터(잔액, 인벤토리, 주식, 회사)는 서버에 저장하지 않습니다.

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
