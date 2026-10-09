COMPANYGAME / 도서관 V4 — THE READING STEPS
Higgsfield 3D Jutsu, committed revision 4

[바로 플레이]
압축을 풀고 Walkthrough.html을 PC의 Chrome 또는 Edge에서 연다.
'도서관 들어가기'를 클릭한다. 프로그램 설치나 개발 서버 없이 실행되는 파일이다.
WASD 이동 / 마우스 시점 / Shift 달리기 / Space 점프 / E 독서석 앉기·일어나기 / R 입구
Esc로 마우스를 풀고 화면을 다시 클릭하면 조작을 재개한다.
터치용 이동 버튼과 드래그도 포함했지만, 실제 기기별 모바일 테스트는 하지 않았다.
브라우저의 WebGL 및 하드웨어 가속이 필요하다. 채팅 앱의 정적 파일 미리보기에서는 실행되지 않을 수 있다.

[실제로 확인한 범위]
최종 GLB 모델을 불러온 Chromium 1인칭 테스트에서 아래를 확인했다.
입구 → 중앙 통행 계단 → 2층 동쪽 복도 → 앞쪽 브리지 → 서쪽 복도 → 왼쪽 계단 → 카페 앞 통로 → 입구.
13개 목표 지점을 캐릭터 크기(높이1.8m, 반지름0.32m)의 충돌체로 연속 이동했다.
외벽 및 서가 정면 충돌 정지, 독서석 앉기/일어나기를 확인했다.
검증 결과: playtest.json. HTML 단독 실행 및 키보드 입력 검사: offline_file_test.json.
렌더 PNG는 실제 모델의 이미지이며, 이미지 생성으로 만든 가짜 모델 화면이 아니다.
이것은 실내 탐색용 로컬 데모다. CompanyGame 본게임이나 멀티플레이 빌드가 아니다.

[공간]
32m × 20m / 2층 바닥 높이3.6m / 중앙 개방형 아트리움.
1층 왼쪽 카페, 열람 테이블 / 오른쪽 4열 서가 / 중앙 9단 독서 테라스.
테라스 옆 20단 통행 계단과 왼쪽 별도 20단 계단.
2층 취식 독서 테이블, 좌식 독서석, 누울 수 있는 자리2개, 뒤쪽 ㄱ자 서가.
앞·뒤 연결 브리지, 유리 난간, 벽·창·천장·채광창.
독서석 앵커62개. 장식 책1547권은 서가·재질별48개 묶음으로 정리했다.
충돌용 메시261개를 별도 이름으로 넣었다. 계단은 보이는 계단과 별개인 숨은 경사 충돌체를 사용한다.
작은 장식·책·의자 다리에는 개별 정밀 충돌체를 두지 않았다.

[파일]
Library_V4.glb: 이 데모에서 실제로 사용하고 검증한 최종 모델.
Library_V4.blend: 같은 리비전의 편집 가능한 Blender 원본.
Walkthrough.html: 모델과 실행 코드를 포함한 단일 파일 탐색 데모.
Unity/LibraryDemoPlayer.cs: Unity용 분리된 테스트 캐릭터 컨트롤러.
Unity/Editor/LibraryPlaytestBuilder.cs: 선택한 모델에서 새 테스트 씬을 만드는 메뉴.
Export_Unity_FBX.py: Blender에서 실행하는 FBX 내보내기 보조 스크립트. FBX 파일 자체는 포함하지 않았다.
walkthrough.js + template.html: 탐색 데모 소스.
LICENSE_THREE.txt: 탐색 데모가 사용하는 Three.js의 MIT 라이선스.

[Unity에서 테스트 씬 생성]
1. 프로젝트를 백업하거나 테스트 브랜치를 사용한다.
2. Package Manager에서 'Add package by name'으로 com.unity.cloud.gltfast를 설치한다.
   이미 다른 GLB importer를 사용 중이면 기본 importer 중복 충돌을 먼저 확인한다.
3. Library_V4.glb와 Unity 폴더 안의 C# 파일을 프로젝트의 Assets 하위에 넣는다.
   LibraryPlaytestBuilder.cs는 반드시 Editor 폴더 안에 둔다.
4. Project 창에서 Library_V4.glb의 가져온 모델을 선택한다.
5. Tools > CompanyGame > Library V4 > Create Playtest From Selected Model 실행.
6. 현재 씬 저장 여부를 확인한 후 새 Library_Playtest 씬이 생성되면 Play를 누른다.
새 씬에는 모델, 261개 MeshCollider, 카메라, 테스트 캐릭터, 독서석 연결이 추가된다.
같은 이름의 기존 씬을 덮어쓰지 않고 별도 파일명을 사용한다.
기존 CompanyGame PlayerMovement, 인벤토리, 카메라, 네트워크 코드는 수정하지 않는다.

[검증하지 않은 부분]
Unity Editor를 이 환경에서 실행하지 않았으므로, C# 컴파일/Unity 물리/URP 재질/사용자 프로젝트와의 호환성은 별도 확인이 필요하다.
Unity 전용 스크립트는 시작용 구현이며 본게임의 사용자 정의 컨트롤러에 자동 통합되지 않는다.
NPC AI, 좌석 예약·동시성, 아이템 대여·독서 UI, 카페 구매, 자동문 동작, 멀티플레이 동기화는 연결하지 않았다.
문은 열린 배치다. 좌석 기능은 1인칭 시점 이동 데모이며 아바타 앉기 애니메이션은 없다.
내비게이션 베이크, 라이트맵 UV·베이크, 오클루전 컬링, LOD, 목표 기기 프레임 성능은 출시 전에 검토한다.
모든 좌석과 모든 방향의 충돌을 완전 탐색한 것은 아니며, 기록된 경로와 별도 접촉 검사 범위가 검증 대상이다.
Higgsfield 편집기, Blender 렌더, 브라우저 데모, Unity의 조명과 셰이더는 서로 다를 수 있다.

[공식 참고 문서]
https://github.com/Unity-Technologies/com.unity.cloud.gltfast/blob/main/Packages/com.unity.cloud.gltfast/Documentation~/ImportEditor.md
https://threejs.org/docs/pages/Octree.html
https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/charactercontroller/stepoffset

[편집 프로젝트]
https://higgsfield.ai/3d-jutsu/d9a33081-47e1-4d22-a066-fa8be7f1da47
