#region Using directives
using System;
using UAManagedCore;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.Core;
using System.Collections.Generic;
using FTOptix.WebUI;
using FTOptix.System;
#endregion

public class Float_TagsSimulator : BaseNetLogic
{
    private PeriodicTask _simulationTask;
    private LongRunningTask _cachingTask;
    private readonly Random _random = new Random();
    private int NumberOfTags; 

    private List<IUAVariable> _floatTags = new List<IUAVariable>();

    /// <summary>
    /// Called when the NetLogic starts. Initializes tag caching asynchronously without blocking the UI.
    /// </summary>
    public override void Start()
    {
        // Cache all tag references in a background thread to avoid blocking the UI
        _cachingTask = new LongRunningTask(CacheTagsAsync, LogicObject);
        _cachingTask.Start();
    }

    /// <summary>
    /// Caches references to all Float tags in the project asynchronously for efficient access during simulation.
    /// Runs in a background thread to prevent UI blocking.
    /// </summary>
    private void CacheTagsAsync(LongRunningTask task)
    {
        try
        {
            var tagsFolder = Project.Current.Get<Folder>("Model/Float_Tags");
            NumberOfTags = LogicObject.GetVariable("NumberOfTags").Value;

            // Cache Float tags
            for (int i = 0; i < NumberOfTags; i++)
            {
                // Check if cancellation was requested
                if (task.IsCancellationRequested)
                    return;

                var tag = tagsFolder.Get<IUAVariable>($"Float{i}");
                if (tag != null) 
                    _floatTags.Add(tag);
            }

            // Once tags are cached, start the periodic simulation task
            _simulationTask = new PeriodicTask(UpdateAllTags, 100, LogicObject);
            _simulationTask.Start();

            Log.Debug("Float_TagsSimulator", $"Cached {_floatTags.Count} tags successfully. Simulation started.");
        }
        catch (Exception ex)
        {
            Log.Error("Float_TagsSimulator", $"Error during tag caching: {ex.Message}");
        }
    }

    /// <summary>
    /// Called when the NetLogic stops. Disposes of both the caching and simulation tasks to clean up resources.
    /// </summary>
    public override void Stop()
    {
        _simulationTask?.Dispose();
        _simulationTask = null;
        
        _cachingTask?.Dispose();
        _cachingTask = null;
    }

    /// <summary>
    /// Updates all cached tags with random values. This method is called periodically by the simulation task.
    /// </summary>
    private void UpdateAllTags()
    {
        // Update all cached Float tags
        foreach (var tag in _floatTags)
        {
            tag.Value = new UAValue((float)(_random.NextDouble() * 1000.0));
        }
    }
}
