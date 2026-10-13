using MoreMountains.Tools;
using UnityEngine;
using Yeolha.BeltScroll;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 대화가 시작될 때 플레이어와 카메라를 정해진 자리에 앉히는 "대화 연출 배치".
    ///
    /// <see cref="NpcDialogueZone"/> / <see cref="EventZone"/>가 대화를 열기 직전에 <see cref="Apply"/>를,
    /// 대화가 끝나면 <see cref="Release"/>를 부른다. 존과 같은 오브젝트에 붙여두면 자동으로 찾아 쓰고,
    /// 다른 오브젝트에 붙였으면 존 인스펙터의 Staging 슬롯에 물려주면 된다.
    ///
    /// 지금은 **시작 시 한 번 배치**만 한다. 대사 노드별 카메라 이동·캐릭터 액션은 Dialogue System의
    /// 시퀀서(<see cref="SequencerCommandAiaraAction"/> 등)로 붙일 예정이라, 여기서는 그 연출들이 올라탈
    /// 기준 자세(플레이어 위치/방향 + 대화용 카메라)만 만들어 준다.
    ///
    /// 방향은 **지점의 회전이 곧 방향**이다. 플레이어는 <see cref="PlayerStandPoint"/>를 돌려놓은 각도로,
    /// NPC는 <see cref="ConversantFacingPoint"/>를 돌려놓은 각도로 선다 — 씬 뷰에서 두 지점을 돌려 구도를 잡으면
    /// 그대로 나온다. 지점 없이 서로 마주 보게만 하고 싶으면 <see cref="PlayerFacing"/>을 TowardConversant,
    /// <see cref="ConversantFacing"/>을 TowardPlayer로 두면 된다.
    ///
    /// NPC 모델이 트랜스폼의 파란 축(+Z)을 정면으로 두고 있지 않으면(이 프로젝트 NPC는 +X가 많다)
    /// <see cref="ConversantModelForward"/>로 알려줘야 그 각도만큼 어긋나지 않는다.
    ///
    /// 벨트스크롤 진행축(스플라인)은 대화 동안 무시한다 — 돌리는 일 자체는 <see cref="DialogueFacing"/>이
    /// 맡는다(루트 yaw 고정 + 좌우 플립 정리). **대화가 끝나면 둘 다 원래 각도로 돌아간다** —
    /// 플레이어(캐릭터)는 잠금이 풀리면서 스스로 진행축으로 돌아가고, NPC는 잡기 전 각도로 되돌아간다
    /// (<see cref="KeepConversantFacingAfterEnd"/>를 켜면 그 자세로 남는다).
    ///
    /// 카메라는 **쓰던 카메라를 그대로 쓴다**. <see cref="CameraViewPoint"/>에 빈 오브젝트를 하나 꽂아
    /// 위치와 각도만 정해두면, 대화가 시작될 때 그 자리로 카메라를 **즉시 컷**하고 끝나면 게임플레이 쪽에
    /// 돌려준다. 새 카메라를 만들지 않으므로 렌즈·후처리가 갈리지 않는다.
    /// (부드럽게 밀고 들어가고 싶으면 <see cref="CameraBlendDuration"/>을 0보다 크게 두면 된다.)
    /// </summary>
    /// <remarks>
    /// LateUpdate가 카메라 리그보다 **뒤에** 돌아야 우리가 쓴 좌표가 남는다.
    /// ver2의 카메라 실행 순서는 <see cref="CameraRig"/>(100) → <see cref="CameraSequenceRunner"/>(200)이라
    /// 그 뒤인 300에 둔다.
    /// </remarks>
    [DefaultExecutionOrder(300)]
    [AddComponentMenu("Aiara/Dialogue/Dialogue Staging")]
    public class DialogueStaging : MonoBehaviour
    {
        /// <summary>대화를 시작할 때 플레이어가 바라볼 방향을 정하는 방식.</summary>
        public enum PlayerFacingModes
        {
            /// <summary>StandPoint의 forward(파란 축)를 그대로 쓴다.</summary>
            StandPointForward,
            /// <summary>대화 상대(FacingTarget 또는 이 오브젝트) 쪽을 본다.</summary>
            TowardConversant,
            /// <summary>방향은 건드리지 않는다.</summary>
            Keep
        }

        /// <summary>
        /// NPC 모델이 자기 트랜스폼에서 어느 쪽을 정면으로 두고 있는지.
        ///
        /// 대부분의 캐릭터는 파란 축(+Z)이 정면이라 그대로 두면 되지만, 이 프로젝트의 NPC FBX처럼
        /// **빨간 축(+X)을 보는 모델**도 있다(트랜스폼을 −90°로 돌려놓고 쓰는 그것). 모델마다 다른 값이라
        /// 코드가 알아낼 방법이 없어 여기서 알려줘야 한다 — 틀리면 딱 그 각도만큼(보통 90°) 어긋나 선다.
        /// </summary>
        public enum ModelForwardAxes
        {
            /// <summary>파란 축(+Z). 플레이어·적 모델이 이쪽이다.</summary>
            ZPlus,
            /// <summary>빨간 축(+X).</summary>
            XPlus,
            /// <summary>파란 축 반대(−Z).</summary>
            ZMinus,
            /// <summary>빨간 축 반대(−X).</summary>
            XMinus
        }

        /// <summary>대화를 시작할 때 NPC(대화 상대)가 바라볼 방향을 정하는 방식.</summary>
        public enum ConversantFacingModes
        {
            /// <summary>건드리지 않는다. (AI나 애니메이션이 방향을 잡고 있을 때)</summary>
            Keep,
            /// <summary>플레이어 쪽으로 돈다.</summary>
            TowardPlayer,
            /// <summary>지정한 지점의 forward를 그대로 쓴다.</summary>
            ConversantPointForward
        }

        [MMInspectorGroup("방향 공통", true, 99)]

        [Tooltip("돌아서는 속도(도/초). **0이면 즉시**(권장) — 대화 시작과 함께 카메라도 컷으로 바뀌므로 " +
                 "그 프레임에 이미 자세가 잡혀 있어야 한다. 0보다 크면 그 속도로 돌아서고, " +
                 "대화가 끝나 원래 각도로 돌아갈 때도 같은 속도를 쓴다.")]
        public float TurnSpeed = 0f;

        [Tooltip("**대화가 끝나도 NPC를 그 각도로 둔다.** 기본은 꺼짐 = 잡기 전 각도로 되돌린다. " +
                 "대화 뒤에 NPC가 플레이어를 계속 보고 있어야 하는 연출에만 켠다.")]
        public bool KeepConversantFacingAfterEnd = false;

        [MMInspectorGroup("플레이어 배치", true, 100)]

        [Tooltip("대화 시작 시 플레이어를 StandPoint로 옮길지 여부. 끄면 방향만 처리한다.")]
        public bool PlacePlayer = true;

        [Tooltip("플레이어가 서게 될 자리. 빈 게임오브젝트를 씬에 놓고 물려주면 된다. " +
                 "위치와 함께 **회전(파란 축)이 곧 바라볼 방향**이다. (Facing이 StandPointForward일 때)")]
        public Transform PlayerStandPoint;

        [Tooltip("대화 시작 시 플레이어가 바라볼 방향. " +
                 "기본(StandPointForward)은 설 자리 지점을 돌려놓은 각도 그대로 선다. " +
                 "NPC 쪽을 보게 하려면 TowardConversant.")]
        public PlayerFacingModes PlayerFacing = PlayerFacingModes.StandPointForward;

        [Tooltip("플레이어가 바라볼 대상이자, **실제로 돌릴 NPC 트랜스폼**. " +
                 "비워두면 존의 'Conversant Transform'을 쓰고, 그것도 없으면 이 오브젝트의 부모를 쓴다. " +
                 "NPC는 자기 트랜스폼의 파란 축(+Z)이 정면이라고 보고 돌린다. 모델이 자식에서 따로 돌아가 있어 " +
                 "(예: Body가 −90°) 본체를 돌렸을 때 엉뚱한 쪽을 보면, 모델이 실제로 달린 그 자식을 여기에 물려주면 된다.")]
        public Transform FacingTarget;

        [Tooltip("대화가 끝나면 플레이어를 원래 서 있던 자리로 되돌린다. " +
                 "보통은 끈 채로 둔다 — 대화 뒤 이동 연출과 충돌하기 때문.")]
        public bool RestorePlayerPositionOnEnd = false;

        [MMInspectorGroup("대화 상대(NPC) 방향", true, 101)]

        [Tooltip("대화 시작 시 NPC가 바라볼 방향. 기본(ConversantPointForward)은 각도 지점을 돌려놓은 대로 선다. " +
                 "Keep이면 건드리지 않고, TowardPlayer로 두면 지점 없이 플레이어 쪽을 본다.")]
        public ConversantFacingModes ConversantFacing = ConversantFacingModes.ConversantPointForward;

        [Tooltip("ConversantPointForward일 때 쓸 방향 기준. **이 지점을 돌려놓은 각도(파란 축)가 곧 NPC의 대화 자세**다. " +
                 "비우면 NPC 자신의 지금 forward를 써서 결과적으로 방향이 바뀌지 않는다.")]
        public Transform ConversantFacingPoint;

        [Tooltip("NPC 모델의 정면이 트랜스폼의 어느 축인지. 대화 때 NPC가 90° 어긋나 서면 여기를 바꾼다. " +
                 "씬 뷰의 **회색 선이 '지금 모델이 보는 쪽'**이니, 그 선이 실제 모델 정면과 맞는 값을 고르면 된다. " +
                 "(이 프로젝트의 NPC FBX는 대체로 +X다)")]
        public ModelForwardAxes ConversantModelForward = ModelForwardAxes.ZPlus;

        [MMInspectorGroup("카메라 배치", true, 101)]

        [Tooltip("대화 시작 시 카메라를 지정한 자리로 가져올지 여부")]
        public bool OverrideCamera = true;

        [Tooltip("대화 중 카메라가 있을 자리. 이 Transform의 **위치와 회전**을 그대로 쓴다. " +
                 "새 카메라를 만들지 않고 지금 쓰는 카메라를 이 자리로 가져온다.")]
        public Transform CameraViewPoint;

        [Tooltip("카메라가 그 자리로 이동하는 데 걸리는 시간(초). **기본 0 = 즉시 컷**(권장). " +
                 "0보다 크게 두면 그 시간 동안 미끄러지듯 이동한다. 타임스케일과 무관하게 흐른다.")]
        public float CameraBlendDuration = 0f;

        [MMInspectorGroup("디버그", true, 102)]

        [Tooltip("배치가 실제로 적용됐는지 콘솔에 남긴다.")]
        public bool DebugLog = false;

        /// <summary>지금 이 배치가 적용된 상태인가.</summary>
        public bool IsApplied => _applied;

        /// <summary>모델 정면이 트랜스폼의 파란 축(+Z)에서 몇 도 돌아가 있는지. 축 설정을 각도로 바꾼 값이다.</summary>
        public float ConversantModelYaw
        {
            get
            {
                switch (ConversantModelForward)
                {
                    case ModelForwardAxes.XPlus: return 90f;
                    case ModelForwardAxes.ZMinus: return 180f;
                    case ModelForwardAxes.XMinus: return -90f;
                    default: return 0f;
                }
            }
        }

        /// <summary>지금 이 배치가 돌리게 될 NPC 트랜스폼. 에디터 툴이 지점을 만들 때도 이 값을 기준으로 삼는다.</summary>
        public Transform ConversantRoot => ResolveConversantRoot();

        /// <summary>NPC 모델이 지금 실제로 보고 있는 방향(정면 축 반영). 기즈모와 에디터 툴이 쓴다.</summary>
        public Vector3 ConversantModelForwardDirection
        {
            get
            {
                Transform conversant = ResolveConversantRoot();
                if (conversant == null)
                {
                    return Vector3.forward;
                }

                Vector3 forward = Quaternion.Euler(0f, ConversantModelYaw, 0f) * conversant.forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            }
        }

        protected Character _player;
        protected bool _applied;

        // 플레이어 복귀용
        protected Vector3 _playerReturnPosition;
        protected bool _playerMoved;

        // 방향 고정 — 대화 동안 매 프레임 붙잡고, 끝나면 잠금을 되돌린다.
        protected DialogueFacing _playerFacing;
        protected DialogueFacing _conversantFacing;

        // 카메라 인계용
        protected Transform _cameraTransform;
        protected bool _drivingCamera;
        protected float _blendElapsed;
        protected Vector3 _blendFromPosition;
        protected Quaternion _blendFromRotation;

        // 카메라 로컬 기준선 — 리그가 부모 노드만 갱신하기 때문에, 카메라의 월드 좌표를 직접 쓰면
        // 그 차이가 '로컬 오프셋'으로 남고 아무도 지워주지 않는다. 시작 전 로컬 pose를 기억해뒀다가
        // 끝날 때 되돌려 리그가 카메라를 온전히 되찾게 한다. (CameraSequenceRunner와 같은 방식)
        protected Transform _cameraParent;
        protected Vector3 _cameraBaseLocalPosition;
        protected Quaternion _cameraBaseLocalRotation;
        protected bool _hasCameraBaseline;

        protected virtual void OnDisable()
        {
            // 대화 도중 오브젝트가 꺼져도 강제 회전·대화 카메라가 남지 않게 한다.
            // 꺼진 뒤에는 LateUpdate가 돌지 않으므로 되돌리기는 연출 없이 지금 끝낸다.
            Release(immediate: true);
        }

        /// <summary>
        /// 플레이어를 자리에 놓고 대화 카메라를 켠다. 대화를 시작하기 **직전**에 부른다.
        /// (조작 잠금이 이미 걸린 뒤에 불러야 배치 직후 스틱 입력으로 밀려나지 않는다.)
        /// </summary>
        public virtual void Apply(Character player)
        {
            if (_applied)
            {
                return;
            }

            _applied = true;
            _player = player;

            ApplyPlayerPlacement(player);
            ApplyCamera();

            if (DebugLog)
            {
                Debug.Log($"[DialogueStaging] '{name}' 배치 적용 — " +
                          $"플레이어: {(_playerMoved ? "이동함" : "이동 안 함")}/{PlayerFacing}, " +
                          $"NPC 방향: {ConversantFacing}, " +
                          $"카메라: {(_drivingCamera ? CameraViewPoint.name + "로 이동" : "그대로")}", this);
            }
        }

        /// <summary>대화가 끝났을 때 강제 회전과 카메라를 원래대로 돌린다.</summary>
        public virtual void Release()
        {
            Release(false);
        }

        /// <param name="immediate">
        /// NPC를 원래 각도로 되돌릴 때 <see cref="TurnSpeed"/>를 무시하고 즉시 끝낸다.
        /// 컴포넌트가 꺼지는 길처럼 더 이상 LateUpdate가 돌지 않는 상황에서 쓴다.
        /// </param>
        public virtual void Release(bool immediate)
        {
            if (!_applied)
            {
                return;
            }

            _applied = false;

            ReleaseCamera();
            ReleasePlayerPlacement(immediate);

            _player = null;
        }

        #region 플레이어 배치

        protected virtual void ApplyPlayerPlacement(Character player)
        {
            _playerMoved = false;

            if (player == null)
            {
                return;
            }

            if (PlacePlayer)
            {
                if (PlayerStandPoint != null)
                {
                    _playerReturnPosition = player.transform.position;
                    TeleportCharacter(player, PlayerStandPoint.position);
                    _playerMoved = true;
                }
                else
                {
                    Debug.LogWarning($"[DialogueStaging] '{name}'의 Player Stand Point가 비어 있어 " +
                                     "플레이어를 옮기지 못했습니다.", this);
                }
            }

            ApplyPlayerFacing(player);
            ApplyConversantFacing(player);
        }

        protected virtual void ReleasePlayerPlacement(bool immediate)
        {
            _playerFacing.Release(immediate);
            _conversantFacing.Release(immediate);

            if (RestorePlayerPositionOnEnd && _playerMoved && _player != null)
            {
                TeleportCharacter(_player, _playerReturnPosition);
            }

            _playerMoved = false;
        }

        /// <summary>
        /// 캐릭터를 즉시 다른 자리로 옮긴다.
        ///
        /// <see cref="CharacterMovement.Teleport"/>를 쓴다 — CharacterController를 잠깐 끄고 좌표를 넣은 뒤
        /// 남아 있던 속도까지 지워준다(켠 채로 옮기면 내부 위치와 어긋나 다음 Move에서 되돌아간다).
        /// 지금 보고 있는 좌우는 그대로 넘겨 유지하고, 방향은 뒤이어 <see cref="ApplyPlayerFacing"/>이 잡는다.
        ///
        /// Movement가 없는 오브젝트(소품 등)를 대비해 같은 일을 직접 하는 경로도 남겨둔다.
        /// </summary>
        public static void TeleportCharacter(Character character, Vector3 position)
        {
            if (character == null)
            {
                return;
            }

            CharacterMovement movement = character.Movement;
            if (movement != null)
            {
                movement.Teleport(position, movement.FacingRight);
                return;
            }

            var characterController = character.gameObject.MMGetComponentNoAlloc<CharacterController>();
            bool controllerWasEnabled = characterController != null && characterController.enabled;
            if (controllerWasEnabled)
            {
                characterController.enabled = false;
            }

            character.transform.position = position;

            if (controllerWasEnabled)
            {
                characterController.enabled = true;
            }
        }

        protected virtual void ApplyPlayerFacing(Character player)
        {
            if (PlayerFacing == PlayerFacingModes.Keep)
            {
                return;
            }

            Vector3 forward = ResolveFacingDirection(player);
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
            {
                return;
            }

            _playerFacing.Apply(player, player.transform, forward, TurnSpeed);
        }

        protected virtual Vector3 ResolveFacingDirection(Character player)
        {
            if (PlayerFacing == PlayerFacingModes.StandPointForward && PlayerStandPoint != null)
            {
                return PlayerStandPoint.forward;
            }

            Transform target = ResolveConversantRoot();
            return target.position - player.transform.position;
        }

        /// <summary>대화 상대(NPC)를 지정한 방향으로 돌린다. Keep이면 아무것도 하지 않는다.</summary>
        protected virtual void ApplyConversantFacing(Character player)
        {
            if (ConversantFacing == ConversantFacingModes.Keep)
            {
                return;
            }

            Transform conversant = ResolveConversantRoot();
            if (conversant == null)
            {
                return;
            }

            Vector3 forward;

            if (ConversantFacing == ConversantFacingModes.ConversantPointForward)
            {
                forward = ConversantFacingPoint != null ? ConversantFacingPoint.forward : conversant.forward;
            }
            else
            {
                // 플레이어 쪽. 이미 StandPoint로 옮긴 뒤라 최종 위치를 기준으로 잡힌다.
                Transform playerTransform = player != null ? player.transform : null;
                if (playerTransform == null)
                {
                    return;
                }

                forward = playerTransform.position - conversant.position;
            }

            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
            {
                return;
            }

            forward.Normalize();

            // 여기까지의 forward는 "모델이 봐야 할 방향"이다. 실제로 돌리는 DialogueFacing은 트랜스폼의
            // 파란 축(+Z)을 그 방향에 맞추는 일만 하므로, 모델 정면이 +Z가 아니면 그만큼 미리 되돌려 둔다.
            // (전부 yaw뿐이라 이렇게 먼저 빼두면 결과가 정확히 맞는다)
            if (Mathf.Abs(ConversantModelYaw) > 0.01f)
            {
                forward = Quaternion.Euler(0f, -ConversantModelYaw, 0f) * forward;
            }

            Character conversantCharacter = conversant.GetComponentInParent<Character>();
            _conversantFacing.Apply(conversantCharacter, conversant, forward,
                                    TurnSpeed, !KeepConversantFacingAfterEnd);
        }

        /// <summary>
        /// 실제로 돌릴 대화 상대의 트랜스폼. 위에서부터 찾아 처음 걸리는 것을 쓴다:
        ///   ① <see cref="FacingTarget"/> — 직접 물려준 것. 모델이 자식에서 따로 돌아가 있을 때 여기에 넣는다.
        ///   ② 이 배치를 쓰는 존의 <c>Conversant Transform</c> — <b>존과 NPC가 따로 떨어져 있어도 맞는 길</b>.
        ///      존이 이미 "이 NPC와 대화한다"고 가리키고 있으므로 같은 값을 두 번 물릴 필요가 없다.
        ///   ③ 이 오브젝트의 부모 — 존을 NPC의 자식으로 둔 경우(<see cref="NpcDialogueZoneSetup"/>가 만드는 모양).
        /// </summary>
        protected virtual Transform ResolveConversantRoot()
        {
            if (FacingTarget != null)
            {
                return FacingTarget;
            }

            Transform fromZone = ResolveZoneConversant();
            if (fromZone != null)
            {
                return fromZone;
            }

            return transform.parent != null ? transform.parent : transform;
        }

        /// <summary>이 배치를 쓰는 존이 가리키는 대화 상대. 존을 못 찾거나 비어 있으면 null.</summary>
        protected virtual Transform ResolveZoneConversant()
        {
            var npcZone = GetComponentInParent<NpcDialogueZone>();
            if (npcZone != null && npcZone.ConversantTransform != null)
            {
                return npcZone.ConversantTransform;
            }

            var eventZone = GetComponentInParent<EventZone>();
            if (eventZone != null && eventZone.ConversantTransform != null)
            {
                return eventZone.ConversantTransform;
            }

            return null;
        }

        #endregion

        #region 카메라 배치

        /// <summary>
        /// 쓰던 카메라를 그대로 대화 자리로 가져온다. 새 카메라를 만들지 않는다.
        ///
        /// 카메라를 하나 더 두고 갈아타는 방법도 있지만, 그러면 NPC마다 카메라 오브젝트가 하나씩 늘고
        /// 렌즈·후처리 설정이 게임플레이 카메라와 갈린다. ver2는 이미
        /// <see cref="CameraSequenceRunner"/>가 같은 방식으로 Camera.main의 pose를 덮어쓰므로 그 관례를 따른다.
        ///
        /// 리그를 끄지는 않는다 — 리그(100)와 시퀀스 러너(200)가 평가를 마친 뒤 이 컴포넌트(300)가
        /// 마지막에 pose를 덮어쓴다. 리그는 계속 제 값을 유지하고 있으므로, 대화가 끝나 우리가 손을 떼는
        /// 순간 평소 damping으로 자연스럽게 제 프레이밍으로 돌아간다.
        /// </summary>
        protected virtual void ApplyCamera()
        {
            _drivingCamera = false;

            if (!OverrideCamera)
            {
                return;
            }

            if (CameraViewPoint == null)
            {
                Debug.LogWarning($"[DialogueStaging] '{name}'에 Camera View Point가 없어 " +
                                 "카메라를 옮기지 못했습니다.", this);
                return;
            }

            UnityEngine.Camera mainCamera = UnityEngine.Camera.main;
            if (mainCamera == null)
            {
                Debug.LogWarning($"[DialogueStaging] '{name}': MainCamera 태그가 붙은 카메라를 찾지 못했습니다.", this);
                return;
            }

            _cameraTransform = mainCamera.transform;
            _blendFromPosition = _cameraTransform.position;
            _blendFromRotation = _cameraTransform.rotation;
            _blendElapsed = 0f;
            _drivingCamera = true;

            // 우리가 월드 좌표를 쓰기 전의 로컬 pose를 기억해둔다. 되돌릴 사람이 우리뿐이다.
            _cameraParent = _cameraTransform.parent;
            _cameraBaseLocalPosition = _cameraTransform.localPosition;
            _cameraBaseLocalRotation = _cameraTransform.localRotation;
            _hasCameraBaseline = true;
        }

        /// <summary>
        /// 카메라를 게임플레이 쪽에 돌려준다.
        ///
        /// 원래 자리로 되돌리는 연출은 하지 않는다 — 대화 중 플레이어가 다른 곳으로 옮겨졌을 수 있어
        /// "원래 자리"가 이미 틀린 값이기 때문이다. 대신 우리가 오염시킨 **로컬** pose만 원상복구하면,
        /// 리그가 다음 프레임부터 카메라를 온전히 소유하고 평소 damping으로 제 프레이밍까지 돌아간다.
        /// </summary>
        protected virtual void ReleaseCamera()
        {
            if (!_drivingCamera)
            {
                return;
            }

            _drivingCamera = false;

            // 부모가 그대로일 때만 되돌린다 — 씬 전환 등으로 부모가 바뀌었으면 우리가 남긴 값이 아니다.
            if (_hasCameraBaseline && _cameraTransform != null && _cameraTransform.parent == _cameraParent)
            {
                _cameraTransform.localPosition = _cameraBaseLocalPosition;
                _cameraTransform.localRotation = _cameraBaseLocalRotation;
            }

            _hasCameraBaseline = false;
            _cameraParent = null;
            _cameraTransform = null;
        }

        /// <summary>
        /// 카메라를 실제로 옮기는 곳. LateUpdate인 이유는 카메라 리그의 평가가 끝난 뒤에 덮어써야 하기
        /// 때문이고, 실행 순서를 300으로 둔 이유도 같다(클래스의 DefaultExecutionOrder 참고).
        /// </summary>
        protected virtual void LateUpdate()
        {
            // 방향은 카메라와 무관하게, 잡고 있는 동안 매 프레임 다시 눌러쓴다. 배치를 놓은 뒤에도
            // 원래 각도로 돌아가는 동안에는 계속 불러야 하므로 조건을 걸지 않는다(잡은 게 없으면 곧바로 빠져나온다).
            // 여기서 하는 이유는 애니메이터 평가가 끝난 뒤여야 우리가 쓴 각도가 남기 때문이다.
            // 대화 중 슬로우모션이 걸려도 도는 속도는 그대로여야 하므로 unscaled를 쓴다(카메라와 같은 이유).
            float facingDelta = Time.unscaledDeltaTime;
            _playerFacing.Tick(facingDelta);
            _conversantFacing.Tick(facingDelta);

            if (!_drivingCamera)
            {
                return;
            }

            if (_cameraTransform == null || CameraViewPoint == null)
            {
                _drivingCamera = false;
                return;
            }

            float progress = 1f;

            if (CameraBlendDuration > 0f)
            {
                // 대화 중 슬로우모션·일시정지가 걸려도 카메라는 제 속도로 움직여야 한다.
                _blendElapsed += Time.unscaledDeltaTime;
                progress = Mathf.Clamp01(_blendElapsed / CameraBlendDuration);
            }

            float eased = Mathf.SmoothStep(0f, 1f, progress);

            _cameraTransform.SetPositionAndRotation(
                Vector3.Lerp(_blendFromPosition, CameraViewPoint.position, eased),
                Quaternion.Slerp(_blendFromRotation, CameraViewPoint.rotation, eased));
        }

        #endregion

#if UNITY_EDITOR
        protected virtual void OnDrawGizmos()
        {
            if (PlayerStandPoint != null)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
                Gizmos.DrawWireSphere(PlayerStandPoint.position + Vector3.up * 0.05f, 0.35f);

                Transform conversantRoot = ResolveConversantRoot();

                Vector3 facing = PlayerFacing == PlayerFacingModes.StandPointForward
                    ? PlayerStandPoint.forward
                    : (conversantRoot.position - PlayerStandPoint.position);

                facing.y = 0f;
                if (facing.sqrMagnitude > 0.0001f)
                {
                    Vector3 from = PlayerStandPoint.position + Vector3.up * 0.05f;
                    Gizmos.DrawLine(from, from + facing.normalized);
                }

                // 지금 모델이 보고 있는 쪽. 이 선이 실제 모델의 정면과 어긋나 있으면
                // Conversant Model Forward(정면 축)가 틀린 것이다 — 그대로 두면 대화 때도 그만큼 어긋난다.
                if (conversantRoot != null)
                {
                    Gizmos.color = new Color(0.7f, 0.7f, 0.7f, 0.9f);
                    Vector3 from = conversantRoot.position + Vector3.up * 0.05f;
                    Gizmos.DrawLine(from, from + ConversantModelForwardDirection * 0.7f);
                }

                // NPC가 바라볼 방향도 같이 그린다 — 둘이 서로를 보는지 눈으로 확인하려고.
                if (ConversantFacing != ConversantFacingModes.Keep && conversantRoot != null)
                {
                    Vector3 conversantFacing = ConversantFacing == ConversantFacingModes.ConversantPointForward
                        ? (ConversantFacingPoint != null ? ConversantFacingPoint.forward : conversantRoot.forward)
                        : (PlayerStandPoint.position - conversantRoot.position);

                    conversantFacing.y = 0f;
                    if (conversantFacing.sqrMagnitude > 0.0001f)
                    {
                        Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.9f);
                        Vector3 from = conversantRoot.position + Vector3.up * 0.05f;
                        Gizmos.DrawLine(from, from + conversantFacing.normalized);
                    }
                }
            }

            if (CameraViewPoint != null)
            {
                Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);

                // 실제 카메라 화각으로 그려야 구도가 눈에 맞는다. 못 찾으면 대충 35도.
                UnityEngine.Camera mainCamera = UnityEngine.Camera.main;
                float fieldOfView = mainCamera != null ? mainCamera.fieldOfView : 35f;
                float aspect = mainCamera != null ? mainCamera.aspect : 1.7778f;

                Matrix4x4 previous = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(CameraViewPoint.position, CameraViewPoint.rotation, Vector3.one);
                Gizmos.DrawFrustum(Vector3.zero, fieldOfView, 3f, 0.1f, aspect);
                Gizmos.matrix = previous;

                if (PlayerStandPoint != null)
                {
                    Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.3f);
                    Gizmos.DrawLine(CameraViewPoint.position, PlayerStandPoint.position);
                }
            }
        }

        /// <summary>
        /// 씬 뷰에서 보고 있는 그 화면을 카메라 지점에 그대로 박는다.
        /// 구도를 눈으로 잡고(씬 뷰를 원하는 각도로 돌린 뒤) 이 메뉴만 누르면 된다.
        /// </summary>
        [ContextMenu("카메라 지점 ← 지금 씬 뷰 화면")]
        protected virtual void CopySceneViewToCameraPoint()
        {
            var sceneView = UnityEditor.SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null || CameraViewPoint == null)
            {
                Debug.LogWarning("[DialogueStaging] 씬 뷰나 Camera View Point가 없습니다.", this);
                return;
            }

            Transform sceneCamera = sceneView.camera.transform;

            UnityEditor.Undo.RecordObject(CameraViewPoint, "대화 카메라 지점 맞추기");
            CameraViewPoint.SetPositionAndRotation(sceneCamera.position, sceneCamera.rotation);
            UnityEditor.EditorUtility.SetDirty(CameraViewPoint);
        }

        /// <summary>반대로, 카메라 지점이 보는 화면을 씬 뷰로 확인한다.</summary>
        [ContextMenu("씬 뷰 → 카메라 지점에서 보기")]
        protected virtual void MoveSceneViewToCameraPoint()
        {
            var sceneView = UnityEditor.SceneView.lastActiveSceneView;
            if (sceneView == null || CameraViewPoint == null)
            {
                Debug.LogWarning("[DialogueStaging] 씬 뷰나 Camera View Point가 없습니다.", this);
                return;
            }

            // 씬 뷰 카메라는 pivot에서 size만큼 떨어진 곳에 놓이므로, 지점 앞쪽을 pivot으로 잡아야
            // 카메라가 정확히 그 지점에 선다.
            const float pivotDistance = 2f;
            Vector3 pivot = CameraViewPoint.position + CameraViewPoint.forward * pivotDistance;

            sceneView.LookAt(pivot, CameraViewPoint.rotation, pivotDistance);
        }
#endif
    }
}
