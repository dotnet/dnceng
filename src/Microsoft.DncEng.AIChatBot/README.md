# .NET Engineering Services OpenAI Proof of Concept

## Organization

This PoC has two major software components:

The Backend is an ASP.NET (Core) website that provides API access for document searching

The Frontent is a Blazor SPA served by the backend and providing all user-facing functionality.

Additionally, there are ".Tests" projects for unit and functional testing, plus one "shared" library for code common to the back and front-end.

## Azure Resources

The backend requires:

- Managed Identity: Used to grant the backend access to resources
- App Service: Hosts the website
- App Service Plan: Hosting the website
- Application Insights: Logging and telemetry for all resources
- Log Analytics workspace: Logging and telemetry for all resources
- OpenAI: Provides the magic
- Search Service: Interface for querying the magic
- Storage Account (specifically, blob storage)

The documentation indexing pipeline requires these additional resources:

- Form Recognizer

## Permissions

The backend needs these permissions:

- "Cognitive Services OpenAI User" for the OpenAI resource
- "Storage Blob Data Reader" for the Storage Account
- "Search Index Data Reader" for the Search Service

## Configuration

The backend configuration is set in two places:

- appsettings.json
- App Service Configuration "Application Settings"