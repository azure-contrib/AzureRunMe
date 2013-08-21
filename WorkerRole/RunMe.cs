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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Timers;
using System.Xml;
using System.Linq;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.Diagnostics.Management;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using SevenZip;
using Microsoft.WindowsAzure.StorageClient;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;


namespace WorkerRole
{
    public class RunMe
    {
        Log log;

        CloudDrive cloudDrive = null;

        List<Process> processes = null;

        bool isRoleStopping = false;
        bool roleIsBusy = false;

        string approot;

        System.Timers.Timer timer;

        // Configuration setting keys
        const string TRACE_FORMAT = "TraceFormat";
        const string DATA_CONNECTION_STRING = "DataConnectionString";
        const string PACKAGES = "Packages";
        const string WORKING_DIRECTORY = "WorkingDirectory";
        const string COMMANDS = "Commands";
        const string CLOUD_DRIVE_CONNECTION_STRING = "CloudDriveConnectionString";
        const string CLOUD_DRIVE = "CloudDrive";
        const string CLOUD_DRIVE_SIZE = "CloudDriveSize";
        const string DEFAULT_CONNECTION_LIMIT = "DefaultConnectionLimit";
        const string LABEL = "Label";
        const string UPDATE_INDICATOR = "UpdateIndicator";
        const string PRE_UPDATE_SLEEP = "PreUpdateSleep";
        const string POST_UPDATE_COMMANDS = "PostUpdateCommands";
        const string PRE_UPDATE_COMMANDS = "PreUpdateCommands";
        const string LOG_CONNECTION_STRING = "LogConnectionString";
        const string DONT_EXIT = "DontExit";

        public RunMe()
        {
            log = new Log(RoleEnvironment.GetConfigurationSettingValue(LOG_CONNECTION_STRING));

            timer = new System.Timers.Timer();
            timer.Enabled = false;
            timer.Elapsed += OnTimedEvent;
        }

        public static string GetAzureRunMeVersion()
        {
            return new AssemblyName(Assembly.GetExecutingAssembly().FullName).Version.ToString();
        }

