using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

// This program reads an Argos XML export file and searches for data blocks that contain certain search terms.
// It outputs a list of data blocks that contain the search terms, along with their reports.
// The search terms are optional and can be provided as a comma-separated list.
// The output is written to SearchMatches.txt in the same directory as the program.
// The program is case-insensitive and searches for the terms in the data block content, ignoring XML tags.
// The program is designed to work with Argos XML exports that contain <DataBlock> and <Report> elements.
// The program is not designed to work with other types of XML files.
// The program is not designed to work with XML files that contain invalid XML syntax.
// The program is not designed to work with XML files that contain large amounts of data.
// The program is not designed to work with XML files that contain sensitive information.
// The program is not designed to work with XML files that contain nested <DataBlock> elements.
// The program is not designed to work with XML files that contain nested <Report> elements.
// The program is not designed to work with XML files that contain other types of nested elements.  
// The program is not designed to work with XML files that contain elements with attributes other than Name.

namespace ArgosToSql
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: ArgosChildrenParser.exe <ArgosExport.xml> [searchTermsCsv]");
                Environment.Exit(1);
            }

            string inFile = args[0];
            if (!File.Exists(inFile))
            {
                Console.WriteLine($"File not found: {inFile}");
                Environment.Exit(1);
            }

            // Parse optional search terms (comma separated)
            var searchTerms = new List<string>();
            if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
            {
                string[] splitted = args[1].Split(',');
                foreach (string s in splitted)
                {
                    string t = s.Trim();
                    if (!string.IsNullOrEmpty(t))
                        searchTerms.Add(t);
                }
            }

            // Read lines
            var allLines = File.ReadAllLines(inFile);
            Console.WriteLine($"[DEBUG] Read {allLines.Length} lines from {inFile}");

            // We'll parse from top-level looking for <Children>
            var dataBlocks = new List<DataBlockNode>();
            int index = 0;
            while (index < allLines.Length)
            {
                var line = allLines[index].TrimStart();
                // if we see <Children
                if (Regex.IsMatch(line, @"^(<|&lt;)Children(\s|>|&gt;)", RegexOptions.IgnoreCase))
                {
                    Console.WriteLine($"[DEBUG] Found <Children> at line {index}");
                    index++;
                    ParseChildren(allLines, ref index, dataBlocks, null);
                }
                else
                {
                    index++;
                }
            }

            Console.WriteLine($"[DEBUG] Finished parsing. Found {dataBlocks.Count} data blocks total.");

            // Now final search step -> produce SearchMatches.txt
            bool anyMatch = false;
            int unnamedCounter = 0;

            using (var sw = new StreamWriter("SearchMatches.txt", false, Encoding.UTF8))
            {
                foreach (var block in dataBlocks)
                {
                    // unify the name. if sub-element also said "Main", we fallback
                    string finalName = block.BlockName;
                    if (string.IsNullOrEmpty(finalName) || finalName.Equals("main", StringComparison.OrdinalIgnoreCase))
                    {
                        unnamedCounter++;
                        finalName = $"UnnamedDataBlock_{unnamedCounter}";
                    }

                    if (MatchesSearch(block.BlockData, searchTerms))
                    {
                        anyMatch = true;
                        sw.WriteLine($"DataBlock: {finalName}");
                        sw.WriteLine("Reports:");
                        if (block.Reports.Count == 0)
                        {
                            sw.WriteLine("  (No reports)");
                        }
                        else
                        {
                            foreach (var repName in block.Reports)
                            {
                                sw.WriteLine($"  - {repName}");
                            }
                        }
                        sw.WriteLine();
                    }
                }

                if (!anyMatch)
                {
                    sw.WriteLine("No data blocks matched the search terms, or no search terms provided.");
                }
            }

            Console.WriteLine("[DEBUG] Done. See SearchMatches.txt for results.");
        }

        /// <summary>
        /// Parse lines that are inside a <Children>...</Children> block,
        /// collecting <DataBlock> or <Report> or nested <Children>.
        /// If we find <DataBlock>, we parse it, store as a DataBlockNode.
        /// Then we look for <Children> inside the block for <Report> elements, etc.
        /// </summary>
        private static void ParseChildren(string[] lines, ref int index, List<DataBlockNode> dataBlocks, DataBlockNode currentBlock)
        {
            while (index < lines.Length)
            {
                var line = lines[index].TrimStart();
                // if we see </Children>
                if (Regex.IsMatch(line, @"^(<|&lt;)\/Children(>|&gt;)", RegexOptions.IgnoreCase))
                {
                    Console.WriteLine($"[DEBUG] Found </Children> at line {index}, returning from ParseChildren");
                    index++;
                    break;
                }
                // <DataBlock
                else if (Regex.IsMatch(line, @"^(<|&lt;)DataBlock(\s|>|&gt;)", RegexOptions.IgnoreCase))
                {
                    Console.WriteLine($"[DEBUG] Found <DataBlock> at line {index}");
                    index++;
                    var block = ParseDataBlock(lines, ref index);
                    dataBlocks.Add(block);

                    // a data block can have <Children> inside it too, so parse that
                    while (index < lines.Length)
                    {
                        var ln2 = lines[index].TrimStart();
                        if (Regex.IsMatch(ln2, @"^(<|&lt;)\/DataBlock(>|&gt;)", RegexOptions.IgnoreCase))
                        {
                            Console.WriteLine($"[DEBUG] Found </DataBlock> at line {index}, finishing block '{block.BlockName}'");
                            index++;
                            break;
                        }
                        else if (Regex.IsMatch(ln2, @"^(<|&lt;)Children(\s|>|&gt;)", RegexOptions.IgnoreCase))
                        {
                            Console.WriteLine($"[DEBUG] Found <Children> inside DataBlock, parse children for block '{block.BlockName}'");
                            index++;
                            ParseChildren(lines, ref index, dataBlocks, block);
                        }
                        else
                        {
                            index++;
                        }
                    }
                }
                // <Report
                else if (Regex.IsMatch(line, @"^(<|&lt;)Report(\s|>|&gt;)", RegexOptions.IgnoreCase))
                {
                    Console.WriteLine($"[DEBUG] Found <Report> at line {index}");
                    var repName = ParseReport(lines, ref index);
                    if (currentBlock != null)
                    {
                        Console.WriteLine($"[DEBUG] Linking report '{repName}' to block '{currentBlock.BlockName}'");
                        currentBlock.Reports.Add(repName);
                    }
                }
                // nested <Children>
                else if (Regex.IsMatch(line, @"^(<|&lt;)Children(\s|>|&gt;)", RegexOptions.IgnoreCase))
                {
                    Console.WriteLine($"[DEBUG] Found nested <Children> at line {index}, parse children with same block");
                    index++;
                    ParseChildren(lines, ref index, dataBlocks, currentBlock);
                }
                else
                {
                    index++;
                }
            }
        }

        /// <summary>
        /// Parse <DataBlock> content. We expect possibly:
        ///   - <DataBlock Name="..." ...> or no attribute
        ///   - <Name>some block</Name> sub-element
        ///   - <Data> lines of code </Data>
        /// We'll gather:
        ///   blockName from attribute or <Name> sub-element
        ///   blockData from <Data> lines (lowercased)
        /// Then we'll break once we see <Children> or <Report> or </DataBlock> in the parent loop
        /// </summary>
        private static DataBlockNode ParseDataBlock(string[] lines, ref int index)
        {
            var block = new DataBlockNode();
            bool insideData = false;
            StringBuilder sbData = new StringBuilder();

            // We already advanced index past the <DataBlock line, so let's check that line:
            var openLineIndex = index - 1;
            var openLine = lines[openLineIndex];
            // check if we have an attribute Name="..."
            var mAttr = Regex.Match(openLine, @"Name\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
            if (mAttr.Success)
            {
                block.BlockName = mAttr.Groups[1].Value.Trim();
                Console.WriteLine($"[DEBUG] DataBlock with attribute Name=\"{block.BlockName}\" at line {openLineIndex}");
            }

            while (index < lines.Length)
            {
                var line = lines[index].TrimStart();

                // <Name>some block</Name> sub-element
                var mName = Regex.Match(line, @"^(<|&lt;)Name(>|&gt;)([^<]+)(<|&lt;)\/Name(>|&gt;)", RegexOptions.IgnoreCase);
                if (mName.Success && mName.Groups.Count >= 4)
                {
                    var subName = mName.Groups[3].Value.Trim();
                    Console.WriteLine($"[DEBUG] Found sub-element <Name>: '{subName}' at line {index}");
                    // If the attribute was "Main" or empty, we override with subName
                    if (string.IsNullOrEmpty(block.BlockName) || block.BlockName.Equals("main", StringComparison.OrdinalIgnoreCase))
                    {
                        block.BlockName = subName;
                    }
                }

                // <Data>
                if (Regex.IsMatch(line, @"^(<|&lt;)Data(>|&gt;)", RegexOptions.IgnoreCase))
                {
                    Console.WriteLine($"[DEBUG] Found <Data> start at line {index}");
                    insideData = true;
                    index++;
                    continue;
                }
                // </Data>
                if (Regex.IsMatch(line, @"^(<|&lt;)\/Data(>|&gt;)", RegexOptions.IgnoreCase))
                {
                    Console.WriteLine($"[DEBUG] Found </Data> at line {index}");
                    insideData = false;
                    index++;
                    continue;
                }

                // if inside data, store lines in lowercase
                if (insideData)
                {
                    sbData.AppendLine(line.ToLower());
                    index++;
                    continue;
                }

                // If we see <Children> or <Report> or </DataBlock>, we stop
                if (Regex.IsMatch(line, @"^(<|&lt;)\/DataBlock(>|&gt;)|^(<|&lt;)Children(\s|>|&gt;)|^(<|&lt;)Report(\s|>|&gt;)", RegexOptions.IgnoreCase))
                {
                    break;
                }

                index++;
            }

            // if the attribute name is missing or says "Main" but we found no sub <Name>, we'll keep it for now
            // fallback logic is in final step
            block.BlockData = sbData.ToString();
            Console.WriteLine($"[DEBUG] parseDataBlock => blockName='{block.BlockName}', dataLength={block.BlockData.Length}");
            return block;
        }

        /// <summary>
        /// Parse <Report ...> lines until </Report>. Return the name found, else "(Unnamed Report)".
        /// </summary>
        private static string ParseReport(string[] lines, ref int index)
        {
            string repName = "";
            var openLine = lines[index];
            index++;

            // check name= if available
            var mName = Regex.Match(openLine, @"Name\s*=\s*""([^""]*)""");
            if (mName.Success)
            {
                repName = mName.Groups[1].Value.Trim();
            }

            bool foundClose = false;
            while (index < lines.Length)
            {
                var line = lines[index].TrimStart();
                if (Regex.IsMatch(line, @"^(<|&lt;)\/Report(>|&gt;)", RegexOptions.IgnoreCase))
                {
                    Console.WriteLine($"[DEBUG] Found </Report> at line {index}");
                    index++;
                    foundClose = true;
                    break;
                }
                else
                {
                    index++;
                }
            }
            if (!foundClose)
            {
                Console.WriteLine($"[DEBUG] Did not find </Report> before file ended or next block");
            }

            Console.WriteLine($"[DEBUG] Done parseReport => '{repName}'");
            if (string.IsNullOrEmpty(repName)) repName = "(Unnamed Report)";
            return repName;
        }

        /// <summary>
        /// Returns true if blockData has any search term, ignoring lines that start with < or &lt;.
        /// blockData is already lowercased.
        /// </summary>
        private static bool MatchesSearch(string blockData, List<string> searchTerms)
        {
            if (searchTerms.Count == 0)
            {
                Console.WriteLine("[DEBUG] No search terms => automatically matches everything.");
                return true;
            }

            // split by lines
            var lines = blockData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // convert user's terms to lowercase
            var lowers = new List<string>();
            foreach (var t in searchTerms)
                lowers.Add(t.ToLower());

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                // skip if starts with < or &lt;
                if (line.StartsWith("<") || line.StartsWith("&lt;"))
                {
                    continue;
                }

                // check if line contains any search term
                foreach (var term in lowers)
                {
                    if (line.Contains(term))
                    {
                        Console.WriteLine($"[DEBUG] MATCH for '{term}' in line {i}: {line}");
                        return true;
                    }
                }
            }

            return false;
        }
    }

    // ------------------------------------------------------------------
    // Data classes
    // ------------------------------------------------------------------
    internal class DataBlockNode
    {
        public string BlockName { get; set; } = "";
        public string BlockData { get; set; } = "";  // all-lower text from <Data>
        public List<string> Reports { get; } = new List<string>();
    }
}
