using UnityEditor;
using UnityEngine;

namespace Aiara.Dialogue.EditorTools
{
    /// <summary>
    /// 선택한 NPC에 말 걸기 존을 붙여준다.
    /// 메뉴: <b>Tools/Aiara/선택한 NPC에 대화 존 붙이기</b>
    ///
    /// 존은 NPC 본체가 아니라 **자식 오브젝트**로 만든다. NPC 본체에 트리거 콜라이더를 얹으면
    /// 이미 붙어 있는 피격/이동용 콜라이더와 섞여서 나중에 구분이 안 되기 때문이다.
    /// 자식으로 두면 말 거는 범위만 따로 키우고 줄일 수도 있다.
    ///
    /// 붙인 뒤 인스펙터에서 채워야 하는 건 <b>Conversations</b> 목록(이벤트 ID)이다.
    /// 위에서부터 조건을 검사해 처음 통과하는 것이 열리므로, 조건 없는 잡담은 맨 아래에 둔다.
    ///
    /// 대화 시작 시 플레이어·카메라를 앉힐 자리(<see cref="DialogueStaging"/>)도 같이 만들어준다.
    /// NPC 앞 <see cref="StandDistance"/>m 지점에 설 자리를, 그 둘을 옆에서 잡는 지점에 카메라를,
    /// NPC 자리에 대화 중 볼 각도를 놓는 **기본 구도**이고, 씬 뷰에서 옮겨·돌려 잡으라고 만들어두는 것이다.
    /// **이미 있는 지점은 절대 건드리지 않는다** — 여러 번 눌러도 잡아둔 자리가 안 날아간다.
    /// </summary>
    public static class NpcDialogueZoneSetup
    {
        private const string ZoneObjectName = "NPC Dialogue Zone";
        private const string StandPointName = "Player Stand Point";
        private const string FacingPointName = "NPC Facing Point";
        private const string CameraPointName = "Camera View Point";

        /// <summary>플레이어가 NPC 앞 몇 m에 서게 할지.</summary>
        private const float StandDistance = 2f;

        /// <summary>대화 카메라를 두 사람 축에서 옆으로 얼마나 뺄지.</summary>
        private const float CameraSideDistance = 3.2f;

        /// <summary>대화 카메라 높이(바닥 기준).</summary>
        private const float CameraHeight = 1.8f;

        /// <summary>카메라가 바라볼 지점의 높이(대략 가슴~얼굴).</summary>
        private const float LookAtHeight = 1.3f;

        [MenuItem("Tools/Aiara/선택한 NPC에 대화 존 붙이기", false, 21)]
        public static void AttachDialogueZone()
        {
            GameObject[] targets = Selection.gameObjects;
            if (targets == null || targets.Length == 0)
            {
                EditorUtility.DisplayDialog("대화 존 붙이기",
                    "하이라키에서 NPC를 먼저 선택하세요.", "확인");
                return;
            }

            var created = new System.Collections.Generic.List<GameObject>();

            foreach (GameObject npc in targets)
            {
                if (npc == null)
                {
                    continue;
                }

                GameObject zone = AttachTo(npc);
                if (zone != null)
                {
                    created.Add(zone);
                }
            }

            if (created.Count == 0)
            {
                return;
            }

            // 붙인 존을 선택 상태로 만들어, 곧바로 대화 제목을 입력할 수 있게 한다.
            Selection.objects = created.ToArray();

            Debug.Log($"[대화 존] {created.Count}개를 정리했습니다.\n" +
                      "1) 인스펙터의 'Conversations'에 이벤트 ID를 넣으세요.\n" +
                      "2) 'Player Stand Point'를 플레이어가 설 자리로 옮기세요 — 돌려놓은 각도가 곧 플레이어가 볼 방향입니다.\n" +
                      "3) 'NPC Facing Point'를 돌려 대화 중 NPC가 볼 각도를 잡으세요(위치는 상관없습니다).\n" +
                      "4) 씬 뷰를 원하는 구도로 맞춘 뒤, Dialogue Staging 컴포넌트 기어 메뉴의 " +
                      "'카메라 지점 ← 지금 씬 뷰 화면'을 누르면 그 화면이 그대로 대화 구도가 됩니다.");
        }

