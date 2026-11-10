# Persisted Workflows — Design

**Date:** 2026-11-08
**Status:** Approved in brainstorming; awaiting written-spec review
**Scope:** `StatePipes` core, `StatePipes.Test`, `StatePipes.ServiceCreatorTool` (generators + Resources), `StatePipes.Diagrammer`, `StatePipes.ClaudePlugin`

---

## 1. Purpose

Extend the StatePipes state machine programming model with **workflows**: many durable instances of one state machine
inside a **single microservice**, each identified by a **CorrelationId**, persisted to PostgreSQL so an instance outlives
the service session that created it.

The driving use case is a **shopping cart**: thousands of carts, each its own instance of `CartWorkflow`, living for
minutes to days, surviving service restarts. This is **not** a saga / process manager: a workflow does not coordinate
other microservices; it is a multi-instance, persisted state machine hosted by one service.

### Success criteria

1. A developer authors a workflow with the same model as a state machine — state classes, `[PermitIf]` / `[IgnoreIf]` /
   `[PermitReentryIf]`, `OnEntry` / `OnExit`, parented states, `MoveToState`, generated diagrams.
2. Any workflow command carrying a CorrelationId is routed to, and only to, that instance; the instance is reconstituted
   from PostgreSQL (current state **and** all state fields) to handle it.
3. Workflow timers (`IDelayedMessageSender`) keep counting while the instance is not in memory and across service
   restarts.
4. Two replicas of the service never operate on the same instance at the same time.
5. PostgreSQL access is authenticated by client certificate only.
6. Existing singleton state machines behave exactly as before.
7. The architectural properties in `docs/Paper.md` §3 — state cohesion (B1), additive extensibility (B2), natural
   exclusion (B3), structural testability (B4), diagram accuracy (B5) — hold for workflows.

### Out of scope

- Saga / cross-service orchestration.
- An in-memory instance cache (the `IWorkflowStore` seam allows one later; v1 loads from PostgreSQL on every command).
- Database providers other than PostgreSQL.
- `GetAllStateMachineDiagramsCommand` / `GetAllStateMachineStatusCommand` support for workflows (explicitly excluded).

---

## 2. Decisions log

| # | Decision |
|---|---|
| D1 | Multi-instance state machine in one service, keyed by CorrelationId (not a saga). |
| D2 | Design for multiple replicas from day one; single replica is the first deployment. |
| D3 | PostgreSQL, authenticated by client certificate (`cert` in `pg_hba.conf`, CN = role). |
| D4 | One generic framework-owned table; instance data is a `jsonb` document. |
| D5 | Workflow states **must have parameterless constructors** (enforced). Therefore every field is per-instance data and every field is persisted. |
| D6 | Workflows reach singleton / DI-managed services **only by sending a command** to a microservice-level state machine; the reply comes back **as a workflow command**. |
| D7 | Fields of **every** state in the workflow are persisted (not just the current state). |
| D8 | Timers keep using `IDelayedMessageSender`; it gains `TimerId`; `Enabled` is not serialized; implementations delegate to a timer-thread interface. Workflow timers persist to a table `(timer_id, expiration, message type full name, message json, …)`. |
| D9 | Unknown CorrelationId → instance created implicitly. |
| D10 | Entering an `IFinalWorkflowState` deletes the instance and its timers. Every workflow must have **at least one** final state; multiple allowed. |
| D11 | Outgoing messages are **sent immediately** during firing (no outbox). |
| D12 | Exclusive per-instance lock held for **one command only**, transaction-scoped: `pg_try_advisory_xact_lock`. |
| D13 | Locked → requeue to the end of the command bus after a configurable delay, up to a configurable retry count; then log "not satisfied: workflow locked" and discard. |
| D14 | Engine approach: **fresh machine per command** (new state objects + new Stateless machine), with per-type reflection / Cecil metadata cached. |
| D15 | Persistence outcome is driven by **whether a handler is declared**, not by guard results. |
| D16 | Periodic timers that are overdue **skip** missed periods (fire once, next due on the original cadence). |
| D17 | Singleton machines move to an in-memory implementation of the same timer-thread interface. |
| D18 | Framework creates its own tables at startup; Npgsql lives in the core `StatePipes` package. |
| D19 | Workflows live in the existing `StateMachines/` folder; `-w` generates a `FinishedState`. |
| D20 | Full scope in one spec: runtime, infrastructure scripts, test harness, ServiceCreatorTool, Diagrammer, Claude plugin. |

