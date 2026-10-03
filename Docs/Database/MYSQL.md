# MySQL 테이블과 온라인 저장 구조

기준: MySQL 8.4, InnoDB, utf8mb4. [TABLES.md](TABLES.md)의 공통 게임 테이블을 MySQL 자료형으로 만들고 아래 서버 전용 테이블을 추가한다. 단일 서버 프로세스/시뮬레이션 월드부터 시작하는 설계 초안이다. 현재 체크아웃 브랜치에는 실제 서버 구현이 없다.

## 1. SQLite와의 대응

| 내용 | SQLite | MySQL |
|---|---|---|
| 공통 게임 상태 | 로컬 세이브의 권위 데이터 | 온라인 서버가 관리하는 권위 데이터 |
| world_id | 슬롯마다 한 월드 | 독립 경제/시간을 공유하는 서버 월드 |
| actor/character ID | 로컬 발급 UUID 또는 안정된 시드 ID | 서버 발급 UUID 또는 안정된 시드 ID |
| 사용자 구분 | local_profiles/local_characters | accounts/account_characters |
| 활성 캐릭터 | local_active_characters | active_account_characters |
| 돈·주식·아이템 갱신 | 로컬 명령+SQLite 트랜잭션 | 인증된 API 명령+MySQL 트랜잭션 |
| 중복 처리 | game_operations의 요청 키 | 같은 키+인증된 actor, 클라이언트 재시도에도 유지 |
| UI/소리/키 설정 | local_preferences | 기본은 기기에 유지. 원하면 별도 계정 설정 API |
| 다른 플레이어 시세/회사/게시물 | 싱글 월드 NPC 시뮬레이션 | 같은 world의 공유 상태 |

## 2. 서버 전용 테이블

이 절은 `[W]` 자동 규칙을 쓰지 않고 모든 PK/FK 열을 직접 명시한다. `?` 외에는 NOT NULL.

### 2.1 `accounts` - 로그인 계정

필드: `account_id ID`, `login_name S(100)`, `login_name_key S(100)`, `password_hash S(255)?`, `auth_provider CODE`, `provider_subject S(255)?`, `status CODE=active`, `created_at UTC`, `updated_at UTC`.

PK(account_id); UQ(login_name_key); UQ(auth_provider,provider_subject). CK: status IN(active,suspended,closed); 로컬 비밀번호 방식이면 password_hash 필수, 외부 인증 방식이면 provider_subject 필수. auth_provider 허용값은 채택할 로그인 방식에 따라 확정.

login_name_key는 서버가 유니코드 정규화/대소문자 정책을 적용한 비교용 값이고 이진 collation으로 UNIQUE를 건다. 표시용 이름의 collation에 로그인 중복 판정을 맡기지 않는다. password_hash에는 salt/파라미터가 포함된 검증된 패스워드 해시 형식만 저장한다. 평문 비밀번호 저장 없음. 계정 ID가 기존 개발 중 API의 int와 다르면 별도 ID 매핑/마이그레이션이 필요하다.

### 2.2 `auth_sessions` - 로그인 세션/리프레시 토큰

필드: `session_id ID`, `account_id ID`, `refresh_token_hash S(64)`, `issued_at UTC`, `expires_at UTC`, `revoked_at UTC?`, `last_seen_at UTC?`.

PK(session_id); FK(account_id)->accounts(account_id); UQ(refresh_token_hash); IX(account_id,expires_at). CK: expires>issued, revoked NULL 또는 >=issued, hash 길이=64. 토큰 원문 대신 충분히 랜덤한 리프레시 토큰의 해시를 보관하고 회전/폐기 처리. JWT만 쓰는 구현이라면 이 테이블을 자동 도입하지 말고 세션 정책부터 결정한다.

### 2.3 `world_memberships` - 계정의 월드 접근권

필드: `world_id ID`, `account_id ID`, `role CODE=player`, `status CODE=active`, `joined_at UTC`, `updated_at UTC`.

PK(world_id,account_id); FK(world_id)->worlds(world_id); FK(account_id)->accounts(account_id). CK: role IN(player,moderator,admin), status IN(active,banned,left). TX: world_mode=online, 서버가 역할 부여. 멤버십 존재만으로 모든 개인 메모/DM 조회를 허용하지 않는다.

### 2.4 `account_characters` - 계정의 캐릭터 소유

필드: `world_id ID`, `character_id ID`, `account_id ID`, `created_at UTC`.

PK(world_id,character_id); UQ(account_id,world_id,character_id); FK(world_id,character_id)->characters(world_id,character_id); FK(world_id,account_id)->world_memberships(world_id,account_id). 여러 은퇴 캐릭터 보존 가능. 캐릭터 소유 계정 변경은 일반 API에서 허용하지 않는다.

