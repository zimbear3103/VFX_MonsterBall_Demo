# MonsterBall — VFX & Animation Demo

Demo Unity tập trung vào **VFX, animation timing và phản hồi khi va chạm** trong một encounter với MonsterBall. Trọng tâm trình diễn là **tích hợp, điều chỉnh và đồng bộ animation/VFX với tình huống tương tác**.

Player có thể di chuyển, tấn công và tương tác với monster để quan sát chuỗi hiệu ứng từ lúc trồi lên mặt đất, lăn/nhảy tấn công đến khi phát nổ.

**Unity 6 · URP · Visual Effect Graph · Particle System · C#**

> **Video/GIF showcase:** Chưa bổ sung vào README.

## Điểm nổi bật

| Tình huống | Animation & VFX |
| --- | --- |
| **Unburrow** | Monster xuất hiện khi player đến gần; chuyển động trồi lên theo Animation Curve, kết hợp ground burst, smoke, debris và lửa bám thân. |
| **Ground roll** | Monster lăn theo hướng đã chọn, kèm bụi mặt đất và wind VFX; phát impact khi chạm player hoặc vật cản. |
| **Jump roll** | Monster chốt điểm đáp theo vị trí player lúc bắt đầu attack, giữ khoảng cách contact; chuyển động và độ cao đồng bộ với animation, kèm impact lúc bật nhảy và tiếp đất. |
| **Guard / hit reaction** | Player quay mặt về phía đòn đánh sẽ tự guard; guard thành công trước roll/jump roll khiến monster bị stun. Đòn từ phía sau gây knockdown, get-up và hit flame. |
| **Death / explosion** | Normal attack kích hoạt nổ tại chỗ; heavy attack đẩy monster bay lên trước khi nổ. Vụ nổ có thể gây hit reaction cho player ở gần. |
| **Reset** | Đưa player và monster về vị trí ban đầu, phục hồi visual/animation và clear các hiệu ứng để lặp lại demo. |

## Chạy project

### Yêu cầu

| Thành phần | Phiên bản trong project |
| --- | --- |
| Unity Editor | **6000.0.37f1** |
| Universal Render Pipeline | **17.0.3** |
| Visual Effect Graph | **17.0.3** |
| Shader Graph | **17.0.3** |
| Cinemachine | **2.10.3** |

Phiên bản Editor được lưu tại [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt); dependencies được khai báo trong [manifest.json](Packages/manifest.json).

### Các bước mở demo

1. Clone repository bằng Git. Project dùng **Git LFS** cho model, texture và các binary asset. Trong thư mục repository, chạy:

   ```powershell
   git lfs install --local
   git lfs pull
   ```

2. Trong **Unity Hub → Add project from disk**, chọn thư mục chứa `Assets`, `Packages` và `ProjectSettings`.
3. Mở bằng **Unity 6000.0.37f1**, đợi Unity restore packages và import asset.
4. Mở scene [Monsterball_Demo.unity](Assets/VFXDemo/Scenes/Monsterball_Demo.unity).
5. Bấm **Play**, click vào **Game view** để nhận input.

Scene demo đã được bật trong Build Settings. Các script điều khiển dùng `UnityEngine.Input`; **Active Input Handling** của project hiện đặt là **Both**.

## Điều khiển

| Phím | Chức năng |
| --- | --- |
| **W A S D** / **phím mũi tên** | Di chuyển theo hướng camera. |
| **J** | Normal attack — đánh trúng để monster nổ tại chỗ. |
| **K** | Heavy attack — đánh trúng để monster bay lên rồi nổ. |
| **R** | Reset toàn bộ encounter. |
| **G** | Debug preview: death và explosion tại chỗ, bỏ qua kiểm tra tầm đánh của player. |
| **H** | Debug preview: launched death và explosion, bỏ qua kiểm tra tầm đánh của player. |

- **J/K** cần player đứng trong tầm, quay đúng hướng và animation chạy tới hit event. Player tạm dừng di chuyển trong lúc attack hoặc hit reaction.
- Guard được xác định tự động từ hướng player đang quay, không cần phím riêng.
- **G/H** chỉ được nhận sau khi monster hoàn tất Unburrow và trước khi bắt đầu death. Hai phím preview hiện vẫn hoạt động trong build.

