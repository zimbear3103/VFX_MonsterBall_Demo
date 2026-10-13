using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Controller 공통 기반 (Player / AI 공용).
    ///
    /// Controller는 "누가 캐릭터를 조종/판단하는가"를 담당한다. 캐릭터 기능은 구현하지 않고,
    /// 조종 대상 Character의 CommandManager에 명령만 전달한다.
    ///
    /// ※ 컴포넌트 통합(2026-08-19): 조종 대상은 이제 <b>Character 컴포넌트</b>를 가진 GameObject.
    /// CommandManager는 Character가 소유하는 일반 클래스이므로 Character를 통해 접근한다.
    /// 이름을 'CharacterController'로 짓지 않는다 — UnityEngine.CharacterController와 충돌하기 때문.
    /// </summary>
    public abstract class CharacterControllerBase : MonoBehaviour
    {
        [Header("Controlled Character")]
        [Tooltip("조종할 캐릭터 루트. Character 컴포넌트를 가진 오브젝트를 지정한다.")]
        [SerializeField] protected GameObject controlledCharacter;

        /// <summary>현재 조종 중인 캐릭터의 명령 통로(Character가 소유). null이면 아직 Possess 전.</summary>
        public CharacterCommandManager Commands { get; private set; }

        /// <summary>현재 조종 중인 Character(공용 통합 컴포넌트). null 가능.</summary>
        public Character ControlledCharacterComponent { get; private set; }

        /// <summary>현재 조종 중인 캐릭터 루트 GameObject.</summary>
        public GameObject ControlledCharacter => controlledCharacter;

        public bool HasControl => Commands != null;

        protected virtual void Start()
        {
            if (Commands == null && controlledCharacter != null)
                Possess(controlledCharacter);
        }

        /// <summary>GameObject를 조종 대상으로 잡는다(Character 컴포넌트 필요).</summary>
        public void Possess(GameObject characterObject)
        {
            if (characterObject == null)
            {
                UnPossess();
                return;
            }

            var character = characterObject.GetComponent<Character>();
            if (character == null)
            {
                Debug.LogWarning(
                    $"[{GetType().Name}] '{characterObject.name}'에 Character가 없어 Possess할 수 없습니다.",
                    this);
                return;
            }

            Possess(character);
        }

        /// <summary>Character를 직접 지정해 Possess한다.</summary>
        public void Possess(Character character)
        {
            if (character == null)
            {
                UnPossess();
                return;
            }

            ControlledCharacterComponent = character;
            Commands = character.CommandManager;
            controlledCharacter = character.gameObject;
            OnPossessed(Commands);
        }

        public void UnPossess()
        {
            var previous = Commands;
            Commands = null;
            ControlledCharacterComponent = null;
            OnUnPossessed(previous);
        }

        protected virtual void OnPossessed(CharacterCommandManager commands) { }
        protected virtual void OnUnPossessed(CharacterCommandManager previous) { }
    }
}
