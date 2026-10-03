# 후속 콘텐츠 테이블 초안

기획안에는 있지만 현재 브랜치에 구현된 저장 모델이 거의 없는 영역이다. 아래 테이블은 `[C]`로 분류하며 해당 기능 개발 시 확정한다. [TABLES.md](TABLES.md)의 자료형, `?` NULL 표기, NOT NULL 기본값, `[W]`의 world_id 복합 PK/FK, 가변/불변 메타 필드, 기본 RESTRICT 정책을 그대로 적용한다. 양쪽 DB에서 같은 논리 테이블을 사용할 수 있다. 실제 온라인 플레이어 간 기능은 싱글 버전에서 비활성화한다.

## 1. 이동·차량

### 1.1 `transport_routes` [C] - 대중교통/택시 경로 정의

필드: `route_id ID`, `ruleset_id ID`, `transport_kind CODE`, `from_map_id ID`, `to_map_id ID`, `to_spawn_id ID`, `fare N`, `travel_game_minutes N`.

PK(route_id); FK(ruleset_id)->game_rulesets(ruleset_id); FK(from_map_id)->map_definitions(map_id); FK(to_map_id,to_spawn_id)->spawn_points(map_id,spawn_id). CK: kind IN(bus,subway,taxi), fare/travel>=0. TX: 이용가능 출발지 검사. 건물 앞 택시 목적지는 해당 건물 좌표로 보완; 택시가 대중교통보다 비싸야 한다는 비교는 규칙 검증.

### 1.2 `travel_records` [C] [W] - 이동 비용과 복구 지점, 불변

필드: `travel_id ID`, `character_id ID`, `route_id ID`, `destination_building_id ID?`, `fare N`, `departed_game_minute N`, `arrived_game_minute N`, `transaction_id ID?`, `operation_id ID`.

PK(travel_id); FK(character_id)->characters(character_id); FK(route_id)->transport_routes(route_id); FK(destination_building_id)->buildings(building_id); FK(transaction_id)->money_transactions(transaction_id); FK(operation_id)->game_operations(operation_id); UQ(operation_id); UQ(transaction_id). CK: fare>=0, 0<=departed<=arrived; fare>0이면 transaction 필수. TX: 차감/목적지 저장/이동 이력 동시 커밋. 렌더링용 씬 로드 실패는 커밋된 목적지 재시도 또는 명시적 환불 정책으로 처리.

### 1.3 `vehicles` [C] [W] - 소유 차량, 가변

필드: `vehicle_id ID`, `owner_character_id ID`, `vehicle_item_id ID`, `map_id ID`, `position_x R`, `position_y R`, `position_z R`, `yaw R`, `fuel_amount R`, `fuel_capacity R`, `status CODE`.

PK(vehicle_id); FK(owner_character_id)->characters(character_id); FK(vehicle_item_id)->item_definitions(item_id); FK(map_id)->map_definitions(map_id). CK: 0<=fuel_amount<=fuel_capacity, capacity>0, status IN(parked,driving,stored). TX: 주유 아이템 차감과 연료 증가 함께 저장. 차량 규격은 item_definitions.effects_json. 자차 운전/장식 범위는 최종 기획 확인.

## 2. 아르바이트·취업

### 2.1 `job_definitions` [C] - 일자리 정의

필드: `job_id ID`, `ruleset_id ID`, `job_kind CODE`, `display_name S(100)`, `reward_per_task N`, `season_salary N`, `required_intelligence N=0`.

PK(job_id); FK(ruleset_id)->game_rulesets(ruleset_id). CK: kind IN(part_time,employment), 세 금액/스탯>=0. 현 기획 편의점500/도서관1000/시청700원은 시드 데이터. 월급은 계절 5일 정산 설정.

### 2.2 `character_jobs` [C] [W] - 취업 계약, 가변

필드: `employment_id ID`, `character_id ID`, `job_id ID`, `employer_company_id ID?`, `started_game_minute N`, `ended_game_minute N?`, `salary_snapshot N`.

PK(employment_id); FK(character_id)->characters(character_id); FK(job_id)->job_definitions(job_id); FK(employer_company_id)->companies(company_id). CK: start>=0, end NULL 또는 >=start, salary>=0. TX: 정규 취업은 NPC 회사만 허용, 학업 기준 확인. 동시 취업 수는 기획 결정 전 서비스에서 하나로 제한하는 것을 제안.

### 2.3 `job_payouts` [C] [W] - 업무/월급 보수, 불변

