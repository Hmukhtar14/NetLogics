#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.HMIProject;
using FTOptix.UI;
using FTOptix.Retentivity;
using FTOptix.NativeUI;
using FTOptix.RAEtherNetIP;
using FTOptix.Modbus;
using FTOptix.CommunicationDriver;
using FTOptix.CoreBase;
using FTOptix.Core;
using FTOptix.NetLogic;
using FTOptix.Alarm;
using FTOptix.WebUI;
using FTOptix.S7TiaProfinet;
using FTOptix.OPCUAServer;
using FTOptix.OPCUAClient;
#endregion

public class VariableSyncronizer_ContainsNumber : BaseNetLogic
{
    private int i = 0;

    public override void Start()
    {
       startSyncronization();
    }

    public void startSyncronization() 
    {
        // Insert code to be executed when the user-defined logic is started
        var updateRate = TimeSpan.FromSeconds(1000);
        variableSynchronizer = new RemoteVariableSynchronizer(updateRate);
        myLongRunningTask = new LongRunningTask(LoopInVariables, LogicObject);
        myLongRunningTask.Start();
    }

    private void LoopInVariables() {
        var targetNode = LogicObject.GetVariable("TagsToSync").Value;
        RecursiveSearch(InformationModel.Get(targetNode));
        
        Log.Info("RecursiveSearch.Complete", "Completed search with " + i + " variables found.");

        myLongRunningTask.Dispose();
    }

    private void RecursiveSearch(IUANode startingNode) 
    {
        if (startingNode.BrowseName.Contains("1") || startingNode.BrowseName.Contains("2") || startingNode.BrowseName.Contains("3") || startingNode.BrowseName.Contains("4") || startingNode.BrowseName.Contains("5") || startingNode.BrowseName.Contains("6") || startingNode.BrowseName.Contains("7") || startingNode.BrowseName.Contains("8") || startingNode.BrowseName.Contains("9") || startingNode.BrowseName.Contains("0"))
        {
            IUAVariable sourceVar = null;
            //IUAVariable destVar = null;
            //UAValue destNode = null;
            try {
                sourceVar = InformationModel.GetVariable(startingNode.NodeId);
            } catch {
                Log.Error("RecursiveSearch.Exception", "Skipping " + startingNode.BrowseName + " of type " + startingNode.GetType().ToString());
            }
            //try {
            //    destNode = sourceVar.GetVariable("DynamicLink").Value;
            //} catch {
            //    Log.Error("RecursiveSearch.Exception", "Skipping " + destNode + " of type " + destNode.GetType().ToString());
            //}
            if (sourceVar != null) 
            {
                i++;    
                variableSynchronizer.Add(sourceVar);
            }     
        }
        if (startingNode.Children.Count > 0)
        {
            foreach (IUANode children in startingNode.Children) {
                RecursiveSearch(children);
            }
        }
        // If the node is not an object or variable, log skip
        if (!(startingNode.GetType() == typeof(IUAObject) || startingNode.GetType() == typeof(IUAVariable)))
        {
            Log.Debug("RecursiveSearch.Skip", "Skipping " + startingNode.BrowseName + " of type " + startingNode.GetType().ToString());
        }
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }
    private RemoteVariableSynchronizer variableSynchronizer;
    private LongRunningTask myLongRunningTask;
}
