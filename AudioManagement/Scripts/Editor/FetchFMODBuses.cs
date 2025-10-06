#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using Debug = UnityEngine.Debug;
using INITFLAGS = FMOD.Studio.INITFLAGS;

namespace Lumenfish.AudioManagement.Editor
{
    /// <summary>
    /// Provides functionality to automate fetching and parsing of FMOD buses from FMOD Studio bank files.
    /// This class is designed to integrate FMOD audio systems with Unity by generating C# source code
    /// that lists available FMOD buses as enums for simplified access within the Unity editor.
    /// </summary>
    public static class FetchFMODBuses
    {
        /// <summary>
        /// Retrieves FMOD buses from the specified bank directory, processes the bank files to extract bus paths,
        /// and generates a C# source file containing definitions for the retrieved buses. The method ensures the
        /// generated file is saved in an auto-created directory within the Unity project, and subsequently imports
        /// the file into the Unity editor as an asset. Displays dialog messages for notification and error reporting.
        /// </summary>
        [MenuItem("Lumenfish/Fetch FMOD Buses")]
        public static void Fetch()
        {
            try
            {
                string bankRootDirectory = ResolveBankRoot();
                if (!Directory.Exists(bankRootDirectory))
                {
                    EditorUtility.DisplayDialog("FMOD", $"Bank folder not found:\n{bankRootDirectory}", "OK");
                    return;
                }

                var createResult = FMOD.Studio.System.create(out var studioSystem);
                if (createResult != RESULT.OK) throw new Exception("FMOD Studio System.create failed: " + createResult);

                studioSystem.getCoreSystem(out FMOD.System coreSystem);
                coreSystem.setOutput(OUTPUTTYPE.NOSOUND_NRT);

                var initResult = studioSystem.initialize(64, INITFLAGS.NORMAL, FMOD.INITFLAGS.NORMAL,
                    IntPtr.Zero);
                if (initResult != RESULT.OK) throw new Exception("FMOD Studio initialize failed: " + initResult);

                var loadedBanksList = new List<Bank>();
                try
                {
                    var allBankFiles = Directory.GetFiles(bankRootDirectory, "*.bank", SearchOption.TopDirectoryOnly)
                        .ToList();

                    var stringsBankFiles = allBankFiles
                        .Where(path => path.EndsWith(".strings.bank", StringComparison.OrdinalIgnoreCase)).ToList();
                    var regularBankFiles = allBankFiles
                        .Where(path => !path.EndsWith(".strings.bank", StringComparison.OrdinalIgnoreCase)).ToList();
                    var orderedBankFiles = stringsBankFiles.Concat(regularBankFiles);

                    foreach (var bankFilePath in orderedBankFiles)
                    {
                        var loadResult = studioSystem.loadBankFile(bankFilePath, LOAD_BANK_FLAGS.NORMAL,
                            out Bank bankInstance);
                        if (loadResult is RESULT.OK or RESULT.ERR_EVENT_ALREADY_LOADED)
                        {
                            if (bankInstance.isValid()) loadedBanksList.Add(bankInstance);
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"[FMOD] Failed to load bank: {Path.GetFileName(bankFilePath)} => {loadResult}");
                        }
                    }

                    // Collect Bus objects from all loaded banks
                    var busList = new List<Bus>();
                    foreach (var bank in loadedBanksList.Where(b => b.isValid()))
                    {
                        bank.getBusCount(out int busCount);
                        if (busCount == 0) continue;

                        bank.getBusList(out var buses);
                        if (buses != null && buses.Length > 0)
                        {
                            busList.AddRange(buses.Where(b => b.isValid()));
                        }
                    }

                    // Extract unique "bus:/" paths
                    var uniqueBusPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var validBusPaths = new List<string>();
                    foreach (var bus in busList)
                    {
                        bus.getPath(out string path);
                        if (string.IsNullOrEmpty(path)) continue;
                        if (!path.StartsWith("bus:/", StringComparison.OrdinalIgnoreCase)) continue;

                        // Some FMOD projects may expose the root master as "bus:/" (rare). Keep it if present.
                        if (uniqueBusPaths.Add(path)) validBusPaths.Add(path);
                    }

                    if (validBusPaths.Count == 0)
                    {
                        EditorUtility.DisplayDialog("FMOD",
                            "No buses found. Make sure banks are built for the correct platform.", "OK");
                        return;
                    }

                    var busItems = BuildUniqueIdentifiers(validBusPaths);

                    var outputDirectory = "Assets/Game/Domains/AudioManagement/Generated";
                    Directory.CreateDirectory(outputDirectory);
                    var outputFilePath = Path.Combine(outputDirectory, "FmodBuses.g.cs");

                    var sourceCode = GenerateSourceCode(busItems);
                    File.WriteAllText(outputFilePath, sourceCode.ToString(), Encoding.UTF8);
                    AssetDatabase.ImportAsset(outputFilePath);

                    EditorUtility.DisplayDialog("FMOD",
                        $"Generated enum and database:\n{outputFilePath}\n\n{busItems.Count} buses found.",
                        "OK");
                }
                finally
                {
                    foreach (var bank in loadedBanksList.Where(b => b.isValid()))
                        bank.unload();

                    studioSystem.release();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("FMOD", "Error: " + exception.Message, "OK");
            }
        }

