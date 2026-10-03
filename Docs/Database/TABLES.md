# 공통 게임 테이블 명세

SQLite 싱글 세이브와 MySQL 온라인 월드에 공통 적용한다. 각 DB의 추가 테이블 및 차이는 [SQLITE.md](SQLITE.md), [MYSQL.md](MYSQL.md)에 정의한다. 후속 콘텐츠는 [EXTENSIONS.md](EXTENSIONS.md)에 있다.

## 표기와 물리 자료형

`?`가 붙은 필드만 NULL 허용, 나머지는 모두 NOT NULL이다. `=값`은 DEFAULT이며 생략하면 삽입 시 명시한다. PK 필드도 항상 NOT NULL로 선언한다. `PK`, `FK`, `UQ`, `CK`, `IX`는 각각 기본키, 외래키, UNIQUE, CHECK, 일반 인덱스다. `TX`는 여러 행/권한을 읽는 서비스 트랜잭션 규칙으로 CHECK만으로 보장되지 않는다.

제약식은 설계용 축약 표기다. 예를 들어 `0<=a<b`는 SQL에서 `a>=0 AND a<b`로 풀어 쓴다. NULL 가능한 필드를 상태에 따라 필수로 만들 때는 `IS NOT NULL`도 명시한다. 비교 결과가 NULL인 CHECK만으로는 필수값을 강제할 수 없다.

| 약어 | 의미 | SQLite | MySQL |
|---|---|---|---|
| ID | 1~64자 ASCII 식별자. 신규 동적 객체는 UUID 문자열, 기존 고정 종목 ID도 수용 | TEXT COLLATE BINARY + 길이 CK | VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin |
| CODE | 1~32자 고정 코드 | TEXT + 길이 CK | VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin |
| S(n) | 최대 n자 문자열 | TEXT + 길이 CK | VARCHAR(n), utf8mb4 |
| TEXT | 본문 | TEXT | TEXT |
| N | 카운트/분/금액/단가/수량: 부호 있는 64비트 정수 | INTEGER | BIGINT |
| R | 좌표·체력·시뮬레이션 근삿값. 돈에는 금지 | REAL | DOUBLE |
| BOOL | 논리값 | INTEGER, CK IN(0,1) | TINYINT, CK IN(0,1) |
| UTC | 실제 시각(UTC, 밀리초) | TEXT, 고정 ISO 8601 UTC 형식 | DATETIME(3), 세션 UTC |
| JSON | 스키마 버전으로 검증하는 문서 | TEXT + json_valid CK | JSON |

`N` 금액은 원 단위이고 모든 언어에서 64비트 정수/정확한 정수로 처리한다. JavaScript Number의 안전 범위를 넘을 수 있으므로 API에서는 금액·큰 정수의 10진 문자열 전송을 권장한다. `*_ppm`은 1,000,000을 100%로 보는 정수 비율이다(수수료 0.15%=1,500). 곱셈은 넓은 정밀도에서 계산 후 반올림 규칙을 적용하고 64비트 범위를 확인한다.

### 월드 테이블의 공통 필드와 키 확장

`[W]` 표가 붙은 **모든** 테이블에는 아래 필드를 추가한다.

```text
world_id ID
FK(world_id) -> worlds(world_id)
```

이 문서의 `[W] PK(a,b)`는 실제 `PRIMARY KEY(world_id,a,b)`를 뜻한다. `[W] UQ(a,b)`와 `IX(a,b)`에도 world_id가 맨 앞에 붙는다. W 테이블 사이 `FK(x) -> parent(y)`는 실제 `FOREIGN KEY(world_id,x) REFERENCES parent(world_id,y)`다. **정의/마스터 테이블 FK에는 world_id를 붙이지 않는다.** 합성 FK의 모든 구성 열은 명시된 PK/UQ와 정확히 일치해야 한다.

가변 상태 행은 `row_version N=0, created_at UTC, updated_at UTC`를 추가한다(절별로 '가변' 표시). 불변 이력 행은 `created_at UTC`를 추가한다. 이미 필드 목록에 있는 메타 필드는 중복 추가하지 않으며 PK에 포함하지 않는다. `row_version>=0`; 업데이트마다 1 증가. UTC 값은 저장/API 계층에서 채운다. `game_minute`는 게임 시작 후 경과 분이며 실제 UTC와 별개다.

삭제 정책: 원칙적으로 모든 FK는 `ON DELETE RESTRICT / ON UPDATE RESTRICT`. 이력은 불변, 계정/캐릭터/회사 등은 상태를 바꾸어 보존한다. 좋아요·팔로우·대화 참여 같은 연결 행은 명시적으로 삭제할 수 있다. 슬롯 전체 삭제는 백업 후 자식부터 명시적으로 삭제하는 별도 작업이다. 원장의 FK에 일괄 CASCADE를 걸지 않는다.

토지의 사업장 포인터와 회사의 건물 포인터처럼 순환 참조가 있는 곳은 nullable 연결을 비운 채 부모행부터 만든 뒤 같은 TX에서 연결한다. 월드 전체를 삭제할 때도 이러한 nullable 포인터를 먼저 해제해야 한다. FK 검사를 끄는 방식으로 평상시 생성/삭제를 구현하지 않는다.

## 1. 게임 정의와 불변 규칙

각 정의는 배포 후 불변으로 취급하고, 의미/밸런스를 바꾸면 새 ID/버전을 발급한다. `ruleset_id`가 있는 정의는 `FK(ruleset_id)->game_rulesets(ruleset_id)`를 갖는다. 한 월드의 정의 선택은 `worlds.ruleset_id`와 일치하는지 TX/콘텐츠 빌드 검증에서 확인한다. 기존 저장 데이터를 위해 이전 정의를 삭제하지 않는다. ScriptableObject/JSON으로 원본을 관리해도 DB 배포본은 같은 고정 ID를 사용한다.

### 1.1 `game_rulesets` [A] - 달력과 경제 규칙 버전

필드: `ruleset_id ID`, `version N`, `days_per_season N=30`, `seasons_per_year N=4`, `real_seconds_per_game_hour R=300`, `week_start_offset N=0`, `stock_open_minute N=540`, `stock_close_minute N=960`, `news_minute N=480`, `daily_sns_limit N=3`, `settings_json JSON`, `created_at UTC`.

PK(ruleset_id); UQ(version). CK: version>0, days_per_season>0, seasons_per_year=4, real_seconds_per_game_hour>0, week_start_offset BETWEEN 0 AND 6, 0<=stock_open_minute<stock_close_minute<=1440, news_minute BETWEEN 0 AND 1439, daily_sns_limit>=0.

`settings_json`의 필수 하위 규격: `schema_version`, stock(가격 모델·수수료·가격제한·IPO·히스토리 길이), company(성장 항목/업종별 임계값/고용 한도), report(쿨다운/사업등록 제한), bank(금리·만기·신용도 정책), hospital(입원 시간), sns(글자수/쿨다운), shop(업종별 온라인 해금). CK가 JSON 속성의 의미까지 검증하지는 않는다. 0번 요일을 월요일로 제안하며 기존 세이브의 요일 규칙은 별도 확정한다.

### 1.2 `map_definitions` [A] - 씬 식별

필드: `map_id ID`, `display_name S(100)`, `scene_key S(255)`, `enabled BOOL=1`.

PK(map_id); UQ(scene_key). CK: 이름/scene_key 비어 있지 않음. `scene_key`는 빌드에서 해결 가능한 씬 경로/Addressables 키이며 Unity instanceID가 아니다.

### 1.3 `spawn_points` [A] - 맵 진입 지점

필드: `map_id ID`, `spawn_id ID`, `position_x R`, `position_y R`, `position_z R`, `yaw R`.

PK(map_id,spawn_id); FK(map_id)->map_definitions(map_id). TX: 좌표가 유한수이고 맵 경계 안인지 콘텐츠 검증.

### 1.4 `sector_definitions` [A] - 기업 업종

필드: `sector_id CODE`, `display_name S(100)`, `can_list BOOL=1`.

PK(sector_id). 살인청부 업종은 can_list=0. 기존 코드의 tech/auto/game/construction/beverage를 유지하며, 기획의 세부 업종은 별도 코드로 확장한다.

### 1.5 `item_definitions` [A/B] - 아이템 종류

필드: `item_id ID`, `ruleset_id ID`, `item_code CODE`, `display_name S(100)`, `category CODE`, `base_price N`, `max_stack N=1`, `tradable BOOL=1`, `health_delta R=0`, `energy_delta R=0`, `stress_delta R=0`, `asset_key S(255)?`, `effects_json JSON`.

PK(item_id); UQ(ruleset_id,item_code). CK: base_price>=0, max_stack>0. category IN(food,material,medicine,clothing,furniture,weapon,seed,fish,cosmetic,trophy,other). TX: 제한 보상은 tradable=0이고 거래 시 검사. 질병 치료/연료/장비 효과는 버전 있는 effects_json을 해석한다.

