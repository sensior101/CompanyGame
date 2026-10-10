# 도서관 무인 기기

제공된 참고 이미지에 맞춰 Blender에서 제작한 가구 에셋. Unity 도서관 1층 오른쪽 창가에 동일 프리팹 3대를 배치했다.

## 파일과 책임

- `LibraryKiosk.blend`: 편집용 Blender 원본. 나뭇결 텍스처를 내부에 포함한다.
- `build_library_kiosk.py`: 기존 RetailPOS의 미터 단위·모델/촬영 컬렉션 분리·독립 화면·FBX 내보내기 방식을 적용한 모델 제작 레시피.
- `Exports/`: Unity에 전달하는 FBX, 재질 명세 JSON, 나뭇결 PNG.
- `integrate_library_kiosk.cs`: 기존 Unity CLI의 `eval_file`로 실행하는 에디터 작업 레시피. Unity 프로젝트의 런타임 컴파일 대상이 아니다.
- `CompanyGame/Assets/Art/Items/Furniture/LibraryKiosk/`: Unity용 모델·텍스처·재질 8개·프리팹 1개.
- 수정한 기존 파일: `CompanyGame/Assets/Scenes/Interiors/CivicLibraryInterior.unity`에 `10_World/LibraryKiosks` 그룹과 프리팹 인스턴스 3개 추가.

## 기존 시스템 연결

`Blender 원본 → FBX/재질 명세/PNG → ModelImporter·AssetDatabase → PrefabUtility → 기존 도서관 씬` 순서로 반영한다.
기존 `WorldObject`를 `StaticProp` / `FurnitureFunction.Display`로 사용한다. 충돌은 프리팹의 BoxCollider 하나가 담당한다.
게임 스크립트, 공개 API, 저장 데이터, 경제·거래·대화 시스템은 변경하지 않았다. 별도 관리자나 중복 공통 기능은 없다.

## 모델 규격

- 너비 0.76m × 높이 약 1.74m × 깊이 0.636m.
- 원본 16,105 삼각형, 메시 7개. 세 인스턴스가 모델과 재질을 공유한다.
- 크림색 몸체, 원목 측판과 선반, 키보드, 책 인식 표시, 영수증 슬롯, 후면 점검문·통풍구·포트.
- `Screen_Main` 메시와 `Kiosk_Screen` 재질 분리. UV는 0–1 전체 사각형으로 추후 화면 연결에 사용할 수 있다.
- 원점은 바닥 중심, Unity 프리팹의 앞 방향은 +Z.
- `generateSecondaryUV=false`: 자동 라이트맵 UV 생성 시 작은 책 인식 표시가 UV 패킹 실패로 사라져 비활성화했다. 원본 UV와 기존 씬 조명을 사용한다. 라이트맵 베이크 대상이 아니다.

## 배치

| 인스턴스 | Unity 위치 | Y 회전 |
|---|---|---|
| LibraryKiosk_01 | (15.1, 0, -4.6) | 270° |
| LibraryKiosk_02 | (15.1, 0, -6.0) | 270° |
| LibraryKiosk_03 | (15.1, 0, -7.4) | 270° |

중심 간격은 1.4m이며 화면이 실내를 향한다. 서가·소파·화분과 겹치지 않고 기기 앞 접근 공간을 확보했다.

## 재생성

Unity를 편집 모드로 두고 `CivicLibraryInterior`를 연다. 프로젝트 루트에서:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python 'ArtSource/Items/Furniture/LibraryKiosk/build_library_kiosk.py'
& 'C:/Users/usercom/AppData/Local/Unity/bin/unity.exe' command eval_file 'C:/서현/프로젝트/companyGame/ArtSource/Items/Furniture/LibraryKiosk/integrate_library_kiosk.cs' --project-path 'C:/서현/프로젝트/companyGame/CompanyGame' --caller plugin --skill unity-cli --json
```

Blender 명령 끝에 `-- --skip-render`를 붙이면 QA 렌더를 생략한다. 제작 레시피를 다시 실행하면 이 폴더의 원본을 재생성하므로 수작업으로 편집한 원본은 먼저 따로 보관한다. 통합 레시피는 같은 이름의 프리팹과 인스턴스를 갱신한다.

## 확인 결과와 범위

- FBX 재수입, 원본·Unity 복사본 해시, 메시 7개와 재질 연결을 확인했다.
- Unity 편집·플레이 모드에서 3대의 배치, 전면 접근 공간과 충돌을 확인했다.
- `QA/` 렌더·스크린샷·보고서는 로컬 생성 자료로 정리했으며 버전 관리하지 않는다. Blender 검토 렌더는 위 제작 명령으로 다시 생성한다.
- 이번 작업은 외형과 씬 배치다. 기기 조작·검색·대출 UI는 연결하지 않았다. 플레이어 직접 보행 조작 및 별도 빌드 검증은 수행하지 않았다.
