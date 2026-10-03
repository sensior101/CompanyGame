# CompanyGame DB 설계 초안 v0.1

작성일: 2026-09-23. 기준: `feature/phone-backend`, 커밋 `d99846e`.

요청의 `future/phone-backend`는 실제 체크아웃 이름인 `feature/phone-backend`로 해석했다. 이번 산출물은 테이블/필드/PK/FK/제약조건 명세다. ERD, 실행용 DDL, DB 생성, Unity 코드 변경은 포함하지 않는다. 확정되지 않은 게임 규칙은 제안과 확인 필요 사항으로 구분했다.

## 읽는 순서

1. [공통 테이블 명세](TABLES.md): 양쪽 DB에 들어갈 게임 데이터의 필드, 키, 관계, 제약조건.
2. [SQLite 설계](SQLITE.md): 로컬 전용 테이블, 저장 슬롯, 자료형과 저장 처리.
3. [MySQL 설계](MYSQL.md): 서버 전용 테이블, 계정, 월드 참여, 요청 중복 방지와 동시성.
4. [후속 콘텐츠 테이블](EXTENSIONS.md): 기획에는 있지만 현재 코드에 없는 부가 콘텐츠의 초안.

**SQLite와 MySQL은 서로 다른 종류의 게임 변수를 나누어 맡는 DB가 아니다.** 싱글플레이에서는 SQLite가 해당 세이브의 재화, 인벤토리, 회사, 시세, 퀘스트까지 소유한다. 온라인에서는 백엔드와 MySQL이 온라인 월드의 해당 상태를 소유한다. 같은 개념의 테이블을 공유하되, 로컬 설정/세이브 관리와 서버 계정/접속 관리는 별도로 둔다.

온라인 Unity 클라이언트는 API에 명령을 전송한다. MySQL 접속 계정과 SQL 실행 권한을 클라이언트에 넣지 않는다. 온라인 잔액·주식·아이템을 로컬 저장값으로 덮어쓰는 양방향 동기화는 이 설계에 없다. 싱글 세이브의 온라인 반입을 허용하려면 별도의 승인·검증 정책이 필요하다.

## DB별 구성 요약

| 구분 | SQLite 싱글플레이 | MySQL 온라인 |
|---|---|---|
| 공통 게임 테이블 | TABLES.md의 79개 중 구현할 A/B 기능 선택 | 같은 79개 논리 테이블 사용 |
| 기기/계정 관리 | local_profiles, save_slots, local_characters, local_active_characters, local_preferences | accounts, auth_sessions, world_memberships, account_characters, active_account_characters, character_names, player_connections |
| 서버 전달·감사 | 기본 불필요 | outbox_events, server_audit_logs |
| 스키마 버전 | schema_migrations | schema_migrations, SQLite와 독립 이력 |
| 후속 콘텐츠 | EXTENSIONS.md의 33개 중 필요한 것만 추가 | 동일, 플레이어 간 기능은 온라인에서 활성화 |

숫자는 설계 후보의 전체 목록이지 첫 개발 단계의 필수 테이블 수가 아니다. ERD에서는 게임 정의, 월드/캐릭터, 금융, 회사/상품, 주식, 소셜/신고를 나누어 작업하면 된다. 특히 `accounts.account_id`는 **로그인 계정 ID**, `money_accounts.account_id`는 **현금/예금 등 금융계좌 ID**로 서로 다른 키다. 이름이 같다는 이유로 둘을 FK로 연결하지 않는다.

## 근거와 현재 구현 범위

원문은 저장소 상위의 `기획안.pdf` 15쪽, `역할_분담.pdf` 2쪽이다. README와 과거 구현 계획보다 이 PDF들의 최신 기획을 우선했다. 역할 분담은 1쪽의 상세 텍스트와 2쪽의 역할 요약 이미지를 함께 확인했다.

