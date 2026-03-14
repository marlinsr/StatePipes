# StatePipes Authorization Architecture
*Conversation archived: 2026-03-06*

## Status
Architecture discussion complete. Ready to begin implementation planning.

---

## Context

StatePipes is used in enterprise-connected collections of industrial networks (Purdue Model / ISA-95 topology). Authorization needs to support both human operators and machine services, with dynamic admin reconfiguration of roles and command permissions.

---

## Topology

```
Enterprise Network (IT)
  └── Keycloak (identity + policy system of record)

Industrial Networks (OT) — multiple, may be air-gapped
  └── System Instance 1
        └── RabbitMQ (one per system)
              └── StatePipes Services (multiple, share the one RabbitMQ)
              └── Sync Agent (one per RabbitMQ = one per system instance)
  └── System Instance 2
        └── RabbitMQ
              └── StatePipes Services
              └── Sync Agent
```

**Key structural facts:**
- A **system** = a collection of StatePipes services sharing ONE RabbitMQ instance
- An industrial network runs **multiple instances of the same system**
- A system can also contain a StatePipes service that **orchestrates other systems** (via BrokerProxy)
- Each system's sync agent is companion to that system's RabbitMQ

---

## Two Principal Types

| Type | Authentication | Authorization managed by |
|---|---|---|
| Human operators | Keycloak JWT token | Keycloak roles → scopes |
| Machine services / proxies | X.509 cert (CN = identity) | Keycloak service account → roles |

Both are unified under the same Keycloak RBAC model. The sync agent treats them identically.

---

## Authorization Model

**Roles** are semantic (e.g., `operator`, `supervisor`, `viewer`, `admin`).
**Commands** are RabbitMQ routing keys = C# type full names (e.g., `MyApp.Commands.StartMachineCommand`).
**Roles map to permitted command routing keys**, with wildcard support (`MyApp.Commands.Get*`).
**Users/service accounts are assigned roles** per system instance.

### Keycloak Structure

```
Client Template: "PlantControllerSystem"   <- defined once per system type
  operator → [MyApp.Commands.Start*, MyApp.Commands.Stop*, MyApp.Commands.Get*]
  viewer   → [MyApp.Commands.Get*]

Client: "statepipes.{exchangeNamePrefix}.{brokerHost}"  <- one per system instance
  Inherits: PlantControllerSystem
  alice (user)               → operator
  net-a.line1-orchestrator   <- service account for proxy cert CN
    (service account)        → operator
```

Instance-specific overrides are possible but not required for standard deployments.

---

## Sync Agent Responsibilities

One sync agent per system (per RabbitMQ). It is a companion service shipped with every StatePipes deployment.

```
On startup:
  1. Load local cached policy from disk (operate immediately, no Keycloak dependency)
  2. Connect to Keycloak, pull current policy for this resource server
  3. Connect to RabbitMQ Management API
  4. Reconcile: set_topic_permissions for every identity on every exchange (idempotent)
  5. Publish AuthorizationPolicyChangedEvent to local broker
  6. Persist policy to local JSON cache

On Keycloak policy change (webhook or polling):
  1. Pull delta
  2. Apply set_topic_permissions to RabbitMQ
  3. Publish AuthorizationPolicyChangedEvent
  4. Persist updated cache

On disconnect from Keycloak:
  - No operational impact — RabbitMQ already has correct permissions applied
  - Log warning only

On sync agent restart:
  - Load cache → reapply to RabbitMQ (idempotent) → continue
```

### Sync Agent Identity (Dual)

- **Enterprise-facing**: Keycloak service account (Client Credentials flow) — read-only on realm policy
- **Industrial-network-facing**: X.509 cert — narrowly scoped role, can only publish `AuthorizationPolicyChangedEvent` and respond to `SyncNowCommand`

### Auto-Registration

On first startup, if the Keycloak client doesn't exist:
1. Derive resource server name: `statepipes.{exchangeNamePrefix}.{brokerHost}`
2. Register in Keycloak, link to system-type client template (sourced from `AssemblyName` in `ServiceConfiguration`)
3. Pull inherited policy, proceed normally

Subsequent new instances of the same system type self-register and inherit without admin action.

---

## Enforcement Layers

### Layer 0: RabbitMQ (primary enforcement)
`rabbitmq_auth_backend_multi` chains:
- `rabbit_auth_backend_oauth2` — for human users (Keycloak JWT)
- `rabbit_auth_backend_external` — for machine services (X.509 EXTERNAL)

Both coexist. `set_topic_permissions` restricts which routing keys (= command types) each identity can publish. Unauthorized commands rejected at the broker before reaching any StatePipes service.

### Layer 1: StatePipes (secondary / defense-in-depth)
In `ExecuteMessageHelper.ExecuteMessage`, check authenticated identity against local in-memory policy copy. Primary purpose: covers internal `SendCommand` calls that never touch RabbitMQ.

StatePipes services update their in-memory policy by subscribing to `AuthorizationPolicyChangedEvent`.

---

## Identity Threading (Required Code Changes)

