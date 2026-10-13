using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PixelCrushers.DialogueSystem;
using UnityEditor;
using UnityEngine;

namespace Aiara.Dialogue.EditorTools
{
    /// <summary>
    /// Assets/Data의 CSV 시트를 Pixel Crushers의 DialogueDatabase 애셋으로 변환한다.
    ///
    /// 시트가 원본(source of truth)이다. 이 임포터는 항상 DB를 통째로 다시 만들되
    /// **애셋 파일 자체는 재사용**한다 — GUID가 유지되어야 Dialogue Manager의
    /// Initial Database 참조와 씬/프리팹 연결이 끊기지 않기 때문이다.
    ///
    /// 매핑:
    ///   ActorData     → Actor      (ID=내부 이름, Name=Display Name, ProfileImage=초상화)
    ///   퀘스트 ID     → Item       (Is Item=False 이면 퀘스트로 취급된다)
    ///   EventData     → Conversation (ID=제목, ActiveQuest/ClearQuest=시작 조건)
    ///   EventNodeData → DialogueEntry (EventNode 순서대로 일렬 연결)
    ///   TextData      → DialogueEntry의 Dialogue Text
    ///
    /// <c>IsPlayer</c> 칸은 NPC / 플레이어 / **아군** 세 값을 받는다 (<see cref="DialogueSpeakerTypes"/>).
    /// 아군은 IsPlayer를 켜지 않고 액터에 <c>SpeakerType</c> 필드만 적어두며, 자막 패널 교체는
    /// 런타임의 <see cref="DialogueSpeakerPanels"/>가 맡는다.
    ///
    /// EventType 중 <c>Action</c> 노드는 대사가 아니라 연출 지시다. 키 입력 없이 자동으로 다음 노드로
    /// 넘어가고, AdditionalData1에 시간(초)이 적혀 있으면 그만큼 기다렸다가 넘어간다.
    ///
    /// EventType 중 <c>SelectDialog</c>만 일렬 연결에서 벗어난다. 그 노드는 대사를 말한 뒤 선택지를 띄우고,
    /// 고른 선택지에 따라 **다른 이벤트로 건너뛴다**(AdditionalData1=선택지 텍스트 ID, AdditionalData2=이동할
    /// 이벤트 ID, 둘 다 콤마 구분에 같은 순서). 그래서 SelectDialog가 나오면 그 이벤트는 거기서 끝난다.
    /// </summary>
    public static class DialogueCsvImporter
    {
        private const string CsvFolder = "Assets/Data";
        private const string DatabaseAssetPath = "Assets/Data/YeolhaDialogueDatabase.asset";
        private const string ProfileFolder = "Assets/Resources/Profiles";

        /// <summary>ActorData에 IsPlayer 열이 없을 때 플레이어로 볼 액터 ID.</summary>
        private const string FallbackPlayerActorId = "JangHyu";

        /// <summary>
        /// 한 칸에 값을 여러 개 넣을 때의 구분자.
        ///
        /// 콤마도 받는다 — 선택지(SelectDialog)의 AdditionalData1/2가 콤마로 나열되기 때문이다.
        /// CSV에서 콤마는 칸 구분자지만, 콤마가 든 칸은 큰따옴표로 감싸여 들어오므로
        /// (<see cref="CsvTable"/>가 RFC4180 규칙을 지킨다) 값 안의 콤마는 그대로 살아 있다.
        /// </summary>
        private static readonly char[] MultiValueSeparators = { '|', ';', ',' };

        /// <summary>
        /// 대화의 첫 실제 노드 ID. 0번은 비어 있는 START 노드라 실제 내용은 1번부터 시작한다.
        /// 선택지가 다른 이벤트로 건너뛸 때 이 번호를 목적지로 쓴다.
        /// </summary>
        private const int FirstEntryId = 1;

        /// <summary>
        /// Action 노드의 시퀀스.
        ///
        /// 액션을 걸고 <b>키 입력 없이</b> 다음 노드로 넘어간다 — 연출 지시는 대사가 아니라서
        /// 플레이어가 넘길 것이 없다. AdditionalData1에 시간이 적혀 있으면 그만큼 기다렸다가 넘어간다
        /// (<c>Continue()@2.5</c> = 2.5초 뒤 진행). 그동안 앞 대사의 자막은 화면에 남아 있다.
        /// </summary>
        private const string ActionSequenceFormat = "AiaraAction({0}); Continue()";

        /// <summary>딜레이가 있는 Action 노드의 시퀀스. {1} = 초.</summary>
        private const string ActionSequenceDelayedFormat = "AiaraAction({0}); Continue()@{1}";

        /// <summary>
        /// 선택지 노드의 EventType.
        ///
        /// 이 노드 자체는 평범한 대사(Data의 텍스트를 말한다)이고, 그 뒤에 선택지가 붙는다:
        ///   AdditionalData1 = 선택지로 보여줄 텍스트 ID 목록 (콤마 구분)
        ///   AdditionalData2 = 그 선택지를 고르면 넘어갈 이벤트 ID 목록 (콤마 구분, 같은 순서)
        /// </summary>
        private const string SelectDialogType = "SelectDialog";