| 영역 | 현재 코드의 상태 | DB 설계에 반영한 내용 |
|---|---|---|
| 시간 | `GameTime`, `SimpleGameClock` 구현. 계절당 20일 | `worlds.game_minute`, `game_rulesets.days_per_season`. 신규 월드는 PDF의 30일 제안 |
| 재화/은행 | `PropertyManager`, `PropertyWallet`, `BankAccount`의 현금/예금 | 현금/예금을 별도 `money_accounts` 행으로 관리하고 거래 원장과 함께 저장 |
| 회사 | `CompanyState`, 일별 매출·수요·점유율, 세 가지 기존 레벨 | 회사 상태·일별 실적·업종별 성장 항목 분리. 순이익/비용은 신규 제안 |
| 주식 | 종목, 시세, 시간별 갱신, 매매, 보유 주식, IPO, 뉴스 | 시세 현재값/이력/체결/보유량/뉴스 분리. 실행 시간과 식별자 추가 |
| 경제 이벤트 | 정의, 업종별 충격, 활성 이벤트 | 이벤트 정의와 월드별 발생 인스턴스 분리 |
| SNS | 글/좋아요/뉴스 자동 게시, 10분 쿨다운 | 게시물/좋아요/팔로우 분리. 일 3회 제한은 PDF 기준 신규 로직 |
| 신고 | 신고, 판결, 벌점 원장. 증거 문자열이 있으면 유죄인 임시 판정 | 증거/사건/판결/벌점 이력/처벌 집행 분리 |
| 저장/아이템/퀘스트/세금/토지 | `SaveManager` 등 대부분 틀만 있음 | 해당 테이블은 구현 완료 모델이 아니라 기획 기반 제안 |
| 서버 | 현재 브랜치의 `Server`는 README만 존재 | MySQL 인증/접속 테이블은 신규 제안. 다른 브랜치의 API와 확정 통합된 명세가 아님 |

주요 코드 근거: [GameTime.cs](../../CompanyGame/Assets/Scripts/Core/GameTime.cs), [PropertyManager.cs](../../CompanyGame/Assets/Scripts/Economy/Property/PropertyManager.cs), [CompanyState.cs](../../CompanyGame/Assets/Scripts/Economy/Company/CompanyState.cs), [StockExchange.cs](../../CompanyGame/Assets/Scripts/Economy/StockMarket/StockExchange.cs), [SaveManager.cs](../../CompanyGame/Assets/Scripts/Core/SaveManager.cs).

역할 분담상 **성진: 시간·날짜, 주식 로직, 씬 이동**, **채영: 서버 연동, 휴대폰 서비스/UI, 주식 뉴스**다. 따라서 성진은 월드 시간·시세·매매·회사 실적에 필요한 데이터 계약을 먼저 정하고, 채영과 인증 ID·거래 요청/응답·저장 트랜잭션을 합의하는 순서가 맞다. PDF에는 DB 전체 설계의 단독 담당자가 명시되지 않았다.

## 구현 단계

명세의 `[A]`는 현재 프레임워크 저장 및 기본 싱글플레이, `[B]`는 기획 기능 확장, `[C]`는 후속 콘텐츠다. 모든 테이블을 한 번에 구현할 필요는 없다.

| 우선순위 | 먼저 연결할 테이블 | 완료 기준 |
|---|---|---|
| 1 | `game_rulesets`, `worlds`, `map_definitions`, `spawn_points`, `actors`, `characters`, SQLite 전용 세이브 테이블 | 새 게임/저장/로드/씬 이동 후 시간과 위치 복원 |
| 2 | `game_operations`, `money_accounts`, `money_transactions`, `money_entries` | 입출금 실패 시 부분 차감 없음, 종료 후 현금/예금 복원 |
| 3 | 회사·경제 이벤트·주식·뉴스 테이블, `world_rng_states`, `world_job_runs` | 매매 1회 처리, 08시 뉴스/09시 개장/16시 마감, 로드 후 같은 시장 상태 |
| 4 | 인벤토리·상점·배송, SNS·신고 | 구매 후 물품 보존, 중복 수령 방지, 신고/좋아요 복원 |
| 5 | 퀘스트·업적·직원·세금·대출 등 `[B]` | 기능별 상태와 보상까지 하나의 트랜잭션으로 처리 |
| 6 | MySQL 전용 계정/접속 + 서버 권한 검증 | 같은 월드의 여러 플레이어가 중복 지급 없이 거래 |

## 미확정 및 코드와 다른 규칙

