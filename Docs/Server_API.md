# 서버 API 계약

기본 주소는 개발 중 `http://localhost:3000`(Node 서버)입니다. 항상 켜 둔 Cloudflare 서버는 `https://companygame-server.<계정-서브도메인>.workers.dev`이고 API·프로토콜이 같습니다([Server.md](Server.md) "Cloudflare 배포"). 본문은 JSON이고, 인증이 필요한 요청은 `Authorization: Bearer <token>` 헤더를 붙입니다. 토큰은 7일 동안 유효합니다.

| 메서드 | 경로 | 인증 | 요청 본문 | 성공 응답 |
| --- | --- | --- | --- | --- |
| POST | `/auth/register` | 없음 | `{ username, password }` | `201 { token }` |
| POST | `/auth/login` | 없음 | `{ username, password }` | `200 { token }` |
| GET | `/me` | 필요 | 없음 | `200 { id, username, nickname, gender }` |
| PUT | `/me/profile` | 필요 | `{ nickname, gender }` | `200 { nickname, gender }` |
| GET | `/me/state` | 필요 | 없음 | `200 { map, x, y, z, ry, inventory_json, personal_json, updated_at }` (저장 전이면 `updated_at: 0`, 나머지는 기본값) |
| PUT | `/me/state` | 필요 | `{ map, x, y, z, ry, inventory_json?, personal_json? }` (본문 64KB 이하) | `200` + 저장된 상태 |
| GET | `/rooms` | 필요 | 없음 | `200 { rooms: [{ rid, room, host, hostName, count, max }] }` (`rid`는 방 코드) |
| WS | `/ws?token=<JWT>` | 쿼리 토큰 | 아래 "실시간 프로토콜" | |

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

## 플레이어 상태 (`/me/state`)

- `map` 200자 이하 문자열, `x y z ry` 절댓값 1e6 이하 숫자. 필수.
- `inventory_json`(지갑 현금 포함 인벤토리), `personal_json`(은행 잔액·보유 주식): 문자열이고 비어 있거나 올바른 JSON이어야 합니다. **생략하면 저장된 값을 유지**합니다(위치만 갱신 가능). JSON이 잘못되면 `400 { error: "invalid_json" }`, 본문이 64KB를 넘으면 `413`.
- 정의되지 않은 필드는 조용히 버려집니다(Fastify 기본 동작).
- 클라이언트가 보낸 내용을 그대로 저장합니다(서버는 내용을 검증하지 않는 테스트 서버).
- WS 연결이 끊기면 서버가 마지막 `pos`를 같은 표에 저장합니다(JSON 필드는 그대로).

## 요청 제한 (남용 방지)

- `POST /auth/register`: IP당 분당 10회, `POST /auth/login`: IP당 분당 30회 + 아이디당 분당 10회. 초과는 `429 { error: "rate_limited" }`. 닉네임은 서버 스키마(한글/영문/숫자/_ 2~12자)가 이미 `<` `>`를 막습니다.
- `state`는 별도 한도: 연결당 초당 1개(버스트 3), 지속 128KB/s(버스트 512KB). 초과분은 `err rate_limited`(연결은 유지).
- IP당 동시 WS 연결 20개, 계정당 연결 1개·방 1개. 전체 방 수 상한 50은 그대로이며 IP별 몫은 따로 두지 않았습니다(IP당 연결 20개 + 가입 분당 10회가 사실상 상한).
- 서버가 터널/프록시 뒤에 있으면 모든 사용자의 IP가 같아 보이므로 `TRUST_PROXY=1`로 `X-Forwarded-For`를 쓰게 합니다(프록시 없이 켜면 IP 위조 가능).

## 방 코드

`rid`는 **6자리 방 코드**입니다. 문자는 `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`(0, O, 1, I 제외, 32종)만 쓰고 서버가 만들어 `joined.rid`와 `rooms` 목록으로 알려 줍니다.
- `join`은 **대소문자를 구분하지 않고** 앞뒤 공백을 무시합니다(`ab3k9z`, ` Ab3K9z ` 모두 `AB3K9Z`). 서버가 돌려주는 `rid`는 항상 대문자입니다.
- 형식이 다르거나 없는 코드는 모두 `err room_not_found`입니다(`bad_request`는 `rid`가 문자열이 아니거나 32바이트를 넘을 때만).
- 코드는 방이 살아 있는 동안만 유효하고 방이 비면 사라집니다(재사용될 수 있음).

## 실시간 프로토콜 (WebSocket `/ws?token=<JWT>`)

