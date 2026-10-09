# Git 협업 안내

## 저장소 기준

Git 저장소는 최상위 `companyGame` 하나입니다. 그 안의 `CompanyGame`은 Unity 프로젝트 폴더입니다.
내부에 다시 `git init`하거나 별도의 `.git`을 만들지 않습니다.

```powershell
# 저장소 루트에서 실행하면 이 폴더가 출력되어야 합니다.
git rev-parse --show-toplevel
git remote -v
```

원격 주소는 `https://github.com/sensior101/CompanyGame.git`입니다.
비공개 저장소라면 팀원이 GitHub에서 저장소 접근 권한을 받은 계정으로 인증해야 합니다.

## 브랜치 규칙

| 브랜치 | 역할 | 규칙 |
| --- | --- | --- |
| `main` | 플레이 가능한 안정판 | 직접 푸시 금지. `Dev`에서 올린 PR만 받습니다. |
| `Dev` | 통합 브랜치 | 직접 푸시 금지. 기능 브랜치의 PR만 받습니다. |
| `feature/*`, `fix/*`, `docs/*` | 개별 작업 | 최신 `Dev`에서 분기하고 `Dev`로 PR을 보냅니다. |

- 영문 소문자와 하이픈을 쓰고 브랜치 하나에는 기능 하나만 담습니다.
- 원격 브랜치 이름 `Dev`는 대소문자를 그대로 유지합니다.
- 작성자가 아닌 팀원 한 명 이상이 승인한 뒤 합칩니다.
- 기능 브랜치 → `Dev`: **Squash and merge**.
- `Dev` → `main`: 관리자가 Unity 확인 후 **Create a merge commit**. Squash하지 않습니다.
- 합친 기능 브랜치는 삭제하고, 다음 작업은 최신 `Dev`에서 새로 시작합니다.
- 강제 푸시와 이미 푸시한 브랜치의 rebase는 금지합니다.

## 작업 순서

Unity 씬과 프로젝트를 저장하고 기존 변경을 먼저 안전하게 정리합니다.

```powershell
git fetch origin
git switch Dev
git pull --ff-only
git switch -c feature/player-interaction
```

변경 목록을 확인하고 해당 기능의 파일과 `.meta`만 명시적으로 추가합니다.

```powershell
git status
git diff --stat
# git add -- <검토한 파일과 .meta 경로>
git diff --cached --stat
git diff --cached --check
git commit -m "Implement player interaction"
git push -u origin HEAD
```

GitHub에서 `Dev`를 대상으로 PR을 엽니다. 설명에는 변경 내용, Unity 확인 방법,
씬·프리팹 변경 여부를 적습니다. 씬·프리팹 변경과 코드는 커밋과 PR을 나눕니다.
PR 사이에 의존성이 있으면 필요한 PR과 병합 순서를 명시합니다.
영어 명령형 한 줄 커밋 제목을 쓰고, 필요한 설명은 빈 줄 아래 본문에 적습니다.

작업 중 `Dev`가 앞서가면 기능 브랜치에서 다음을 실행합니다.

```powershell
git fetch origin
git merge origin/Dev
```

`Library`, `Temp`, `Logs`, `UserSettings`, 재생성 가능한 QA 출력은 커밋하지 않습니다.
게임에서 사용하는 텍스처와 유지하기로 한 카탈로그 미리보기는 QA 출력과 구분합니다.
큰 모델은 Git LFS로 관리하며, Unity를 열기 전 필요한 LFS 본체를 받습니다.

```powershell
git lfs pull
```

새 스크립트는 UTF-8로 저장합니다. 기존 CP949 스크립트의 인코딩 변환은
기능 수정과 섞지 않고 별도 커밋으로 처리합니다.

## 긴급 수정

`main`에서 `fix/이름`을 만들고 `main` 대상 PR로 수정합니다.
병합 후 `main`의 변경을 `Dev`에도 반영하는 PR을 엽니다.

## 관리자 확인

- `main`, `Dev`: PR 필수, 승인 한 명 이상, 강제 푸시·삭제 금지.
- 병합 담당자와 자동 브랜치 삭제 설정을 확인합니다.
- `Dev` → `main` 전에 Unity 컴파일과 주요 씬을 확인합니다.
- 병합 후 두 브랜치의 파일 상태가 같은지 확인합니다.

## 맵과 에셋 공동 작업

- 현재 마을은 큰 프리팹 한 개에 많은 오브젝트가 들어 있습니다. 같은 씬·프리팹의 동시 편집은 작업자끼리 조정하세요. 한 씬은 한 번에 한 명만 수정하는 것을 원칙으로 합니다.
- 이후 맵 편집 구조를 개선할 때는 건물별 프리팹, 구역별 씬으로 나누는 것을 권장합니다. 이번 Git 정리에는 이 변경이 포함되지 않습니다.
- 반복되는 메시와 재질은 공유합니다. 충돌을 피하려고 원본 에셋을 복제하지 않습니다.
- `.meta`를 삭제하거나 다시 만들지 않습니다. 에셋과 `.meta`를 한 변경으로 커밋합니다.
- 씬·프리팹 충돌은 자동으로 한쪽 파일을 선택하지 말고, 담당자와 함께 Unity에서 결과를 확인합니다.
- 고급 병합이 필요하면 [UnityYAMLMerge 공식 안내](https://docs.unity3d.com/6000.4/Documentation/Manual/SmartMerge.html)에 따라 각 개발 환경에 도구를 설정합니다. 이번 설정에서는 사용자별 Unity 설치 경로에 의존하는 병합 드라이버를 강제로 등록하지 않았습니다.

## 제작 도구 취급

에디터 도구는 `CompanyGame/Assets/Scripts/Editor`에 있고, 모두 Unity 위 메뉴에서 실행합니다.

| 메뉴 | 용도 |
| --- | --- |
| CompanyGame → Setup → Build Phone UI Prefab | `Resources/PhoneUI.prefab` 다시 만들기 |
| CompanyGame → Setup → Move Scene Players To Prefab | 씬에 들어간 플레이어를 지우고 `Resources/Player.prefab`으로 모으기 |
| Tools → Company Game → Maps → Validate Maps | 맵 씬 검사 (플레이어 없음, 카메라 1개, `default` 스폰) |
| Tools → Company Game → Maps → Create Small Map | 새 작은 맵 만들기 |
| Tools → Company Game → Characters → Rebuild / Validate Reference Girl·Boy | 캐릭터 메시 재생성·검사 |

예전 `CompanyGame/AgentScripts`와 메뉴 없는 맵 생성·검사 스크립트는 2026-10-04에 제거했습니다. 필요하면 커밋 `ccfd0c6`에서 복원합니다.
Blender 재생성 스크립트는 열린 장면을 비우는 코드가 있으므로 별도 작업 파일에서 실행합니다.

## 이번 폴더 정리의 백업

중첩 Git 정보, 내부의 중복 `ArtSource`·`Docs`와 `.gitignore`는 저장소 밖의
`../companyGame-backups/git-setup-<날짜-시간>/`에 보관합니다.
백업의 `Moves.json`은 원래 위치와 백업 위치를 기록하고, `ProjectFilesBefore.json`은 기존 프로젝트 파일의 SHA-256을 기록합니다.
`RootGitBefore`에는 설정 전 상위 저장소의 Git 정보가 있습니다.

백업을 복원할 때는 현재 파일을 먼저 별도로 보관하고 필요한 항목만 복원하세요.
현재 저장소 안에 백업된 `.git`을 다시 넣으면 중첩 저장소가 재발생합니다.