필드: `payout_id ID`, `employment_id ID`, `work_key ID`, `amount N`, `transaction_id ID`, `game_minute N`.

PK(payout_id); UQ(employment_id,work_key); FK(employment_id)->character_jobs(employment_id); FK(transaction_id)->money_transactions(transaction_id); UQ(transaction_id). CK: amount>0, game_minute>=0. work_key는 서버/로컬 서비스가 발급한 완료업무 ID 또는 월급 계절 번호로 구성한 고정 키. 사용자 임의 키로 중복 월급 수령 불가.

## 3. 로또·게임머니 도박

### 3.1 `lottery_draws` [C] [W] - 회차, 가변

필드: `draw_id ID`, `draw_no N`, `sales_start_game_minute N`, `sales_end_game_minute N`, `draw_game_minute N`, `ticket_price N=5000`, `status CODE`.

PK(draw_id); UQ(draw_no); CK: draw_no>0, 0<=start<end<=draw, ticket_price>0, status IN(open,closed,drawn). TX: 월드 달력의 토요일18시 추첨/일요일23:59까지 구매불가를 ruleset으로 계산. 추첨 직전 구매가 어느 회차인지 명시하고 서버 시각으로 검사.

### 3.2 `lottery_draw_numbers` [C] [W] - 당첨 번호

필드: `draw_id ID`, `position N`, `number_value N`.

PK(draw_id,position); UQ(draw_id,number_value); FK(draw_id)->lottery_draws(draw_id). CK: position BETWEEN 1 AND 7, number_value BETWEEN 1 AND 30. 1~6은 본번호, 7은 보너스. TX: 정확히 7행 기록과 status=drawn을 함께 커밋, 확정 후 수정 금지.

### 3.3 `lottery_tickets` [C] [W] - 구매한 복권, 불변

필드: `ticket_id ID`, `draw_id ID`, `character_id ID`, `transaction_id ID`, `purchased_game_minute N`.

PK(ticket_id); FK(draw_id)->lottery_draws(draw_id); FK(character_id)->characters(character_id); FK(transaction_id)->money_transactions(transaction_id); UQ(transaction_id). CK: purchased>=0. TX: 구매 가능 시간/실제 가격 확인 후 번호 6개와 함께 저장.

### 3.4 `lottery_ticket_numbers` [C] [W] - 선택한 번호

필드: `ticket_id ID`, `position N`, `number_value N`.

PK(ticket_id,position); UQ(ticket_id,number_value); FK(ticket_id)->lottery_tickets(ticket_id). CK: position BETWEEN 1 AND 6, number_value BETWEEN 1 AND 30. TX: 정확히 6개 보장. 일반 CHECK는 '행 6개' 조건을 강제하지 못함.

### 3.5 `lottery_claims` [C] [W] - 당첨금 수령, 불변

필드: `ticket_id ID`, `prize_rank N`, `prize_amount N`, `transaction_id ID`, `claimed_game_minute N`.

PK(ticket_id); FK(ticket_id)->lottery_tickets(ticket_id); FK(transaction_id)->money_transactions(transaction_id); UQ(transaction_id). CK: rank BETWEEN 1 AND 5, amount>0, claimed>=0. TX: 티켓 본인 소유/추첨결과 검증 후 은행에서 1회 지급. 10억원 1등 상금 등은 ruleset 배당표 사용.

### 3.6 `casino_rounds` [C] [W] - 슬롯머신 1회, 불변

필드: `round_id ID`, `character_id ID`, `bet_amount N`, `multiplier N`, `payout_amount N`, `operation_id ID`, `bet_transaction_id ID`, `payout_transaction_id ID?`, `game_minute N`, `result_json JSON`.

PK(round_id); FK(character_id)->characters(character_id); FK(operation_id)->game_operations(operation_id); FK(bet_transaction_id)->money_transactions(transaction_id); FK(payout_transaction_id)->money_transactions(transaction_id); UQ(operation_id); UQ(bet_transaction_id); UQ(payout_transaction_id). CK: bet>0, multiplier IN(0,2,3,6), payout>=0, game_minute>=0; payout>0이면 지급거래 필수. TX: 확률은 아직 미정인 ruleset 값, RNG·베팅·결과·배당을 한 번에 기록. 결과 재뽑기 금지.

## 4. 농사·펫·보험

### 4.1 `crop_definitions` [C] - 작물 정의

필드: `crop_id ID`, `ruleset_id ID`, `seed_item_id ID`, `harvest_item_id ID`, `growth_minutes N`, `harvest_quantity N`.

