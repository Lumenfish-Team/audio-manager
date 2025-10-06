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
using Debug = UnityEngine.Debug;

namespace Lumenfish.AudioManagement.Editor
{
    public static class FetchFMODEvents
{
    /// <summary>
    /// Fetches FMOD events from the designated bank directory and generates a C# source code file
    /// defining the retrieved events. The method processes FMOD bank files, parses event paths,
    /// and outputs generated content into an auto-created directory in the Unity project.
    /// The generated file is subsequently imported into the Unity editor as an asset.
    /// If errors occur during execution, appropriate error dialogs are displayed in the Unity editor.
    /// </summary>
    /// <exception cref="System.Exception">
    /// Thrown when FMOD initialization or operation steps fail due to system errors or missing dependencies.
    /// </exception>
    /// <remarks>
    /// This method is intended to be run as a Unity Editor menu item under the path "Lumenfish/Fetch FMOD Events."
    /// </remarks>
    [MenuItem("Lumenfish/Fetch FMOD Events")]
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

            var initResult =
                studioSystem.initialize(64, FMOD.Studio.INITFLAGS.NORMAL, FMOD.INITFLAGS.NORMAL, IntPtr.Zero);
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
                    var loadResult =
                        studioSystem.loadBankFile(bankFilePath, LOAD_BANK_FLAGS.NORMAL, out Bank bankInstance);
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

                var eventDescriptions = new List<EventDescription>();
                foreach (var bankInstance in loadedBanksList.Where(bank => bank.isValid()))
                {
                    bankInstance.getEventCount(out var eventCount);
                    if (eventCount == 0) continue;

                    bankInstance.getEventList(out var eventArray);
                    eventDescriptions.AddRange(eventArray.Where(eventDesc => eventDesc.isValid()));
                }

                var uniqueEventPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var validEventPaths = new List<string>();
                foreach (var eventDescription in eventDescriptions)
                {
                    eventDescription.isSnapshot(out bool isSnapshot);
                    if (isSnapshot) continue;
                    eventDescription.getPath(out string eventPath);
                    if (string.IsNullOrEmpty(eventPath)) continue;
                    if (!eventPath.StartsWith("event:/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (uniqueEventPaths.Add(eventPath)) validEventPaths.Add(eventPath);
                }

                if (validEventPaths.Count == 0)
                {
                    EditorUtility.DisplayDialog("FMOD",
                        "No events found. Make sure banks are built for the correct platform.", "OK");
                    return;
                }

                var eventItems = BuildUniqueIdentifiers(validEventPaths);

                var outputDirectory = "Assets/Game/Domains/Audio/Generated";
                Directory.CreateDirectory(outputDirectory);
                var outputFilePath = Path.Combine(outputDirectory, "FmodEvents.g.cs");

                var sourceCode = GenerateSourceCode(eventItems);
                File.WriteAllText(outputFilePath, sourceCode.ToString(), Encoding.UTF8);
                AssetDatabase.ImportAsset(outputFilePath);

                EditorUtility.DisplayDialog("FMOD",
                    $"Generated enum and database:\n{outputFilePath}\n\n{eventItems.Count} events found.",
                    "OK");
            }
            finally
            {
                foreach (var bankInstance in loadedBanksList.Where(bank => bank.isValid()))
                {
                    bankInstance.unload();
                }

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
    /// Generates a C# source code file representing FMOD event identifiers and their corresponding paths.
    /// The method defines an enum for event identifiers and a static class for mapping these identifiers
    /// to event paths, using the provided list of event items.
    /// </summary>
    /// <param name="eventItems">
    /// A list of tuples where each tuple contains an identifier as a string and its associated FMOD event path as a string.
    /// </param>
    /// <returns>
    /// A <see cref="StringBuilder"/> containing the C# source code that defines the FMOD event identifiers and their mappings.
    /// </returns>
    private static StringBuilder GenerateSourceCode(List<(string Identifier, string EventPath)> eventItems)
    {
        var code = new StringBuilder();
        code.AppendLine("// <auto-generated> Lumenfish FMOD codegen </auto-generated>");
        code.AppendLine("using System.Collections.Generic;");
        code.AppendLine("");
        code.AppendLine("namespace Lumenfish.Audio");
        code.AppendLine("{");
        code.AppendLine("    public enum FmodEventId");
        code.AppendLine("    {");
        foreach (var item in eventItems) code.AppendLine($"        {item.Identifier},");
        code.AppendLine("    }");
        code.AppendLine("");
        code.AppendLine("    public static class FmodEventDatabase");
        code.AppendLine("    {");
        code.AppendLine("        private static readonly Dictionary<FmodEventId, string> _map = new()");
        code.AppendLine("        {");
        foreach (var item in eventItems)
            code.AppendLine($"            [FmodEventId.{item.Identifier}] = \"{item.EventPath}\",");
        code.AppendLine("        };");
        code.AppendLine("        public static string GetPath(FmodEventId id) => _map[id];");
        code.AppendLine("    }");
        code.AppendLine("}");
        return code;
    }

    /// <summary>
    /// Resolves the root directory for FMOD bank files based on the project's configuration, build target,
    /// and user-specified paths. The method attempts to locate the most appropriate folder containing FMOD
    /// bank files by examining platform-specific paths, project directories, and predefined exclusions.
    /// If a suitable directory isn't found, a folder selection dialog is presented to the user.
    /// </summary>
    /// <returns>
    /// A string representing the path to the identified FMOD bank root directory.
    /// </returns>
    /// <exception cref="DirectoryNotFoundException">
    /// Thrown when no valid FMOD bank folder is located and the user cancels the folder selection dialog.
    /// </exception>
    private static string ResolveBankRoot()
    {
        var fmodSettings = FMODUnity.Settings.Instance;
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

        var bestMatchFolder = FindBestBankFolder(projectRoot, new[] { "Library", "Temp", "Obj", "Logs", "Packages" });
        if (bestMatchFolder != null) return bestMatchFolder;

        var userSelectedFolder = EditorUtility.OpenFolderPanel("Select FMOD bank folder", projectRoot, "");
        if (!string.IsNullOrEmpty(userSelectedFolder)) return userSelectedFolder;

        throw new DirectoryNotFoundException("FMOD bank folder not found.");
    }

    /// <summary>
    /// Determines the appropriate platform-specific folder name for storing FMOD banks
    /// based on the specified build target.
    /// </summary>
    /// <param name="target">The target build platform for which to identify the folder name.</param>
    /// <returns>The platform-specific folder name as a string.</returns>
    private static string GuessPlatformFolder(BuildTarget target)
    {
        return target switch
        {
            BuildTarget.WebGL => "WebGL",
            _ => "Desktop"
        };
    }

    /// <summary>
    /// Identifies the directory containing the most FMOD bank files within a given root directory.
    /// This method performs a recursive search while excluding specified directories, evaluates
    /// the number of bank files within each directory, and determines the best match.
    /// </summary>
    /// <param name="rootDirectory">The root directory to begin the search for FMOD bank files.</param>
    /// <param name="excludedDirectories">
    /// A list of directory names to be excluded from the search. Matching directories and their subdirectories are ignored.
    /// </param>
    /// <returns>
    /// The path to the directory with the highest number of FMOD bank files. Returns null if no suitable directory is found.
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
                // ignored
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
                // ignored
            }
        }

