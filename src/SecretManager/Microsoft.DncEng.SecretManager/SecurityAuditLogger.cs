#if INTERNAL 
// NOTE: 
// We conditional compile this code because it depends on 
// references that are only available in the internal build
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Audit.Geneva;

namespace Microsoft.DncEng.SecretManager
{
    /// <summary>
    /// Enum that mirrors values from the OperationResult type defined in OpenTelemetry.Audit.Geneva
    /// </summary>
    /// <remarks>
    /// This enum is needed to ensure any external consumers of the SecurityAuditLogger class do not need to 
    /// reference the OpenTelemetry.Audit.Geneva library directly.
    /// </remarks>
    public enum SecretManagerOperationResult
    {
        // All enum values should mirrors what is available in the OperationResult enum in OpenTelemetry.Audit.Geneva
        // This ensure the code will adapted to value changes in the OpenTelemetry.Audit.Geneva library
        // and it will ensure a compile time error is thrown here if a named enum type is changed or removed
        // from the OpenTelemetry.Audit.Geneva library
        // NOTE: It will not handle named enum additions to the OpenTelemetry.Audit.Geneva library
        Success = OperationResult.Success,
        Failure = OperationResult.Failure
    }

    /// <summary>
    /// SecurityAuditLogger is a class that is used to log security audit events to the local event log 
    /// in a formate that is comparable with Geneva audit logging
    /// </summary>
    public class SecurityAuditLogger
    {
        private ILogger ControlPlaneLogger;

        /// <summary>
        /// Main constructor that configures the logger for the specified service tree id.
        /// </summary>
        public SecurityAuditLogger(Guid serviceTreeId)
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
        public void LogSecretUpdate(ITokenCredentialProvider credentialProvider, string secretName, string secretStoreType, string secretLocation, SecretManagerOperationResult result = SecretManagerOperationResult.Success, string resultMessage = "", [CallerMemberName] string operationName = "")
        {
            try
            {
                LogSecretAction(OperationType.Update, operationName, credentialProvider, secretName, secretStoreType, secretLocation, result, resultMessage);
            }
            // Audit logging is a 'volatile' operation meaning it can throw exceptions if logging fails.
            // This could lead to service instability caused by simple logging issues which is not desirable.
            // So we catch all exceptions and write a safe warning message to console 
            // The hope is that app insights will also catch the base exception for debugging.
            catch
            {
                Console.WriteLine($"Failed to add audit log for secret update!");
            }
        }

        private void LogSecretAction(OperationType operationType, string operationName, ITokenCredentialProvider credentialProvider, string secretName, string secretStoreType, string secretLocation, SecretManagerOperationResult result, string resultMessage)
        {
            // The token application id of the client running the assembly.
            // NOTE: The user identity here should be something 'dynamic'.
            // If you are hard coding this value you should question if this Audit Log is useful
            // as it is likely redundant to lower level permission change logging that is already occurring.
            var user = credentialProvider.ApplicationId;
            // Get the tenant ID that provided the token for the credential provider.
            var tenantId = credentialProvider.TenantId;

            // Create a logging record that is compatible with Geneva audit logging
            var auditRecord = new AuditRecord
            {
                OperationResultDescription = $"Action '{operationType}' For Secret '{secretName}' With Operation '{operationName}' By User '{user}' On Source '{secretLocation}' Resulted In '{result}'.",
                CallerAgent = GetType().Namespace,
                OperationName = operationName,
                OperationType = operationType,
                OperationAccessLevel = GetOperationAccessLevel(operationType),
                CallerIpAddress = GetLocalIPAddress(),
                OperationResult = (OperationResult)(result)
            };

            // Add additional context to the audit record as required by Geneva audit logging
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
// NOTE: 
// We conditional compile this code because it depends on 
// references that are only available in the internal build
// Public build implementations will perform no-op logging processes
using System;
using System.Runtime.CompilerServices;


namespace Microsoft.DncEng.SecretManager
{
    /// <summary>
    /// Enum that mirrors values from the OperationResult type defined in OpenTelemetry.Audit.Geneva
    /// </summary>
    /// <remarks>
    /// This enum is needed to ensure any external consumers of the SecurityAuditLogger class do not need to reference the OpenTelemetry.Audit.Geneva library.
    /// </remarks>
    public enum SecretManagerOperationResult
    {
        // All enum values should mirrors what is available in the OperationResult enum in OpenTelemetry.Audit.Geneva
        // NOTE: The named values here should be kept in sync with the values defined in the INTERNL IF block above.
        Success = 1,
        Failure = 2
    }

    /// <summary>
    /// SecurityAuditLogger No-Op implementation for non-internal builds
    /// </summary>
    public class SecurityAuditLogger
    {
        /// <summary>
        /// Main constructor that configures the logger for the specified service tree id.
        /// </summary>
        public SecurityAuditLogger(Guid serviceTreeId)
        {
        }

        /// <summary>
        /// Add an audit log for secret update operations performed on behalf of a user.
        /// </summary>
        public void LogSecretUpdate(ITokenCredentialProvider credentialProvider, string secretName, string secretStoreType, string secretLocation, SecretManagerOperationResult result = SecretManagerOperationResult.Success, string resultMessage = "", [CallerMemberName] string operationName = "")
        {
            //No-op
        }
    }
}
#endif