### Accepted limitations

- **L-1 (send immediately):** if the commit fails after messages were sent (e.g. a DB error at save), those messages
  describe a transition that was rolled back. Likewise, when a *new* instance's first state sends messages in `OnEntry`
  and the command turns out to be undeclared, the instance is rolled back but the messages stand.
- **L-2 (timer delivery):** at-least-once. A crash between sending a fired timer's message and committing the timer
  row update re-fires it.
- **L-3 (timer precision):** roughly `TimerPollInterval` (default 1 s).
- **L-4 (throughput):** workflow commands run on the service's single bus thread with synchronous DB I/O; each one's
  round trip delays every other command in that service, including singleton machine commands.

---

## 3. Authoring model

### 3.1 Types

| Type | Kind | Purpose |
|---|---|---|
| `IWorkflow : IStateMachine` | public interface | Workflow marker. Inheriting `IStateMachine` keeps the existing attributes, constraints and diagram tooling applicable. |
| `BaseWorkflowTriggerCommand<TWorkflow> : ITrigger` | public class/record base | Every workflow trigger. Adds `string CorrelationId { get; init; }`. |
| `BaseWorkflowState<TWorkflow>` | public base | Sibling of `BaseStateMachineState<T>` (not a subclass). |
| `ParentedBaseWorkflowState<TWorkflow, TParent>` | public base | Substates; calls `SubstateOf<TParent>()`, mirroring `ParentedBaseStateMachineState`. |
| `IFinalWorkflowState` | public marker | Terminal state; entering it deletes the instance. |
| `IFirstStateForStateMachine`, `IFirstSubstate` | existing | Reused unchanged. |

### 3.2 `BaseWorkflowState<TWorkflow>` API

Exposed (protected): `CorrelationId`, `SendCommand`, `PublishEvent`, `SendResponse`, `CreateDelayedMessageSender<T>()`,
`GetCurrentTrigger<T>()`, `GetCurrentResponseInfo()`, virtual `OnEntry` / `OnExit` / `OnActivate` / `OnDeActivate`.

Deliberately **not** exposed: `Fire`, `FireExternal`, `GetCurrentStateForExternal`, `SendCurrentStatus`,
`SendCurrentStatusAllStateMachines` — these would bypass D6.

The attribute scanning currently in `BaseStateMachineState.Configure` (`[PermitIf]`, `[IgnoreIf]`, `[PermitReentryIf]`,
first-substate discovery, and the Mono.Cecil scan for `PublishEvent` / `SendResponse` / `SendCommand` types) moves into a
shared internal helper used by both base classes. `BaseStateMachineState` behaviour is unchanged.

The workflow base constructor binds to the engine's per-command context (the workflow analogue of
`TemporaryStateMachineHolder`), so `CreateDelayedMessageSender` is callable from the parameterless constructor.

### 3.3 Example

```csharp
public class CartWorkflow : IWorkflow { }

public record AddItem(string Sku, int Qty) : BaseWorkflowTriggerCommand<CartWorkflow>;
public record Checkout                     : BaseWorkflowTriggerCommand<CartWorkflow>;
public record PriceQuoted(decimal Total)   : BaseWorkflowTriggerCommand<CartWorkflow>;   // reply from a singleton
public record CartAbandoned                : BaseWorkflowTriggerCommand<CartWorkflow>;   // timer message

public class CartOpenState : BaseWorkflowState<CartWorkflow>, IFirstStateForStateMachine
{
    private List<CartLine> _lines = [];                              // persisted
    private IDelayedMessageSender<CartAbandoned> _abandon;           // persisted as { TimerId }

    public CartOpenState() => _abandon = CreateDelayedMessageSender<CartAbandoned>();

    [PermitReentryIf]
    private bool OnAdd(AddItem cmd, BusConfig? r)
    {
        _lines.Add(new(cmd.Sku, cmd.Qty));
        _abandon.StartOneShot(TimeSpan.FromDays(7), new CartAbandoned { CorrelationId = CorrelationId });
        return false;                                                // in-place processing; still persisted (D15)
    }

    [PermitIf(typeof(PricingState))]
    private bool OnCheckout(Checkout cmd, BusConfig? r) => _lines.Count > 0;

    [PermitIf(typeof(AbandonedState))]
    private bool OnAbandoned(CartAbandoned cmd, BusConfig? r) => true;
}

public class PricingState : BaseWorkflowState<CartWorkflow>
{
    public override void OnEntry() => SendCommand(new QuotePrice(CorrelationId /*, lines */));   // to a singleton machine
    [PermitIf(typeof(CheckedOutState))]
    private bool OnQuoted(PriceQuoted cmd, BusConfig? r) => true;
}

public class CheckedOutState : BaseWorkflowState<CartWorkflow>, IFinalWorkflowState
{
    public override void OnEntry() => PublishEvent(new CartCheckedOut(CorrelationId));
}

public class AbandonedState : BaseWorkflowState<CartWorkflow>, IFinalWorkflowState { }
```

