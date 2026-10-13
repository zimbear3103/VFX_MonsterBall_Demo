using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Aiara
{
    public class Spline : MonoBehaviour
    {
        Spline previousSpline;
        Spline nextSpline;

        /// <summary>
        /// 스플라인의 진행 방향(tangent).
        /// LookAt(previousSpline) + 90도 회전으로 인해 -transform.right가 진행 방향입니다.
        /// </summary>
        public Vector3 Tangent => -transform.right;

        float previousDistance = 0f;
        float nextDistance = 0f;

        public void Initialize(Spline previous, Spline next)
        {
            previousSpline = previous;
            nextSpline = next;

            if (previousSpline != null)
            {
                var prevPos = previousSpline.transform.position;
                var curPos = transform.position;
                prevPos.y = 0;
                curPos.y = 0;
                if (Vector3.Distance(prevPos, curPos) > 0.01f)
                {
                    transform.LookAt(previousSpline.transform);
                    transform.rotation = transform.rotation * Quaternion.Euler(0f, 90f, 0f);
                }
                previousDistance = Vector3.Distance(transform.position, previousSpline.transform.position);
            }
            if (nextSpline != null)
            {
                nextDistance = Vector3.Distance(transform.position, nextSpline.transform.position);
            }
        }

        // NOTE(ver2 포팅): 구 프로젝트의 Entity 타입은 미승계라 MonoBehaviour로 대체 (컴파일 오류 방지)
        public bool UpdateTransform(MonoBehaviour entity, bool position = false, bool force = false)
        {
            Vector3 currentPosition = entity.transform.position;
            currentPosition.y = 0f;

            Vector3 targetPosition = Vector3.zero;
            Quaternion targetRotation = Quaternion.identity;

            if (nextSpline == null)
            {
                targetPosition = transform.position;
                targetRotation = transform.rotation;
            }
            else if (previousSpline == null)
            {
                targetPosition = transform.position;
                targetRotation = nextSpline.transform.rotation;
            }
            else
            {
                Vector3 previousPosition = previousSpline.transform.position;
                Vector3 nextPosition = nextSpline.transform.position;
                Vector3 thisPosition = transform.position;

                previousPosition.y = 0f;
                nextPosition.y = 0f;
                thisPosition.y = 0f;

                float pDistance = Vector3.Distance(currentPosition, previousPosition);
                float nDistance = Vector3.Distance(currentPosition, nextPosition);
                float distance = Vector3.Distance(currentPosition, thisPosition);

                float pOffset = pDistance / previousDistance;
                float nOffset = nDistance / nextDistance;
                float offset = 0f;

                if (pOffset < nOffset)
                {
                    offset = distance / (distance + pDistance);
                    targetPosition = Vector3.Lerp(transform.position, previousSpline.transform.position, offset);
                }
                else
                {
                    offset = distance / (distance + nDistance);
                    targetPosition = Vector3.Lerp(transform.position, nextSpline.transform.position, offset);
                }
                targetRotation = pOffset < nOffset ? transform.rotation : nextSpline.transform.rotation;
            }

            if (position)
            {
                // 컴파일 에러 방지를 위한 임시 주석처리입니다. TopDownEngine과 병합 후 삭제해주세요.

                // if (entity is Character)
                // {
                //     (entity as Character).movementComponent.Move(new Vector3(targetPosition.x - entity.transform.position.x, 0f, targetPosition.z - entity.transform.position.z));
                //     //entity.transform.position = new Vector3(targetPosition.x, entity.transform.position.y, targetPosition.z);
                // }
                // else
                // {
                //     entity.transform.position = new Vector3(targetPosition.x, entity.transform.position.y, targetPosition.z);
                // }
            }
            if (force)
            {
                entity.transform.rotation = targetRotation;
            }
            else
            {
                entity.transform.rotation = Quaternion.Lerp(entity.transform.rotation, targetRotation, Time.deltaTime * 5f);
            }
            return true;
        }
    }
}
