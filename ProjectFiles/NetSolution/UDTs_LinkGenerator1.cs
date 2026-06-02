#region Using directives
using System;
using UAManagedCore;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.Core;
using FTOptix.CoreBase; // Required for DynamicLinkMode validation
#endregion

public class UDTs_LinkGenerator1 : BaseNetLogic
{
    // Renamed uniquely to resolve any CS0229 field ambiguity
    private const string TargetSourcePath = "OPC-UA/Client_MainServer/Objects/OPCUA_TagSimulator/Model/UDT_Tags/Objects_Set1";
    private const string TargetDriverPath = "CommDrivers/RAEtherNet_IPDriver1/ControlLogix5580/Tags/Controller Tags";

    [ExportMethod]
    public void GenerateDynamicLinks()
    {
        Log.Info("LinkGenerator", "Starting Dynamic Link generation...");

        // Resolve folders natively
        var sourceFolder = Project.Current.Get<Folder>(TargetSourcePath);
        var driverFolder = Project.Current.Get<Folder>(TargetDriverPath);

        if (sourceFolder == null)
        {
            Log.Error("LinkGenerator", $"OPC UA Source folder not found: {TargetSourcePath}");
            return;
        }

        if (driverFolder == null)
        {
            Log.Error("LinkGenerator", $"PLC Driver folder not found: {TargetDriverPath}");
            return;
        }

        int linksCreated = 0;

        // Iterate through each Object folder inside the PLC driver structure (e.g., Object0, Object1)
        foreach (var driverObject in driverFolder.Children)
        {
            // Find the matching source object folder inside the OPC UA Client node tree
            var sourceObject = sourceFolder.Get(driverObject.BrowseName);
            if (sourceObject == null)
            {
                Log.Warning("LinkGenerator", $"Matching OPC UA Client object folder not found for PLC object: {driverObject.BrowseName}");
                continue;
            }

            // Iterate through the variables inside the PLC driver object structure
            foreach (var driverVariableNode in driverObject.Children)
            {
                if (driverVariableNode is not IUAVariable driverVar) continue;

                // Look for the matching variable name inside the remote OPC UA source object
                var sourceVar = sourceObject.Get<IUAVariable>(driverVar.BrowseName);
                if (sourceVar == null)
                {
                    Log.Warning("LinkGenerator", $"OPC UA Client variable '{driverVar.BrowseName}' not found under '{sourceObject.BrowseName}'");
                    continue;
                }

                // Clean any existing dynamic link off the PLC tag first to prevent duplicates
                driverVar.ResetDynamicLink();

                // Call SetDynamicLink directly on the PLC Tag (driverVar)
                driverVar.SetDynamicLink(sourceVar, DynamicLinkMode.Read);

                linksCreated++;
            }
        }

        Log.Info("LinkGenerator", $"Success! Created {linksCreated} dynamic links onto the PLC tags.");
    }

    [ExportMethod]
    public void ClearAllDynamicLinks()
    {
        Log.Info("LinkGenerator", $"Starting deletion of dynamic links under PLC path: {TargetDriverPath}");

        var driverFolder = Project.Current.Get<Folder>(TargetDriverPath);
        if (driverFolder == null)
        {
            Log.Error("LinkGenerator", $"Target folder not found: {TargetDriverPath}");
            return;
        }

        int linksCleared = 0;
        
        // Call the uniquely named cleanup method to eliminate ambiguity errors
        ExecuteDeepLinkCleanup(driverFolder, ref linksCleared);

        Log.Info("LinkGenerator", $"Cleanup complete. Removed {linksCleared} dynamic links from PLC driver.");
    }

    // Renamed to completely prevent signature matching conflicts
    private void ExecuteDeepLinkCleanup(IUANode node, ref int count)
    {
        if (node == null) return;

        if (node is IUAVariable variable)
        {
            var dynamicLinkRef = variable.Refs.GetVariable(FTOptix.CoreBase.ReferenceTypes.HasDynamicLink);
            if (dynamicLinkRef != null)
            {
                variable.ResetDynamicLink();
                count++;
            }
        }

        foreach (var child in node.Children)
        {
            ExecuteDeepLinkCleanup(child, ref count);
        }
    }
}
