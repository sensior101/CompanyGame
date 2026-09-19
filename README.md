# CompanyGame

Unity 클라이언트, 맵 제작 원본, 기획 문서를 함께 관리하는 저장소입니다.
Git 명령은 이 README가 있는 **저장소 루트**에서 실행하고, Unity Hub에서는 그 안의 **`CompanyGame` 폴더**를 엽니다.

## 프로젝트 구조

```text
CompanyGame/                 Unity 프로젝트
  Assets/                    씬, 프리팹, 메시, 재질, C# 코드와 .meta
  Packages/                  패키지 목록과 잠금 파일
  ProjectSettings/           팀이 공유하는 Unity 설정
  AgentScripts/              맵 제작·검증에 사용한 C# 소스
ArtSource/Daldongne/          Blender 원본과 Python 생성 스크립트
Docs/                        기획·구현 계획·협업 안내
pre_design/                  디자인 참고 이미지
Server/                      서버 개발을 위한 위치와 안내
```

`Library`, `Temp`, `Logs`, `obj`, `UserSettings` 등은 로컬에 생성되며 Git에 포함하지 않습니다.
`Assets`와 `.meta`, `Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings`는 함께 공유합니다.
`ArtSource`와 `Docs`의 기준 위치는 저장소 루트입니다.

## 팀원이 처음 실행하기

아래 clone 명령은 아직 저장소가 없는 팀원을 위한 절차입니다. 이미 작업하던 프로젝트가 있는 PC에서는 다시 clone하거나 새 Unity 프로젝트를 만들지 않고, 기존 프로젝트를 Hub에 추가합니다.
선택할 폴더 바로 아래에 `Assets`, `Packages`, `ProjectSettings`가 있어야 하며, `Assets/Scenes/daldongnaemap.unity`가 있는지도 확인하세요.
현재 작업 PC에서는 `C:\서현\프로젝트\companyGame\CompanyGame`이 올바른 Unity 프로젝트 위치입니다. 여기서 `CompanyGame` 폴더를 한 단계 더 들어가지 않습니다.

1. Git, Git LFS, Unity Hub를 설치합니다.
2. Git LFS를 활성화하고 저장소를 내려받습니다. 아래 명령은 프로젝트를 둘 상위 폴더에서 실행합니다.

   ```powershell
   git lfs install
   git clone https://github.com/sensior101/CompanyGame.git
   Set-Location CompanyGame
   git lfs pull
   ```

3. Unity Hub에서 **6000.4.5f1**을 설치합니다. 정확한 버전 기준은 `CompanyGame/ProjectSettings/ProjectVersion.txt`입니다.
4. Unity Hub의 프로젝트 추가 기능으로 내려받은 저장소 안의 **`CompanyGame` 하위 폴더**를 선택합니다.
5. 패키지 다운로드와 에셋 임포트가 끝나면 `Assets/Scenes/daldongnaemap.unity`를 열고 Play를 누릅니다.

`WASD` 이동, `Shift` 달리기, `F` 걷기/전체 시점 전환, `1`/`2` 캐릭터 선택, 포털 근처에서 `E` 맵 이동을 사용할 수 있습니다.
조작과 맵 구성은 [마을 안내](CompanyGame/Assets/Art/Daldongne/WarmVillage/README.md)와 [플레이어 안내](CompanyGame/Assets/Art/Daldongne/Players/README.md)를 참고하세요.

기본 빌드 씬 목록은 `daldongnaemap`에서 시작하며, `SampleScene`과 작은 예제 맵 `DaldongnePocketGarden`을 포함합니다.
마을 역 앞의 청록색 포털과 정원 입구의 포털로 두 맵을 왕복합니다. 새 맵은 **Tools → Company Game → Maps → Create Small Map**으로 만드세요.
공통 Hierarchy, 건물·나무 프리팹, 씬 템플릿과 포털 설정은 [작은 맵 제작 안내](Docs/Small_Map_Workflow.md)를 참고하세요.
활성 Build Profile에서 별도 Scene List를 사용하는 경우에는 해당 목록에도 새 맵을 추가하세요.

## Git과 LFS

- `.blend`, `.glb`, `.fbx`, `.psd`는 LFS로 관리합니다.
- C#과 Unity 씬·프리팹·메타 파일은 일반 Git으로 관리합니다.
- `.unitypackage`는 배포용 출력물이라 제외합니다. 팀원은 저장소의 Unity 프로젝트를 사용합니다.
- 에셋을 추가·이동·삭제할 때 해당 `.meta`도 함께 커밋합니다. 에셋 이동은 Unity Project 창에서 하는 것을 권장합니다.
- 작업별 브랜치에서 수정하고 Pull Request로 `main`에 합칩니다.

[첫 업로드와 일상 작업 절차](Docs/Git_Collaboration.md)를 참고하세요.

## 게임 코드와 서버

현재 게임 기능의 구현 수준과 순서는 [Unity 스크립트 구현 계획](Docs/Unity_Script_Implementation_Plan.md)에 정리되어 있습니다.
현재 구현된 맵 미리보기와 아직 틀만 있는 게임 시스템을 구분해서 작업하세요.
서버 구현과 프레임워크는 아직 없으며, [서버 작업 안내](Server/README.md)에 분리 기준을 정리했습니다.

## 맵 원본과 제작 도구

- [Blender 원본 안내](ArtSource/Daldongne/README.md)
- [마을 제작·동선 안내](ArtSource/Daldongne/WARM_VILLAGE.md)
- [제작 도구의 실행 조건](Docs/Git_Collaboration.md#제작-도구-취급)

팀원이 맵을 열고 플레이하는 데 제작 스크립트의 재실행은 필요하지 않습니다.
