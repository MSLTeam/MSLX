# Plugins and Mods Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let each instance configure relative plugin and mod directories used by the instance plugin/mod manager.

**Architecture:** Add `PluginsPath` and `ModsPath` to the existing instance model/request, validate them with the same relative-path semantics as `ServerPropertiesPath`, and make `PluginsAndModsController` resolve paths from instance config. The frontend adds two settings rows near `Server.properties 路径`.

**Tech Stack:** ASP.NET Core 10, C#, Vue 3, TypeScript, TDesign Vue Next, xUnit.

---

## Files

Modify:

- `MSLX.SDK/Models/MCServerInfo.cs`
- `MSLX.SDK/Models/Instance/UpdateServerRequest.cs`
- `MSLX.Daemon/Services/DeployServerService/ServerUpdateService.cs`
- `MSLX.Daemon/Controllers/InstanceControllers/InstanceSettingsController.cs`
- `MSLX.Daemon/Controllers/FilesControllers/PluginsAndModsController.cs`
- `MSLX.Tests/ServerPropertiesPathUtilsTests.cs`
- `MSLX.WebPanel/src/api/model/instance.ts`
- `MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/GeneralSettings.vue`

---

### Task 1: Backend model, validation, and controller usage

- [ ] Add to `MSLX.SDK/Models/MCServerInfo.cs` next to `ServerPropertiesPath`:

```csharp
public string PluginsPath { get; set; } = "plugins";
public string ModsPath { get; set; } = "mods";
```

- [ ] Add to `MSLX.SDK/Models/Instance/UpdateServerRequest.cs` next to `ServerPropertiesPath`:

```csharp
public string PluginsPath { get; set; } = "plugins";
public string ModsPath { get; set; } = "mods";
```

- [ ] Add request validation by reusing the same relative path validation shape used for `ServerPropertiesPath`. Invalid messages:

```text
插件目录路径必须是实例目录内的相对路径
模组目录路径必须是实例目录内的相对路径
```

- [ ] Save normalized values in `InstanceSettingsController` after `ServerPropertiesPath`:

```csharp
server.PluginsPath = ServerPropertiesPathUtils.NormalizeRelativePath(request.PluginsPath, "plugins", "插件目录路径必须是实例目录内的相对路径");
server.ModsPath = ServerPropertiesPathUtils.NormalizeRelativePath(request.ModsPath, "mods", "模组目录路径必须是实例目录内的相对路径");
```

If the existing utility only accepts one default/message, extend it with an overload:

```csharp
public static string NormalizeRelativePath(string? path, string defaultPath, string invalidPathMessage)
```

Keep the existing `NormalizeRelativePath(string? path)` behavior unchanged for server.properties.

- [ ] Save the same values in `ServerUpdateService` so background core updates do not drop them.

- [ ] In `PluginsAndModsController`, replace hard-coded target path:

```csharp
string targetPath = mode == "plugins" ? Path.Combine(server.Base, "plugins") : Path.Combine(server.Base, "mods");
```

with a helper:

```csharp
private static string GetPluginOrModPath(McServerInfo.ServerInfo server, string? mode)
{
    var relativePath = mode == "mods"
        ? ServerPropertiesPathUtils.NormalizeRelativePath(server.ModsPath, "mods", "模组目录路径必须是实例目录内的相对路径")
        : ServerPropertiesPathUtils.NormalizeRelativePath(server.PluginsPath, "plugins", "插件目录路径必须是实例目录内的相对路径");

    var check = FileUtils.GetSafePath(server.Base, relativePath);
    if (!check.IsSafe) throw new ArgumentException(check.Message);
    return check.FullPath;
}
```

Use this helper in list and enable/disable/delete endpoints.

- [ ] Add/extend xUnit tests in `MSLX.Tests/ServerPropertiesPathUtilsTests.cs` for defaults and invalid values:

```csharp
Assert.Equal("plugins", ServerPropertiesPathUtils.NormalizeRelativePath(null, "plugins", "bad"));
Assert.Equal("mods", ServerPropertiesPathUtils.NormalizeRelativePath("", "mods", "bad"));
Assert.Throws<ArgumentException>(() => ServerPropertiesPathUtils.NormalizeRelativePath("../plugins", "plugins", "bad"));
Assert.Throws<ArgumentException>(() => ServerPropertiesPathUtils.NormalizeRelativePath(@"C:\\plugins", "plugins", "bad"));
```

- [ ] Run:

```bash
dotnet test MSLX.Tests/MSLX.Tests.csproj --filter ServerPropertiesPathUtilsTests
```

Expected: pass.

---

### Task 2: Frontend settings fields

- [ ] Add to `MSLX.WebPanel/src/api/model/instance.ts` in both update/settings models:

```ts
pluginsPath?: string;
modsPath?: string;
```

- [ ] In `GeneralSettings.vue`, add defaults:

```ts
pluginsPath: 'plugins',
modsPath: 'mods',
```

- [ ] Add normalize/validation using the existing `normalizeServerPropertiesPath` and `isSafeServerPropertiesPath`, or rename locally to a generic instance relative path helper.

- [ ] Add validation rules for both fields with messages:

```text
插件目录路径必须是实例目录内的相对路径
模组目录路径必须是实例目录内的相对路径
```

- [ ] Normalize loaded/saved values:

```ts
pluginsPath: normalizeInstanceRelativePath(res.pluginsPath, 'plugins'),
modsPath: normalizeInstanceRelativePath(res.modsPath, 'mods'),
```

Before submit:

```ts
formData.value.pluginsPath = normalizeInstanceRelativePath(formData.value.pluginsPath, 'plugins');
formData.value.modsPath = normalizeInstanceRelativePath(formData.value.modsPath, 'mods');
```

- [ ] Add two UI rows after `Server.properties 路径`:

```vue
<div class="...same row classes...">
  <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
    <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">插件目录路径</div>
    <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">相对实例路径，用于读取和管理服务端插件文件</div>
  </div>
  <div class="w-full md:w-[340px] shrink-0 flex">
    <t-input v-model="formData.pluginsPath" placeholder="plugins 或 server/plugins" class="w-full" />
  </div>
</div>
<div class="...same row classes...">
  <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
    <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">模组目录路径</div>
    <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">相对实例路径，用于读取和管理服务端模组文件</div>
  </div>
  <div class="w-full md:w-[340px] shrink-0 flex">
    <t-input v-model="formData.modsPath" placeholder="mods 或 server/mods" class="w-full" />
  </div>
</div>
```

- [ ] Run:

```bash
cd MSLX.WebPanel && npm run build
```

Expected: pass and copy assets to `MSLX.Daemon/Frontend`.

---

### Task 3: Final verification

- [ ] Run:

```bash
dotnet test MSLX.Tests/MSLX.Tests.csproj
dotnet build MSLX.Daemon/MSLX.Daemon.csproj
cd MSLX.WebPanel && npm run build
```

Expected: all pass.

- [ ] Search for remaining hard-coded instance plugin/mod manager paths in `PluginsAndModsController`:

```bash
rg 'Path\.Combine\(server\.Base, "plugins"|Path\.Combine\(server\.Base, "mods"' MSLX.Daemon/Controllers/FilesControllers/PluginsAndModsController.cs
```

Expected: no matches.

---

## Self-Review Notes

Spec coverage: model fields, backend validation/save, plugin/mod controller path use, frontend settings UI, validation, and verification are covered.

No unrelated daemon extension plugin path changes are included.
