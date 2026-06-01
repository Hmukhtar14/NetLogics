#region Using directives
using FTOptix.CommunicationDriver;
using FTOptix.Core;
using FTOptix.CoreBase;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using UAManagedCore;
using FTOptix.WebUI;
using FTOptix.System;
using OpcUa = UAManagedCore.OpcUa;
#endregion

public class Float_TagsGenerator : BaseNetLogic
{
    
    private int NumberOfTags; 
    
    [ExportMethod]
    public void GenerateTags()
    {
        var tempFolder = InformationModel.Make<Folder>("Float_Tags");

        NumberOfTags = LogicObject.GetVariable("NumberOfTags").Value;

        for (int i = 0; i < NumberOfTags; i++)
        {
            var floatTag = InformationModel.MakeVariable($"Float{i}", OpcUa.DataTypes.Float);

            tempFolder.Add(floatTag);
        }
        Project.Current.Get("Model").Add(tempFolder);
    }
}
