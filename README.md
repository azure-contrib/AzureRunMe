AzureRunMe 1.0.0.21
===================

Probably the quickest way to get your legacy or third-party code running on Windows Azure.

N.B. AzureRunMe has moved to https://github.com/RobBlackwell/AzureRunMe

Introduction
------------

AzureRunMe is a boostrap program that provides an off-the-shelf CSPKG file that you can upload to Windows Azure Compute and just run.

From there you can upload your code via ZIP files in Blob Storage and kick off your processes in a repeatable way, just by changing configuraon.

If you are using Java, Clojure, C++ or other languages this might be the quickest way to get your code running in Azure without having to worry about building any .NET code.

Background
----------

There are a number of code samples that show how to run Java, Ruby, Python etc on Windows Azure, but they all vary in approach and complexity. 
Everyone seems to write their own boostrap program. I thought there ought to be a simplified, standardised way.

I wanted something simple that took a self contained ZIP file, unpacked it and just executed a batch file. 
All the role information like ipaddress and port could be passed as environment variables %IPAddress% or %Http% etc.

I wanted ZIP files to be stored in Blob storage to allow them to be easily updated with all configuration settings in the Azure Service Configuration file.

AzureRunMe was born, and to my very great suprise, is now being used by a number of commercial organisations, hobbyists and even Microsoft themselves!

Example Scenarios
-----------------

* A Tomcat hosted web application
* A JBOSS hosted app
* A legacy C / C++ application
* A Clojure / Compojure app
* A Common Lisp app
* A Delphi back end server
* A Ruby on Rails web app
* Use PortBridgeAgent to proxy some ports from an intranet server e.g. LDAP.
* Use PortBridge to proxy internal endpoints back to on premises e.g. JPDA to your Eclipse debugger


Getting Started
---------------

Organise your project so that it can all run from under one directory and has a batch file at the top level.

In my case, I have a directory called c:\foo. Under that I have copied the Java Runtime JRE. I have my JAR files in a subdirectory called test and a runme.bat above those that looks like this:

	cd test
	..\jre\bin\java -cp Test.jar;lib\* Test %http%

I can bring up a console window using cmd and change directory into Foo

Then I can try things out locally by typing:

	C:>Foo> set http=8080
	C:>Foo> runme.bat

The application runs and serves a web page on port 8080.

I package the jre directory as jre.zip and the test directory along with the runme.bat file together as dist.zip.

Having two ZIP files saves me time - I don't have to keep uploading the JRE each time I change my Java application.

My colleague has a ruby.zip file containing Ruby and Mongrel and his web application in rubyapp.zip in a similar way.

Upload the zip files to blob store. Create a container called "packages" and put them in there. The easiest way to do this is via Cerebrata Cloud Studio. Another alternative is to use the UploadBlob command line app distributed with this project.

The next step is to build and deploy Azure RunMe ..

Load Azure RunMe in Visual Studio and build.

Change the ServiceConfiguration.cscfg file: 

Update DiagnosticsConnectionString with your Windows Azure Storage account details so that AzureRunme can send trace information to storage. 

Update DataConnectionString with your Windows Azure Storage account details so that AzureRunme can get ZIP files from Blob store. 

