using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Mantle (climb-up) state. Moves the character from the ledge-hang point up onto the
    /// top surface (ClimbableSurface.MantleWorld) over a short interpolation, then hands off
    /// to the input-resolved grounded state. Gameplay owns the final position even if the
    /// clip carries root motion, so the character always lands exactly on the surface.
    ///
    /// Forced reactions win via ChangeState -> Exit (EndTraversal restores gravity).
    /// </summary>
    public class CharacterMantleState : CharacterState
    {
        private const float FallbackDuration = 0.45f;

        private float _t;
        private float _duration;
        private Vector3 _start;
        private Vector3 _target;
        private bool _valid;

        public override CharacterStateType Type => CharacterStateType.Mantle;

        public override void Enter()
        {
            var surf = Movement != null ? Movement.ActiveClimbable : null;
            if (surf == null)
            {
                Machine.ChangeState(Machine.ResolveGroundedStateByInput());
                return;
            }

            Movement.BeginTraversal();
            Movement.RotationLocked = true;
            Movement.FacingLocked = true;
            Machine.TriggerMantle();

            _t = 0f;
            _start = Movement.Root.position;
            _target = surf.MantleWorld;
            _duration = surf.MantleDuration > 0.01f ? surf.MantleDuration : FallbackDuration;
            _valid = true;
        }

        public override void Tick(float deltaTime)
        {
            if (!_valid)
            {
                Machine.ChangeState(Machine.ResolveGroundedStateByInput());
                return;
            }

            _t += deltaTime;
            float k = _duration > 0f ? Mathf.Clamp01(_t / _duration) : 1f;

            Vector3 desired = Vector3.Lerp(_start, _target, k);
            Vector3 cur = Movement.Root.position;
            Movement.SetTraversalVelocity(deltaTime > 0f ? (desired - cur) / deltaTime : Vector3.zero);

            if (k >= 1f)
            {
                // Guarantee the exact landing position regardless of collisions/root motion.
                Movement.EndTraversal();
                Movement.Teleport(_target, Movement.FacingRight);
                Movement.ActiveClimbable = null;
                _valid = false;
                Machine.ChangeState(Machine.ResolveGroundedStateByInput());
            }
        }

        public override void Exit()
        {
            if (Movement == null) return;
            Movement.EndTraversal();
            Movement.RotationLocked = false;
            Movement.FacingLocked = false;
            Movement.ActiveClimbable = null;
            _valid = false;
        }
    }
}
