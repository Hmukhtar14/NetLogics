#region Using directives
using System;
using System.Collections.Generic;
using System.Linq;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using UAManagedCore;
#endregion

public class DeviceSizingAnalyzer : BaseNetLogic
{
    /// <summary>
    /// Disposes the existing task if it exists and then starts a new long-running task for analyzing item count.
    /// </summary>
    /// <remarks>
    /// If an existing task named 'analyzerTask' is not null, it will be disposed before starting a new one.
    /// The new task is created using the 'AnalyzeItemsCount' logic object.
    /// </remarks>
    [ExportMethod]
    public void GetItemsCount()
    {
        // Execute the method in a separate thread
        analyzerTask?.Dispose();
        analyzerTask = new LongRunningTask(AnalyzeItemsCount, LogicObject);
        analyzerTask.Start();
    }

    /// <summary>
    /// This method analyzes the count of items within each category and calculates their averages for logging purposes.
    /// <example>
    /// For example:
    /// <code>
    /// AnalyzeItemsCount();
    /// </code>
    /// The output will log information about device sizing counters with details on tags and variables, structured tags, alarms, data loggers, event loggers, recipes, web clients, OPC clients, low density UI types, medium density UI types, and high density UI types.
    /// </example>
    /// </summary>
    /// <remarks>
    /// It iterates through children of the current project and recursively searches for nodes. Then it calculates averages for different categories such as structured tags, data loggers, event loggers, and recipes based on the counts of relevant fields or events.
    /// </remarks>
    private void AnalyzeItemsCount()
    {
        // Loop in the project nodes to count the number of each type of item
        foreach (var item in Project.Current.Children)
        {
            RecursiveProjectNodesSearch(item);
        }

        int averageDataLoggerFields = 0;
        int averageEventLoggerFields = 0;
        int averageStructuredTagsFields = 0;
        int averageRecipeFields = 0;

        // Calculate average for objects with multiple fields
        if (structuredTagsFieldsCount.Count > 0)
        {
            averageStructuredTagsFields = (int) structuredTagsFieldsCount.Average();
        }
        if (dataLoggerVariablesCount.Count > 0)
        {
            averageDataLoggerFields = (int) dataLoggerVariablesCount.Average();
        }
        if (eventLoggerEventsCount.Count > 0)
        {
            averageEventLoggerFields = (int) eventLoggerEventsCount.Average();
        }
        if (recipeFieldsCount.Count > 0)
        {
            averageRecipeFields = (int) recipeFieldsCount.Average();
        }

        // Print the results
        Log.Info("DeviceSizingToolCounter", $"Tags and variables: {tagsCount}");
        Log.Info("DeviceSizingToolCounter", $"Structured Tags: {structuredTagsCount}, average structure items: {averageStructuredTagsFields}");
        Log.Info("DeviceSizingToolCounter", $"Alarms: {alarmsCount}");
        Log.Info("DeviceSizingToolCounter", $"Data Loggers: {dataLoggersCount}, average logged variables: {averageDataLoggerFields}");
        Log.Info("DeviceSizingToolCounter", $"Event Loggers: {eventLoggersCount}, average logged fields: {averageEventLoggerFields}");
        Log.Info("DeviceSizingToolCounter", $"Recipes: {recipesCount}, average recipe fields: {averageRecipeFields}");
        Log.Info("DeviceSizingToolCounter", $"Web Clients: {webClients}");
        Log.Info("DeviceSizingToolCounter", $"OPC Clients: {opcClients}");
        Log.Info("DeviceSizingToolCounter", $"Low Density UI types: {lowDensityPages}");
        Log.Info("DeviceSizingToolCounter", $"Medium Density UI types: {mediumDensityPages}");
        Log.Info("DeviceSizingToolCounter", $"High Density UI types: {highDensityPages}");
    }