        /// <summary>
        /// 말풍선 모양(NodeType) → 자막 패널 번호. (NPC = 화면 상단, PC = 화면 하단)
        ///
        /// **비어 있으면 SetPanel을 아예 내보내지 않고 PC 기본 동작에 맡긴다** —
        /// 액터의 IsPlayer로 NPC 패널(상단)/PC 패널(하단)이 자동으로 갈린다.
        /// Normal 하나뿐인 지금은 그걸로 충분하다.
        ///
        /// 강조/독백 말풍선이 생기면 여기에 한 줄씩 추가하고, UI 프리팹에 같은 번호의
        /// 자막 패널을 만들어 두면 된다. 한 줄이라도 등록되는 순간부터는 **모든 Dialog 노드에**
        /// SetPanel이 붙는다 — SetPanel은 한 번 부르면 그 액터에게 계속 남아서,
        /// 명시하지 않으면 앞 대사의 말풍선이 다음 대사까지 따라오기 때문이다.
        /// 등록되지 않은 NodeType은 default(기본 패널)로 되돌린다.
        /// </summary>
        private static readonly Dictionary<string, (int Npc, int Pc)> NodeTypePanels =
            new Dictionary<string, (int, int)>
            {
                // 예시 — 실제 패널을 만든 뒤 주석을 풀 것:
                // { "강조", (2, 3) },
                // { "독백", (4, 5) },
            };

        [MenuItem("Tools/Aiara/대화 데이터 임포트 (CSV → DialogueDatabase)")]
        public static void Import()
        {
            var log = new ImportLog();

            CsvTable textTable = LoadCsv("TextData.csv", log);
            CsvTable actorTable = LoadCsv("ActorData.csv", log);
            CsvTable eventTable = LoadCsv("EventData.csv", log);
            CsvTable nodeTable = LoadCsv("EventNodeData.csv", log);

            if (textTable == null || actorTable == null || eventTable == null || nodeTable == null)
            {
                log.Finish("대화 데이터 임포트 실패", false);
                return;
            }

            DialogueDatabase database = LoadOrCreateDatabase();
            ResetDatabase(database);

            var context = new BuildContext(database, log);

            BuildTexts(textTable, context);
            BuildActors(actorTable, CollectSpeakerKinds(nodeTable, log), context);
            BuildQuests(eventTable, nodeTable, context);
            BuildConversations(eventTable, nodeTable, context);

            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(database);

            log.Summary =
                $"액터 {database.actors.Count} / 퀘스트 {database.items.Count} / " +
                $"이벤트(대화) {database.conversations.Count} / 텍스트 {context.Texts.Count}";
            log.Finish("대화 데이터 임포트 완료", true);
        }

        #region CSV 읽기

