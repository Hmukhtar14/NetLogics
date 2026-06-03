#region Using directives
using System;
using UAManagedCore;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.Core;
#endregion

/// <summary>
/// Runtime NetLogic script that uses an asynchronous LongRunningTask thread
/// to safely populate the background variable synchronizer without leaking RAM.
/// </summary>
public class OPCUABackgroundSynchronizer1 : BaseNetLogic
{
    private const string TargetSourcePath = "OPC-UA/Client_MainServer/Objects/OPCUA_TagSimulator/Model/UDT_Tags/Objects_Set1";
    
    // Limits background polling strictly to a set number of tags to prevent driver saturation
    private const int MaxAllowedVariables = 0;

    private RemoteVariableSynchronizer variableSynchronizer;
    private LongRunningTask initializationTask;
    private int totalRegisteredCount = 0;

    public override void Start()
    {
        // Offload the entire registration sweep to an asynchronous background worker thread.
        // This prevents thread blocking and resolves gradual RAM growth at startup.
        initializationTask = new LongRunningTask(AsynchronousSyncSetup, LogicObject);
        initializationTask.Start();
    }

    private void AsynchronousSyncSetup()
    {
        try
        {
            var opcUaFolder = Project.Current.Get<Folder>(TargetSourcePath);
            if (opcUaFolder == null)
            {
                Log.Warning("OPCUABackgroundSynchronizer", $"Sync aborted: Folder path not found: {TargetSourcePath}");
                return;
            }

            // Creating the synchronizer with no arguments invokes the native engine optimization,
            // grouping variables into unified batch read request packets.
            variableSynchronizer = new RemoteVariableSynchronizer(TimeSpan.FromSeconds(1));
            totalRegisteredCount = 0;

            // Execute the recursive tree walk on the background worker thread
            CrawlAndRegisterVariables(opcUaFolder);

            Log.Info(LogicObject.BrowseName, $"Success! Background synchronizer is actively polling {totalRegisteredCount} remote variables.");
            initializationTask?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("OPCUABackgroundSynchronizer", $"Critical error executing runtime initialization: {ex.Message}");
        }
    }

    private void CrawlAndRegisterVariables(IUANode node)
    {
        if (node == null) return;
        if (totalRegisteredCount >= MaxAllowedVariables) return;

        // If the node is a valid variable type, add it to the background sync engine
        if (node is IUAVariable variable)
        {
            variableSynchronizer.Add(variable);
            // Log.Info(variable.BrowseName);
            totalRegisteredCount++;
            return;
        }

        // Deep-crawl recursively into sub-folders, structured UDT fields, and arrays
        foreach (var child in node.Children)
        {
            if (totalRegisteredCount >= MaxAllowedVariables) break;
            CrawlAndRegisterVariables(child);

        }
    }

    public override void Stop()
    {
        // Terminate the worker thread safely
        initializationTask?.Dispose();
        Log.Info("OPCUABackgroundSynchronizer", "Background synchronizer stopped and memory freed successfully.");
    
    }
}
