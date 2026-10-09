# Supplied library interior V4

Source: user-provided `01e0f9ce-f307-4a73-8cd2-6b6d7448706d.zip`.
The original Blender and GLB files are preserved under `Supplied/`.
The existing CivicDistrict library exterior is retained.

## Delivered Unity assets

- `Assets/Art/WorldDistricts/CivicLibraryV4/CivicLibraryV4.prefab`
- `Assets/Scenes/Interiors/CivicLibraryInterior.unity`
- CivicDistrict entrance → `library_entry`; interior exit → `library_exit`.
- 62 seating anchors, 261 authored mesh colliders, 55,660 visible triangles.
- 19 URP Lit materials, including transparent glass.
- Existing persistent player, scene loading, interaction UI, and seating components.
- No supplied demo player, demo controls, additional packages, or replacement managers.

## Branch-specific integration

The current `feature/library` branch lacked seating. With explicit user approval,
the existing Seat, PlayerSeating, SeatInteraction, WorldObjectType and
FurnitureFunction were ported. WorldObject retains only the dependencies needed
by this branch; unrelated inventory, book, storage and NPC systems were not ported.
The existing PlayerInventory and PlayerInteraction attach and prioritize seating.
DaldongneAvatarMotion supplies the existing seated pose; PlayerSeating refreshes
its avatar references at sit time to handle visuals created after Awake.

## Verification

Unity 6000.4.5f1, isolated copy of the current branch:

- 구조 검증: 12 structural, material, bench and CharacterController checks.
- 런타임 검증: 16 runtime checks, including actual Space input,
  occupied-seat prompt suppression, seated avatar pose, indoor seating,
  persistent-player scene round trip, and return spawn.
- QA screenshots are Unity renders, not Blender previews.
- Runtime QA uses a separate product name and save directory.
- Headless QA input focus is configured only in the editor-only test harness.
- Unity Search emitted an editor indexing exception; all gameplay checks completed.

`export_v4.py` exports the supplied Blender source with auto-execution disabled.
`UnityLibraryV4Integration.cs` runs only inside the isolated validation project.
`LibraryInteriorRuntimeQA.cs` is editor-only validation tooling and is not installed
in the delivered game's Assets folder.

Future authoring workflow is documented in `ArtSource/AGENTS.md`.

Generated QA reports/screenshots are excluded from version control. Supplied model files and final integration tools are retained; obsolete one-time migration scripts are omitted.
