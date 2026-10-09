# Downloaded furniture library

Source folder: `C:/서현/3D 모델/` (original files are not moved or modified).

## Unity use

Open `Assets/Art/Items/Furniture/Library/Prefabs` and drag a prefab into a scene.
Prefabs are grouped by furniture function. Storage is divided into General,
Clothing, Food and Books. Unity asset labels include the Korean name and category.
The searchable preview catalog is `CompanyGame/Docs/FurnitureLibrary/Catalog.html`.

These are reusable visual assets with basic box colliders. Classification does not
automatically install runtime seating, storage, cooking, lighting or ownership
behavior. Wire the game's existing components when placing an asset.

## Sources and separation

- The five previously processed furniture collections reuse 61 individually
  separated WhiteWood models/materials from the local `whitewood-furniture` Git
  branch, including the actual Git LFS objects. No branch switch or merge.
- New downloads supply 26 prefabs. The outdoor cafe table and chair are separate;
  its second identical chair is omitted. The picnic table remains a complete set.
- `sofa_company.fbx` and `sofa_company2.fbx` have identical SHA-256 hashes and share
  one prefab. `duplicates.json` records that relationship.
- Textureless models retain a neutral material and are marked in the catalog.
- Models exceeding 80,000 triangles get a separate reduced Unity copy; their
  original files are unchanged. `new_catalog.json` records before/after counts.
- New FBX copies use a bottom-center origin. Unscaled downloads use nominal
  furniture heights from `jobs.json`; Blender assets authored in meters retain
  their dimensions. Final prefab dimensions are checked during import.
- Materials use URP Lit. Metallic/roughness maps are packed into Unity's
  metallic/smoothness format. Imported textures are limited to 2048 pixels.

## Not included

The five ZIPs named 공중전화기, 금괴, 베이커리 카운터, 오락기계, and 화장실 표지판(남여)
contain the BlenderKit add-on, not the named models. No add-on code was executed.
Vehicles, weapons, characters, clothing, buildings, motions and standalone
material packs are outside this furniture library.

## Reproduction

`prepare.py` copies/records selected sources and resolves local LFS objects.
`inspect_models.py` and `export_models.py` run with Blender `--disable-autoexec`.
`BuildFurnitureLibrary.cs` runs only in the isolated Unity validation project;
it generates categorized prefabs and previews, checks their bounds/renderers and
URP materials, and writes `FurnitureLibraryQA/Result.json`.
`catalogue.py` assembles the searchable HTML catalog.
Temporary source copies are ignored by Git to avoid committing redundant downloads.

Validation completed with 87 prefabs and zero import/material errors. Generated
QA reports and contact sheets are not versioned; catalog previews are retained.
The authoring tools expect the downloaded sources listed in jobs.json and a local
WhiteWood source branch/LFS cache. They are not required to use the shipped prefabs.
