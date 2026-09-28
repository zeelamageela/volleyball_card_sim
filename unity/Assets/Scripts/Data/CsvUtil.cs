using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VolleyballData
{
    /// <summary>
    /// Minimal CSV reader mirroring Python's csv.DictReader closely enough for this
    /// project's data files: comma-delimited, double-quoted fields (with "" as an
    /// escaped quote inside a quoted field) are supported since at least one real
    /// file (team_passives.csv) uses them; embedded newlines inside quoted fields
    /// are not supported (none of this project's CSVs use them).
    ///
    /// Strips a leading UTF-8 BOM, matching Python's `encoding="utf-8-sig"`.
    /// </summary>
    public static class CsvUtil
    {
        /// <summary>Read a CSV file into a list of header-keyed row dictionaries.</summary>
        public static List<Dictionary<string, string>> ReadRows(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Length > 0 && text[0] == '﻿')
            {
                text = text.Substring(1);
            }

            var lines = SplitLines(text);
            var rows = new List<Dictionary<string, string>>();
            if (lines.Count == 0)
            {
                return rows;
            }

            var headers = ParseLine(lines[0]);
            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i].Length == 0)
                {
                    continue; // skip trailing blank lines
                }
                var fields = ParseLine(lines[i]);
                var row = new Dictionary<string, string>();
                for (int h = 0; h < headers.Count; h++)
                {
                    row[headers[h]] = h < fields.Count ? fields[h] : "";
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>Get a column value, or "" if the column is missing/absent from this row
        /// (mirrors Python's row.get(key, "")).</summary>
        public static string Get(Dictionary<string, string> row, string key) =>
            row.TryGetValue(key, out string v) ? v : "";

        private static List<string> SplitLines(string text)
        {
            var lines = new List<string>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    int end = i;
                    if (end > start && text[end - 1] == '\r')
                    {
                        end--;
                    }
                    lines.Add(text.Substring(start, end - start));
                    start = i + 1;
                }
            }
            if (start < text.Length)
            {
                lines.Add(text.Substring(start));
            }
            return lines;
        }

        private static List<string> ParseLine(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        fields.Add(current.ToString());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
            }
            fields.Add(current.ToString());
            return fields;
        }
    }
}