        public string GetWindowsAzureSDKVersion()
        {
            try
            {
                // Warning this is undocumented and could break in a future SDK release
                string roleModelFile = Environment.GetEnvironmentVariable("RoleRoot") + "\\RoleModel.xml";
                XmlDocument xmlDocument = new XmlDocument();
                xmlDocument.Load(roleModelFile);

                return xmlDocument.GetElementsByTagName("RoleModel")[0].Attributes["version"].Value;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        /// <summary>
        /// Expands keywords in the buffer to allow configuration strings
        /// to be set dynamically at runtime
        /// </summary>
        private string ExpandKeywords(string buffer)
        {
            buffer = buffer.Replace("$approot$", approot);
            buffer = buffer.Replace("$deploymentid$", RoleEnvironment.DeploymentId);
            buffer = buffer.Replace("$roleinstanceid$", RoleEnvironment.CurrentRoleInstance.Id);
            buffer = buffer.Replace("$computername$", Environment.MachineName);
            buffer = buffer.Replace("$guid$", Guid.NewGuid().ToString());
            buffer = buffer.Replace("$now$", DateTime.Now.ToString());
            buffer = buffer.Replace("$roleroot$", Environment.GetEnvironmentVariable("RoleRoot"));
            buffer = buffer.Replace("$version$", GetAzureRunMeVersion());

            if (cloudDrive != null)
                buffer = buffer.Replace("$clouddrive$", cloudDrive.LocalPath);

            return buffer;
        }

        /// <summary>
        /// Configures the maximum number of concurrent outbound connections
        /// </summary>
        private void ConfigureDefaultConnectionLimit()
        {
            int limit = int.Parse(RoleEnvironment.GetConfigurationSettingValue(DEFAULT_CONNECTION_LIMIT));
            ServicePointManager.DefaultConnectionLimit = limit;

            Tracer.WriteLine(string.Format("ServicePointManager.DefaultConnectionLimit = {0}", limit), "Information");
        }

        private void ConfigureTraceFormat()
        {
            string traceFormat = RoleEnvironment.GetConfigurationSettingValue(TRACE_FORMAT);
            Tracer.format = ExpandKeywords(traceFormat);

            Tracer.WriteLine(string.Format("Tracer.format = {0}", traceFormat), "Information");
        }

        private static void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            Tracer.WriteLine(string.Format("OnTimedEvent Start {0}", e.SignalTime), "Information");

            try
            {
                string[] filePaths = RoleEnvironment.GetConfigurationSettingValue("ScheduledTransferFiles").Split(';');

                if (filePaths.Length > 0)
                {

                    CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                        RoleEnvironment.GetConfigurationSettingValue("Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString"));
                    CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

                    CloudBlobContainer blobContainer = blobClient.GetContainerReference("azurerunme-files");
                    blobContainer.CreateIfNotExists();

                    foreach (string filePath in filePaths)
                    {
                        try
                        {
                            Tracer.WriteLine(string.Format("Copying {0}", filePath), "Information");
                            string fileName = Path.GetFileName(filePath);
                            CloudBlockBlob blockBlob = blobContainer.GetBlockBlobReference(fileName);
                            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                blockBlob.UploadFromStream(fileStream);
                            }
                        }
                        catch (Exception ex)
                        {
                            Tracer.WriteLine(ex.Message, "Error");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Tracer.WriteLine(ex.Message, "Error");
            }

            Tracer.WriteLine("OnTimedEvent Completed", "Information");
        }

        /// <summary>
        /// Creates a package receipt (a simple text file in the temp directory) 
        /// to record the successful download and installation of a package
        /// </summary>
        private void WritePackageReceipt(string receiptFileName)
        {
            TextWriter textWriter = new StreamWriter(receiptFileName);
            textWriter.WriteLine(DateTime.Now);
            textWriter.Close();

            Tracer.WriteLine(string.Format("Writing package receipt {0}", receiptFileName), "Information");
        }

        /// <summary>
        /// Checks a package in Blob Storage against any previous package receipt
        /// to determine whether to reinstall it
        /// </summary>
        private bool IsNewPackage(string containerName, string packageName, string packageReceiptFile)
        {
            var storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue(DATA_CONNECTION_STRING));

            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            blobClient.RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(2), 10);

            CloudBlobContainer container = blobClient.GetContainerReference(containerName);
            CloudBlockBlob blob = container.GetBlockBlobReference(packageName);

            blob.FetchAttributes();

            DateTime blobTimeStamp = DateTime.MaxValue;
            if (blob.Properties.LastModified.HasValue)
            {
                blobTimeStamp = blob.Properties.LastModified.Value.UtcDateTime;
            }

            DateTime fileTimeStamp = File.GetCreationTimeUtc(packageReceiptFile);

            if (fileTimeStamp.CompareTo(blobTimeStamp) < 0)
            {
                Tracer.WriteLine(string.Format("{0} is new or not yet installed.", packageName), "Information");
                return true;
            }
            else
            {
                Tracer.WriteLine(string.Format("{0} has previously been installed, skipping download.", packageName), "Information");
                return false;
            }
        }

        /// <summary>
        /// Download a package from blob storage and unzip it
        /// </summary>
        /// <param name="containerName">The Blob storage container name</param>
        /// <param name="packageName">The name of the zip file package</param>
        /// <param name="workingDirectory">Where to extract the files</param>
        private void InstallPackage(string containerName, string packageName, string workingDirectory)
        {

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue(DATA_CONNECTION_STRING));

            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            blobClient.RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(2), 10);

            CloudBlobContainer container = blobClient.GetContainerReference(containerName);
            CloudBlockBlob blob = container.GetBlockBlobReference(packageName);

            Tracer.WriteLine(string.Format("Downloading {0} to {1}", blob.Uri, workingDirectory), "Information");

