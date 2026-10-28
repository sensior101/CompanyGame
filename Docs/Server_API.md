# 서버 API 계약

기본 주소는 개발 중 `http://localhost:3000`(Node 서버)입니다. Cloudflare 서버(`https://companygame-server.<계정-서브도메인>.workers.dev`)도 API가 같습니다([Server.md](Server.md) "Cloudflare 배포"). 본문은 JSON이고, 인증이 필요한 요청은 `Authorization: Bearer <token>` 헤더를 붙입니다. 토큰은 7일 동안 유효합니다.

| 메서드 | 경로 | 인증 | 요청 본문 | 성공 응답 |
| --- | --- | --- | --- | --- |
| POST | `/auth/register` | 없음 | `{ username, password }` | `201 { token }` |
| POST | `/auth/login` | 없음 | `{ username, password }` | `200 { token }` |
| GET | `/me` | 필요 | 없음 | `200 { id, username, nickname, gender }` |
| PUT | `/me/profile` | 필요 | `{ nickname, gender }` | `200 { nickname, gender }` |

## 입력 규칙

- `username`: 영소문자, 숫자, `_` 3~20자
- `password`: 8~64자
- `nickname`: 한글, 영문, 숫자, `_` 2~12자. 다른 플레이어와 중복 불가
- `gender`: `male` 또는 `female`. 튜토리얼에서 선택하기 전에는 `nickname`, `gender`가 `null`입니다.

## 오류

| 코드 | 본문 | 의미 |
| --- | --- | --- |
| 400 | Fastify 검증 오류 (Cloudflare 서버는 `{ error: "bad_request" }`) | 입력 규칙 위반. 클라이언트는 상태 코드만 보면 됩니다. |
| 401 | `{ error: "invalid_credentials" }` | 아이디 또는 비밀번호 불일치. 없는 아이디도 같은 응답입니다. |
| 401 | `{ error: "unauthorized" }` | 토큰이 없거나 만료됨 |
| 409 | `{ error: "username_taken" }` | 이미 있는 아이디 |
| 409 | `{ error: "nickname_taken" }` | 이미 있는 닉네임 |
| 429 | `{ error: "rate_limited" }` | 요청 제한 초과(아래) |
| 503 | `{ error: "daily_budget" }` + `Retry-After` | Cloudflare 서버만. 하루 예산을 다 씀(다음 UTC 자정까지, [Server.md](Server.md) "한도") |

## 요청 제한 (남용 방지)

- `POST /auth/register`: IP당 분당 10회, `POST /auth/login`: IP당 분당 30회 + 아이디당 분당 10회. 초과는 `429 { error: "rate_limited" }`. 닉네임은 서버 스키마(한글/영문/숫자/_ 2~12자)가 이미 `<` `>`를 막습니다.
- 서버가 터널/프록시 뒤에 있으면 모든 사용자의 IP가 같아 보이므로 `TRUST_PROXY=1`로 `X-Forwarded-For`를 쓰게 합니다(프록시 없이 켜면 IP 위조 가능).

## 이 서버가 하지 않는 것 (4e, 2026-10-19)

실시간 플레이(아바타·월드·경제·인벤토리)는 이 서버가 아니라 **동기화 계층 서버**(서버 열기/혼자 하기, 또는 서버 프로세스)가 맡습니다([Multiplayer_Redesign.md](Multiplayer_Redesign.md) 12장). 옛 방 기능인 WebSocket `/ws`(`create`·`join`·`pos`·`state`·`op`·`veh.*` 등), `GET /rooms`, 방 코드, 월드별 캐릭터 저장 `GET/PUT /me/state`는 지웠습니다. 남은 것은 계정(가입·로그인·프로필)뿐입니다. **2b-2(2026-10-19)부터 게임은 이 서버를 부르지 않습니다**(계정은 EOS PUID, 게임 쪽 클라이언트 코드는 지움). 코드와 배포된 Worker는 저장소에 그대로 둡니다.
