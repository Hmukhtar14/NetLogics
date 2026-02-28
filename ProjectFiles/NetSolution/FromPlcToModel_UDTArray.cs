#region Using directives
using System;
using System.Linq;
using System.Reflection;
using FTOptix.CommunicationDriver;
using FTOptix.Core;
using FTOptix.CoreBase;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using UAManagedCore;
#endregion


// Mirrors PLC tag structures into the Model folder, optionally deleting existing tags.
public class FromPlcToModel_UDTArray : BaseNetLogic
{
    private LongRunningTask generateNodesTask;
    private IUANode startingNodeToFetch;
    private Folder targetFolder;
    private IUANode targetNode;

    private bool deleteExistingTags;

    /// <summary>
    /// Starts generation after validating and reading configuration variables.
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


    // Background task entry point
   private void GenerateNodesMethod()
    {
        GenerateNodes(startingNodeToFetch);
        generateNodesTask?.Dispose();
    }
    
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

    private static bool IsTagStructureArray(IUANode fieldNode) => ((TagStructure)fieldNode).ArrayDimensions?.Length > 0;

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
            Log.Info($"{GetType().Name}.{System.Reflection.MethodBase.GetCurrentMethod().Name}", $"Creating \"{Log.Node(newFolder)}\"");
            return newFolder;
        }
        else
        {
            if (deleteExistingTags == true)
            {
                Log.Info($"{GetType().Name}.{System.Reflection.MethodBase.GetCurrentMethod().Name}", $"Deleting \"{Log.Node(fieldNode)}\" (DeleteExistingTags is set to True)");
                parentNode.Get<FTOptix.Core.Folder>(fieldNode.BrowseName).Children.Clear();
            }
            else
            {
                Log.Info($"{GetType().Name}.{System.Reflection.MethodBase.GetCurrentMethod().Name}", $"\"{Log.Node(fieldNode)}\" already exists, skipping creation or children deletion (DeleteExistingTags is set to False)");
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
    /// Creates or updates an object instance for structured tags (UDT).
    /// </summary>
    private void CreateOrUpdateObject(IUANode fieldNode, IUANode parentNode, string browseNamePrefix = "")
    {
        var existingObject = GetChild<IUAObject>(fieldNode, parentNode, browseNamePrefix);
        var safeBrowseName = fieldNode.BrowseName.Replace("/", "_");

        // Read custom object type -- HM
        var typeVar = LogicObject.GetVariable("TargetObjectType");
        var customType = typeVar != null ? typeVar.Value : null;

        if (existingObject == null)
        {
            var createdObject = InformationModel.MakeObject(browseNamePrefix + safeBrowseName, customType);
            parentNode.Add(createdObject);
            Log.Info($"{GetType().Name}.{MethodBase.GetCurrentMethod().Name}", $"Creating {Log.Node(createdObject)} object");
            existingObject = createdObject;
        }
        else
        {
            Log.Info($"{GetType().Name}.{MethodBase.GetCurrentMethod().Name}", $"Updating {Log.Node(existingObject)} object");
        }

        foreach (IUANode child in fieldNode.Children.Where(c => !IsArrayDimensionsVariable(c)))
            CreateModelTag(child, existingObject);
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
            
            // Skipping the creation of the BlockRead and SymbolName variables, which are present in UDT arrays and might/can cause issues if created in FTOptix due to their reserved keywords as variable names
            if (fieldTag.BrowseName == "BlockRead" || fieldTag.BrowseName == "SymbolName")
            {
                Log.Warning($"{GetType().Name}.{System.Reflection.MethodBase.GetCurrentMethod().Name}", $"Skipping \"{Log.Node(fieldTag)}\" variable creation because its name is either \"BlockRead\" or \"BlockWrite\", which are reserved keywords in FTOptix and may cause issues if used as variable names");
                return;
            }

            existingNode = InformationModel.MakeVariable(tagBrowseName, fieldTag.DataType, fieldTag.ArrayDimensions);
            parentNode.Add(existingNode);
            Log.Info($"{GetType().Name}.{System.Reflection.MethodBase.GetCurrentMethod().Name}", $"Creating \"{Log.Node(existingNode)}\" variable");
        }
        else
        {
            Log.Info($"{GetType().Name}.{System.Reflection.MethodBase.GetCurrentMethod().Name}", $"Updating \"{Log.Node(existingNode)}\" object");
        }
        if (existingNode.DataType != fieldTag.DataType)
        {
            existingNode.DataType = fieldTag.DataType;
        }
        existingNode.SetDynamicLink(fieldTag, FTOptix.CoreBase.DynamicLinkMode.ReadWrite);
    }


    private static bool IsArrayDimensionsVariable(IUANode n) => n.BrowseName?.Contains("arraydimen", StringComparison.InvariantCultureIgnoreCase) == true;

    /// <summary>
    /// Retrieves a child node of a specified type from the parent node using the browse name prefix.
    /// If the child node is not found, it returns the default value of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of the child node to retrieve.</typeparam>
    /// <param name="child">The child node to retrieve.</param>
    /// <param name="parent">The parent node from which to retrieve the child node.</param>
    /// <param name="browseNamePrefix">The prefix to be added to the browse name of the child node.</param>
    private static T GetChild<T>(IUANode child, IUANode parent, string browseNamePrefix = "")
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
    /// Checks for unresolved dynamic links in the target node and logs a warning if any are found.
    /// </summary>
    private void CheckDynamicLinks()
    {
        foreach (DynamicLink dataBind in targetNode.FindNodesByType<FTOptix.CoreBase.DynamicLink>())
        {
            if (LogicObject.Context.ResolvePath(dataBind.Owner, dataBind.Value).ResolvedNode == null)
                Log.Warning($"{GetType().Name}.{System.Reflection.MethodBase.GetCurrentMethod().Name}", $"\"{Log.Node(dataBind.Owner)}\" has unresolved databind, you may need to either: manually reimport the missing PLC tag(s), manually delete the unresolved Model variable(s) or set DeleteExistingTags to True (which may lead to unresolved DynamicLinks somewhere else)");
        }
    }
}