PK(crop_id); FK(ruleset_id)->game_rulesets(ruleset_id); FK(seed_item_id)->item_definitions(item_id); FK(harvest_item_id)->item_definitions(item_id). CK: growth/quantity>0. 기획의 1~7일 성장은 1440~10080게임분 시드.

### 4.2 `farm_crops` [C] [W] - 심은 작물, 가변

필드: `crop_instance_id ID`, `plot_id ID`, `cell_key S(64)`, `crop_id ID`, `planted_game_minute N`, `ready_game_minute N`, `last_watered_game_minute N?`, `harvested_game_minute N?`, `harvest_operation_id ID?`.

PK(crop_instance_id); FK(plot_id)->land_plots(plot_id); FK(crop_id)->crop_definitions(crop_id); FK(harvest_operation_id)->game_operations(operation_id); UQ(harvest_operation_id). CK: 0<=planted<ready; watered NULL 또는 >=planted; harvested와 operation은 둘 다 NULL 또는 둘 다 NOT NULL이며 harvested>=ready. TX: 같은 (world,plot,cell)에서 미수확 작물 1개를 잠금/조건부 생성으로 보장, 씨앗 소비/수확 보상과 함께 저장. 과거 행을 유지하므로 일반 UQ(plot,cell)는 사용하지 않음.

### 4.3 `pet_definitions` [C] - 펫/알 종류

필드: `pet_definition_id ID`, `ruleset_id ID`, `display_name S(100)`, `egg_item_id ID`, `breeding_rules_json JSON`.

PK(pet_definition_id); FK(ruleset_id)->game_rulesets(ruleset_id); FK(egg_item_id)->item_definitions(item_id). JSON에 허용 교배/자손 정의를 넣고 콘텐츠 빌드에서 참조를 검증.

### 4.4 `pets` [C] [W] - 개체와 계보, 가변

필드: `pet_id ID`, `pet_definition_id ID`, `owner_character_id ID`, `ranch_building_id ID?`, `parent_a_id ID?`, `parent_b_id ID?`, `born_game_minute N`, `next_breeding_game_minute N?`, `following BOOL=0`.

PK(pet_id); FK(pet_definition_id)->pet_definitions(pet_definition_id); FK(owner_character_id)->characters(character_id); FK(ranch_building_id)->buildings(building_id); FK(parent_a_id)->pets(pet_id); FK(parent_b_id)->pets(pet_id). CK: born>=0, next NULL 또는 >=born; 부모는 둘 다 NULL 또는 둘 다 NOT NULL, 각 부모<>pet, 부모끼리 다름. TX: 부모 출생시각이 자식보다 앞서는지 검증해 순환 계보 차단, 목장 용량/소유/교배 쿨다운 확인.

### 4.5 `insurance_policies` [C] [W] - 보험 계약, 가변

필드: `policy_id ID`, `character_id ID`, `insurer_company_id ID`, `premium_amount N`, `premium_period_minutes N`, `starts_game_minute N`, `paid_through_game_minute N`, `covers_hospital_fee BOOL`, `waives_hospital_stay BOOL`, `status CODE`.

PK(policy_id); FK(character_id)->characters(character_id); FK(insurer_company_id)->companies(company_id). CK: premium>0, period>0, 0<=starts<=paid_through, status IN(active,lapsed,closed). TX: 보험 업종/가입 상태 및 납부를 검사. 입원 4/6시간 불일치 해결 후 보장 조건 확정.

### 4.6 `insurance_payments` [C] [W] - 보험료 납입, 불변

필드: `policy_id ID`, `period_no N`, `amount N`, `transaction_id ID`, `game_minute N`.

PK(policy_id,period_no); FK(policy_id)->insurance_policies(policy_id); FK(transaction_id)->money_transactions(transaction_id); UQ(transaction_id). CK: period_no>0, amount>0, game_minute>=0. TX: 보험료 이체와 보장 기간 연장 함께 기록.

### 4.7 `hospital_stays` [C] [W] - 입원/보험 적용, 가변

필드: `stay_id ID`, `character_id ID`, `admitted_game_minute N`, `release_game_minute N`, `policy_id ID?`, `fee N`, `payment_transaction_id ID?`, `status CODE`.

