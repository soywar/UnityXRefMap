using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityXRefMap.Yaml;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace UnityXRefMap
{
    internal class Program
    {
        private static readonly string UnityCsReferenceRepositoryUrl;
        private static readonly string UnityCsReferenceLocalPath;
        private static readonly string GeneratedMetadataPath;
        private static readonly string OutputFolder;
        private static readonly Serializer Serializer;
        private static readonly Deserializer Deserializer;
        private static readonly List<XRefMapReference> References;
        private static readonly Process Process;
        private static readonly Regex BranchRegex;

        static Program()
        {
            UnityCsReferenceRepositoryUrl = "https://github.com/Unity-Technologies/UnityCsReference";
            UnityCsReferenceLocalPath = Path.Join(Environment.CurrentDirectory, "UnityCsReference");
            GeneratedMetadataPath = Path.Join(Environment.CurrentDirectory, "ScriptReference");
            OutputFolder = Path.Join(Environment.CurrentDirectory, "out");
            Serializer = new();
            Deserializer = new();
            References = new();
            BranchRegex = new(@"^origin/(\d{4}\.\d+)$");
            Process = new()
            {
                StartInfo = new()
                {
                    CreateNoWindow = true,
                    FileName = "docfx",
                    Arguments = "metadata",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            Process.OutputDataReceived += (_, args) => Logger.Trace("[DocFX]" + args.Data, 1);
            Process.ErrorDataReceived += (_, args) =>
            {
                if (string.IsNullOrEmpty(args.Data)) return;

                Logger.Error($"[DocFX] {args.Data}");
            };
        }
        
        private static void Main(string[] args)
        {
            Match match;
            List<string> files = new();
            
            if (!Directory.Exists(UnityCsReferenceLocalPath))
            {
                Repository.Clone(UnityCsReferenceRepositoryUrl, UnityCsReferenceLocalPath);
            }

            using (Repository repo = new(UnityCsReferenceLocalPath))
            {
                foreach (Branch branch in repo.Branches.OrderByDescending(b => b.FriendlyName))
                {
                    match = BranchRegex.Match(branch.FriendlyName);

                    if (!match.Success) continue;

                    string version = match.Groups[1].Value;

                    if (args.Length > 0 && Array.IndexOf(args, version) == -1)
                    {
                        Logger.Warning($"Skipping '{branch.FriendlyName}'");
                        continue;
                    }

                    Logger.Info($"Checking out '{branch.FriendlyName}'");

                    Commands.Checkout(repo, branch);

                    repo.Reset(ResetMode.Hard);

                    int exitCode = RunDocFx();

                    if (exitCode != 0)
                    {
                        Logger.Error($"DocFX exited with code {exitCode}");
                        continue;
                    }

                    files.Add(GenerateMap(version));
                }
            }

            using (StreamWriter writer = new(Path.Join(OutputFolder, "index.html")))
            {
                Logger.Info("Writing index.html");

                writer.WriteLine("<html>\n<body>\n<ul>");

                foreach (string file in files)
                {
                    writer.WriteLine($"<li><a href=\"{file}\">{file}</a></li>");
                }

                writer.WriteLine("</ul>\n</body>\n</html>");
            }
        }

        private static int RunDocFx()
        {
            Logger.Info("Running DocFX");

            Process.Start();

            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();

            Process.WaitForExit();

            Process.CancelOutputRead();
            Process.CancelErrorRead();

            return Process.ExitCode;
        }

        private static string GenerateMap(string version)
        {
            YamlMappingNode reference;
            Logger.Info($"Generating XRef map for Unity {version}");

            References.Clear();

            foreach (string file in Directory.GetFiles(GeneratedMetadataPath, "*.yml"))
            {
                Logger.Trace($"Reading '{file}'", 1);

                using (TextReader reader = new StreamReader(file))
                {
                    if (reader.ReadLine() != "### YamlMime:ManagedReference") continue;

                    reference = Deserializer.Deserialize<YamlMappingNode>(reader);

                    foreach (YamlMappingNode item in (YamlSequenceNode)reference.Children["items"])
                    {
                        string apiUrl = $"https://docs.unity3d.com/{version}/Documentation/ScriptReference/";
                        string commentId = item.GetScalarValue("commentId");

                        if (commentId.Contains("Overload:")) continue;

                        XRefMapReference xRefMapReference = new()
                        {
                            Uid = item.GetScalarValue("uid"),
                            Name = item.GetScalarValue("name"),
                            CommentId = commentId,
                            FullName = item.GetScalarValue("fullName"),
                            NameWithType = item.GetScalarValue("nameWithType"),
                            Type = item.GetScalarValue("type")
                        };
                        
                        xRefMapReference.FixHref(apiUrl);
                        
                        Logger.Trace($"Adding reference to '{xRefMapReference.FullName}'", 2);
                        
                        References.Add(xRefMapReference);
                    }
                }
            }

            string serializedMap = Serializer.Serialize(new XRefMap
            {
                Sorted = true,
                References = References.OrderBy(r => r.Uid).ToArray()
            });

            string relativeOutputFilePath = Path.Join(version, "xrefmap.yml");
            string outputFilePath = Path.Join(OutputFolder, relativeOutputFilePath);

            Logger.Info($"Saving XRef map to '{outputFilePath}'");

            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath));

            File.WriteAllText(outputFilePath, $"### YamlMime:XRefMap\n{serializedMap}");

            return relativeOutputFilePath;
        }
    }
}