### 3.4 Rules

1. **Parameterless constructor.** Every workflow state has a public parameterless constructor and no other
   constructors taking parameters.
2. **Persisted fields.** All instance fields — public and non-public, including auto-property backing fields — declared
   on types between the concrete state class and `BaseWorkflowState` / `ParentedBaseWorkflowState` (exclusive) are
   persisted. Framework base-class fields are not.
3. **First state.** Exactly one state implements `IFirstStateForStateMachine`.
4. **Final states.** At least one state implements `IFinalWorkflowState`; several are allowed.
5. **Triggers.** Every trigger targeting a workflow derives from `BaseWorkflowTriggerCommand<TWorkflow>`; a
   `BaseTriggerCommand<TWorkflow>` is an error. Every state of a workflow derives from the workflow bases; a
   `BaseStateMachineState<TWorkflow>` is an error.
6. **Singleton request/reply convention (D6).** The request command to a singleton machine carries the CorrelationId as
   an ordinary field; the singleton replies by sending a `BaseWorkflowTriggerCommand<TWorkflow>` with that
   CorrelationId. Documented and taught by the plugin; not enforced by the framework.
7. **No automatic CorrelationId stamping.** Messages a workflow sends (including timer messages) carry whatever
   CorrelationId the developer sets.

Rules 1, 3, 4 and 5 — plus "every workflow trigger type round-trips through the workflow JSON settings" — are validated
at container build; violations fail startup with a message naming the offending type.

### 3.5 Registration

`AllStateMachineContainerSetup` is changed to skip `IWorkflow` types. A new internal `AllWorkflowContainerSetup`
discovers `IWorkflow` types in the service assembly, builds and caches per-type metadata, registers one
`BaseWorkflowTriggerCommandHandler<TCommand, TWorkflow>` per workflow trigger type, and validates the rules above.
`StatePipesServiceContainerSetup` wires both automatically. Workflow state objects are **never** registered in the DI
container.

Workflows are not held by `StateMachineManager`; `SendCurrentStatusAllStateMachines`,
`GetAllStateMachineDiagramsCommand` and `GetAllStateMachineStatusCommand` therefore exclude them. Workflow transitions
do **not** publish `StateStatusEvent` (one event per cart transition would flood status consumers such as the
Explorer).

---

## 4. Runtime: processing a workflow command

### 4.1 Routing

`BaseWorkflowTriggerCommandHandler<TCommand, TWorkflow>.HandleMessage` passes `(command, busConfig, retryCount)` to
`WorkflowEngine<TWorkflow>`. `ReceivedCommandMessage` gains `int RetryCount` (default 0); because `StatePipesService`
processes commands on a single `TaskWrapper` thread, it exposes the in-flight message's retry count to the handler via
an internal per-dispatch context.

### 4.2 Per-type cached metadata

Built once per workflow type at startup: state types and names, parent relationships, first state, final states,
per-state handler declarations (trigger type → attribute kind + `MethodInfo` + destination), the Cecil-derived
event/command lists (for diagrams), and the per-state persisted `FieldInfo` lists.

### 4.3 Pipeline (bus thread, synchronous Npgsql API)

1. Open a pooled connection; `BEGIN`.
2. **Lock:** `SELECT pg_try_advisory_xact_lock(@key)` where `@key` is a stable 64-bit hash of
   `workflow_type + '\0' + correlation_id`. `false` → step 7.
