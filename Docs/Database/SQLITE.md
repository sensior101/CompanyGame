# SQLite 테이블과 로컬 저장 구조

기준은 싱글플레이 게임의 로컬 저장이다. [TABLES.md](TABLES.md)의 공통 테이블을 SQLite 자료형으로 사용하고, 아래 5개 로컬 전용 테이블을 추가한다. 후속 기능을 개발할 때 [EXTENSIONS.md](EXTENSIONS.md)의 해당 테이블을 추가한다. 아직 기능을 구현하지 않은 테이블을 처음부터 모두 만들 필요는 없다.

## 1. 파일 배치와 데이터 소유

권장 초기 구성: Unity의 `Application.persistentDataPath` 아래 `companygame.sqlite` 1개에 여러 슬롯을 저장한다. 각 슬롯은 서로 다른 world_id를 가진다. 슬롯을 복사하면 새 world_id를 발급하고 그 슬롯의 모든 W 테이블 행에 새 값을 적용한다. 서로 다른 슬롯에 동일 stock_id/company_id가 있어도 PK에 world_id가 포함되어 충돌하지 않는다.

마스터 정의와 세이브 테이블을 같은 SQLite 파일에 두면 일반 FK를 사용할 수 있다. 원본 정의는 버전 관리되는 JSON/ScriptableObject로 배포하고 DB 초기화 시 넣는다. 추후 catalog.sqlite와 save.sqlite로 물리 분리하면 SQLite의 일반 FK로 파일 간 참조를 강제할 수 없으므로, 이 문서의 FK를 그대로 유지한다고 가정하면 안 된다.

온라인 접속 시 이 파일의 싱글 월드에 온라인 진행을 덮어쓰지 않는다. 온라인 응답 캐시는 별도 파일 `online_cache.sqlite`로 분리하는 것을 권장한다. 캐시에는 공개 콘텐츠/본인에게 허용된 응답만 보관하며 아래 권위 있는 세이브 테이블 전체를 다운로드하지 않는다.

## 2. 로컬 전용 테이블

공통 자료형/NOT NULL 표기는 TABLES.md와 같다. 아래 테이블은 `[W]` 자동 규칙을 쓰지 않으므로 world_id가 필요한 곳에 직접 적었다.

### 2.1 `local_profiles` - 설치 기기의 로컬 사용자

필드: `local_profile_id ID`, `display_name S(100)`, `created_at UTC`.

PK(local_profile_id). CK: 이름 비어 있지 않음. 온라인 로그인 계정과 별개이고 비밀번호/인증 토큰을 저장하지 않는다.

### 2.2 `save_slots` - 슬롯 목록

필드: `slot_id ID`, `local_profile_id ID`, `slot_no N`, `world_id ID`, `label S(100)`, `save_format_version N`, `save_sequence N=0`, `saved_game_minute N`, `last_saved_at UTC?`, `play_seconds N=0`, `last_map_id ID`, `thumbnail_path S(512)?`.

PK(slot_id); UQ(local_profile_id,slot_no); UQ(world_id); FK(local_profile_id)->local_profiles(local_profile_id); FK(world_id)->worlds(world_id); FK(last_map_id)->map_definitions(map_id).

CK: slot_no>=0, save_format_version>0, save_sequence/saved_game_minute/play_seconds>=0. TX: world_mode=single. saved_game_minute/last_map은 슬롯 메뉴용 요약 캐시이며 worlds/characters와 같은 TX에서 갱신. 실제 로드는 worlds/characters가 원본이다.

### 2.3 `local_characters` - 로컬 프로필과 캐릭터 연결

필드: `local_profile_id ID`, `world_id ID`, `character_id ID`, `created_at UTC`.

PK(world_id,character_id); FK(local_profile_id)->local_profiles(local_profile_id); FK(world_id,character_id)->characters(world_id,character_id). UQ(local_profile_id,world_id,character_id)는 아래 복합 FK 대상용으로 명시한다.

TX: 해당 world의 slot.local_profile_id와 일치 확인. 은퇴한 캐릭터도 연결행 보존. 한 슬롯의 과거 캐릭터를 새 캐릭터 ID로 덮어쓰지 않는다.

### 2.4 `local_active_characters` - 슬롯에서 플레이 중인 캐릭터

필드: `local_profile_id ID`, `world_id ID`, `character_id ID`.

PK(local_profile_id,world_id); UQ(world_id); FK(local_profile_id,world_id,character_id)->local_characters(local_profile_id,world_id,character_id).

한 싱글 월드당 활성 플레이어 1명이라는 초기 가정이다. TX: 해당 캐릭터가 retired/disabled가 아닌지 검사. 새 캐릭터 시작 시 이 포인터를 변경하고 이전 캐릭터 상태를 보존한다.

### 2.5 `local_preferences` - 화면·소리·조작 설정

필드: `local_profile_id ID`, `setting_key CODE`, `value_json JSON`, `updated_at UTC`.

PK(local_profile_id,setting_key); FK(local_profile_id)->local_profiles(local_profile_id). 예: volume, graphics, input_bindings, phone_last_tab. 허용 키/JSON 스키마를 앱에서 검사. 잔액·보유주식·퀘스트를 이 범용 설정 테이블에 넣지 않는다.

## 3. 양쪽 DB에 둘 `schema_migrations`

