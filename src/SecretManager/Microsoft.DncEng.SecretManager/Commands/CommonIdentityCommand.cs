
using System;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.VisualStudio.Services.Common;
using Mono.Options;


namespace Microsoft.DncEng.SecretManager.Commands
{
    /// <summary>
    /// This class is used to extend the CommandLineLib.Command class to provide common identity value options which can be used by other commands via inheritance
    /// </summary>
    public class CommonIdentityCommand : CommandLineLib.Command
    {
        /// <summary>
        /// Check for local environment values to indicate you are running for Azure DevOps
        /// SYSTEM_COLLECTIONURI is a default environment variable in Azure DevOps
        /// </summary>
        private bool RunningInAzureDevOps = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SYSTEM_COLLECTIONURI"));

        /// <summary>
        /// Provides the ServiceTreeId set with global options
        /// The ID is a guid and is set to the Helix service tree ID by default
        /// </summary>
        public Guid ServiceTreeId { get; private set; } = new Guid("8835b1f3-0d22-4e28-bae0-65da04655ed4");

        // Local console object used to write messages to the console
        private readonly IConsole _console;

        /// <summary>
        /// Base constructor for the CommonIdentityCommand class
        /// </summary>
        public CommonIdentityCommand(IConsole console)
        {
            _console = console;
        }

        /// <summary>
        /// Overrides the GetOptions method from the base class to add a custom option for the ServiceTreeId
        /// </summary>
        public override OptionSet GetOptions()
        {
            var options = base.GetOptions().AddRange(new OptionSet()
            {
                {"servicetreeid=", "Your service tree ID (Ids are defined at aka.ms/servicetree)", id =>
                    {
                        if (Guid.TryParse(id, out var guid))
                        {
                            ServiceTreeId = guid;
                        }
                        else
                        {
                            throw new ArgumentException($"Failed to parse a valid Guid value from ServiceTreeId value '{id}'!");
                        }
                    }
                }
            });
            return options;
        }

        /// <summary>
        /// Provides a non-volitie warning message if the ServiceTreeId option is set to a empty guid value
        internal void WarnIfServiceTreeIdIsSetToEmptyGuid()
        {
            if (ServiceTreeId == Guid.Empty)
            {
                // If running in Azure DevOps use VSO tagging in the console output to the warning message will be handled by the Azure DevOps build system
                if (RunningInAzureDevOps)
                {
                    _console.WriteError("##vso[task.logissue type=warning]ServiceTreeId is set to an Empty Guid!\n");
                }
                // Else write a general warning messgae to console
                else
                {
                    _console.WriteError("ServiceTreeId is set to an Empty Guid!\n");
                }
            }
        }
    }
}