### 1.6 `recipe_definitions` [B] - 3×3 조합법

필드: `recipe_id ID`, `ruleset_id ID`, `output_item_id ID`, `output_quantity N=1`, `required_sector_id CODE?`, `craft_minutes N=0`.

PK(recipe_id); FK(output_item_id)->item_definitions(item_id); FK(required_sector_id)->sector_definitions(sector_id). CK: output_quantity>0, craft_minutes>=0.

### 1.7 `recipe_cells` [B] - 조합법 칸

필드: `recipe_id ID`, `cell_index N`, `ingredient_item_id ID`, `quantity N=1`.

PK(recipe_id,cell_index); FK(recipe_id)->recipe_definitions(recipe_id); FK(ingredient_item_id)->item_definitions(item_id). CK: cell_index BETWEEN 0 AND 8, quantity>0. 빈 칸은 행 없음. 회전/뒤집기 허용 여부는 기획 확인 필요.

### 1.8 `quest_definitions` [B] - 퀘스트 정의

필드: `quest_id ID`, `ruleset_id ID`, `title S(200)`, `repeat_kind CODE`, `requirements_json JSON`, `rewards_json JSON`.

PK(quest_id). CK: repeat_kind IN(once,daily,repeatable). 조건/보상 JSON의 아이템·칭호 ID는 콘텐츠 검증에서 존재와 ruleset 일치 확인.

### 1.9 `quest_objectives` [B] - 퀘스트 목표

필드: `quest_id ID`, `objective_no N`, `objective_kind CODE`, `target_item_id ID?`, `target_map_id ID?`, `required_count N`.

PK(quest_id,objective_no); FK(quest_id)->quest_definitions(quest_id); FK(target_item_id)->item_definitions(item_id); FK(target_map_id)->map_definitions(map_id). CK: objective_no>0, required_count>0; kind IN(collect,deliver,visit,talk,earn,other). collect/deliver는 target_item_id 필수, visit은 target_map_id 필수. 세부 talk 대상은 퀘스트 조건 규격으로 확정.

### 1.10 `achievement_definitions` [B] - 업적

필드: `achievement_id ID`, `ruleset_id ID`, `title S(200)`, `metric_code CODE`, `required_value N`, `rewards_json JSON`.

PK(achievement_id). CK: required_value>=0. 사업등록/범죄/기부/낚시 등 확정된 metric_code만 서비스에서 수용.

### 1.11 `title_definitions` [B] - 칭호

필드: `title_id ID`, `display_name S(50)`.

PK(title_id). 채팅 닉네임 앞에 표시. 획득 조건은 업적/시상식 정의에서 참조.

### 1.12 `employee_grade_definitions` [B] - 직원 등급별 능력

필드: `grade_id ID`, `ruleset_id ID`, `grade_code CODE`, `salary N`, `bonus_json JSON`.

PK(grade_id); UQ(ruleset_id,grade_code). CK: salary>=0, grade_code IN(C,B,A,S). 수치 능력은 이 정의만 참조하고 이름/나이/성별로 성능을 바꾸지 않는다. 일회성 방어 효과의 사용 상태는 직원 행에 별도 저장한다.

### 1.13 `crime_rules` [A] - 범죄별 처벌 기준

필드: `crime_rule_id ID`, `ruleset_id ID`, `crime_type CODE`, `penalty_points N`, `fixed_fine N=0`, `stolen_value_fine_ppm N=0`, `jail_minutes N=0`, `settlement_allowed BOOL=0`, `credit_delta N=0`.

PK(crime_rule_id); UQ(ruleset_id,crime_type). CK: crime_type IN(theft,trespass,assault,murder); penalty_points/fixed_fine/stolen_value_fine_ppm/jail_minutes>=0. 절도 배상 200%=2,000,000 ppm이며 비율을 100% 이하로 제한하지 않는다. 폭행 120분, 살인 1440분. 신용도 하락 수치는 현재 코드 제안값과 최종 기획을 구분한다.

### 1.14 `market_event_definitions` [A] - 경제 이벤트 종류

필드: `event_definition_id ID`, `ruleset_id ID`, `event_code CODE`, `display_name S(100)`, `description TEXT`, `daily_probability_ppm N`, `duration_days N`, `demand_multiplier_ppm N`.

PK(event_definition_id); UQ(ruleset_id,event_code). CK: daily_probability_ppm BETWEEN 0 AND 1000000, duration_days>0, demand_multiplier_ppm>=0.

### 1.15 `market_event_effects` [A] - 업종별 가격 충격

필드: `event_definition_id ID`, `effect_no N`, `scope_kind CODE`, `sector_id CODE?`, `hourly_log_return_ppm N`.

PK(event_definition_id,effect_no); FK(event_definition_id)->market_event_definitions(event_definition_id); FK(sector_id)->sector_definitions(sector_id). CK: effect_no>0; scope_kind='all'이면 sector_id IS NULL, 'sector'이면 NOT NULL. TX: 동일 정의에서 업종별 중복 효과/전체 효과 중복 등록 방지. PDF의 '+30%'와 코드의 '시간당 로그수익률'은 의미가 달라 자동 치환하지 않는다.

## 2. 월드, 플레이어, 진행 상태

### 2.1 `worlds` [A] - 세이브/온라인 월드, 가변

필드: `world_id ID`, `ruleset_id ID`, `world_name S(100)`, `world_mode CODE`, `game_minute N=480`, `clock_remainder_ms N=0`, `simulation_seed N`, `total_consumers N=1000000`, `status CODE`, `created_at UTC`, 공통 가변 메타 필드.

PK(world_id); FK(ruleset_id)->game_rulesets(ruleset_id). CK: game_minute/clock_remainder_ms>=0, total_consumers BETWEEN 10000 AND 10000000, world_mode IN(single,online), status IN(active,archived). TX: clock_remainder_ms가 1게임분에 대응하는 실제 시간 미만인지 검증. ruleset_id는 시작 후 고정; 변경 시 명시적 세이브 변환.

날짜는 game_minute로부터 계산한다. 연도/계절/일차/시/분을 각각 권위 있는 컬럼으로 중복 저장하지 않는다.

### 2.2 `world_rng_states` [A] [W] - 난수 흐름, 가변

필드: `stream_code CODE`, `algorithm_code CODE`, `algorithm_version N`, `initial_seed N`, `draw_count N=0`, `state_json JSON`.

PK(stream_code). CK: algorithm_version>0, draw_count>=0. stream 예: market,company,events. TX: clock/시세와 같은 저장 트랜잭션. 현재 System.Random의 시드만으로 중간 상태는 복원되지 않으므로 직렬화 가능한 PRNG 어댑터 구현 필요.

### 2.3 `world_job_runs` [A] [W] - 정시 작업 중복 방지, 불변 완료 이력

필드: `job_code CODE`, `scheduled_game_minute N`, `completed_game_minute N`, `operation_id ID`.

PK(job_code,scheduled_game_minute); FK(operation_id)->game_operations(operation_id). CK: 0<=scheduled_game_minute<=completed_game_minute. 작업 결과와 완료행을 한 TX에서 기록. 예: 08시 뉴스, 시세 틱, 일매출, 배송, 급여. 온라인 분산 스케줄러의 리더/잠금은 별도이며 이 PK만으로 시뮬레이션 메모리를 보호하지 않는다.

### 2.4 `actors` [A] [W] - 플레이어/NPC/시스템 주체, 가변

필드: `actor_id ID`, `actor_kind CODE`, `display_name S(100)`, `status CODE=active`, `created_game_minute N`.

PK(actor_id). CK: actor_kind IN(player,npc,system), status IN(active,retired,disabled), created_game_minute>=0. 돈 발행처·증시 뉴스 등은 system actor. 경제 시뮬레이션 집계 소비자는 actor로 생성하지 않는다.

### 2.5 `characters` [A] [W] - 플레이어 캐릭터, 가변

필드: `character_id ID`, `gender_code CODE?`, `appearance_json JSON`, `health R=100`, `energy R=100`, `stress R=0`, `intelligence N=0`, `charm N=0`, `physical N=0`, `farming N=0`, `cooking N=0`, `map_id ID`, `spawn_id ID`, `position_x R`, `position_y R`, `position_z R`, `yaw R`, `retired_game_minute N?`.

PK(character_id); FK(character_id)->actors(actor_id); FK(map_id,spawn_id)->spawn_points(map_id,spawn_id). CK: health/energy/stress BETWEEN 0 AND 100, 스탯 5개>=0, retired_game_minute NULL 또는 >=0. TX: actor_kind=player, 좌표 유한수/씬 유효성. energy는 현재 브랜치 코드에 없지만 기획에 있어 추가한 필드. 온라인 계정 연결은 MySQL의 `account_characters`, 로컬 연결은 SQLite의 `local_characters`에서 한다.

