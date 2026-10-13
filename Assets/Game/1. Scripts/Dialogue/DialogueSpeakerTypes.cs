using PixelCrushers.DialogueSystem;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 대사를 어느 자막 패널로 띄울지 가르는 화자 구분.
    /// 값의 크기가 곧 **우선순위**다 — 한 액터의 표기가 줄마다 엇갈릴 때 큰 쪽으로 통일한다.
    /// </summary>
    public enum SpeakerKind
    {
        /// <summary>일반 NPC. 기본 NPC 패널을 쓴다.</summary>
        Npc = 0,

        /// <summary>아군(동료). 플레이어는 아니지만 전용 패널을 쓴다.</summary>
        Ally = 1,

        /// <summary>플레이어. Pixel Crushers의 IsPlayer가 켜지고 PC 패널을 쓴다.</summary>
        Player = 2
    }

    /// <summary>
    /// 시트의 <c>IsPlayer</c> 칸 하나로 NPC / 플레이어 / **아군**을 가르는 규칙.
    ///
    /// Pixel Crushers는 액터의 <c>IsPlayer</c> 불리언 하나로 NPC 패널과 PC 패널만 고른다.
    /// 아군은 "플레이어가 아니지만 NPC와도 다른 창"이라 그 두 값으로는 표현이 안 되므로,
    /// 임포터가 액터에 <see cref="FieldName"/>(<c>SpeakerType</c>) 커스텀 필드를 같이 적어두고
    /// 런타임에 <see cref="DialogueSpeakerPanels"/>가 그걸 읽어 패널을 갈아끼운다.
    ///
    /// 아군의 <c>IsPlayer</c>는 **false**로 둔다 — true로 하면 대화의 PC 역할과 선택지 주체까지
    /// 아군이 되어버린다. 즉 아군은 "NPC이지만 패널만 다른 화자"다.
    ///
    /// 시트에 적는 값(대소문자 무시):
    ///   - 플레이어: <c>Player</c>, <c>PC</c>, <c>TRUE</c>, <c>1</c>, <c>Y</c>, <c>플레이어</c>
    ///   - 아군: <c>Ally</c>, <c>Friend</c>, <c>Companion</c>, <c>2</c>, <c>아군</c>, <c>동료</c>
    ///   - NPC: 빈 칸, <c>FALSE</c>, <c>0</c>, <c>N</c>, <c>NPC</c> (그 외 알 수 없는 값도 NPC)
    /// </summary>
    public static class DialogueSpeakerTypes
    {
        /// <summary>액터에 적히는 커스텀 필드 이름.</summary>
        public const string FieldName = "SpeakerType";

        public const string NpcValue = "NPC";
        public const string PlayerValue = "Player";
        public const string AllyValue = "Ally";

        /// <summary>시트 칸 값을 화자 구분으로 바꾼다. 알 수 없는 값은 NPC로 본다.</summary>
        public static SpeakerKind Parse(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return SpeakerKind.Npc;
            }

            string trimmed = value.Trim();

            if (Matches(trimmed, "Player", "PC", "TRUE", "1", "Y", "플레이어", "주인공"))
            {
                return SpeakerKind.Player;
            }

            if (Matches(trimmed, "Ally", "Friend", "Companion", "2", "아군", "동료"))
            {
                return SpeakerKind.Ally;
            }

            return SpeakerKind.Npc;
        }

        /// <summary>액터 필드에 적을 문자열.</summary>
        public static string ToFieldValue(SpeakerKind kind)
        {
            switch (kind)
            {
                case SpeakerKind.Player: return PlayerValue;
                case SpeakerKind.Ally: return AllyValue;
                default: return NpcValue;
            }
        }

        /// <summary>
        /// 액터에 적힌 화자 구분을 읽는다.
        /// 필드가 없는(예전 임포트로 만든) 액터는 <c>IsPlayer</c>만 보고 NPC/플레이어로 판정한다.
        /// </summary>
        public static SpeakerKind GetKind(Actor actor)
        {
            if (actor == null)
            {
                return SpeakerKind.Npc;
            }

            string value = actor.LookupValue(FieldName);
            if (string.IsNullOrEmpty(value))
            {
                return actor.IsPlayer ? SpeakerKind.Player : SpeakerKind.Npc;
            }

            return Parse(value);
        }

        public static bool IsAlly(Actor actor)
        {
            return GetKind(actor) == SpeakerKind.Ally;
        }

        private static bool Matches(string value, params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (string.Equals(value, candidates[i], System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
