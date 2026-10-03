# 서버 작업 공간

Node.js 24 + Fastify + SQLite(`node:sqlite`) 한 프로세스입니다. 별도 빌드 없이 TypeScript를 바로 실행합니다. 전부 무료입니다.

## 실행

```powershell
cd Server
npm install
copy .env.example .env   # JWT_SECRET을 긴 임의 문자열로 바꿉니다
npm start                # http://localhost:3000
npm test
```

DB는 `DB_PATH`(기본 `./data/game.db`) 파일 하나이며 Git에서 제외됩니다. 실제 인증값은 `.env`에만 두고 커밋하지 않습니다.

## 구조

- `src/app.ts`: HTTP API (회원가입, 로그인, 내 정보, 닉네임·성별)
- `src/db.ts`: SQLite 열기와 테이블 정의
- `src/sim/`: 주가·이벤트·소비자 시뮬레이션이 들어갈 자리. 입력과 출력만 함수로 주고받게 만들어, 무거워지면 워커나 별도 프로세스로 옮깁니다.
- 이벤트 정의(역병, 전쟁 등)는 DB가 아니라 JSON 설정 파일에 두고, DB에는 발생 기록만 남깁니다.

## 규칙

- 일반 API 서버는 Unity 그래픽 에셋을 빌드 의존성으로 사용하지 않습니다.
- 클라이언트·서버가 공유할 요청·응답 형식은 `Docs/Server_API.md`에 API 계약으로 문서화합니다.
- 표시용 이름과 저장·통신에 쓰는 ID는 구분하고, ID는 팀이 합의해서 고정합니다.
- 동시 접속이 수백 명을 넘으면 Postgres로 옮깁니다.
