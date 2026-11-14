# ArtSource

게임에 들어가는 모델의 원본 작업 폴더다. Unity가 실제로 쓰는 결과물은 `CompanyGame/Assets/Art` 아래에 있다.

## Git에 올리는 것과 올리지 않는 것 (2026-11-13부터)

| Git에 올림 | Git에 올리지 않음 (각자 PC와 공유 드라이브에 보관) |
|---|---|
| `build_*.py` 등 생성 스크립트, `.cs` 도구 | `.blend`, `.blend1`, `.fbx`, `.glb`, `.gltf`, `.obj` |
| `README.md`, 메모 `.txt` | `.zip` 묶음 |
| 수치를 적은 작은 `.json`, `.csv` | 렌더·참고 이미지 `.png`, `.jpg`, `.webp`, `.gif`, `.exr`, `.psd`, `.tga` |
| | 영상 `.mp4`, `.bin` |

- 규칙은 루트 `.gitignore`의 "Heavy art sources" 부분에 있다.
- 이 규칙 전에 이미 커밋한 원본(약 800MB, 대부분 LFS)은 계속 Git에 남아 있다.
- 무거운 원본은 잃어버리지 않도록 공유 드라이브에 폴더 구조 그대로 올려 둔다. 다시 받을 때는 같은 경로에 풀면 스크립트와 README가 그대로 맞는다.
- 백업 위치: OneDrive `companyGame-ArtSource/ArtSource/` (2026-11-13 첫 복사). 이 폴더를 저장소 루트에 그대로 덮어 넣으면 된다. 새 원본을 만들면 같은 자리에 추가로 복사한다.
- 스크립트로 다시 만들 수 있는 파일(렌더, 미리보기, Unity로 내보낸 FBX)은 원본을 지워도 스크립트를 다시 돌리면 된다. 손으로 만든 `.blend`와 외부에서 받은 원본만 꼭 백업한다.

## 작업 규칙
모델링 작업 방식은 `AGENTS.md`를 따른다.
