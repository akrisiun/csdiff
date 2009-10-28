//
// Copyright (C) 2009  Thomas Bluemel <thomasb@reactsoft.com>
// 
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 2
// of the License, or (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Diff
{
    public class UnifiedDiffInfo
    {
        #region Fields
        private Stream _stream;
        private string _label = string.Empty;
        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets the data stream.
        /// </summary>
        public Stream Stream { get { return _stream; } set { _stream = value; } }
        /// <summary>
        /// Gets or sets the label.
        /// </summary>
        public string Label { get { return _label; } set { _label = value; } }
        #endregion

        #region Constructors
        public UnifiedDiffInfo()
        {
        }

        public UnifiedDiffInfo(Stream stream)
            : this()
        {
            _stream = stream;
        }

        public UnifiedDiffInfo(Stream stream, string label)
            : this(stream)
        {
            _label = label;
        }
        #endregion
    }

    public static class UnifiedDiff
    {
        #region DiffLine class
        private class DiffLine : IComparable
        {
            #region Fields
            public int lineNumber;
            public string line;
            #endregion

            #region Constructors
            public DiffLine(int lineNumber, string line)
            {
                this.lineNumber = lineNumber;
                this.line = line;
            }
            #endregion

            #region Public methods
            public int CompareTo(object obj)
            {
                if (obj is DiffLine)
                    return line.CompareTo((obj as DiffLine).line);
                else if (obj is string)
                    return line.CompareTo(obj);
                throw new ArgumentException("obj is not DiffLine or string");
            }
            #endregion
        }
        #endregion

        #region Hunk class
        private class Hunk
        {
            private int lineStart1;
            private int lineStart2;
            private int lines1 = 0;
            private int lines2 = 0;
            private List<string> lines = new List<string>();

            public bool HasLines { get { return lines.Count > 0; } }

            public Hunk(int lineStart1, int lineStart2)
            {
                this.lineStart1 = lineStart1;
                this.lineStart2 = lineStart2;
            }

            public void Write(StreamWriter writer)
            {
                writer.WriteLine(string.Format("@@ -{0},{1} +{2},{3} @@", lineStart1 + 1, lines1, lineStart2 + 1, lines2));
                foreach (var line in lines)
                    writer.WriteLine(line);
            }

            public void AddContext(string line)
            {
                lines.Add(string.Format(" {0}", line));
                lines1++;
                lines2++;
            }

            public void AddRemove(string line)
            {
                lines.Add(string.Format("-{0}", line));
                lines1++;
            }

            public void AddNew(string line)
            {
                lines.Add(string.Format("+{0}", line));
                lines2++;
            }
        }
        #endregion

        #region Public static methods
        /// <summary>
        /// This method creates a unified diff file.
        /// </summary>
        /// <param name="oldInfo">Provides the information for the old file (optional).</param>
        /// <param name="newInfo">Provides the information for the new file (optional).</param>
        /// <param name="streamOut">Stream where the diff file is written.</param>
        /// <param name="contextLines">Number of context lines around hunks.</param>
        /// <returns></returns>
        public static bool Create(UnifiedDiffInfo oldInfo, UnifiedDiffInfo newInfo, Stream streamOut, int contextLines)
        {
            if (streamOut == null)
                throw new ArgumentException("streamOut must not be null");
            if (contextLines < 0)
                throw new ArgumentException("contextLines cannot be negative");

            StreamReader streamReader1 = new StreamReader(oldInfo != null && oldInfo.Stream != null ? oldInfo.Stream : new MemoryStream());
            StreamReader streamReader2 = new StreamReader(newInfo != null && newInfo.Stream != null ? newInfo.Stream : new MemoryStream());
            StreamWriter streamWriter = new StreamWriter(streamOut);
            List<DiffLine> lines1 = new List<DiffLine>();
            List<DiffLine> lines2 = new List<DiffLine>();
            string line1, line2;
            int start = 0, end = 0, firstLine = 0;

            // Read text stream into a list. If the beginning of the file matches, this only keeps the
            // the last contextLines number of lines in memory for the unified context of the
            // first hunk.  Diff.CreateDiff() actually would do this for us too, but we don't need
            // to waste a lot of memory if we're not going to use it anyway.

            do
            {
                line1 = streamReader1.ReadLine();
                line2 = streamReader2.ReadLine();

                if (line1 != null && line2 != null && string.Equals(line1, line2))
                {
                    lines1.Add(new DiffLine(firstLine, line1));
                    lines2.Add(new DiffLine(firstLine, line2));

                    if (start == contextLines)
                    {
                        lines1.RemoveAt(0);
                        lines2.RemoveAt(0);
                        firstLine++;
                    }
                    else
                        start++;
                    continue;
                }

                while (line1 != null)
                {
                    lines1.Add(new DiffLine(firstLine + lines1.Count, line1));
                    line1 = streamReader1.ReadLine();
                }

                while (line2 != null)
                {
                    lines2.Add(new DiffLine(firstLine + lines2.Count, line2));
                    line2 = streamReader2.ReadLine();
                }

            } while (line1 != null || line2 != null);

            // Also get rid of the last lines of the file if they are equal
            int endCnt = Math.Min(lines1.Count, lines2.Count) - start;
            int removeEnd = 0;
            for (int i = 0; i < endCnt; i++)
            {
                if (string.Equals(lines1[lines1.Count - i - 1].line, lines2[lines2.Count - i - 1].line))
                {
                    if (end == contextLines)
                        removeEnd++;
                    else
                        end++;
                }
                else
                    break;
            }

            if (removeEnd > 0)
            {
                lines1.RemoveRange(lines1.Count - removeEnd, removeEnd);
                lines2.RemoveRange(lines2.Count - removeEnd, removeEnd);
            }

            // Now we've got a list of lines to compare.  The list context contains start
            // lines before the first difference, and end lines after the last difference.
            List<DiffEntry<DiffLine>> diffEntries = Diff.CreateDiff<DiffLine>(lines1, lines2, start, end);

            // Create a list of hunks and their lines including the context lines
            List<Hunk> hunks = new List<Hunk>();
            Hunk hunk = null;

            int l1 = firstLine, l2 = firstLine;
            for (int i = 0; i < diffEntries.Count; i++)
            {
                DiffEntry<DiffLine> entry = diffEntries[i];

                if (hunk == null)
                {
                    if (entry.EntryType == DiffEntry<DiffLine>.DiffEntryType.Equal)
                    {
                        int cnt = Math.Min(entry.Count, contextLines);
                        hunk = new Hunk(l1 + entry.Count - cnt, l2 + entry.Count - cnt);
                        for (int j = cnt; j > 0; j--)
                            hunk.AddContext(lines1[l1 + entry.Count - j - firstLine].line);
                        hunks.Add(hunk);
                        l1 += entry.Count;
                        l2 += entry.Count;
                        continue;
                    }

                    hunk = new Hunk(l1, l2);
                    hunks.Add(hunk);
                }

                switch (entry.EntryType)
                {
                    case DiffEntry<DiffLine>.DiffEntryType.Add:
                        hunk.AddNew(lines2[l2 - firstLine].line);
                        l2++;
                        break;
                    case DiffEntry<DiffLine>.DiffEntryType.Remove:
                        hunk.AddRemove(lines1[l1 - firstLine].line);
                        l1++;
                        break;
                    case DiffEntry<DiffLine>.DiffEntryType.Equal:
                        if (i == diffEntries.Count - 1)
                        {
                            int cnt = Math.Min(contextLines, entry.Count);
                            for (int j = 0; j < cnt; j++)
                                hunk.AddContext(lines1[l1 + j - firstLine].line);
                        }
                        else
                        {
                            if (entry.Count > 2 * contextLines)
                            {
                                for (int j = 0; j < contextLines; j++)
                                    hunk.AddContext(lines1[l1 + j - firstLine].line);
                                l1 += entry.Count;
                                l2 += entry.Count;

                                hunk = new Hunk(l1 - contextLines, l2 - contextLines);
                                for (int j = contextLines; j > 0; j--)
                                    hunk.AddContext(lines1[l1 - j - firstLine].line);
                                hunks.Add(hunk);
                            }
                            else
                            {
                                for (int j = 0; j < entry.Count; j++)
                                    hunk.AddContext(lines1[l1 + j - firstLine].line);
                                l1 += entry.Count;
                                l2 += entry.Count;
                            }
                        }
                        break;
                }
            }

            if (hunks.Count > 0)
            {
                // Write the hunks to the output stream
                if (oldInfo != null && !string.IsNullOrEmpty(oldInfo.Label))
                    streamWriter.WriteLine(string.Format("--- {0}", oldInfo.Label));
                if (newInfo != null && !string.IsNullOrEmpty(newInfo.Label))
                    streamWriter.WriteLine(string.Format("+++ {0}", newInfo.Label));

                foreach (var hk in hunks)
                    hk.Write(streamWriter);

                streamWriter.WriteLine();
                streamWriter.Flush();
                return true;
            }

            return false;
        }
        #endregion
    }
}
