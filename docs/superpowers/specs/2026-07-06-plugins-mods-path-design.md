# Custom Plugins and Mods Directory Design

Date: 2026-07-06

## Goal

Allow each instance to configure the relative directories used for server plugins and mods while preserving existing defaults and UI style.

Defaults remain unchanged:

- Plugins directory: `plugins`
- Mods directory: `mods`

This feature applies only to Minecraft server instance plugin/mod folders. It does not change the MSLX daemon extension plugin directory `DaemonData/Plugins`.

## Scope

In scope:

- Add per-instance `PluginsPath` and `ModsPath` settings.
- Store the paths in the existing instance configuration model.
- Show and save both fields in the existing instance general settings page.
- Make plugin/mod list and state operations use the configured paths.
- Validate that both paths are relative and stay inside the instance directory.
- Keep missing directories allowed at save time and reported at list time.

Out of scope:

- Absolute paths.
- Paths outside the instance directory.
- File picker UI.
- Automatically creating missing plugin/mod directories on save.
- Changing MSLX daemon extension plugin loading, installing, enabling, disabling, or deleting under `DaemonData/Plugins`.

## Data Model

Add properties to `MSLX.SDK.Models.McServerInfo.ServerInfo`:

```csharp
public string PluginsPath { get; set; } = "plugins";
public string ModsPath { get; set; } = "mods";
```

Compatibility:

- Existing `ServerList.json` entries do not need migration.
- Missing or blank values are treated as their defaults.
- Saving an instance writes normalized values back into server config.

The TypeScript instance settings model will add matching fields:

```ts
pluginsPath?: string;
modsPath?: string;
```

## Path Rules

The backend is the source of truth.

Rules:

- Blank plugin path resolves to `plugins`.
- Blank mod path resolves to `mods`.
- Only relative paths are allowed.
- Absolute paths are rejected, including Windows drive paths on non-Windows hosts.
- Path traversal such as `../plugins` is rejected.
- Resolved full path must remain under `server.Base`.
- Both `/` and `\` separators are accepted from input and normalized.

This should reuse or generalize the path validation created for `ServerPropertiesPath` so all instance-relative path settings behave consistently.

## Backend Changes

`InstanceSettingsController`:

- `GET /api/instance/settings/general/{id}` returns `PluginsPath` and `ModsPath` through the existing server info object.
- `POST /api/instance/settings/general/{id}` accepts, validates, normalizes, and saves both fields.
- Invalid path returns 400 with a clear message:
  - `插件目录路径必须是实例目录内的相对路径`
  - `模组目录路径必须是实例目录内的相对路径`
- Legal but missing directories save successfully.

`ServerUpdateService`:

- Preserve and save both fields during background update tasks, so downloading/updating a core does not drop the directory configuration.

`PluginsAndModsController`:

- Replace hard-coded `{server.Base}/plugins` and `{server.Base}/mods` with resolved configured paths.
- Use `PluginsPath` for `mode=plugins`.
- Use `ModsPath` for `mode=mods`.
- Apply the same configured path to:
  - list plugins/mods
  - client-only mod detection
  - enable/disable/delete operations
- Directory-missing messages should refer to the configured directory, not a fixed default directory.

Do not change daemon extension plugin controllers under `MSLX.Daemon/Controllers/PluginsController` or plugin loading in `Program.cs`.

## Frontend Settings UI

Add two rows in `GeneralSettings.vue`, near the existing `Server.properties 路径` row.

Style:

- Reuse the current row layout.
- Use `t-input` controls.
- Keep existing admin-only form disabling behavior.

Rows:

1. Label: `插件目录路径`
   Description: `相对实例路径，用于读取和管理服务端插件文件`
   Placeholder: `plugins 或 server/plugins`

2. Label: `模组目录路径`
   Description: `相对实例路径，用于读取和管理服务端模组文件`
   Placeholder: `mods 或 server/mods`

Frontend validation:

- Blank is allowed and normalized to the default before saving.
- Reject absolute-looking paths and `..` path segments before submit.
- Backend still performs final validation.

## Frontend Plugin/Mod Manager

`ModsPluginsManager.vue` should continue calling existing APIs without sending paths. The backend decides the target directories from instance settings.

If the page displays any directory-missing message coming from the backend, the wording should remain compatible with custom paths.

## Error Handling

Invalid path on save:

- HTTP 400.
- Clear localized message for the specific field.

Missing directory:

- Save succeeds.
- Plugin/mod list endpoint returns the existing not-found style response, but with wording for the configured directory.
- No directory is auto-created.

Read/action failure:

- Keep existing API error shape.
- Include enough message text to identify that the configured plugin/mod directory was not found or could not be accessed.

## Verification

Checks should cover:

- Old instances without the fields still use `plugins` and `mods`.
- `server/plugins` makes plugin listing read `{base}/server/plugins`.
- `server/mods` makes mod listing read `{base}/server/mods`.
- Enable/disable/delete uses the configured directory.
- Client-only mod detection uses the configured mod directory.
- Blank plugin/mod paths normalize to defaults.
- `../plugins`, `C:\plugins`, and `/plugins` fail validation.
- Missing configured directories save successfully and list APIs report missing directory.
- The settings page displays, saves, and reloads the two paths.