    /// <summary>
    /// Recursively searches for nodes matching specific types within the given IUANode structure.
    /// <example>
    /// For example:
    /// <code>
    /// RecursiveProjectNodesSearch(rootNode);
    /// </code>
    /// will recursively traverse all child nodes of the provided root node.
    /// </example>
    /// </summary>
    /// <param name="node">The starting point for the search.</param>
    /// <returns></returns>
    /// <remarks>
    /// Searches for nodes that match predefined criteria such as tags, variables, alarms, data loggers,
    /// event loggers, recipes, web clients, OPC servers, and containers/windows. Depending on the type of
    /// node found, it increments counters for various counts related to the device sizing process.
    /// The function also calculates the count of UI elements under each UI page found during traversal.
    /// </remarks>
    private void RecursiveProjectNodesSearch(IUANode node)
    {
        // Check the type of the node and increment the corresponding counter
        if (IsAssignable<FTOptix.CommunicationDriver.TagStructure>(node))
        {
            ++structuredTagsCount;
            Log.Debug("DeviceSizingToolCounter", $"Found a structured Tag: {node.BrowseName}");
            structuredTagsFieldsCount.Add(node.Children.OfType<FTOptix.CommunicationDriver.Tag>().Count());
        }
        else if (IsAssignable<FTOptix.CommunicationDriver.Tag>(node) ||
            IsAssignable<IUAVariable>(node))
        {
            ++tagsCount;
            Log.Debug("DeviceSizingToolCounter", $"Found a Tag or a Variable: {node.BrowseName}");
        }
        else if (IsAssignable<FTOptix.Alarm.AlarmController>(node))
        {
            ++alarmsCount;
            Log.Debug("DeviceSizingToolCounter", $"Found an Alarm: {node.BrowseName}");
        }
        else if (IsAssignable<FTOptix.DataLogger.DataLogger>(node))
        {
            ++dataLoggersCount;
            Log.Debug("DeviceSizingToolCounter", $"Found a Data Logger: {node.BrowseName}");
            dataLoggerVariablesCount.Add(((FTOptix.DataLogger.DataLogger) node).VariablesToLog.Count);
        }
        else if (IsAssignable<FTOptix.EventLogger.EventLogger>(node))
        {
            ++eventLoggersCount;
            Log.Debug("DeviceSizingToolCounter", $"Found an Event Logger: {node.BrowseName}");
            eventLoggerEventsCount.Add(((FTOptix.EventLogger.EventLogger) node).EventFieldsToLog.Count);
        }
        else if (IsAssignable<FTOptix.Recipe.RecipeSchema>(node))
        {
            ++recipesCount;
            Log.Debug("DeviceSizingToolCounter", $"Found a Recipe: {node.BrowseName}");
            recipeFieldsCount.Add(GetRecipeSchemaFields((FTOptix.Recipe.RecipeSchema) node));
        }
        else if (IsAssignable<FTOptix.WebUI.WebUIPresentationEngine>(node))
        {
            webClients += ((FTOptix.WebUI.WebUIPresentationEngine) node).MaxNumberOfConnections;
            Log.Debug("DeviceSizingToolCounter", $"Found a Web Client: {node.BrowseName}");
        }
        else if (IsAssignable<FTOptix.OPCUAServer.OPCUAServer>(node))
        {
            opcClients += ((FTOptix.OPCUAServer.OPCUAServer) node).MaxNumberOfConnections;
            Log.Debug("DeviceSizingToolCounter", $"Found an OPC Client: {node.BrowseName}");
        }

        // Count the number of UI pages in the project and group them by density
        if (node is FTOptix.UI.ContainerType || node is FTOptix.UI.WindowType)
        {
            int childrenCount = GetUiObjectCount(node);

            Log.Debug("DeviceSizingToolCounter", $"Found a UI page '{node.BrowseName}' with {childrenCount} UI objects");

            if (childrenCount < 100)
            {
                ++lowDensityPages;
            }
            else if (childrenCount < 300)
            {
                ++mediumDensityPages;
            }
            else
            {
                ++highDensityPages;
            }
        }

        // Recursively search the children of the current node
        foreach (var item in node.Children)
        {
            RecursiveProjectNodesSearch(item);
        }
    }

    /// <summary>
    /// This method recursively counts the number of UI objects within an UGANode structure.
    /// <example>
    /// For example:
    /// <code>
    /// int objectCount = GetUiObjectCount(node);
    /// </code>
    /// results in <c>objectCount</c>'s being the total count of UI objects under the specified node.
    /// </example>
    /// </summary>
    /// <param name="node">The UGANode structure to search through.</param>
    /// <returns>
    /// The total count of UI objects found within the node's hierarchy.
    /// </returns>
    private static int GetUiObjectCount(IUANode node)
    {
        int count = 0;

        // Count only graphical objects (not properties)
        if (IsAssignable<FTOptix.UI.BaseUIObject>(node))
        {
            ++count;
        }

        foreach (var item in node.Children)
        {
            count += GetUiObjectCount(item);
        }

        return count;
    }

    /// <summary>
    /// This method retrieves the number of fields in a recipe schema.
    /// <example>
    /// For example:
    /// <code>
    /// int fieldCount = GetRecipeSchemaFields(recipeSchema);
    /// </code>
    /// results in <c>fieldCount</c>'s being the number of fields in the specified recipe schema.
    /// </example>
    /// </summary>
    /// <param name="recipeSchema">The recipe schema to analyze.</param>
    private static int GetRecipeSchemaFields(FTOptix.Recipe.RecipeSchema recipeSchema)
    {
        var store = InformationModel.Get<FTOptix.Store.Store>(recipeSchema.Store);
        if (store != null)
        {
            var table = store.Tables.Get<FTOptix.Store.Table>(string.IsNullOrEmpty(recipeSchema.TableName) ? recipeSchema.BrowseName : recipeSchema.TableName);
            if (table != null)
            {
                // Remove the "Name" field from the count
                return table.Columns.Count - 1;
            }
            else
            {
                Log.Warning("DeviceSizingToolCounter", $"Recipe {recipeSchema.BrowseName} does not have a valid table");
            }
        }
        else
        {
            Log.Warning("DeviceSizingToolCounter", $"Recipe {recipeSchema.BrowseName} does not have a valid store");
        }

        return 0;
    }

    /// <summary>
    /// This method checks if a node is assignable to a specific type (likely a subtype).
    /// <example>
    private static bool IsAssignable<T>(IUANode node) => node.GetType().IsAssignableTo(typeof(T));

    private long tagsCount;
    private long structuredTagsCount;
    private readonly List<int> structuredTagsFieldsCount = [];
    private long alarmsCount;
    private long dataLoggersCount;
    private readonly List<int> dataLoggerVariablesCount = [];
    private long eventLoggersCount;
    private readonly List<int> eventLoggerEventsCount = [];
    private long recipesCount;
    private readonly List<int> recipeFieldsCount = [];
    private long webClients;
    private long opcClients;
    private long lowDensityPages;
    private long mediumDensityPages;
    private long highDensityPages;
    private LongRunningTask analyzerTask;
}
