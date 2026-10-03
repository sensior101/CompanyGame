# CompanyGame

늪지대컴퍼니 기반 3D 멀티플레이 경제 시뮬레이션 게임입니다.
기획 정본은 노션 [기획안 (수정본)](https://mushroom00.notion.site/3e9f770a0fc780d5a8d8ff291b3fc30a)입니다. 예전 기획 복사본은 `Docs/Game_Plan_Legacy.md`에 있습니다.

## 시작하기

1. Unity Hub → Add project from disk → 저장소의 `CompanyGame/` 폴더 (`Assets`, `ProjectSettings`가 있는 곳)
2. 에디터 버전 **6000.4.5f1**
3. `Assets/Scenes/daldongnaemap`을 열고 ▶

| 키 | 동작 |
| --- | --- |
| WASD / Shift | 이동 / 달리기 |
| Space | 상호작용 (문, 정류장, 포털) |
| E / R | 인벤토리 / 핸드폰 |
| Tab | 1인칭 ↔ 3인칭 |
| 마우스 왼쪽 | 공격·사용 |

키 설정은 전부 `Core/GameInput.cs` 한 파일에 있습니다.

## 저장소 구성

| 폴더 | 내용 |
| --- | --- |
| `CompanyGame/` | Unity 프로젝트 |
| `Server/` | Node + Fastify + SQLite 서버 (로그인·프로필). [Server/README.md](Server/README.md) |
| `Docs/` | 협업 규칙, 서버 API, DB 설계 |
| `ArtSource/` | Blender·이미지 원본과 생성 스크립트 |

## 구조

스크립트는 폴더마다 어셈블리로 나뉘어 있고 `Core → Economy → Gameplay → World → Player → UI` 방향으로만 참조합니다.
플레이어는 `Resources/Player.prefab` 하나를 실행 중에 만들고, 매니저는 `GameSystems`가 만듭니다. 맵 씬에는 카메라와 스폰 지점만 둡니다.
자세한 내용은 [Docs/Architecture.md](Docs/Architecture.md).

## 문서

| 문서 | 내용 |
| --- | --- |
| [Docs/Architecture.md](Docs/Architecture.md) | 프로젝트 구조 |
| [Docs/Git_Collaboration.md](Docs/Git_Collaboration.md) | 협업 규칙 |
| [Docs/Server.md](Docs/Server.md) | 서버 |
| [Docs/Remaining_Work.md](Docs/Remaining_Work.md) | 남은 작업 |
| [Docs/README.md](Docs/README.md) | 전체 문서 목록 |

## 협업

브랜치·커밋·씬 작업 규칙은 [Docs/Git_Collaboration.md](Docs/Git_Collaboration.md)를 따릅니다.
한 씬은 한 번에 한 명만 수정하고, 자주 바뀌는 부분은 프리팹으로 빼서 작업합니다.
