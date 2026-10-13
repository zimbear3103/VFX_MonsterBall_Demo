using System;
using UnityEngine;

/// <summary>
/// 애니메이션 이벤트 릴레이 (구 프로젝트 AnimationEventHandler의 ver2 포팅).
/// 애니메이션 클립의 AnimationEvent에서 AnimationEvent(string) 함수를 호출하면 이 컴포넌트가 받는다.
///
/// 구버전은 MoreMountains(MMAnimationEvent)·TopDownEngine·Aiara.CharacterAnimationEventReceiver에
/// 의존했으나 ver2엔 없으므로 그 의존을 제거했다. 대신 C# 이벤트 OnAnimationEvent로 재발행하여,
/// 필요한 ver2 리스너(사운드/VFX/판정 등)가 구독해 처리하도록 확장 훅만 남긴다.
///
/// 이 컴포넌트가 애니메이션되는 오브젝트(보통 적의 'Visual', Animator 소유)에 붙어 있어야
/// 클립의 AnimationEvent가 수신되며, 없으면 Unity가 "has no receiver!" 에러를 낸다.
/// GUID는 구버전과 동일(97a33d30...)하게 유지한다.
/// </summary>
public class AnimationEventHandler : MonoBehaviour
{
    [Tooltip("체크 시 애니 이벤트마다 로그 출력(디버그용). 평소엔 꺼두어 로그 스팸 방지.")]
    [SerializeField] private bool _verboseLogging = false;

    /// <summary>애니메이션 이벤트 훅. function = 클립 이벤트에 지정된 문자열 인자.</summary>
    public event Action<string> OnAnimationEvent;

    protected Animator _animator;

    protected virtual void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    /// <summary>애니메이션 클립 이벤트가 호출하는 진입점(문자열 인자).</summary>
    public void AnimationEvent(string function)
    {
        if (_verboseLogging)
            Debug.Log($"[AnimationEventHandler][{name}] '{function}' fired.", this);

        OnAnimationEvent?.Invoke(function);
    }
}