        return maxBankCount > 0 ? bestBankPath : null;
    }

    /// <summary>
    /// Converts the given string into a PascalCase formatted token.
    /// This method removes non-alphanumeric characters, ensures each word starts
    /// with an uppercase letter, and concatenates them into a single string.
    /// If the resulting string is empty, a default value of "Evt" is returned.
    /// A leading underscore is added if the resulting string starts with a numeric character.
    /// </summary>
    /// <param name="raw">The raw input string to be converted into a PascalCase token.</param>
    /// <returns>A PascalCase formatted string derived from the input. Defaults to "Evt" or prepends
    /// an underscore if the result starts with a number.</returns>
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
        if (string.IsNullOrEmpty(s)) s = "Evt";
        if (char.IsDigit(s[0])) s = "_" + s;
        return s;
    }

    /// <summary>
    /// Generates a concatenated PascalCase formatted name derived from the last specified number
    /// of segments in the provided list. The method processes the input segments by converting
    /// each into PascalCase format and combining them into a single string.
    /// </summary>
    /// <param name="segments">A read-only list of string segments to generate a name from. Each segment is processed individually.</param>
    /// <param name="takeFromEndCount">The number of segments to take from the end of the list for use in name generation. This determines the range of segments processed.</param>
    /// <returns>A single PascalCase formatted string created by concatenating the processed segments. Returns an empty string if no segments are processed.</returns>
    private static string MakeNameFromSegments(IReadOnlyList<string> segments, int takeFromEndCount)
    {
        var start = Math.Max(0, segments.Count - takeFromEndCount);
        var parts = new List<string>(takeFromEndCount);
        for (var i = start; i < segments.Count; i++)
            parts.Add(ToPascalToken(segments[i]));

        return string.Concat(parts);
    }

    /// <summary>
    /// Builds a list of unique identifiers for FMOD event paths by transforming each event path
    /// into a unique and valid C# identifier. This process ensures identifier uniqueness and resolves
    /// naming conflicts by progressively appending additional segments of the event path or
    /// numerically suffixing identifiers to eliminate duplicates.
    /// </summary>
    /// <param name="eventPaths">An enumerable collection of FMOD event paths to be processed into unique identifiers.</param>
    /// <returns>A list of tuples where each tuple contains a unique C# identifier and its corresponding event path.</returns>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when the <paramref name="eventPaths"/> parameter is null.
    /// </exception>
    private static List<(string Identifier, string EventPath)> BuildUniqueIdentifiers(IEnumerable<string> eventPaths)
    {
        var items = eventPaths.Select(p =>
        {
            var segs = p.Substring("event:/".Length).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
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

            var depth1 = depth;
            foreach (var idx in from kv in conflicts
                     from idx in kv.Value
                     where depth1 <= items[idx].Segs.Count
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

        var result = new List<(string Identifier, string EventPath)>(items.Count);
        for (var i = 0; i < items.Count; i++)
            result.Add((names[i], items[i].Path));

        result.Sort((a, b) => string.Compare(a.Identifier, b.Identifier, StringComparison.Ordinal));
        return result;
    }
}
}

#endif