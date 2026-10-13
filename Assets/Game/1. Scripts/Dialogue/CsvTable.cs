using System.Collections.Generic;
using System.Text;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 헤더 이름으로 값을 읽는 CSV 테이블.
    ///
    /// 열 순서가 아니라 헤더 이름으로 접근하기 때문에, 나중에 번역 열(Text_EN 등)을
    /// 중간에 끼워넣어도 코드를 고칠 필요가 없다.
    ///
    /// RFC4180 따옴표 규칙을 지킨다 — 큰따옴표로 감싼 필드 안의 콤마/줄바꿈은 값의 일부로 보고,
    /// 값 안의 큰따옴표는 ""로 이스케이프된 것으로 본다.
    /// (TextData의 "(게투마티)의 언덕에서 죽어가던 넌, ..." 같은 줄이 여기 해당한다.)
    /// </summary>
    public class CsvTable
    {
        /// <summary>헤더 이름 → 열 인덱스</summary>
        private readonly Dictionary<string, int> _columns = new Dictionary<string, int>();

        private readonly List<string[]> _rows = new List<string[]>();

        public IList<string[]> Rows => _rows;

        public int RowCount => _rows.Count;

        public bool HasColumn(string columnName) => _columns.ContainsKey(columnName);

        /// <summary>
        /// CSV 텍스트를 파싱한다. 첫 줄은 헤더로 취급한다.
        /// </summary>
        public static CsvTable Parse(string text)
        {
            var table = new CsvTable();
            if (string.IsNullOrEmpty(text))
            {
                return table;
            }

            List<string[]> records = ParseRecords(text);
            if (records.Count == 0)
            {
                return table;
            }

            string[] header = records[0];
            for (int i = 0; i < header.Length; i++)
            {
                string name = header[i].Trim();

                // 엑셀에서 저장하면 첫 셀에 BOM이 붙는 경우가 있다.
                name = name.TrimStart('﻿');

                if (!string.IsNullOrEmpty(name) && !table._columns.ContainsKey(name))
                {
                    table._columns[name] = i;
                }
            }

            for (int i = 1; i < records.Count; i++)
            {
                if (IsEmptyRecord(records[i]))
                {
                    continue;
                }

                table._rows.Add(records[i]);
            }

            return table;
        }

        /// <summary>
        /// 행에서 열 이름으로 값을 읽는다. 열이 없거나 값이 비면 빈 문자열.
        /// </summary>
        public string Get(string[] row, string columnName)
        {
            if (row == null || !_columns.TryGetValue(columnName, out int index))
            {
                return string.Empty;
            }

            // 뒤쪽 빈 칸은 CSV에서 아예 생략되기도 한다.
            if (index >= row.Length)
            {
                return string.Empty;
            }

            return row[index].Trim();
        }

        /// <summary>
        /// 행에서 열 **번호**로 값을 읽는다. 헤더가 비어 있어 이름으로 못 잡는 열을 읽을 때 쓴다.
        /// (스프레드시트에서 뽑으면 A1이 빈 칸인 경우가 흔하다.)
        /// </summary>
        public string GetAt(string[] row, int index)
        {
            if (row == null || index < 0 || index >= row.Length)
            {
                return string.Empty;
            }

            return row[index].Trim();
        }

        private static bool IsEmptyRecord(string[] record)
        {
            for (int i = 0; i < record.Length; i++)
            {
                if (!string.IsNullOrEmpty(record[i].Trim()))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 따옴표 상태를 추적하며 한 글자씩 읽는다.
        /// 따옴표 안에서는 콤마와 줄바꿈이 구분자로 동작하지 않는다.
        /// </summary>
        private static List<string[]> ParseRecords(string text)
        {
            var records = new List<string[]>();
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        // "" 는 값에 포함된 따옴표 한 개를 뜻한다.
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;

                    case ',':
                        fields.Add(sb.ToString());
                        sb.Length = 0;
                        break;

                    case '\r':
                        // \r\n 은 한 번만 처리한다.
                        break;

                    case '\n':
                        fields.Add(sb.ToString());
                        sb.Length = 0;
                        records.Add(fields.ToArray());
                        fields.Clear();
                        break;

                    default:
                        sb.Append(c);
                        break;
                }
            }

            // 마지막 줄에 개행이 없을 수 있다.
            if (sb.Length > 0 || fields.Count > 0)
            {
                fields.Add(sb.ToString());
                records.Add(fields.ToArray());
            }

            return records;
        }
    }
}
