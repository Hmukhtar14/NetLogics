#region Using directives
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NativeUI;
using FTOptix.RAEtherNetIP;
using FTOptix.Retentivity;
using FTOptix.CoreBase;
using FTOptix.CommunicationDriver;
using FTOptix.Core;
using FTOptix.NetLogic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using FTOptix.Store;
using YamlDotNet.Core.Tokens;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
#endregion

/// <summary>
/// TagImporterOptimizer - Analyzes imported tags and identifies which ones are actually being used in the HMI project.
/// This helps clean up unused tags and generate reports for better project management.
/// </summary>
public class TagImporterOptimizer : BaseNetLogic
{
    // ===== GLOBAL VARIABLES =====
    // These lists track all the tags we find throughout the analysis process
    
    /// <summary>Holds ALL tags that were imported from the PLC YAML file</summary>
    private List<string> allImportedTags = new List<string>();
    
    /// <summary>Holds tags that are actually referenced/used in the Model YAML files</summary>
    private List<string> usedTagsModel = new List<string>();
    
    /// <summary>Holds tags that are actually referenced/used in the UI YAML files</summary>
    private List<string> usedTagsUI = new List<string>();
    
    /// <summary>Holds tags that were imported but are NOT used anywhere (candidates for deletion)</summary>
    private List<string> tagsToBeDeleted = new List<string>();
    
    /// <summary>Tracks tag substitutions: maps original {Tag} reference to the substituted value</summary>
    private List<(string Original, string Substituted)> tagSubstitutions = new List<(string, string)>();
    
    /// <summary>Path to the Nodes folder where all YAML files are stored</summary>
    private string nodesFolderPath = Project.Current.ProjectDirectory.Replace("ProjectFiles", "Nodes");
    


    /// <summary>
    /// Main entry point for the tag optimization process. This is called from the UI.
    /// It orchestrates the entire workflow: get imported tags -> get used tags -> compare and delete unused ones
    /// </summary>
    [ExportMethod]
    public void OptimizeImportedTags()
    {
        // Step 1: Grab the configuration variables that tell us what to analyze
        // These are set in the UI as dropdown selections

        // Get the Communication Driver (like "EtherNet/IP" or "Modbus")
        var targetCommDriverNodeId = LogicObject.GetVariable("TargetCommDriver").Value;
        if (targetCommDriverNodeId.Value == null)        {
            Log.Error("Error: TargetCommDriver variable is not set or is null.");
            return;
        }
        // Extract the driver name and replace spaces with underscores (for file path compatibility)
        string targetCommDriver = InformationModel.Get<CommunicationDriver>(targetCommDriverNodeId).BrowseName.Replace(" ", "_");
        
        
        // Get the PLC/Station (the specific device we're analyzing, like "Charlotte_Line_01")
        var targetStationNodeId = LogicObject.GetVariable("TargetStation").Value;
        if (targetStationNodeId.Value == null)        { 
            Log.Error("Error: TargetStation variable is not set or is null.");
            return;
        }
        // Extract the station name and clean it up for file paths
        string targetStation = InformationModel.Get<Station>(targetStationNodeId).BrowseName.Replace(" ", "_");


        // Get the specific Tags Folder to analyze (like "Valves", "Motors", etc.)
        var targetTagsFolderNodeId = LogicObject.GetVariable("TargetTagsFolder").Value;
        if (targetTagsFolderNodeId.Value == null)        
        {
            Log.Error("Error: TargetTagsFolder variable is not set or is null.");
            return;
        }
        // Clean up the folder name for file path usage
        var targetTagsFolder = InformationModel.Get<Folder>(targetTagsFolderNodeId).BrowseName.Replace(" ", "_");

        
        // DEBUG: Uncomment these to see what values we extracted
        //Log.Info(nodesFolderPath);
        //Log.Info($"TargetCommDriver: {targetCommDriver}, TargetStation: {targetStation}, TargetTagsFolder: {targetTagsFolder}");
        
        // Step 2: Run the analysis in the background so the UI doesn't freeze
        // This is important because we're dealing with large YAML files
        Task.Run(() =>
        {
            // === STAGE 1: Get all imported tags from the source YAML file ===
            Log.Info("Stage 1: Getting all imported tags...");
            GetAllImportedTags(targetCommDriver, targetStation, targetTagsFolder);
            
            // Optional: Small delay to ensure file writing completes before moving on
            //Thread.Sleep(3000);
                
            // === STAGE 2: Find which tags are actually used in the project ===
            Log.Info("Stage 2: Getting all used tags...");
            GetAllUsedTags();
            
            // === STAGE 3: Compare and delete unused tags ===
            // (Currently disabled - enable when you're ready to actually delete tags!)
            Log.Info("Stage 3: Comparing tags and deleting unused ones...");
            //CompareTagsForDeletion();
            
            // Done! All reports and logs have been written
            Log.Info("Optimization complete.");
        });
    }



#region Get all the Imported Tags 
#endregion

