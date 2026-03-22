#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.NativeUI;
using FTOptix.Retentivity;
using FTOptix.CoreBase;
using FTOptix.Core;
#endregion

using System.Linq;
using FTOptix.OPCUAServer;
using System.Collections.Generic;
using FTOptix.Store;
using FTOptix.ODBCStore;
using FTOptix.DataLogger;
using FTOptix.RAEtherNetIP;
using FTOptix.CommunicationDriver;

/// <summary>
/// NetLogic class for simulating OPC UA tags in FactoryTalk Optix.
/// This class creates a periodic task to update various types of tags (Int, String, Double, Float) with random values.
/// </summary>
public class TagsSimulator : BaseNetLogic
{
    private PeriodicTask _simulationTask;
    private readonly Random _random = new Random();

    private List<IUAVariable> _intTags = new List<IUAVariable>();
    private List<IUAVariable> _stringTags = new List<IUAVariable>();
    private List<IUAVariable> _doubleTags = new List<IUAVariable>();
    private List<IUAVariable> _floatTags = new List<IUAVariable>();

    /// <summary>
    /// Called when the NetLogic starts. Initializes the tag caching and starts the simulation task.
    /// </summary>
    public override void Start()
    {
        // Cache all tag references for efficiency (avoid repeated lookups)
        CacheTags();

        // Start a periodic task that updates all tags each interval (every 1 second for efficiency)
        _simulationTask = new PeriodicTask(UpdateAllTags, 1000, LogicObject);
        _simulationTask.Start();
    }

    /// <summary>
    /// Caches references to all OPC UA tags in the project for efficient access during simulation.
    /// Tags are organized by type (Int, String, Double, Float) and stored in separate lists.
    /// </summary>
    private void CacheTags()
    {
        var tagsFolder = Project.Current.Get<Folder>("Model/OPCUA_Tags");

        // Cache Int tags
        for (int i = 0; i < 20000; i++)
        {
            var tag = tagsFolder.Get<IUAVariable>($"Int{i}");
            if (tag != null) _intTags.Add(tag);
        }

        // Cache String tags
        for (int i = 0; i < 20000; i++)
        {
            var tag = tagsFolder.Get<IUAVariable>($"String{i}");
            if (tag != null) _stringTags.Add(tag);
        }

        // Cache Double tags
        for (int i = 0; i < 20000; i++)
        {
            var tag = tagsFolder.Get<IUAVariable>($"Double{i}");
            if (tag != null) _doubleTags.Add(tag);
        }

        // Cache Float tags
        for (int i = 0; i < 20000; i++)
        {
            var tag = tagsFolder.Get<IUAVariable>($"Float{i}");
            if (tag != null) _floatTags.Add(tag);
        }
    }

    /// <summary>
    /// Called when the NetLogic stops. Disposes of the simulation task to clean up resources.
    /// </summary>
    public override void Stop()
    {
        _simulationTask?.Dispose();
        _simulationTask = null;
    }

    /// <summary>
    /// Updates all cached tags with random values. This method is called periodically by the simulation task.
    /// </summary>
    private void UpdateAllTags()
    {
        // Update all cached Int tags
        foreach (var tag in _intTags)
        {
            tag.Value = new UAValue(_random.Next(0, 100000));
        }

        // Update all cached String tags
        foreach (var tag in _stringTags)
        {
            tag.Value = new UAValue(GenerateRandomString(10));
        }

        // Update all cached Double tags
        foreach (var tag in _doubleTags)
        {
            tag.Value = new UAValue(_random.NextDouble() * 1000.0);
        }

        // Update all cached Float tags
        foreach (var tag in _floatTags)
        {
            tag.Value = new UAValue((float)(_random.NextDouble() * 1000.0));
        }
    }

    /// <summary>
    /// Generates a random string of the specified length using uppercase letters and digits.
    /// </summary>
    /// <param name="length">The length of the random string to generate.</param>
    /// <returns>A random string consisting of uppercase letters and digits.</returns>
    private string GenerateRandomString(int length)
    {
        const string usableCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Repeat(usableCharacters, length)
            .Select(s => s[_random.Next(s.Length)])
            .ToArray());
    }
}