        /// <summary>NPC 하나에 존과 연출 배치를 붙인다. 이미 있는 것은 그대로 두고 빠진 것만 채운다.</summary>
        private static GameObject AttachTo(GameObject npc)
        {
            // 본체나 자식에 이미 존이 있으면 새로 만들지 않는다 — 여러 번 눌러도 안전해야 한다.
            var existing = npc.GetComponentInChildren<NpcDialogueZone>(true);
            if (existing != null)
            {
                // 존은 그대로 두고, 아직 없는 연출 배치만 채워준다.
                EnsureStaging(npc, existing);
                Debug.Log($"[대화 존] '{npc.name}'에는 이미 대화 존이 있어 연출 배치만 확인했습니다.", existing);
                return existing.gameObject;
            }

            var zoneObject = new GameObject(ZoneObjectName);
            Undo.RegisterCreatedObjectUndo(zoneObject, "NPC 대화 존 붙이기");

            Undo.SetTransformParent(zoneObject.transform, npc.transform, "NPC 대화 존 붙이기");
            zoneObject.transform.localPosition = Vector3.zero;
            zoneObject.transform.localRotation = Quaternion.identity;
            zoneObject.transform.localScale = Vector3.one;
            zoneObject.layer = ResolveZoneLayer(npc);

            var sphere = Undo.AddComponent<SphereCollider>(zoneObject);
            sphere.isTrigger = true;
            sphere.radius = NpcDialogueZone.DefaultZoneRadius;

            var zone = Undo.AddComponent<NpcDialogueZone>(zoneObject);

            // 카메라·연출이 존이 아니라 NPC 본체를 바라보게 한다.
            zone.ConversantTransform = npc.transform;
            zone.ButtonPromptText = "X";

            EnsureStaging(npc, zone);

            EditorUtility.SetDirty(zone);
            return zoneObject;
        }

        /// <summary>
        /// 대화 시작 시 플레이어·카메라를 앉힐 배치를 만들어 존에 물린다.
        ///
        /// 이미 있는 것은 손대지 않는다 — 컴포넌트도, 지점 오브젝트도, 이미 물려 있는 슬롯도.
        /// 자리를 잡아둔 뒤에 이 메뉴를 다시 눌러도 잡아둔 구도가 그대로 남아야 하기 때문이다.
        /// </summary>
        private static void EnsureStaging(GameObject npc, NpcDialogueZone zone)
        {
            GameObject zoneObject = zone.gameObject;

            var staging = zone.Staging != null ? zone.Staging : zoneObject.GetComponentInChildren<DialogueStaging>(true);
            if (staging == null)
            {
                staging = Undo.AddComponent<DialogueStaging>(zoneObject);
            }

            if (zone.Staging != staging)
            {
                Undo.RecordObject(zone, "대화 연출 배치 만들기");
                zone.Staging = staging;
                EditorUtility.SetDirty(zone);
            }

            Undo.RecordObject(staging, "대화 연출 배치 만들기");

            // 구도의 기준은 **존이 가리키는 대화 상대**다. 존과 NPC 모델이 따로 떨어져 있는 씬에서도
            // 지점들이 모델 주위에 잡히도록, 존을 선택해 이 메뉴를 눌렀을 때까지 맞게 만든다.
            Transform npcTransform = zone.ConversantTransform != null ? zone.ConversantTransform : npc.transform;

            // NPC가 바라보는 방향을 기준으로 잡는다. 위아래 성분은 버려서 지면에 평평하게 눕힌다.
            Vector3 axis = Vector3.ProjectOnPlane(npcTransform.forward, Vector3.up);
            if (axis.sqrMagnitude < 0.0001f)
            {
                axis = Vector3.forward;
            }

            axis.Normalize();

            Vector3 npcPosition = npcTransform.position;
            Vector3 standPosition = npcPosition + axis * StandDistance;

            if (staging.PlayerStandPoint == null)
            {
                // 설 자리는 NPC를 마주 보게 둔다(Facing이 StandPointForward일 때도 바로 맞도록).
                Transform standPoint = CreatePoint(zoneObject, StandPointName, standPosition,
                    Quaternion.LookRotation(-axis));
                staging.PlayerStandPoint = standPoint;
            }

            EnsureConversantFacingPoint(staging, npcPosition);

            if (staging.CameraViewPoint == null)
            {
                // 두 사람을 옆에서 잡는 기본 구도. 축의 오른쪽에서 살짝 위로.
                Vector3 lookAt = (npcPosition + standPosition) * 0.5f + Vector3.up * LookAtHeight;
                Vector3 side = Vector3.Cross(Vector3.up, axis).normalized;
                Vector3 cameraPosition = new Vector3(lookAt.x, npcPosition.y + CameraHeight, lookAt.z)
                                         + side * CameraSideDistance;

                Vector3 toLookAt = lookAt - cameraPosition;
                Quaternion cameraRotation = toLookAt.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(toLookAt)
                    : Quaternion.identity;

                staging.CameraViewPoint = CreatePoint(zoneObject, CameraPointName, cameraPosition, cameraRotation);
            }

            EditorUtility.SetDirty(staging);
        }

        /// <summary>
        /// NPC가 대화 중 볼 각도를 잡는 지점을 만들어 물린다. 이미 있으면 손대지 않는다.
        ///
        /// **지점의 파란 축이 곧 NPC 모델이 볼 방향**이라 위치는 상관없지만, NPC 자리에 두어야
        /// 씬 뷰에서 화살표가 몸에 붙어 보인다. 처음 만들 때는 <b>지금 모델이 보고 있는 쪽</b>으로 맞춰둔다 —
        /// 그래야 돌려놓기 전까지는 대화를 해도 각도가 바뀌지 않는다(정면 축이 +Z가 아닌 모델도 마찬가지).
        /// </summary>
        private static void EnsureConversantFacingPoint(DialogueStaging staging, Vector3 npcPosition)
        {
            if (staging.ConversantFacingPoint != null)
            {
                return;
            }

            Vector3 modelForward = staging.ConversantModelForwardDirection;
            Quaternion rotation = modelForward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(modelForward)
                : Quaternion.identity;

            staging.ConversantFacingPoint = CreatePoint(staging.gameObject, FacingPointName, npcPosition, rotation);

            // 지점을 방금 만들었을 때만 모드를 맞춰준다. 이미 잡아둔 설정은 건드리지 않는다.
            if (staging.ConversantFacing == DialogueStaging.ConversantFacingModes.Keep)
            {
                staging.ConversantFacing = DialogueStaging.ConversantFacingModes.ConversantPointForward;
            }
        }

