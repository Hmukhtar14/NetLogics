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
#endregion

public class TagImporterOptimizer : BaseNetLogic
{

    // Define all the global variables we will need for this NetLogic Object
    private List<string> allImportedTags = new List<string>();
    private List<string> tagsToBedeleted = new List<string>();
    private List<string> allYamlFilesFound = new List<string>();

    private string nodesFolderPath = Project.Current.ProjectDirectory.Replace("ProjectFiles", "Nodes");
    


    [ExportMethod]
    public void OptimizeImportedTags()
    {
        // Get all the variables from the Properties of this NetLogic Object

        // Communication Driver
        var targetCommDriverNodeId = LogicObject.GetVariable("TargetCommDriver").Value;
        if (targetCommDriverNodeId.Value == null)        {
            Log.Error("Error: TargetCommDriver variable is not set or is null.");
            return;
        }
        string targetCommDriver = InformationModel.Get<CommunicationDriver>(targetCommDriverNodeId).BrowseName.Replace(" ", "_");
        
        
        // PLC/Station
        var targetStationNodeId = LogicObject.GetVariable("TargetStation").Value;
        if (targetStationNodeId.Value == null)        { 
            Log.Error("Error: TargetStation variable is not set or is null.");
            return;
        }
        string targetStation = InformationModel.Get<Station>(targetStationNodeId).BrowseName.Replace(" ", "_");


        // Tags Folder
        var targetTagsFolderNodeId = LogicObject.GetVariable("TargetTagsFolder").Value;
        if (targetTagsFolderNodeId.Value == null)        
        {
            Log.Error("Error: TargetTagsFolder variable is not set or is null.");
            return;
        }
        var targetTagsFolder = InformationModel.Get<Folder>(targetTagsFolderNodeId).BrowseName.Replace(" ", "_");

        
        Log.Info(nodesFolderPath);
        // Log the retrieved values to the output for debugging 
        //Log.Info($"TargetCommDriver: {targetCommDriver}, TargetStation: {targetStation}, TargetTagsFolder: {targetTagsFolder}");
        
        GetAllImportedTags(targetCommDriver, targetStation, targetTagsFolder);
        GetAllUsedTags();
    }

#region Get all the Imported Tags 
#endregion

