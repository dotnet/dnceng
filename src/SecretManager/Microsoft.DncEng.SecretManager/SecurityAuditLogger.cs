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
    /// SecurityAuditLogger is a class that is used to log security audit events to the security event log and Geneva
    /// </summary>
    public class SecurityAuditLogger
    {
        private ILogger ControlPlaneLogger;

        private bool SuppressAuditLogging = false;

        /// <summary>
        /// Base constructor for the SecurityAuditLogger
        /// </summary>
        public SecurityAuditLogger(Guid serviceTreeId)
        {
            var auditFactory = AuditLoggerFactory.Create(options =>
            {
                // We use ETW as the destination for the audit logs becsue the application is not gurenteed to run on windows
                options.Destination = AuditLogDestination.ETW;
                options.ServiceId = serviceTreeId;
                // If the service ID is a empty guid we should suppress audit logging
                if (serviceTreeId == Guid.Empty)
                {
                    SuppressAuditLogging = true;
                }
            });

            ControlPanelLogger = auditFactory.CreateControlPlaneLogger();
        }

        /// <summary>
        /// Add an audit log for secret update operations perfomred on behalf of a user.
        /// </summary>
        public void LogSecretUpdate(ITokenCredentialProvider credentialProvider, string secretName, string secretStoreType, string secretLocation, OperationResult result = OperationResult.Success, string resultMessage = "", [CallerMemberName] string operationName = "")
        {
            try
            {
                LogSecretAction(OperationType.Update, operationName, credentialProvider, secretName, secretStoreType, secretLocation, result, resultMessage);
            }
            // Audit logging is a 'volatile' operation meaning it can throw exceptions if logging fails.
            // This could lead to service instability caused by simple logging issues which is not desirable.
            // So we catch all exceptions and write them to console as a last resort.
            // The hope is that app insights will also catch the base exception for debugging.
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to add audit log for secret update!: <{ex.Message}>");
            }            
        }

        internal void LogSecretAction(OperationType operationType, string operationName, ITokenCredentialProvider credentialProvider, string secretName, string secretStoreType, string secretLocation, OperationResult result, string resultMessage)
        {
            // Perform a no op if audit logging is suppressed
            if (SuppressAuditLogging)
            {
                return;
            }

            // The token application id of the client running the assembly.
            // NOTE: The user identity here should be something 'dynamic'.
            // If you are hard coding this value you should question if this Audit Log is useful
            // as it is likly redundant to lower level permission change logging that is already occuring.
            var user = credentialProvider.ApplicationId;
            // Get the tenant ID that provided the token for the credential provider.
            var tenantId = credentialProvider.TenantId;
            var auditRecord = new AuditRecord
            {
                OperationResultDescription = $"Action '{operationType}' For Secret '{secretName}' With Opeation '{operationName}' By User '{user}' On Source '{secretLocation}' Resulted In '{result}'.",
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
            // The access level is defiend by permission setting of the 'user'
            // which are not static and not defined by the service
            // So we are specifying what we belive the minmal acces level
            // would be requried for this operation to be successful
            auditRecord.AddCallerAccessLevel("Writer");
            auditRecord.AddTargetResource(secretStoreType, secretLocation);
            auditRecord.OperationResultDescription = (!string.IsNullOrWhiteSpace(resultMessage)) ? $"{resultMessage}" : $"'{operationName}' : '{result}'";

            ControlPanelLogger.LogAudit(auditRecord);
        }
        internal static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ipAddress = host.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
            if (ipAddress == null)
            {
                throw new Exception("No network adapters with an IPv4 address in the system!");
            }
            return ipAddress.ToString();
        }

        internal string GetOperationAccessLevel(OperationType operation)
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