        /// <summary>
        /// Generates a C# source file containing an enumeration of FMOD bus identifiers and their associated paths,
        /// and a static database class to map each identifier to its corresponding FMOD bus path.
        /// The generated source code is intended to be used for accessing FMOD buses programmatically.
        /// </summary>
        /// <param name="items">A list of tuples containing FMOD bus identifiers and their corresponding bus paths.</param>
        /// <returns>A StringBuilder object containing the generated source code, ready to be saved to a file.</returns>
        private static StringBuilder GenerateSourceCode(List<(string Identifier, string BusPath)> items)
        {
            var code = new StringBuilder();
            code.AppendLine("// <auto-generated> Lumenfish FMOD codegen (Buses) </auto-generated>");
            code.AppendLine("using System.Collections.Generic;");
            code.AppendLine();
            code.AppendLine("namespace Lumenfish.Audio");
            code.AppendLine("{");
            code.AppendLine("    public enum FmodBusId");
            code.AppendLine("    {");
            foreach (var item in items) code.AppendLine($"        {item.Identifier},");
            code.AppendLine("    }");
            code.AppendLine();
            code.AppendLine("    public static class FmodBusDatabase");
            code.AppendLine("    {");
            code.AppendLine("        private static readonly Dictionary<FmodBusId, string> _map = new()");
            code.AppendLine("        {");
            foreach (var item in items)
                code.AppendLine($"            [FmodBusId.{item.Identifier}] = \"{item.BusPath}\",");
            code.AppendLine("        };");
            code.AppendLine();
            code.AppendLine("        public static string GetPath(FmodBusId id) => _map[id];");
            code.AppendLine("    }");
            code.AppendLine("}");
            return code;
        }

