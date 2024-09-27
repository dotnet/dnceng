
using Microsoft.DncEng.CommandLineLib;
using Mono.Options;
using System;

namespace Microsoft.DncEng.SecretManager.Commands
{
    /// <summary>
    /// This class is used to extend the CommandLineLib.GlobalCommand class to add a global options spacific to this project
    /// </summary>
    public class ProjectBaseCommand : GlobalCommand
    {

        /// <summary>
        /// Indictes if the global option for 'quiet' is set
        /// </summary>
        public bool Quiet { get { return Verbosity == VerbosityLevel.Quiet; } }

        /// <summary>
        /// Check for local environment values to indicate you are running for Azure DevOps
        /// SYSTEM_COLLECTIONURI is a default environment variable in Azure DevOps
        /// </summary>
        private bool RunningInAzureDevOps = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SYSTEM_COLLECTIONURI"));

        /// <summary>
        /// Provides the ServiceTreeId set with global options
        /// The ID is a goid and is set to Guid.Empty if not set
        /// </summary>
        public Guid ServiceTreeId = Guid.Empty;

        /// <summary>
        /// Base constructor for the ProjectBaseCommand class
        /// </summary>
        public ProjectBaseCommand(GlobalCommand globalCommand)
        {
            Verbosity = globalCommand.Verbosity;
            Help = globalCommand.Help;
        }

        /// <summary>
        /// Overides the GetOptions method from the base class to add a cusotm option for the ServiceTreeId
        /// </summary>
        public override OptionSet GetOptions()
        {
            return new OptionSet()
            {
                {"servicetreeid=", "The service tree ID (must be a valid GUID id from aka.ms/servicetree)", id =>
                    {
                        if (Guid.TryParse(id, out var guid))
                        {
                            ServiceTreeId = guid;
                        }
                        // If running in Azure DevOps use VSO tagging in the console output to the warning message will be handled by the Azure DevOps build system
                        else if (RunningInAzureDevOps)
                        {
                            WriteWarningMessage($"##vso[task.logissue type=warning]Failed to parse a valid Guid value from ServiceTreeId value '{id}'! Security Audit logging will be suppressed!");
                        }
                        // Else write a general warning messgae to console
                        else
                        {
                            WriteWarningMessage($"Failed to parse a valid Guid value from ServiceTreeId value '{id}'! Security Audit logging will be suppressed!");
                        }
                    }
                } 
            };
        }

        /// <summary>
        /// Provides a non-volitie warning message if the ServiceTreeId option is set to a empty guid value and argments have been parsed
        internal void ValidateServiceTreeIdOption()
        {
            if (!Quiet && ServiceTreeId == Guid.Empty)
            {
                // If running in Azure DevOps use VSO tagging in the console output to the warning message will be handled by the Azure DevOps build system
                if (RunningInAzureDevOps)
                {
                    WriteWarningMessage("##vso[task.logissue type=warning]ServiceTreeId is set to an Empty Guid! Security Audit logging will be suppressed!");
                }
                // Else write a general warning messgae to console
                else
                {
                    WriteWarningMessage("ServiceTreeId is set to an Empty Guid! Security Audit logging will be suppressed!");
                }
            }
        }

        internal void WriteWarningMessage(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
}