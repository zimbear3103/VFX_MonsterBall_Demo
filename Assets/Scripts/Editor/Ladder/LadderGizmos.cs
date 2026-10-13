using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 씬 뷰에 사다리를 그린다. 런타임 OnDrawGizmosSelected가 존/축을 그리는 것과 달리,
    /// 여기서는 저작에 필요한 정보 — 캐릭터가 실제로 붙는 선, 한 칸 눈금, 탈출 지점 — 를 겹쳐 그린다.
    /// </summary>
    public static class LadderGizmos
    {
        private static readonly Color AxisColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        private static readonly Color AttachColor = new Color(0.3f, 1f, 0.4f, 0.9f);
        private static readonly Color StepColor = new Color(1f, 0.85f, 0.2f, 0.8f);
        private static readonly Color ExitColor = new Color(1f, 0.35f, 0.9f, 1f);

        public static void DrawLadder(LadderTraversable ladder, bool selected)
        {
            if (ladder == null) return;

            Vector3 bottom = ladder.BottomWorld;
            Vector3 top = ladder.TopWorld;

            // 사다리 축(기둥 중심)
            Handles.color = AxisColor;
            Handles.DrawLine(bottom, top);

            // 캐릭터가 실제로 붙는 선 — climbOffset이 반영된 위치. 축과 벌어진 만큼이 곧 오프셋.
            Vector3 attachBottom = ladder.CenterAtHeight(bottom.y);
            Vector3 attachTop = ladder.CenterAtHeight(top.y);
            Handles.color = AttachColor;
            Handles.DrawLine(attachBottom, attachTop);
            Handles.DrawDottedLine(bottom, attachBottom, 3f);
            Handles.DrawDottedLine(top, attachTop, 3f);

            if (selected) DrawSteps(ladder, attachBottom, attachTop);
            DrawExits(ladder);
        }

        /// <summary>한 칸(StepDistance)마다 눈금을 그려 캐릭터가 멈추는 높이를 보여준다.</summary>
        private static void DrawSteps(LadderTraversable ladder, Vector3 attachBottom, Vector3 attachTop)
        {
            float step = LadderAuthoring.GetFloat(ladder, LadderAuthoring.P_StepDistance);
            float height = attachTop.y - attachBottom.y;
            if (step <= 0.01f || height <= 0.01f) return;

            int count = Mathf.FloorToInt(height / step);
            if (count > 200) return; // 눈금이 무의미해질 만큼 촘촘하면 생략

            Vector3 side = ladder.transform.right * 0.18f;
            Handles.color = StepColor;

            for (int i = 1; i <= count; i++)
            {
                Vector3 p = attachBottom + Vector3.up * (step * i);
                Handles.DrawLine(p - side, p + side);
            }
        }

        private static void DrawExits(LadderTraversable ladder)
        {
            Handles.color = ExitColor;

            Vector3 topExit = ladder.TopExitWorld;
            Vector3 bottomExit = ladder.BottomExitWorld;

            float topSize = HandleUtility.GetHandleSize(topExit) * 0.08f;
            float bottomSize = HandleUtility.GetHandleSize(bottomExit) * 0.08f;

            Handles.DrawWireCube(topExit, Vector3.one * topSize);
            Handles.DrawWireCube(bottomExit, Vector3.one * bottomSize);

            Handles.Label(topExit + Vector3.up * topSize * 2f, "상단 탈출");
            Handles.Label(bottomExit + Vector3.up * bottomSize * 2f, "하단 탈출");
        }
    }
}