        /// <summary>
        /// Resolves the root directory for FMOD bank files based on the project configuration and platform-specific settings.
        /// Attempts to locate the most appropriate directory where FMOD bank files are stored by checking several potential directories,
        /// including platform-specific paths, the source bank path, and user-selected directories. If no suitable directory is found,
        /// prompts the user to select a bank directory or throws a <see cref="DirectoryNotFoundException"/> if the process fails.
        /// </summary>
        /// <returns>
        /// A string representing the full path to the directory containing FMOD bank files.
        /// </returns>
        private static string ResolveBankRoot()
        {
            var fmodSettings = Settings.Instance;
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            var sourceBankPath = fmodSettings.SourceBankPath;
            if (!Path.IsPathRooted(sourceBankPath))
                sourceBankPath = Path.GetFullPath(Path.Combine(projectRoot, sourceBankPath));

            var platformFolder = GuessPlatformFolder(EditorUserBuildSettings.activeBuildTarget);
            var platformSpecificPath = Path.Combine(sourceBankPath, platformFolder);
            if (Directory.Exists(platformSpecificPath) && Directory.GetFiles(platformSpecificPath, "*.bank").Any())
                return platformSpecificPath;

            if (Directory.Exists(sourceBankPath) && Directory.GetFiles(sourceBankPath, "*.bank").Any())
                return sourceBankPath;

            var bestMatchFolder =
                FindBestBankFolder(projectRoot, new[] { "Library", "Temp", "Obj", "Logs", "Packages" });
            if (bestMatchFolder != null) return bestMatchFolder;

            var userSelectedFolder = EditorUtility.OpenFolderPanel("Select FMOD bank folder", projectRoot, "");
            if (!string.IsNullOrEmpty(userSelectedFolder)) return userSelectedFolder;

            throw new DirectoryNotFoundException("FMOD bank folder not found.");
        }

        /// <summary>
        /// Determines the appropriate folder name for FMOD bank files based on the given build target.
        /// This allows the application to locate platform-specific FMOD banks during runtime or build processes.
        /// </summary>
        /// <param name="target">The active build target for which the folder is being determined.</param>
        /// <returns>A string representing the folder name corresponding to the specified build target.</returns>
        private static string GuessPlatformFolder(BuildTarget target)
        {
            return target switch
            {
                BuildTarget.WebGL => "WebGL",
                _ => "Desktop"
            };
        }

        /// <summary>
        /// Searches for the most suitable FMOD bank folder within the specified root directory,
        /// excluding certain directories. The method examines subdirectories to find the folder
        /// containing the highest number of FMOD bank files, aiming to identify the best candidate
        /// for FMOD integration.
        /// </summary>
        /// <param name="rootDirectory">The root directory to begin the search for FMOD bank folders.</param>
        /// <param name="excludedDirectories">A collection of directory names to exclude from the search.</param>
        /// <returns>
        /// The path to the directory containing the highest number of FMOD bank files. Returns null
        /// if no valid folder is found.
        /// </returns>
        private static string FindBestBankFolder(string rootDirectory, IEnumerable<string> excludedDirectories)
        {
            var maxBankCount = 0;
            string bestBankPath = null;
            var excludedDirs = new HashSet<string>(excludedDirectories ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            var directoryStack = new Stack<string>();
            directoryStack.Push(rootDirectory);

            while (directoryStack.Count > 0)
            {
                var currentDirectory = directoryStack.Pop();
                var directoryName = Path.GetFileName(currentDirectory);
                if (excludedDirs.Contains(directoryName)) continue;

                var bankCount = 0;
                try
                {
                    bankCount = Directory.GetFiles(currentDirectory, "*.bank", SearchOption.TopDirectoryOnly).Length;
                }
                catch
                {
                    /* ignored */
                }

                if (bankCount > maxBankCount)
                {
                    maxBankCount = bankCount;
                    bestBankPath = currentDirectory;
                }

                try
                {
                    foreach (var subdirectory in Directory.GetDirectories(currentDirectory))
                        directoryStack.Push(subdirectory);
                }
                catch
                {
                    /* ignored */
                }
            }

            return maxBankCount > 0 ? bestBankPath : null;
        }

        /// <summary>
        /// Converts a given string into Pascal-case format, where the first character of each word is capitalized,
        /// and all non-alphanumeric characters are removed. Words are identified based on transitions from
        /// non-alphanumeric characters to alphanumeric ones. If the resulting string is empty or begins with
        /// a digit, a prefix is added to ensure validity as a C# identifier.
        /// </summary>
        /// <param name="raw">The input string to be converted into a Pascal-case token.</param>
        /// <returns>A Pascal-case formatted string, guaranteed to be a valid C# identifier.</returns>
        private static string ToPascalToken(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            var newWord = true;
            foreach (var ch in raw)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(newWord ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
                    newWord = false;
                }
                else
                {
                    newWord = true;
                }
            }

            var s = sb.ToString();
            if (string.IsNullOrEmpty(s)) s = "Bus";
            if (char.IsDigit(s[0])) s = "_" + s;
            return s;
        }