        /// <summary>
        /// 열려 있는 씬의 대화 연출 배치들을 지금 규칙에 맞게 정리한다.
        /// 메뉴: <b>Tools/Aiara/대화 존 방향 설정 정리</b>
        ///
        /// 하는 일은 하나다 — <b>NPC 각도 지점이 없는 배치에 지점을 만들어 물리고, 방향 모드를 그 지점으로 맞춘다.</b>
        /// 지점은 '지금 모델이 보는 쪽'으로 만들어지므로, 돌려놓기 전까지는 대화를 해도 각도가 바뀌지 않는다.
        ///
        /// 잡아둔 것은 건드리지 않는다 — 이미 있는 각도 지점, 설 자리, 카메라 지점, 그리고 이미 골라둔 방향 모드는 그대로 둔다.
        /// </summary>
        [MenuItem("Tools/Aiara/대화 존 방향 설정 정리", false, 22)]
        public static void FixFacingSettings()
        {
            DialogueStaging[] stagings =
                Object.FindObjectsByType<DialogueStaging>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (stagings.Length == 0)
            {
                EditorUtility.DisplayDialog("대화 존 방향 설정 정리",
                    "열려 있는 씬에서 Dialogue Staging을 찾지 못했습니다.", "확인");
                return;
            }

            int touched = 0;
            int pointsCreated = 0;

            foreach (DialogueStaging staging in stagings)
            {
                if (staging == null)
                {
                    continue;
                }

                bool hadPoint = staging.ConversantFacingPoint != null;
                bool needsMode = staging.ConversantFacing == DialogueStaging.ConversantFacingModes.Keep;

                if (hadPoint && !needsMode)
                {
                    continue;
                }

                Undo.RecordObject(staging, "대화 존 방향 설정 정리");

                Transform conversant = staging.ConversantRoot;
                EnsureConversantFacingPoint(staging, conversant != null ? conversant.position : staging.transform.position);

                if (!hadPoint && staging.ConversantFacingPoint != null)
                {
                    pointsCreated++;
                }

                EditorUtility.SetDirty(staging);
                touched++;

                if (!Application.isPlaying)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(staging.gameObject.scene);
                }
            }

            Debug.Log($"[대화 존] 방향 설정 {touched}개를 손봤습니다 (각도 지점 {pointsCreated}개 생성).\n" +
                      "· 대화가 끝나면 NPC는 잡기 전 각도로 되돌아갑니다(그대로 두려면 Keep Conversant Facing After End).\n" +
                      "· 각 존의 'NPC Facing Point'를 돌려 대화 중 NPC가 볼 각도를 잡으세요.\n" +
                      "· NPC가 90° 어긋나 서면 Dialogue Staging의 'Conversant Model Forward'를 " +
                      "실제 모델 정면 축(이 프로젝트 NPC는 대체로 +X)으로 바꾸세요. " +
                      "씬 뷰의 회색 선이 '지금 모델이 보는 쪽'입니다.");
        }

        /// <summary>지점용 빈 오브젝트를 만든다. 이미 같은 이름이 있으면 그것을 그대로 쓴다.</summary>
        private static Transform CreatePoint(GameObject parent, string name, Vector3 position, Quaternion rotation)
        {
            Transform existing = parent.transform.Find(name);
            if (existing != null)
            {
                return existing;
            }

            var point = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(point, "대화 연출 배치 만들기");
            Undo.SetTransformParent(point.transform, parent.transform, "대화 연출 배치 만들기");

            point.transform.SetPositionAndRotation(position, rotation);
            point.transform.localScale = Vector3.one;

            return point.transform;
        }

        /// <summary>
        /// 존을 올릴 레이어를 고른다. 기본은 NPC와 같은 레이어지만, 그 레이어가 Physics 설정에서
        /// Player와 충돌하지 않게 돼 있으면 트리거가 아예 안 불린다 — 그때는 Default로 떨어뜨린다.
        /// (증상이 "버튼을 눌러도 아무 일도 없음"이라 원인을 찾기 어려운 종류의 실수다.)
        /// </summary>
        private static int ResolveZoneLayer(GameObject npc)
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer < 0 || !Physics.GetIgnoreLayerCollision(npc.layer, playerLayer))
            {
                return npc.layer;
            }

            Debug.LogWarning($"[대화 존] '{npc.name}'의 레이어({LayerMask.LayerToName(npc.layer)})는 " +
                             "Player와 충돌하지 않도록 설정돼 있어 존을 Default 레이어에 올립니다.", npc);

            return 0;
        }
    }
}
