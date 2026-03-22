#region Using directives
using System;
using UAManagedCore;
using FTOptix.NetLogic;
using FTOptix.OPCUAServer;
using FTOptix.Store;
using FTOptix.ODBCStore;
using FTOptix.DataLogger;
using FTOptix.RAEtherNetIP;
using FTOptix.CommunicationDriver;
#endregion

public class VariablesSimulator : BaseNetLogic
{
    private readonly Random _random = new Random();

    public override void Start()
    {
        // Get local variables
        runVariable = LogicObject.GetVariable("RunSimulation");
        sine = LogicObject.GetVariable("Sine");
        ramp = LogicObject.GetVariable("Ramp");
        cosine = LogicObject.GetVariable("Cosine");

        // Start simulation
        simulationTask = new PeriodicTask(Simulation, 250, LogicObject);
        simulationTask.Start();
    }

    /// <summary>
    /// Simulates some operations based on boolean run variable.
    /// When running, updates each variable with a random value.
    /// </summary>
    private void Simulation()
    {
        if (runVariable.Value)
        {
            ramp.Value = _random.Next(0, 100);
            sine.Value = _random.NextDouble() * 100;
            cosine.Value = _random.NextDouble() * 50;
        }
    }

    public override void Stop()
    {
        simulationTask?.Dispose();
    }

    private PeriodicTask simulationTask;
    private IUAVariable runVariable;
    private IUAVariable sine;
    private IUAVariable cosine;
    private IUAVariable ramp;
}
