#region Using directives
using System;
using UAManagedCore;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.CoreBase;
using FTOptix.NetLogic;
using FTOptix.Store;
#endregion

public class DataGridSynchronizer : BaseNetLogic
{
    public override void Start()
    {
        // Insert code to be executed when the user-defined logic is started
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    [ExportMethod]
    public void QueryOnStore(string tableName)
    {
        queryTask?.Dispose();
        var arguments = new object[] { tableName };
        queryTask = new LongRunningTask(QueryAndUpdate, arguments, LogicObject);
        queryTask.Start();
    }

    private void QueryAndUpdate(LongRunningTask myTask, object args)
    {
        // Get the table name from the arguments
        var argumentsArray = (object[])args;
        var tableName = (string)argumentsArray[0];

        if (string.IsNullOrEmpty(tableName))
            return;

        // Get the DataGrid from the pointer variable. 
        // The Pointer is can be assigned dynamically from the UI or can be hardcoded if the Datasotre is fixed.
        var databaseNodeId = LogicObject.GetVariable("Database_Pointer").Value;
        var targetDatabase = InformationModel.Get<Store>(databaseNodeId);

        // Get the DataGrid from the pointer variable. 
        // The Pointer is can be assigned dynamically from the UI or can be hardcoded if the DataGrid is fixed.
        var dataGridNodeId = LogicObject.GetVariable("DataGrid_Pointer").Value;
        var targetDataGrid = InformationModel.Get<DataGrid>(dataGridNodeId);

        if (targetDatabase == null || targetDataGrid == null)
            return;

        // Reset the grid
        targetDataGrid.Query = "";

        // Prepare the query
        var query = $"SELECT * FROM {tableName}";

        // Execute the query
        targetDatabase.Query(query, out String[] header, out Object[,] resultSet);

        if (header == null || resultSet == null)
            return;

        // Clear existing rows
        targetDataGrid.Columns.Clear();

        // Create columns based on the header (List of Column Names)
        foreach (var columnName in header)
        {
            var newDataGridColumn = InformationModel.MakeObject<DataGridColumn>(columnName);
            newDataGridColumn.Title = columnName;
            newDataGridColumn.DataItemTemplate = InformationModel.MakeObject<DataGridLabelItemTemplate>("DataItemTemplate");
            var dynamicLink = InformationModel.MakeVariable<DynamicLink>("DynamicLink", FTOptix.Core.DataTypes.NodePath);
            dynamicLink.Value = "{Item}/" + NodePath.EscapeNodePathBrowseName(columnName);
            newDataGridColumn.DataItemTemplate.GetVariable("Text").Refs.AddReference(FTOptix.CoreBase.ReferenceTypes.HasDynamicLink, dynamicLink);
            newDataGridColumn.OrderBy = dynamicLink.Value;
            targetDataGrid.Columns.Add(newDataGridColumn);
        }


        // Add the new query to the grid
        // Adding an order by (First Column) to ensure the grid is populated. It can be changes from the UI if not needed.
        targetDataGrid.Query = query + " ORDER BY " + header[0]; 
    }

    private LongRunningTask queryTask;
}
