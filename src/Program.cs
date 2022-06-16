#region Using

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Markdig;

#endregion

namespace Generator
{
    public class Program
    {
        public static Configuration Settings;
        public static MarkdownPipeline MarkDigPipeline;
        public static string[] IgnoreFiles = {"order.txt"};

        public static void Main(string[] args)
        {
            Console.WriteLine("~-~ Wiki Generator ~-~");

            // Try to read settings.
            var serializer = new XmlSerializer(typeof(Configuration));
            if (File.Exists("settings.xml"))
            {
                using FileStream fileStream = File.OpenRead("settings.xml");
                Settings = (Configuration) serializer.Deserialize(fileStream);
                Console.WriteLine("Settings loaded!");
            }

            if (Settings == null)
            {
                Settings = new Configuration();
                Console.WriteLine("Couldn't read settings file, creating one with default settings.");
                using FileStream fileStream = File.Create("settings.xml");
                using var writer = new XmlTextWriter(fileStream, Encoding.Unicode);
                writer.Formatting = Formatting.Indented;
                serializer.Serialize(writer, Settings);
            }

            // Build markdown pipeline.
            MarkDigPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

            Build();

            if (args.Length > 0 && args[0] == "sentry") SentrySetup();

            Console.WriteLine("Done. Press any key to quit.");
            Console.ReadKey();
        }

        private static void Build()
        {
            Stopwatch timeTracker = Stopwatch.StartNew();

            // Generate all nodes.
            Console.WriteLine("Generating nodes...");
            Node masterNode = GetDirectoryNode(Settings.SourcePath);
            if (masterNode == null) return;

            // Copy source and template.
            Console.WriteLine("Copying template and source files to output...");
            CopyFolderToFolder(Settings.SourcePath, Settings.OutputDirectory + Settings.SourceOutput, fi => ProcessFile(PreprocessFile(fi, masterNode)));
            CopyFolderToFolder(Settings.TemplateDirectory, Settings.OutputDirectory, fi => ProcessHTMLTags(fi));

            // Generate list for navigator.
            Console.WriteLine("Creating navigation...");
            string navigationTemplate = $"{Settings.OutputDirectory}\\navigator.html";
            List<string> navigationFileHtml = File.ReadAllLines(navigationTemplate).ToList();
            for (int i = 0; i < navigationFileHtml.Count; i++)
            {
                if (!navigationFileHtml[i].Contains("[Navigator]")) continue;

                navigationFileHtml.RemoveAt(i);
                navigationFileHtml.InsertRange(i, GenerateList(masterNode));
                break;
            }

            File.WriteAllLines(navigationTemplate, navigationFileHtml);

            Console.WriteLine($"Built in {timeTracker.ElapsedMilliseconds}ms!");
        }

        /// <summary>
        /// Get the node which corresponds to a specific directory.
        /// Includes all children nodes.
        /// </summary>
        /// <param name="directoryPath">The path to the directory to get the node of.</param>
        /// <returns>The node corresponding to the provided path.</returns>
        private static Node GetDirectoryNode(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Console.WriteLine($"[!] Directory path missing - {directoryPath}");
                return null;
            }

            var node = new Node
            {
                Name = Path.GetFileNameWithoutExtension(directoryPath),
                File = "about:blank"
            };

            string[] files = Directory.GetFiles(directoryPath);
            string[] directories = Directory.GetDirectories(directoryPath);

            Array.Sort(files);
            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                if (fileName == "Folder.md")
                {
                    node.File = Path.Join(directoryPath, "Folder.md");
                    continue;
                }

                // Check if file is being skipped.
                if (IgnoreFiles.FirstOrDefault(x => x == fileName) != null) continue;

                var newNode = new Node
                {
                    Name = Path.GetFileNameWithoutExtension(fileName),
                    File = Path.Join(directoryPath, fileName)
                };

                node.Children.Add(newNode);
            }

            Array.Sort(directories);
            foreach (string dir in directories)
            {
                node.Children.Add(GetDirectoryNode(dir));
            }

            // Check if an ordering file exists.
            string orderFile = Path.Join(directoryPath, "order.txt");
            if (!File.Exists(orderFile)) return node;
            string[] order = File.ReadAllLines(orderFile);

