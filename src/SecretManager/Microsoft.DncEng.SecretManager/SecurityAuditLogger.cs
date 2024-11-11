#if INTERNAL
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.DncEng.SecretManager.Commands;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Audit.Geneva;

namespace Microsoft.DncEng.SecretManager
{
    /// <summary>
    /// SecurityAuditLogger is a class that is used to log security audit events to the local event log and Geneva
    /// </summary>
    public class SecurityAuditLogger
    {
        private ILogger ControlPlaneLogger;

        /// <summary>
        /// Constructor for the SecurityAuditLogger that takes a CommonIdentityCommand to extract the service tree id value.
        /// </summary>
        public SecurityAuditLogger(CommonIdentityCommand commonIdentityCommand) : this(commonIdentityCommand.ServiceTreeId)
        {
        }

        /// <summary>
        /// Private constructor that configures the logger for the specified service tree id.
        /// </summary>
        private SecurityAuditLogger(Guid serviceTreeId)
        {
            var auditFactory = AuditLoggerFactory.Create(options =>
            {
                // We use ETW as the destination for the audit logs because the application is not guaranteed to run on windows
                options.Destination = AuditLogDestination.ETW;
                options.ServiceId = serviceTreeId;
            });

            ControlPlaneLogger = auditFactory.CreateControlPlaneLogger();
        }

        /// <summary>
        /// Add an audit log for secret update operations performed on behalf of a user.
        /// </summary>
        public void LogSecretUpdate(ITokenCredentialProvider credentialProvider, string secretName, string secretStoreType, string secretLocation, OperationResult result = OperationResult.Success, string resultMessage = "", [CallerMemberName] string operationName = "")
        {
            try
            {
                LogSecretAction(OperationType.Update, operationName, credentialProvider, secretName, secretStoreType, secretLocation, result, resultMessage);
            }
            // Audit logging is a 'volatile' operation meaning it can throw exceptions if logging fails.
            // This could lead to service instability caused by simple logging issues which is not desirable.
            // So we catch all exceptions and write a safe warding message to console 
            // The hope is that app insights will also catch the base exception for debugging.
            catch 
            {
                Console.WriteLine($"Failed to add audit log for secret update!");
            }            
        }

        private void LogSecretAction(OperationType operationType, string operationName, ITokenCredentialProvider credentialProvider, string secretName, string secretStoreType, string secretLocation, OperationResult result, string resultMessage)
        {
            // The token application id of the client running the assembly.
            // NOTE: The user identity here should be something 'dynamic'.
            // If you are hard coding this value you should question if this Audit Log is useful
            // as it is likely redundant to lower level permission change logging that is already occurring.
            var user = credentialProvider.ApplicationId;
            // Get the tenant ID that provided the token for the credential provider.
            var tenantId = credentialProvider.TenantId;
            var auditRecord = new AuditRecord
            {
                OperationResultDescription = $"Action '{operationType}' For Secret '{secretName}' With Operation '{operationName}' By User '{user}' On Source '{secretLocation}' Resulted In '{result}'.",
                CallerAgent = GetType().Namespace,
                OperationName = operationName,
                OperationType = operationType,
                OperationAccessLevel = GetOperationAccessLevel(operationType),
                CallerIpAddress = GetLocalIPAddress(),
                OperationResult = result
            };

            auditRecord.AddOperationCategory(OperationCategory.PasswordManagement);
            auditRecord.AddCallerIdentity(CallerIdentityType.ApplicationID, user);
            auditRecord.AddCallerIdentity(CallerIdentityType.TenantId, tenantId);
            // This value is basically a hard coded 'guess'
            // The access level is defined by permission setting of the 'user'
            // which are not static and not defined by the service
            // So we are specifying what we believe the minimal access level
            // would be required for this operation to be successful
            auditRecord.AddCallerAccessLevel("Writer");
            auditRecord.AddTargetResource(secretStoreType, secretLocation);
            auditRecord.OperationResultDescription = (!string.IsNullOrWhiteSpace(resultMessage)) ? $"{resultMessage}" : $"'{operationName}' : '{result}'";

            ControlPlaneLogger.LogAudit(auditRecord);
        }

        private static string GetLocalIPAddress()
        {
            // Default to an empty IP address
            var result = "0.0.0.0";
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ipAddress = host?.AddressList?.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork || ip.AddressFamily == AddressFamily.InterNetworkV6);

            // If we can't find a valid ipAddress we will return the default value
            if (default(IPAddress) != ipAddress)
            {
                result = ipAddress.ToString();
            }

            return result;
        }

        private string GetOperationAccessLevel(OperationType operation)
        {
            switch (operation)
            {
                case OperationType.Create:
                    return "Write";
                case OperationType.Update:
                    return "Write";
                case OperationType.Delete:
                    return "Write";
                default:
                    return "Read";
            }
        }
    }
}
#else
using System;
using System.Runtime.CompilerServices;
using Microsoft.DncEng.SecretManager.Commands;


namespace Microsoft.DncEng.SecretManager
{

    /// <summary>
    /// Enum to eliminate the OpenTelemetry.Audit.Genev using statement
    /// </summary>
    public enum OperationResult
    {
        Success = 1,
        Failure
    }

    /// <summary>
    /// SecurityAuditLogger No-Op implementation for non-internal builds
    /// </summary>
    public class SecurityAuditLogger
    {

        /// <summary>
        /// Constructor for the SecurityAuditLogger that takes a CommonIdentityCommand to extract the service tree id value.
        /// </summary>
        public SecurityAuditLogger(CommonIdentityCommand commonIdentityCommand) : this(commonIdentityCommand.ServiceTreeId)
        {
        }

        /// <summary>
        /// Private constructor that configures the logger for the specified service tree id.
        /// </summary>
        private SecurityAuditLogger(Guid serviceTreeId)
        {
        }

        /// <summary>
        /// Add an audit log for secret update operations performed on behalf of a user.
        /// </summary>
        public void LogSecretUpdate(ITokenCredentialProvider credentialProvider, string secretName, string secretStoreType, string secretLocation, OperationResult result = OperationResult.Success, string resultMessage = "", [CallerMemberName] string operationName = "")
        {
            //No-op
        }
    }
}
#endif
