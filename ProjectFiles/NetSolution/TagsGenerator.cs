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
using FTOptix.OPCUAServer;
using FTOptix.Store;
using FTOptix.ODBCStore;
using FTOptix.DataLogger;
using FTOptix.RAEtherNetIP;
using FTOptix.CommunicationDriver;
using System.Runtime.Versioning;
#endregion

public class TagsGenerator : BaseNetLogic
{
    [ExportMethod]
    public void GenerateTags()
    {
        var tagsFolder = Project.Current.Get<Folder>("Model/OPCUA_Tags");

        

        // Generate 20k Int32 tags
        for (int i = 0; i < 20000; i++)
        {
            var intTag = InformationModel.MakeVariable($"Int{i}", OpcUa.DataTypes.Int32);
            tagsFolder.Add(intTag);
        }

        // Generate 20k String tags
        for (int i = 0; i < 20000; i++)
        {
            var stringTag = InformationModel.MakeVariable($"String{i}", OpcUa.DataTypes.String);
            tagsFolder.Add(stringTag);
        }

        // Generate 20k Double tags
        for (int i = 0; i < 20000; i++)
        {
            var doubleTag = InformationModel.MakeVariable($"Double{i}", OpcUa.DataTypes.Float);
            tagsFolder.Add(doubleTag);
        }

        // Generate 20k Float (Single) tags
        /* for (int i = 0; i < 20000; i++)
        {
            var floatTag = InformationModel.MakeVariable($"Float{i}", OpcUa.DataTypes.Float);
            tagsFolder.Add(floatTag);
        }
        */
    }
}