### 1. CN Extraction
In `StatePipesConnectionFactory.CreateConnection`, extract cert CN:
```csharp
var username = clientCert.GetNameInfo(X509NameType.SimpleName, false);
```
Store on `ConnectionChannel`, thread through to `ReceivedCommandMessage`:
```csharp
ReceivedCommandMessage(object command, BusConfig replyTo, string authenticatedUser)
```

### 2. BusConfig Identity Fields (new)
```csharp
BusConfig(
    // existing fields...
    string? senderIdentity,    // cert CN or JWT subject of immediate sender
    string? originalIdentity   // human at the top of the hop chain (never overwritten)
)
```

**Direct human command**: both fields = `"alice"`

**Orchestrator forwarding on behalf of alice**:
- `senderIdentity = "net-a.line1-orchestrator"` (proxy cert CN, set by framework)
- `originalIdentity = "alice"` (carried forward from inbound command's BusConfig)

**Rule**: `originalIdentity` is never overwritten by intermediate systems. It is set once at the entry point and propagated unchanged through all hops.

`senderIdentity` is stamped automatically by the framework from `AuthenticatedUser`. Application code only carries `originalIdentity` forward when proxying.

---

## Audit Logging

The subsystem's audit log entry has full context from `BusConfig`:

```
"alice" (originalIdentity) via "net-a.line1-orchestrator" (senderIdentity)
executed StartMachineCommand at subsystem "net-a.line1-actuator" at 2026-03-06T14:32:01Z

Hop chain (via PreviousHop):
  BusConfig[0]: senderIdentity="alice", originalIdentity="alice"  (origin)
  BusConfig[1]: senderIdentity="net-a.line1-orchestrator", originalIdentity="alice"  (subsystem received)
```

No external system query needed at audit time — full chain is in the `BusConfig` structure.

---

## Inter-System Authorization (Orchestrator → Subsystem)

The proxy cert CN is a **Keycloak service account** on the subsystem's Keycloak client. The subsystem's sync agent configures RabbitMQ permissions for the orchestrator's service account exactly as it does for human users.

This means an admin can grant or revoke an orchestrator's access to a subsystem via Keycloak without touching deployment config or redeploying.

---

## Graceful Degradation

| Scenario | Behavior |
|---|---|
| Connected to enterprise | Live Keycloak policy, configurable sync interval |
| Keycloak temporarily unreachable | RabbitMQ already has correct permissions — no impact |
| Sync agent down | Instances continue on in-memory policy — no operational impact |
| Sync agent restarts | Loads cache, reapplies to RabbitMQ, resumes |
| New human operator, disconnected | Cannot authenticate (explicit documented limitation) |
| Machine service restart, disconnected | Works fully — cert auth + cached local policy |

---

## Implementation Build Sequence

```
1. CN extraction
   StatePipesConnectionFactory → ConnectionChannel.AuthenticatedUser
   → ReceivedCommandMessage carries AuthenticatedUser

2. BusConfig identity fields
   Add SenderIdentity + OriginalIdentity
   Framework stamps SenderIdentity automatically
   Orchestrating services carry OriginalIdentity forward when proxying

3. Authorization policy model
   IAuthorizationPolicy interface
   In-memory implementation with wildcard matching
   JSON persistence via existing JsonFileHelperUtility

4. Enforcement in ExecuteMessageHelper.ExecuteMessage
   Uses SenderIdentity for authz check
   Covers internal SendCommand path (broker doesn't cover this)

5. AuthorizationPolicyChangedEvent
   All services subscribe and update in-memory policy

6. Sync Agent service
   Keycloak Management API client
   RabbitMQ Management API client (set_topic_permissions)
   Delta detection, local persistence, event publishing
   Auto-registration on first startup

7. Command space discovery integration
   SelfDescriptionEvent → Keycloak scope registration
   Surfaces available commands to Keycloak admin without manual enumeration

8. Audit logging integration
   Read SenderIdentity + OriginalIdentity + PreviousHop chain from BusConfig
```

Steps 1–5 are internal to StatePipes with no Keycloak dependency — testable against a static JSON policy file.
Steps 6–8 are the Keycloak/external integration layer.

---

## Key Files Relevant to This Work

- `StatePipes/Comms/Internal/StatePipesConnectionFactory.cs` — CN extraction goes here
- `StatePipes/Comms/Internal/ExecuteMessageHelper.cs` — enforcement point
- `StatePipes/Comms/Internal/ReceivedCommandMessage.cs` — add AuthenticatedUser
- `StatePipes/Comms/BusConfig.cs` — add SenderIdentity, OriginalIdentity
- `StatePipes/Comms/Internal/StatePipesService.cs` — DoWork dispatches commands
- `StatePipes/Comms/Internal/StatePipesProxyInternal.cs` — proxy sends commands to subsystems
- `StatePipes/Comms/ProxyConfiguration.cs` — proxy BusConfig (cert identity for subsystem)
- `StatePipes/Messages/GetSelfDescriptionCommand.cs` — command space discovery