            using (MemoryStream stream = new MemoryStream())
            {
                blob.DownloadToStream(stream);

                Tracer.WriteLine(string.Format("Extracting {0}", packageName), "Information");

                using (SevenZipExtractor extractor = new SevenZipExtractor(stream))
                {
                    // set 7zip dll path
                    string sevenZipPath = Path.Combine(Directory.GetCurrentDirectory(), @"Redist\7z64.dll");
                    SevenZipExtractor.SetLibraryPath(sevenZipPath);
                    extractor.ExtractArchive(workingDirectory);
                }
            }

            Tracer.WriteLine("Extraction finished", "Information");
        }

        private static void SetEnvironmentVariable(ProcessStartInfo startInfo, string variable, string value)
        {
            try
            {
                startInfo.EnvironmentVariables.Add(variable, value);
                Tracer.WriteLine(string.Format("Setting %{0}% to {1}", variable, value), "Information");
            }
            catch (ArgumentException)
            {
                Tracer.WriteLine(string.Format("Environment Variable %{0}% already set", variable), "Information");
            }
        }

        /// <summary>
        /// Runs a batch file or executable and hooks up stdout and stderr
        /// </summary>
        /// <param name="workingDirectory">Directory on disk</param>
        /// <param name="script">Batch file name (e.g. runme.bat)</param>
        private Process Run(string workingDirectory, string environmentVariables, string batchFile)
        {
            const string IP_ADDRESS = "ipaddress";
            const string DEPLOYMENT_ID = "deploymentid";
            const string ROLE_INSTANCE_ID = "roleinstanceid";
            const string CLOUD_DRIVE = "clouddrive";

            string command = Path.Combine(
                    workingDirectory,
                    batchFile);

            ProcessStartInfo startInfo = new ProcessStartInfo(command)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };

            EnvironmentVariables(startInfo, environmentVariables);

            // Set an environment variable for each InstanceEndPoint
            foreach (var endpoint in RoleEnvironment.CurrentRoleInstance.InstanceEndpoints)
            {
                string variable = endpoint.Key;
                string value = endpoint.Value.IPEndpoint.Port.ToString();

                SetEnvironmentVariable(startInfo, variable, value);

                if (!startInfo.EnvironmentVariables.ContainsKey(IP_ADDRESS))
                {
                    string ipAddress = endpoint.Value.IPEndpoint.Address.ToString();
                    SetEnvironmentVariable(startInfo, IP_ADDRESS, ipAddress);
                }
            }

            SetEnvironmentVariable(startInfo, DEPLOYMENT_ID, RoleEnvironment.DeploymentId);
            SetEnvironmentVariable(startInfo, ROLE_INSTANCE_ID, RoleEnvironment.CurrentRoleInstance.Id);

            if (cloudDrive != null)
            {
                SetEnvironmentVariable(startInfo, CLOUD_DRIVE, cloudDrive.LocalPath);
            }

            Tracer.WriteLine(string.Format("Start Process {0}", command), "Information");

            Process process = new Process()
            {
                StartInfo = startInfo
            };

            process.ErrorDataReceived += (sender, e) => { Tracer.WriteLine(e.Data, "Information"); };
            process.OutputDataReceived += (sender, e) => { Tracer.WriteLine(e.Data, "Information"); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Tracer.WriteLine(string.Format("Process {0}", process.Handle), "Information");

            return process;
        }

        public string GetLabel()
        {
            return ExpandKeywords(RoleEnvironment.GetConfigurationSettingValue(LABEL));
        }

        public string GetWorkingDirectory()
        {
            return ExpandKeywords(RoleEnvironment.GetConfigurationSettingValue(WORKING_DIRECTORY));
        }

        public bool OnStart()
        {
            log.WriteEntry("OnStart", "", GetLabel());

            approot = Directory.GetCurrentDirectory();

            ConfigureTraceFormat();

            Tracer.WriteLine("OnStart", "Information");
            Trace.AutoFlush = true;

            Tracer.WriteLine("", "Information");
            Tracer.WriteLine(string.Format("AzureRunMe {0} on Windows Azure SDK {1}",
                GetAzureRunMeVersion(), GetWindowsAzureSDKVersion()), "Information");

            Tracer.WriteLine("Copyright (c) 2010 - 2013 Two10degrees Ltd [www.aws.net]", "Information");
            Tracer.WriteLine("", "Information");

            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.
            RoleEnvironment.Changing += RoleEnvironmentChanging;
            RoleEnvironment.Changed += RoleEnvironmentChanged;

            RoleEnvironment.StatusCheck += RoleEnvironmentStatusCheck;
            RoleEnvironment.Stopping += RoleEnvironmentStopping;

            ConfigureDefaultConnectionLimit();

            Tracer.WriteLine(string.Format("Label: {0}", GetLabel()), "Information");

            Tracer.WriteLine(string.Format("DeploymentId: {0}", RoleEnvironment.DeploymentId), "Information");
            Tracer.WriteLine(string.Format("RoleInstanceId: {0}", RoleEnvironment.CurrentRoleInstance.Id), "Information");
            Tracer.WriteLine(string.Format("MachineName: {0}", Environment.MachineName), "Information");
            Tracer.WriteLine(string.Format("ProcessorCount: {0}", Environment.ProcessorCount), "Information");
            Tracer.WriteLine(string.Format("Time: {0}", DateTime.Now), "Information");

            try
            {
                // set 7zip dll path
                string sevenZipPath = Path.Combine(approot, @"Redist\7z64.dll");
                SevenZipExtractor.SetLibraryPath(sevenZipPath);

                InstallPackages();

                string commands = RoleEnvironment.GetConfigurationSettingValue("OnStartCommands");
                WaitForCommandsExit(RunCommands(commands));
            }
            catch (Exception e)
            {
                Tracer.WriteLine(e, "Error");
            }

            Tracer.WriteLine("Started", "Information");

            log.WriteEntry("Started", "", GetLabel());

            return true;
        }


        private void InstallPackages()
        {
            Tracer.WriteLine("InstallPackages", "Information");

            bool alwaysInstallPackages = bool.Parse(RoleEnvironment.GetConfigurationSettingValue("AlwaysInstallPackages"));
            Tracer.WriteLine(string.Format("AlwaysInstallPackages: {0}", alwaysInstallPackages), "Information");

            string workingDirectory = GetWorkingDirectory();

            // Retrieve the semicolon delimitted list of zip file packages and install them
            string[] packages = RoleEnvironment.GetConfigurationSettingValue(PACKAGES).Split(';');
            foreach (string package in packages)
            {
                try
                {
                    if (package != string.Empty)
                    {
                        // Parse out the container\file pair
                        string[] fields = package.Split(new char[] { '/', '\\' }, 2);

                        string containerName = fields[0];
                        string packageName = fields[1];
                        if (packageName == "*")
                        {
                            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue(DATA_CONNECTION_STRING));
                            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                            var container = blobClient.GetContainerReference(containerName);
                            foreach (var blobListItem in container.ListBlobs().OrderBy(x => x.Uri.ToString()))
                            {
                                var blob = container.GetBlobReferenceFromServer(blobListItem.Uri.ToString());
                                InstallPackageIfNewer(alwaysInstallPackages, workingDirectory, containerName, blob.Name);
                            }
                        }
                        else
                        {
                            InstallPackageIfNewer(alwaysInstallPackages, workingDirectory, containerName, packageName);
                        }
                    }
                }
                catch (Exception e)
                {
                    Tracer.WriteLine(string.Format("Package \"{0}\" failed to install, {1}", package, e), "Information");
                }
            }
        }

        private void InstallPackageIfNewer(bool alwaysInstallPackages, string workingDirectory, string containerName, string packageName)
        {
            try
            {
                string packageReceiptFileName = Path.Combine(workingDirectory, packageName + ".receipt");

                if (alwaysInstallPackages || IsNewPackage(containerName, packageName, packageReceiptFileName))
                {
                    InstallPackage(containerName, packageName, workingDirectory);
                    WritePackageReceipt(packageReceiptFileName);
                }
            }
            catch (Exception e)
            {
                Tracer.WriteLine(string.Format("Package \"{0}\" failed to install, {1}", packageName, e), "Information");
            }
        }

        private void KillProcesses()
        {
            if (processes != null)
                foreach (Process process in processes)
                {
                    Tracer.WriteLine(string.Format("Killing process: {0}", process.Id), "Information");
                    process.Kill();
                    Tracer.WriteLine(string.Format("Process: {0} killed", process.Id), "Information");
                }
        }

        private List<Process> RunCommands()
        {
            string commands = RoleEnvironment.GetConfigurationSettingValue(COMMANDS);
            return RunCommands(commands);

        }

        public void Run()
        {
            roleIsBusy = false;

            Tracer.WriteLine("Running", "Information");
            log.WriteEntry("Running", "", GetLabel());

            try
            {

                WaitForCommandsExit(RunCommands());

                // If DontExit is set, then keep runing even though all the Commands have finished
                // (Useful if you want to RDP in afterwards). 
                bool dontExit = bool.Parse(RoleEnvironment.GetConfigurationSettingValue(DONT_EXIT));

                Tracer.WriteLine(string.Format("DontExit: {0}", dontExit), "Information");

                if (dontExit)
                    while (!isRoleStopping)
                        Thread.Sleep(1000);

                Tracer.WriteLine("Run method exiting", "Information");

            }
            catch (Exception e)
            {
                Tracer.WriteLine(e, "Error");
            }

            Tracer.WriteLine("Stopping", "Critical");
            log.WriteEntry("Stopping", "", GetLabel());
        }

        private void UnmountCloudDrive()
        {
            if (cloudDrive != null)
            {
                Tracer.WriteLine(string.Format("Unmounting {0} from {1}", cloudDrive.Uri, cloudDrive.LocalPath), "Information");
                cloudDrive.Unmount();
            }
        }

        public void OnStop()
        {
            Tracer.WriteLine("OnStop", "Critical");
            log.WriteEntry("OnStop", "", GetLabel());

            isRoleStopping = true;

            string commands = RoleEnvironment.GetConfigurationSettingValue("OnStopCommands");
            WaitForCommandsExit(RunCommands(commands));

            UnmountCloudDrive();

            Tracer.WriteLine("Stopped", "Critical");
            log.WriteEntry("Stopped", "", GetLabel());
        }

        private void EnvironmentVariables(ProcessStartInfo processStartInfo, string environmentVariables)
        {
            string[] assignments = environmentVariables.Split(';');

            foreach (string assignment in assignments)
            {
                if (!String.IsNullOrEmpty(assignment))
                {
                    string[] parts = assignment.Split('=');
                    SetEnvironmentVariable(processStartInfo, parts[0].Trim(), parts[1].Trim());
                }
            }
        }

        private void WaitForCommandsExit(List<Process> processes)
        {
            // Wait for all processes to exit
            foreach (Process process in processes)
            {
                process.WaitForExit();
                Tracer.WriteLine(string.Format("Process exit {0}, code {1}", process.Handle, process.ExitCode), "Information");
            }
        }

        private List<Process> RunCommands(string commandList)
        {

            string environmentVariables = RoleEnvironment.GetConfigurationSettingValue("EnvironmentVariables");

            string workingDirectory = GetWorkingDirectory();

            // Spawn a new process for each command
            List<Process> processes = new List<Process>();
            string[] commands = commandList.Split(';');
            foreach (string command in commands)
            {
                try
                {
                    if (command != string.Empty)
                    {
                        Process process = Run(workingDirectory, environmentVariables, command);
                        processes.Add(process);
                        Tracer.WriteLine(string.Format("Process {0} started,({1})", process.Handle, command), "Information");
                    }
                }
                catch (Exception e)
                {
                    Tracer.WriteLine(string.Format("Command \"{0}\" , {1}", command, e), "Information");
                }
            }

            return processes;

        }

        private void RoleEnvironmentChanging(object sender, RoleEnvironmentChangingEventArgs e)
        {
            Tracer.WriteLine("RoleEnvironmentChanging", "Information");
            log.WriteEntry("RoleEnvironmentChanging", "", GetLabel());

            // Don't object to any role environment changes
            // See RoleEnvironmentChanged for our attempt to cope with the changes
        }

        private void RoleEnvironmentStatusCheck(object sender, RoleInstanceStatusCheckEventArgs e)
        {
            if (roleIsBusy)
                e.SetBusy();
        }


        private void DoUpdate()
        {
            Tracer.WriteLine("DoUpdate", "Information");

            roleIsBusy = true;

            Tracer.WriteLine("PreUpdateCommands", "Information");

            string commands = RoleEnvironment.GetConfigurationSettingValue(PRE_UPDATE_COMMANDS);
            WaitForCommandsExit(RunCommands(commands));

            // A potential snag is if the user's pre update commands never exit,  but ignore that for now

            // Wait for any asynchronous stop actions like Tomcat
            int sleep = int.Parse(RoleEnvironment.GetConfigurationSettingValue(PRE_UPDATE_SLEEP));
            Tracer.WriteLine(String.Format("Sleeping {0}", sleep), "Information");
            Thread.Sleep(sleep);

            // Hopefully, everything will have shut down cleanly, but just in case ..
            KillProcesses();

            InstallPackages();

            Tracer.WriteLine("PostUpdateCommands", "Information");

            commands = RoleEnvironment.GetConfigurationSettingValue(POST_UPDATE_COMMANDS);
            processes = RunCommands(commands);

            roleIsBusy = false;

            Tracer.WriteLine("DoUpdate Finished", "Information");
        }

        private void RoleEnvironmentStopping(object sender, RoleEnvironmentStoppingEventArgs e)
        {
            Tracer.WriteLine("RoleEnvironmentStopping ", "Information");
            log.WriteEntry("RoleEnvironmentStopping", "", GetLabel());
        }

        private void RoleEnvironmentChanged(object sender, RoleEnvironmentChangedEventArgs e)
        {
            Tracer.WriteLine("RoleEnvironmentChanged", "Information");
            log.WriteEntry("RoleEnvironmentChanged", "", GetLabel());

            bool update = false;

            foreach (RoleEnvironmentChange roleEnvironmentChange in e.Changes)
            {
                if (roleEnvironmentChange.GetType() == typeof(RoleEnvironmentConfigurationSettingChange))
                {

                    RoleEnvironmentConfigurationSettingChange change = (RoleEnvironmentConfigurationSettingChange)roleEnvironmentChange;

                    string message = string.Format("{0} = \"{1}\"", change.ConfigurationSettingName, RoleEnvironment.GetConfigurationSettingValue(change.ConfigurationSettingName));

                    Tracer.WriteLine(message, "Information");
                    log.WriteEntry("RoleEnvironmentConfigurationSettingChange", message, GetLabel());

                    switch (change.ConfigurationSettingName)
                    {
                        case TRACE_FORMAT:
                            ConfigureTraceFormat();
                            break;

                        case UPDATE_INDICATOR:
                            update = true;
                            break;

                        case DEFAULT_CONNECTION_LIMIT:
                            ConfigureDefaultConnectionLimit();
                            break;

                    }

                }
                else if (roleEnvironmentChange.GetType() == typeof(RoleEnvironmentTopologyChange))
                {
                    Tracer.WriteLine("RoleEnvironmentTopologyChange", "Information");
                    log.WriteEntry("RoleEnvironmentTopologyChange", "", GetLabel());
                }
                else
                {
                    Tracer.WriteLine("UnknownRoleEnvironmentChange", "Information");
                    log.WriteEntry("UnknownRoleEnvironmentChange", roleEnvironmentChange.GetType().ToString(), GetLabel());
                }
            }

            if (update)
                DoUpdate();
        }
    }
}
