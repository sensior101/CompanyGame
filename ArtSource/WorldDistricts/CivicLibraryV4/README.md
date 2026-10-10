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
- 검증 화면은 Unity에서 캡처했으며, 생성 보고서·스크린샷은 저장소 정리 시 보관하지 않는다.
- Runtime QA uses a separate product name and save directory.
- Headless QA input focus is configured only in the editor-only test harness.
- Unity Search emitted an editor indexing exception; all gameplay checks completed.

`export_v4.py` exports the supplied Blender source with auto-execution disabled.
`UnityLibraryV4Integration.cs` runs only inside the isolated validation project.
`LibraryInteriorRuntimeQA.cs` is editor-only validation tooling and is not installed
in the delivered game's Assets folder.

Future authoring workflow is documented in `ArtSource/AGENTS.md`.

Generated QA reports/screenshots are not retained in version control. Supplied model files and final integration tools are retained; validation tools regenerate local output when run.

## Library services on Dev (2026-10-10)

The existing interior has a female and male librarian at the entrance desk and
two female cafe employees. The cafe cashier uses the previously authored
RetailPOS Blender model. Its metre mesh uses `ModelImporter.useFileScale=false`
to avoid the FBX centimetre tag shrinking it by 100. The old counter screen and
stand are disabled scene overrides. `ConfigureLibraryServices.cs` is the Editor
integration recipe.

### Shared UI and transactions

- All shops use `TradeWindow` and `TradeSession`. Library-specific click-to-buy
  rendering was removed, including `TradeWindow.Library.cs`. Buying and author
  withdrawal use ordinary sessions; books are carried on the existing cursor
  and dropped into the player's chosen inventory slot.
- Offer rows show icons only. Hovering payment shows the price; hovering the
  output shows its item name or book title. Prices and names are not printed
  over the grid. The common window automatically pages in groups of 18 offers.
- A zero-won payment resolves to the existing silver coin icon but requires no
  currency item. This rule is shared by TradeSession and CashService.
- `RentalTradeSession` adds availability/dimming and the tooltip suffix
  `----- / 누군가가 대여중입니다.` to the same window and drag flow. It has no
  scene-name dependencies. A catalogue fulfilment checks availability again on
  placement, so simultaneous previews cannot commit the same lending copy.
- `ITradeFulfillment` supplies item identity and final domain updates (loans,
  listings and royalties). Payment validation and drag placement remain in the
  common session. Cancelling an uncommitted library preview changes no money,
  loan, listing or royalty balance. Unpaid previews are not persisted as owned
  books.

### Publication and withdrawal

The four librarian choices are buy, borrow, publish and withdraw.

Publication uses `InventoryItemSelector.cs`, not a trade window. Its two rows of
inventory slots and wallet display are built by the existing InventoryUI widget
builders. A supplied predicate permits only the player's authored nonempty
originals, excluding blank books, other authors' books and loans. Selection hands
that exact book to the NPC via the common inventory handoff. Pending handoffs stay
included in book saves.

The NPC asks for confirmation. Yes charges 10,000 won from the displayed wallet
(BankManager) and lists the book at 20,000 won. Exactly 10,000 is sufficient. No,
Escape, leaving range or insufficient funds returns the original. Publication
never pays the author 10,000 won. BankManager.TrySpend and the existing BankWallet/BankManager.AddMoney own all
publication expenses and royalty income. MoneyChangeReason identifies publication
fees, royalties and late fees. ChatUIManager subscribes to MoneyChanged and uses
its shared system-message path for chat history and popups, including season-end
settlements without an open NPC conversation. LibraryDesk only presents dialogue;
it does not emit duplicate money logs. Failed payments emit no success message.

Author withdrawal is a free ordinary trade containing only that author's listed
works. Dropping the original into inventory delists it, recalls active loans and
pays accumulated royalties into the wallet. The NPC states the title and paid
amount. Already purchased copies remain, while future player/NPC purchases fail.

### Calendar and royalties

- Blank book: 1,000 won. Authored book: 20,000 won. Library purchase payments
  follow the same physical currency denomination rule as other ordinary shops.
- Each work has one shared lending copy, and each borrower has one active loan.
  The first ten elapsed game days are free; days 11–30 accrue 200 won per day.
  Day 30 adds five penalty points and automatically recalls the loan.
- Player and NPC purchases both accrue 4,000 won for the author. Settlement is
  midnight ending each 20-day season, or immediately on author withdrawal.
- Cafe uses the existing NpcTrader and DialogueData, with PlayerInteraction opening
  the ordinary shop after the greeting: coffee 3,000, tea 2,500 and cookies 1,500 won.
- Simple NPC speech duration grows with text length. Scripted confirmation and
  selection speech reuse DialogueManager without mutating NPC scene dialogue.

### Persistence and network boundary

Book instances and the ledger save as `books.json` and `library.json` in the
existing persistent data directory. Clock progress uses a PlayerPrefs checkpoint.
Wallet payments use BankManager; no general economy save system is introduced.
The host owns catalogue writes. World-state request/replication transport is not
yet present in this project. `PurchaseForNpc` is a paid transaction entry point,
not autonomous NPC shopping.

### Verification

- `VerifyLibraryLedger.cs`: 출판비·반납·무료 거래·인세·만료·권한·아이템 ID를 검사한다.
  Unity CLI `eval_file`로 편집 모드에서 실행하며, 임시 장부와 플레이어 상태를 복원한다.
- `VerifyLibraryInput.cs`: 선택 UI·확인/취소·금액 부족·거래 드래그·툴팁·페이지·채팅 로그를
  플레이 모드에서 검사한다. 검사 후 임시 데이터·입력 장치·카메라 설정을 복원한다.
- `VerifySharedTradeScenes.cs`: 독립 임시 씬에서 공통/무료 거래·대여·툴팁·페이지를 검사하고
  원래 씬으로 복귀한다. 경제·범죄 로그의 공통 경로는 `Docs/Architecture.md`에 정리되어 있다.
- `QA/`는 위 검증 도구가 생성하는 로컬 출력 경로다. 기존 보고서·스크린샷은 정리했으며
  버전 관리하지 않는다. 검증 코드는 유지한다.
- Existing runtime C# files were reorganized with their GUIDs and assemblies intact:
  book views in `Gameplay/Item/Book/UI`, world drops in `Gameplay/Item/World/UI`,
  trade models in `Gameplay/Trade`, and trade views in `UI/Trade`. Two `.asmref`
  files retain the existing UI assembly for category-specific views; no additional
  runtime script or manager was introduced. See `Docs/Architecture.md` for the
  157-file responsibility audit, inventory ownership and shared JSON write path.
- Existing inventory-preview bind-pose and play-stop cleanup messages predate
  these changes; they are not claimed as fixed here.

Existing book/dialogue dependencies came from `feature/books` (0c7cd8a) and
`shared-interactions` (290e818). RetailPOS source came from
`feature/retail-pos-model` (14f23e8). The ongoing bicycle work remains in place.
