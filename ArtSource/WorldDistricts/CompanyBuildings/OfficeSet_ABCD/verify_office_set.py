"""A-D export QA using the existing Office01 round-trip implementation.

Run after all four models have been generated:
    blender --background --python-exit-code 1 --python verify_office_set.py

Writes each model's Validation/export_roundtrip.json and a combined set report.
"""
import importlib.util
import json
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
MODELS = HERE.parent
MODEL_FOLDERS = ('02_SlimGlass', '03_UrbanTerrace',
                 '04_MinimalDark', '05_ClassicStone')


def main():
    validator_path = MODELS / '01_LimestoneTower' / 'Validation' / 'verify_roundtrip.py'
    spec = importlib.util.spec_from_file_location('companygame_office_roundtrip', validator_path)
    validator = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(validator)
    combined = {'checked_at_utc': datetime.now(timezone.utc).isoformat(),
                'validator': str(validator_path), 'expected_export_count': 8,
                'models': [], 'exports': []}
    for folder in MODEL_FOLDERS:
        model_dir = MODELS / folder
        try:
            report = validator.validate_model(model_dir)
            combined['exports'].extend(report['exports'])
            combined['models'].append({
                'folder': folder, 'asset': report['asset'],
                'report': str(model_dir / 'Validation' / 'export_roundtrip.json'),
                'passed': report['passed'],
            })
        except Exception as error:
            combined['models'].append({'folder': folder, 'passed': False,
                                       'error': f'{type(error).__name__}: {error}'})
    combined['passed'] = (
        len(combined['exports']) == combined['expected_export_count'] and
        all(model['passed'] for model in combined['models']))
    destination = HERE / 'Validation' / 'export_roundtrip.json'
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(combined, ensure_ascii=False, indent=2), encoding='utf-8')
    print('OFFICE_SET_ROUNDTRIP_' + ('PASS' if combined['passed'] else 'FAIL'))
    if not combined['passed']:
        raise RuntimeError(f'Office set export validation failed: {destination}')


if __name__ == '__main__':
    main()
