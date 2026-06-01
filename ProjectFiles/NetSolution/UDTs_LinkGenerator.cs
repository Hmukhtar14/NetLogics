#region Using directives
using System;
using UAManagedCore;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.Core;
#endregion

public class UDTs_LinkGenerator : BaseNetLogic
{
    
    // 1. Define paths (Adjust these paths to match your exact project structure)
    private const string sourceFolderPath = "OPC-UA/Client_MainServer/Objects/OPCUA_TagSimulator/Model/UDT_Tags/Objects_Set1";

    private const string driverFolderPath = "CommDrivers/RAEtherNet_IPDriver1/ControlLogix5590/Tags/Controller Tags";

    [ExportMethod]
    public void GenerateDynamicLinks()
    {
        
        Log.Info("LinkGenerator", "Starting Dynamic Link generation...");

        // 2. Resolve target folders
        var sourceFolder = Project.Current.Get<Folder>(sourceFolderPath);
        var driverFolder = Project.Current.Get<Folder>(driverFolderPath);

        if (sourceFolder == null)
        {
            Log.Error("LinkGenerator", $"Source folder not found: {sourceFolderPath}");
            return;
        }

        if (driverFolder == null)
        {
            Log.Error("LinkGenerator", $"Driver folder not found: {driverFolderPath}");
            return;
        }

        int linksCreated = 0;

        // 3. Iterate through each Object inside the Source folder (e.g., Object0, Object1)
        foreach (var sourceObject in sourceFolder.Children)
        {
            // Find the matching object inside the Comms Driver by matching the BrowseName
            var driverObject = driverFolder.Get(sourceObject.BrowseName);
            if (driverObject == null)
            {
                Log.Warning("LinkGenerator", $"Matching driver object not found for: {sourceObject.BrowseName}");
                continue;
            }

            // 4. Iterate through variables expected under each object (Float1, Float2, Bool1, Bool2)
            foreach (var sourceVariableNode in sourceObject.Children)
            {
                // Verify it's a variable node
                if (sourceVariableNode is not IUAVariable sourceVar) continue;

                // Look for the matching variable inside the PLC driver object structure
                var driverVar = driverObject.Get<IUAVariable>(sourceVar.BrowseName);
                if (driverVar == null)
                {
                    Log.Warning("LinkGenerator", $"Driver variable '{sourceVar.BrowseName}' not found under '{driverObject.BrowseName}'");
                    continue;
                }

                // 5. Native FactoryTalk Optix Link assignment
                // Using ReadWrite mode establishes a bidirectional dynamic link to the driver variable
                sourceVar.SetDynamicLink(driverVar, FTOptix.CoreBase.DynamicLinkMode.ReadWrite);

                linksCreated++;
            }
        }

        Log.Info("LinkGenerator", $"Success! Created/Updated {linksCreated} dynamic links.");
    }


    [ExportMethod]
    public void ClearAllDynamicLinks()
    {
        Log.Info("LinkGenerator", $"Starting deletion of dynamic links under: {sourceFolderPath}");

        var sourceFolder = Project.Current.Get<Folder>(sourceFolderPath);
        if (sourceFolder == null)
        {
            Log.Error("LinkGenerator", $"Target folder not found: {sourceFolderPath}");
            return;
        }

        int linksCleared = 0;
        
        // Execute the recursive cleanup starting at the root folder node
        RecursiveClearLinks(sourceFolder, ref linksCleared);

        Log.Info("LinkGenerator", $"Cleanup complete. Removed {linksCleared} dynamic links.");
    }

    private void RecursiveClearLinks(IUANode node, ref int count)
    {
        if (node == null) return;

        // If the node is a variable, check for the internal framework reference link
        if (node is IUAVariable variable)
        {
            var dynamicLinkRef = variable.Refs.GetVariable(FTOptix.CoreBase.ReferenceTypes.HasDynamicLink);
            if (dynamicLinkRef != null)
            {
                variable.ResetDynamicLink();
                count++;
            }
        }

        // Drill down to ensure child groupings and nested structures are cleared too
        foreach (var child in node.Children)
        {
            RecursiveClearLinks(child, ref count);
        }
    }
}