        private static CsvTable LoadCsv(string fileName, ImportLog log)
        {
            string path = Path.Combine(CsvFolder, fileName);

            if (!File.Exists(path))
            {
                log.Error($"CSV를 찾을 수 없습니다: {path}");
                return null;
            }

            return CsvTable.Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        #endregion

        #region 데이터베이스 준비

        private static DialogueDatabase LoadOrCreateDatabase()
        {
            var database = AssetDatabase.LoadAssetAtPath<DialogueDatabase>(DatabaseAssetPath);
            if (database != null)
            {
                return database;
            }

            string directory = Path.GetDirectoryName(DatabaseAssetPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            database = ScriptableObject.CreateInstance<DialogueDatabase>();
            AssetDatabase.CreateAsset(database, DatabaseAssetPath);
            return database;
        }

        private static void ResetDatabase(DialogueDatabase database)
        {
            database.actors.Clear();
            database.items.Clear();
            database.locations.Clear();
            database.variables.Clear();
            database.conversations.Clear();

            database.author = "Aiara";
            database.description = "Assets/Data의 CSV에서 자동 생성됨. 직접 편집하지 말 것 — 시트를 고치고 다시 임포트할 것.";
        }

        #endregion

        #region 텍스트 / 액터 / 퀘스트

        private static void BuildTexts(CsvTable table, BuildContext context)
        {
            foreach (string[] row in table.Rows)
            {
                string id = table.Get(row, "ID");
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                if (context.Texts.ContainsKey(id))
                {
                    context.Log.Warning($"TextData에 중복된 ID가 있습니다: {id}");
                    continue;
                }

                context.Texts[id] = table.Get(row, "Text");
            }
        }

        /// <summary>
        /// EventNodeData의 대사 단위 IsPlayer 표기를 액터 단위로 접는다.
        ///
        /// 자막 패널은 대사가 아니라 **액터** 단위로 정해지기 때문에(PC는 IsPlayer로, 아군은
        /// 임포터가 액터에 적어두는 SpeakerType으로 - <see cref="DialogueSpeakerTypes"/> 참고),
        /// 줄마다 다르게 표기하면 그대로 옮길 수 없다. 지금은 한 액터가 항상 같게 표기돼 있어
        /// 손실 없이 접히지만, 나중에 엇갈리면 경고를 띄워 알린다.
        /// (그때는 줄마다 SetPanel() 시퀀서 커맨드를 넣는 방식으로 바꿔야 한다.)
        ///
        /// 엇갈릴 때는 더 구체적인 쪽(플레이어 &gt; 아군 &gt; NPC)으로 통일한다.
        /// </summary>
        private static Dictionary<string, SpeakerKind> CollectSpeakerKinds(CsvTable nodeTable, ImportLog log)
        {
            var marks = new Dictionary<string, SpeakerKind>();

            if (!nodeTable.HasColumn("IsPlayer"))
            {
                return marks;
            }

            foreach (string[] row in nodeTable.Rows)
            {
                if (!IsSpokenNode(nodeTable.Get(row, "EventType")))
                {
                    continue;
                }

                string actorKey = nodeTable.Get(row, "Actor");
                if (string.IsNullOrEmpty(actorKey))
                {
                    continue;
                }

                SpeakerKind kind = DialogueSpeakerTypes.Parse(nodeTable.Get(row, "IsPlayer"));

                if (marks.TryGetValue(actorKey, out SpeakerKind previous))
                {
                    if (previous != kind)
                    {
                        SpeakerKind winner = previous > kind ? previous : kind;

                        log.Warning($"'{actorKey}'의 IsPlayer 표기가 줄마다 엇갈립니다 " +
                                    $"({previous} / {kind}). 패널은 액터 단위로만 고를 수 있어 " +
                                    $"'{DialogueSpeakerTypes.ToFieldValue(winner)}'로 통일해 처리합니다.");

                        marks[actorKey] = winner;
                    }

                    continue;
                }

                marks[actorKey] = kind;
            }

            return marks;
        }

        private static void BuildActors(CsvTable table, Dictionary<string, SpeakerKind> speakerKinds, BuildContext context)
        {
            bool hasIsPlayerColumn = table.HasColumn("IsPlayer");
            int nextId = 1;

            // 시트에서 뽑으면 A1(액터 ID 열의 헤더)이 비어 있는 경우가 흔하다. 그때는 첫 열을 ID로 본다.
            // 이걸 안 받아주면 모든 행이 ID 없음으로 걸러져 액터가 통째로 0개가 된다.
            bool hasIdColumn = table.HasColumn("ID");
            if (!hasIdColumn)
            {
                context.Log.Warning("ActorData에 'ID' 열이 없어 첫 번째 열을 액터 ID로 씁니다. " +
                                    "시트 A1에 'ID'를 넣어두면 더 확실합니다.");
            }

            foreach (string[] row in table.Rows)
            {
                string id = hasIdColumn ? table.Get(row, "ID") : table.GetAt(row, 0);
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                if (context.ActorIds.ContainsKey(id))
                {
                    context.Log.Warning($"ActorData에 중복된 ID가 있습니다: {id}");
                    continue;
                }

                // 액터 시트에 직접 적힌 값이 가장 우선이고,
                // 없으면 EventNodeData의 대사 단위 표기를 접어서 쓰고,
                // 그것도 없으면 마지막으로 이름으로 때려맞춘다.
                SpeakerKind kind;
                if (hasIsPlayerColumn)
                {
                    kind = DialogueSpeakerTypes.Parse(table.Get(row, "IsPlayer"));
                }
                else if (speakerKinds.TryGetValue(id, out SpeakerKind markedKind))
                {
                    kind = markedKind;
                }
                else
                {
                    kind = string.Equals(id, FallbackPlayerActorId) ? SpeakerKind.Player : SpeakerKind.Npc;
                }

                // 아군은 IsPlayer를 켜지 않는다 — 켜면 대화의 PC 역할(선택지 주체·PC 초상화)까지
                // 아군이 되어버린다. 아군은 "NPC이지만 자막 패널만 다른 화자"로 다루고,
                // 패널 교체는 런타임에 DialogueSpeakerPanels가 SpeakerType 필드를 보고 한다.
                bool isPlayer = kind == SpeakerKind.Player;

                var actor = new Actor { id = nextId, fields = new List<Field>() };
                Field.SetValue(actor.fields, "Name", id);
                Field.SetValue(actor.fields, "Display Name", table.Get(row, "Name"));
                Field.SetValue(actor.fields, "IsPlayer", isPlayer);
                Field.SetValue(actor.fields, DialogueSpeakerTypes.FieldName, DialogueSpeakerTypes.ToFieldValue(kind));
                Field.SetValue(actor.fields, "Description", string.Empty);

                AssignPortrait(actor, table.Get(row, "ProfileImage"), context.Log);

                context.Database.actors.Add(actor);
                context.ActorIds[id] = nextId;
                context.ActorKeys[nextId] = id;

                if (kind == SpeakerKind.Ally)
                {
                    context.AllyActorIds.Add(nextId);
                }

                if (isPlayer && context.PlayerActorId == 0)
                {
                    context.PlayerActorId = nextId;
                }

                nextId++;
            }

            // 액터가 0개면 대사는 만들어져도 이름·초상화가 전부 비고 PC/NPC 패널 구분도 죽는다.
            // 조용히 넘어가면 원인 찾기가 어려우므로 오류로 띄운다.
            if (context.Database.actors.Count == 0)
            {
                context.Log.Error("ActorData에서 액터를 하나도 읽지 못했습니다. " +
                                  "첫 열(액터 ID)이 비어 있지 않은지, 헤더 줄이 맞는지 확인하세요.");
            }

            if (context.PlayerActorId == 0)
            {
                context.Log.Warning(
                    "플레이어로 표시된 액터가 없습니다 (ActorData의 IsPlayer 열, EventNodeData의 IsPlayer 표기, " +
                    $"'{FallbackPlayerActorId}' 이름 순으로 찾습니다). 첫 액터를 플레이어로 씁니다.");

                context.PlayerActorId = context.Database.actors.Count > 0 ? context.Database.actors[0].id : 0;
            }
        }

        private static void AssignPortrait(Actor actor, string profileImage, ImportLog log)
        {
            if (string.IsNullOrEmpty(profileImage))
            {
                return;
            }

            if (!Directory.Exists(ProfileFolder))
            {
                log.Warning($"프로필 폴더가 없습니다: {ProfileFolder} (초상화는 비워둡니다)");
                return;
            }

            string[] guids = AssetDatabase.FindAssets($"{profileImage} t:Texture2D", new[] { ProfileFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // FindAssets는 부분 일치도 잡아내므로 파일명이 정확히 같은 것만 쓴다.
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), profileImage))
                {
                    continue;
                }

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null)
                {
                    continue;
                }

                actor.portrait = texture;
                actor.textureName = profileImage;
                return;
            }

