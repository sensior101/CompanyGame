# 멀티플레이 코딩 규칙

기능을 새로 만들거나 고칠 때 멀티 구조에 맞게 짜는 방법입니다. 설계 배경과 단계별 기록은 [Multiplayer_Redesign.md](Multiplayer_Redesign.md), 폴더·어셈블리 구조는 [Architecture.md](Architecture.md)를 봅니다. 기준: `feature/multiplayer-redesign` 브랜치, 2026-10-19.

## 1. 먼저 알아 둘 것

- **혼자 하기도 서버를 거칩니다.** 혼자 하기, 서버 열기, 전용 서버 모두 같은 서버 코드(`GameServer` + 게임 쪽 `ServerWorld`)를 씁니다. "혼자일 때만 되는 기능"은 만들지 않습니다. 혼자 하기에서 되면 멀티에서도 되어야 합니다.
- **돈·소유권·아이템은 서버가 정합니다.** 클라이언트는 요청만 보내고, 서버가 검사해서 바꾼 결과로 화면을 맞춥니다. 공개 서버라 클라이언트가 보낸 값은 믿지 않습니다.
- **메시지 종류는 늘리지 않습니다.** 서버와 오가는 것은 엔티티 상태(생성·변경·삭제)와 엔티티 이벤트 두 가지입니다. 기능마다 전용 패킷을 만들지 않습니다.
- 오프라인 경로(에디터에서 맵 씬을 바로 Play)는 개발용입니다. 이 경로에서만 되는 코드는 버그입니다.

## 2. 무엇을 어디서 판정하나

| 대상 | 판정 | 방법 (아래 절) |
| --- | --- | --- |
| 내 캐릭터 이동·애니메이션·든 물건 | 그 플레이어 | 엔티티 상태 (3) |
| 내가 모는 차, 던진 물건의 물리 | 주인 플레이어. 결과만 보냄 | 엔티티 상태 (3) |
| 돈(은행·지갑), 인벤토리, 주식 | 서버 | 개인 요청 `p.*` + 장부 (4.2) |
| 소유권, 땅, 회사, SNS, 신고 | 서버 | 공유 요청 (4.1) |
| 드롭 줍기, 탈것 탑승 자리 | 서버 (먼저 온 요청만 성공) | 요청 + 서버가 만든 엔티티 (3, 4) |
| 시계, 주가 | 서버 | 시계·월드 섹션 (5) |
| 시민·교통 | 각 PC가 똑같이 계산 | 시드 + 월드 시계 (6) |
| 채팅 | 서버가 모두에게 전달 | `WorldStateSync.SendChat` (4.3) |

## 3. 공유 오브젝트: NetEntity + NetBehaviour

다른 사람에게 보여야 하는 오브젝트에는 `NetEntity`를 붙이고, 공유할 값은 `NetBehaviour`를 상속한 컴포넌트에 둡니다. 예: `NetTransform`(위치), `AvatarState`(외형), `DropState`(드롭·탈것).

```csharp
public sealed class DoorState : NetBehaviour
{
    public bool open;
    public override void Write(BinaryWriter w) => w.Write(open);
    public override void Read(BinaryReader r) { open = r.ReadBoolean(); Apply(); }
    public void Toggle() { if (!IsOwner) return; open = !open; Apply(); MarkDirty(); }
    void Apply() { /* 화면에 반영 */ }
}
```

- `Write`/`Read`는 **항상 컴포넌트의 전체 상태**를 씁니다. 변화분만 보내지 않습니다. 한 번 빠져도 다음 상태가 덮어씁니다.
- 값을 바꾼 쪽이 **주인일 때만** `MarkDirty()`를 부릅니다. 0.1초 안에 같은 맵 사람에게 갑니다. 주인이 아니면 아무 일도 안 합니다.
- 순간 동작(공격, 상호작용)은 `SendEvent(id, target, write)` → 받는 쪽은 `OnEvent(id, sender, r)`.
- 만들기: `SyncClient.Instance.Spawn(prefabKey, instance, flags)`. 다른 PC는 같은 키로 프리팹을 만들므로 `SyncClient.PrefabFactory`에 키를 등록합니다(`DropState.Register` 참고). 한 GameObject의 NetBehaviour 순서는 모든 PC에서 같아야 합니다(같은 프리팹).
- 주인이 나가도 남아야 하는 것(놓아둔 차)은 `EntityFlags.PersistOnLeave`. 서버가 주인이 되고 월드 파일에 저장됩니다.
- 남의 엔티티를 움직이려면 `Claim` → 서버가 허락하면 주인이 바뀜(`NetEntity`의 Owner 알림). 끝나면 `Release`.
- 상태 하나(blob)는 **4KB 이하**(`SyncProtocol.MaxBlob`). 큰 데이터(책 본문 등)는 서버에 두고 요청으로 받습니다(12.10.3).
- 자주 바뀌는 값은 JSON 말고 이진(`BinaryWriter`)으로 씁니다. 안 바뀌면 `MarkDirty`를 부르지 않습니다.