### 2.6 `npc_states` [B] [W] - 실제 NPC와 지원자 정보, 가변

필드: `npc_actor_id ID`, `template_key S(100)?`, `age N?`, `gender_code CODE?`, `flavor_ability_text S(255)?`, `map_id ID?`, `position_x R?`, `position_y R?`, `position_z R?`, `schedule_state_json JSON`.

PK(npc_actor_id); FK(npc_actor_id)->actors(actor_id); FK(map_id)->map_definitions(map_id). CK: age NULL 또는 >=0, 좌표 3개는 전부 NULL 또는 전부 NOT NULL. TX: actor_kind=npc. template_key는 Unity 콘텐츠 외부 키이며 DB FK가 아님. 이름은 actors에서 조회.

### 2.7 `character_effects` [B] [W] - 상태이상, 가변

필드: `character_id ID`, `effect_code CODE`, `started_game_minute N`, `expires_game_minute N?`, `stacks N=1`, `parameters_json JSON`.

PK(character_id,effect_code); FK(character_id)->characters(character_id). CK: started_game_minute>=0, expires NULL 또는 >=started, stacks>0. 허용 effect_code는 콘텐츠 정의에서 검증. 약 복용 시 아이템 소모와 효과 해제를 한 TX.

### 2.8 `actor_relationships` [B] [W] - NPC 호감도, 가변

필드: `character_id ID`, `target_actor_id ID`, `affinity N=0`, `last_dialogue_key S(100)?`.

PK(character_id,target_actor_id); FK(character_id)->characters(character_id); FK(target_actor_id)->actors(actor_id). CK: character_id<>target_actor_id. 호감도 범위는 기획 미확정이므로 설정/서비스에서 제한.

### 2.9 `character_titles` [B] [W] - 획득 칭호, 불변

필드: `character_id ID`, `title_id ID`, `acquired_game_minute N`, `operation_id ID`.

PK(character_id,title_id); FK(character_id)->characters(character_id); FK(title_id)->title_definitions(title_id); FK(operation_id)->game_operations(operation_id). CK: acquired_game_minute>=0.

### 2.10 `equipped_titles` [B] [W] - 표시 중인 칭호, 가변

필드: `character_id ID`, `title_id ID`.

PK(character_id); FK(character_id,title_id)->character_titles(character_id,title_id). 칭호 해제는 이 행 삭제. DB FK로 미획득 칭호 장착을 막는다.

### 2.11 `character_quests` [B] [W] - 퀘스트 수락/완료, 가변

필드: `character_id ID`, `quest_id ID`, `run_no N=1`, `status CODE`, `accepted_game_minute N`, `completed_game_minute N?`, `rewarded_game_minute N?`, `reward_operation_id ID?`.

PK(character_id,quest_id,run_no); FK(character_id)->characters(character_id); FK(quest_id)->quest_definitions(quest_id); FK(reward_operation_id)->game_operations(operation_id); UQ(reward_operation_id). CK: run_no>0, accepted>=0; status IN(active,completed,rewarded,abandoned); completed NULL 또는 >=accepted; completed/rewarded 상태에서는 completed 필수; active/abandoned 상태에서는 completed NULL; rewarded NULL 또는 (completed NOT NULL AND rewarded>=completed); rewarded status이면 rewarded/operation 모두 NOT NULL, 다른 status에서는 모두 NULL. TX: 반복 불가 퀘스트는 run_no=1만 허용; 여러 회차가 동시에 active가 되지 않도록 잠금 검사.

### 2.12 `quest_progress` [B] [W] - 목표별 진행도, 가변

필드: `character_id ID`, `quest_id ID`, `run_no N`, `objective_no N`, `current_count N=0`.

PK(character_id,quest_id,run_no,objective_no); FK(character_id,quest_id,run_no)->character_quests(character_id,quest_id,run_no); FK(quest_id,objective_no)->quest_objectives(quest_id,objective_no). CK: current_count>=0. TX: 목표 충족/아이템 제출/퀘스트 완료/보상은 함께 처리.

### 2.13 `character_achievements` [B] [W] - 업적 진행, 가변

필드: `character_id ID`, `achievement_id ID`, `progress N=0`, `unlocked_game_minute N?`, `reward_operation_id ID?`.

PK(character_id,achievement_id); FK(character_id)->characters(character_id); FK(achievement_id)->achievement_definitions(achievement_id); FK(reward_operation_id)->game_operations(operation_id); UQ(reward_operation_id). CK: progress>=0, unlocked NULL 또는 >=0; reward_operation_id NOT NULL이면 unlocked NOT NULL. TX: 최초 지급 시 조건부 업데이트/잠금으로 중복 지급 차단.

### 2.14 `character_recipes` [B] [W] - 조합법 해금, 불변

필드: `character_id ID`, `recipe_id ID`, `unlocked_game_minute N`.

PK(character_id,recipe_id); FK(character_id)->characters(character_id); FK(recipe_id)->recipe_definitions(recipe_id). CK: unlocked_game_minute>=0. 회사에서 사용할 수 있는 레시피 범위는 소유자/업종 규칙으로 검사.

## 3. 거래·은행·세금

### 3.1 `game_operations` [A] [W] - 성공한 명령의 중복 처리 방지, 불변

필드: `operation_id ID`, `actor_id ID`, `request_key ID`, `operation_kind CODE`, `payload_hash S(64)`, `result_json JSON`, `game_minute N`.

PK(operation_id); UQ(actor_id,request_key); FK(actor_id)->actors(actor_id). CK: game_minute>=0, payload_hash 길이=64. 상태 변경과 이 성공행을 같은 TX로 커밋. 같은 키·같은 요청은 저장된 결과 반환; 같은 키·다른 해시의 요청은 거절. 실패 시 해당 TX 전체 롤백. 재시도 ID는 클라이언트가 재전송 중 유지하며 서버가 인증한 actor에 범위를 묶는다.

### 3.2 `money_accounts` [A] [W] - 현금·예금·회사·시스템 계좌, 가변

필드: `account_id ID`, `owner_actor_id ID?`, `owner_company_id ID?`, `system_code CODE?`, `account_kind CODE`, `balance N=0`, `allow_negative BOOL=0`, `status CODE=active`.

PK(account_id); FK(owner_actor_id)->actors(actor_id); FK(owner_company_id)->companies(company_id); UQ(owner_actor_id,account_kind); UQ(owner_company_id,account_kind); UQ(system_code). CK: 세 owner 필드 중 정확히 하나만 NOT NULL; kind IN(cash,bank,company,system); cash/bank이면 actor만, company이면 company만, system이면 system_code만 사용; status IN(active,frozen,closed); allow_negative=0이면 balance>=0; allow_negative=1은 system 계좌만 허용.

NPC도 actor 계좌를 가질 수 있다. 현금/예금 한 개씩을 가정한다. 대출은 별도 부채이며 현금 계좌 음수로 표현하지 않는다. 은퇴/탈퇴 후에도 금융 이력을 보존한다.

### 3.3 `money_transactions` [A] [W] - 거래 헤더, 불변

필드: `transaction_id ID`, `operation_id ID`, `reason_code CODE`, `game_minute N`, `reverses_transaction_id ID?`, `description S(255)?`.

PK(transaction_id); FK(operation_id)->game_operations(operation_id); FK(reverses_transaction_id)->money_transactions(transaction_id); UQ(reverses_transaction_id). CK: game_minute>=0, 역거래가 자기 자신을 참조하지 않음. 한 operation에 여러 transaction 허용. UQ는 '전액 취소 1회' 모델이며 부분 환불은 별도 신규 거래로 기록. reason_code는 기존 MoneyChangeReason 0~11과 명시적으로 매핑.

### 3.4 `money_entries` [A] [W] - 계좌별 변동, 불변

필드: `transaction_id ID`, `line_no N`, `account_id ID`, `amount_delta N`, `balance_after N`.

PK(transaction_id,line_no); FK(transaction_id)->money_transactions(transaction_id); FK(account_id)->money_accounts(account_id); IX(account_id,created_at). CK: line_no>0, amount_delta<>0.

TX: 거래당 2행 이상, SUM(amount_delta)=0, balance_after가 직전 잔액+delta와 일치, 비음수 계좌 잔액>=0. 체크는 서비스가 대상 계좌를 잠근 뒤 수행한다. 입금=현금 -1000/예금 +1000; 퀘스트 보상=시스템 발행계좌 -1000/현금 +1000. 이 불변식은 일반 CHECK/FK로 강제되지 않는다. 계좌 잔액 캐시는 매번 동일 TX에서 갱신하고 정기 대사한다.

### 3.5 `bank_loans` [B] [W] - 대출 계약, 가변

필드: `loan_id ID`, `character_id ID`, `principal N`, `principal_remaining N`, `accrued_interest N=0`, `interest_rate_ppm N`, `interest_period_minutes N`, `opened_game_minute N`, `due_game_minute N`, `last_accrued_game_minute N`, `disbursement_transaction_id ID`, `status CODE`.

