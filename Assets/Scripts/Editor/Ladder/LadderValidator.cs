using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 사다리 세팅에서 자주 나는 실수를 훑어 경고 목록으로 돌려준다.
    /// 기계적으로 고칠 수 있는 항목은 Fix 델리게이트를 함께 실어 보낸다.
    ///
    /// 사다리는 계층(Zone/Exit) · 수치(Step) · 애니메이터(파라미터/이벤트) 세 축이 모두 맞아야
    /// 동작하므로, 각 축을 따로 검사할 수 있게 나눠 두었다.
    /// </summary>
    public static class LadderValidator
    {
        /// <summary>한 칸 이동이 이보다 오래 걸리면 등반이 기어가는 것처럼 보인다.</summary>
        private const float MaxSaneStepDuration = 1.2f;

        /// <summary>한 칸 이동이 이보다 짧으면 애니 사이클보다 빨라 순간이동처럼 보인다.</summary>
        private const float MinSaneStepDuration = 0.15f;

        public struct Issue
        {
            public MessageType Type;
            public string Message;
            public UnityEngine.Object Context;
            public string FixLabel;
            public Action Fix;
        }

        // ─────────── 사다리 개별 검증 ───────────

        public static List<Issue> Validate(LadderTraversable ladder)
        {
            List<Issue> issues = new List<Issue>();
            if (ladder == null) return issues;

            ValidateHierarchy(ladder, issues);
            ValidateZones(ladder, issues);
            ValidateNumbers(ladder, issues);
            ValidatePlacement(ladder, issues);

            return issues;
        }

        private static void ValidateHierarchy(LadderTraversable ladder, List<Issue> issues)
        {
            bool missing =
                LadderAuthoring.GetRef(ladder, LadderAuthoring.P_ClimbVolume) == null ||
                LadderAuthoring.GetRef(ladder, LadderAuthoring.P_BottomZone) == null ||
                LadderAuthoring.GetRef(ladder, LadderAuthoring.P_TopZone) == null ||
                LadderAuthoring.GetRef(ladder, LadderAuthoring.P_TopExitPoint) == null ||
                LadderAuthoring.GetRef(ladder, LadderAuthoring.P_BottomExitPoint) == null;

            if (missing)
            {
                Add(issues, MessageType.Error,
                    "Zone/Exit 참조가 비어 있습니다. 진입 판정이나 탈출 위치가 동작하지 않습니다.", ladder,
                    "계층 자동 구성", () => LadderAuthoring.EnsureHierarchy(ladder));
            }
        }

        private static void ValidateZones(LadderTraversable ladder, List<Issue> issues)
        {
            CheckZone(ladder, LadderAuthoring.P_BottomZone, LadderZone.Kind.Bottom, "BottomZone", issues);
            CheckZone(ladder, LadderAuthoring.P_TopZone, LadderZone.Kind.Top, "TopZone", issues);
        }

        private static void CheckZone(LadderTraversable ladder, string prop, LadderZone.Kind expected,
                                      string label, List<Issue> issues)
        {
            LadderZone zone = LadderAuthoring.GetRef(ladder, prop) as LadderZone;
            if (zone == null) return; // 계층 검증에서 이미 보고됨

            if (zone.ZoneKind != expected)
            {
                // 상/하단이 뒤바뀌면 CheckLadder의 입력 방향 조건이 반대로 걸려 사다리에 붙지 못한다.
                Add(issues, MessageType.Error,
                    $"{label}의 Kind가 {zone.ZoneKind}로 되어 있습니다(정상: {expected}). 진입 방향이 반대로 판정됩니다.", zone,
                    "Kind 교정", () =>
                    {
                        SerializedObject so = new SerializedObject(zone);
                        so.FindProperty(LadderAuthoring.P_ZoneKind).enumValueIndex = expected == LadderZone.Kind.Top ? 1 : 0;
                        so.ApplyModifiedProperties();
                    });
            }

            UnityEngine.Object owner = LadderAuthoring.GetRef(zone, LadderAuthoring.P_ZoneLadder);
            if (owner != ladder)
            {
                Add(issues, MessageType.Error,
                    $"{label}의 Ladder 참조가 이 사다리를 가리키지 않습니다.", zone,
                    "참조 연결", () =>
                    {
                        SerializedObject so = new SerializedObject(zone);
                        so.FindProperty(LadderAuthoring.P_ZoneLadder).objectReferenceValue = ladder;
                        so.ApplyModifiedProperties();
                    });
            }

            BoxCollider box = zone.GetComponent<BoxCollider>();
            if (box == null)
            {
                Add(issues, MessageType.Error, $"{label}에 BoxCollider가 없어 진입을 감지하지 못합니다.", zone,
                    "BoxCollider 추가", () =>
                    {
                        BoxCollider added = Undo.AddComponent<BoxCollider>(zone.gameObject);
                        added.isTrigger = true;
                    });
            }
            else if (!box.isTrigger)
            {
                Add(issues, MessageType.Warning, $"{label}의 Is Trigger가 꺼져 있어 플레이어를 밀어냅니다.", zone,
                    "Is Trigger 켜기", () =>
                    {
                        Undo.RecordObject(box, "Set Is Trigger");
                        box.isTrigger = true;
                        EditorUtility.SetDirty(box);
                    });
            }
        }

        private static void ValidateNumbers(LadderTraversable ladder, List<Issue> issues)
        {
            float height = LadderAuthoring.GetFloat(ladder, LadderAuthoring.P_Height);
            float stepDistance = LadderAuthoring.GetFloat(ladder, LadderAuthoring.P_StepDistance);
            float stepSpeed = LadderAuthoring.GetFloat(ladder, LadderAuthoring.P_StepSpeed);
            float duration = LadderAuthoring.StepDuration(ladder);

            if (stepDistance > height)
            {
                Add(issues, MessageType.Warning,
                    $"한 칸 이동 거리({stepDistance:0.##}m)가 사다리 높이({height:0.##}m)보다 큽니다. 한 번에 끝까지 올라갑니다.", ladder);
            }

            if (duration > MaxSaneStepDuration)
            {
                // 프리팹 기본값(거리 0.6 / 속도 0.4)이 이 경우다 — 두 값이 뒤바뀐 것으로 보이는 전형적인 실수.
                Add(issues, MessageType.Warning,
                    $"한 칸에 {duration:0.##}초 걸립니다(거리 {stepDistance:0.##}m ÷ 속도 {stepSpeed:0.##}m/s). " +
                    "등반이 기어가듯 보입니다. 두 값이 뒤바뀌지 않았는지 확인하세요.", ladder,
                    "거리·속도 맞바꾸기", () =>
                    {
                        Undo.RecordObject(ladder, "Swap Ladder Step Values");
                        SerializedObject so = new SerializedObject(ladder);
                        so.FindProperty(LadderAuthoring.P_StepDistance).floatValue = stepSpeed;
                        so.FindProperty(LadderAuthoring.P_StepSpeed).floatValue = stepDistance;
                        so.ApplyModifiedProperties();
                    });
            }
            else if (duration < MinSaneStepDuration)
            {
                Add(issues, MessageType.Info,
                    $"한 칸에 {duration:0.###}초로 매우 빠릅니다. 등반 애니 사이클보다 빨라 순간이동처럼 보일 수 있습니다.", ladder);
            }

            Vector3 climbOffset = LadderAuthoring.GetVector3(ladder, LadderAuthoring.P_ClimbOffset);
            if (Mathf.Abs(climbOffset.x) < 0.001f && Mathf.Abs(climbOffset.z) < 0.001f)
            {
                Add(issues, MessageType.Warning,
                    "Climb Offset의 X/Z가 0입니다. 캐릭터가 사다리 기둥 한가운데에 파묻혀 보입니다.", ladder);
            }

            if (height < 1f)
            {
                Add(issues, MessageType.Info,
                    $"높이가 {height:0.##}m로 매우 낮습니다. 상/하단 Zone이 겹쳐 진입 판정이 불안정할 수 있습니다.", ladder);
            }
        }

        private static void ValidatePlacement(LadderTraversable ladder, List<Issue> issues)
        {
            // 등반 축은 항상 월드 Y다(TopWorld = position + up * height). 기울이면 비주얼과 판정이 어긋난다.
            if (Vector3.Angle(ladder.transform.up, Vector3.up) > 1f)
            {
                Add(issues, MessageType.Warning,
                    "사다리가 기울어져 있습니다. 등반 축은 항상 월드 Y라서 비주얼과 실제 이동 경로가 어긋납니다.", ladder,
                    "기울기 제거", () =>
                    {
                        Undo.RecordObject(ladder.transform, "Level Ladder");
                        Vector3 e = ladder.transform.eulerAngles;
                        ladder.transform.rotation = Quaternion.Euler(0f, e.y, 0f);
                    });
            }

            Vector3 scale = ladder.transform.lossyScale;
            if (scale.y <= 0f)
            {
                Add(issues, MessageType.Error,
                    $"Y 스케일이 {scale.y:0.##}입니다. 사다리 높이가 0 이하로 계산됩니다.", ladder);
            }

            // 물리 질의는 콜라이더가 있는 씬에서만 의미가 있다. 재생 중이 아니어도 씬 콜라이더는 유효.
            if (!LadderAuthoring.HasFloorUnderTopExit(ladder))
            {
                Add(issues, MessageType.Warning,
                    "상단 탈출 지점 아래에 바닥이 없습니다. 다 올라간 직후 그대로 떨어집니다.", ladder,
                    "바닥에 맞추기", () => LadderAuthoring.SnapToGround(ladder));
            }
        }

        // ─────────── 애니메이터 계약 검증 (플레이어 공통) ───────────

        /// <summary>
        /// 사다리 등반은 컨트롤러 파라미터 + 등반 클립의 climbingUp/Down 애니 이벤트가 모두 있어야
        /// 성립한다. 파라미터가 없으면 애니가 안 바뀌고, 이벤트가 없으면 한 칸도 움직이지 않는다.
        /// </summary>
        public static List<Issue> ValidateAnimator(Animator animator)
        {
            List<Issue> issues = new List<Issue>();
            if (animator == null)
            {
                Add(issues, MessageType.Info, "검사할 Animator를 지정하세요.", null);
                return issues;
            }

            RuntimeAnimatorController runtime = animator.runtimeAnimatorController;
            if (runtime == null)
            {
                Add(issues, MessageType.Error, "Animator에 컨트롤러가 없습니다.", animator);
                return issues;
            }

            AnimatorController controller = ResolveController(runtime);
            if (controller == null)
            {
                Add(issues, MessageType.Info,
                    $"'{runtime.name}'의 기반 AnimatorController를 찾지 못해 파라미터 검사를 건너뜁니다.", animator);
            }
            else
            {
                ValidateParameters(controller, animator, issues);
            }

            ValidateClimbEvents(runtime, animator, issues);
            ValidateEventReceiver(animator, issues);

            return issues;
        }

        /// <summary>AnimatorOverrideController를 타고 내려가 기반 컨트롤러를 찾는다.</summary>
        public static AnimatorController ResolveController(RuntimeAnimatorController runtime)
        {
            int guard = 0;
            while (runtime is AnimatorOverrideController over && guard++ < 8)
                runtime = over.runtimeAnimatorController;
            return runtime as AnimatorController;
        }

        private static void ValidateParameters(AnimatorController controller, Animator context, List<Issue> issues)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;

            foreach ((string name, AnimatorControllerParameterType type) in LadderAuthoring.RequiredAnimParams)
            {
                AnimatorControllerParameter found = null;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].name == name) { found = parameters[i]; break; }
                }

                if (found == null)
                {
                    string captured = name;
                    AnimatorControllerParameterType capturedType = type;
                    Add(issues, MessageType.Error,
                        $"파라미터 '{name}'({type})가 없습니다. 해당 사다리 동작이 재생되지 않습니다.", controller,
                        "파라미터 추가", () =>
                        {
                            Undo.RecordObject(controller, "Add Ladder Parameter");
                            controller.AddParameter(captured, capturedType);
                            EditorUtility.SetDirty(controller);
                        });
                }
                else if (found.type != type)
                {
                    Add(issues, MessageType.Error,
                        $"파라미터 '{name}'의 타입이 {found.type}입니다(정상: {type}).", controller);
                }
            }
        }

        private static void ValidateClimbEvents(RuntimeAnimatorController runtime, Animator context, List<Issue> issues)
        {
            bool hasUp = false;
            bool hasDown = false;

            AnimationClip[] clips = runtime.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i];
                if (clip == null) continue;

                AnimationEvent[] events = AnimationUtility.GetAnimationEvents(clip);
                for (int e = 0; e < events.Length; e++)
                {
                    if (events[e].functionName == LadderAuthoring.ClimbUpEvent) hasUp = true;
                    else if (events[e].functionName == LadderAuthoring.ClimbDownEvent) hasDown = true;
                }

                if (hasUp && hasDown) break;
            }

            if (!hasUp || !hasDown)
            {
                string missing = !hasUp && !hasDown
                    ? $"'{LadderAuthoring.ClimbUpEvent}'와 '{LadderAuthoring.ClimbDownEvent}'"
                    : (!hasUp ? $"'{LadderAuthoring.ClimbUpEvent}'" : $"'{LadderAuthoring.ClimbDownEvent}'");

                // 한 칸 이동은 전적으로 이 이벤트가 시작한다. 없으면 사다리에 붙기만 하고 오르지 못한다.
                Add(issues, MessageType.Error,
                    $"등반 클립에 {missing} 애니메이션 이벤트가 없습니다. 사다리에 붙어도 한 칸도 오르지 못합니다.",
                    runtime);
            }
        }

        private static void ValidateEventReceiver(Animator animator, List<Issue> issues)
        {
            // 이벤트는 Animator가 붙은 GameObject에서 메서드를 찾는다.
            if (animator.GetComponent<AttackAnimationEventReceiver>() == null)
            {
                Add(issues, MessageType.Error,
                    $"'{animator.gameObject.name}'에 AttackAnimationEventReceiver가 없습니다. " +
                    "climbingUp/Down 이벤트를 받을 대상이 없어 등반이 동작하지 않습니다.", animator,
                    "컴포넌트 추가", () => Undo.AddComponent<AttackAnimationEventReceiver>(animator.gameObject));
            }
        }

        private static void Add(List<Issue> list, MessageType type, string message, UnityEngine.Object context,
                                string fixLabel = null, Action fix = null)
        {
            list.Add(new Issue
            {
                Type = type,
                Message = message,
                Context = context,
                FixLabel = fixLabel,
                Fix = fix
            });
        }
    }
}
