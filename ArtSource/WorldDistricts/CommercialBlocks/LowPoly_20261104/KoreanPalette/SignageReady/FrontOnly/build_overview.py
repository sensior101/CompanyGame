"""Reuse the original contact sheet for eight front-only building variants."""
from pathlib import Path
HERE=Path(__file__).resolve()
original=HERE.parents[3]/'build_overview.py'
code=original.read_text(encoding='utf8')
code=code.replace('COMPANY GAME  /  LOW-POLY COMMERCIAL BLOCKS','COMPANY GAME  /  ONE CONTINUOUS FRONT SIGN')
code=code.replace('12 × 8 m · 3층 · 무광 단색 재질 · 기존 8종 설계 기반  /  Blender 프리뷰','건물마다 정면 간판 한 장  /  측면 간판 없음  /  돌출 로고는 별도 선택 에셋  /  Blender 프리뷰')
code=code.replace("원본 대비 {row['triangleReductionPercent']:.1f}% 감소","전면 간판 1장 · 본체 유지")
exec(compile(code,str(original),'exec'),{'__file__':str(HERE),'__name__':'__main__'})