PK(loan_id); FK(character_id)->characters(character_id); FK(disbursement_transaction_id)->money_transactions(transaction_id); UQ(disbursement_transaction_id). CK: principal>0, 0<=principal_remaining<=principal, accrued_interest>=0, rate>=0, period>0, 0<=opened<=last_accrued<=due, status IN(active,repaid,defaulted). 만기 이후 연체이자는 별도 정책 확정 전 자동 적용하지 않는다. TX: 신용도/한도/동시 대출/이자 계산과 입금 처리.

### 3.6 `loan_payments` [B] [W] - 대출 상환, 불변

필드: `payment_id ID`, `loan_id ID`, `transaction_id ID`, `principal_amount N`, `interest_amount N`, `game_minute N`.

PK(payment_id); FK(loan_id)->bank_loans(loan_id); FK(transaction_id)->money_transactions(transaction_id); UQ(transaction_id). CK: 두 금액>=0, 합계>0, game_minute>=0. TX: 상환액을 남은 원금·이자에서 차감.

### 3.7 `bank_savings` [B] [W] - 적금 계약, 가변

필드: `savings_id ID`, `character_id ID`, `principal_balance N=0`, `installment_amount N`, `interest_rate_ppm N=100000`, `opened_game_minute N`, `maturity_game_minute N`, `next_payment_game_minute N?`, `payment_period_minutes N`, `status CODE`, `payout_transaction_id ID?`.

PK(savings_id); FK(character_id)->characters(character_id); FK(payout_transaction_id)->money_transactions(transaction_id); UQ(payout_transaction_id). CK: principal_balance>=0, installment>0, rate>=0, 0<=opened<maturity, period>0, status IN(active,matured,withdrawn_early,paid). TX: 10%는 **만기 전체 수익률 제안**이며 연이율로 임의 환산하지 않는다. 중도해지는 원금만 지급하고 payout 1회. 원금은 현금/예금에 중복 포함하지 않는 계약자산.

### 3.8 `savings_payments` [B] [W] - 적금 납입, 불변

필드: `savings_id ID`, `installment_no N`, `amount N`, `transaction_id ID`, `game_minute N`.

PK(savings_id,installment_no); FK(savings_id)->bank_savings(savings_id); FK(transaction_id)->money_transactions(transaction_id); UQ(transaction_id). CK: installment_no>0, amount>0, game_minute>=0. 납입금은 지정 시스템 수탁계좌로 이체하고 principal_balance를 함께 갱신.

### 3.9 `bank_cards` [B] [W] - 게임 내 결제카드, 가변

필드: `card_id ID`, `character_id ID`, `linked_account_id ID`, `fee_rate_ppm N`, `issued_game_minute N`, `status CODE`.

PK(card_id); FK(character_id)->characters(character_id); FK(linked_account_id)->money_accounts(account_id). CK: fee_rate_ppm BETWEEN 0 AND 1000000, issued>=0, status IN(active,suspended,closed). TX: 연결 계좌가 본인 bank 계좌인지 확인. 기획에 신용 결제/청구 주기가 없어 우선 잔액 기반 결제 가정; 현실 카드번호 저장 없음.

### 3.10 `tax_assessments` [B] [W] - 계절별 세금 고지, 가변

필드: `assessment_id ID`, `character_id ID`, `season_no N`, `assessment_version N=1`, `assessed_amount N`, `paid_amount N=0`, `due_game_minute N`, `status CODE`.

PK(assessment_id); UQ(character_id,season_no,assessment_version); FK(character_id)->characters(character_id). CK: season_no>=0, version>0, assessed>=0, 0<=paid<=assessed, due>=0, status IN(open,paid,cancelled). season_no는 월드 시작 후 0부터 증가하는 계절 번호. TX: 같은 계절 고지의 재발행은 기존 고지 취소/차액 정책에 따라 처리하고 중복 청구하지 않음.

### 3.11 `tax_assessment_lines` [B] [W] - 과세 근거 스냅샷, 불변

필드: `assessment_id ID`, `line_no N`, `tax_kind CODE`, `land_plot_id ID?`, `company_id ID?`, `taxable_value N`, `tax_rate_ppm N`, `exemption_amount N=0`, `tax_amount N`, `reason_text S(255)`.

PK(assessment_id,line_no); FK(assessment_id)->tax_assessments(assessment_id); FK(land_plot_id)->land_plots(plot_id); FK(company_id)->companies(company_id). CK: line_no>0, 금액/세율>=0; kind=land이면 토지만, company이면 회사만, assets이면 둘 다 NULL. TX: 세율·면제·반올림 결과와 고지 합계 일치. 사업 등록된 회사가 실제 사용하는 부지 면제 범위는 기획 확정 필요.

### 3.12 `tax_payments` [B] [W] - 세금 납부, 불변

필드: `payment_id ID`, `assessment_id ID`, `transaction_id ID`, `amount N`, `game_minute N`.

PK(payment_id); FK(assessment_id)->tax_assessments(assessment_id); FK(transaction_id)->money_transactions(transaction_id); UQ(transaction_id). CK: amount>0, game_minute>=0. TX: 고지행을 잠그고 잔여 세금 초과 납부 방지, 원장/paid_amount 동시 갱신.

### 3.13 `donations` [B] [W] - 기부 실적, 불변

필드: `donation_id ID`, `character_id ID`, `amount N`, `credit_points_granted N`, `transaction_id ID`, `game_minute N`.

PK(donation_id); FK(character_id)->characters(character_id); FK(transaction_id)->money_transactions(transaction_id); UQ(transaction_id). CK: amount>0, credit_points_granted>=0, game_minute>=0. TX: 1만원당 1점 계산에서 잔액 이월 여부를 정하고 신용 이력과 동시에 저장. 수혜자 하위 10% 선정·분배는 EXTENSIONS의 복지 배분으로 기록.

## 4. 토지·회사·직원·아이템·쇼핑

### 4.1 `land_plots` [B] [W] - 토지 소유 상태, 가변

필드: `plot_id ID`, `map_id ID`, `plot_key S(100)`, `owner_character_id ID?`, `purchase_price N`, `assessed_value N`, `business_use_company_id ID?`, `status CODE`.

PK(plot_id); UQ(map_id,plot_key); FK(map_id)->map_definitions(map_id); FK(owner_character_id)->characters(character_id); FK(business_use_company_id)->companies(company_id). CK: 두 금액>=0, status IN(available,owned,seized). TX: 구매할 토지/지갑 잠금, owned이면 소유자 존재, 사업용 부지와 회사의 소유권 일치. map+plot_key는 Unity 배치 데이터와 연결하는 외부 키이며 맵 렌더링 데이터를 DB에 복제하지 않음.

### 4.2 `buildings` [B] [W] - 건물과 창고, 가변

필드: `building_id ID`, `plot_id ID`, `building_kind CODE`, `level N=0`, `floors_unlocked N=1`, `position_x R`, `position_y R`, `position_z R`, `yaw R`, `asset_key S(255)`, `state_json JSON`.

PK(building_id); FK(plot_id)->land_plots(plot_id). CK: level BETWEEN 0 AND 10, floors_unlocked>=1; kind IN(home,office,warehouse,ranch,shop,farm,other). 한 토지에 여러 건물 허용은 **제안**이며 1개로 결정되면 UQ(plot_id) 추가. TX: 건설비·건물 생성·업그레이드 동시 처리, 회사 레벨 하락에도 floors_unlocked 감소 금지.

### 4.3 `companies` [A/B] [W] - 회사 현재 상태, 가변

필드: `company_id ID`, `owner_character_id ID?`, `display_name S(100)`, `sector_id CODE`, `company_kind CODE`, `level N=0`, `base_level N=0`, `registered_game_minute N?`, `headquarters_building_id ID?`, `warehouse_building_id ID?`, `online_shop_open BOOL=0`, `reference_price N`, `legacy_product_price N`, `purchase_rate_ppm N=1000000`, `base_appeal_ppm N=1000000`, `consumer_count N=0`, `market_share_ppm N=0`, `status CODE`.

PK(company_id); FK(owner_character_id)->characters(character_id); FK(sector_id)->sector_definitions(sector_id); FK(headquarters_building_id)->buildings(building_id); FK(warehouse_building_id)->buildings(building_id); UQ(headquarters_building_id); UQ(warehouse_building_id).

CK: company_kind IN(npc,player), player이면 owner NOT NULL, npc이면 owner NULL; level/base_level BETWEEN 0 AND 10; registered NULL 또는 >=0; 가격>0, purchase_rate/base_appeal/consumer_count>=0, market_share_ppm BETWEEN 0 AND 1000000; status IN(active,closed,seized). TX: 본사/창고 소유 및 건물 종류 확인, 동일 건물을 서로 다른 역할로 중복 사용하지 않는지 검사. 벌점에 따른 등록 제한/강제매각, 온라인몰 해금 업종·레벨은 서비스 검증.

