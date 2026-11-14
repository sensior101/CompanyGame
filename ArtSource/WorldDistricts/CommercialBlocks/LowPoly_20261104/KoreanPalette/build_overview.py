"""Reuse the previous contact-sheet generator for the color-only review renders."""
from pathlib import Path
HERE=Path(__file__).resolve()
original=HERE.parent.parent/'build_overview.py'
code=original.read_text(encoding='utf8')
code=code.replace('COMPANY GAME  /  LOW-POLY COMMERCIAL BLOCKS','COMPANY GAME  /  KOREAN URBAN PALETTE')
code=code.replace('12 × 8 m · 3층 · 무광 단색 재질 · 기존 8종 설계 기반  /  Blender 프리뷰','12 × 8 m · 3층 · 승인된 형태 유지 · 한국 상가 외장 색상 조정  /  Blender 프리뷰')
exec(compile(code,str(original),'exec'),{'__file__':str(HERE),'__name__':'__main__'})