PK(stay_id); FK(character_id)->characters(character_id); FK(policy_id)->insurance_policies(policy_id); FK(payment_transaction_id)->money_transactions(transaction_id); UQ(payment_transaction_id). CK: 0<=admitted<=release, fee>=0, status IN(admitted,released). TX: 입원 당시 유효 보험/회복/행동 제한, 비용 결제/체납 정책. 보험 적용 후 비용과 입원 종료시각을 결과로 저장.

## 5. 작품·투자·청부 계약

### 5.1 `books` [C] [W] - 작성한 책, 가변

필드: `book_id ID`, `author_character_id ID`, `title S(200)`, `body TEXT`, `content_hash S(64)`, `page_count N`, `published_game_minute N?`, `status CODE`.

PK(book_id); FK(author_character_id)->characters(character_id); CK: page_count>0, hash 길이=64, status IN(draft,published,rejected), published NULL 또는 >=0. TX: 반복 문자/표절 중복/원고 길이 기준은 서비스에서 검증. 페이지당 매입가/열람 인세는 ruleset.

### 5.2 `book_daily_records` [C] [W] - 일별 열람·인세, 가변→일마감 불변

필드: `book_id ID`, `absolute_day N`, `qualified_read_count N`, `royalty_amount N`, `transaction_id ID?`.

PK(book_id,absolute_day); FK(book_id)->books(book_id); FK(transaction_id)->money_transactions(transaction_id); UQ(transaction_id). CK: day/read_count/royalty>=0. TX: 자가 열람/반복 열람을 정산에 포함하는 정책 확정. 매일 아침 지급 완료는 transaction 존재로 구분하고 1회 지급.

### 5.3 `investment_mandates` [C] [W] - 펀드매니저 위탁 계약, 가변

필드: `mandate_id ID`, `investor_character_id ID`, `manager_character_id ID`, `principal N`, `fee_rate_ppm N`, `opened_game_minute N`, `closed_game_minute N?`, `status CODE`.

PK(mandate_id); FK(investor_character_id)->characters(character_id); FK(manager_character_id)->characters(character_id). CK: investor<>manager, principal>0, fee BETWEEN 0 AND 1000000, 0<=opened, closed NULL 또는 >=opened, status IN(proposed,active,closed).

아직 설계 골격만이다. 이 테이블만으로 자금 이체/대리매매를 허용하지 않는다. 위탁 전용 계좌·포트폴리오, 매매권한/손익분배·해지 규칙을 확정한 뒤 stock_holdings의 소유 모델을 확장해야 한다. 싱글 버전에서는 비활성.

### 5.4 `service_contracts` [C] [W] - 게임 내 청부/신분세탁 의뢰, 가변

필드: `contract_id ID`, `client_character_id ID`, `company_id ID`, `target_character_id ID`, `service_kind CODE`, `quoted_price N`, `penalty_points_to_remove N=0`, `accepted_game_minute N?`, `due_game_minute N?`, `status CODE`, `payment_transaction_id ID?`.

PK(contract_id); FK(client_character_id)->characters(character_id); FK(company_id)->companies(company_id); FK(target_character_id)->characters(character_id); FK(payment_transaction_id)->money_transactions(transaction_id); UQ(payment_transaction_id). CK: kind IN(assassination,identity_cleansing), price/points>=0, status IN(requested,accepted,succeeded,failed,cancelled), accepted/due 모두 NULL 또는 0<=accepted<=due.

TX: 해당 업종/레벨 확인. 청부 대상은 characters FK로 NPC 배제, 자기 자신 대상 불가. 세탁은 벌점 1점당 100만원 기획값과 실제 제거 가능 벌점 검증, 원장/벌점 이력 동시 저장. 1일 1회 시도/지연 보수 차감/선불 환불 정책은 별도 확정. 청부 기능은 싱글 비활성.

### 5.5 `service_contract_attempts` [C] [W] - 의뢰 실행 이력, 불변

필드: `contract_id ID`, `absolute_day N`, `success BOOL`, `operation_id ID`, `result_json JSON`.

PK(contract_id,absolute_day); FK(contract_id)->service_contracts(contract_id); FK(operation_id)->game_operations(operation_id); UQ(operation_id). CK: day>=0. TX: '하루 1회'가 회사 전체인지 의뢰별인지 기획 확정 전 이 PK를 회사 전체 제한으로 오인하지 않는다. 전체 제한이면 회사·날짜 유일 실행 테이블 추가.

## 6. 시상식·은퇴·복지

### 6.1 `award_definitions` [C] - 시상 부문

필드: `award_id ID`, `ruleset_id ID`, `display_name S(100)`, `metric_code CODE`, `winner_count N=1`, `tie_policy CODE`.

