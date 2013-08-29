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
using System;

namespace WorkerRole
{
    public class WorkerRole : RoleEntryPoint
    {
        RunMe runMe = new RunMe();

        private void LogEvent(string eventName, string notes)
        {
            Log log = new Log(RoleEnvironment.GetConfigurationSettingValue("LogConnectionString"));
            log.WriteEntry(eventName, notes, "");
        }

        public override void Run()
        {
            try
            {
                runMe.Run();
            }
            catch (Exception e)
            {
                LogEvent("RunException", e.ToString());
            }
        }

        public override bool OnStart()
        {
            try
            {
                runMe.OnStart();
                return base.OnStart();
            }
            catch (Exception e)
            {
                LogEvent("OnStartException", e.ToString());
                return false;
            }
        }

        public override void OnStop()
        {
            try
            {
                runMe.OnStop();
                base.OnStop();
            }
            catch (Exception e)
            {
                LogEvent("OnStopException", e.ToString());
            }
        }
    }
}
