using UnityEngine;
using Yeolha.BeltScroll;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 대화/이벤트 동안 플레이어 조작을 통째로 묶어두는 잠금.
    ///
    /// 두 채널을 모두 끊어야 실제로 조작이 멈춘다 — 하나만으로는 부족하다:
    ///   - <b>입력</b>: <see cref="PlayerController"/> 컴포넌트를 꺼서 새 명령이 들어오지 않게 한다.
    ///     끄는 것만으로는 이미 세워둔 이동/홀드 값이 그대로 남으므로
    ///     <see cref="CharacterCommandManager"/>를 직접 비워준다 — 안 그러면 마지막 스틱 값이
    ///     대화 내내 캐릭터를 밀고 있다.
    ///   - <b>이동</b>: 컨트롤러를 못 찾는 경우(AI가 조종 중이거나 씬 구성이 다른 경우)를 대비해
    ///     <see cref="CharacterMovement.CanMove"/>·<see cref="CharacterMovement.CanJump"/>를 직접 내린다.
    ///     이 둘은 명령이 어디서 오든 이동/점프 자체를 막는다.
    ///
    /// 원본(TDE) 구현은 <c>InputManager.InputDetectionActive</c>와 <c>CharacterMovement.MovementForbidden</c>을
    /// 껐지만, ver2에는 전역 InputManager가 없고 입력은 PlayerController가 소유하므로 그 자리에
    /// 컴포넌트 비활성화 + 명령 비우기가 들어간다.
    ///
    /// 잠금은 이름(<c>owner</c>)으로 관리되어 자기가 건 것만 푼다. 껐던 값들은 잠글 때의 값을 기억했다가
    /// 그대로 되돌린다 — 다른 시스템이 이미 꺼둔 상태였다면 켜버리지 않기 위해서다.
    /// </summary>
    public struct DialogueControlLock
    {
        private Character _character;
        private CharacterMovement _movement;
        private PlayerController _controller;
        private string _owner;
        private bool _locked;
        private bool _previousControllerEnabled;
        private bool _previousCanMove;
        private bool _previousCanJump;

        public bool IsLocked => _locked;

        /// <summary>지금 이 잠금을 건 주체 이름. 잠겨 있지 않으면 빈 문자열.</summary>
        public string Owner => _locked ? (_owner ?? string.Empty) : string.Empty;

        public void Lock(Character character, string owner)
        {
            if (_locked || character == null)
            {
                return;
            }

            _character = character;
            _owner = owner;
            _locked = true;

            // ① 입력 — 이 캐릭터를 조종 중인 컨트롤러를 찾아 끈다.
            _controller = FindControllerFor(character);
            if (_controller != null)
            {
                _previousControllerEnabled = _controller.enabled;
                _controller.enabled = false;
            }

            // 컨트롤러를 껐어도 마지막으로 세워둔 명령은 그대로 남아 있다. 반드시 비운다.
            ClearCommands(character);

            // ② 이동 — 명령이 어디서 오든 이동·점프 자체를 막는다.
            _movement = character.Movement;
            if (_movement != null)
            {
                _previousCanMove = _movement.CanMove;
                _previousCanJump = _movement.CanJump;
                _movement.CanMove = false;
                _movement.CanJump = false;
            }
        }

        public void Unlock()
        {
            if (!_locked)
            {
                return;
            }

            _locked = false;

            if (_movement != null)
            {
                _movement.CanMove = _previousCanMove;
                _movement.CanJump = _previousCanJump;
                _movement = null;
            }

            if (_controller != null)
            {
                _controller.enabled = _previousControllerEnabled;
                _controller = null;
            }

            // 잠금이 풀리는 프레임에 묵은 명령이 되살아나지 않도록 한 번 더 비운다.
            ClearCommands(_character);

            _character = null;
            _owner = null;
        }

        /// <summary>이동/홀드/원샷 명령을 모두 비운다. 잠글 때와 풀 때 양쪽에서 부른다.</summary>
        private static void ClearCommands(Character character)
        {
            CharacterCommandManager commands = character != null ? character.CommandManager : null;
            if (commands == null)
            {
                return;
            }

            commands.SetMoveInput(Vector2.zero);
            commands.SetRunHeld(false);
            commands.SetGuardHeld(false);
            commands.ClearOneShots();
            commands.ClearComboReservation();
        }

        /// <summary>
        /// 이 캐릭터를 Possess하고 있는 <see cref="PlayerController"/>. 없으면 null.
        ///
        /// 컨트롤러는 캐릭터와 분리된 오브젝트에 있어 <c>GetComponent</c>로는 찾을 수 없다.
        /// 대화를 여는 순간에만 한 번 도는 경로라 씬 검색으로 충분하다.
        /// </summary>
        private static PlayerController FindControllerFor(Character character)
        {
            PlayerController[] controllers =
                Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);

            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i] != null && controllers[i].ControlledCharacterComponent == character)
                {
                    return controllers[i];
                }
            }

            return null;
        }
    }
}
