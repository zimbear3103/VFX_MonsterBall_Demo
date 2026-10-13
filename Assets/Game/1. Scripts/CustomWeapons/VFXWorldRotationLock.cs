using UnityEngine;

namespace Aiara
{
    /// <summary>
    /// 부모에 붙어 위치는 함께 이동하되, 월드 회전은 지정한 값으로 고정한다.
    /// 손/총구에 붙인 발사 이펙트가 부모(본)의 회전을 따라 돌아가는 것을 막을 때 사용.
    /// </summary>
    [AddComponentMenu("Aiara/Effects/VFX World Rotation Lock")]
    public class VFXWorldRotationLock : MonoBehaviour
    {
        [Tooltip("유지할 월드 회전")]
        public Quaternion WorldRotation = Quaternion.identity;

        protected virtual void LateUpdate()
        {
            transform.rotation = WorldRotation;
        }
    }
}