로컬과 MySQL에 각각 독립 생성한다. 필드: `version N`, `name S(200)`, `checksum S(64)`, `applied_at UTC`.

PK(version); CK: version>0, checksum 길이=64. 파일에 성공 적용된 스키마 버전을 기록한다. 게임 설정의 ruleset 버전, 데이터 형식의 save_format_version과 별개다. checksum이 다른 동일 version 스크립트를 자동 재적용하지 않는다.

## 4. SQLite 물리 제약

- 지원하는 SQLite 런타임이면 STRICT 테이블을 사용한다(3.37 이상). `VARCHAR`, `DATETIME`, `BOOL`이라고 선언하지 않고 표의 TEXT/INTEGER/REAL로 치환한다.
- 모든 연결에서 트랜잭션을 시작하기 **전에** `PRAGMA foreign_keys=ON`을 실행하고 실제 값이 1인지 확인한다. [SQLite FK 공식 문서](https://www.sqlite.org/foreignkeys.html)
- SQLite의 TEXT 길이는 `VARCHAR(n)`처럼 자동 제한되지 않으므로 ID/CODE/본문 길이에 CHECK를 둔다. JSON 필드는 `json_valid(column)`로 문법 확인 후 앱에서 의미를 검증한다. [STRICT 공식 문서](https://www.sqlite.org/stricttables.html)
- 명세의 BOOL에는 CHECK IN(0,1), 금액·수량의 비음수/양수 CHECK, 모든 복합 PK 열에 NOT NULL을 명시한다.
- FK 자식 열에는 조회/삭제 검사에 필요한 인덱스를 만든다. PK/UNIQUE의 왼쪽 접두어로 이미 충족하는 인덱스는 중복 생성하지 않는다.
- 전역 설정으로 WAL 사용을 검토하고, 쓰기는 하나의 저장 큐로 직렬화한다. 거래는 `BEGIN IMMEDIATE`로 시작해 잔액 확인부터 변경까지 수행한다. WAL에서도 동시 쓰기 무제한이 되는 것은 아니다.
- 백업은 SQLite 온라인 백업 API 또는 DB가 닫힌 일관된 상태에서 수행한다. 실행 중 main .sqlite 파일만 복사하면 WAL의 최신 커밋을 놓칠 수 있다. [SQLite 백업](https://www.sqlite.org/backup.html), [WAL](https://www.sqlite.org/wal.html)
- 실제 저장 파일을 Git에 넣지 않고 스키마/마이그레이션과 시드 원본만 버전 관리한다.

조건부 CHECK를 DDL로 옮길 때 NULL을 주의한다. 예를 들어 완료 상태의 completed_at을 요구하려면 `status<>'completed' OR (completed_at IS NOT NULL AND ...)`처럼 존재 조건을 명시한다. 단순 날짜 비교는 NULL에서 UNKNOWN이 되어 CHECK를 통과할 수 있다.

## 5. 저장·로드 순서

1. 게임 스레드에서 시계/회사/주식/계좌/인벤토리 등의 DTO를 같은 논리 시점으로 캡처한다. 현재 CaptureState()의 얕은 복사는 깊은 복사 또는 상태 변경 정지로 보완한다.
2. 쓰기 큐가 트랜잭션을 시작한다. 신규 부모행을 먼저 만들고 자식행을 저장한다. 계좌 변경 등 명령 단위 커밋과 전체 체크포인트가 서로 옛값을 덮어쓰지 않도록 단일 저장 서비스가 버전을 관리한다.
3. RNG 상태, world_job_runs, worlds 및 슬롯 요약을 함께 갱신한다. 정상 완료 시 COMMIT, 실패하면 ROLLBACK한다.
4. 로드 시 schema_migrations/저장 형식을 확인하고, 마스터→월드/actor→회사/계좌→종목/보유량→자식 이력 순으로 DTO를 복구한다. FK와 무관한 C# 복원 순서도 의존성을 따라야 한다.
5. UI 이벤트는 데이터 복원 완료 후 발행한다. 실행 중인 씬 오브젝트를 먼저 켜서 시작 자금/초기 회사가 저장값 위에 생성되지 않도록 한다.

시간, 시세, 거래 내역을 로드할 때 중복 경제 시뮬레이션이 수행되지 않도록 마지막 완료 job과 저장 시점을 확인한다. 로컬 파일 변조를 완전히 막는 보안 수단으로 SQLite 제약을 해석하지 않는다. 이 DB는 싱글 월드 저장의 일관성을 위한 것이다.

## 6. 초기 데이터

신규 슬롯마다: worlds 1개, player actor/character 1개, 캐시·예금계좌 각 1개, 시스템 발행/증시/수수료/세금 수취 계좌, 코드의 기본 회사 6개 및 해당 종목·시세, NPC 상점/기본 물류회사(구현 단계에 맞게)를 생성한다. 시작금 10000원은 현재 씬의 값이므로 기획 확정값으로 표시하지 말고 ruleset 설정으로 둔다. 시작금도 시스템 계좌에서 지급하는 최초 거래로 기록한다.

기존 고정 ID 예: `company_sg_hynix`, `stock_sg_hynix`, `stock_saseong`, `stock_kia`. PDF의 표시명 '기우자동차'와 코드 '기아자동차'는 표시명 변경 논의 대상이며 안정된 ID는 이름과 분리한다.