    /// <summary>
    /// Reads the PLC's YAML file and extracts ALL tag names that were imported.
    /// This gives us the complete list of what's available to use.
    /// </summary>
    private void GetAllImportedTags(string driver, string station, string tagsFolder)
    {
        Log.Info("Starting GetAllImportedTags method execution...");

        // Clear out any old data so we start fresh
        allImportedTags.Clear();
        
        try
        {
            // Build the file path to the PLC's YAML file
            // Example: /Nodes/CommDrivers/EtherNet_IP/PLC_01/Tags/Valves/Valves.yaml
            string yamlFilePath = nodesFolderPath + "/CommDrivers/" + driver + "/" + station + "/Tags/" + tagsFolder + "/" + tagsFolder + ".yaml";
            
            // Make sure the file actually exists before we try to read it
            if (string.IsNullOrEmpty(yamlFilePath) || !System.IO.File.Exists(yamlFilePath))
            {
                Log.Error("Error: YAML file not found at path: " + yamlFilePath);
                return;
            }

            // Parse the YAML file and extract all tag names
            // This method builds our 'allImportedTags' list and tracks which ones we skipped
            ParseYamlTags(yamlFilePath, out int totalTags, out int excludedTags, out List<string> excludedTagPaths);

            // Write the accepted tags to a report file (for reference and debugging)
            string outputFilePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(yamlFilePath), System.IO.Path.GetFileNameWithoutExtension(yamlFilePath) + "_accepted_tags.txt");
            System.IO.File.WriteAllLines(outputFilePath, allImportedTags);
            Log.Info($"Successfully wrote {allImportedTags.Count} tags to: {outputFilePath}");

            // Also write out the tags we excluded (BlockRead, SymbolName, @Alarms, etc.)
            string excludedOutputFilePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(yamlFilePath), System.IO.Path.GetFileNameWithoutExtension(yamlFilePath) + "_excluded_tags.txt");
            System.IO.File.WriteAllLines(excludedOutputFilePath, excludedTagPaths);
            Log.Info($"Successfully wrote {excludedTagPaths.Count} excluded tags to: {excludedOutputFilePath}");

            // Also mirror accepted/not accepted lists into the shared reports folder
            string baseReportDir = @"C:\Users\hmukhtar\OneDrive - Van Meter Inc\Desktop\Esco - Cargill Optix Project\Tag Import Optimizer - Reports";
            if (!System.IO.Directory.Exists(baseReportDir))
            {
                System.IO.Directory.CreateDirectory(baseReportDir);
            }

            string importedTagsReportDir = System.IO.Path.Combine(baseReportDir, "ImportedTags");
            if (!System.IO.Directory.Exists(importedTagsReportDir))
            {
                System.IO.Directory.CreateDirectory(importedTagsReportDir);
            }