기존 CompanyState의 listed는 종목 존재 여부로 계산하고 별도 플래그를 두지 않는다. isPlayerOwned는 company_kind로 표현. legacy_product_price는 현재 회사당 단일 제품 가격 계산을 보존하는 과도기 필드이며 복수 상품 모델 이행 후 제거한다.

### 4.4 `company_growth` [A/B] [W] - 업종별 세부 성장, 가변

필드: `company_id ID`, `metric_code CODE`, `level N`, `measured_value R`, `evaluated_game_minute N`.

PK(company_id,metric_code); FK(company_id)->companies(company_id). CK: level BETWEEN 0 AND 10, evaluated>=0. TX: ruleset에서 업종별 허용 metric/개수/임계값 검증. 기존 production/quality/marketing을 각각 보존; 새 기획의 profitability/scale/distribution/performance와 다른 항목으로 취급. measured_value는 측정 통계이며 재화 원본이 아니다. 수익성은 순이익이므로 음수 가능.

### 4.5 `company_daily_records` [A/B] [W] - 일별 경영 지표, 불변 일마감

필드: `company_id ID`, `absolute_day N`, `revenue N`, `operating_cost N`, `wage_cost N`, `tax_cost N`, `net_profit N`, `units_sold N`, `demand_consumers N`, `served_consumers N`, `capacity_consumers N`, `market_share_ppm N`, `operation_id ID`.

PK(company_id,absolute_day); FK(company_id)->companies(company_id); FK(operation_id)->game_operations(operation_id). CK: day>=0; net_profit 외 금액·수량>=0; net_profit=revenue-operating_cost-wage_cost-tax_cost; served<=demand AND served<=capacity; share BETWEEN 0 AND 1000000. TX: 비용 분류 간 이중 계산 방지, 같은 날짜에 일마감 1회. 아직 비용 미구현인 세이브는 0을 '실제 비용 0'으로 오인하지 않도록 ruleset/마이그레이션 이력에 명시. 최소 30일 평균 계산 및 시상식용 연간 집계를 보존하고 현재 코드의 30행 제한을 그대로 데이터 삭제 정책으로 사용하지 않음.

### 4.6 `recruitment_batches` [B] [W] - 채용공고 1회, 가변

필드: `batch_id ID`, `company_id ID`, `recruitment_kind CODE`, `fee N`, `transaction_id ID`, `opened_game_minute N`, `status CODE`.

PK(batch_id); UQ(batch_id,company_id); FK(company_id)->companies(company_id); FK(transaction_id)->money_transactions(transaction_id); UQ(transaction_id). CK: kind IN(normal,premium), fee>=0, opened>=0, status IN(open,selected,discarded). TX: 일반 5000원/고급 100000원은 ruleset 값으로 검증, 최초 공고는 본사 데스크 상호작용만 허용.

### 4.7 `recruitment_candidates` [B] [W] - 후보 3명, 가변

필드: `batch_id ID`, `candidate_no N`, `npc_actor_id ID`, `grade_id ID`, `status CODE`.

PK(batch_id,candidate_no); UQ(batch_id,npc_actor_id); FK(batch_id)->recruitment_batches(batch_id); FK(npc_actor_id)->npc_states(npc_actor_id); FK(grade_id)->employee_grade_definitions(grade_id). CK: candidate_no BETWEEN 1 AND 3; status IN(offered,selected,rejected). TX: 공고 생성 시 정확히 3명, 선택은 0명 또는 1명만 허용; 같은 공고에 대한 선택을 잠금으로 직렬화. 생김새·이름·나이·성별·능력 설명은 actors/npc_states에 보존.

### 4.8 `company_employees` [B] [W] - 현직 직원, 가변

필드: `npc_actor_id ID`, `company_id ID`, `grade_id ID`, `source_batch_id ID?`, `is_special BOOL=0`, `role_code CODE`, `assigned_building_id ID?`, `assigned_recipe_id ID?`, `hired_game_minute N`, `salary N`, `remaining_event_shields N=0`.

PK(npc_actor_id); FK(npc_actor_id)->npc_states(npc_actor_id); FK(company_id)->companies(company_id); FK(grade_id)->employee_grade_definitions(grade_id); FK(source_batch_id,npc_actor_id)->recruitment_candidates(batch_id,npc_actor_id); FK(source_batch_id,company_id)->recruitment_batches(batch_id,company_id); FK(assigned_building_id)->buildings(building_id); FK(assigned_recipe_id)->recipe_definitions(recipe_id).

CK: hired/salary/shields>=0. TX: actor당 현직 1개(PK), 채용 한도/등급 급여/공고 선택 검증. 스페셜 직원 해고 금지; 배치 건물은 소속 회사 건물; 제조 직원당 assigned_recipe_id 하나만. 해고는 현직행 제거와 이력 기록을 한 TX로 처리. 기획상 레벨 하락으로 정원을 초과하면 해고해야 한다. 해고 대상을 사용자가 정할 수 있도록 초과 상태 자체는 저장 가능하게 하고 추가 채용은 금지한다. 정리 기한/자동 해고 여부는 미정이며, 상시 초과 고용을 허용하는 정책으로 해석하지 않는다.

### 4.9 `employee_history` [B] [W] - 고용 변경 이력, 불변

필드: `history_id ID`, `npc_actor_id ID`, `company_id ID`, `event_kind CODE`, `game_minute N`, `operation_id ID`, `snapshot_json JSON`.

PK(history_id); FK(npc_actor_id)->npc_states(npc_actor_id); FK(company_id)->companies(company_id); FK(operation_id)->game_operations(operation_id). CK: event_kind IN(hired,fired,assigned,promoted), game_minute>=0. 이력은 현직행이 아닌 NPC와 회사를 참조해 해고 후에도 유지.

### 4.10 `inventory_containers` [A/B] [W] - 가방·창고·택배 보관함, 가변

필드: `container_id ID`, `owner_character_id ID?`, `owner_company_id ID?`, `container_kind CODE`, `capacity_slots N`, `building_id ID?`.

PK(container_id); FK(owner_character_id)->characters(character_id); FK(owner_company_id)->companies(company_id); FK(building_id)->buildings(building_id). CK: owner 둘 중 정확히 하나 NOT NULL, capacity_slots>0, kind IN(bag,warehouse,mailbox,display). TX: 개인 가방/우편함은 character 소유, 기업 창고는 company 소유; 같은 종류 여러 보관함 허용. building과 보관함 소유 일치.

### 4.11 `inventory_items` [A/B] [W] - 아이템 스택/개체, 가변

필드: `item_instance_id ID`, `container_id ID`, `slot_no N`, `item_id ID`, `quantity N`, `durability R?`, `bound_character_id ID?`, `instance_state_json JSON`.

PK(item_instance_id); UQ(container_id,slot_no); FK(container_id)->inventory_containers(container_id); FK(item_id)->item_definitions(item_id); FK(bound_character_id)->characters(character_id). CK: slot_no>=0, quantity>0, durability NULL 또는 >=0. TX: slot_no<capacity, quantity<=item.max_stack; 귀속/거래 가능 여부, 아이템 이동/분할/합치기 검증. 장비·트로피는 max_stack=1. 슬롯 번호는 0부터 시작.

### 4.12 `character_equipment` [B] [W] - 장착 상태, 가변

필드: `character_id ID`, `equipment_slot CODE`, `item_instance_id ID`.

PK(character_id,equipment_slot); UQ(item_instance_id); FK(character_id)->characters(character_id); FK(item_instance_id)->inventory_items(item_instance_id). TX: 아이템이 본인 보관함에 있고 슬롯 종류가 호환되는지 검증. 장착을 유지한 채 판매/양도 금지 또는 원자적 해제.

### 4.13 `shop_listings` [A/B] [W] - 상점의 판매 등록, 가변

필드: `listing_id ID`, `seller_company_id ID`, `item_id ID`, `channel CODE`, `unit_price N`, `stock_quantity N`, `reserved_quantity N=0`, `status CODE`.

PK(listing_id); FK(seller_company_id)->companies(company_id); FK(item_id)->item_definitions(item_id); UQ(seller_company_id,item_id,channel). CK: price>0, 0<=reserved<=stock_quantity, channel IN(offline,online,wholesale), status IN(active,paused,closed). 판매 가능량은 stock_quantity-reserved_quantity로 계산한다. TX: 회사 레벨/채널 해금, 허용 가격, 실제 창고 재고/예약 수량 일치. 물류회사의 동일 상품은 기획의 '직접판매 또는 온라인 중 택 1'에 따라 두 소매 채널을 동시에 active로 만들지 않는다. 현재 ShopApplication의 고정 5개 상품은 NPC 소매회사 판매 등록으로 시드.

### 4.14 `shop_orders` [B] [W] - 플레이어 구매 주문, 가변