            log.Warning($"프로필 이미지를 찾지 못했습니다: {ProfileFolder}/{profileImage}");
        }

        /// <summary>
        /// 시트 어디에도 퀘스트 목록 자체는 없으므로, 조건/노드에서 언급된 퀘스트 ID를 모아
        /// Item으로 만든다. Is Item=False가 퀘스트라는 표시다.
        /// </summary>
        private static void BuildQuests(CsvTable eventTable, CsvTable nodeTable, BuildContext context)
        {
            var questIds = new List<string>();

            void Collect(string raw)
            {
                foreach (string value in SplitMultiValue(raw))
                {
                    if (!questIds.Contains(value))
                    {
                        questIds.Add(value);
                    }
                }
            }

            foreach (string[] row in eventTable.Rows)
            {
                Collect(eventTable.Get(row, "ActiveQuest"));
                Collect(eventTable.Get(row, "ClearQuest"));
            }

            foreach (string[] row in nodeTable.Rows)
            {
                string type = nodeTable.Get(row, "EventType");
                if (IsQuestNode(type))
                {
                    Collect(nodeTable.Get(row, "Data"));
                }
            }

            int nextId = 1;
            foreach (string questId in questIds)
            {
                var item = new Item { id = nextId, fields = new List<Field>() };
                Field.SetValue(item.fields, "Name", questId);
                Field.SetValue(item.fields, "Display Name", questId);
                Field.SetValue(item.fields, "Description", string.Empty);
                Field.SetValue(item.fields, "Success Description", string.Empty);
                Field.SetValue(item.fields, "Failure Description", string.Empty);

                // 퀘스트로 인식시키는 핵심 필드. 이게 True면 인벤토리 아이템이 된다.
                Field.SetValue(item.fields, "Is Item", "False");

                // 시작 상태. 최초 퀘스트를 여는 방법은 QuestInitializer를 볼 것.
                Field.SetValue(item.fields, "State", "unassigned");

                context.Database.items.Add(item);
                context.QuestIds.Add(questId);
                nextId++;
            }
        }

        #endregion

        #region 이벤트 → 대화

        private static void BuildConversations(CsvTable eventTable, CsvTable nodeTable, BuildContext context)
        {
            Dictionary<string, List<string[]>> nodesByEvent = GroupNodesByEvent(nodeTable, context);

            var planned = new List<PlannedConversation>();
            var builtEventIds = new HashSet<string>();

            foreach (string[] row in eventTable.Rows)
            {
                string eventId = eventTable.Get(row, "ID");
                if (string.IsNullOrEmpty(eventId) || !builtEventIds.Add(eventId))
                {
                    continue;
                }

                nodesByEvent.TryGetValue(eventId, out List<string[]> nodes);
                if (nodes == null || nodes.Count == 0)
                {
                    context.Log.Warning($"EventData에 있지만 EventNodeData에 노드가 없습니다: {eventId}");
                    continue;
                }

                planned.Add(new PlannedConversation(eventId, BuildConditions(eventTable, row), nodes));
            }

            // 조건 없이 노드만 있는 이벤트도 대화로 만들어 둔다 (EventZone에서 바로 부를 수 있게).
            foreach (KeyValuePair<string, List<string[]>> pair in nodesByEvent)
            {
                if (builtEventIds.Contains(pair.Key))
                {
                    continue;
                }

                context.Log.Warning($"EventNodeData에만 있고 EventData에 조건이 없습니다: {pair.Key} (조건 없이 생성)");
                planned.Add(new PlannedConversation(pair.Key, string.Empty, pair.Value));
            }

            // 선택지(SelectDialog)는 아직 안 만들어진 이벤트로도 건너뛸 수 있어야 하므로,
            // 내용을 만들기 전에 대화 ID를 전부 먼저 정해둔다.
            for (int i = 0; i < planned.Count; i++)
            {
                context.ConversationIds[planned[i].EventId] = i + 1;
            }

            for (int i = 0; i < planned.Count; i++)
            {
                PlannedConversation plan = planned[i];
                BuildConversation(plan.EventId, plan.Conditions, plan.Nodes, nodeTable,
                    context.ConversationIds[plan.EventId], context);
            }

            VerifyCrossConversationLinks(context);
        }

        /// <summary>
        /// 선택지가 만들어 놓은 **다른 대화로 가는 링크**가 실제로 도착 가능한지 확인한다.
        ///
        /// 링크를 걸 때는 목적지가 아직 안 만들어져 있어서 검사할 수 없다. 그리고 노드가 하나도
        /// 실행 가능하지 않은 이벤트는 ID만 잡아두고 대화가 생성되지 않으므로, 링크가 허공을 가리킬 수 있다.
        /// 런타임에는 그냥 대화가 끊길 뿐이라 원인을 찾기 어려우니 임포트 때 잡는다.
        /// </summary>
        private static void VerifyCrossConversationLinks(BuildContext context)
        {
            var entriesByConversation = new Dictionary<int, Dictionary<int, DialogueEntry>>();

            foreach (Conversation conversation in context.Database.conversations)
            {
                var entries = new Dictionary<int, DialogueEntry>();
                foreach (DialogueEntry entry in conversation.dialogueEntries)
                {
                    entries[entry.id] = entry;
                }

                entriesByConversation[conversation.id] = entries;
            }

            foreach (Conversation conversation in context.Database.conversations)
            {
                foreach (DialogueEntry entry in conversation.dialogueEntries)
                {
                    foreach (Link link in entry.outgoingLinks)
                    {
                        if (link.destinationConversationID == conversation.id)
                        {
                            continue;
                        }

                        string where = $"{conversation.Title} / {Field.LookupValue(entry.fields, "Title")}";

                        if (!entriesByConversation.TryGetValue(link.destinationConversationID,
                                out Dictionary<int, DialogueEntry> entries)
                            || !entries.TryGetValue(link.destinationDialogueID, out DialogueEntry destination))
                        {
                            context.Log.Warning($"선택지가 가리키는 이벤트에 도착할 수 없습니다: {where} " +
                                                "(대상 이벤트에 실행 가능한 노드가 없습니다)");
                            continue;
                        }

                        // 대상 이벤트에 시작 조건이 걸려 있으면, 선택지를 골라도 조건이 안 맞으면
                        // 대화가 그냥 끝나버린다. 화면상 아무 일도 안 일어나 원인을 찾기 어렵다.
                        if (!string.IsNullOrEmpty(destination.conditionsString))
                        {
                            context.Log.Warning($"선택지가 조건이 걸린 이벤트를 가리킵니다: {where} " +
                                                "(조건이 안 맞으면 선택해도 대화가 그대로 끝납니다)");
                        }
                    }
                }
            }
        }

