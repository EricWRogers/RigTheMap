# Upgrade from 1.x to 7.x

Version 7.0.0 of Netcode for Entities renames the `NetCode` namespace to `Netcode` and changes several defaults. Refer to the following sections for the changes that affect your project and the steps to take for each one.

## Update NetCode namespaces to Netcode

In earlier versions, Netcode for Entities used the `NetCode` namespace and type names with a capital C. Version 7.0.0 updates all namespaces and type names to use `Netcode` with a lowercase c. The auto updater makes this transition easier. If you experience issues with the auto updater, report using the [Unity Bug Reporter](https://unity.com/releases/editor/qa/bug-reporting).

> [!WARNING]
> The auto updater and the commands in this section modify your files in place. Commit or back up your work before you update your project.

The auto updater doesn't work in the following cases:

- Custom serializers: change `Unity_NetCode_Generated_Unity_NetCode` to `Unity_NetCode_Generated_Unity_Netcode`, and change every `Unity.NetCode` namespace reference to `Unity.Netcode`. Refer to the following sample in the EntityComponentSystemSamples repository for an example of what to change:
    - [`CustomChunkSerializer.cs`](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/master/NetcodeSamples/Assets/Samples/CustomSerializer/CustomChunkSerializer.cs)
- Custom templates: change every `Unity.NetCode` namespace reference to `Unity.Netcode`. Refer to the following samples in the EntityComponentSystemSamples repository for examples of what to change:
    - [`UserDefinedTemplates.cs`](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/master/NetcodeSamples/Assets/Samples/NetCodeGen/UserDefined/UserDefinedTemplates.cs)
    - [`UserDefinedGhostSubTypes.cs`](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/master/NetcodeSamples/Assets/Samples/NetCodeGen/UserDefined/UserDefinedGhostSubTypes.cs)

### Duplicate type suggestions in your IDE

Types such as `NetcodeConfig` have shims in the old namespace to allow for auto updating. If you use **Find Symbol** or autocomplete on those types, your integrated development environment (IDE) might suggest two versions: one with a capital C and one with a lowercase c. You can set up an IDE to ignore APIs marked with `[EditorBrowsable(EditorBrowsableState.Never)]`. Most IDEs don't ignore these APIs by default, so change the setting manually.

After you update your project, you can optionally set the `NETCODE_NO_OBSOLETE_HELPER` scripting define symbol in **Edit** > **Project Settings** > **Player** > **Scripting Define Symbols** to exclude the duplicated types from compilation. This step isn't required to complete the upgrade; it's a convenience that stops the old types from appearing in your IDE.

### Auto updater limitations

The auto update doesn't always complete in one pass. If the update is incomplete, trigger compilation again so that the auto updater can continue to apply its changes.

The auto updater prefixes every usage of a Netcode type with `Unity.Netcode`. For example, `new GhostOwner` becomes `new Unity.Netcode.GhostOwner`. The result is verbose, so add a `using Unity.Netcode;` directive to each file and let your IDE remove the prefixes automatically. The auto updater gets your project into a working state, but not necessarily into a clean one.

To add the directive to files whose changes reference the old `Unity.NetCode` namespace, you can run the following commands in Bash:

```bash
git diff -G "Unity.NetCode" --name-only -- "*.cs" > filesWithDiff.txt
cat filesWithDiff.txt | while IFS= read -r f; do { echo "using Unity.Netcode;"; cat "$f"; } > "$f.tmp" && mv "$f.tmp" "$f"; done
```

The commands match only files where a changed line references the old namespace, so check the result and add the directive manually to any file they miss.

Then run **Remove redundant qualifiers** in your IDE.

> [!IMPORTANT]
> If you make manual changes, note that the `NetworkTime` type has moved from the `Unity.NetCode` namespace to the `Unity.Netcode.NetcodeTime` namespace.

### Hash changes

Variant hashes are name-based. `GhostAuthoringInspectionComponent` now includes namespace migration logic that finds the hash for the old namespace and updates your prefabs with the corresponding new hash. As a result, you might notice hash changes in your prefabs.

### System ordering changes

Entities uses a system's full name to generate the hash that orders systems when no ordering attribute, such as `[UpdateBefore]`, is present. As a result, the namespace rename reorders every system that doesn't have an explicit ordering attribute. Keep the reordering in mind if you encounter behavior-breaking changes when you update to this version.

## NetcodeWorld replaces World in public APIs

Various APIs that deal with worlds used to fail at runtime when you passed a non-Netcode world. `NetcodeWorld`, previously internal, is now public, and several public APIs use it in place of the `World` type that the Entities package defines. The change produces compile errors instead of runtime errors.

Because of the change, you might encounter compilation errors in your own code. To resolve them, swap to `NetcodeWorld`. `NetcodeWorld` inherits from `World`, so the swap usually works.

## AutoConnectPort defaults to a nonzero port

`AutoConnectPort` now defaults to `7979` instead of `0`. New projects adopt this behavior, so `ClientServerBootstrap` activates Netcode worlds automatically: client worlds connect to the configured endpoint, and server and host worlds listen on it. A single-world host doesn't open a local client connection, because the same world both simulates and listens.

To revert to the previous behavior, update your bootstrapper to set `AutoConnectPort` to `0`.

## Single-world host mode is the default

[Single-world host mode](single-world-host-mode.md) is no longer experimental. Version 7.0.0 removes the `NETCODE_EXPERIMENTAL_SINGLE_WORLD_HOST` scripting define symbol, so delete it from **Edit** > **Project Settings** > **Player** > **Scripting Define Symbols**.

Version 7.0.0 also introduces a breaking change to the default host mode for client-hosted applications. In earlier versions, a client-hosted application always used binary worlds: separate client and server worlds connected through intra-process communication (IPC), with no alternative. Version 7.0.0 makes the host world mode configurable, and `NetcodeConfig.HostWorldModeSelection` defaults to `NetcodeConfig.HostWorldMode.SingleWorld`, so an application that uses `PlayType.ClientAndServer` now creates a single host world by default.

Single-world host mode differs from binary-worlds host mode in ways that don't produce compile errors, so you might only notice them as unexpected behavior at runtime. For example, client systems run in the host's world alongside the server systems, and those client systems receive every player's input rather than only the local player's. Before you migrate, refer to [Limitations and considerations](single-world-host-mode.md#limitations-and-considerations) for the full list of differences.

To keep the previous behavior, set **Host World Mode Selection** to **Binary Worlds** on your global `NetcodeConfig` asset.

## Additional resources

- [Upgrading from Entities 0.51 to 1.0](https://docs.unity3d.com/Packages/com.unity.netcode@1.12/manual/upgrade-guide.html) in the version 1.12 documentation, for projects that haven't upgraded to version 1.x yet