필드: `order_id ID`, `buyer_character_id ID`, `seller_company_id ID`, `operation_id ID`, `total_amount N`, `shipping_fee N=0`, `ordered_game_minute N`, `status CODE`, `payment_transaction_id ID`, `destination_building_id ID?`.

PK(order_id); FK(buyer_character_id)->characters(character_id); FK(seller_company_id)->companies(company_id); FK(operation_id)->game_operations(operation_id); FK(payment_transaction_id)->money_transactions(transaction_id); FK(destination_building_id)->buildings(building_id); UQ(operation_id); UQ(payment_transaction_id). CK: total>0, shipping>=0, ordered>=0; status IN(paid,packed,delivered,claimed,cancelled). 한 주문은 판매자 1곳이라는 제안. TX: 주문 합계/돈 차감/재고 예약 동시 처리, 배송지 소유권/접근권 확인.

### 4.15 `shop_order_lines` [B] [W] - 주문 당시 가격과 수량, 불변

필드: `order_id ID`, `line_no N`, `listing_id ID`, `item_id ID`, `quantity N`, `unit_price N`, `line_total N`.

PK(order_id,line_no); FK(order_id)->shop_orders(order_id); FK(listing_id)->shop_listings(listing_id); FK(item_id)->item_definitions(item_id). CK: line_no/quantity/unit_price>0, line_total>0. TX: line_total=quantity×unit_price를 오버플로 없이 검사, 판매자/아이템이 listing 및 주문과 일치. 이후 가격 변경과 무관하게 이 스냅샷 유지.

### 4.16 `deliveries` [B] [W] - 다음날 배송/수령/절도, 가변

필드: `delivery_id ID`, `order_id ID`, `destination_building_id ID`, `due_game_minute N`, `delivered_game_minute N?`, `claimed_by_actor_id ID?`, `claimed_game_minute N?`, `claim_operation_id ID?`, `status CODE`.

PK(delivery_id); UQ(order_id); UQ(claim_operation_id); FK(order_id)->shop_orders(order_id); FK(destination_building_id)->buildings(building_id); FK(claimed_by_actor_id)->actors(actor_id); FK(claim_operation_id)->game_operations(operation_id). CK: due>=0, delivered NULL 또는 >=due, claimed NULL 또는 (delivered NOT NULL AND claimed>=delivered); status IN(scheduled,delivered,claimed,stolen,cancelled); delivered/claimed/stolen 상태이면 delivered 필수; scheduled이면 delivered NULL; claimed/stolen이면 claimed_by/claimed/claim_operation 필수, 그 외에는 셋 모두 NULL.

TX: 다음날 배송 시각은 ruleset에서 확정. 기본은 주문 아이템을 주문/예약 상태로만 보존하고 수령 또는 절도 시 실제 인벤토리 개체를 생성한다. claim 상태 전이·아이템 지급·범죄 로그를 한 TX로 처리해 두 사람이 같은 택배를 중복 수령하지 못하게 한다. 분할 배송은 초기 범위에서 제외.

### 4.17 `supply_orders` [B] [W] - 제조→물류 납품 제안, 가변

필드: `supply_order_id ID`, `supplier_company_id ID`, `buyer_company_id ID`, `item_id ID`, `quantity N`, `unit_price N`, `status CODE`, `offered_game_minute N`, `resolved_game_minute N?`, `settlement_transaction_id ID?`.

PK(supply_order_id); FK(supplier_company_id)->companies(company_id); FK(buyer_company_id)->companies(company_id); FK(item_id)->item_definitions(item_id); FK(settlement_transaction_id)->money_transactions(transaction_id); UQ(settlement_transaction_id). CK: supplier<>buyer, quantity/price>0, offered>=0, resolved NULL 또는 >=offered, status IN(offered,accepted,rejected,fulfilled,cancelled). 초기에는 납품서 1장=상품 1종. TX: 승낙/실물 이동/창고 용량/대금 처리와 권한 확인. 외상·부분 납품은 후속 확장.

## 5. 주식·시세·경제 이벤트·뉴스

### 5.1 `stock_listings` [A] [W] - 상장 종목, 가변(상장 정보 보존)

필드: `stock_id ID`, `company_id ID`, `display_name S(100)`, `initial_price N`, `hourly_volatility_ppm N`, `listed_game_minute N`, `status CODE`.

PK(stock_id); UQ(company_id); FK(company_id)->companies(company_id). CK: initial_price>0, volatility BETWEEN 0 AND 1000000, listed>=0, status IN(listed,suspended,delisted). TX: 회사 레벨>=ruleset IPO 최소레벨 및 업종 can_list 확인. NPC 기본 상장기업은 월드 초기화 시드로 생성. 종목 업종은 company.sector_id로 조회해 중복 저장하지 않음.

### 5.2 `stock_states` [A] [W] - 종목 현재값, 가변

필드: `stock_id ID`, `current_price N`, `previous_close N`, `day_open N`, `day_high N`, `day_low N`, `last_session_change_ppm N`, `last_tick_game_minute N`, `session_day N`.

PK(stock_id); FK(stock_id)->stock_listings(stock_id). CK: 가격 5개>0, day_high>=day_low, day_low<=current_price<=day_high, day_low<=day_open<=day_high, last_tick/session_day>=0.

TX: 16시 마지막 틱과 종가 확정을 함께 처리. previous_close를 언제 넘길지(당일 마감/다음날 개장) 코드와 통일하고 뉴스는 확정된 직전 세션 변화율을 사용. UI의 change/changeRate는 현재가격과 기준 종가로 계산하므로 별도 저장하지 않음.

### 5.3 `stock_price_ticks` [A] [W] - 시간별 시세 이력, 불변

필드: `stock_id ID`, `game_minute N`, `price N`, `operation_id ID`.

PK(stock_id,game_minute); FK(stock_id)->stock_listings(stock_id); FK(operation_id)->game_operations(operation_id). CK: game_minute>=0, price>0. 현재 코드 시간당 1회 갱신 기준. 분당/틱당 여러 값이 필요해지면 sequence 추가. 조회 IX는 PK로 충족. 240개 화면 표시와 영구 보존 정책은 분리.

### 5.4 `stock_holdings` [A] [W] - 캐릭터별 보유량과 취득원가, 가변

필드: `character_id ID`, `stock_id ID`, `quantity N`, `total_cost N`.

PK(character_id,stock_id); FK(character_id)->characters(character_id); FK(stock_id)->stock_listings(stock_id). CK: quantity>0, total_cost>=0. 0주가 되면 행 삭제. 평균단가=total_cost/quantity, 평가액=quantity×현재가로 계산. 매수 수수료를 원가에 포함하는 현 코드 정책 유지.

### 5.5 `stock_trades` [A] [W] - 성공 체결, 불변

필드: `trade_id ID`, `operation_id ID`, `character_id ID`, `stock_id ID`, `side CODE`, `quantity N`, `unit_price N`, `gross_amount N`, `fee N`, `settlement_amount N`, `removed_cost_basis N=0`, `realized_profit N=0`, `transaction_id ID`, `game_minute N`.

PK(trade_id); UQ(operation_id); UQ(transaction_id); FK(operation_id)->game_operations(operation_id); FK(character_id)->characters(character_id); FK(stock_id)->stock_listings(stock_id); FK(transaction_id)->money_transactions(transaction_id); IX(character_id,game_minute); IX(stock_id,game_minute).

CK: side IN(buy,sell); quantity/unit_price/gross>0; 0<=fee<=gross; settlement/removed_cost>=0; game_minute>=0. TX: 장 시간 09:00<=시각<16:00, 상장상태/주문한도/잔액/보유량 확인. gross=단가×수량, buy 정산=gross+fee, sell 정산=gross-fee, sell 실현손익=정산-제거원가. 모든 곱셈/합계의 64비트 범위 확인. 현금·주식·원장·체결·operation을 한 TX로 변경. 실패 주문은 성공 체결 테이블에 넣지 않고 필요하면 서버 감사로그에 남김.

현재 구조는 NPC 시뮬레이션 거래소가 상대방이고 다른 플레이어의 호가와 매칭하지 않는다. 온라인 진입 시에도 이 모델을 가정한다. 지정가/유한 발행주식/거래 상대방 매칭이 필요하면 주문·체결 할당·예약 잔액을 추가해야 한다.

### 5.6 `market_event_instances` [A] [W] - 이벤트 실제 발생, 불변

필드: `event_instance_id ID`, `event_definition_id ID`, `start_day N`, `end_day N`, `operation_id ID`.

PK(event_instance_id); UQ(event_definition_id,start_day); FK(event_definition_id)->market_event_definitions(event_definition_id); FK(operation_id)->game_operations(operation_id); IX(end_day).

CK: 0<=start_day<end_day. 활성 여부는 start_day<=today<end_day로 계산. TX: 현재 코드처럼 같은 이벤트 중첩을 막으려면 발생 결정 시 월드/스케줄러 잠금 아래 겹치는 기간 검사. 단순 UQ는 기간 중첩을 막지 못함. 불변 event_definition을 참조하므로 지난 효과도 복원 가능.

