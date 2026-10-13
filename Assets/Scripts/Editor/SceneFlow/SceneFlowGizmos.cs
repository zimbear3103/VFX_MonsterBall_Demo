using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 씬 뷰에 포탈과 진입점을 그린다. 런타임 기즈모가 위치만 찍는 것과 달리,
    /// 저작에 필요한 정보 — 이 포탈이 어느 그룹의 어느 키로 보내는지, 진입점이 어느 쪽을 보는지 — 를 겹쳐 그린다.
    /// </summary>
    public static class SceneFlowGizmos
    {
        private static readonly Color PortalColor = new Color(0.6f, 0.4f, 1f, 0.9f);
        private static readonly Color PortalBadColor = new Color(1f, 0.4f, 0.3f, 0.9f);
        private static readonly Color EntryColor = new Color(0.3f, 1f, 0.5f, 0.9f);
        private static readonly Color FacingColor = new Color(1f, 0.85f, 0.2f, 0.9f);

        public static void DrawPortal(Portal portal, bool selected)
        {
            if (portal == null) return;

            bool broken = SceneFlowValidator.CountErrors(SceneFlowValidator.ValidatePortal(portal)) > 0;
            Handles.color = broken ? PortalBadColor : PortalColor;

            Collider col = portal.GetComponent<Collider>();
            if (col is BoxCollider box)
            {
                Matrix4x4 prev = Handles.matrix;
                Handles.matrix = box.transform.localToWorldMatrix;
                Handles.DrawWireCube(box.center, box.size);
                Handles.matrix = prev;
            }
            else if (col != null)
            {
                Bounds b = col.bounds;
                Handles.DrawWireCube(b.center, b.size);
            }

            if (!selected) return;

            Vector3 top = portal.transform.position + Vector3.up * 0.5f;
            Handles.Label(top, PortalSummary(portal));
        }

        private static string PortalSummary(Portal portal)
        {
            if (portal.TargetGroup == null) return "포탈 → (대상 없음)";

            string key = SceneFlowAuthoring.ResolvedEntryKey(portal);
            string suffix = string.IsNullOrEmpty(portal.EntryKey) ? " (그룹 기본)" : "";
            return $"포탈 → {portal.TargetGroup.name} / {key}{suffix}";
        }

        public static void DrawEntry(SceneEntryPoint entry, bool selected)
        {
            if (entry == null) return;

            Vector3 pos = entry.transform.position;
            float size = HandleUtility.GetHandleSize(pos);

            Handles.color = EntryColor;
            Handles.DrawWireDisc(pos, Vector3.up, size * 0.25f);
            Handles.DrawLine(pos, pos + Vector3.up * size * 0.6f);

            // 스폰 직후 바라볼 방향. 벨트스크롤에서는 이게 틀리면 곧장 뒤로 걷는 것처럼 보인다.
            Vector3 facing = entry.FaceRight ? Vector3.right : Vector3.left;
            Handles.color = FacingColor;
            Handles.ArrowHandleCap(0, pos + Vector3.up * size * 0.3f,
                                   Quaternion.LookRotation(facing), size * 0.5f, EventType.Repaint);

            if (!selected) return;

            Handles.color = EntryColor;
            Handles.Label(pos + Vector3.up * size * 0.75f,
                          $"진입점 '{entry.EntryKey}' · {(entry.FaceRight ? "오른쪽" : "왼쪽")}");
        }
    }
}
