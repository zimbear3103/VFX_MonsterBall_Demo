using UnityEngine;
using Yeolha.BeltScroll;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 대화 동안 "이쪽을 봐라"를 붙잡아 두는 방향 고정.
    ///
    /// ver2는 벨트스크롤이라 캐릭터가 보는 방향이 두 겹으로 나뉜다 —
    ///   ① <b>루트 yaw</b>: <see cref="CharacterMovement"/>가 매 프레임 스플라인 접선(진행축)으로 되돌린다.
    ///   ② <b>좌/우 플립</b>: 비주얼 루트의 스케일을 뒤집어 표현한다(<see cref="CharacterMovement.FacingRight"/>).
    /// 그래서 실제로 보이는 방향은 <c>루트 forward × (오른쪽이면 +1, 왼쪽이면 −1)</c>이다.
    /// (이펙트 방향을 잡는 CharacterMovement 쪽 계산도 같은 규칙을 쓴다.)
    ///
    /// 대화에서는 두 사람이 서로 마주 봐야 하므로 진행축과 상관없는 각도가 필요하다. 그래서
    ///   - ①을 <see cref="CharacterMovement.RotationLocked"/>로 멈춘 뒤 루트 yaw를 직접 잡고
    ///     (사다리·난간 상태가 벽 각도를 따라갈 때 쓰는 방식과 같다),
    ///   - ②는 원하는 방향에 가장 가까운 쪽으로 맞춘 뒤 <see cref="CharacterMovement.FacingLocked"/>로 잠근다.
    ///     대화 중 자동 플립이 방향을 도로 뒤집는 것을 막고, 대화가 끝나 진행축으로 돌아갔을 때
    ///     좌우가 어색하지 않게 하려는 것이다.
    ///
    /// 세워둔 각도는 <see cref="Tick"/>에서 매 프레임 다시 눌러쓴다 — 한 번만 넣어두면 애니메이션 루트 모션이나
    /// 다른 시스템이 슬쩍 되돌려도 알아챌 수 없기 때문이다. <b>LateUpdate에서 부를 것</b>
    /// (애니메이터 평가가 끝난 뒤여야 우리가 쓴 각도가 남는다).
    ///
    /// <see cref="Release"/>는 걸어둔 잠금들을 잠글 때의 값으로 되돌린다(단, 그 사이 다른 시스템이 먼저
    /// 풀어놨다면 그대로 둔다). <b>캐릭터의 루트 각도는 여기서 되돌리지 않는다</b> —
    /// 잠금이 풀리는 순간 CharacterMovement가 평소 속도로 진행축까지 부드럽게 되돌려 놓기 때문이다.
    /// <c>restoreRotationOnRelease</c>를 켜면 잡기 전 각도로 직접 되돌린다 — 스스로 방향을 되찾지 못하는
    /// NPC(모델만 있는 오브젝트, 이동을 돌리지 않는 캐릭터)를 위한 것이다. 이때 <c>turnSpeed</c>가 0보다 크면
    /// 곧바로 튀지 않고 그 속도로 돌아간다 — 되돌아가는 동안에도 <see cref="Tick"/>을 계속 불러야 한다
    /// (<see cref="IsBusy"/>가 그 동안 true다).
    /// </summary>
    public struct DialogueFacing
    {
        private CharacterMovement _movement;
        private Transform _root;
        private bool _applied;
        private bool _previousFacingLocked;
        private bool _previousRotationLocked;
        private Quaternion _previousRotation;
        private bool _restoreRotation;
        private bool _returning;
        private Quaternion _targetRotation;
        private float _turnSpeed;

        /// <summary>지금 방향을 붙잡고 있는가.</summary>
        public bool IsApplied => _applied;

        /// <summary>붙잡고 있거나, 놓고 나서 원래 각도로 돌아가는 중인가. 이 동안은 <see cref="Tick"/>을 계속 불러야 한다.</summary>
        public bool IsBusy => _applied || _returning;

        /// <summary>
        /// 이 방향을 보게 하고 대화가 끝날 때까지 붙잡는다. 이미 잡고 있었다면 먼저 풀고 다시 잡는다.
        /// </summary>
        /// <param name="character">
        /// 좌/우 플립과 회전 잠금을 걸 캐릭터. 소품 NPC처럼 Character가 없으면 null로 둔다.
        /// <b>돌리는 대상이 아니라 잠글 대상</b>이다 — 실제로 돌아가는 것은 언제나 <paramref name="root"/>다.
        /// </param>
        /// <param name="root">실제로 돌릴 트랜스폼. 비우면 캐릭터의 루트를 쓴다.</param>
        /// <param name="forward">바라볼 월드 방향. 수평 성분만 쓴다(위아래로는 안 돈다).</param>
        /// <param name="turnSpeed">돌아가는 속도(도/초). 0이면 즉시 돈다.</param>
        /// <param name="restoreRotationOnRelease">끝날 때 잡기 직전의 각도로 되돌릴지.</param>
        public void Apply(Character character, Transform root, Vector3 forward,
                          float turnSpeed = 0f, bool restoreRotationOnRelease = false)
        {
            // 돌릴 대상은 넘겨받은 트랜스폼이 우선이다 — 모델이 캐릭터 루트가 아니라 자식에 달려 있을 수 있다.
            Transform target = root != null ? root : (character != null ? character.transform : null);

            forward.y = 0f;
            if (target == null || forward.sqrMagnitude < 0.0001f)
            {
                Release();
                return;
            }

            forward.Normalize();

            // 아직 원래 각도로 돌아가는 중에 같은 대상을 다시 붙잡았다면, '원래 각도'는 지금의 중간값이 아니라
            // 처음 잡기 전의 값을 그대로 이어받아야 한다. 안 그러면 대화를 반복할 때마다 각도가 조금씩 밀린다.
            bool inheritOriginal = _returning && _root == target;
            Quaternion original = _previousRotation;

            Release();

            _movement = character != null ? character.Movement : null;
            _root = target;
            _applied = true;
            _turnSpeed = Mathf.Max(0f, turnSpeed);
            _previousRotation = inheritOriginal ? original : target.rotation;

            // 되돌리기는 요청한 대로 한다. 캐릭터라도 그냥 서 있기만 하는 NPC라면 CharacterMovement가
            // 진행축으로 되돌려 줄 거라고 믿을 수 없다(이동을 돌리지 않는 오브젝트도 있다).
            // 되돌리는 각도는 잡기 직전의 각도라, 움직이는 캐릭터에게 걸어도 원상복구 이상은 하지 않는다.
            _restoreRotation = restoreRotationOnRelease;

            bool facingRight = true;

            if (_movement != null)
            {
                // 좌/우 먼저. 잠겨 있으면 SetFacingRight가 무시되므로 풀었다가 넣고 다시 잠근다.
                _previousFacingLocked = _movement.FacingLocked;
                _movement.FacingLocked = false;
                _movement.SetFacingRight(ResolveFacingRight(_movement, forward));
                _movement.FacingLocked = true;
                facingRight = _movement.FacingRight;

                // 그 다음 루트 yaw. 이걸 안 멈추면 다음 프레임에 곧바로 진행축으로 되돌아간다.
                _previousRotationLocked = _movement.RotationLocked;
                _movement.RotationLocked = true;
            }

            // 비주얼이 뒤집혀 있으면(왼쪽 보는 중) 루트는 반대로 돌려야 실제로 그쪽을 본다.
            _targetRotation = Quaternion.LookRotation(facingRight ? forward : -forward, Vector3.up);

            if (_turnSpeed <= 0f)
            {
                _root.rotation = _targetRotation;
            }
        }

        /// <summary>
        /// 세워둔 각도를 다시 눌러쓴다. 대화 동안, 그리고 놓은 뒤 원래 각도로 돌아가는 동안
        /// 매 프레임(LateUpdate) 부른다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!IsBusy)
            {
                return;
            }

            // 대상이 파괴됐으면(씬 전환 등) 더 잡을 것도 되돌릴 것도 없다.
            if (_root == null)
            {
                _applied = false;
                _returning = false;
                _movement = null;
                return;
            }

            if (_turnSpeed <= 0f || deltaTime <= 0f)
            {
                _root.rotation = _targetRotation;
            }
            else
            {
                _root.rotation = Quaternion.RotateTowards(
                    _root.rotation, _targetRotation, _turnSpeed * deltaTime);
            }

            // 다 돌아왔으면 손을 뗀다.
            if (_returning && Quaternion.Angle(_root.rotation, _targetRotation) < 0.05f)
            {
                _root.rotation = _targetRotation;
                _returning = false;
                _root = null;
            }
        }

        /// <summary>
        /// 잡아둔 잠금을 원래대로 되돌린다. 잡고 있지 않았다면 아무 일도 하지 않는다.
        ///
        /// 되돌아가는 중에 다시 불리면 기다리지 않고 그 자리에서 원래 각도로 마무리한다.
        /// </summary>
        /// <param name="immediate">
        /// 되돌아가는 연출 없이 지금 끝낸다. 오브젝트가 꺼지거나 씬이 바뀌어 더 돌 시간이 없을 때 쓴다 —
        /// 이때 <see cref="Tick"/>을 불러 줄 사람이 없어서, 부드럽게 돌리려 들면 각도가 그대로 남아버린다.
        /// </param>
        public void Release(bool immediate = false)
        {
            if (_returning)
            {
                if (_root != null)
                {
                    _root.rotation = _previousRotation;
                }

                _returning = false;
                _root = null;
            }

            if (!_applied)
            {
                _movement = null;
                _root = null;
                return;
            }

            _applied = false;

            if (_movement != null)
            {
                // 우리가 걸어둔 잠금이 아직 그대로일 때만 되돌린다. 대화 도중 다른 시스템(넉다운·사다리 등)이
                // 먼저 풀어놨다면 지금은 그쪽이 주인이므로 건드리지 않는다 — 여기서 다시 걸면
                // 대화가 끝난 뒤에도 회전이 묶인 채로 남는다.
                if (_movement.FacingLocked)
                {
                    _movement.FacingLocked = _previousFacingLocked;
                }

                if (_movement.RotationLocked)
                {
                    _movement.RotationLocked = _previousRotationLocked;
                }
            }
            // 잠금 되돌리기와는 **별개**다. 캐릭터가 붙어 있어도(이동을 돌리지 않는 NPC라면 특히)
            // 각도를 되찾아 줄 사람이 없으므로, 요청받았으면 여기서 되돌린다.
            if (_restoreRotation && _root != null)
            {
                _targetRotation = _previousRotation;

                if (_turnSpeed > 0f && !immediate)
                {
                    // 돌아가는 건 Tick이 마저 한다. 그 동안 _root를 놓지 않는다.
                    _returning = true;
                }
                else
                {
                    _root.rotation = _previousRotation;
                }
            }

            _movement = null;
            _restoreRotation = false;

            if (!_returning)
            {
                _root = null;
            }
        }

        /// <summary>
        /// 3D 방향을 좌/우 플립 하나로 환산한다.
        ///
        /// 기준축은 그 자리의 <b>스플라인 접선</b>(<see cref="CharacterMovement.SplineForward"/>)이다.
        /// 원하는 방향이 접선 쪽을 향하면 오른쪽, 반대면 왼쪽이다. 루트 회전 대신 접선을 쓰는 이유는,
        /// 루트 회전이 접선을 Slerp로 뒤따라가기 때문에 방금 순간이동한 직후에는 아직 옛 각도로 남아 있어서다.
        /// 스플라인이 없는 씬에서는 접선이 <c>Vector3.right</c>(월드 +X)로 고정된다.
        ///
        /// 진행축과 거의 수직이면(정면/후면을 보라는 뜻) 좌우 판정 자체가 무의미하므로 지금 보고 있는 쪽을 둔다.
        /// 이 값은 대화 중 화면에는 거의 드러나지 않지만(루트 yaw가 실제 방향을 잡는다),
        /// 대화가 끝나 진행축으로 돌아갔을 때 어느 쪽을 보고 서 있을지를 결정한다.
        /// </summary>
        private static bool ResolveFacingRight(CharacterMovement movement, Vector3 forward)
        {
            Vector3 axis = movement.SplineForward;
            axis.y = 0f;

            if (axis.sqrMagnitude < 0.0001f)
            {
                Transform root = movement.Root;
                axis = root != null ? root.forward : Vector3.right;
                axis.y = 0f;
            }

            if (axis.sqrMagnitude < 0.0001f)
            {
                return movement.FacingRight;
            }

            float alignment = Vector3.Dot(forward.normalized, axis.normalized);

            if (Mathf.Abs(alignment) < 0.01f)
            {
                return movement.FacingRight;
            }

            return alignment > 0f;
        }
    }
}
