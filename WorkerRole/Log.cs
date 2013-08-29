 #region Copyright (c) 2010 - 2013 Two10degrees Ltd
//
// (C) Copyright 2010 - 2013 Two10degrees Ltd
//      All rights reserved.
//
// This software is provided "as is" without warranty of any kind,
// express or implied, including but not limited to warranties as to
// quality and fitness for a particular purpose. Two10degrees Ltd
// does not support the Software, nor does it warrant that the Software
// will meet your requirements or that the operation of the Software will
// be uninterrupted or error free or that any defects will be
// corrected. Nothing in this statement is intended to limit or exclude
// any liability for personal injury or death caused by the negligence of
// Two10degrees Ltd, its employees, contractors or agents.
//
#endregion

using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;

namespace WorkerRole
{
    public class Log
    {
        CloudTableClient cloudTableClient;

        public Log(string connectionString)
        {
            CloudStorageAccount account = CloudStorageAccount.Parse(connectionString);
            cloudTableClient = account.CreateCloudTableClient();
        }

        public void WriteEntry(string eventName, string notes, string label)
        {
            try
            {
                string tableName = RoleEnvironment.GetConfigurationSettingValue("LogTableName");

                CloudTable table = cloudTableClient.GetTableReference(tableName);
                table.CreateIfNotExists();

                TableOperation insertOperation = TableOperation.Insert(new LogEntry(eventName, notes, label));
                table.Execute(insertOperation);

            }
            catch (Exception e)
            {
                Tracer.WriteLine(e.ToString(), "Critical");
            }
        }
    }
}