### 5.7 `news_articles` [A] [W] - 08시 뉴스, 불변

필드: `news_id ID`, `news_kind CODE`, `published_game_minute N`, `headline S(500)`, `body TEXT?`, `stock_id ID?`, `event_instance_id ID?`, `change_rate_ppm N?`, `operation_id ID`.

PK(news_id); FK(stock_id)->stock_listings(stock_id); FK(event_instance_id)->market_event_instances(event_instance_id); FK(operation_id)->game_operations(operation_id); IX(published_game_minute); UQ(published_game_minute,stock_id); UQ(published_game_minute,event_instance_id).

CK: published>=0; stock 뉴스는 stock만, event 뉴스는 event만, system 뉴스는 둘 다 NULL; news_kind IN(stock,event,system). TX: 하루 발행 작업 키로 재발행 방지, 서버가 본문 생성. 과거 뉴스 제목과 당시 변동률은 현재 시세에서 재생성하지 않는다.

## 6. SNS·DM·메모·신고

### 6.1 `sns_posts` [A] [W] - 게시물, 가변(삭제 상태 포함)

필드: `post_id ID`, `author_actor_id ID`, `post_kind CODE`, `body S(140)`, `posted_game_minute N`, `news_id ID?`, `status CODE=visible`, `operation_id ID`.

PK(post_id); FK(author_actor_id)->actors(actor_id); FK(news_id)->news_articles(news_id); FK(operation_id)->game_operations(operation_id); UQ(operation_id); UQ(news_id); IX(posted_game_minute,post_id); IX(author_actor_id,posted_game_minute).

CK: post_kind IN(player,npc,news,system); body 비어 있지 않고 140자 이하; posted>=0; news이면 news_id NOT NULL, 다른 kind는 NULL; status IN(visible,deleted). TX: 작성자 종류와 kind 일치, 입력 trim, 쿨다운 10분과 일 최대 3개를 별도 적용. 일일 제한은 actor를 잠근 뒤 해당 게임일의 player 게시물 수를 확인하고 soft-deleted 글도 횟수에 포함. 자동 뉴스에는 플레이어 횟수 제한 미적용. 각 뉴스 자동 게시별 operation을 발급하거나 일괄 게시 시 공통 operation UQ 정책을 조정.

### 6.2 `sns_likes` [A] [W] - 좋아요, 연결 행

필드: `post_id ID`, `actor_id ID`, `liked_game_minute N`.

PK(post_id,actor_id); FK(post_id)->sns_posts(post_id); FK(actor_id)->actors(actor_id); IX(actor_id). CK: liked>=0. likes 카운트는 COUNT(*)로 계산. API는 재시도 시 반전되는 Toggle 대신 `liked=true/false` 목표 상태 명령을 권장.

### 6.3 `sns_follows` [B] [W] - 팔로우, 연결 행

필드: `follower_actor_id ID`, `followee_actor_id ID`, `followed_game_minute N`.

PK(follower_actor_id,followee_actor_id); FK(follower_actor_id)->actors(actor_id); FK(followee_actor_id)->actors(actor_id); IX(followee_actor_id). CK: follower<>followee, followed>=0. 블록/친구 관계는 별도 기능으로 확정 후 추가.

### 6.4 `chat_conversations` [B] [W] - 채널/DM 대화방, 가변

필드: `conversation_id ID`, `conversation_kind CODE`, `channel_code CODE?`, `dm_actor_low_id ID?`, `dm_actor_high_id ID?`.

PK(conversation_id); FK(dm_actor_low_id)->actors(actor_id); FK(dm_actor_high_id)->actors(actor_id); UQ(channel_code); UQ(dm_actor_low_id,dm_actor_high_id). CK: kind IN(world,system,dm); dm은 두 actor NOT NULL, ASCII 이진 정렬로 low<high, channel NULL; world/system은 channel NOT NULL, 두 actor NULL.

DM의 정렬된 두 actor UQ로 같은 두 사람의 방 중복 생성 방지. 날짜가 아니라 created_at으로 생성 순서 표시. 월드 채팅과 개인 사건 로그의 공개 범위를 혼용하지 않는다.

### 6.5 `chat_members` [B] [W] - 대화방 참여, 가변

필드: `conversation_id ID`, `actor_id ID`, `joined_game_minute N`, `last_read_message_id ID?`, `left_game_minute N?`.

PK(conversation_id,actor_id); FK(conversation_id)->chat_conversations(conversation_id); FK(actor_id)->actors(actor_id); FK(conversation_id,last_read_message_id)->chat_messages(conversation_id,message_id). CK: joined>=0, left NULL 또는 >=joined. TX: DM에는 정해진 두 actor만 가입 가능, 조회/전송 권한 확인. 메시지를 먼저 만든 뒤 read 포인터 갱신해 순환 FK 삽입 문제 회피.

### 6.6 `chat_messages` [B] [W] - 채팅/활동 로그, 불변

필드: `message_id ID`, `conversation_id ID`, `sender_actor_id ID`, `message_kind CODE`, `body S(2000)`, `sent_game_minute N`, `operation_id ID`.

PK(message_id); UQ(conversation_id,message_id); UQ(operation_id); FK(conversation_id)->chat_conversations(conversation_id); FK(sender_actor_id)->actors(actor_id); FK(operation_id)->game_operations(operation_id); IX(conversation_id,created_at,message_id).

CK: kind IN(chat,activity), body 비어 있지 않음, sent>=0. TX: DM 전송자는 멤버 또는 허가된 system actor. 피해자만 보는 사건 로그는 해당 개인의 system 채널/참여자로 제한. 기록 보존 기간 및 개인정보 삭제는 서버 정책으로 정한다.

### 6.7 `actor_notes` [B] [W] - 플레이어/NPC 개인 메모, 가변

필드: `note_id ID`, `owner_character_id ID`, `target_actor_id ID`, `body TEXT`.

PK(note_id); UQ(owner_character_id,target_actor_id); FK(owner_character_id)->characters(character_id); FK(target_actor_id)->actors(actor_id). TX: 작성자 본인만 읽기/쓰기. 초기에는 대상당 메모 1개, 길이 상한은 API 설정. 온라인 계정 간 공개 없음.

### 6.8 `report_evidence` [A/B] [W] - 신고 증거 파일의 메타데이터, 불변

필드: `evidence_id ID`, `uploaded_by_actor_id ID`, `storage_key S(512)`, `content_sha256 S(64)`, `mime_type S(100)`, `byte_size N`, `captured_game_minute N`, `linked_message_id ID?`.

PK(evidence_id); FK(uploaded_by_actor_id)->actors(actor_id); FK(linked_message_id)->chat_messages(message_id). CK: byte_size>0, captured>=0, sha 길이=64. 이미지는 SQLite 옆 파일/서버 객체 저장소에 보관하고 DB에는 키와 해시만 저장. TX: 업로드 완료·파일 존재·권한 확인 후 신고 허용. 같은 이미지의 다중 제출 정책은 서비스 규칙이며 해시만으로 원본성을 보장하지 않음.

### 6.9 `crime_reports` [A] [W] - 신고 접수, 가변

필드: `report_id ID`, `reporter_character_id ID`, `suspect_actor_id ID`, `crime_rule_id ID`, `evidence_id ID`, `stolen_value N=0`, `map_id ID?`, `position_x R?`, `position_y R?`, `position_z R?`, `submitted_game_minute N`, `resolved_game_minute N?`, `status CODE=pending`, `operation_id ID`.

PK(report_id); FK(reporter_character_id)->characters(character_id); FK(suspect_actor_id)->actors(actor_id); FK(crime_rule_id)->crime_rules(crime_rule_id); FK(evidence_id)->report_evidence(evidence_id); FK(map_id)->map_definitions(map_id); FK(operation_id)->game_operations(operation_id); UQ(operation_id); IX(suspect_actor_id,submitted_game_minute); IX(reporter_character_id,submitted_game_minute).

CK: reporter<>suspect, stolen>=0, submitted>=0; status IN(pending,convicted,dismissed); pending이면 resolved NULL, 나머지는 resolved NOT NULL AND resolved>=submitted; 좌표는 세 필드 모두 NULL 또는 NOT NULL, 좌표가 있으면 map NOT NULL. TX: 피신고자 실재/종류, 신고 쿨다운, 증거 사용 권한, 사건 중복 판정 확인. 폭행/살인처럼 플레이어 대상에 한정된 기획은 actor 종류를 검사한다.

### 6.10 `report_verdicts` [A] [W] - 확정 판결, 불변

필드: `report_id ID`, `guilty BOOL`, `penalty_points N`, `fine N`, `jail_minutes N`, `settlement_allowed BOOL`, `credit_delta N`, `reason TEXT?`, `judge_kind CODE`, `resolved_game_minute N`, `operation_id ID`.