3. **Load** `current_state, document` from `workflow_instance` by `(workflow_type, correlation_id)`.
4. **Build** a fresh machine (D14):
   - Instantiate every state class through its parameterless constructor.
   - *Row found:* populate each state object's fields from `document.states[<StateName>]`; construct the Stateless
     machine with initial state `current_state`. `OnEntry` does **not** run.
   - *No row:* construct at `InitialState`, fire `InitTrigger` internally → first state's `OnEntry` runs.
   - `MoveToState` bookkeeping (`_recentlyEnteredStates`) starts empty for each command, so the existing LCA fix
     applies unchanged.
5. **Fire** the command. Outgoing messages go to the bus immediately (D11). Timer starts/stops write to `workflow_timer`
   on the **same connection and transaction**.
6. **Outcome** (D15). *Declared* means the current state or one of its ancestors has a `[PermitIf]`, `[IgnoreIf]` or
   `[PermitReentryIf]` method for this trigger type (from cached metadata), evaluated before firing.
   - **Exception** from state code → `ROLLBACK`; log exception with CorrelationId; discard the command.
   - **Not declared, new instance** → `ROLLBACK` (no row created); log.
   - **Not declared, existing instance** → `ROLLBACK`; verbose log (as unhandled triggers are today).
   - **Declared, resulting state (after `MoveToState` chains) is `IFinalWorkflowState`** → `DELETE` the instance row and
     all `workflow_timer` rows for `(workflow_type, correlation_id)`; `COMMIT`.
   - **Declared, otherwise** → serialize all states' fields; `INSERT … ON CONFLICT (workflow_type, correlation_id) DO UPDATE`
     setting `current_state`, `document`, `version = version + 1`, `updated_at = now()`; `COMMIT`. Guard results do not
     matter.
7. **Locked** → `ROLLBACK`. If `retryCount < LockedMaxRetries`, hand the command to the **in-memory** timer service to be
   re-queued to the end of the command bus after `LockedRetryDelay` with `retryCount + 1`. Otherwise log
   `"Workflow command not satisfied: workflow locked"` (workflow type, CorrelationId, command type, retries) and discard.

The advisory lock is released at commit, rollback or connection loss — a crashed replica never leaves a stale lock. It
works whether or not the row exists, so the implicit-creation race (two replicas creating the same CorrelationId) is
serialized with no phantom messages. A hash collision between two CorrelationIds only causes a spurious "locked" and a
retry.

---

## 5. Persistence

### 5.1 Schema

Created at startup with idempotent `CREATE SCHEMA/TABLE/INDEX … IF NOT EXISTS` (D18).

```sql
CREATE SCHEMA IF NOT EXISTS statepipes;

CREATE TABLE IF NOT EXISTS statepipes.workflow_instance (
  workflow_type   text        NOT NULL,   -- workflow marker Type.FullName
  correlation_id  text        NOT NULL,
  current_state   text        NOT NULL,   -- state class Name (the Stateless state key)
  document        jsonb       NOT NULL,
  version         bigint      NOT NULL,
  created_at      timestamptz NOT NULL,
  updated_at      timestamptz NOT NULL,
  PRIMARY KEY (workflow_type, correlation_id));

CREATE TABLE IF NOT EXISTS statepipes.workflow_timer (
  timer_id               uuid        PRIMARY KEY,
  workflow_type          text        NOT NULL,
  correlation_id         text        NOT NULL,
  expiration_time        timestamptz NOT NULL,
  period                 interval    NULL,          -- NULL = one-shot
  message_type_full_name text        NOT NULL,
  message_json           jsonb       NOT NULL);

CREATE INDEX IF NOT EXISTS workflow_timer_expiration_idx  ON statepipes.workflow_timer (expiration_time);
CREATE INDEX IF NOT EXISTS workflow_timer_owner_idx       ON statepipes.workflow_timer (workflow_type, correlation_id);
```

### 5.2 Document

```json
{ "states": {
    "CartOpenState": { "_lines": [ { "Sku": "sku-1", "Qty": 2 } ], "_abandon": { "TimerId": "9f1c…" } },
    "PricingState":  { } } }
```

### 5.3 Serialization

