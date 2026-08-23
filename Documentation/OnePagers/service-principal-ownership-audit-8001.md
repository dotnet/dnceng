# Service principal ownership audit

Tracking: [AB#8001](https://dev.azure.com/dnceng/internal/_workitems/edit/8001)

## Problem

Microsoft Entra application registrations and enterprise service principals
can become operationally inaccessible when their only owner leaves the team or
is unavailable. Entra groups cannot be assigned as enterprise application
owners, so a team group cannot directly replace individual owners.

Microsoft recommends:

- At least two owners on applications where possible.
- A `serviceManagementReference` that points to durable team contact
  information.
- Proactive monitoring for applications with zero or one owner.

See [Overview of Enterprise Application Ownership](https://learn.microsoft.com/entra/identity/enterprise-apps/overview-assign-app-owners).

## Phase 1: read-only audit

`eng/audit-service-principal-ownership.ps1` audits both sides of an Entra
application:

- The application registration.
- Every enterprise service principal in the current tenant with the same
  application ID.

It reports:

- Missing application registrations or service principals.
- Fewer than the required number of owners.
- Fewer than the required number of enabled user owners.
- Disabled user owners.
- Missing `serviceManagementReference`.
- Different owner sets on the application and service principal.
- Duplicate objects for one application ID.

The script does not add or remove owners.

### Discovery mode

Run without a manifest to inspect objects currently owned by the signed-in
user:

```powershell
.\eng\audit-service-principal-ownership.ps1 `
  -OutputPath .\artifacts\service-principal-ownership.json
```

Discovery mode is not a complete DNCENG inventory. It is intended to find
initial risks and help build the reviewed manifest.

### Manifest mode

Use a reviewed manifest for repeatable team-wide auditing:

```json
{
  "applications": [
    {
      "appId": "00000000-0000-0000-0000-000000000000",
      "displayName": "example"
    }
  ]
}
```

```powershell
.\eng\audit-service-principal-ownership.ps1 `
  -ManifestPath .\path\to\reviewed-manifest.json `
  -OutputPath .\artifacts\service-principal-ownership.json `
  -FailOnFindings
```

`-FailOnFindings` exits with code 2 when the audit finds a policy violation.
Authentication or input failures exit with code 1.

Use `-IncludeOwnerDetails` only when the reviewer needs owner names for
remediation. The default report contains counts without owner names or owner
object IDs.

## Scope and permissions

The signed-in Azure CLI identity supplies the Microsoft Graph token. Run
`az login` before invoking the script.

Discovery mode uses `/me/ownedObjects` and can only prove what the signed-in
user owns. Manifest mode requires read access to every listed application and
service principal. The authoritative manifest must be reviewed against
Service Tree and S360 inventory evidence.

Phase 1 intentionally has no Microsoft Graph write path. Adding owners through
automation requires highly privileged Graph access, including
`Application.ReadWrite.All` for application owner changes. That permission and
any automatic remediation require a separate security review.

## Remediation procedure

For each confirmed finding:

1. Confirm the application belongs to DNCENG and its
   `serviceManagementReference` identifies the correct Service Tree service.
2. Confirm at least two active DNCENG users are accountable for the
   application.
3. Assign those users to both the application registration and enterprise
   service principal.
4. Re-run the manifest audit and attach the clean report to AB#8001.
5. Do not automatically remove an existing owner until the service owner
   confirms the replacement and recovery path.

## Completion criteria

- The reviewed manifest covers the authoritative DNCENG application inventory.
- Every in-scope application and service principal has at least two enabled
  user owners.
- Application and service principal owner sets are aligned or an exception is
  documented.
- Every application has a validated `serviceManagementReference`.
- The recurring read-only audit has an owner, schedule, and team notification
  destination.