## 4. 서버에 바꿔 달라고 하기: 요청(Op)

### 4.1 공유 월드 요청 (소유권·땅·회사·SNS·신고)

매니저는 이 모양을 따릅니다(`SnsManager.ToggleLike`).

```csharp
public bool ToggleLike(string postId)
{
    if (SharedWriteGate.InRoom) // 서버에 캐릭터로 접속 중: 요청만 보냄
        return SharedWriteGate.TrySend("sns.like", new SharedOpArgs { a = postId, b = GameSession.LocalPlayerId });
    return LikeAs(postId, settings.localUserId); // 오프라인(개발용)만 바로 적용
}
```

새 요청 종류를 추가할 때 고치는 곳:

1. **클라이언트**: 매니저에서 `SharedWriteGate.TrySend(kind, args)`. 서버에 있을 때 **로컬 값을 직접 바꾸지 않습니다.** 결과는 서버가 보내는 섹션으로 옵니다. `SendFailed`면 아무것도 적용하지 않습니다.
2. **서버 허용 목록**: `ServerWorld.Allowed`에 kind를 넣습니다. 없으면 "처리할 수 없는 요청입니다"로 거절됩니다.
3. **서버 적용**: `WorldOps.Apply`에 case를 넣습니다. 요청한 계정(`from`)이 그 행동을 할 수 있는지 먼저 검사합니다(`WorldOps`의 `a.b == from` 검사처럼). 실패하면 한국어 이유 문자열을 돌려줍니다. 성공은 `null`.
4. **완료 문구**: 필요하면 `WorldStateSync.DoneText`에 성공 메시지를 넣습니다.

인자는 `SharedOpArgs`(`a`~`d` 문자열, `n` long, `f` bool) 하나로 넘깁니다. 이름처럼 서버가 알 수 있는 값은 클라이언트가 보낸 것을 쓰지 않고 서버가 채웁니다(`WithAuthorName` 참고).

### 4.2 개인 요청 (돈·인벤토리·주식): `p.*`와 장부

- 돈과 인벤토리는 서버의 장부(`ServerLedger`)가 정본입니다. 클라이언트는 `p.*` 요청을 보내고, 서버가 장부를 바꾼 뒤 Ledger 이벤트로 내 은행·인벤토리를 덮어씁니다.
- 새 개인 요청은 `ServerLedger.Kinds`에 넣고 `ServerLedger.Apply`에서 처리합니다. 서버는 **수량·잔액·소유를 반드시 확인**합니다(없는 아이템을 팔거나 음수 금액 등).
- 돈은 두 종류입니다: 은행 계좌(`BankManager`)와 지갑 현금(인벤토리의 지폐). 합치지 않습니다.
- 클라이언트에서 `BankManager.AddMoney`나 `InventoryState.TryAdd`를 직접 불러 "결과를 만드는" 코드를 새로 쓰지 않습니다. 화면을 먼저 바꿔야 하면, 서버 답이 와서 장부로 덮어써져도 맞게 되는지 확인합니다.

### 4.3 채팅·관리자 명령

- 일반 채팅: `WorldStateSync.SendChat(text)` → 서버가 캐릭터 이름을 붙여 모두에게 보냄 → `WorldStateSync.Chatted` 이벤트. 내 줄도 서버에서 돌아와 찍힙니다.
- `/명령`: `WorldStateSync.SendAdmin` → `ServerAdmin`이 권한을 서버의 계정으로 판정합니다.
- 서버가 한 사람이나 모두에게 알릴 때: `ServerWorld.Notice(to, text)`(0 = 모두).

## 5. 서버가 보내는 월드 상태

- 시계·주가·회사·소유권 등 공유 상태는 서버가 `WorldSection`(월드 엔티티의 섹션)으로 보냅니다. 클라이언트는 `WorldStateSync.OnSection`에서 받아 상태를 통째로 넣습니다(`WorldSave.ApplySection`, 시계는 `ApplyClock`). 이 경로는 저장 데이터를 불러오는 함수를 쓰므로 요청(`TrySend`)을 다시 보내지 않습니다. 새 매니저도 "섹션 데이터로 상태 덮어쓰기" 함수를 따로 두고 거기서는 요청을 보내지 않게 합니다. (서버가 요청을 적용하는 동안에는 `SharedWriteGate.Applying`이 켜져 매니저가 바로 씁니다.)
- 새 공유 상태를 추가하면 섹션 하나를 늘리거나 기존 섹션 데이터에 넣습니다. 섹션 데이터는 서버만 씁니다.
- 시간에 따라 바뀌는 것은 **월드 시계**를 씁니다. 캐릭터 나이·배고픔 같은 개인 진행은 **캐릭터 시계**(접속한 동안만 흐름)를 씁니다. `Time.time`이나 PC 시각으로 게임 진행을 정하지 않습니다.

## 6. 모든 PC가 똑같이 계산하는 것 (시민·교통)