        /// <summary>
        /// Constructs a concatenated PascalCase string based on a specified number of segments
        /// from the end of the provided list of string segments. Each segment is converted into
        /// a PascalCase token before being merged into the resulting string.
        /// </summary>
        /// <param name="segments">A read-only list of string segments used to construct the name.</param>
        /// <param name="takeFromEndCount">The number of segments to take from the end of the list for name construction.</param>
        /// <returns>
        /// A concatenated PascalCase string created from the specified number of segments.
        /// </returns>
        private static string MakeNameFromSegments(IReadOnlyList<string> segments, int takeFromEndCount)
        {
            var start = Math.Max(0, segments.Count - takeFromEndCount);
            var parts = new List<string>(takeFromEndCount);
            for (var i = start; i < segments.Count; i++)
                parts.Add(ToPascalToken(segments[i]));

            return string.Concat(parts);
        }

        /// <summary>
        /// Builds unique identifiers for a collection of FMOD bus paths, ensuring each identifier
        /// is distinct while maintaining a structured and hierarchical naming convention.
        /// Conflicts in identifiers are resolved by progressively deepening the naming algorithm
        /// until all paths are uniquely represented.
        /// </summary>
        /// <param name="busPaths">A collection of bus paths for which unique identifiers are to be generated.</param>
        /// <returns>A list of tuples where each tuple contains an identifier and its corresponding bus path.</returns>
        private static List<(string Identifier, string BusPath)> BuildUniqueIdentifiers(IEnumerable<string> busPaths)
        {
            if (busPaths == null) throw new ArgumentNullException(nameof(busPaths));

            var items = busPaths.Select(p =>
            {
                var segs = p.Substring("bus:/".Length).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                // If root master shows up as "bus:/", ensure we still have at least one segment to name.
                if (segs.Length == 0) segs = new[] { "Master" };
                return new { Path = p, Segs = segs.ToList().AsReadOnly() };
            }).ToList();

            var nameMap = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var names = new string[items.Count];

            for (int i = 0; i < items.Count; i++)
            {
                names[i] = MakeNameFromSegments(items[i].Segs, 1);
                if (!nameMap.TryGetValue(names[i], out var list)) nameMap[names[i]] = list = new List<int>();
                list.Add(i);
            }

            var maxDepth = items.Max(it => it.Segs.Count);
            for (var depth = 2; depth <= maxDepth; depth++)
            {
                var conflicts = nameMap.Where(kv => kv.Value.Count > 1).ToList();
                if (conflicts.Count == 0) break;

                var d = depth;
                foreach (var idx in from kv in conflicts
                         from idx in kv.Value
                         where d <= items[idx].Segs.Count
                         select idx)
                {
                    names[idx] = MakeNameFromSegments(items[idx].Segs, depth);
                }

                nameMap.Clear();
                for (var i = 0; i < items.Count; i++)
                {
                    if (!nameMap.TryGetValue(names[i], out var list)) nameMap[names[i]] = list = new List<int>();
                    list.Add(i);
                }
            }

            var finalUsed = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < items.Count; i++)
            {
                var baseName = names[i];
                var candidate = baseName;
                var n = 2;
                while (!finalUsed.Add(candidate))
                    candidate = baseName + "_" + n++;
                names[i] = candidate;
            }

            var result = new List<(string Identifier, string BusPath)>(items.Count);
            for (var i = 0; i < items.Count; i++)
                result.Add((names[i], items[i].Path));

            result.Sort((a, b) => string.Compare(a.Identifier, b.Identifier, StringComparison.Ordinal));
            return result;
        }
    }
}
#endif
