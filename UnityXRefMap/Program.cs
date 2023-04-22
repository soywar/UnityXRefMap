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
        private static readonly Process ProcessExecuteDocFX;
        private static readonly Process ProcessInstallDocFX;
        private static readonly Process ProcessUpgradeDocFX;
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
            BranchRegex = new(@"^origin/(\d{4})\.(\d+)$");
            ProcessExecuteDocFX = new()
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

            ProcessExecuteDocFX.OutputDataReceived += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data)) return;
                Logger.Trace($"[DocFX] {args.Data}", 1);
            };
            ProcessExecuteDocFX.ErrorDataReceived += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data)) return;
                Logger.Error($"[DocFX] {args.Data}");
            };
            
            ProcessInstallDocFX = new()
            {
                StartInfo = new()
                {
                    CreateNoWindow = true,
                    FileName = "dotnet",
                    Arguments = "tool install -g docfx --version 2.61.0",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            ProcessInstallDocFX.OutputDataReceived += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data)) return;
                Logger.Info($"[DocFX] {args.Data}");
            };
            ProcessInstallDocFX.ErrorDataReceived += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data)) return;
                Logger.Error($"[DocFX] {args.Data}");
            };
            
            ProcessUpgradeDocFX = new()
            {
                StartInfo = new()
                {
                    CreateNoWindow = true,
                    FileName = "dotnet",
                    Arguments = "tool update -g docfx",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            ProcessUpgradeDocFX.OutputDataReceived += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data)) return;
                Logger.Info($"[DocFX] {args.Data}");
            };
            ProcessUpgradeDocFX.ErrorDataReceived += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data)) return;
                Logger.Error($"[DocFX] {args.Data}");
            };
        }
        
        private static void Main(string[] args)
        {
            Match match;
            int exitCode;
            string version;
            int majorVersion;
            int minorVersion;
            List<string> files = new();
            bool deprecatedVersion = true;
            
            exitCode = RunProcess(ProcessInstallDocFX);

            if (exitCode != 0)
            {
                throw new ($"DotNet exited with code {exitCode}");
            }
            
            if (!Directory.Exists(UnityCsReferenceLocalPath))
            {
                Repository.Clone(UnityCsReferenceRepositoryUrl, UnityCsReferenceLocalPath);
            }

            using (Repository repo = new(UnityCsReferenceLocalPath))
            {
                foreach (Branch branch in repo.Branches.OrderBy(b => b.FriendlyName))
                {
                    match = BranchRegex.Match(branch.FriendlyName);
                    
                    if (!match.Success) continue;

                    majorVersion = int.Parse(match.Groups[1].Value);
                    minorVersion = int.Parse(match.Groups[2].Value);
                    version = $"{majorVersion}.{minorVersion}";

                    if (args.Length > 0 && Array.IndexOf(args, version) == -1)
                    {
                        Logger.Warning($"Skipping '{branch.FriendlyName}'");
                        continue;
                    }

                    Logger.Info($"Checking out '{branch.FriendlyName}'");

                    Commands.Checkout(repo, branch);

                    repo.Reset(ResetMode.Hard);
                    
                    if (deprecatedVersion && majorVersion >= 2022)
                    {
                        deprecatedVersion = false;
                        
                        exitCode = RunProcess(ProcessUpgradeDocFX);

                        if (exitCode != 0)
                        {
                            throw new($"DotNet exited with code {exitCode}");
                        }
                    }
                    
                    exitCode = RunProcess(ProcessExecuteDocFX);

                    if (exitCode != 0)
                    {
                        Logger.Error($"DocFX exited with code {exitCode}");
                        continue;
                    }

                    files.Add(GenerateMap(version));
                }
            }
        }

        private static int RunProcess(Process process)
        {
            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.WaitForExit();

            process.CancelOutputRead();
            process.CancelErrorRead();

            return process.ExitCode;
        }

        private static string GenerateMap(string version)
        {
            YamlMappingNode reference;
            Logger.Info($"Generating XRef Map for Unity {version}");

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

            Logger.Info($"Saving XRef Map to '{outputFilePath}'");

            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath));

            File.WriteAllText(outputFilePath, $"### YamlMime:XRefMap\n{serializedMap}");

            return relativeOutputFilePath;
        }
    }
}
