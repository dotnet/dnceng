# DNCEng Operational Triage Agent — One Pager

## Description
Automated triage system for DNCEng Tasks under  
`internal\.NET Engineering Services\Operations`, ensuring transparent and consistent priority assignment.

The architecture is intentionally split into two layers with a **strict separation of responsibilities**:

1. **MCP-Based Triage Tooling (Logic Layer — Pure, Side‑Effect‑Free)**  
   - Implemented as one or more **local MCP servers**, distributed via **NuGet packages**  
   - Encapsulates triage logic, rule evaluation, and reasoning generation  
   - **Performs no Azure DevOps I/O**  
   - **Requires no authentication, tokens, or permissions**  
   - Deterministic, testable, and reusable across any environment (Azure job, CLI, Foundry)

2. **Lightweight Agent Layer (Execution / Orchestration Layer — Side‑Effecting)**  
   - Responsible for **all authentication and authorization**  
   - Owns all Azure DevOps interactions (queries, writes, comments, updates)  
   - Invokes MCP logic and applies its results  
   - Initially an **Azure-scheduled job**, but architected to support **CLI** or **Foundry-based agents** using the same MCP tooling

By design, **all security scopes remain in the agent**, eliminating the need to manage authentication or ADO permissions inside MCP tooling.  With this model, the agent is treated with the same security posture as a user would.

## Deliverable at End of Sprint
A functioning system where:
- MCP servers generate pure triage decisions and structured reasoning
- The agent:
  - Authenticates to Azure DevOps
  - Queries for candidate items
  - Invokes MCP tooling
  - Writes updates back to Azure DevOps
- Unprioritized DNCEng Tasks are automatically triaged
- Items assigned to the agent’s service principal are re-triaged and unassigned
- A read-only “Triage agent” section is created and appended to
- Triage decisions follow a versioned wiki-based triage bar

## Motivation
Azure DevOps Tags/Labels must remain strictly **filtering/query metadata**, not programmatic switches.

The system’s architecture reinforces this by:
- Making MCP tooling **pure business logic with zero dependencies on authentication or external APIs**
- Keeping **all operational and security responsibilities in the agent layer**
- Preventing accidental coupling between triage rules and ADO access patterns

This separation:
- Removes the need for security handling in MCP tooling  
- Promotes deterministic, portable logic  
- Preserves a predictable and auditable automation model

## Architectural Overview

### **MCP Tooling (Logic Layer)**
- Distributed as NuGet-based local MCP servers
- Purely logical: no network calls, no credentials  
- Takes structured inputs:
  - Work item fields supplied by the agent
  - Triage bar rules
  - Contextual metadata
- Outputs structured triage decisions:
  - Priority recommendation  
  - Reasoning and decision explanation  
  - Alternate outcomes if additional data were provided  
  - Triage bar version used  

**Because MCP tooling is completely side‑effect‑free, it does not require ADO credentials, eliminating the need to manage authentication/authorization at the logic level.**

### **Agent Layer (Execution / Orchestration Layer)**
The agent is the **only** component that:

- Authenticates to Azure DevOps  
- Holds authorization scopes  
- Performs all ADO reads (queries)  
- Performs all ADO writes (priority updates, comments, section updates)  
- Handles throttling, retries, and permission boundaries  

The agent simply orchestrates:
1. Collecting ADO data  
2. Passing structured inputs to MCP servers  
3. Applying MCP outputs back to ADO  

### **Why ADO I/O Lives Exclusively in the Agent Layer**
Keeping all authentication and authorization in the agent:

- Ensures MCP servers remain universal and environment-agnostic  
- Prevents credential sprawl (no tokens in libraries/packages)  
- Eliminates the need for MCP security review or privileged access  
- Makes MCP tooling safe to run in local tools, pipelines, and Foundry agents  
- Reduces operational blast radius  

**This security simplification is a core architectural advantage.**

## Approach

### 1. Automatic Triage of Unprioritized Tasks
Agent queries ADO → sends data to MCP logic → applies MCP decision.

### 2. Re‑Triage of Tasks Assigned to the Agent
Agent re-evaluates tasks assigned to the service principal, unassigns them, and updates ADO.

### 3. “Triage Agent” Read‑Only Section
Agent writes an append-only, structured section including:
- MCP reasoning  
- Priority determination  
- Data gaps  
- Bar version used  
- Timestamp  
- Wiki link  

### 3a. **Brief Agent Comment on the Work Item**
Whenever the agent triages or re‑triages a work item, it posts a **short comment** indicating that triage occurred,  
and includes a **direct link to the “Triage agent” section** where the full historical record and detailed reasoning are stored.

This ensures:
- Comments stay concise  
- Users immediately know triage occurred  
- Full fidelity details remain centralized and well‑structured  

### 4. Wiki‑Published, Versioned Triage Bar
- Triage bar lives in DNCEng wiki  
- MCP tooling interprets it  
- Updating the bar allows all items to be re‑triaged cleanly  

## Security / Telemetry / Test Coverage / Safe Deployment

### **Security**
- **All authentication and authorization handled by the agent**  
- MCP tooling requires **no credentials and has no permissions**  
- No external calls from MCP servers  

This reduces risk and simplifies compliance.

### **Telemetry**
- MCP evaluations (inputs/outputs)  
- Agent‑applied operations  
- Re-triage deltas  
- User overrides  

### **Test Coverage**
- MCP: deterministic unit + integration tests  
- Agent: orchestration tests, mocked MCP calls  

### **Safe Deployment**
- Wiki-based rule versioning  
- Dry-run modes  
- Controlled rollout via agent configuration  

## Task Breakdown

| Testable Chunk                   | Tasks                                                       | Cost |
|----------------------------------|-------------------------------------------------------------|------|
| MCP triage engine                | Pure logic + triage bar interpreter                        | 2–3 days |
| MCP server infrastructure        | Local MCP server + NuGet packaging                          | 2–3 days |
| Agent orchestration logic        | Azure job + ADO I/O + MCP invocation                       | 2–3 days |
| Triage agent section writer      | Append-only structured section                              | 2 days |
| Commenting + assignment handling | Minimal comments + unassign logic                           | 1–2 days |
| Telemetry + monitoring           | Metrics, dashboards                                         | 1–2 days |
| Testing & rollout                | Unit, integration, and dry-run validation                   | 3 days |