- 시민·교통은 메시지 없이 각 PC가 **시드(월드 id 해시) + `NpcClock`(월드 초)**로 같은 결과를 그립니다.
- 이 코드 안에서는 `UnityEngine.Random`, `Time.deltaTime` 누적, 물리 결과, 프레임 순서에 따라 달라지는 값을 쓰지 않습니다. 같은 시드와 시각이면 언제나 같은 위치가 나와야 합니다.
- 플레이어 앞에서 멈추기 같은 반응은 각 PC의 그리기에서만 하고, 계획표 자체는 바꾸지 않습니다.

## 7. 저장

- 월드와 그 월드의 캐릭터는 서버의 `worlds/<월드 id>/world.dat` 하나에 저장됩니다(`WorldStore`). 월드 이름은 같은 폴더의 `name.txt`.
- 캐릭터 진행(돈, 인벤토리, 위치)은 **서버에만** 저장합니다. 개인 PC 파일(`player/<id>.json`)은 오프라인 개발 경로용입니다.
- `world.dat` 형식을 바꾸면 `WorldStore.FileVersion`을 올리고, 예전 버전 파일도 읽히게 합니다(지금 1~3을 모두 읽음).

## 8. 바꾸면 버전을 올리는 것

| 바꾼 것 | 올릴 것 | 결과 |
| --- | --- | --- |
| 서버-클라이언트 메시지 형식, 새 이벤트 번호, 섹션 형식 | `SyncProtocol.Version` | 다른 버전 빌드는 "버전이 달라" 거절됨. 공개 목록도 버전별로 나뉨 |
| `world.dat` 형식 | `WorldStore.FileVersion` | 예전 파일 읽기 코드를 남김 |

버전을 올리면 팀원과 시험하는 PC 모두 새 빌드가 필요합니다. 커밋 메시지에 적어 둡니다.

## 9. 어셈블리 방향

```
Core → Economy → Gameplay → World → Player → UI
NetCore → Net → Player → UI
Gameplay → NPC → UI
```

- 화살표 반대로는 참조할 수 없습니다(컴파일 오류). 아래 계층이 위를 알아야 하면 Core나 Economy에 훅(`Func`/`Action` 정적 필드)을 두고 위에서 채웁니다. 예: `SharedWriteGate.ServerSender`를 Player의 `WorldStateSync`가 채움.
- `NetCore`는 Unity 게임 코드를 모릅니다(프로토콜·전송·서버 본체·월드 파일). 게임 규칙은 Player의 `ServerWorld`·`ServerLedger`에 둡니다.

## 10. 하지 말 것

- 서버에 접속한 상태에서 클라이언트가 돈·아이템·소유권을 직접 바꾸고 끝내기
- 기능 전용 패킷이나 전용 연결 만들기(옛 `pos`/`op`/`state` 경로는 4e에서 지웠습니다)
- 클라이언트가 보낸 이름·금액·소유자를 서버가 그대로 쓰기
- 시민·교통 계산에 랜덤·프레임 시간 쓰기
- "방장만" 하는 계산. 계산은 서버(`GameSession.IsAuthority`가 참인 곳)에서만 합니다
- 리치 텍스트가 들어갈 수 있는 사용자 입력을 그대로 화면에 찍기(`<noparse>`로 감싸거나 `<` `>`를 뺌)

## 11. 시험

- 기능마다 에디터 QA 스크립트가 있습니다(`Assets/Scripts/Editor/**/*QA.cs`, 맨 위 주석에 실행법). 멀티 기능은 최소한 이렇게 확인합니다.
  - **혼자 하기**에서 동작
  - **두 클라이언트**에서 서로 보이고 결과가 같음. 한 PC 두 창은 서로 다른 프로필 슬롯과 `-noeos`로 실행([Server.md](Server.md) "게임 서버 테스트")
  - **나갔다 다시 들어와도** 결과가 남음(월드 파일 저장)
  - 서버가 **잘못된 요청을 거절**함(남의 것, 없는 것, 음수)
- 서버 쪽만 빠르게 볼 때는 가짜 TCP 클라이언트 QA를 씁니다(`DedicatedServerQA.RunConsole`, `SyncLayerQA`).
- 여러 QA를 한 번에: `_temp/qa6/regress.sh <이름…>`(로컬 스크립트).

## 12. 새 기능 체크리스트

- [ ] 2절 표에서 이 기능의 판정 위치를 정했다
- [ ] 보이기만 하면 되는 것은 NetBehaviour, 결과가 남는 것은 요청(Op)으로 했다
- [ ] 서버 허용 목록과 서버 검사(요청한 계정, 수량, 소유)를 넣었다
- [ ] 서버에 있을 때 클라이언트가 결과를 직접 만들지 않는다
- [ ] 메시지·섹션·월드 파일 형식을 바꿨으면 버전을 올렸다
- [ ] 혼자 하기, 두 클라이언트, 재접속, 잘못된 요청을 시험했다