By default, Packages is set to "packages\jre.zip;packages\dist.zip"  which means download and extract jre.zip then download and extract  dist.zip, before executing runme.bat (Specified as the Command configuration setting.

Click on AzureRunMe and Publish your Azure package. Sign into the [Windows Azure Developer Portal](http://windows.azure.com).

Create a New Hosted Service and upload the package and config to your Windows Azure account. You are nearly ready to go.

Change the app.config file for TraceConsole to include your own service bus credentials.

Right Click, Publish, Configure Remote Desktop Connection.

Deployment might take some time (maybe 10 minutes or more).

If all goes well your app should now be running in the cloud!

Environment Variables
---------------------

Before running your script, the following environment variables are set up

* %IPAddress% to the IP Address of this instance.

All InputEndPoints are setup too, according to the CSDEF, something like is:

* %http% the port corresponding to 80
* %telnet% the port corresponding to 23
* %http-alt% the port corresponding to 8080


Compiling AzureRunMe
--------------------

Prerequisites:

* Visual Studio 2010
* The Windows Azure SDK v1.6

Diagnostics
-----------

By default, log files, event logs and some performance counters are written to table storage using Windows Azure Diagnostics.
This is all controlled from the diagnostics.wadcfg file.

I recommend Cerebrata's [Windows Azure Diagostics Manager](http://www.cerebrata.com/products/AzureDiagnosticsManager/Default.aspx) for viewing
the output.

You can now copy arbitrary log files (e.g. Tomcat logs) ffrom AzureRunMe to the Blob storage azurerunme-files container using:


Packages
--------

You store your packages as a series of ZIP files in Azure Blob Store

The following config setting controls which files are extacted and in which order.

		<Setting name="Packages" value="packages/jre.zip;packages/dist.zip" />

It's usually a good idea to separate your deployment up into several packages so that you dont have large uploads everytime something changes. Here is a recent example

	<Setting name="Packages" value="packages/jdk1.6.0_21.zip;packages/sed.zip;packages/portbridge.zip;packages/apache-tomcat-6.0.29.zip" />

Commands
--------

To run a single command, use a config like this

	<Setting name="Commands" value="runme.bat"/>

If you want to start multiple processes, you can specify them in a semicolon separated list, like this

	<Setting name="Commands" value="portbridge.exe;tomcat.bat"/>

If you leave Commands blank and set DontExit, like this
	
	<Setting name="Commands" value=""/>
	<Setting name="DontExit" value="True" />

Then the instance boots up without running any code, but you can still remote desktop in and start playing.

Optionally you can run some commands when the instance is starting (i.e. via the OnStart method which runs before the load balancer is directing traffic)

	<Setting name="OnStartCommands" value="start.bat" />

Optionally you can run some commands when the instance is stopped (i.e. via the OnStop method)

	<Setting name="OnStopCommands" value="cleanup.bat"/>

DefaultConnectionLimit
----------------------

The default connection limit specifies the number of outbound TCP connections.If your code makes a lot of outbound requests, you may need to tweak this.

		<Setting name="DefaultConnectionLimit" value ="12"/>

Configuration Keyword Expansions
--------------------------------

Several of the configuration file settings support expansion of these variables

* $deploymentid$ expands to the deployment id - something like 3bdbf69e94c645f1ab19f2e428eb05fe
* $roleinstanceid$ expands to the role instance id - something like {"WorkerRole_IN_0"}
* $computername$ expands to the computer NETBIOS name - something like RD00155D3A111A
* $guid$ expands to a new Globally Unique Identifier
* $now$ expands to DateTime.Now (the current time).
* $roleroot$" expands to the role root directory
* $clouddrive$" expands to the directory where the clouddrive is mounted
* $approot$ expands to $roleroot$\approot
* $version$ expands to the AzureRunMe version e.g. 1.0.0.18

Cloud Drives
------------

You can mount a Windows Azure Drive (aka X-Drive), using the following config settings

	<Setting name="CloudDriveConnectionString" value="DefaultEndpointsProtocol=http;AccountName=YOURACCOUNTNAME;AccountKey=YOURACCOUNTKEY" />
	<Setting name="CloudDrive" value ="drives\mydrive.vhd"/>
	<Setting name="CloudDriveSize" value="64" />

The CloudDriveConnectionString is separate to the DataConnectionString because cloud drives don't
support HTTPS.

CloudDrive supports the same expansion syntax as the service path above so that VHD file names can
be tailored according to your requirements. "drives\$computername$.vhd" is often useful.

CloudDriveSize is specified in megabytes.

At run time, the cloud drive location is available through the %clouddrive% environment variable.

DontExit
--------

If DontExit is set to True then the WorkerRole's Run Method wont exit until the WorkerRole is explicitly stopped using OnStop.
If DontExit is set to False then the WorkerRole's Run Method will exit as soon as any processes created from the "Commands" section have exited.

	<Setting name="DontExit" value="True" />


AlwaysInstallPackages
---------------------

AlwaysInstallPackages True ensures that packages are always downloaded and extracted from Blob Storage even if they have previously been installed 
(This is the default and backwards compatible behaviour). 

	<Setting name="AlwaysInstallPackages" value="True" />

Setting AlwaysInstall to False can optimise the time it takes to restart an instance, by only reinstalling packages that have been updated in Blob Store.


Batch file tricks
-----------------

It's common to kick off your processes from a batch file and its idoimatic to call it runme.bat

It can contain all the old-fashioned DOS-like commands echo etc.

I often use

	SET

to display a list of all the environment variables.

Some useful variables include: 

	%ipaddress%  
	%computername%
	%deploymentid%
	%roleinstanceid%

I have a copy of SED (The Unix Stream editor) packaged in a ZIP, and this allows me to perform simple file based configurations changes:

When I start tomcat, I do it like this rather than using the startup script

	apache-tomcat\bin\catalina.bat run

Whilst the CMD shell doesnt really have proper job control, you can start background processes with the START command.

Issues
------

Unfortunately we cant configure more that 5 end points on the Load balancer, so you may need to recompile with your own CSDEF file to fiddle with ports.

The vmsize attribute is unfortunately, baked into the CSDEF file, so specifiying a different instance size also needs a recompile.

When you RDP into AzureRunMe and start a web server from, say, a command prompt it may be that you can only see it locally on the VM and not
via the load balancer. Not yet sure if this is an issue with process context or firewall rules. Currrent work around is that you must start
your web server within a runme.bat or similar so that AzureRunMe can start it as part of its Run method. UPDATE - I think this is a firewall
permissions issue - runme.bat runs as a USER, in remote desktop you are an ADMINISTRATOR.

If you specify PreUpdate or PostUpdate commands and these block, then your role might go unhealthy and the fabric might restart your instance.

Credits
-------

SevenZipSharp http://sevenzipsharp.codeplex.com/ is distributed under the terms of the GNU Lesser General Public License.

Thanks to Jsun for lots of constructive ideas, feature requests and testing.

Thanks to Michal Kruml for various patches.

Thanks to Steve Marx for inspirational code samples and workarounds.

Commercial Support
------------------

This code is now being used for real, on several commercial projects! 

See http://www.two10degrees.com if you'd like to hire us or would like to purchase a formal support contract. We're also on the look out for Window Azure Developers and Architects.


Rob Blackwell

April 2012

