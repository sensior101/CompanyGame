"""Reuse the original eight-building contact sheet for cleared ground-floor models."""
from pathlib import Path
HERE=Path(__file__).resolve()
original=HERE.parents[4]/'build_overview.py'
code=original.read_text(encoding='utf8')
code=code.replace('COMPANY GAME  /  LOW-POLY COMMERCIAL BLOCKS','COMPANY GAME  /  GROUND FLOOR CLEAR')
code=code.replace('12 × 8 m · 3층 · 무광 단색 재질 · 기존 8종 설계 기반  /  Blender 프리뷰','1층 화분과 식재 제거  /  상층 장식 유지  /  전면 간판 한 장  /  Blender 프리뷰')
code=code.replace("원본 대비 {row['triangleReductionPercent']:.1f}% 감소","1층 화분 제거 · 상층 유지")
exec(compile(code,str(original),'exec'),{'__file__':str(HERE),'__name__':'__main__'})