PK(award_id); FK(ruleset_id)->game_rulesets(ruleset_id). CK: winner_count>0; tie_policy IN(shared,earliest,character_id). 동점 처리법은 제안이며 팀 확정 필요. 기업상/부자상/낚시왕/기부왕의 집계 기준을 분명히 한다.

### 6.2 `annual_awards` [C] [W] - 연도별 부문/한정 보상

필드: `game_year N`, `award_id ID`, `reward_item_id ID`, `trophy_item_id ID`, `title_id ID`, `announced_game_minute N`, `settled_game_minute N?`.

PK(game_year,award_id); FK(award_id)->award_definitions(award_id); FK(reward_item_id)->item_definitions(item_id); FK(trophy_item_id)->item_definitions(item_id); FK(title_id)->title_definitions(title_id). CK: year>=1, announced>=0, settled NULL 또는 >=announced. TX: 연초에 보상 공개, 한정 아이템 tradable=0. 연도별 다른 상품은 새로운 item ID로 연결.

### 6.3 `character_annual_metrics` [C] [W] - 연간 실적, 가변→정산 후 불변

필드: `game_year N`, `character_id ID`, `metric_code CODE`, `metric_value N`, `reached_game_minute N`.

PK(game_year,character_id,metric_code); FK(character_id)->characters(character_id); CK: year>=1, reached>=0. metric_value는 순이익 등 음수 가능. TX: donation/fishing/company/자산 평가의 정의와 집계 이벤트를 고정하고 연도 마감 후 변경 금지. 시상식에 필요한 집계를 만들기 전에 원시 이력을 삭제하지 않음.

### 6.4 `award_winners` [C] [W] - 수상과 보상 지급, 가변→지급 후 불변

필드: `game_year N`, `award_id ID`, `character_id ID`, `rank_no N`, `score_snapshot N`, `reward_operation_id ID?`, `rewarded_game_minute N?`.

PK(game_year,award_id,character_id); FK(game_year,award_id)->annual_awards(game_year,award_id); FK(character_id)->characters(character_id); FK(reward_operation_id)->game_operations(operation_id); UQ(reward_operation_id). CK: rank>0, reward operation/시각은 둘 다 NULL 또는 둘 다 NOT NULL, rewarded NULL 또는 >=0. 동점 수상 가능하므로 rank만 UNIQUE로 제한하지 않음. TX: 순위 확정과 이후 아이템·트로피·칭호 지급을 정확히 한 번 수행.

### 6.5 `retirements` [C] [W] - 개인 엔딩/명예의 전당, 불변

필드: `character_id ID`, `ending_kind CODE`, `retired_game_minute N`, `net_worth_snapshot N`, `summary_json JSON`, `operation_id ID`.

PK(character_id); FK(character_id)->characters(character_id); FK(operation_id)->game_operations(operation_id); UQ(operation_id). CK: kind IN(rich,ordinary,bankrupt), retired>=0. 순자산은 부채 때문에 음수 가능. TX: 캐릭터 은퇴와 활성 캐릭터 포인터 변경, 미결제 계약/재산 처리를 기획에 따라 결정. 자동으로 새 캐릭터에 재산을 넘기지 않음.

### 6.6 `welfare_batches` [C] [W] - 하위 10% 지원 배분, 불변

필드: `batch_id ID`, `period_key ID`, `pool_amount N`, `eligible_player_count N`, `cutoff_net_worth N`, `operation_id ID`.

PK(batch_id); UQ(period_key); FK(operation_id)->game_operations(operation_id). CK: pool>=0, eligible_count>=0. TX: 평가시각/하위 10%의 반올림/동점/오프라인 계정 처리 정책 확정 후 대상 스냅샷 생성.

### 6.7 `welfare_payments` [C] [W] - 대상별 복지 지급, 불변

필드: `batch_id ID`, `character_id ID`, `amount N`, `transaction_id ID`.

PK(batch_id,character_id); FK(batch_id)->welfare_batches(batch_id); FK(character_id)->characters(character_id); FK(transaction_id)->money_transactions(transaction_id); UQ(transaction_id). CK: amount>0. TX: 총 지급액<=pool, 시청 계좌 차감과 수혜 계좌 입금, 배분 중복 방지.

## 7. 꺼내 놓은 현금·수표

