
using System;
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
        /// Provides the ServiceTreeId set with global options
        /// The ID is a guid and is set to the Helix service tree ID by default
        /// </summary>
        public Guid ServiceTreeId { get; private set; } = new Guid("8835b1f3-0d22-4e28-bae0-65da04655ed4");

        /// <summary>
        /// Base constructor for the CommonIdentityCommand class
        /// </summary>
        public CommonIdentityCommand()
        {
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
    }
}