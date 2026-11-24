# WebView2 bridge

The web pages and the C# host talk over one WebView2 message pipe (`PostWebMessageAsJson` /
`window.chrome.webview.postMessage`). Two protocols share it, told apart by the top-level `v`:

| `v` | Channel | Used by | Contracts |
|---|---|---|---|
| 1 | **Sky protocol**: the sky engine's messages (frame transform, bodies, layers, selection…) | both pages | hand-written on both sides, kept in step by golden files ([sky-renderer.md](sky-renderer.md#protocol-54)) |
| 2 | **App channel**: requests, responses and events of the v3 UI | `planetarium.html` (`--ui=web`) | C# records, TypeScript generated from them |

The sky protocol is unchanged by v3. The sky engine ignores `v: 2` messages; the app client ignores
`v: 1` messages. On the host, `SkyHost` routes by `AppMessageCodec.IsAppMessage`.

## App channel (v2)

```
web → host   {"v":2,"kind":"req","id":7,"type":"app.info","payload":{}}
             {"v":2,"kind":"cancel","id":7}
host → web   {"v":2,"kind":"res","id":7,"type":"app.info","payload":{…}}
             {"v":2,"kind":"res","id":7,"type":"app.info","error":{"code":"timeout","message":"…"}}
             {"v":2,"kind":"evt","type":"theme.changed","payload":{"theme":"night"}}
```

- **Names** are `area.verb` (`app.info`, later `targets.query`, `plan.schedule`…): a lower-case
  area, then camelCase segments.
- **Ids** are integers 1…2³¹−1 chosen by the web side (wrapping, skipping ids in use); each
  request is answered exactly once. A request that reuses the id of one still running is dropped
  (logged, not answered), so it cannot settle the other.
- **Errors** (`RpcErrorCodes`): `bad-request` (the parameters break the contract),
  `unknown-method`, `timeout` (30 s on the host, 15 s by default on the web side), `cancelled`,
  `too-large` (page the answer), `busy` (more than 32 requests running) and `internal` (details
  only in the host log).
- **Cancellation**: the web side sends `cancel` when it times out or its `AbortSignal` fires; the
  host cancels the handler's token and answers `cancelled`. Closing the window cancels everything.
- **Handlers** must be asynchronous: the timeout answers once a handler yields, so heavy work goes
  to `Task.Run`. A handler that outlives its answer keeps its id reserved until it ends, and a late
  failure is logged.
- **Limits** count UTF-16 characters (up to ~3× as many UTF-8 bytes, harmless at these sizes):
  requests ≤ 64 Ki (longer messages are not even scanned), responses ≤ 4 Mi, events ≤ 256 Ki.
  Bulk data (tiles, images) will go through virtual-host URLs, not messages.
- **Robustness**: nothing the page sends can throw into the host. Decoding failures, a page that is
  already gone when the answer is sent, and events that break their contract are logged, not
  thrown. Envelopes with duplicate members (`{"v":2,…,"v":1}`) are rejected.
- **Security**: only the app origin's messages reach the dispatcher (`SkyHost.IsTrustedSource`).
  Envelopes are strict: an extra member, a wrong type or a bad id rejects the message. Payloads
  are deserialized strictly (unknown members rejected) and then checked against the record's
  DataAnnotations on the host, recursively through nested records and lists (`ContractValidator`).
  The web side validates every answer and event before use.

### Contracts: C# first, TypeScript generated

The records in `src/AstroPlanner.SkyBridge/App/AppContracts.cs` are the source of truth. Their
constraints are DataAnnotations on the properties (`StringLength`, `Range`, `Length` for lists,
`AllowedValues`). `AppMethods` and `AppEvents` list the methods and events.

`tools/AstroPlanner.BridgeGen` writes
`src/web/packages/bridge-client/src/generated/contracts.ts`: one zod schema and one TypeScript
type per record, the `rpcMethods` and `appEvents` maps, the error codes and the limits. It reads
the JSON metadata of `AppJsonContext` (wire names, nullability) and the attributes.

```bash
dotnet run --project tools/AstroPlanner.BridgeGen            # regenerate after changing a contract
dotnet run --project tools/AstroPlanner.BridgeGen -- check   # exit 1 if the file is stale
```

`AppChannelTests.GeneratedTypeScriptContracts_AreUpToDate` runs the same check in `dotnet test`
(so CI fails on a stale file).

### Adding a method

1. Add the request and response records to `AppContracts.cs` (with their constraints) and to
   `AppJsonContext`, and the `RpcMethod` to `AppMethods`.
2. Register the handler in the area's endpoint module (`Application/Bridge`, below); a new area
   gets a new module, added to `UI/Services/AppEndpoints.cs`.
3. Run BridgeGen. The method is then typed on the web side:
   `host.request('area.verb', params)`.
4. Add an outgoing sample to `AppChannelTests.OutgoingSamples` and regenerate the golden files
   (`UPDATE_BRIDGE_FIXTURES=1 dotnet test tests/AstroPlanner.SkyBridge.Tests`).

### Endpoint modules: snapshot, revision, change event

Each area of the web UI (one window) has an `IEndpointModule` in `Application/Bridge`, over the
same view models and state services as the classic window. Every module follows one pattern:

- `area.get` returns an immutable snapshot (a contract record) with a `revision`;
- commands are methods that call the view models;
- one `area.changed {revision}` event tells the page that the state moved on. A `ChangeFeed`
  coalesces a burst of changes (a slider, a recalculation that touches ten properties) into one
  event after 50 ms, published on the UI thread; the page fetches `area.get` again when the
  revision it holds is older (TanStack Query invalidation in R1).

Events never carry rows: big lists are paged requests (`targets.query`, R4), because a response
is limited to 4 Mi characters and an event to 256 Ki.

View models belong to the UI thread. Handlers run there (the dispatcher keeps the UI context);
a timer or a background completion comes back through `IUiDispatcher`. `AppEndpoints` (UI)
creates the dispatcher and the modules; `Stop()` disposes them and cancels running requests.

| Module | Methods | Events |
|---|---|---|
| `AppInfoEndpoints` | `app.info` (versions, theme) | `theme.changed` |
| `ObserverEndpoints` | `observer.get` (site, time, night, Moon, sky quality) | `observer.changed` |

### Golden files (`tests/fixtures/bridge/app`)

Both suites read these files: `AppChannelTests` in C# and `packages/bridge-client/test/contract.test.ts`.

| Folder | Content |
|---|---|
| `outgoing/` | host → web samples, written by the C# tests; each payload must match its zod schema |
| `incoming/` | web → host samples that both sides accept |
| `invalid/` | web → host messages that both sides reject |

### Code

| Side | Files |
|---|---|
| C# (`AstroPlanner.SkyBridge/App`) | `AppContracts` (records), `AppProtocol` (JSON context, `AppMethods`, `AppEvents`), `AppMessageCodec` (envelopes, limits), `RpcDispatcher` (routing, validation, timeout, cancellation) |
| C# (Application) | `Bridge/IEndpointModule`, `ChangeFeed`, `IUiDispatcher`, `AppInfoEndpoints`, `ObserverEndpoints` |
| C# (UI) | `SkyHost` (routing by `v`), `Services/AppEndpoints` (dispatcher + modules), `WebShellWindow` |
| Web (`packages/bridge-client`) | `client.ts` (`createAppBridge`: `request`, `on`), `envelope.ts`, `generated/contracts.ts` |
| Web (`apps/planetarium`) | `host.ts` (the page's client), `useHostTheme` (`app.info` + `theme.changed` → `<html data-theme>`) |
