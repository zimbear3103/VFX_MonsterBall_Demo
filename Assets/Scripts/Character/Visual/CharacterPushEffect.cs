using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 밀림(Push) 이펙트 — 캐릭터가 수평으로 밀릴 때 지정한 자식 이펙트를 한 번 재생하는 순수 시각 컴포넌트.
    ///
    /// 재생 시점(Play 호출처, 상태/전투 계층이 트리거 소유):
    ///   - 일반 push 밀림  : CharacterHitState.Enter (HitReactionKind.Push)
    ///   - 가드 성공 밀림 : CharacterCombat.ApplyGuardKnockback (가드 자세 유지 밀림)
    /// (넉다운 슬라이드에는 재생하지 않는다.)
    ///
    /// CharacterAfterimage와 동일한 방식으로 CharacterStateManager가 GetComponentInChildren로 찾아
    /// Machine.PushEffect?.Play()로 호출한다 — Character/State/Combat 코어의 구조나 책임은 건드리지 않는다.
    /// 이펙트 오브젝트는 인스펙터에서 자식으로 할당하며, 시작 시 꺼둔 뒤 밀릴 때마다 켜고 파티클을 다시 재생한다.
    /// </summary>
    [AddComponentMenu("Yeolha/Visual/Character Push Effect")]
    public class CharacterPushEffect : MonoBehaviour
    {
        [Header("Effect")]
        [Tooltip("밀릴 때 재생할 이펙트 오브젝트(자식으로 두고 여기에 할당). 비우면 아무것도 하지 않는다.")]
        [SerializeField] private GameObject pushEffect;

        [Tooltip("시작 시 이펙트 오브젝트를 자동으로 끈다(밀리기 전까지 보이지 않게). 파티클의 Play On Awake는 별도 설정.")]
        [SerializeField] private bool hideOnStart = true;

        private ParticleSystem[] _particles;
        private bool _resolved;

        private void Awake()
        {
            ResolveParticles();
        }

        private void Start()
        {
            if (hideOnStart && pushEffect != null && pushEffect.activeSelf)
                pushEffect.SetActive(false);
        }

        /// <summary>
        /// 밀림 이펙트 1회 재생 — 이펙트 오브젝트를 켜고 하위 파티클을 Clear 후 Play로 다시 뿜는다.
        /// 연타로 계속 밀려도 매번 처음부터 재생된다. 미할당이면 무시.
        /// </summary>
        public void Play()
        {
            if (pushEffect == null || !isActiveAndEnabled) return;

            if (!pushEffect.activeSelf)
                pushEffect.SetActive(true);

            ResolveParticles();
            for (int i = 0; i < _particles.Length; i++)
            {
                ParticleSystem ps = _particles[i];
                if (ps == null) continue;
                ps.Clear(true);
                ps.Play(true);
            }
        }

        private void ResolveParticles()
        {
            if (_resolved && _particles != null) return;
            _resolved = true;
            _particles = pushEffect != null
                ? pushEffect.GetComponentsInChildren<ParticleSystem>(true)
                : new ParticleSystem[0];
        }
    }
}