## Trải nghiệm nhanh

1. Di chuyển đến gần MonsterBall để kích hoạt Unburrow và quan sát ground burst, smoke, debris, body flame.
2. Quan sát lượt ground roll đầu tiên: monster giữ hướng đã chốt lúc phát hiện player. Né sang bên hoặc để monster chạm vật cản.
3. Sau recovery, giữ khoảng cách gần để quan sát ground roll; lùi xa hơn để quan sát jump roll. Di chuyển khỏi điểm đáp đã chốt để né cú nhảy.
4. Quay mặt về phía monster trước va chạm để xem guard và stun; quay lưng để xem knockdown, get-up và hit flame.
5. Đến gần, hướng mặt về monster rồi dùng **J/K** để so sánh hai kiểu death. Dùng **G/H** khi muốn preview trực tiếp, **R** để lặp lại từng tình huống.

## Điểm kỹ thuật

- **Đồng bộ animation và VFX:** sample Unburrow theo clock riêng; dùng tiến trình animation để điều khiển quãng đường và độ cao của jump roll.
- **Phản hồi đúng hit frame:** normal/heavy attack kiểm tra tầm và góc đánh tại animation event, mỗi attack chỉ resolve hit một lần.
- **Quản lý vòng đời particle:** dừng emission khi vào Recovery để particle tự tan; clear roll dust/wind khi Stun và phục hồi trạng thái khi reset.
- **Hỗ trợ iteration:** chỉnh quỹ đạo, playback và timing VFX bằng Inspector; dùng preview death và reset để so sánh từng tình huống.

<details>
<summary>Source code tham khảo</summary>

Ba script chính nằm trong [Assets/VFXDemo/Scripts](Assets/VFXDemo/Scripts):

| Script | Trách nhiệm |
| --- | --- |
| [MonsterballDemoController.cs](Assets/VFXDemo/Scripts/MonsterballDemoController.cs) | State flow, phát hiện player, chọn attack, di chuyển monster, xử lý contact, death và reset encounter. |
| [MonsterballAnimDemoController.cs](Assets/VFXDemo/Scripts/MonsterballAnimDemoController.cs) | Animator, animation sampling, facing, visual transforms và toàn bộ VFX của monster: timing, play/stop, reset. |
| [DemoPlayerController.cs](Assets/VFXDemo/Scripts/DemoPlayerController.cs) | Di chuyển bằng CharacterController, normal/heavy attack, animation event, guard, knockback, knockdown và hit flame của player. |

Tài nguyên chính: [Animation](Assets/VFXDemo/Animation), [Effects](Assets/VFXDemo/Effects), [Prefabs](Assets/VFXDemo/Prefabs).

</details>

## Asset và phạm vi demo

Project sử dụng các asset có sẵn cho character, animation, environment và VFX, bao gồm tài nguyên trong `Assets/Game`, `Assets/ThirdParty`, `Assets/World_Shared`, `Assets/VFX Assets_Monster ball` và `Assets/VFXDemo/Effects/CinematicExplosions`.

Model, animation và VFX có sẵn được sử dụng làm nền cho việc tích hợp và polish trong demo. Các asset bên thứ ba giữ nguyên quyền sở hữu và điều kiện sử dụng theo license tương ứng; repository không cấp lại quyền sử dụng chúng.

## Lưu ý và giới hạn

- **Lượt ground roll đầu tiên tiếp tục đến khi chạm player hoặc vật cản**, không tự dừng theo `Roll Duration` như các lượt sau.
- `FX_RollImpact` được dùng chung cho contact với player, vật cản, jump takeoff, jump landing và Unburrow landing; chỉnh emitter/material này sẽ ảnh hưởng cả năm tình huống.
- Giữ nguyên Animator states, animation events và các reference đã gán trong scene khi chạy demo; thay chúng có thể làm mất attack hit hoặc hiệu ứng.
- README được đối chiếu với source, scene và project settings. Visual/feel trong Play Mode, standalone build và performance chưa được xác nhận trong lần cập nhật tài liệu này.
