using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Optional per-target capability + carry tuning for the chain grab.
    /// Attach to an enemy (or grabbable object) to restrict what the chain may do to it,
    /// and to fine-tune the held position per enemy size.
    ///
    /// Absent = default (chainable, pullable, carryable, zero offset), so every existing
    /// enemy keeps working without adding this component. This follows the existing
    /// "capability lives on the target as a component/method" convention (cf. EnemyCharacter.SetGrabbed)
    /// instead of introducing a new interface.
    /// </summary>
    [DisallowMultipleComponent]
    public class ChainCarryTarget : MonoBehaviour
    {
        [Header("Capability")]
        [Tooltip("체인 훅에 걸릴 수 있는가. 끄면 이 대상은 체인이 무시한다(대형 적/보스/비대상 오브젝트).")]
        [SerializeField] private bool canChain = true;
        [Tooltip("체인으로 끌려올 수 있는가. 끄면 걸려도 제자리에서 당김만 시도한다(대형 적).")]
        [SerializeField] private bool canPull = true;
        [Tooltip("들쳐업기(Carry)까지 진행할 수 있는가. 끄면 끌어온 뒤 바로 놓는다(무거운 적).")]
        [SerializeField] private bool canCarry = true;

        [Header("Carry")]
        [Tooltip("들었을 때 기본 유지 위치에 더해지는 로컬 오프셋(m). 적 종류/크기별 위치 보정용. 인스펙터에서 조정.")]
        [SerializeField] private Vector3 carryOffset = Vector3.zero;
        [Tooltip("들었을 때 손 위치(HoldAnchor)에 맞출 적 몸의 기준점(예: 목 본). 미지정 시 루트가 기준이라 기존 동작 유지. 적별로 인스펙터에서 지정.")]
        [SerializeField] private Transform carryAnchor;

        public bool CanChain => canChain;
        public bool CanPull => canPull;
        public bool CanCarry => canCarry;
        public Vector3 CarryOffset => carryOffset;
        public Transform CarryAnchor => carryAnchor;
    }
}
