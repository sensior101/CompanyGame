# 서버 API 계약

기본 주소는 개발 중 `http://localhost:3000`입니다. 본문은 JSON이고, 인증이 필요한 요청은 `Authorization: Bearer <token>` 헤더를 붙입니다. 토큰은 7일 동안 유효합니다.

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
| 400 | Fastify 검증 오류 | 입력 규칙 위반 |
| 401 | `{ error: "invalid_credentials" }` | 아이디 또는 비밀번호 불일치. 없는 아이디도 같은 응답입니다. |
| 401 | `{ error: "unauthorized" }` | 토큰이 없거나 만료됨 |
| 409 | `{ error: "username_taken" }` | 이미 있는 아이디 |
| 409 | `{ error: "nickname_taken" }` | 이미 있는 닉네임 |