Newtonsoft.Json with a workflow-specific `JsonSerializerSettings`:
- A custom contract resolver that serializes exactly the field set of §3.4 rule 2 (fields, not properties).
- An `IDelayedMessageSender<T>` converter writing `{ "TimerId": "<guid>" }`; on read it rebuilds the sender bound to the
  persisted timer service for the current instance.
- `TypeNameHandling.Auto` for polymorphic fields, with a serialization binder that allows only the service's own
  assemblies, the StatePipes assembly and BCL primitive/collection types.

The same settings serialize timer `message_json`.

### 5.4 Schema evolution

Because rehydration is *construct, then populate*:
- A new field keeps its constructor/initializer default.
- A removed field is ignored.
- A state renamed/removed while it is some instance's `current_state` → error log with CorrelationId, command
  discarded, row untouched for manual repair.

### 5.5 Configuration

`ServiceConfiguration` gains an optional constructor/JSON parameter `WorkflowStoreConfig? workflowStore = null`,
exposed as `WorkflowStore`.

| Property | Default | Notes |
|---|---|---|
| `Host` | — | e.g. `postgres-db` |
| `Port` | `5432` | |
| `Database` | — | e.g. `statepipes` |
| `Username` | — | must equal the client certificate CN |
| `ClientCertFileName` | — | e.g. `statepipes.postgres-db.client.p12`; resolved in the same certificate directory `BusConfig` uses |
| `PasswordFileName` | `CommonPassword.txt` | password for the `.p12` |
| `LockedMaxRetries` | `10` | |
| `LockedRetryDelay` | `50 ms` | |
| `TimerPollInterval` | `1 s` | |

Npgsql connects with `SslMode=VerifyFull` (server cert validated against the step-ca root in the OS trust store) and the
client `.p12`; no password is placed in the connection string.

Startup: if the service declares workflows and `WorkflowStore` is null, or the connection / table creation fails,
startup fails with a clear message. A service without workflows never touches Npgsql.

Npgsql is added as a `PackageReference` of the core `StatePipes` project.

### 5.6 Store seam

Internal `IWorkflowStore` (begin unit of work, try-lock, load, upsert, delete-with-timers, timer upsert/delete/exists,
claim-due-timers). Implementations: `PostgresWorkflowStore` (production) and `InMemoryWorkflowStore` (tests; still
performs full serialize → deserialize on every command). A future cache plugs in here.

---

## 6. Timers

### 6.1 Interface

```csharp
internal interface ITimerService
{
    void StartOneShot(Guid timerId, TimeSpan dueTime, IMessage message);
    void StartPeriodic(Guid timerId, TimeSpan period, IMessage message);
    void Stop(Guid timerId);
    bool IsEnabled(Guid timerId);
}
```

### 6.2 `IDelayedMessageSender<TMessage>`

Gains `Guid TimerId { get; }`. The implementation `DelayedMessageSender<T>(ITimerService timers, Guid timerId)` is a
thin wrapper: `Enabled => timers.IsEnabled(TimerId)` (marked `[JsonIgnore]`), methods delegate. The per-instance
`System.Threading.Timer` is removed. Public authoring API is otherwise unchanged. `CreateDelayedMessageSender` assigns
a new `Guid`.

**Workflow senders are never disposed** by the engine (disposal calls `Stop()`, which would cancel timers on every
command). Workflow state objects are not DI-registered, so the container never disposes them either.

### 6.3 `InMemoryTimerService`

Used by singleton machines (D17) and by the locked-retry requeue (D13).
- One dedicated thread; `PriorityQueue<Guid, DateTimeOffset>` + dictionary keyed by `TimerId`; waits on a signal until
  the next due time or a change.
