using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using FTOptix.Core;
using FTOptix.HMIProject;
using FTOptix.MicroController;
using FTOptix.NetLogic;
using UAManagedCore;
using FTOptix.AuditSigning;
using OpcUa = UAManagedCore.OpcUa;

public class RSLogix500_TagImporter : BaseNetLogic
{
    private LongRunningTask tagsImportTask;

    [ExportMethod]
    public void ImportTagsFromCSV()
    {
        tagsImportTask?.Dispose();
        tagsImportTask = new LongRunningTask(RSLogix500TagsImporter, LogicObject);
        tagsImportTask.Start();
    }


    // Check if the variables exist

    private void RSLogix500TagsImporter()
    {
        
        // Read the CSV path from the CsvPath variable and convert to a string
        var csvPathVariable = LogicObject.GetVariable("CsvPath");
        string csvPath = new ResourceUri(csvPathVariable.Value).Uri;;

        if (string.IsNullOrEmpty(csvPath))
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, $"Unable to retreive the CSV file path. Please assign the 'CsvPath' variable a valid file path.");
            return;
        }

        if (!File.Exists(csvPath))
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, $"CSV file not found. Please make sure the file exist at the specified path.");
            return;
        }

        // Get the MicroContrller Driver info
        var myDriver = InformationModel.Get<FTOptix.MicroController.Driver>(LogicObject.GetVariable("MicroControllerDriver").Value);
        if (myDriver == null)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, "Please assign the 'MicroControllerDriver' variable to the a Comm Driver.");
            return;
        }

        var myStation = InformationModel.Get<FTOptix.MicroController.Station>(LogicObject.GetVariable("MicroControllerStation").Value);
        // Get the path for the Tags folder in which tags will be created (under the comm driver)
        var tagsFolder = myDriver.Children.Get<IUANode>(myStation.BrowseName).Children.Get<IUANode>("Tags");

        // Read the CSV file and import the tags
        int importedTags = 0;
        using var reader = new StreamReader(csvPath);
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            // Skip empty line
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Data in the csv files are coma seperated.
            var columns = line.Split(',');
            if (columns.Length == 0)
                continue;

            // Get the first column data without white space and assign it to the Symbol
            var symbol = columns[0].Trim();
           
            // GEt the second column data wihtout white spaces and assign it to Description
            var symbolDescription = columns[3].Trim();

            // Skip the DFILE columns (are they needed?)
            if (symbol.StartsWith("DFILE:"))
                continue;

            // Create tag name by replacing : and / with underscroes and strip the ""
            var tagName = symbol.Replace(':', '_').Replace('/', '_').Replace('"', ' ').Replace(' ', '_');

            try
            {
                // Map symbol/address to an OPCUA/OPtix datatype
                var tagType = MapSymbolToDataType(symbol);

                // Create tag and set symbol name
                var microTag = InformationModel.MakeVariable<FTOptix.MicroController.Tag>(tagName, tagType);
                if (microTag == null)
                {
                    Log.Warning(MethodBase.GetCurrentMethod().Name, $"Failed to create tag variable for '{tagName}'. Skipping it...");
                    continue;
                }

                // Assign the Micro tag to the symbol and description
                microTag.SymbolName = symbol;

                var descriptionVar = InformationModel.MakeVariable("Description", OpcUa.DataTypes.String);
                descriptionVar.Value = symbolDescription.Replace('"', ' ').Trim();
                microTag.Add(descriptionVar);


                // Create folders for datatypes
                IUANode parentFolder = tagsFolder;

                // Based on the letter the symbol start with, create the folder names with the Datafile name
                if (symbol.StartsWith("S"))
                {
                    var statusFolder = GetOrCreateFolder(tagsFolder, "StatusFile");
                    if (statusFolder != null)
                        parentFolder = statusFolder;
                }
                else if (symbol.StartsWith("I"))
                {
                    var inputFolder = GetOrCreateFolder(tagsFolder, "InputFile");
                    if (inputFolder != null)
                        parentFolder = inputFolder;
                }
                else if (symbol.StartsWith("O"))
                {
                    var outputFolder = GetOrCreateFolder(tagsFolder, "OutputFile");
                    if (outputFolder != null)
                        parentFolder = outputFolder;
                }

                if (symbol.StartsWith("B"))
                {
                    var boolFolder = GetOrCreateFolder(tagsFolder, "BoolFile");
                    if (boolFolder != null)
                        parentFolder = boolFolder;
                }
                else if (symbol.StartsWith("T"))
                {
                    var timerFolder = GetOrCreateFolder(tagsFolder, "TimerFile");
                    if (timerFolder != null)
                        parentFolder = timerFolder;
                }
                else if (symbol.StartsWith("C"))
                {
                    var counterFolder = GetOrCreateFolder(tagsFolder, "CounterFile");
                    if (counterFolder != null)
                        parentFolder = counterFolder;
                }
                else if (symbol.StartsWith("N"))
                {
                    var intFolder = GetOrCreateFolder(tagsFolder, "IntegerFile");
                    if (intFolder != null)
                        parentFolder = intFolder;
                }
                else if (symbol.StartsWith("F"))
                {
                    var floatFolder = GetOrCreateFolder(tagsFolder, "FloatFile");
                    if (floatFolder != null)
                        parentFolder = floatFolder;
                }

                // Before adding the tag, check if it already exists in the resolved parent folder
                try
                {
                    var existing = parentFolder?.Children.Get<IUANode>(tagName);
                    if (existing != null)
                    {
                        Log.Info(MethodBase.GetCurrentMethod().Name, $"Tag '{tagName}' already exists in folder '{parentFolder.BrowseName}'. Skipping.");
                        continue;
                    }

                    // Add the micro tag to the parent folder and count it
                    parentFolder.Add(microTag);
                    importedTags++;
                }
                catch (Exception addEx)
                {
                    Log.Error(MethodBase.GetCurrentMethod().Name, $"Failed to add tag '{tagName}' to folder: {addEx.Message}");
                    continue;
                }
            }
            catch (Exception innerEx)
            {
                Log.Error(MethodBase.GetCurrentMethod().Name, $"Error creating tag '{tagName}' for symbol '{symbol}': {innerEx.Message}");
            }
        }

        Log.Info(MethodBase.GetCurrentMethod().Name, $"Successfully imported {importedTags} tag(s) from CSV: {csvPath}");
        
    }

    // Get or create a folder with the datafile name
    private IUANode GetOrCreateFolder(IUANode parent, string folderName)
    {
        if (parent == null || string.IsNullOrEmpty(folderName))
            return null;

        try
        {
            // If the folder exist, return it
            var existingFolder = parent.Children.Get<IUANode>(folderName);
            if (existingFolder != null)
            {
                return existingFolder;
            }

            // otherwise, create a new folder
            else
            {
                // Create folder
                var folder = InformationModel.Make<FTOptix.Core.Folder>(folderName);
                parent.Add(folder);
                return folder;
            }
        }
        catch (Exception e)
        {
            Log.Debug(MethodBase.GetCurrentMethod().Name, $"GetOrCreateFolder failed for {folderName}: {e.Message}");
            return null;
        }
    }


    private static NodeId MapSymbolToDataType(string symbol)
    {
        // For any empty or Null datatype default to a BOOL
        if (string.IsNullOrEmpty(symbol))
            return OpcUa.DataTypes.Boolean;

        var mySymbol = symbol.Trim();

        // SLC/MicroLogix data types to OPCUA data types 
        if (mySymbol.StartsWith("I"))
            return OpcUa.DataTypes.Boolean;
        if (mySymbol.StartsWith("O"))
            return OpcUa.DataTypes.Boolean;
        if (mySymbol.StartsWith("B"))
            return OpcUa.DataTypes.Boolean;
        if (mySymbol.StartsWith("S"))
            return OpcUa.DataTypes.Boolean;
        if (mySymbol.StartsWith("F"))
            return OpcUa.DataTypes.Float;
        if (mySymbol.StartsWith("N"))
            return OpcUa.DataTypes.Int16;

        // What should this be? A timer/Counter is a 32-bit value in SLC/MicroLogix
        if (mySymbol.StartsWith("T"))
            return OpcUa.DataTypes.Int32;
        if (mySymbol.StartsWith("C"))
            return OpcUa.DataTypes.Int32;

        // Default to a Bool
        return OpcUa.DataTypes.Boolean;
    }
}
