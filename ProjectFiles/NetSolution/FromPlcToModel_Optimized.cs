#region Using directives

using FTOptix.CommunicationDriver;
using FTOptix.Core;
using FTOptix.CoreBase;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using System;
using System.Linq;
using UAManagedCore;

#endregion

public class FromPlcToModel_Optimized : BaseNetLogic
{
    /// <summary>
    /// This method initiates a long-running task to generate nodes in the model based on PLC tags.
    /// </summary>
    [ExportMethod]
    public void GenerateNodesIntoModel()
    {
        // Get nodes where to search PLC tags
        startingNodeToFetch = InformationModel.Get(LogicObject.GetVariable("StartingNodeToFetch").Value);
        if (startingNodeToFetch == null)
        {
            Log.Error(GetType().Name, "Cannot get StartingNodeToFetch");
            return;
        }
        // Delete existing nodes if needed
        deleteExistingTags = LogicObject.GetVariable("DeleteExistingTags").Value;
        // Get the node where we are going to create the Model variables/objects
        targetNode = InformationModel.Get<Folder>(LogicObject.GetVariable("TargetFolder").Value);
        if (targetNode == null)
        {
            Log.Error(GetType().Name, "Cannot get TargetNode");
            return;
        }
        // Start procedure
        generateNodesTask = new LongRunningTask(GenerateNodesMethod, LogicObject);
        generateNodesTask.Start();
    }

    private void GenerateNodesMethod()
    {
        createdNodesCount = 0;
        GenerateNodes(startingNodeToFetch);
        Log.Info($"{GetType().Name}.{System.Reflection.MethodBase.GetCurrentMethod().Name}", $"Node generation complete. Total nodes created/updated: {createdNodesCount}");
        generateNodesTask?.Dispose();
    }

    private LongRunningTask generateNodesTask;
    private IUANode startingNodeToFetch;
    private IUANode targetNode;
    private bool deleteExistingTags;
    private int createdNodesCount = 0;
    private const int LOG_BATCH_SIZE = 100; // Log only every N nodes to reduce logging overhead

    /// <summary>
    /// Generates a set of objects and variables in model in order to have a "copy" of a set of imported tags, retrieved from a starting node
    /// </summary>
    public void GenerateNodes(IUANode startingNode)
    {
        Folder modelFolder = InformationModel.Get<Folder>(LogicObject.GetVariable("TargetFolder").Value);

        if (modelFolder == null)
        {
            Log.Error($"{GetType().Name}.{System.Reflection.MethodBase.GetCurrentMethod().Name}", "Cannot get to target folder");
            return;
        }

        CreateModelTag(startingNode, modelFolder);
        CheckDynamicLinks();
    }

    /// <summary>
    /// Recursively creates objects and variables in the model based on the structure of the starting node.
    /// </summary>
    /// <param name="fieldNode">The starting node to create the model from.</param>
    /// <param name="parentNode">The parent node in the model where the new nodes will be created.</param>
    /// <param name="browseNamePrefix">The prefix to be added to the browse name of the new nodes.</param>
    private void CreateModelTag(IUANode fieldNode, IUANode parentNode, string browseNamePrefix = "")
    {
        switch (fieldNode)
        {
            case TagStructure:
                if (!IsTagStructureArray(fieldNode))
                    CreateOrUpdateObject(fieldNode, parentNode, browseNamePrefix);
                else
                    CreateOrUpdateObjectArray(fieldNode, parentNode);
                break;
            case FTOptix.Core.Folder:
                IUANode newFolder;
                if (fieldNode.NodeId != startingNodeToFetch.NodeId)
                    newFolder = CreateFolder(fieldNode, parentNode);
                else
                    newFolder = parentNode;

                foreach (IUANode children in fieldNode.Children)
                    CreateModelTag(children, newFolder, browseNamePrefix);
                break;
            default:
                CreateOrUpdateVariable(fieldNode, parentNode, browseNamePrefix);
                break;
        }
    }

    private static bool IsTagStructureArray(IUANode fieldNode) => ((TagStructure) fieldNode).ArrayDimensions.Length != 0;

    /// <summary>
    /// Creates a folder in the model based on the given field node and parent node.
    /// If the folder already exists, it clears its children if DeleteExistingTags is set to true.
    /// </summary>
    /// <param name="fieldNode">The field node to create the folder from.</param>
    /// <param name="parentNode">The parent node in the model where the new folder will be created.</param>
    private IUANode CreateFolder(IUANode fieldNode, IUANode parentNode)
    {
        if (parentNode.Get<FTOptix.Core.Folder>(fieldNode.BrowseName) == null)
        {
            Folder newFolder = InformationModel.Make<FTOptix.Core.Folder>(fieldNode.BrowseName);
            parentNode.Add(newFolder);
            createdNodesCount++;
            if (createdNodesCount % LOG_BATCH_SIZE == 0)
                Log.Info($"{GetType().Name}.{System.Reflection.MethodBase.GetCurrentMethod().Name}", $"Created {createdNodesCount} nodes so far...");
            return newFolder;
        }
        else
        {
            if (deleteExistingTags)
            {
                parentNode.Get<FTOptix.Core.Folder>(fieldNode.BrowseName).Children.Clear();
            }

            return parentNode.Get<FTOptix.Core.Folder>(fieldNode.BrowseName);
        }
    }