| 항목 | 확인된 차이 | 설계 방침 |
|---|---|---|
| 달력 | PDF 30일/계절, 코드 20일/계절 | 월드마다 불변 ruleset을 지정. 기존 세이브를 30일로 몰래 재해석하지 않음 |
| 회사 레벨 | PDF 0레벨 설립, 업종별 성장 항목. 코드는 생산/품질/마케팅 | `company_growth`로 항목 분리. 기존 세 항목을 수익성/규모로 이름만 바꾸지 않음 |
| IPO | PDF 7레벨부터, 살인청부 업종은 불가 | 레벨과 업종 둘 다 서비스에서 검사 |
| SNS | 코드 10분 간격, PDF 플레이어 게시물 하루 최대 3개 | 둘을 독립 설정. 시스템 뉴스는 플레이어 제한에서 제외 |
| 입원 | PDF 보험 항목 4시간, 병원 항목 6시간 | 확정 전 6시간을 제안값으로만 사용. 불변 ruleset에 보관 |
| 신용도 | 기부 시 상승, 신고 시 하락. 대출 문장은 '일정값 이상이면 불가' | 허용 방향/최솟값/최댓값 확정 전 강한 CHECK로 고정하지 않음 |
| 제조업 쇼핑몰 | 일부 문장은 제조업도 5레벨 앱 출시, 상세 공급 흐름은 물류회사 중심 | 판매 채널 해금 규칙을 업종별 설정으로 보관 |
| 주식 거래 | 현재 코드는 시뮬레이션 시장과 즉시 체결 | 초기 온라인도 동일 방식 가정. 플레이어 지정가 호가창은 별도 설계 |
| 퀘스트/업적 | 세부 목표와 보상 목록 미확정 | 정의와 진행도를 분리하고 보상 규격은 버전이 있는 설정으로 보관 |
| 은퇴 | 새 캐릭터로 재시작, 자산 승계 규칙 없음 | 캐릭터 상태 보존, 자동 자산 이전 없음 |

## ERD를 그릴 때 공통 규칙

- 계정과 캐릭터, NPC를 구분한다. 계정은 MySQL 전용이고 게임 행위 주체는 월드 안의 `actors`다.
- 런타임 테이블은 **`world_id`를 포함하는 복합 PK/FK**를 사용한다. 월드 A의 보유 주식이 월드 B 종목을 참조하는 실수를 DB에서 차단한다.
- 현금/예금/회사자금은 계좌별로 분리하고, 잔액 변경과 원장 기록을 함께 커밋한다.
- `List<T>`는 자식 테이블로 분리한다. 주식 이력, 보유량, 좋아요, 신고 목록을 거대한 JSON 세이브 하나에 넣지 않는다.
- JSON은 버전이 있는 튜닝 설정, 난수 상태, 소규모 조건/보상 정의에 한정한다. JSON 안의 식별자는 DB FK가 아니므로 배포 검증 대상이다.
- 회사 점유율/시세 등 화면 값은 파생값과 원본을 구분한다. `AverageCost`, `Likes`, 연도·계절·일차는 조회 시 계산한다.
- 통계상 NPC 소비자 1천만 명을 `actors` 1천만 행으로 만들지 않는다. 실제 등장 NPC만 행으로 만들고 소비자는 집계값으로 저장한다.

## 검증 범위

이 문서는 설계 초안이다. 아래 제약을 `DB`와 `TX`로 구분했으며, TX는 트랜잭션과 서비스 로직을 구현해야 보장된다. 실제 SQLite/MySQL에 DDL을 적용하거나 동시 거래 테스트를 수행한 상태는 아니다.

기술 기준은 SQLite 3.37 이상(STRICT 사용 시), MySQL 8.4/InnoDB다. SQLite 연결마다 FK를 활성화해야 하며, MySQL CHECK는 같은 행 범위의 검증에 사용한다. 여러 행의 합계, 소유권, 계정 권한은 트랜잭션에서 검증한다.

- [SQLite 외래 키](https://www.sqlite.org/foreignkeys.html)
- [SQLite STRICT 테이블](https://www.sqlite.org/stricttables.html)
- [MySQL CHECK 제약조건](https://dev.mysql.com/doc/refman/8.4/en/create-table-check-constraints.html)
- [MySQL 잠금 조회](https://dev.mysql.com/doc/refman/8.4/en/innodb-locking-reads.html)
