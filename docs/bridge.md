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
- **Ids** are integers ≥ 1 chosen by the web side; each request is answered exactly once.
- **Errors** (`RpcErrorCodes`): `bad-request` (the parameters break the contract),
  `unknown-method`, `timeout` (30 s on the host, 15 s by default on the web side), `cancelled`,
  `too-large` (page the answer) and `internal` (details only in the host log).
- **Cancellation**: the web side sends `cancel` when it times out or its `AbortSignal` fires; the
  host cancels the handler's token and answers `cancelled`.
- **Limits**: requests ≤ 64 KiB (the host rejects larger ones unread), responses ≤ 4 MiB, events
  ≤ 256 KiB. Bulk data (tiles, images) will go through virtual-host URLs, not messages.
- **Security**: only the app origin's messages reach the dispatcher (`SkyHost.IsTrustedSource`).
  Envelopes are strict: an extra member, a wrong type or a bad id rejects the message. Payloads
  are deserialized strictly (unknown members rejected) and then checked against the record's
  DataAnnotations on the host. The web side validates every answer and event before use.

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
2. Register the handler in `UI/Services/AppEndpoints.cs`.
3. Run BridgeGen. The method is then typed on the web side:
   `host.request('area.verb', params)`.
4. Add an outgoing sample to `AppChannelTests.OutgoingSamples` and regenerate the golden files
   (`UPDATE_BRIDGE_FIXTURES=1 dotnet test tests/AstroPlanner.SkyBridge.Tests`).

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
| C# (UI) | `SkyHost` (routing by `v`), `Services/AppEndpoints` (the handlers), `WebShellWindow` |
| Web (`packages/bridge-client`) | `client.ts` (`createAppBridge`: `request`, `on`), `envelope.ts`, `generated/contracts.ts` |
| Web (`apps/planetarium`) | `host.ts` (the page's client), `useHostTheme` (`app.info` + `theme.changed` → `<html data-theme>`) |
