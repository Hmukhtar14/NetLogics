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
#endregion

public class VariableSyncronizer3 : BaseNetLogic
{
    public override void Start()
    {
        // Insert code to be executed when the user-defined logic is started
        var updateRate = TimeSpan.FromSeconds(1);
        variableSynchronizer = new RemoteVariableSynchronizer(updateRate);
        myLongRunningTask = new LongRunningTask(LoopInVariables, LogicObject);
        myLongRunningTask.Start();
    }

    private void LoopInVariables() {
        var targetNode = LogicObject.GetVariable("TagsToSync").Value;
        RecursiveSearch(InformationModel.Get(targetNode));
        myLongRunningTask.Dispose();
    }

    private void RecursiveSearch(IUANode startingNode) {

        //Log.Warning(startingNode.GetType().FullName);

        if (startingNode.GetType().FullName.Contains("UAVariable")) {
            //Log.Warning(startingNode.GetType().FullName);
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
            if (sourceVar != null) {
                Log.Info("RecursiveSearch.Add", "Adding " + startingNode.BrowseName + " of type " + startingNode.GetType().ToString());
                variableSynchronizer.Add(sourceVar);
            }            
        }
        if (startingNode.Children.Count > 0) {
            foreach (IUANode children in startingNode.Children) {
                RecursiveSearch(children);
            }
        } else {
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