        /// <summary>ID를 먼저 배분하기 위해 만들기 전에 모아두는 대화 한 건.</summary>
        private readonly struct PlannedConversation
        {
            public PlannedConversation(string eventId, string conditions, List<string[]> nodes)
            {
                EventId = eventId;
                Conditions = conditions;
                Nodes = nodes;
            }

            public string EventId { get; }
            public string Conditions { get; }
            public List<string[]> Nodes { get; }
        }

        private static Dictionary<string, List<string[]>> GroupNodesByEvent(CsvTable nodeTable, BuildContext context)
        {
            var grouped = new Dictionary<string, List<string[]>>();

            foreach (string[] row in nodeTable.Rows)
            {
                string eventId = nodeTable.Get(row, "ID");
                if (string.IsNullOrEmpty(eventId))
                {
                    continue;
                }

                if (!grouped.TryGetValue(eventId, out List<string[]> list))
                {
                    list = new List<string[]>();
                    grouped[eventId] = list;
                }

                list.Add(row);
            }

            // 시트에서 행 순서가 뒤바뀌어도 EventNode 번호대로 실행되도록 정렬한다.
            foreach (List<string[]> list in grouped.Values)
            {
                list.Sort((a, b) => ParseNodeOrder(nodeTable.Get(a, "EventNode"))
                    .CompareTo(ParseNodeOrder(nodeTable.Get(b, "EventNode"))));
            }

            return grouped;
        }

        private static void BuildConversation(string eventId, string conditions, List<string[]> nodes,
            CsvTable nodeTable, int conversationId, BuildContext context)
        {
            int conversantActorId = ResolveConversant(nodes, nodeTable, context);

            var conversation = new Conversation { id = conversationId, fields = new List<Field>() };
            Field.SetValue(conversation.fields, "Title", eventId);
            Field.SetValue(conversation.fields, "Description", string.Empty);
            Field.SetValue(conversation.fields, "Actor", context.PlayerActorId);
            Field.SetValue(conversation.fields, "Conversant", conversantActorId);

            // 0번은 PC 관례상 비어 있는 START 노드다. None()으로 기본 시퀀스(카메라 이동)를 막는다.
            var start = NewEntry(conversationId, 0, "START", context.PlayerActorId, conversantActorId);
            Field.SetValue(start.fields, "Sequence", "None()");
            start.isRoot = true;
            conversation.dialogueEntries.Add(start);

            DialogueEntry previous = start;
            int entryId = FirstEntryId;

            for (int i = 0; i < nodes.Count; i++)
            {
                string[] row = nodes[i];

                DialogueEntry entry = BuildEntry(eventId, row, nodeTable, conversationId, entryId,
                    conversantActorId, context);
                if (entry == null)
                {
                    continue;
                }

                // 조건은 첫 실제 노드에만 건다 — 여기서 막히면 대화 자체가 시작되지 않는다.
                if (previous == start && !string.IsNullOrEmpty(conditions))
                {
                    entry.conditionsString = conditions;
                }

                conversation.dialogueEntries.Add(entry);
                previous.outgoingLinks.Add(new Link(conversationId, previous.id, conversationId, entry.id));
                previous = entry;
                entryId++;

                if (!string.Equals(nodeTable.Get(row, "EventType"), SelectDialogType))
                {
                    continue;
                }

                // 선택지는 이 노드의 자식으로 달리고, 고르면 각자 다른 이벤트로 건너뛴다.
                // 그래서 일렬 연결은 여기서 끝난다 — 뒤에 노드가 더 있으면 영영 실행되지 않는다.
                entryId = BuildChoiceEntries(eventId, row, nodeTable, conversation, entry, entryId,
                    conversantActorId, context);

                if (i < nodes.Count - 1)
                {
                    context.Log.Warning($"SelectDialog 뒤에 노드가 더 있지만 실행되지 않습니다: {eventId} " +
                                        "(선택지에서 각자 다른 이벤트로 넘어가므로 이 이벤트는 여기서 끝납니다)");
                }

                break;
            }

            if (previous == start)
            {
                context.Log.Warning($"실행 가능한 노드가 하나도 없습니다: {eventId}");
                return;
            }

            context.Database.conversations.Add(conversation);
        }

