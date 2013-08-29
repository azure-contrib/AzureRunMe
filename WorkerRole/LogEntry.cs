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
using Microsoft.WindowsAzure.Storage.Table;
using System;

namespace WorkerRole
{
    public class LogEntry : TableEntity
    {
        public LogEntry()
        { }

        public LogEntry(string eventName, string notes, string label)
        {
            DateTime createdUtc = DateTime.UtcNow;

            // Use reverse time so that entries are sorted chronologically with latest entries appearing first
            this.PartitionKey = String.Format("{0}_{1}", (DateTime.MaxValue - createdUtc).Ticks.ToString("d19"), Guid.NewGuid().ToString());

            this.RowKey = RoleEnvironment.DeploymentId;

            this.Timestamp = createdUtc;
            this.InstanceId = RoleEnvironment.CurrentRoleInstance.Id;
            this.EventName = eventName;
            this.Notes = notes;
            this.RoleInstanceId = RoleEnvironment.CurrentRoleInstance.Id;
            this.MachineName = Environment.MachineName;
            this.Label = label;

        }

        public string InstanceId { get; set; }
        public string EventName { get; set; }
        public string Notes { get; set; }
        public string RoleInstanceId { get; set; }
        public string MachineName { get; set; }
        public string Label { get; set; }
    }
}