토큰이 없거나 틀리거나 계정이 없으면 연결 직후 코드 **4401**로 닫습니다. 같은 계정이 다시 접속하면 이전 연결을 **4409**로 닫고, 이전 연결은 그 즉시 방에서 나간 것으로 처리합니다(다른 사람에게 `peer leave`, 방장이었으면 `host` 이관). 새 연결은 방 밖에서 시작하므로 바로 `create`/`join` 할 수 있습니다(`already_in_room` 없음). IP당 동시 연결이 20개를 넘으면 새 연결을 **4429**로 닫습니다. 토큰은 로그에 남기지 않습니다. `id` = `players.id`(문자열), `name` = 닉네임(없으면 `player<id>`). 계정 id와 닉네임은 이미 `hello{id,name}`, `joined.players[{id,name}]`, `peer{ev,id,name}`에 모두 들어 있습니다(추가 필드 없음).
텍스트 JSON 한 줄, 모든 메시지에 `t`. 크기 한도는 **바이트(UTF-8)** 기준입니다(한글 1자 = 3바이트): `state` 512KB, 그 외 8KB(초과 시 `bad_request`; 512KB를 넘는 프레임은 연결이 끊김). 15초 ping/pong으로 죽은 연결 정리. 모든 메시지 초당 60개 초과 시 1008로 종료.

클라이언트 → 서버

| 메시지 | 설명 |
| --- | --- |
| `{t:"create", room, max}` | 방 만들기(이름 1~30자, `<` `>` 포함 시 `bad_request`(리치텍스트 방지), `max` 1~8, 생략하면 4). 만든 사람이 방장 |
| `{t:"list"}` / `{t:"join", rid}` / `{t:"leave"}` | `rid` = 방 코드(대소문자 무관) |
| `{t:"transfer", to}` | 방장만. 대상은 방 안의 다른 사람 |
| `{t:"pos", map, x, y, z, ry, sp}` | 내 위치. 방 안이면 다른 사람에게 `from`을 붙여 중계. 초당 30개 초과분은 버림(오류 없음). 방 밖에서도 마지막 값은 접속 종료 시 저장 |
| `{t:"state", snap}` | 방장만. 공용 월드 스냅샷(문자열). 서버는 방별 **최신 1개만 메모리에** 보관하고 다른 사람에게 중계. 내용은 해석하지 않음(`opSeq`도 그대로) |
| `{t:"world", ev, data}` | 방장만. `ev` 1~32자, `data` 문자열 8KB 이하. 다른 사람에게 그대로 중계, **저장 안 함** |
| `{t:"op", ev, data}` | 방 안 누구나. 서버가 방별 `seq`(1부터)를 붙여 **보낸 사람 포함 전원**에게 전달. 한도 8KB, 1인 초당 10개(초과는 `rate_limited`) |
| `{t:"msg", to, data}` | 방장은 `to` = ID 또는 빈 문자열(전원). 그 외 멤버는 `to`가 현재 방장일 때만 가능(아니면 `not_host`) |

서버 → 클라이언트

| 메시지 | 설명 |
| --- | --- |
| `{t:"hello", id, name}` | 접속 직후 |
| `{t:"rooms", rooms:[{rid,room,host,hostName,count,max}]}` | |
| `{t:"joined", rid, host, players:[{id,name}], snap, ops:[{seq,from,ev,data}]}` | create/join 성공. `rid`는 방 코드(대문자). `snap`은 방의 최신 스냅샷(없으면 `""`), `ops`는 방의 최근 op 최대 256개(오래된 것부터). 클라이언트는 `snap`의 `opSeq`보다 큰 것만 적용 |
| `{t:"peer", ev:"join"\|"leave", id, name}` | 방 안 다른 사람들에게 |
| `{t:"host", host, reason:"transfer"\|"disconnect", snap}` | 방 전체에게. 방장이 나가거나 끊기면 가장 먼저 들어온 사람이 새 방장 |
| `{t:"pos", from, map, x, y, z, ry, sp}`, `{t:"state", snap}`, `{t:"world", ev, data}`, `{t:"op", seq, from, ev, data}`, `{t:"msg", from, data}` | 중계 |
| `{t:"err", code}` | `bad_request`, `not_in_room`, `already_in_room`, `room_not_found`, `room_full`, `not_host`, `too_many_rooms`, `peer_not_found`(transfer/msg 대상 없음), `rate_limited`(op, state) |

Cloudflare 서버에서는 문자열 `ping`을 보내면 `pong`을 돌려줍니다(선택 사항, 연결 유지용. 서버를 깨우지 않음). 512KB를 넘는 프레임은 코드 1009로 닫힙니다.

규칙: 빈 방은 즉시 삭제, 방 정원 1~8, 방 최대 50개. `seq`는 방이 삭제될 때까지 계속 증가하고 방장이 바뀌어도 이어집니다.