        private static DialogueEntry BuildEntry(string eventId, string[] row, CsvTable nodeTable,
            int conversationId, int entryId, int conversantActorId, BuildContext context)
        {
            string type = nodeTable.Get(row, "EventType");
            string data = nodeTable.Get(row, "Data");
            string nodeOrder = nodeTable.Get(row, "EventNode");
            string where = $"{eventId} / 노드 {nodeOrder}";

            // SelectDialog도 노드 자신은 평범한 대사다. 선택지는 이 대사의 자식으로 따로 붙는다.
            if (string.Equals(type, "Dialog") || string.Equals(type, SelectDialogType))
            {
                int actorId = ResolveActor(nodeTable.Get(row, "Actor"), where, context);
                int listenerId = actorId == context.PlayerActorId ? conversantActorId : context.PlayerActorId;

                DialogueEntry entry = NewEntry(conversationId, entryId, data, actorId, listenerId);

                if (!context.Texts.TryGetValue(data, out string text))
                {
                    context.Log.Warning($"TextData에 없는 텍스트 ID입니다: {data} ({where})");
                    text = string.Empty;
                }

                Field.SetValue(entry.fields, "Dialogue Text", text);

                // 말풍선 모양(Normal/강조/독백...). PC 기본 기능이 아니라 우리가 붙인 커스텀 필드다.
                // 지금은 값만 실어두고 아무도 읽지 않는다 — 렌더링 방식이 정해지면
                // 패널 분리(SetPanel)든 스킨 교체든 여기서 값을 꺼내 쓰면 된다.
                string nodeType = nodeTable.Get(row, "NodeType");
                if (!string.IsNullOrEmpty(nodeType))
                {
                    Field.SetValue(entry.fields, "NodeType", nodeType);
                }

                string panelSequence = BuildPanelSequence(nodeType, actorId, where, context);
                if (!string.IsNullOrEmpty(panelSequence))
                {
                    Field.SetValue(entry.fields, "Sequence", panelSequence);
                }

                return entry;
            }

            if (IsQuestNode(type))
            {
                DialogueEntry entry = NewEntry(conversationId, entryId, $"{type}:{data}",
                    context.PlayerActorId, conversantActorId);

                string state = string.Equals(type, "ClearQuest") ? "success" : "active";
                var script = new StringBuilder();

                foreach (string questId in SplitMultiValue(data))
                {
                    if (!context.QuestIds.Contains(questId))
                    {
                        context.Log.Warning($"알 수 없는 퀘스트 ID입니다: {questId} ({where})");
                    }

                    if (script.Length > 0)
                    {
                        script.Append("; ");
                    }

                    // 테이블을 직접 건드리지 않고 QuestLog를 거친다 — 그래야 퀘스트 트래커와
                    // 인디케이터 갱신, 상태 변경 이벤트가 같이 따라온다.
                    script.Append($"SetQuestState(\"{questId}\", \"{state}\")");
                }

                entry.userScript = script.ToString();

                // 대사가 없는 노드라 곧바로 다음으로 넘긴다.
                Field.SetValue(entry.fields, "Sequence", "Continue()");
                return entry;
            }

            if (string.Equals(type, "Action"))
            {
                DialogueEntry entry = NewEntry(conversationId, entryId, $"Action:{data}",
                    context.PlayerActorId, conversantActorId);

                // 무엇을 할지는 런타임의 DialogueActions 표가 정한다(PlayerTalk1/PlayerTalk2 …).
                // 여기서는 액션 ID를 실어 보내고, 넘어가는 타이밍만 정해준다.
                string delay = ParseActionDelay(nodeTable.Get(row, "AdditionalData1"), where, context);

                Field.SetValue(entry.fields, "Sequence", string.IsNullOrEmpty(delay)
                    ? string.Format(ActionSequenceFormat, data)
                    : string.Format(ActionSequenceDelayedFormat, data, delay));

                return entry;
            }

            context.Log.Warning($"모르는 EventType이라 건너뜁니다: '{type}' ({where})");
            return null;
        }

        /// <summary>
        /// Action 노드의 대기 시간(AdditionalData1)을 읽는다. 비었거나 0 이하면 빈 문자열 —
        /// 그때는 기다리지 않고 곧바로 다음 노드로 넘어간다.
        ///
        /// 시트가 어느 나라 로케일에서 저장됐든 같게 읽히도록 <see cref="CultureInfo.InvariantCulture"/>로
        /// 파싱하고, 시퀀스에 적을 때도 같은 형식으로 되돌린다 — 한국어 윈도우에서 "2,5"로 찍히면
        /// 시퀀서가 인자를 두 개로 본다.
        /// </summary>
        private static string ParseActionDelay(string raw, string where, BuildContext context)
        {
            if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(raw.Trim()))
            {
                return string.Empty;
            }