    // Two Layer Search (One UDT)
    private void GetAllImportedTags(string driver, string station, string tagsFolder)
    {
        // Print start of method execution to the output
        Log.Info("Starting GetAllImportedTags method execution...");

        // Empty out the List prior to populating it
        allImportedTags.Clear();
        
        try
        {
            // Get the yaml file path from the NetLogic properties
            string yamlFilePath = nodesFolderPath + "/CommDrivers/" + driver + "/" + station + "/Tags/" + tagsFolder + "/" + tagsFolder + ".yaml";
            
            // Check the yaml file path
            if (string.IsNullOrEmpty(yamlFilePath) || !System.IO.File.Exists(yamlFilePath))
            {
                Log.Error("Error: YAML file not found at path: " + yamlFilePath);
                return;
            }

            // Read the YAML file
            string yamlContent = System.IO.File.ReadAllText(yamlFilePath);

            // Deserialize YAML
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var data = deserializer.Deserialize<Dictionary<object, object>>(yamlContent);
            
            // Extract all tags and their children
            // If the data dicitonary has elements in it, continue with the filetrs
            if (data != null)
            {
                // If the 
                if (data.ContainsKey("Children"))
                {
                    var children = data["Children"] as List<object>;
                    if (children != null)
                    {
                        // First Layer - Loop through all the childeren elements of the Controller Tags FOlder
                        foreach (var tagItem in children)
                        {
                            if (tagItem is Dictionary<object, object> tagDict)
                            {
                                // Get tag name of the first/Parent tags
                                if (tagDict.ContainsKey("Name"))
                                {
                                    string tagName = tagDict["Name"].ToString();
                                    allImportedTags.Add(tagName);
                                    //Log.Info(tagName);

                                    // Process children of the tag 
                                    if (tagDict.ContainsKey("Children"))
                                    {
                                        var tagChildren = tagDict["Children"] as List<object>;
                                        if (tagChildren != null)
                                        {   
                                            // Second Layer - Loop through all the children of the parent tags
                                            foreach (var childItem in tagChildren)
                                            {
                                                if (childItem is Dictionary<object, object> childDict)
                                                {
                                                    if (childDict.ContainsKey("Name"))
                                                    {
                                                        string childName = childDict["Name"].ToString();
                                                        
                                                        // drop the BlockRead and SybolName Tags
                                                        if (childName != "BlockRead" && childName != "SymbolName" )
                                                        {
                                                            // Add in format: Tag1.Temp, Tag1.Speed, etc.
                                                            allImportedTags.Add($"{tagName}/{childName}");

                                                            Log.Info($"Found tag element: {tagName}.{childName}");   
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            Log.Info($"Successfully loaded {allImportedTags.Count} tags and tag elements");

        }
        catch (Exception ex)
        {
            Log.Error($"Error in GetAllImportedTags: {ex.Message}");
        }
    }

#region Get All the Used Tags 
#endregion
    private void GetAllUsedTags()
    {
        // Print start of method execution to the output
        Log.Info("Starting GetAllUsedTags method execution...");

        // Initialize the list of tags to be deleted
        tagsToBedeleted.Clear();
        allYamlFilesFound.Clear();
        
        try
        {
            // Get the folder path where UI/HMI files are located (typically the ProjectFiles folder)
            string searchFolderPath = nodesFolderPath+ "/UI";
            
            if (string.IsNullOrEmpty(searchFolderPath) || !System.IO.Directory.Exists(searchFolderPath))
            {
                Log.Error("Error: Search folder not found at path: " + searchFolderPath);
                return;
            }

            Log.Info($"Recursively searching for YAML files in: {searchFolderPath}");

            // Recursively search for all YAML files
            RecursiveSearchForYamlFiles(searchFolderPath);

            Log.Info($"Found {allYamlFilesFound.Count} YAML files to analyze");

            // Track which tags are actually used
            HashSet<string> usedTags = new HashSet<string>();

            // Parse each YAML file and check for tag usage
            foreach (var yamlFile in allYamlFilesFound)
            {
                try
                {
                    string yamlContent = System.IO.File.ReadAllText(yamlFile);
                    
                    // Check if any of the imported tags are referenced in this file
                    foreach (var tag in allImportedTags)
                    {
                        // Search for the tag as a complete match, not as a substring
                        // Look for the tag surrounded by non-word characters or quotes
                        if (IsTagReferenced(yamlContent, tag))
                        {
                            usedTags.Add(tag);
                            Log.Info($"Tag '{tag}' found in use at: {yamlFile}");
                        }
                    }
                }
                catch (Exception fileEx)
                {
                    Log.Error($"Error reading YAML file {yamlFile}: {fileEx.Message}");
                }
            }

            // Identify unused tags - tags that are in allImportedTags but not in usedTags
            foreach (var importedTag in allImportedTags)
            {
                if (!usedTags.Contains(importedTag.Replace(".", "/"))) // Also check for the slash version of the tag
                {
                    tagsToBedeleted.Add(importedTag);
                    Log.Info($"Unused tag identified: {importedTag}");
                }
            }

            Log.Info($"Analysis complete. Found {tagsToBedeleted.Count} unused tags out of {allImportedTags.Count} imported tags");
        }
        catch (Exception ex)
        {
            Log.Error($"Error in GetAllUsedTags: {ex.Message}");
        }
    }

    private void RecursiveSearchForYamlFiles(string folderPath)
    {
        try
        {
            // Get all YAML files in the current directory
            string[] yamlFiles = System.IO.Directory.GetFiles(folderPath, "*.yaml", System.IO.SearchOption.TopDirectoryOnly);
            
            foreach (var yamlFile in yamlFiles)
            {
                // Exclude the Controller_Tags YAML file itself from the search
                if (!yamlFile.Contains("Controller_Tags"))
                {
                    allYamlFilesFound.Add(yamlFile);
                    Log.Info($"Found YAML file: {yamlFile}");
                }
            }

            // Get all subdirectories and recursively search them
            string[] subdirectories = System.IO.Directory.GetDirectories(folderPath);
            
            foreach (var subdir in subdirectories)
            {
                // Skip certain system folders
                string folderName = System.IO.Path.GetFileName(subdir);
                if (!folderName.Equals("bin") && !folderName.Equals("obj") && !folderName.Equals(".git"))
                {
                    RecursiveSearchForYamlFiles(subdir);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error in RecursiveSearchForYamlFiles: {ex.Message}");
        }
    }

    private bool IsTagReferenced(string content, string tag)
    {
        // Use regex to find the tag as a complete token
        // Also search for the tag with forward slashes (Tag1/Temp) since YAML files use that format
        
        // Build the slash-based version: Tag1.Temp -> Tag1/Temp
        string tagWithSlash = tag.Replace(".", "/");
        
        // Match the tag when it's surrounded by word boundaries or specific delimiters
        System.Text.RegularExpressions.Regex regexWithDot = new System.Text.RegularExpressions.Regex(
            @"([""\'\s:\-\[\],]|^)" + System.Text.RegularExpressions.Regex.Escape(tag) + @"([""\'\s:\-\[\],]|$)"
        );
        
        System.Text.RegularExpressions.Regex regexWithSlash = new System.Text.RegularExpressions.Regex(
            @"([""\'\s:\-\[\],]|^)" + System.Text.RegularExpressions.Regex.Escape(tagWithSlash) + @"([""\'\s:\-\[\],]|$)"
        );
        
        return regexWithDot.IsMatch(content) || regexWithSlash.IsMatch(content);
    }

#region Check If Tag is Used 
#endregion
    private void CheckIfTagIsUsed()
    {
        
    }

#region Delete Unused Tags 
#endregion
    private void DeleteTag()
    {
        
    }

    private void RecursiveSearch()
    {
        
    }

} 