PK(report_id); FK(report_id)->crime_reports(report_id); FK(operation_id)->game_operations(operation_id); UQ(operation_id). CK: penalty/fine/jail/resolved>=0; judge_kind IN(test,server,ai,manual); guilty=0이면 penalty=fine=jail=credit_delta=0. TX: 신고 pending→최종상태 전이, 판결 1회, 벌점/신용 변경·처벌 생성 함께 수행. AI 결과는 서버가 검증하고 정규화한다. 현재 EvidenceRequiredJudge는 test로만 취급.

### 6.11 `actor_legal_states` [A/B] [W] - 벌점·신용도 현재 합계, 가변

필드: `actor_id ID`, `penalty_points N=0`, `credit_score N=0`, `donation_credit_remainder N=0`.

PK(actor_id); FK(actor_id)->actors(actor_id). CK: penalty_points>=0, donation_credit_remainder BETWEEN 0 AND 9999. credit_score 범위는 기획 확정 전 제한하지 않음. donation remainder는 누적 기부 잔액 이월을 채택할 때만 사용(제안). TX: 합계는 아래 이력과 함께 갱신/대사.

### 6.12 `legal_state_changes` [A/B] [W] - 벌점/신용 증감 이력, 불변

필드: `change_id ID`, `actor_id ID`, `penalty_delta N=0`, `credit_delta N=0`, `reason_code CODE`, `report_id ID?`, `donation_id ID?`, `operation_id ID`, `game_minute N`.

PK(change_id); FK(actor_id)->actors(actor_id); FK(report_id)->report_verdicts(report_id); FK(donation_id)->donations(donation_id); FK(operation_id)->game_operations(operation_id); UQ(report_id); UQ(donation_id); IX(actor_id,game_minute).

CK: penalty_delta<>0 OR credit_delta<>0; game_minute>=0; reason IN(report,donation,cleansing,adjustment); reason=report면 report만 NOT NULL, donation이면 donation만 NOT NULL, cleansing/adjustment이면 둘 다 NULL. TX: 대상이 판결 피신고자/기부자와 일치, 벌점 음수 방지. 무죄 또는 1만원 미만 기부 잔여액 누적처럼 점수 변동이 0이면 change 행은 만들지 않고 사건/기부 원본만 보존한다. 정정은 과거 이력 편집 대신 새 change를 추가하되 별도 이유/권한을 요구한다.

### 6.13 `sanctions` [B] [W] - 벌금·수감·합의 실행, 가변

필드: `sanction_id ID`, `report_id ID`, `actor_id ID`, `sanction_kind CODE`, `fine_amount N=0`, `jail_minutes N=0`, `started_game_minute N?`, `ends_game_minute N?`, `status CODE`, `settlement_with_character_id ID?`, `transaction_id ID?`.

PK(sanction_id); UQ(report_id); UQ(transaction_id); FK(report_id)->report_verdicts(report_id); FK(actor_id)->actors(actor_id); FK(settlement_with_character_id)->characters(character_id); FK(transaction_id)->money_transactions(transaction_id).

CK: kind IN(fine,jail,settlement,none); 금액/기간>=0; 시작/종료는 둘 다 NULL 또는 0<=start<=end; status IN(pending,active,completed,cancelled); fine/settlement는 jail_minutes=0, jail은 fine_amount=0, none은 둘 다 0. TX: 유죄 판결의 동일 피신고자, 선택 가능한 처벌인지 확인. PDF의 '감옥/벌금/합의금 중 1'을 1건당 하나로 제안. 벌점/신용 변경은 처벌 선택과 별도로 항상 처리. 전재산 몰수/회사 매각은 별도 operation으로 소유권과 계좌까지 원자적으로 처리해야 한다.

## 7. 공통 트랜잭션과 저장 계약

| 명령 | 같이 저장할 대상 | 재시도/동시성 기준 |
|---|---|---|
| 은행 입출금 | 현금계좌, 예금계좌, 거래, 원장 2행, operation | 본인 계좌 검증; 고정 account_id 순서로 잠금; 잔액 음수 거절 |
| 주식 매매 | 현금, 보유주식, 체결, 수수료 원장, operation | 요청 키 UQ; 장 상태와 가격을 동일 시점에서 확인; 서버가 단가 결정 |
| 쇼핑 | 결제, 재고 예약, 주문/라인, 배송, operation | listing 재고 잠금; 구매 가격은 서버 계산; 전부 실패 또는 전부 성공 |
| 택배 수령/절도 | 배송 상태, inventory_items, operation 및 사건 로그 | delivered 상태일 때만 조건부 갱신; 정확히 한 명이 획득 |
| 퀘스트/업적 보상 | 완료 상태, 계좌/아이템/칭호, reward_operation | 미지급 조건을 잠금/조건부 UPDATE로 확인 |
| 세금 | 고지 paid_amount, 납부, 계좌/원장, operation | 잔여액 확인 후 동일 TX |
| 뉴스/일마감 | 시세/회사 집계/뉴스, world_job_runs, RNG/clock 체크포인트 | (world,job,time) 중복 방지; 결과와 완료 표시 함께 커밋 |
| 신고 판결 | 신고, 판결, legal_state_changes, 합계, sanctions | pending일 때만 1회 전이; 다른 대상의 벌점 변경 금지 |

DB에 매 프레임 모든 값을 쓰지 않는다. 위치/시뮬레이션 상태는 체크포인트 단위로, 재화와 보상은 명령 성공 시 기록한다. 단, '돈만 먼저 커밋하고 시계·시세는 5분 전 상태'처럼 서로 다른 시점으로 복원되지 않도록 거래 직전 가격/게임시각 및 경제 시뮬레이션 체크포인트를 같은 일관성 경계에서 저장한다.

설정된 잔액 상한을 넘는 정수 연산, 음수 수량, 부정한 FK, 다른 월드 대상 요청을 거절한다. 클라이언트의 금액/보유량/현재시각은 온라인 권위값으로 사용하지 않는다. MySQL에서 게임 시뮬레이션은 DB 테이블을 매 프레임 읽는 방식이 아니라 서버 메모리 상태와 주기적/명령별 트랜잭션을 함께 사용하는 구조로 구현한다.

### 현재 C# 객체와의 연결

| 코드 모델/값 | 테이블/필드 | 변환 주의 |
|---|---|---|
| GameTime.TotalMinutes | worlds.game_minute | 달력 ruleset 별도. SimpleGameClock의 잔여 초와 RNG도 체크포인트에 포함 |
| PropertyManager.Money | money_accounts(kind=cash).balance | BankAccount와 합쳐 한 money 컬럼으로 만들지 않음 |
| BankAccountState.balance | money_accounts(kind=bank).balance | 입출금은 두 계좌의 이동 |
| CompanyState | companies + company_growth | ownerId는 characters FK, 고정 NPC 회사는 owner NULL |
| DailyCompanyRecord | company_daily_records | 코드에 없는 비용·순이익은 새 데이터이며 임의 추정 금지 |
| StockData / IPO 목록 | stock_listings | companyId FK, 고정 stock_* ID 보존 가능 |
| StockState | stock_states + stock_price_ticks | 기존 history는 시각 없는 List<long>; 이력 시각을 새로 기록하도록 수정 필요 |
| Holding | stock_holdings | 장부원가/수량을 정확한 정수로 보존 |
| TradeReceipt | stock_trades + money_transactions | trade_id, operation_id, 시각, buy/sell 구분 추가 |
| MarketEventDef/SectorShock | market_event_definitions/effects | `*`는 scope_kind=all로 변환 |
| ActiveMarketEvent | market_event_instances | event 정의 ID와 발생 ID를 분리 |
| NewsItem.relatedId | news_articles.stock_id/event_instance_id | 타입에 맞는 FK로 분리 |
| SnsPost / likedBy | sns_posts / sns_likes | post_1 같은 카운터는 월드 범위에서만 안전; 신규 UUID 권장 |
| CrimeReport / ReportVerdict | crime_reports / report_verdicts | evidenceId를 실제 증거 FK로 연결 |
| PenaltyLedger | actor_legal_states + legal_state_changes | 합계만 저장하면 벌점 원인/중복 판결 추적 불가 |
| lastPostMinutes / lastSubmitMinutes | sns_posts / crime_reports의 최신 시각 조회 | 재접속 후 쿨다운을 초기화하지 않음 |
| PhoneManager.CurrentApp / 버튼 이벤트 | DB에 저장하지 않음 | UI 상태. 마지막 탭 선호는 필요 시 로컬 설정 |

CaptureState의 얕은 복사 문제는 DB 스키마만으로 해결되지 않는다. 저장 도중 값이 바뀌지 않도록 게임 스레드에서 일관된 DTO를 만들고, 모든 관련 행을 한 저장 트랜잭션으로 커밋하는 어댑터가 필요하다.