            if (!float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
            {
                context.Log.Warning($"Action 노드의 AdditionalData1을 시간(초)으로 읽을 수 없습니다: " +
                                    $"'{raw}' — 기다리지 않고 넘어갑니다 ({where})");
                return string.Empty;
            }

            if (seconds <= 0f)
            {
                return string.Empty;
            }

            return seconds.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// SelectDialog 노드에 선택지를 달아준다.
        ///
        /// PC(Pixel Crushers)에서 선택지는 별도 개념이 아니라 **플레이어가 화자인 자식 노드들**이다.
        /// 부모 대사가 끝나면 자식 중 플레이어 것들이 모여 응답 메뉴로 뜨고, 고른 것이 다음 노드가 된다.
        /// 그래서 선택지 하나당 노드를 하나씩 만들고, 그 노드의 링크를 **다른 대화의 첫 노드**로 건다.
        /// (대화를 가로지르는 링크는 PC가 기본으로 지원한다.)
        ///
        /// 고른 선택지의 텍스트를 플레이어 대사로 한 번 더 띄울지는 Dialogue Manager의
        /// Subtitle Settings → <c>Skip PC Subtitle After Response Menu</c>로 정한다.
        /// 여기서는 Menu Text(메뉴에 뜨는 글)와 Dialogue Text(고른 뒤 대사)를 둘 다 채워두어
        /// 어느 쪽으로 켜든 글자가 비지 않게 한다.
        /// </summary>
        /// <returns>선택지를 만들고 난 다음 노드 ID</returns>
        private static int BuildChoiceEntries(string eventId, string[] row, CsvTable nodeTable,
            Conversation conversation, DialogueEntry parent, int entryId, int conversantActorId, BuildContext context)
        {
            string where = $"{eventId} / 노드 {nodeTable.Get(row, "EventNode")}";

            List<string> textIds = SplitToList(nodeTable.Get(row, "AdditionalData1"));
            List<string> targetEventIds = SplitToList(nodeTable.Get(row, "AdditionalData2"));

            if (textIds.Count == 0)
            {
                context.Log.Warning($"SelectDialog인데 AdditionalData1에 선택지 텍스트가 없습니다 ({where})");
                return entryId;
            }

            int count = textIds.Count;

            if (textIds.Count != targetEventIds.Count)
            {
                context.Log.Warning($"선택지 개수가 맞지 않습니다 ({where}): " +
                                    $"텍스트 {textIds.Count}개 / 이동할 이벤트 {targetEventIds.Count}개. " +
                                    "적은 쪽에 맞춰 만듭니다.");

                count = System.Math.Min(textIds.Count, targetEventIds.Count);
            }

            for (int i = 0; i < count; i++)
            {
                string textId = textIds[i];
                string targetEventId = targetEventIds[i];

                DialogueEntry choice = NewEntry(conversation.id, entryId, $"Select:{textId}",
                    context.PlayerActorId, conversantActorId);

                if (!context.Texts.TryGetValue(textId, out string text))
                {
                    context.Log.Warning($"TextData에 없는 선택지 텍스트 ID입니다: {textId} ({where})");
                    text = string.Empty;
                }

                Field.SetValue(choice.fields, "Menu Text", text);
                Field.SetValue(choice.fields, "Dialogue Text", text);

                parent.outgoingLinks.Add(new Link(conversation.id, parent.id, conversation.id, choice.id));

                if (context.ConversationIds.TryGetValue(targetEventId, out int targetConversationId))
                {
                    choice.outgoingLinks.Add(new Link(conversation.id, choice.id, targetConversationId, FirstEntryId));
                }
                else
                {
                    context.Log.Warning($"선택지가 가리키는 이벤트를 찾을 수 없습니다: {targetEventId} ({where})");
                }

                conversation.dialogueEntries.Add(choice);
                entryId++;
            }

            return entryId;
        }

        private static List<string> SplitToList(string raw)
        {
            var values = new List<string>();

            foreach (string value in SplitMultiValue(raw))
            {
                values.Add(value);
            }

            return values;
        }

        private static DialogueEntry NewEntry(int conversationId, int entryId, string title, int actorId, int conversantId)
        {
            var entry = new DialogueEntry
            {
                id = entryId,
                conversationID = conversationId,
                fields = new List<Field>(),
                outgoingLinks = new List<Link>(),
            };

            Field.SetValue(entry.fields, "Title", title);
            Field.SetValue(entry.fields, "Dialogue Text", string.Empty);
            Field.SetValue(entry.fields, "Menu Text", string.Empty);
            Field.SetValue(entry.fields, "Sequence", string.Empty);
            Field.SetValue(entry.fields, "Actor", actorId);
            Field.SetValue(entry.fields, "Conversant", conversantId);

            return entry;
        }

        /// <summary>
        /// 말풍선 모양에 맞는 자막 패널로 바꾸는 시퀀스를 만든다.
        /// <see cref="NodeTypePanels"/>가 비어 있으면 빈 문자열을 돌려주고, 그러면 이 대사는
        /// Sequence 없이 PC 기본 동작(IsPlayer로 상단/하단 패널 선택)을 그대로 탄다.
        /// </summary>
        private static string BuildPanelSequence(string nodeType, int actorId, string where, BuildContext context)
        {
            if (NodeTypePanels.Count == 0)
            {
                return string.Empty;
            }

            string panel = "default";

            if (!string.IsNullOrEmpty(nodeType))
            {
                if (NodeTypePanels.TryGetValue(nodeType, out (int Npc, int Pc) panels))
                {
                    panel = (actorId == context.PlayerActorId ? panels.Pc : panels.Npc).ToString();
                }
                else if (!string.Equals(nodeType, "Normal"))
                {
                    context.Log.Warning($"패널이 정해지지 않은 NodeType입니다: '{nodeType}' ({where}). 기본 말풍선으로 표시됩니다.");
                }
            }

            string actorKey = context.ActorKeys.TryGetValue(actorId, out string key) ? key : SequencerKeywords.Speaker;

            // {{default}}가 없으면 기본 시퀀스(대사 길이만큼 대기)가 통째로 사라져 대사가 즉시 넘어간다.
            return $"SetPanel({actorKey}, {panel}); {SequencerKeywords.DefaultSequence}";
        }

        /// <summary>
        /// 대화 상대는 이 이벤트에서 말하는 플레이어 아닌 첫 액터로 잡는다.
        ///
        /// 단 **아군은 뒤로 미룬다** — 아군이 먼저 말하는 이벤트에서 아군이 대화 상대가 되면
        /// 초상화·카메라 기준이 진짜 상대(NPC)가 아니라 동료 쪽으로 잡힌다.
        /// 말하는 액터가 아군뿐이면 그때는 아군을 상대로 쓴다(동료끼리 하는 대화).
        /// </summary>
        private static int ResolveConversant(List<string[]> nodes, CsvTable nodeTable, BuildContext context)
        {
            int allyFallback = 0;

            foreach (string[] row in nodes)
            {
                if (!IsSpokenNode(nodeTable.Get(row, "EventType")))
                {
                    continue;
                }

                string actorKey = nodeTable.Get(row, "Actor");
                if (string.IsNullOrEmpty(actorKey))
                {
                    continue;
                }

                if (!context.ActorIds.TryGetValue(actorKey, out int actorId) || actorId == context.PlayerActorId)
                {
                    continue;
                }

                if (context.AllyActorIds.Contains(actorId))
                {
                    if (allyFallback == 0)
                    {
                        allyFallback = actorId;
                    }

                    continue;
                }

                return actorId;
            }

            return allyFallback != 0 ? allyFallback : context.PlayerActorId;
        }

        /// <summary>액터가 실제로 말하는 노드인가. SelectDialog도 노드 자신은 대사다.</summary>
        private static bool IsSpokenNode(string eventType)
        {
            return string.Equals(eventType, "Dialog") || string.Equals(eventType, SelectDialogType);
        }

        private static int ResolveActor(string actorKey, string where, BuildContext context)
        {
            if (string.IsNullOrEmpty(actorKey))
            {
                // 시트가 아직 안 채워진 칸이다. 채워지기 전까지는 플레이어로 표시된다.
                context.Log.Warning($"Actor가 비어 있습니다 ({where})");
                return context.PlayerActorId;
            }

            if (context.ActorIds.TryGetValue(actorKey, out int actorId))
            {
                return actorId;
            }

            context.Log.Warning($"ActorData에 없는 Actor입니다: {actorKey} ({where})");
            return context.PlayerActorId;
        }

        /// <summary>EventData의 조건 열을 Lua 논리식으로 바꾼다.</summary>
        private static string BuildConditions(CsvTable eventTable, string[] row)
        {
            var conditions = new List<string>();

            foreach (string questId in SplitMultiValue(eventTable.Get(row, "ActiveQuest")))
            {
                conditions.Add($"CurrentQuestState(\"{questId}\") == \"active\"");
            }

            foreach (string questId in SplitMultiValue(eventTable.Get(row, "ClearQuest")))
            {
                conditions.Add($"CurrentQuestState(\"{questId}\") == \"success\"");
            }

            return string.Join(" and ", conditions.ToArray());
        }

        #endregion

        #region 잡다한 것

        private static bool IsQuestNode(string type)
        {
            return string.Equals(type, "ActiveQuest") || string.Equals(type, "ClearQuest");
        }

        private static int ParseNodeOrder(string value)
        {
            return int.TryParse(value, out int order) ? order : int.MaxValue;
        }

        private static IEnumerable<string> SplitMultiValue(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                yield break;
            }

            foreach (string part in raw.Split(MultiValueSeparators))
            {
                string trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    yield return trimmed;
                }
            }
        }

        #endregion

        private class BuildContext
        {
            public BuildContext(DialogueDatabase database, ImportLog log)
            {
                Database = database;
                Log = log;
            }

            public DialogueDatabase Database { get; }
            public ImportLog Log { get; }

            public Dictionary<string, string> Texts { get; } = new Dictionary<string, string>();
            public Dictionary<string, int> ActorIds { get; } = new Dictionary<string, int>();

            /// <summary>이벤트 ID → 대화 ID. 선택지가 다른 이벤트로 건너뛸 때 목적지를 찾는 데 쓴다.</summary>
            public Dictionary<string, int> ConversationIds { get; } = new Dictionary<string, int>();
            public Dictionary<int, string> ActorKeys { get; } = new Dictionary<int, string>();
            public HashSet<string> QuestIds { get; } = new HashSet<string>();
            public int PlayerActorId { get; set; }

            /// <summary>아군으로 표시된 액터들. 대화 상대(Conversant)를 고를 때 뒤로 미룬다.</summary>
            public HashSet<int> AllyActorIds { get; } = new HashSet<int>();
        }

        /// <summary>
        /// 경고를 콘솔에 흩뿌리지 않고 한 번에 모아서 보여준다. 시트를 고칠 때 훨씬 편하다.
        /// </summary>
        private class ImportLog
        {
            private readonly List<string> _messages = new List<string>();
            private int _warningCount;
            private int _errorCount;

            public string Summary { get; set; } = string.Empty;

            public void Info(string message) => _messages.Add($"· {message}");

            public void Warning(string message)
            {
                _warningCount++;
                _messages.Add($"[경고] {message}");
            }

            public void Error(string message)
            {
                _errorCount++;
                _messages.Add($"[오류] {message}");
            }

            public void Finish(string title, bool success)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"=== {title} ===");

                if (!string.IsNullOrEmpty(Summary))
                {
                    sb.AppendLine(Summary);
                }

                sb.AppendLine($"경고 {_warningCount}건 / 오류 {_errorCount}건");

                foreach (string message in _messages)
                {
                    sb.AppendLine(message);
                }

                string report = sb.ToString();

                if (_errorCount > 0)
                {
                    Debug.LogError(report);
                }
                else if (_warningCount > 0)
                {
                    Debug.LogWarning(report);
                }
                else
                {
                    Debug.Log(report);
                }

                EditorUtility.DisplayDialog(title,
                    (string.IsNullOrEmpty(Summary) ? string.Empty : Summary + "\n") +
                    $"경고 {_warningCount}건 / 오류 {_errorCount}건\n\n자세한 내용은 콘솔을 확인하세요.",
                    "확인");
            }
        }
    }
}
