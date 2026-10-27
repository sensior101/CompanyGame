"""Package only final deliverables; retain local QA working files outside the archive."""
import json,hashlib,zipfile,collections
from pathlib import Path
ROOT=Path(__file__).resolve().parent
cat=json.loads((ROOT/'catalog.json').read_text(encoding='utf8'))
qa=json.loads((ROOT/'QA/validation.json').read_text(encoding='utf8'))
review=json.loads((ROOT/'quality_review.json').read_text(encoding='utf8'))
assert cat['count']==134 and qa['status']=='PASS' and review['visuallyReviewed']==134
counts=collections.Counter(x['category'] for x in cat['items'])
assert counts=={'Convenience':10,'Korean':17,'Western':15,'Japanese':16,'Chinese':13,'Asian':12,'Dessert':28,'Cafe':23}
for x in cat['items']:
    for key in ['fbx','glb','render']:assert (ROOT/x[key]).is_file(),x
readme=f'''조합 음식 134종 — 무광 세미 로우폴리 3D 개선판 V3
제작일: 2026-10-12

1. 구성
- 편의점 10 / 한식 17 / 양식 15 / 일식 16 / 중식 13 / 아시아 12 / 디저트 28 / 카페 23
- 사용자 표에 명시한 134종만 포함. 표 밖 키위라떼·블루베리라떼는 추가하지 않았습니다.
- CraftedFood_134.blend: 전체 편집 원본, 카테고리별 컬렉션, 검수용 배열.
- Models/FBX: 개별 모델 134개. Models/GLB: 개별 모델 134개.
- Renders: 모델별 실제 Blender 렌더 134장. 배경을 포함한 검수 이미지입니다.
- Previews: 카테고리별 모음 및 전체 라이브러리.
- index.html: 검색·분류·확대·파일 다운로드를 지원하는 오프라인 목록.
- quality_review.html / quality_review.json: 134종 각각의 수정 전후 이미지·발견 사항·개선 내용.
- QA/Before/Renders: V1 이미지 보존. QA/RevisionBefore: 이번 수정 직전 7종 이미지. QA/Views: 주요 19종 측후면 38장.
- Textures: 휴대 가능한 BaseColor / tangent-space Normal PNG. Blender, FBX, GLB에 포함.
- 기존 CraftedFood_134_Matte.zip은 수정 전 백업으로 유지했습니다. 이전 V2 ZIP도 유지하며 이번 수정은 별도 V3 ZIP입니다.
- menu.json / catalog.json: 이름, 조합법, 모델 경로, 메시 정보.
- References: 사용자가 첨부한 참고 이미지 9장, 원본 그대로 보관.

2. 같은 모델에서 색상만 변경한 그룹
- 파스타: 토마토 / 크림 — 2종
- 라떼: 카페 / 딸기 / 초코 / 고구마 / 말차 — 5종
- 빙수: 딸기 / 망고 / 초코 / 말차 / 블루베리 — 5종
- 소프트아이스크림: 딸기 / 망고 / 바닐라 / 초코 / 말차 / 오렌지 — 6종
Blender에서는 그룹별로 하나의 Mesh 데이터 블록을 공유합니다. 토핑·컵·그릇·크기·정점·면까지 동일하며 오브젝트별 재질 색상만 다릅니다. 고유 메시 수는 120개입니다.
각 FBX/GLB는 독립 파일입니다. Unity에서 개별 파일을 임포트하면 자동으로 메시 메모리를 공유하지 않으므로, 통합 시 그룹별 메시 1개와 재질 변형으로 연결하면 됩니다.

3. 모델 사양
- 실제 3D 메시. 이미지 평면이나 빌보드가 아닙니다.
- 단위: 미터. 개별 파일 원점은 X/Y 중심축과 메시 최하단을 기준으로 합니다.
- FBX/GLB는 Y-up으로 내보냈습니다. Blender 원본은 Z-up입니다.
- 모델마다 단일 Mesh 오브젝트와 복수의 색상 재질 슬롯을 사용합니다.
- Roughness 0.88 / Metallic 0 / Specular IOR Level 0.12.
- UV는 면의 주축을 기준으로 투영한 반복 UV입니다. 아틀라스·라이트맵용 UV는 별도입니다.
- 실제 기하와 작은 BaseColor/Normal 텍스처로 빵결·튀김옷·고기·잎의 차이를 표현했습니다.
- 원본 Blender는 이미지를 패킹했으며, FBX·GLB에도 이미지 데이터가 들어 있습니다. Textures는 수정용 사본입니다.
- 삼각형: 최소 {qa['triangles']['min']:,}, 중앙값 {qa['triangles']['median']:,.0f}, 최대 {qa['triangles']['max']:,}.
- Studio의 바닥·조명·카메라는 개별 내보내기 파일에서 제외했습니다.
- 유리·얼음·차·쌀종이는 반투명 무광 표현입니다. 엔진에서 알파 블렌딩/정렬 설정이 필요합니다.

4. 사용
Blender 원본을 열면 카테고리별 배열을 확인할 수 있습니다. 개별 모델은 FBX 또는 GLB로 가져오세요.
Unity에서는 사용할 렌더 파이프라인에 맞게 재질을 연결하고 Smoothness를 낮춰 무광을 유지하세요. Collider, 손 장착 위치, 아이템 정의, 레시피 시스템은 게임 기존 구조에 연결하는 별도 작업입니다.
현재 작업은 아트 원본 제작이며 Unity 에디터 실행, Assets 반영, 씬·프리팹 연결, C# 수정, 커밋·푸시는 하지 않았습니다.

5. 검증
- 메뉴 수·카테고리 수·ID 중복·파일 누락 확인.
- 원본 메시의 유한 좌표, 비영 크기, 바닥 원점, 삼각형 구성, 영면적 면 검사.
- 4개 공용 그룹의 실제 Blender 메시 데이터 공유와 맛별 색상 차이 확인.
- FBX 134개와 GLB 134개를 각각 독립적으로 다시 불러와 메시 수·삼각형 수·크기 비교: {qa['passedRoundTrips']}/268 통과.
- 134개 실제 렌더를 메뉴별로 검토했고 V2 주요 12종과 V3 수정 7종은 측면·후면을 추가 렌더했습니다.
- Blender 텍스처 패킹, UV 유무, FBX·GLB 내 이미지 데이터 포함도 검사했습니다.
- 이전 모델 대비 기하 변경 {review["geometryChanged"]}종. 나머지는 실루엣을 유지하고 재질 또는 파일 검사를 수행했습니다.
- 시각 검토는 디자인 판단이며 사용자 최종 승인을 뜻하지 않습니다.
- 세부 결과: QA/validation.json. 파일 무결성: checksums.json.
- 미검증: Unity 내 화면, 셰이더 변환, 충돌체, 손 장착, 게임 동작, 멀티플레이 및 기기별 성능.

6. 기존 V2 변경 기록 및 재현
수정한 기존 파일: build_crafted.py, refresh_models.py, validate_crafted.py, finalize_library.py, package_preview.cjs, package_assets.py, CraftedFood_134.blend, Models/FBX 및 Models/GLB 각 134개, Renders·Previews·index.html·catalog.json·README·checksums.
신규 파일: Textures의 BaseColor/Normal PNG, quality_review.html/json, Previews/BeforeAfter.png, QA/Before의 수정 전 보존 자료, QA/Views 및 검토 기록. 신규 런타임 스크립트 없음.
menu.json의 메뉴·레시피와 References의 첨부 원본은 변경하지 않았습니다.
기존 ArtSource/Items/Food/mesh3d.py의 lathe_geo, prism_geo, rrect, wob 등 순수 기하 함수를 재사용했습니다. 재현용 원본 사본은 Source/mesh3d.py이며 원본을 수정하지 않았습니다.
재사용: 기존 Geo·mesh_from_geo·메뉴별 build_*·render_item와 패키징/검사 도구를 확장했습니다.
데이터 흐름: 동일한 메뉴 표 → 수정한 기하·무광 재질 → FBX/GLB·수정 전후 렌더 → 독립 파일 재불러오기 검사. 게임의 데이터 흐름 변경 없음.
새 Python/JavaScript 파일은 오프라인 아트 제작·검수·패키징 전용입니다. 게임 공통 기능이나 관리자를 중복 구현하지 않았고 기존 게임 API·저장 데이터·호출부에 영향이 없습니다.

재생성: Blender 5.2.1 LTS에서 build_crafted.py를 --all 인자로 실행.
검사: validate_crafted.py. 원본 배열·전체 렌더: finalize_library.py.
검수 페이지: Node.js + sharp로 package_preview.cjs --gallery 실행.
압축: Python으로 package_assets.py 실행.

7. 이번 V3 수정 (2026-10-12)
- 수정 대상: 돌솥비빔밥, 계란찜, 해물파전, 연어초밥, 참치초밥, 새우초밥, 계란초밥 — 총 7종.
- 돌솥비빔밥: 반복 격자형 나물을 비정형 재료 더미로, 두꺼운 계란 원판을 얇고 굴곡진 흰자와 둥근 노른자로 수정.
- 계란찜: 전용 노란색 재질. 해물파전: 황금빛 반죽과 갈색 구운 얼룩.
- 초밥: 길쭉한 밥알 표면과 밥을 감싸는 토핑. 연어 지방결과 새우 마디는 텍스처에 통합. 계란말이와 김띠 곡면 개선.
- 수정한 제작 도구: build_crafted.py, validate_crafted.py, package_preview.cjs, package_assets.py. 기존 Geo, folded_meat, mesh_from_geo, render_item, refresh_models.py 재사용.
- 신규 스크립트 없음. 신규 파일은 전용 텍스처, 이번 수정 전 이미지, 14장 측후면 렌더, revision_review.html, Previews/RequestedChanges.png 및 범위 검사 기록.
- 7종 모델 및 렌더, 기존 전체 .blend, 카탈로그, 미리보기, QA/검토 기록, 문서·체크섬 갱신. 전체 134종 수와 레시피 유지.
- 재현: Blender --background --factory-startup --addons cycles,io_scene_fbx,io_scene_gltf2 --python refresh_models.py -- StoneBibimbap SteamedEgg SeafoodPancake SalmonSushi TunaSushi ShrimpSushi EggSushi
- 비교 페이지: package_preview.cjs --gallery --quality --revision
- 데이터 흐름: 기존 메뉴 정의 → 기존 생성기 확장 → 동일 경로의 모델/텍스처/렌더 → 재불러오기 검증. 게임 시스템 데이터 흐름 변경 없음.
- 중복 공통 기능 및 신규 관리자 없음. 기존 게임 API, 저장 데이터, 이벤트, UI, 호출부 변경 없음.
- Unity 적용과 게임 내 검증은 수행하지 않았습니다.
'''
(ROOT/'README.txt').write_text(readme,encoding='utf8')
def include(p):
    rel=p.relative_to(ROOT)
    if not p.is_file() or p.name in ['checksums.json'] or p.suffix in ['.blend1','.log','.pyc']:return False
    if '__pycache__' in rel.parts:return False
    if rel.parts[0]=='QA':
        keep=(p.name in ['validation.json','review_notes.json','visually_reviewed.json','views.json','revision_scope.json'] or (len(rel.parts)>2 and rel.parts[1] in ['Views','RevisionBefore']) or (len(rel.parts)>3 and rel.parts[1:3]==('Before','Renders')))
        if not keep:return False
    return True
files=sorted(p for p in ROOT.rglob('*') if include(p))
checksums={str(p.relative_to(ROOT)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
(ROOT/'checksums.json').write_text(json.dumps(checksums,ensure_ascii=False,indent=2),encoding='utf8')
files.append(ROOT/'checksums.json')
out=ROOT.parent/'CraftedFood_134_Matte_V3.zip'
with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for p in files:z.write(p,ROOT.name+'/'+str(p.relative_to(ROOT)))
with zipfile.ZipFile(out) as z:
    assert z.testzip() is None
    for extension,expected in [('.fbx',134),('.glb',134)]:assert sum(n.endswith(extension) for n in z.namelist())==expected
    assert len(z.namelist())==len(files)
print(json.dumps({'archive':str(out),'files':len(files),'bytes':out.stat().st_size,'sha256':hashlib.sha256(out.read_bytes()).hexdigest(),'menuCount':cat['count'],'uniqueGeometry':cat['uniqueGeometry']}))
