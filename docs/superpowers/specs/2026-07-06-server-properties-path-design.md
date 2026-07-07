# Custom server.properties Path Design

Date: 2026-07-06

## Goal

Allow each instance to configure the relative path used for reading and editing `server.properties`, while keeping existing instances compatible and matching the current instance settings UI style.

The default behavior remains unchanged: if no custom path is configured, MSLX reads `{instance base}/server.properties`.

## Scope

In scope:

- Add a per-instance `ServerPropertiesPath` setting.
- Store the path in the existing instance configuration model.
- Use the configured path when `/api/instance/info` reads port, difficulty, game mode, level name, and online mode.
- Show and save the setting in the existing instance general settings page.
- Make the Server.properties editor read and write the same configured path.
- Validate that the path is relative and stays inside the instance directory.

Out of scope:

- Absolute paths.
- Paths outside the instance directory.
- File picker UI.
- Creating missing `server.properties` files automatically.
- Bedrock-specific config parsing or non-`key=value` formats.

## Data Model

Add a property to `MSLX.SDK.Models.McServerInfo.ServerInfo`:

```csharp
public string ServerPropertiesPath { get; set; } = "server.properties";
```

Meaning: a path relative to the instance base directory.

Compatibility:

- Existing `ServerList.json` entries do not need migration.
- Missing or blank values are treated as `server.properties`.
- Saving an instance writes the normalized value back into the server config.

The TypeScript instance settings model will add the matching `serverPropertiesPath?: string` field.

## Path Rules

The backend is the source of truth for validation and resolution.

Rules:

- Blank input resolves to `server.properties`.
- Only relative paths are allowed.
- Absolute paths are rejected.
- Path traversal such as `../server.properties` is rejected.
- The resolved full path must remain under `server.Base`.
- Both `/` and `\` separators are accepted from input and resolved through platform path APIs.

Recommended backend helper behavior:

```text
NormalizeServerPropertiesPath(input) -> string
ResolveServerPropertiesPath(server) -> full path
```

The helper should be used anywhere the code needs the configured `server.properties` path.

## Backend Changes

`InstanceSettingsController`:

- `GET /api/instance/settings/general/{id}` returns `ServerPropertiesPath` through the existing server info object.
- `POST /api/instance/settings/general/{id}` accepts and validates `ServerPropertiesPath` via `UpdateServerRequest`.
- Invalid path returns 400 with a clear message: `server.properties 路径必须是实例目录内的相对路径`.
- A legal but missing file path is saved successfully.

`InstanceInfoController.GetInstanceInfo`:

- Replace the hard-coded `{server.Base}/server.properties` path with the resolved configured path.
- If the file exists, parse it with `ServerPropertiesLoader` using `server.FileEncoding`.
- If the file does not exist, return `mcConfig` values as `未知`.
- If parsing fails because of IO, permissions, or encoding, keep the instance info endpoint successful and return `未知` values for `mcConfig`; log the failure for diagnosis.

Returned `mcConfig` should continue to include:

- `difficulty`
- `gamemode`
- `levelName`
- `serverPort`
- `onlineMode`

Optionally include:

- `serverPropertiesPath`
- `serverPropertiesExists`

These optional fields help the frontend show a precise editor hint without changing the main display behavior.

## Frontend Settings UI

Add one setting row in `GeneralSettings.vue`, near the existing file encoding controls because the user selected this placement.

Style:

- Reuse the current row layout: left label and description, right control.
- Use `t-input` for the value.
- Keep the form disabled for non-admin users through the existing form-level disabled behavior.

Text:

- Label: `Server.properties 路径`
- Description: `相对实例路径，用于读取端口、难度、游戏模式等服务端配置`
- Placeholder: `server.properties 或 config/server.properties`

Frontend validation:

- Blank is allowed and normalized to default before saving.
- Reject absolute-looking paths and `..` path segments before submit.
- Backend still performs final validation.

## Server.properties Editor

The current editor reads and writes `server.properties` directly. It should instead use the configured path so the displayed summary and the editor operate on the same file.

Behavior:

- Load instance settings or instance info to determine `serverPropertiesPath`.
- Use that value in `getFileContent(instanceId, path)` and `saveFileContent(instanceId, path, content)`.
- If the configured file does not exist, show a non-blocking warning in the editor instead of silently editing the default root file.
- The editor should not auto-create a missing file unless the user explicitly saves content.

## Data Flow

1. User opens instance settings.
2. Frontend calls `getInstanceSettings(id)`.
3. `serverPropertiesPath` appears in the advanced/file encoding area.
4. User saves the form.
5. Frontend calls `postInstanceSettings(formData)`.
6. Backend validates and stores `ServerPropertiesPath` in the instance config.
7. Instance console calls `getInstanceInfo(id)`.
8. Backend resolves `{server.Base}/{ServerPropertiesPath}` and reads config values from that file.
9. Server.properties editor uses the same path for file reads and writes.

## Error Handling

Invalid path on save:

- HTTP 400.
- Message: `server.properties 路径必须是实例目录内的相对路径`.

Missing file:

- Save succeeds.
- Instance info shows `未知` for derived Minecraft config fields.
- Editor shows a warning that the configured file was not found.

Read/parse failure:

- Instance info endpoint remains successful.
- Derived fields become `未知`.
- Backend logs the file path and exception message.

## Verification

Manual and automated checks should cover:

- Old instances without `ServerPropertiesPath` still read `{base}/server.properties`.
- `config/server.properties` reads port, difficulty, game mode, level name, and online mode correctly.
- Blank value behaves as `server.properties`.
- `../server.properties` is rejected.
- Windows absolute paths such as `C:\temp\server.properties` are rejected.
- Unix absolute paths such as `/tmp/server.properties` are rejected.
- Legal but missing paths save successfully and display `未知` in instance info.
- The settings page displays, saves, and reloads the configured path.
- The Server.properties editor uses the same configured path.