현재 `CurrencyBreakdown`은 잔액을 권종별로 나누어 표시할 뿐 별도 재산이 아니다. 아래는 기획 15쪽의 '지갑에서 화폐 꺼내기'를 실제 양도/절도 가능한 월드 오브젝트로 구현할 때만 추가한다. 현금 객체를 일반 inventory_items에도 중복 생성하지 않는 초기 제안이다.

### 7.1 `cash_objects` [C] [W] - 현물 화폐 묶음, 가변

필드: `cash_object_id ID`, `cash_kind CODE`, `denomination N`, `unit_count N`, `total_value N`, `holder_actor_id ID?`, `map_id ID?`, `position_x R?`, `position_y R?`, `position_z R?`, `status CODE`, `issued_operation_id ID`, `issued_game_minute N`, `redeemed_transaction_id ID?`.

PK(cash_object_id); FK(holder_actor_id)->actors(actor_id); FK(map_id)->map_definitions(map_id); FK(issued_operation_id)->game_operations(operation_id); FK(redeemed_transaction_id)->money_transactions(transaction_id). CK: denomination/unit_count/total_value>0, issued>=0; kind IN(currency,check); currency이면 denomination IN(100,500,1000,5000,10000), check이면 unit_count=1; status IN(held,dropped,redeemed,repacked); held이면 holder만 NOT NULL이고 맵/좌표는 NULL, dropped이면 holder NULL이고 맵/좌표 필수, 나머지는 holder/맵/좌표 모두 NULL; redeemed 상태일 때만 redeemed_transaction 필수.

TX: total_value=denomination×unit_count를 안전한 정수 계산으로 확인. 발행 후 권종/수량/가치는 불변이다. 꺼내기는 현금계좌에서 같은 금액을 차감하고 시스템 `physical_cash_escrow` 계좌에 적립한다. 다시 지갑에 넣을 때는 해당 객체를 1회 redeemed로 바꾸고 escrow에서 수령자 현금계좌로 지급한다. 양도/절도는 holder만 이동하며 잔액을 추가 지급하지 않는다. 분할/합치기는 기존 묶음을 repacked로 보존하고 동일 총액의 새 ID들을 만드는 한 operation으로 처리한다. 이벤트와 위치 변경도 같은 TX에 포함한다.

계좌 현금과 꺼낸 현금을 동시에 소비할 수 없게 한다. 순자산에는 소지 중인 현물만 별도 합산하고, escrow 전체를 플레이어 자산으로 또 합산하지 않는다. 땅에 떨어진 현금의 만료/회수 정책은 확정 전 자동 삭제하지 않는다.

### 7.2 `cash_object_events` [C] [W] - 화폐 이동·회수 이력, 불변

필드: `event_id ID`, `cash_object_id ID`, `operation_id ID`, `event_kind CODE`, `from_actor_id ID?`, `to_actor_id ID?`, `game_minute N`, `details_json JSON`.

PK(event_id); UQ(cash_object_id,operation_id,event_kind); FK(cash_object_id)->cash_objects(cash_object_id); FK(operation_id)->game_operations(operation_id); FK(from_actor_id)->actors(actor_id); FK(to_actor_id)->actors(actor_id); IX(cash_object_id,game_minute). CK: kind IN(issued,transferred,dropped,picked_up,stolen,redeemed,repacked), game_minute>=0. TX: 변경 전후 소유/위치와 operation 일치; 재발행/재수령 방지. details_json은 위치/분할 묶음 ID의 감사 스냅샷이며 현재 소유권의 원본은 cash_objects다.

## 8. 별도 설계가 더 필요한 백로그

기획서 자체에서 후순위/토의 대상으로 남긴 공동창업, 실시간 경매장, 결혼, 방문 권한, 상점 운영 세부 로직은 이번에 확정 테이블로 만들지 않는다. 특히 공동창업은 companies.owner_character_id 단일 소유 가정을, 지정가/P2P 주식 거래와 위탁펀드는 stock_holdings/매매의 소유·예약 가정을 바꾼다. 이를 개발할 때 지분·자금 예약·계약 권한부터 확장한다.

낚시는 초기에는 fish 아이템 지급, game_operations의 catch 결과, 연간 fishing 지표를 함께 저장하면 된다. 대회별 개체 크기/희귀도/우승 산정이 필요해지면 fishing_catches를 별도 도입한다. 랜덤박스는 아이템 차감과 추첨 보상을 하나의 operation으로 기록하고, 기획상 코스메틱 보상만 허용한다. 이 두 기능을 위해 현재 미확정 필드를 임의로 늘리지 않는다.