            string acceptedReportPath = System.IO.Path.Combine(importedTagsReportDir, $"AcceptedTags_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            System.IO.File.WriteAllLines(acceptedReportPath, allImportedTags.OrderBy(tag => tag));
            Log.Info($"Successfully wrote {allImportedTags.Count} accepted tags to reports folder: {acceptedReportPath}");

            string notAcceptedReportPath = System.IO.Path.Combine(importedTagsReportDir, $"NotAcceptedTags_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            System.IO.File.WriteAllLines(notAcceptedReportPath, excludedTagPaths.OrderBy(tag => tag));
            Log.Info($"Successfully wrote {excludedTagPaths.Count} not accepted tags to reports folder: {notAcceptedReportPath}");
            
            // Summary of what we found
            Log.Info($"Parsed {totalTags} tag entries; excluded {excludedTags}; accepted {allImportedTags.Count} tags.");

            
        }
        catch (Exception ex)
        {
            Log.Error($"Error in GetAllImportedTags: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses YAML file line-by-line to extract tag names.
    /// We use a stack-based approach to track the hierarchy/indentation of tags.
    /// This way we can build full paths like "Motor/Speed/Current"
    /// </summary>
    private void ParseYamlTags(string filePath, out int totalTags, out int excludedTags, out List<string> excludedTagPaths)
    {
        // Stack to track the hierarchy of tags as we go through the file
        // Each entry stores: (indentation level, tag name)
        var pathStack = new List<(int Indent, string Name)>();
        
        // Regex to find lines with "Name: SomeTagName"
        // This matches both list items (- Name:) and regular properties (Name:)
        var nameRegex = new Regex(@"^(\s*)(?:-\s*)?Name:\s*(.+)$", RegexOptions.Compiled);

        // Initialize output counters
        totalTags = 0;
        excludedTags = 0;
        excludedTagPaths = new List<string>();

        // Read through the file line-by-line
        foreach (var line in System.IO.File.ReadLines(filePath))
        {
            // Skip empty lines
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Try to match this line as a tag name
            var match = nameRegex.Match(line);
            if (!match.Success)
                continue;  // Not a tag name, skip it

            // Count all tags we encounter
            totalTags++;
            
            // Extract the tag name from the regex match
            string tagName = match.Groups[2].Value.Trim();

            // Figure out how indented this line is (determines hierarchy level)
            int currentIndent = match.Groups[1].Value.Length;

            // Pop the stack so we're back at the right hierarchy level for this tag
            // If a parent's indentation is >= current indentation, it's no longer our parent
            while (pathStack.Count > 0 && pathStack[pathStack.Count - 1].Indent >= currentIndent)
            {
                pathStack.RemoveAt(pathStack.Count - 1);
            }

            // Build the full path to this tag
            // Example: if we've seen Motor, then Speed within Motor, the path is "Motor/Speed"
            string currentPath = pathStack.Count > 0
                ? $"{string.Join("/", pathStack.Select(item => item.Name))}/{tagName}"
                : tagName;

            // Skip certain tags that we don't want to track
            // BlockRead and SymbolName are internal PLC variables, @Alarms are alarm-related items
            if (string.Equals(tagName, "BlockRead", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tagName, "SymbolName", StringComparison.OrdinalIgnoreCase) ||
                tagName.Contains("@Alarms", StringComparison.OrdinalIgnoreCase) ||
                currentPath.Contains("@Alarms", StringComparison.OrdinalIgnoreCase))
            {
                // This tag is excluded, track it separately
                excludedTags++;
                excludedTagPaths.Add(currentPath);
                // Still add to stack so children of excluded tags are processed correctly
                pathStack.Add((currentIndent, tagName));
                continue;
            }

            // This is a good tag we want to keep track of
            allImportedTags.Add(currentPath);
            // Add it to our stack as a potential parent for future tags
            pathStack.Add((currentIndent, tagName));
        }
    }




#region Get All the Used Tags 
#endregion
    /// <summary>
    /// Searches through all YAML files in the Model and UI folders to find which imported tags are actually being used.
    /// This is the core of the analysis - we need to find tag references in DynamicLinks.
    /// </summary>
    private void GetAllUsedTags()
    {
        // Set up the paths where we'll search for tag usage
        // We look in Model (for business logic) and UI (for screens) folders
        string modelFolderNodesPath = nodesFolderPath + "/Model"; 
        string uiFolderNodesPath = nodesFolderPath + "/UI";

        // Clear previous results so we start fresh
        usedTagsModel.Clear();
        usedTagsUI.Clear();
        tagSubstitutions.Clear();  // Clear substitutions from previous runs

        try
        {
            // Define a helper function that we'll use for both Model and UI folders
            // This function handles the complex logic of finding {Tag} references and substituting them
            void ProcessYamlFiles(IEnumerable<string> yamlFiles, List<string> tagList, string folderType, List<(string, string)> substitutionsList)
            {
                // Process each YAML file one at a time
                foreach (var file in yamlFiles)
                {
                    try
                    {
                        // Read the entire file into memory so we can look back up the hierarchy
                        var lines = System.IO.File.ReadAllLines(file);
                        
                        // Create a map: line number -> the actual tag name that {Tag} refers to
                        // This will help us resolve {Tag} placeholders to actual tag values
                        var tagAliasMap = new Dictionary<int, string>();
                        
                        // Step 1: Scan through the file to find all Tag aliases and their DynamicLink values
                        // Example: if we find Name: Tag with a DynamicLink Value pointing to "V640TA_EV",
                        // we map that line to "V640TA_EV"
                        for (int i = 0; i < lines.Length; i++)
                        {
                            // Look for a line that defines a "Tag" property
                            if (lines[i].TrimStart().StartsWith("- Name: Tag") || lines[i].TrimStart().StartsWith("Name: Tag"))
                            {
                                // Search the next ~10 lines to confirm this is a Tag Alias (not just any property named Tag)
                                int j = i + 1;
                                bool isAlias = false;
                                while (j < lines.Length && (j - i) <= 10)
                                {
                                    // Check if this is marked as an Alias type
                                    if (lines[j].TrimStart().StartsWith("Type: Alias"))
                                    {
                                        isAlias = true;
                                    }
                                    // If it's an alias, look for its DynamicLink
                                    if (isAlias && (lines[j].TrimStart().StartsWith("- Name: DynamicLink") || lines[j].TrimStart().StartsWith("Name: DynamicLink")))
                                    {
                                        // Found the DynamicLink, now find its Value
                                        int k = j + 1;
                                        while (k < lines.Length && (k - j) <= 5)
                                        {
                                            var valLine = lines[k].TrimStart();
                                            if (valLine.StartsWith("Value:"))
                                            {
                                                // Extract the value (e.g., "/Objects/Model/Devices/V640TA_EV")
                                                var value = valLine.Substring("Value:".Length).Trim().Trim('"');
                                                // Get just the LAST part after the last / (e.g., "V640TA_EV")
                                                // This is what we'll use to replace {Tag}
                                                var lastSegment = value.Split('/').LastOrDefault();
                                                if (!string.IsNullOrEmpty(lastSegment))
                                                {
                                                    // Map this line number to the tag name
                                                    tagAliasMap[i] = lastSegment;
                                                }
                                                break;
                                            }
                                            k++;
                                        }
                                        break;
                                    }
                                    j++;
                                }
                            }
                        }

                        // Step 2: Now scan through again looking for {Tag} references
                        // When we find one, we'll substitute it with the actual tag name from our map
                        for (int i = 0; i < lines.Length; i++)
                        {
                            var line = lines[i];
                            // Does this line contain a {Tag} placeholder?
                            if (line.Contains("{Tag}"))
                            {
                                // Look backwards to find the nearest Tag alias that applies to this reference
                                // (The Tag defined closest above this line in the hierarchy)
                                string aliasValue = null;
                                int search = i;
                                while (search >= 0)
                                {
                                    if (tagAliasMap.ContainsKey(search))
                                    {
                                        // Found it! This is the Tag alias we should use
                                        aliasValue = tagAliasMap[search];
                                        break;
                                    }
                                    search--;
                                }
                                if (!string.IsNullOrEmpty(aliasValue))
                                {
                                    // Good! We found an alias value. Now substitute it.
                                    // Example: "{Tag}/Device/&@AlarmSet/InAlarmUnackedCount" becomes "V640TA_EV/Device/&@AlarmSet/InAlarmUnackedCount"
                                    var substituted = line.Replace("{Tag}", aliasValue);
                                    
                                    // Extract the tag path from this line
                                    // We look for the Value: field which contains the actual tag reference
                                    var idx = substituted.IndexOf("Value:");
                                    if (idx >= 0)
                                    {
                                        // Pull out the value and clean it up (remove quotes, whitespace)
                                        var tagPath = substituted.Substring(idx + "Value:".Length).Trim().Trim('"');
                                        // Add it to our list if we haven't seen it before
                                        if (!tagList.Contains(tagPath))
                                            tagList.Add(tagPath);
                                        
                                        // Also track the substitution: original -> substituted
                                        // Extract the original value from the unsubstituted line
                                        var originalIdx = line.IndexOf("Value:");
                                        if (originalIdx >= 0)
                                        {
                                            var originalValue = line.Substring(originalIdx + "Value:".Length).Trim().Trim('"');
                                            // Record this substitution (original format with {Tag} -> new format with alias)
                                            if (!string.IsNullOrEmpty(originalValue) && originalValue != tagPath)
                                            {
                                                substitutionsList.Add((originalValue, tagPath));
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // Step 3: Also check for direct tag references (tags that don't use {Tag})
                        // This is a fallback to catch any tags that are hardcoded
                        CheckUsedTagsInYaml(file, tagList, folderType);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Error processing YAML file {file}: {ex.Message}");
                    }
                }
            }

            // Process all Model YAML files (unless they're alarm-related)
            if (System.IO.Directory.Exists(modelFolderNodesPath))
            {
                // Get all .yaml files recursively, but skip AlarmWidgets (those have their own tag system)
                var modelYamlFiles = System.IO.Directory.GetFiles(modelFolderNodesPath, "*.yaml", System.IO.SearchOption.AllDirectories)
                    .Where(path => !path.Contains("/AlarmWidgets/"));
                ProcessYamlFiles(modelYamlFiles, usedTagsModel, "Model", tagSubstitutions);
            }

            // Process all UI YAML files (unless they're keyboards)
            if (System.IO.Directory.Exists(uiFolderNodesPath))
            {
                // Get all .yaml files recursively, but skip Keyboards (those have hardcoded layouts)
                var uiYamlFiles = System.IO.Directory.GetFiles(uiFolderNodesPath, "*.yaml", System.IO.SearchOption.AllDirectories)
                    .Where(path => !path.Contains("/Keyboards/"));
                ProcessYamlFiles(uiYamlFiles, usedTagsUI, "UI", tagSubstitutions);
            }

            // Report what we discovered
            Log.Info($"Collected {usedTagsModel.Count} used tags from Model folder.");
            Log.Info($"Collected {usedTagsUI.Count} used tags from UI folder.");
            int totalUsedTags = usedTagsModel.Count + usedTagsUI.Count;
            Log.Info($"Total used tags found: {totalUsedTags}");

            // Write all discovered tags to a report file for easy review
            WriteUsedTagsReport();
            
            // Write all tag substitutions to a separate report
            WriteTagSubstitutionsReport();
        }
        catch (Exception ex)
        {
            Log.Error($"Error in GetAllUsedTags: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates a report file containing all the used tags we found.
    /// This report helps you see which tags are actually referenced in your HMI project.
    /// </summary>
    private void WriteUsedTagsReport()
    {
        try
        {
            // Create a reports directory where we'll store all our analysis results
            string reportDir = @"C:\Users\hmukhtar\OneDrive - Van Meter Inc\Desktop\Esco - Cargill Optix Project\Tag Import Optimizer - Reports";
            
            // Make sure the reports directory exists
            if (!System.IO.Directory.Exists(reportDir))
            {
                System.IO.Directory.CreateDirectory(reportDir);
            }
            
            // Combine tags from both Model and UI
            var allUsedTags = new List<string>(usedTagsModel);
            allUsedTags.AddRange(usedTagsUI);
            
            // Remove any duplicates and sort alphabetically for easier reading
            var uniqueUsedTags = allUsedTags.Distinct().OrderBy(t => t).ToList();
            
            // Create a timestamped report file so each run has its own output
            string reportFilePath = System.IO.Path.Combine(reportDir, $"UsedTags_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            System.IO.File.WriteAllLines(reportFilePath, uniqueUsedTags);
            Log.Info($"Wrote {uniqueUsedTags.Count} unique used tags to: {reportFilePath}");
        }
        catch (Exception ex)
        {
            Log.Error($"Error writing used tags report: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes all tag substitutions (original {Tag} references and their substituted values) to a report file.
    /// This helps track which tags used alias substitution and what they became.
    /// Report goes to a "TagSubstitutions" subdirectory within the main reports folder.
    /// </summary>
    private void WriteTagSubstitutionsReport()
    {
        try
        {
            // Create the base reports directory if needed
            string baseReportDir = @"C:\Users\hmukhtar\OneDrive - Van Meter Inc\Desktop\Esco - Cargill Optix Project\Tag Import Optimizer - Reports";
            if (!System.IO.Directory.Exists(baseReportDir))
            {
                System.IO.Directory.CreateDirectory(baseReportDir);
            }
            
            // Create a subdirectory for substitutions
            string substitutionDir = System.IO.Path.Combine(baseReportDir, "TagSubstitutions");
            if (!System.IO.Directory.Exists(substitutionDir))
            {
                System.IO.Directory.CreateDirectory(substitutionDir);
            }
            
            // If we have substitutions to report, write them
            if (tagSubstitutions.Count > 0)
            {
                // Create formatted output: show original and substituted side by side
                var formattedLines = new List<string>();
                formattedLines.Add("TAG SUBSTITUTIONS REPORT");
                formattedLines.Add("=".PadRight(100, '='));
                formattedLines.Add($"Original Format (with {{Tag}})\t|\tSubstituted Format (with alias)");
                formattedLines.Add("-".PadRight(100, '-'));
                
                // Add each substitution
                var uniqueSubstitutions = tagSubstitutions.Distinct().OrderBy(s => s.Original).ToList();
                foreach (var (original, substituted) in uniqueSubstitutions)
                {
                    formattedLines.Add($"{original}\t|\t{substituted}");
                }
                
                // Write to timestamped file
                string reportFilePath = System.IO.Path.Combine(substitutionDir, $"TagSubstitutions_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                System.IO.File.WriteAllLines(reportFilePath, formattedLines);
                Log.Info($"Wrote {uniqueSubstitutions.Count} tag substitutions to: {reportFilePath}");
            }
            else
            {
                Log.Info("No tag substitutions found (no {Tag} references were processed).");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error writing tag substitutions report: {ex.Message}");
        }
    }

    /// <summary>
    /// Looks for any imported tags that are referenced directly (without {Tag} substitution).
    /// This is a fallback method to catch hardcoded tag references.
    /// </summary>
    private void CheckUsedTagsInYaml(string filePath, List<string> tagList, string folderType)
    {
        try
        {
            // Read the entire file as one big string so we can search for tag names
            string content = System.IO.File.ReadAllText(filePath);
            var tagHashSet = new HashSet<string>(tagList);
            int foundCount = 0;
            
            // Check each imported tag to see if it's mentioned in this file
            foreach (var tag in allImportedTags)
            {
                // If the file contains this tag AND we haven't already added it, add it now
                if (content.Contains(tag) && !tagHashSet.Contains(tag))
                {
                    tagList.Add(tag);
                    tagHashSet.Add(tag);
                    foundCount++;
                }
            }
            
            // Log how many tags we found in this file
            if (foundCount > 0)
            {
                Log.Info($"Added {foundCount} used tags from {Path.GetFileName(filePath)}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error checking YAML file {filePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Compares imported tags vs used tags to figure out which ones can be deleted.
    /// A tag is a candidate for deletion if:
    /// 1. It's not directly referenced anywhere
    /// 2. None of its child tags are referenced
    /// </summary>
    private void CompareTagsForDeletion()
    {
        tagsToBeDeleted.Clear();
        
        // Combine all tags we found being used
        var allUsedTags = new HashSet<string>(usedTagsModel.Concat(usedTagsUI));

        // Check each imported tag
        foreach (var tag in allImportedTags)
        {
            // Is this tag directly used?
            bool isUsed = allUsedTags.Contains(tag);
            
            // Do any of this tag's children get used? (e.g., if Motor isn't used but Motor/Speed is)
            bool hasUsedDescendants = allImportedTags.Any(t => t.StartsWith(tag + "/") && allUsedTags.Contains(t));
            
            // If it's not used AND has no used children, it's safe to delete
            if (!isUsed && !hasUsedDescendants)
            {
                tagsToBeDeleted.Add(tag);
            }
        }

        // Report our findings
        Log.Info($"Found {tagsToBeDeleted.Count} tags to be deleted out of {allImportedTags.Count} imported tags.");

        // If we found unused tags, proceed with rewriting the YAML file to remove them
        if (tagsToBeDeleted.Count > 0)
        {
            // Reconstruct the path to the original YAML file so we can modify it
            string yamlFilePath = nodesFolderPath + "/CommDrivers/" + InformationModel.Get<CommunicationDriver>(LogicObject.GetVariable("TargetCommDriver").Value).BrowseName.Replace(" ", "_") + "/" +
                InformationModel.Get<Station>(LogicObject.GetVariable("TargetStation").Value).BrowseName.Replace(" ", "_") + "/Tags/" +
                InformationModel.Get<Folder>(LogicObject.GetVariable("TargetTagsFolder").Value).BrowseName.Replace(" ", "_") + "/" +
                InformationModel.Get<Folder>(LogicObject.GetVariable("TargetTagsFolder").Value).BrowseName.Replace(" ", "_") + ".yaml";
            
            // Rewrite the YAML file, filtering out the unused tags
            RewriteYamlExcludingTags(yamlFilePath, new HashSet<string>(tagsToBeDeleted));
        }
    }

    /// <summary>
    /// Rewrites the YAML file, removing all tags that were identified as unused.
    /// This is a destructive operation - it actually modifies the source YAML file!
    /// We use indentation levels to identify which lines belong to a tagged block.
    /// </summary>
    private void RewriteYamlExcludingTags(string filePath, HashSet<string> tagsToExclude)
    {
        try
        {
            // Load the entire file
            var lines = System.IO.File.ReadAllLines(filePath).ToList();
            var outputLines = new List<string>();  // This will hold the cleaned-up lines
            var pathStack = new List<(int Indent, string Name)>();  // Track the hierarchy as we go
            var nameRegex = new Regex(@"^(\s*)(?:-\s*)?Name:\s*(.+)$", RegexOptions.Compiled);
            
            // Flag to track when we're inside a block that needs to be deleted
            bool skipBlock = false;
            int skipIndent = -1;  // The indentation level of the tag we're skipping

            // Process each line
            foreach (var line in lines)
            {
                // Skip empty lines ONLY if we're in a skip block (otherwise keep them for formatting)
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (!skipBlock) outputLines.Add(line);
                    continue;
                }

                // Try to parse this line as a tag name
                var match = nameRegex.Match(line);
                if (match.Success)
                {
                    string tagName = match.Groups[2].Value.Trim();
                    int currentIndent = match.Groups[1].Value.Length;

                    // Update our hierarchy stack to match current indentation
                    while (pathStack.Count > 0 && pathStack[pathStack.Count - 1].Indent >= currentIndent)
                    {
                        pathStack.RemoveAt(pathStack.Count - 1);
                    }

                    // Build the full path to this tag
                    string currentPath = pathStack.Count > 0
                        ? $"{string.Join("/", pathStack.Select(item => item.Name))}/{tagName}"
                        : tagName;

                    // Is this a tag we want to delete?
                    if (tagsToExclude.Contains(currentPath))
                    {
                        // Start skipping - don't add this line or anything indented under it
                        skipBlock = true;
                        skipIndent = currentIndent;
                    }
                    else
                    {
                        // Check if we were skipping but this line is at a lower indent (back to parent level)
                        if (skipBlock && currentIndent <= skipIndent)
                        {
                            skipBlock = false;
                        }
                        // Only add the line if we're not currently skipping a block
                        if (!skipBlock)
                        {
                            outputLines.Add(line);
                            pathStack.Add((currentIndent, tagName));
                        }
                    }
                }
                else
                {
                    // This line doesn't have a Name field, it's a property or value line
                    // Check if we need to stop skipping based on indentation
                    int lineIndent = line.Length - line.TrimStart().Length;
                    if (skipBlock && lineIndent <= skipIndent)
                    {
                        // We've reached a lower or equal indent - we're back at parent level, stop skipping
                        skipBlock = false;
                    }
                    // Add the line if we're not currently in a skip block
                    if (!skipBlock)
                    {
                        outputLines.Add(line);
                    }
                }
            }

            // Write the filtered lines back to the file, replacing the original
            System.IO.File.WriteAllLines(filePath, outputLines);
            Log.Info($"Rewrote YAML file, excluding {tagsToExclude.Count} unused tags.");
        }
        catch (Exception ex)
        {
            Log.Error($"Error rewriting YAML file {filePath}: {ex.Message}");
        }
    }

}




 
