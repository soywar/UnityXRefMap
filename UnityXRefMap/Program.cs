﻿using LibGit2Sharp;
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
        private static readonly string UnityCsReferenceRepositoryUrl = "https://github.com/Unity-Technologies/UnityCsReference";
        private static readonly string UnityCsReferenceLocalPath = Path.Join(Environment.CurrentDirectory, "UnityCsReference");
        private static readonly string GeneratedMetadataPath = Path.Join(Environment.CurrentDirectory, "ScriptReference");
        private static readonly string OutputFolder = Path.Join(Environment.CurrentDirectory, "out");

        private static void Main(string[] args)
        {
            if (!Directory.Exists(UnityCsReferenceLocalPath))
            {
                Repository.Clone(UnityCsReferenceRepositoryUrl, UnityCsReferenceLocalPath);
            }

            var files = new List<string>();

            using (var repo = new Repository(UnityCsReferenceLocalPath))
            {
                Regex branchRegex = new Regex(@"^origin/(\d{4}\.\d+)$");

                foreach (Branch branch in repo.Branches.OrderByDescending(b => b.FriendlyName))
                {
                    Match match = branchRegex.Match(branch.FriendlyName);

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

            using (var writer = new StreamWriter(Path.Join(OutputFolder, "index.html")))
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

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    FileName = "docfx",
                    Arguments = "metadata",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            process.OutputDataReceived += (sender, args) => Logger.Trace("[DocFX]" + args.Data, 1);
            process.ErrorDataReceived += (sender, args) =>
            {
                if (string.IsNullOrEmpty(args.Data)) return;

                Logger.Error("[DocFX]" + args.Data);
            };

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.WaitForExit();

            return process.ExitCode;
        }

        private static string GenerateMap(string version)
        {
            Logger.Info($"Generating XRef map for Unity {version}");

            var serializer = new Serializer();
            var deserializer = new Deserializer();

            var references = new List<XRefMapReference>();

            foreach (var file in Directory.GetFiles(GeneratedMetadataPath, "*.yml"))
            {
                Logger.Trace($"Reading '{file}'", 1);

                using (TextReader reader = new StreamReader(file))
                {
                    if (reader.ReadLine() != "### YamlMime:ManagedReference") continue;

                    YamlMappingNode reference = deserializer.Deserialize<YamlMappingNode>(reader);

                    foreach (YamlMappingNode item in (YamlSequenceNode)reference.Children["items"])
                    {
                        string apiUrl = $"https://docs.unity3d.com/{version}/Documentation/ScriptReference/";
                        string commentId = item.GetScalarValue("commentId");

                        if (commentId.Contains("Overload:")) continue;

                        XRefMapReference xRefMapReference = new XRefMapReference()
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
                        
                        references.Add(xRefMapReference);
                    }
                }
            }

            var serializedMap = serializer.Serialize(new XRefMap
            {
                Sorted = true,
                References = references.OrderBy(r => r.Uid).ToArray()
            });

            string relativeOutputFilePath = Path.Join(version, "xrefmap.yml");
            string outputFilePath = Path.Join(OutputFolder, relativeOutputFilePath);

            Logger.Info($"Saving XRef map to '{outputFilePath}'");

            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath));

            File.WriteAllText(outputFilePath, "### YamlMime:XRefMap\n" + serializedMap);

            return relativeOutputFilePath;
        }
    }
}