### 2.5 `active_account_characters` - 월드별 활성 캐릭터

필드: `account_id ID`, `world_id ID`, `character_id ID`.

PK(account_id,world_id); UQ(world_id,character_id); FK(account_id,world_id,character_id)->account_characters(account_id,world_id,character_id). TX: active인 캐릭터만 선택. 한 계정이 한 월드에서 동시 여러 캐릭터로 재화/보상을 받지 않는 초기 정책.

### 2.6 `character_names` - 온라인 닉네임 중복 관리

필드: `world_id ID`, `character_id ID`, `nickname_key S(100)`.

PK(world_id,character_id); UQ(world_id,nickname_key); FK(world_id,character_id)->characters(world_id,character_id). nickname_key는 서버 정규화 후 이진 비교. 표시명은 actors.display_name. 변경 시 두 행을 한 TX. 월드 내 유일/은퇴 후 보존을 제안하며 게임 전체 유일 여부는 팀 결정 필요.

### 2.7 `player_connections` - 중복 접속/재연결

필드: `world_id ID`, `character_id ID`, `session_id ID`, `connection_epoch N`, `connected_at UTC`, `heartbeat_at UTC`, `server_instance_key S(100)`.

PK(world_id,character_id); FK(world_id,character_id)->characters(world_id,character_id); FK(session_id)->auth_sessions(session_id). CK: epoch>0, heartbeat>=connected. TX: session 계정이 해당 캐릭터 소유자인지 확인. 재접속마다 epoch 증가, 이전 연결의 명령 거절. 이 테이블만으로 실시간 움직임을 매 프레임 저장하지 않음.

### 2.8 `outbox_events` - DB 커밋 후 클라이언트 알림 전달

필드: `event_id ID`, `world_id ID`, `operation_id ID`, `sequence_no N`, `event_type CODE`, `recipient_actor_id ID?`, `payload_json JSON`, `created_at UTC`, `published_at UTC?`, `attempt_count N=0`.

PK(event_id); UQ(world_id,operation_id,sequence_no); FK(world_id,operation_id)->game_operations(world_id,operation_id); FK(world_id,recipient_actor_id)->actors(world_id,actor_id); IX(published_at,created_at). CK: sequence_no>0, attempts>=0. NULL recipient는 월드 공개용 이벤트에만 사용. 개인 잔액/DM/신고 증거를 브로드캐스트하지 않는다.

거래와 알림행을 같은 TX에 기록하고 별도 전송기가 커밋 후 전달한다. 네트워크 재시도로 중복 전달될 수 있으므로 수신자는 event_id/row_version으로 중복 적용을 막는다. outbox는 정확히 한 번 전송을 보장하는 장치가 아니다.

### 2.9 `server_audit_logs` - 권한·관리자·실패 요청 감사

필드: `audit_id ID`, `account_id ID?`, `world_id ID?`, `action_code CODE`, `request_key ID?`, `outcome CODE`, `details_json JSON`, `created_at UTC`.

PK(audit_id); FK(account_id)->accounts(account_id); FK(world_id)->worlds(world_id); IX(account_id,created_at); IX(world_id,created_at). CK: outcome IN(success,rejected,error). 민감한 비밀번호/토큰/증거 원문은 기록하지 않는다. 요청 key는 진단 정보이며 이 테이블의 UNIQUE로 경제 중복 처리를 구현하지 않음.

### 2.10 `schema_migrations` - 서버 DB 마이그레이션

필드: `version N`, `name S(200)`, `checksum S(64)`, `applied_at UTC`.

