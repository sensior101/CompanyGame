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

## 첫 업로드

최초 Git 정리 시점에는 로컬 커밋과 원격 브랜치가 없었습니다.
그 이후 다른 사람이 먼저 원격에 커밋했다면 아래 초기 업로드 절차를 그대로 진행하지 말고 원격 이력부터 확인하세요. 강제 푸시는 사용하지 않습니다.

저장소 루트에서 아래 순서로 실행합니다.

```powershell
# 본인 Git 작성자 정보가 등록되어 있는지 확인합니다.
git var GIT_AUTHOR_IDENT

# 최초 등록할 파일을 검토합니다.
git add .
git status
git diff --cached --stat
git lfs ls-files

# 검토 후 첫 커밋과 업로드를 진행합니다.
git commit -m "Add Unity project and shared development sources"
git push -u origin main
```

작성자 정보가 없다면 본인의 이름과 GitHub 이메일 또는 GitHub에서 제공하는 비공개 이메일로 설정합니다.

```powershell
git config --local user.name "본인 이름"
git config --local user.email "본인 GitHub 이메일"
```

`git status`에 `Library`, `Temp`, `Logs`, `UserSettings`, `__pycache__`, `.unitypackage`가 나타나지 않아야 합니다.
LFS 파일의 본체는 pre-push 훅이 업로드합니다. `.gitattributes`도 반드시 커밋합니다.

## 일상 작업

아래 명령은 로컬 변경을 먼저 커밋하거나 정리한 상태에서 실행합니다.

```powershell
git switch main
git pull --ff-only
git switch -c feature/player-interaction
```

각자 작업에 맞는 브랜치 이름을 사용합니다. 작업 후에는 Unity에서 씬과 프로젝트를 저장하고 변경 파일을 확인합니다.

```powershell
git status
git diff --stat
git add .
git diff --cached --stat
git commit -m "Implement player interaction"
git push -u origin HEAD
```

GitHub에서 Pull Request를 열어 검토 후 합칩니다.
다른 사람의 변경을 받을 때는 Unity에서 편집한 내용을 먼저 저장하고 로컬 변경을 정리하세요.
LFS 다운로드를 건너뛰었던 경우 Unity를 열기 전에 `git lfs pull`을 실행합니다.

## 맵과 에셋 공동 작업

- 현재 마을은 큰 프리팹 한 개에 많은 오브젝트가 들어 있습니다. 같은 씬·프리팹의 동시 편집은 작업자끼리 조정하세요.
- 이후 맵 편집 구조를 개선할 때는 건물별 프리팹, 구역별 씬으로 나누는 것을 권장합니다. 이번 Git 정리에는 이 변경이 포함되지 않습니다.
- 반복되는 메시와 재질은 공유합니다. 충돌을 피하려고 원본 에셋을 복제하지 않습니다.
- `.meta`를 삭제하거나 다시 만들지 않습니다. 에셋과 `.meta`를 한 변경으로 커밋합니다.
- 씬·프리팹 충돌은 자동으로 한쪽 파일을 선택하지 말고, 담당자와 함께 Unity에서 결과를 확인합니다.
- 고급 병합이 필요하면 [UnityYAMLMerge 공식 안내](https://docs.unity3d.com/6000.4/Documentation/Manual/SmartMerge.html)에 따라 각 개발 환경에 도구를 설정합니다. 이번 설정에서는 사용자별 Unity 설치 경로에 의존하는 병합 드라이버를 강제로 등록하지 않았습니다.

## 제작 도구 취급

`CompanyGame/AgentScripts`는 맵 제작·검증 과정에서 사용한 C# 코드 보관 폴더입니다.
`Assets` 밖에 있으므로 Unity가 자동으로 컴파일하지 않습니다.
여러 파일에 `DaldongneWarmImport`, `DaldongneWarmPolish` 같은 동일 클래스가 중복 선언되어 있어, 폴더 전체를 `Assets/Editor`로 복사하면 컴파일 충돌이 발생할 수 있습니다.

일반 팀원은 이미 저장된 씬·프리팹·메시를 사용하면 됩니다.
제작 도구를 재사용하려면 필요한 버전과 의존 파일만 선별하고 중복 클래스를 정리한 뒤 Editor 도구로 통합해야 합니다.
여러 스크립트는 Unity 프로젝트를 기준으로 `../ArtSource/Daldongne`에 결과를 저장하므로 현재 저장소 배치를 유지하세요.
Blender 재생성 스크립트는 열린 장면을 비우는 코드가 있으므로 별도 작업 파일에서 실행합니다.

## 이번 폴더 정리의 백업

중첩 Git 정보, 내부의 중복 `ArtSource`·`Docs`와 `.gitignore`는 저장소 밖의
`../companyGame-backups/git-setup-<날짜-시간>/`에 보관합니다.
백업의 `Moves.json`은 원래 위치와 백업 위치를 기록하고, `ProjectFilesBefore.json`은 기존 프로젝트 파일의 SHA-256을 기록합니다.
`RootGitBefore`에는 설정 전 상위 저장소의 Git 정보가 있습니다.

백업을 복원할 때는 현재 파일을 먼저 별도로 보관하고 필요한 항목만 복원하세요.
현재 저장소 안에 백업된 `.git`을 다시 넣으면 중첩 저장소가 재발생합니다.
