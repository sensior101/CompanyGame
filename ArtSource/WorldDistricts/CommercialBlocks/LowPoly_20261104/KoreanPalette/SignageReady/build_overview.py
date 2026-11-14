"""Reuse the existing eight-building contact sheet for the signage-ready assets."""
from pathlib import Path
HERE=Path(__file__).resolve()
original=HERE.parents[2]/'build_overview.py'
code=original.read_text(encoding='utf8')
code=code.replace('COMPANY GAME  /  LOW-POLY COMMERCIAL BLOCKS','COMPANY GAME  /  SIGNAGE READY')
code=code.replace('12 × 8 m · 3층 · 무광 단색 재질 · 기존 8종 설계 기반  /  Blender 프리뷰','건물마다 전면 2곳 · 측면 2곳 · 돌출 1곳  /  교체용 빈 간판 보드  /  Blender 프리뷰')
code=code.replace("원본 대비 {row['triangleReductionPercent']:.1f}% 감소","간판 5곳 · 본체 형상 유지")
exec(compile(code,str(original),'exec'),{'__file__':str(HERE),'__name__':'__main__'})