    /// <summary>
    /// Creates or updates an object array in the model based on the given field node and parent node.
    /// If the object array already exists, it updates its children.
    /// </summary>
    /// <param name="fieldNode">The field node to create the object array from.</param>
    /// <param name="parentNode">The parent node in the model where the new object array will be created.</param>
    private void CreateOrUpdateObjectArray(IUANode fieldNode, IUANode parentNode)
    {
        TagStructure tagStructureArrayTemp = (TagStructure)fieldNode;

        foreach (IUANode tagStructureMember in tagStructureArrayTemp.Children.Where(c => !IsArrayDimensionsVariable(c)))
            CreateModelTag(tagStructureMember, parentNode, fieldNode.BrowseName + "_");
    }

    /// <summary>
    /// Creates or updates an object in the model based on the given field node and parent node.
    /// If the object already exists, it updates its children.
    /// </summary>
    /// <param name="fieldNode">The field node to create the object from.</param>
    /// <param name="parentNode">The parent node in the model where the new object will be created.</param>
    /// <param name="browseNamePrefix">The prefix to be added to the browse name of the new object.</param>
    private void CreateOrUpdateObject(IUANode fieldNode, IUANode parentNode, string browseNamePrefix = "")
    {
        IUAObject existingNode = GetChild<IUAObject>(fieldNode, parentNode, browseNamePrefix);
        // Replacing "/" with "_". Nodes with BrowseName "/" are not allowed
        string filedNodeBrowseName = fieldNode.BrowseName.Replace("/", "_");

        if (existingNode == null)
        {
            existingNode = InformationModel.MakeObject(browseNamePrefix + filedNodeBrowseName);
            parentNode.Add(existingNode);
            createdNodesCount++;
            if (createdNodesCount % LOG_BATCH_SIZE == 0)
                Log.Info($"{GetType().Name}.{System.Reflection.MethodBase.GetCurrentMethod().Name}", $"Created {createdNodesCount} nodes so far...");
        }

        foreach (IUANode childrenNode in fieldNode.Children.Where(c => !IsArrayDimensionsVariable(c)))
            CreateModelTag(childrenNode, existingNode);
    }

    /// <summary>
    /// Creates or updates a variable in the model based on the given field node and parent node.
    /// If the variable already exists, it updates its properties.
    /// </summary>
    /// <param name="fieldNode">The field node to create the variable from.</param>
    /// <param name="parentNode">The parent node in the model where the new variable will be created.</param>
    /// <param name="browseNamePrefix">The prefix to be added to the browse name of the new variable.</param>
    private void CreateOrUpdateVariable(IUANode fieldNode, IUANode parentNode, string browseNamePrefix = "")
    {
        if (IsArrayDimensionsVariable(fieldNode))
        {
            return;
        }
        IUAVariable existingNode = GetChild<IUAVariable>(fieldNode, parentNode, browseNamePrefix);
        IUAVariable fieldTag = (IUAVariable) fieldNode;
        if (existingNode == null)
        {
            // Replacing "/" with "_". Nodes with BrowseName "/" are not allowed
            string tagBrowseName = fieldTag.BrowseName.Replace("/", "_");
            existingNode = InformationModel.MakeVariable(tagBrowseName, fieldTag.DataType, fieldTag.ArrayDimensions);
            parentNode.Add(existingNode);
            createdNodesCount++;
            if (createdNodesCount % LOG_BATCH_SIZE == 0)
                Log.Info($"{GetType().Name}.{System.Reflection.MethodBase.GetCurrentMethod().Name}", $"Created {createdNodesCount} nodes so far...");
        }
        if (existingNode.DataType != fieldTag.DataType)
        {
            existingNode.DataType = fieldTag.DataType;
        }
        existingNode.SetDynamicLink(fieldTag, FTOptix.CoreBase.DynamicLinkMode.ReadWrite);
    }

    private static bool IsArrayDimensionsVariable(IUANode n) => n.BrowseName.Contains("arraydimen", System.StringComparison.InvariantCultureIgnoreCase);

    /// <summary>
    /// Retrieves a child node of a specified type from the parent node using the browse name prefix.
    /// If the child node is not found, it returns the default value of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of the child node to retrieve.</typeparam>
    /// <param name="child">The child node to retrieve.</param>
    /// <param name="parent">The parent node from which to retrieve the child node.</param>
    /// <param name="browseNamePrefix">The prefix to be added to the browse name of the child node.</param>
    private static T GetChild<T>(IUANode child, IUANode parent, string browseNamePrefix = "") where T : class
    {
        T returnValue;
        try
        {
            returnValue = (T)parent.Children[browseNamePrefix + child.BrowseName];
        }
        catch
        {
            returnValue = default;
        }
        return returnValue;
    }

    /// <summary>
    /// Checks for unresolved dynamic links in the target node and logs a summary warning if any are found.
    /// For large datasets, only warns about the count rather than checking each individual link.
    /// </summary>
    private void CheckDynamicLinks()
    {
        try
        {
            var dynamicLinks = targetNode.FindNodesByType<FTOptix.CoreBase.DynamicLink>();
            int unresolvedCount = 0;
            
            foreach (DynamicLink dataBind in dynamicLinks)
            {
                if (LogicObject.Context.ResolvePath(dataBind.Owner, dataBind.Value).ResolvedNode == null)
                    unresolvedCount++;
            }
            
            if (unresolvedCount > 0)
                Log.Warning($"{GetType().Name}.{System.Reflection.MethodBase.GetCurrentMethod().Name}", $"Found {unresolvedCount} unresolved datalinks. You may need to either: manually reimport the missing PLC tag(s), manually delete the unresolved Model variable(s) or set DeleteExistingTags to True.");
        }
        catch (Exception ex)
        {
            Log.Error($"{GetType().Name}.{System.Reflection.MethodBase.GetCurrentMethod().Name}", $"Error checking dynamic links: {ex.Message}");
        }
    }
}
