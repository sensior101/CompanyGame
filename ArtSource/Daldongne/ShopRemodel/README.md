# 동네 슈퍼·2층 빵집 리모델링

2026-09-19. 사용자 참고 사진 두 장을 기준으로 제작한 Unity 네이티브 메시.

## 적용

- **Supermarket**: 빨래방을 같은 위치에서 교체했다. 회벽 단층 건물, 낮은 철판 지붕, 바랜 `연희네 슈퍼` 간판, 초록·흰 차양, 미닫이 유리 입면과 상품 안내문, 아이스크림 냉동고, 음료 상자·병, 평상·스툴, 둥근 외등, 측면 창살을 만들었다.
- **Bakery**: 2층 목조 외관, 가로 목재 판재와 기둥, 큰 상층 창과 덧문, 노랑·흰 차양, 따뜻한 진열창, 격자 유리문, 진열 빵·바게트 바구니·벤치·메뉴판·돌출 간판을 만들었다.
- 원래 대지와 배치 피벗을 유지했다. Bakery는 `DaldongnePocketGarden`에서도 같은 프리팹을 공유하므로 새 외형이 함께 반영된다.
- 기존 `Laundry.prefab`는 Unity `AssetDatabase.MoveAsset`으로 `Supermarket.prefab`로 변경하여 GUID를 유지했다. 마을 내부의 이름 override도 `Supermarket`으로 변경했다.
- 이전 고시원·편의점·HOME A~D 모델과 재질은 그대로 유지했다.
- 실내 출입이나 상점 게임 기능은 이번 외관 모델링 범위에 포함하지 않는다.

## 파일

- `CompanyGame/Assets/Art/Daldongne/WarmVillage/Prefabs/Buildings/Supermarket.prefab`
- `CompanyGame/Assets/Art/Daldongne/WarmVillage/Prefabs/Buildings/Bakery.prefab`
- `CompanyGame/Assets/Art/Daldongne/WarmVillage/ShopRemodel/`: 재질과 결합 메시.
- `CompanyGame/AgentScripts/ShopRemodel.cs`: 원본 생성기. 현재 씬을 저장하거나 전환하지 않고 프리팹 에셋만 갱신한다.
- `CompanyGame/AgentScripts/ShopRemodelInspect.cs`: 최초 변경 전 GUID·피벗 기록. 기존 before.json을 덮어쓰지 않는다.
- `CompanyGame/AgentScripts/ShopRemodelQA.cs`: 에셋 검사·실제 Unity 캡처·물리 이동 검사.
- 이 폴더의 `*_InMap.png`: 현재 맵 배치 화면, `*_Model.png`와 `*_Front.png`: 별도 프리뷰 씬의 모델 검토 화면.

## 검증 결과

- Unity 6000.4.5f1, 컴파일 오류 없음.
- 두 프리팹의 GUID·루트 fileID·원래 배치 피벗과 현재 씬의 프리팹 연결 유지.
- 누락 메시·재질·스크립트·직렬화 참조 없음. 주요 건물 본체에는 실제 Collider가 있다.
- **98개 물리 이동 검사, 실패 0개.** 기존 37경로 양방향 74개 + 두 가게 앞 3개 차선 × 걷기·달리기 × 양방향 24개.
- 검사 환경은 **Edit Mode의 임시 CharacterController**이며 반지름 0.35m, 높이 1.8m, stepOffset 0.23m, slopeLimit 45도, 걷기 3m/s·달리기 4.5m/s이다. 사용자의 실제 플레이어 상태를 바꾸거나 Play Mode로 전환하지 않았다.
- 작업 전 현재 씬에 미저장 변경이 있었다. 씬을 저장·닫기·전환하지 않고 그 상태를 유지했으며, 수정한 프리팹 에셋은 디스크에 저장했다.
- 상세 결과: `asset-validation.json`, `navigation-validation.json`, `capture-validation.json`.

재생성은 저장소 루트에서 해당 Unity 프로젝트가 열린 상태로 실행한다.

```powershell
unity command --project-path ./CompanyGame --caller plugin --skill unity-cli --timeout 180 run_script --file AgentScripts/ShopRemodel.cs --entry ShopRemodel.Build --timeout_ms 180000
unity command --project-path ./CompanyGame --caller plugin --skill unity-cli run_script --file AgentScripts/ShopRemodelQA.cs --entry ShopRemodelQA.Audit
unity command --project-path ./CompanyGame --caller plugin --skill unity-cli run_script --file AgentScripts/ShopRemodelQA.cs --entry ShopRemodelQA.Navigation
unity command --project-path ./CompanyGame --caller plugin --skill unity-cli run_script --file AgentScripts/ShopRemodelQA.cs --entry ShopRemodelQA.Capture
```

이전 전체 맵 Blender·GLB·Unitypackage 파일은 이 업데이트를 포함하지 않는다. 현재 편집 원본은 위 C# 생성기와 Unity 메시·재질이다. 커밋·푸시는 하지 않았다.