            node.Children.Sort((x, y) =>
            {
                var indexOfX = Array.IndexOf(order, x.Name);
                var indexOfY = Array.IndexOf(order, y.Name);
                return indexOfX - indexOfY;
            });

            return node;
        }

        #region Markdown Preprocessor

        /// <summary>
        /// Link format is [[Link Text]](Optional link overwrite)
        /// The link overwrite is handled outside the regex because it may contain nested brackets.
        /// </summary>
        private static Regex _linkFinder = new Regex(@"(?:\[\[)([\s\S]+?)(?:\]\])");

        /// <summary>
        /// Tab format is [\t]
        /// </summary>
        private static Regex _tabFinder = new Regex(@"\[\\t\]");

        /// <summary>
        /// Applies markdown preprocessing such as:
        /// - Adding wiki style links.
        /// </summary>
        /// <param name="fileInfo">The file info.</param>
        /// <param name="topNode">The top node in the tree.</param>
        /// <returns>The processed file info.</returns>
        private static ProcessFileInfo PreprocessFile(ProcessFileInfo fileInfo, Node topNode)
        {
            Console.WriteLine($" Preprocessing file {fileInfo.FileName}...");
            string[] contents = fileInfo.Contents;

            // Create a lookup for node links.
            var nodeLookup = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            var nodeStack = new Stack<Node>();
            nodeStack.Push(topNode);
            while (nodeStack.Count != 0)
            {
                Node curNode = nodeStack.Pop();

                // Add children to the stack to be processed.
                foreach (Node child in curNode.Children)
                {
                    nodeStack.Push(child);
                }

                // Check if the node name is taken.
                string keyName = curNode.Name;
                if (!nodeLookup.ContainsKey(keyName)) nodeLookup.Add(keyName, curNode);

                // Add the node as relative path as well.
                string pathFromSource = Path.Join(Path.GetDirectoryName(curNode.File).Replace(Settings.SourcePath, ""), Path.GetFileNameWithoutExtension(curNode.File));
                if (!string.IsNullOrEmpty(pathFromSource) && pathFromSource != "about:blank" && !nodeLookup.ContainsKey(pathFromSource))
                    nodeLookup.Add(pathFromSource, curNode);
            }

            for (int i = 0; i < contents.Length; i++)
            {
                // Replace with custom tags.
                foreach (Tag tag in Settings.CustomTags)
                {
                    contents[i] = contents[i].Replace(tag.Key, tag.Value);
                }

                // Look for links.
                Match match = _linkFinder.Match(contents[i]);
                if (match.Success)
                    while (match.Success)
                    {
                        string linkText = match.Groups[1].Value;
                        //string linkOverwrite = match.Groups[3].Value;
                        string link;

                        // Look for a link overwrite. Format is "[regex matched](link overwrite)"
                        string linkOverwrite = null;
                        int matchEnd = match.Index + match.Length;
                        if (matchEnd < contents[i].Length - 1)
                            // Link overwrite opening tag.
                            if (contents[i][matchEnd] == '(')
                            {
                                // Find the closing bracket.
                                linkOverwrite = "";
                                int openingBracketsCount = 1;
                                int pointer = matchEnd;
                                while (pointer <= contents[i].Length - 1)
                                {
                                    pointer++;
                                    char nextChar = contents[i][pointer];
                                    if (nextChar == ')') openingBracketsCount--;
                                    if (openingBracketsCount == 0)
                                        break;
                                    if (nextChar == '(') openingBracketsCount++;

                                    linkOverwrite += nextChar;
                                }

                                matchEnd = pointer + 1;
                            }

                        bool foundLink = nodeLookup.TryGetValue(string.IsNullOrEmpty(linkOverwrite) ? linkText : linkOverwrite, out Node referencedNode);
                        if (foundLink)
                        {
                            string path = TransformPathToWeb(referencedNode.File);
                            link = $"<a class=\"link\" link='{path}'>{linkText}</a>";
                        }
                        else
                        {
                            link = $"<span class=\"missingLink\">{linkText}</span>";
                        }

                        contents[i] = contents[i].Substring(0, match.Index) + link + contents[i].Substring(matchEnd);
                        match = _linkFinder.Match(contents[i]);
                    }

                // Look for tabs.
                match = _tabFinder.Match(contents[i]);
                if (!match.Success) continue;

                while (match.Success)
                {
                    contents[i] = contents[i].Substring(0, match.Index) + "&nbsp;&nbsp;&nbsp;&nbsp;" + contents[i].Substring(match.Index + match.Length);
                    match = _linkFinder.Match(contents[i]);
                }
            }

            return fileInfo;
        }

        private static List<string> _cachedMarkdownTemplate;
        private static int _markdownTemplateInsertionIndex = -1;

        /// <summary>
        /// Transforms the markdown file into an html file.
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <returns></returns>
        private static ProcessFileInfo ProcessFile(ProcessFileInfo fileInfo)
        {
            Console.WriteLine($" Processing file {fileInfo.FileName}...");
            fileInfo.FileName = Path.Join(Path.GetDirectoryName(fileInfo.FileName), Path.GetFileNameWithoutExtension(fileInfo.FileName)) + ".html";

            // Check if the markdown template is loaded.
            if (_cachedMarkdownTemplate == null)
            {
                string templateFile = Path.Join(Settings.TemplateDirectory, "markdownTemplate.html");
                if (File.Exists(templateFile))
                {
                    _cachedMarkdownTemplate = File.ReadAllLines(templateFile).ToList();
                    // Find the index where the content is to be inserted.
                    for (int i = 0; i < _cachedMarkdownTemplate.Count; i++)
                    {
                        if (!_cachedMarkdownTemplate[i].Contains("[Content]")) continue;

                        _cachedMarkdownTemplate.RemoveAt(i);
                        _markdownTemplateInsertionIndex = i;
                        break;
                    }

                    if (_markdownTemplateInsertionIndex == -1) Console.WriteLine(" [!] Couldn't find markdown template [Content] tag!");

                    Console.WriteLine(" Loaded markdown template file!");
                }
                else
                {
                    Console.WriteLine(" [!] Couldn't find markdown template html!");
                    return fileInfo;
                }
            }

            // Copy the template into a new content holder.
            var newFileContent = new List<string>();
            newFileContent.AddRange(_cachedMarkdownTemplate);

            // Insert the rendered html.
            string htmlMarkdown = Markdown.ToHtml(string.Join("\n", fileInfo.Contents), MarkDigPipeline);
            newFileContent.Insert(_markdownTemplateInsertionIndex, htmlMarkdown);

            // Perform additional processing on html to insert relative paths to the template.
            for (int i = 0; i < newFileContent.Count; i++)
            {
                newFileContent[i] = newFileContent[i].Replace("[RelativePath]", Path.GetRelativePath(Path.GetDirectoryName(fileInfo.FileName), Settings.OutputDirectory).Replace("\\", "/"));
            }

            fileInfo.Contents = newFileContent.ToArray();

            return fileInfo;
        }

        #endregion

        #region HTML Preprocessor

        /// <summary>
        /// Preprocess HTML tags.
        /// - [Title] turns into Settings.WikiTitle
        /// </summary>
        /// <param name="fileInfo">The file info.</param>
        /// <returns>The processed file info.</returns>
        public static ProcessFileInfo ProcessHTMLTags(ProcessFileInfo fileInfo)
        {
            var processingTags = new Dictionary<string, string>
            {
                {"[Title]", Settings.WikiTitle}
            };
            FindReplaceTags(fileInfo.Contents, processingTags);
            return fileInfo;
        }

        /// <summary>
        /// Finds the provided list of tags (keys) within the content list and replaces them with their values.
        /// </summary>
        /// <param name="content">The content to look in.</param>
        /// <param name="tags">The tags to replace.</param>
        private static void FindReplaceTags(string[] content, Dictionary<string, string> tags)
        {
            // Find and replace all specified tags with their values.
            for (int i = 0; i < content.Length; i++)
            {
                foreach ((string key, string value) in tags.Where(tag => content[i].Contains(tag.Key)))
                {
                    content[i] = content[i].Replace(key, value);
                }
            }
        }

        #endregion

        #region Navigation

        /// <summary>
        /// Generates the navigation list.
        /// </summary>
        /// <param name="topNode">The node to start from/</param>
        /// <returns>HTML for the navigation list.</returns>
        private static IEnumerable<string> GenerateList(Node topNode)
        {
            var list = new List<string>();

            foreach (Node child in topNode.Children)
            {
                var html = new List<string>();
                string childFileLink = TransformPathToWeb(child.File);
                bool activeLink = File.Exists(child.File);
                string linkAttribute = activeLink ? $"</span><span class=\"link\" link=\"{childFileLink}\">{child.Name}" : $"{child.Name}";

                // Check further children.
                if (child.Children.Count > 0)
                {
                    html.Add($"<li><span class=\"caret\">{linkAttribute}</span>");
                    html.Add("<ul class=\"active\">");
                    html.AddRange(GenerateList(child));
                    html.Add("</ul>");
                    html.Add("</li>");
                }
                else
                {
                    html.Add($"<li><span class=\"fake-caret\">\u2BC4 {linkAttribute}</span></li>");
                }

                list.AddRange(html);
            }

            return list;
        }

        /// <summary>
        /// Transforms the path to a web path.
        /// </summary>
        /// <param name="input">The path to transform.</param>
        /// <returns>The transformed path.</returns>
        private static string TransformPathToWeb(string input)
        {
            string path = input.Replace(Settings.SourcePath, Settings.SourceOutput + Settings.WebPrefix);
            int extensionIndex = path.LastIndexOf('.');
            if (extensionIndex != -1)
                return path.Substring(0, path.LastIndexOf('.')) + ".html";
            return path;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Copies a folder to another folder, maintaining its internal structure.
        /// Allows for file processing as well.
        /// </summary>
        /// <param name="src">The source folder.</param>
        /// <param name="dst">The destination folder.</param>
        /// <param name="processFunction">The function which will process each file before it is copied.</param>
        private static void CopyFolderToFolder(string src, string dst, Action<ProcessFileInfo> processFunction = null)
        {
            string[] sourceFiles = Directory.GetFiles(src, "*", SearchOption.AllDirectories);
            string[] directories = Directory.GetDirectories(src, "*", SearchOption.AllDirectories);
            foreach (string dir in directories)
            {
                Directory.CreateDirectory(dir.Replace(src, dst));
            }

            foreach (string file in sourceFiles)
            {
                // Check if file is being skipped.
                string fileName = Path.GetFileName(file);
                if (IgnoreFiles.FirstOrDefault(x => x == fileName) != null) continue;

                string destName = file.Replace(src, dst);

                if (processFunction != null)
                {
                    string[] contents = File.ReadAllLines(file);
                    var info = new ProcessFileInfo(destName, contents);
                    processFunction(info);
                    File.WriteAllLines(info.FileName, info.Contents);
                }
                else
                {
                    File.Copy(file, destName, true);
                }
            }
        }

        #endregion

        #region Sentry

        public static Task SentryTask;
        public static FileInfo[] SentryFiles;

        public static void SentrySetup()
        {
            SentryTask = Task.Run(SentryLoop);
            SentryTask.Wait();
        }

        public static void SentryLoop()
        {
            // Cache all current source files.
            string[] files = Directory.GetFiles(Settings.SourcePath, "*", SearchOption.AllDirectories);
            SentryFiles = files.Select(x => new FileInfo(x)).ToArray();

            Console.WriteLine("Sentry mode started!");

            while (!Environment.HasShutdownStarted)
            {
                // Sentry tick delay.
                Task.Delay(10000).Wait();
                Console.WriteLine("Sentry tick!");

                bool rebuild = false;

                // Check for modified files.
                foreach (FileInfo file in SentryFiles)
                {
                    DateTime lastModified = file.LastWriteTimeUtc;
                    file.Refresh();
                    if (lastModified == file.LastWriteTimeUtc) continue;
                    Console.WriteLine($"File {file.Name} has changed.");
                    rebuild = true;
                    break;
                }

                // Check for new files.
                files = Directory.GetFiles(Settings.SourcePath, "*", SearchOption.AllDirectories);
                if (files.Length != SentryFiles.Length)
                {
                    Console.WriteLine($"File count has changed, from {SentryFiles.Length} to {files.Length}.");
                    SentryFiles = files.Select(x => new FileInfo(x)).ToArray();
                    rebuild = true;
                }

                if (!rebuild) continue;
                Console.WriteLine("Sentry is rebuilding...");
                Build();
            }
        }

        #endregion
    }
}