PK(version); CK: version>0, checksum 길이=64. SQLite의 동일 이름 테이블과 별도의 적용 이력. MySQL DDL의 암시적 커밋을 고려해 '여러 CREATE TABLE 전체가 일반 트랜잭션으로 자동 롤백된다'고 가정하지 않는다. [MySQL 암시적 커밋](https://dev.mysql.com/doc/refman/8.4/en/implicit-commit.html)

## 3. MySQL 물리 제약과 인덱스

- InnoDB를 사용하고, 문자 PK/FK는 길이·문자셋·collation을 일치시킨다. ID는 ASCII 이진 비교, 한글 본문은 utf8mb4.
- FK 대상은 반드시 명시적 PK 또는 전체 UNIQUE 키. `world_id`가 포함된 복합 FK를 단일 ID FK로 축소하지 않는다.
- CHECK는 MySQL 8.4에서 행 단위 값의 범위를 강제한다. NULL 허용 여부는 CHECK와 별도로 선언한다. 다른 행의 합계/소유권/동시성은 CHECK가 해결하지 않는다. [MySQL CHECK](https://dev.mysql.com/doc/refman/8.4/en/create-table-check-constraints.html)
- 금액은 BIGINT signed, 금리/비율은 ppm 정수. DOUBLE은 좌표·시뮬레이션 근삿값에만 사용. C# long과 다른 UNSIGNED 범위를 무심코 섞지 않는다.
- 가능한 FK 인덱스는 명시하고, PK의 왼쪽 접두어로 커버되는 인덱스는 중복 생성하지 않는다. 주요 조회는 `(world_id,character_id,game_minute)`, `(world_id,conversation_id,created_at,message_id)`, `(world_id,stock_id,game_minute)` 순서를 사용한다.
- `created_at/updated_at`은 실제 UTC, 게임 시간은 BIGINT game_minute. real-time 토큰 만료와 game-time 적금/납세 기한을 혼용하지 않는다.
- 화면용 가격이력 240개, SNS 최근 200개 같은 제한은 쿼리 LIMIT이다. 거래/판결/재산 이력을 UI 제한 때문에 삭제하지 않는다.

## 4. 거래 처리 예: 주식 매수

1. 인증 세션에서 account_id를 얻고 월드 접근권과 선택 캐릭터를 검증한다. 요청 body의 character_id를 신뢰하지 않는다.
2. 트랜잭션에서 (world,actor,request_key) 성공행을 확인한다. 있다면 payload_hash를 비교하고 저장 결과를 반환한다. 동시 최초 요청은 UNIQUE 충돌 시 전체 롤백 후 기존 결과를 다시 조회한다.
3. 서버의 시뮬레이션 시각/가격 버전을 확인한다. 그 가격을 고정한 상태에서 대상 계좌·보유량을 잠금 또는 조건부 UPDATE로 갱신한다. 보유주식 행이 아직 없다면 유일한 PK에 대해 INSERT 후 충돌 처리/재조회한다.
4. 최대 주문량·장 시간·잔액·종목 상태를 검사하고 서버가 총액/수수료를 계산한다. 복수 계좌 잠금 순서는 account_id 오름차순으로 고정한다.
5. game_operations, stock_trades, stock_holdings, money_transactions/entries, 계좌 잔액, 필요한 알림 outbox를 함께 저장하고 COMMIT한다. 하나라도 실패하면 전부 ROLLBACK한다.
6. 커밋 후 결과를 전송한다. deadlock/네트워크 재시도는 동일 request_key를 유지한다.

`SELECT ... FOR UPDATE`는 트랜잭션 안에서 사용한다. 단순 SELECT로 잔액을 확인한 뒤 별개 UPDATE하면 다른 요청이 그 사이에 소비할 수 있다. [MySQL 잠금 조회](https://dev.mysql.com/doc/refman/8.4/en/innodb-locking-reads.html)

초기 시세는 월드당 단일 시뮬레이션 소유자 가정을 둔다. 서버 프로세스를 여러 개로 늘리면 job PK 외에 시뮬레이션 소유권/lease와 버전 검증을 추가해야 한다. SQLite 파일을 MySQL로 복사하는 것만으로 멀티플레이가 구현되는 것은 아니다.

## 5. 기본 API 책임 경계 제안

| 기능 | 클라이언트가 보내는 것 | 서버가 계산·결정하는 것 |
|---|---|---|
| 주식 매매 | 종목 ID, 수량, 방향, 요청 키 | 현재 가격, 수수료, 잔액·보유량 변화, 체결 결과 |
| 입출금 | 금액, 방향, 요청 키 | 본인 계좌, 가능한 잔액, 거래 이력 |
| 쇼핑 | 판매등록 ID, 수량, 배송지, 요청 키 | 단가, 판매 권한, 재고, 결제, 배송일 |
| 신고 | 대상, 범죄유형, 증거 ID, 요청 키 | 증거 접근권, 유죄/무죄, 벌금·벌점·신용 변화 |
| SNS/DM | 내용, 공개대상, 요청 키 | 작성자 ID, 횟수/쿨다운, 수신 권한, 발행시각 |
| 세금 | 고지 ID, 납부금액, 요청 키 | 대상자, 남은 세금, 계좌 차감, 납부 처리 |

SQLite 서비스를 먼저 구현할 때도 이 명령/결과 단위를 인터페이스로 잡으면 후에 원격 API 어댑터로 바꾸기 쉽다. Unity UI가 임의 SQL이나 account.balance 대입을 직접 수행하지 않도록 한다.