- Starting an existing `TimerId` replaces it (today's Stop-then-start semantics).
- Periodic timers reschedule from their previous due time (no drift).
- Sends via the bus `SendMessage`, as today.
- Takes a `TimeProvider` (tests use `FakeTimeProvider`).

### 6.4 `PersistedTimerService` (workflows)

**Start / Stop / IsEnabled** run on the current workflow command's connection and transaction:
- Start → upsert by `timer_id` with `expiration_time = now() + @due` (computed in SQL; the DB clock is the only clock),
  `period`, the owning `workflow_type` / `correlation_id`, `message_type_full_name`, `message_json`.
- Stop → delete by `timer_id`. IsEnabled → row exists (sees the transaction's uncommitted writes).

**Firing thread** (one per replica), every `TimerPollInterval`:
1. `BEGIN`; `SELECT … FROM statepipes.workflow_timer WHERE workflow_type = ANY(@thisServicesWorkflowTypes) AND
   expiration_time <= now() ORDER BY expiration_time LIMIT 100 FOR UPDATE SKIP LOCKED`.
2. For each row: resolve `message_type_full_name` and deserialize `message_json` with the workflow settings; send the
   message on the local command bus (it then goes through §4.3 like any command).
3. One-shot → `DELETE`. Periodic → `expiration_time = expiration_time + period * ceil((now() - expiration_time) / period)`,
   advanced by at least one period (D16: overdue periodic timers fire once and resume on the original cadence).
4. `COMMIT`.

`SKIP LOCKED` guarantees each due timer is claimed by exactly one replica.

---

## 7. Error handling

| Situation | Behaviour |
|---|---|
| Lock held elsewhere | Rollback; requeue after `LockedRetryDelay` with `retry + 1`; at `LockedMaxRetries` log "not satisfied: workflow locked" and discard. |
| Handler not declared — new instance | Rollback (no row); log. |
| Handler not declared — existing instance | Rollback; verbose log. |
| Exception from state code | Rollback; log exception; discard; no retry. Sent messages stand (L-1). |
| Document deserialization fails / unknown `current_state` | Rollback; error log; row untouched; command discarded. |
| PostgreSQL unreachable while handling a command | Error log; command discarded (not requeued). |
| PostgreSQL unreachable in the timer thread | Log; back off; retry next poll. Timers fire late; missed periods skipped. |
| Timer message type unresolvable | Error log once per `timer_id` per process; row left for repair and excluded from that process's subsequent claims. |
| Startup validation / store connection / table creation fails | Service fails to start with a message naming the offending type or setting. |

All log entries carry workflow type, CorrelationId and (where applicable) command type.

---

## 8. Infrastructure scripts (`StatePipes.ServiceCreatorTool/Resources`)

### 8.1 `DockerInfrastructureStart_ps1.sample`

- **Variables:** `$PostgresContainerName = "postgres-db"`, `$PostgresImage = "postgres:17"` (pinned major),
  `$PostgresDatabase = "statepipes"`, `$PostgresRole = "statepipes"`,
  `$PostgresProgramDataPrefix = $programData + "\PostgreSQL\" + $PostgresContainerName + "\"`.
- **Comment block** listing the required hosts-file line
  `127.0.0.1 amqp09-broker kafka-broker postgres-db statepipes.explorer` (existing comments reference such a list,
  which does not exist yet).
- **Step 1 teardown:** add `postgres-db` to the stop/remove loop; remove `CN=postgres-db` from `TrustedPublisher`;
  remove `$PostgresProgramDataPrefix`. The named volume is **kept**.
- **Step 4 certificates** (same `step-ca`, `@signWithIntermediate`):
  - server leaf `postgres-db` → `postgres-db.server.crt/.key`, SANs `postgres-db`, `localhost`, `127.0.0.1`;
  - client leaf `statepipes` (CN = role) → `statepipes.postgres-db.client.crt/.key`;
  - `.p12` bundle `statepipes.postgres-db.client.p12` with the CA chain and `$CommonPassword`.
- **Step 5 copy-out:** `postgres-db.server.crt`, `postgres-db.server.key`, `statepipes.postgres-db.client.crt`,
  `statepipes.postgres-db.client.p12`.
- **New step — PostgreSQL:**
  - Write to `$PostgresProgramDataPrefix`: `certs\` (server crt, server key, CA bundle root+intermediate),
    `pg_hba.conf`, `initdb\10-statepipes.sql`.
  - `pg_hba.conf`:
    ```
    local    all  postgres            peer
    hostssl  all  all       0.0.0.0/0 cert
    hostssl  all  all       ::/0      cert
    ```
  - `10-statepipes.sql`: `CREATE ROLE statepipes LOGIN;` and `ALTER DATABASE statepipes OWNER TO statepipes;`.
  - `docker run -d --restart always --name postgres-db --hostname postgres-db --network $Net -p 5432:5432`
    `-v postgres-db-data:/var/lib/postgresql/data` `-v <certs>:/certs-src:ro` `-v <pg_hba.conf>:/hba-src/pg_hba.conf:ro`
    `-v <initdb>:/docker-entrypoint-initdb.d:ro` `-e POSTGRES_PASSWORD=$CommonPassword -e POSTGRES_DB=statepipes`
    with a wrapper command that copies `/certs-src/*` and `/hba-src/pg_hba.conf` to `/etc/postgresql-certs/`,
    `chown postgres:postgres` and `chmod 600` the key, then
    `exec docker-entrypoint.sh postgres -c ssl=on -c ssl_min_protocol_version=TLSv1.3 -c ssl_cert_file=… -c ssl_key_file=… -c ssl_ca_file=… -c hba_file=…`.
    (PostgreSQL refuses a key readable by group/other, which every file bind-mounted from Windows is.)
  - Readiness loop on `docker exec postgres-db pg_isready`, failing with a `docker logs` hint like the existing loops.
  - Print the reset hint: `docker volume rm postgres-db-data`.
- **Step 9:** import `postgres-db.server.crt` into `TrustedPublisher`; summary line
  `PostgreSQL postgres-db:5432 (client-cert auth, role statepipes)`.

### 8.2 `DockerInfrastructureStop_ps1.sample`

Stop and remove `postgres-db`; keep the volume.

### 8.3 `DockerStart_ps1.sample`

Also copy `statepipes.postgres-db.client.p12` into the service certificate directory.

### 8.4 `Service_csproj.sample`

Append to the PostBuild `Exec` (bare-metal Debug runs from Visual Studio):
```
if exist $(ProgramData)\SmallStep\step-ca\statepipes.postgres-db.client.p12 copy $(ProgramData)\SmallStep\step-ca\statepipes.postgres-db.client.p12 $(OutDir)\certs\statepipes.postgres-db.client.p12
```
`if exist` keeps builds green for services without workflows or with pre-PostgreSQL infrastructure.

---

## 9. Tooling

### 9.1 ServiceCreatorTool

- **`-w` Add Workflow** (`--workflow-name`): generates `StateMachines/<Name>/<Name>.cs` (`: IWorkflow`),
  `States/TopLevelState.cs` (`BaseWorkflowState`, `IFirstStateForStateMachine`) and `States/FinishedState.cs`
  (`IFinalWorkflowState`). On a service's first workflow it also, idempotently:
  - inserts a `WorkflowStoreConfig` into `DefaultServiceConfiguration.cs` via new
    `DefaultServiceConfiguration_AddWorkflowStore.sample` (same mechanism as `_AddProxy.sample`);
  - inserts the §8.4 line into the service's `.Service.csproj` PostBuild `Exec` if absent.
- **`-a` Add State:** workflow-aware templates (`UnparentedWorkflowState`, `ParentedWorkflowState`,
  `FirstParentedWorkflowState`); new `--final` (headless) / prompt (interactive) adds `IFinalWorkflowState`.
- **`-t` Add Trigger:** for a workflow, generates `: BaseWorkflowTriggerCommand<TWorkflow>`.
- **`-pi` / `-pri` / `-ii`:** unchanged (identical guard signatures).
- **`-pti` Periodic trigger:** workflow variant stamps `CorrelationId = CorrelationId` on the message.
- **`ClaudeMdWriter`:** generated `CLAUDE.md` gains a "Workflows" section summarizing §3.4.
- **`DefaultServiceConfiguration_cs.sample` / `ServiceConfiguration`:** optional `workflowStore` parameter so existing
  generated code compiles unchanged.

### 9.2 Diagrammer

Workflows are discovered through `IStateMachine`. The dummy container setup gains a workflow path that builds the
Stateless graph from fresh state objects with no database. `DotGraphEnhancer` renders `IFinalWorkflowState` states with
a distinct terminal style.

### 9.3 Claude plugin (`StatePipes.ClaudePlugin`)

| Skill | Change |
|---|---|
| `statepipes-domain-fit` | "Many durable instances of one lifecycle keyed by an ID" is a workflow fit. |
| `statepipes-domain-modeling` | Model distinguishes singleton machines from workflows; per workflow: CorrelationId, final states, persisted fields, singleton request/reply commands. |
| `statepipes-conventions` | §3.4 rules; `[IgnoreIf]` for stale timer messages; no `FireExternal`. |
| `statepipes-scaffold` | New `operations/add-workflow.md` (including both §9.1 insertions); workflow variants in `add-state`, `add-trigger`, `add-periodic`; `new-service` covers `WorkflowStore`. |
| `statepipes-verify` | `Check-StatePipesService.ps1`: workflow states with constructor parameters; workflows lacking `IFinalWorkflowState`; `FireExternal` in workflow states; a `WorkflowForTest` test per new workflow transition. |
| `tests/scenarios.md` | Workflow scenarios: create a cart workflow, add a final state, add a periodic timeout. |

---

## 10. Testing

### 10.1 `WorkflowForTest<TWorkflow>` (public harness, `StatePipes.StateMachine.Test`)

Uses `InMemoryWorkflowStore`, an in-memory persisted-timer equivalent driven by `FakeTimeProvider`, and the existing
`TestStatePipesService`. No PostgreSQL required.

```csharp
using var wf = new WorkflowForTest<CartWorkflow>();
wf.SendCommand(new AddItem("sku-1", 2) { CorrelationId = "cart-1" });
Assert.AreEqual(nameof(CartOpenState), wf.CurrentState("cart-1"));
wf.AdvanceTime(TimeSpan.FromDays(7));
Assert.IsFalse(wf.Exists("cart-1"));
var lines = wf.GetField<CartOpenState, List<CartLine>>("cart-1", "_lines");
wf.SimulateLocked("cart-1");
// CommandInvocations / EventInvocations / FilterCommand / TrapCommand as in StateMachineForTest
```

### 10.2 Framework tests (`StatePipes.Test`)

- **Unit:**
  - validation (§3.4 rules 1, 3, 4, 5; non-serializable trigger);
  - contract resolver (private fields, backing fields, excluded base fields, sender → `{TimerId}`, binder rejects
    foreign types);
  - engine outcomes (declared/undeclared × new/existing; `[PermitReentryIf]` returning `false` persists; exception
    rollback; final state deletes instance and timers; `MoveToState` / LCA fix inside workflows; `OnEntry` not re-run on
    rehydration; schema evolution; unknown `current_state`);
  - timers (one-shot, periodic without drift, replace, stop, `Enabled`, skip-missed computation, timer survives a
    rehydration cycle);
  - requeue (retry count, delay, discard + log at max).
- **Regression:** the existing singleton-machine suite passes unchanged on `InMemoryTimerService`.
- **Integration** (`TestCategories.Integration`; `Inconclusive` when the client `.p12` is absent): idempotent table
  creation; full round trip; advisory-lock contention across two connections; two pollers + `SKIP LOCKED` fire each
  timer exactly once; periodic rescheduling; rollback leaves no row; password login and non-TLS connection are refused.
- **ServiceCreatorTool:** `-w`, `-a --final`, `-t` for workflows; csproj and `DefaultServiceConfiguration` insertions
  are idempotent.
- **ClaudePlugin:** new `Check-StatePipesService.ps1` checks against passing and failing fixtures.

### 10.3 Manual verification — infrastructure script

1. Run `DockerInfrastructureStart.ps1` as Administrator; all readiness loops pass.
2. From the host, connect with `statepipes.postgres-db.client.p12` and `VerifyFull` → succeeds.
3. Attempt a password login → refused. Attempt a non-TLS connection → refused.
4. `docker logs postgres-db` shows SSL on with TLS 1.3 minimum.
5. Re-run the script → workflow rows created earlier still exist (volume kept).

---

## 11. Paper.md conformance

- **B1 state cohesion / B2 additive extensibility / B3 natural exclusion:** unchanged — workflows use the same per-state
  classes and attributes; adding a state is a new class.
- **B4 structural testability:** `WorkflowForTest` places an instance in a state and asserts on state, fields, commands,
  events and timers.
- **B5 diagram accuracy:** workflows are diagrammed from the same attribute declarations, with final states marked.
- **Liability L1 (framework dependency)** grows: PostgreSQL becomes a runtime dependency for services that declare
  workflows (only those).
