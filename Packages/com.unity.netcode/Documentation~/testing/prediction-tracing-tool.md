# Prediction Tracing Tool

Use the Netcode for Entities Prediction Tracing Tool to find out which system caused a client's predicted state to diverge from the server's authoritative state.

The Prediction Tracing Tool records the values of the components you select, before and after each of the systems you select, in both the client world and the server world. It then compares the two recordings tick by tick and highlights every point where the client and the server disagree. Because the tool records around individual systems, you can identify the first system that introduced a divergence, rather than only observing the [misprediction](../prediction.md) after the fact.

Use the Prediction Tracing Tool to investigate prediction errors, differences in system update order between the client and the server, and [ghosts](../ghosts.md) that exist on one side of the connection but not the other. To measure bandwidth and the cost of synchronizing ghosts instead, use the [Network Profiler tool](network-profiler.md).

The tool records ghost entities only. Entities that aren't ghosts never appear in a trace, whichever components you select.

## Requirements

To use the Prediction Tracing Tool, your project must meet the following requirements:

- Unity version: 6.7 or newer
- Netcode for Entities: `com.unity.netcode` package version 7.0 or newer

The Prediction Tracing Tool compares a client world against a server world, so both worlds must exist in the same process, and both must be in game. A world is in game when its connection has a `NetworkStreamInGame` component. Until then, the tool captures nothing. Tracing is unavailable in [single-world host mode](../single-world-host-mode.md), because that mode doesn't run separate client and server worlds.

## Select a tracing target

Tracing is opt-in, and the tool captures nothing until you set a tracing target. The set of systems and components that the tool records is called the tracing target, and it must contain at least one system and at least one component. Recording every system and component in a project uses a large amount of memory and takes a long time to process, so keep the target as small as your investigation allows and widen it as you narrow down the problem.

To set a tracing target:

1. Open the **Prediction Tracing Tool** window from **Window** > **Multiplayer** > **Prediction Tracing Tool**.
1. Open the **Prediction Tracing Targets** window in either of the following ways:
    - Select the **Targets** field at the top of the window.
    - Select **Set prediction tracing target** in the center of the window. This button appears only while no tracing target is set.
1. Use the **Systems** and **Components** tabs to choose what to record. The **All** tab lists systems and components together, and the **Selected** tab lists the current target.
1. For each component, choose whether it's required or optional:
    - **Required**: Netcode for Entities only records an entity if it has every required component.
    - **Optional**: Netcode for Entities records the component whenever an entity has it.
1. Close the window. The tool applies the new target immediately.

Until you select at least one system and at least one component, the window displays **Set a tracing target to get started** and records nothing.

The tracing target is saved per user, per project in `UserSettings/Packages/com.unity.netcode/`, so it persists between Unity Editor sessions.

>[!IMPORTANT]
>Changing the tracing target doesn't discard the data you have already captured, and it doesn't apply retroactively to that data. Each trace keeps the target that its recording started with: a target you remove keeps its recorded data and stays viewable, and a target you add isn't traced until you start a new recording. The **Tracing Targets** list in the **Filter Options** panel marks both cases so that you can tell them apart.

## Record a trace

To capture data:

1. Set a tracing target.
1. Enter Play mode and connect a client to a server. Use the [PlayMode Tool](playmode-tool.md) to create the client and server worlds and to apply representative network conditions.
1. Select **Record** in the toolbar to start capturing.
1. Reproduce the behavior you want to investigate.
1. Pause Play mode.

The tool displays data when the recording stops. This happens when you pause Play mode, when you select **Record** again to stop capturing mid-session, or when you exit Play mode.

The displayed data stays available until you clear it or start a new recording. Resuming Play mode from a pause discards it and starts capturing again, so inspect a capture while Play mode is still paused.

When the recording stops, the tool processes the captured data in two steps, and a progress bar reports which step is running:

1. **Processing raw traces**
1. **Processing diffs**

The tool then builds the UI, which it reports with a spinner instead of a progress bar.

Processing time and memory use scale with the number of frames you capture and the size of your tracing target. To control how much memory tracing consumes, keep your tracing target small and your captures short. Refer to [Memory use](#memory-use).

Select **Clear** to discard the captured data and start again.

## Inspect a trace

The **Prediction Tracing Tool** window is organized into a toolbar, a timeline, and the Tick Inspector. A separate overlay visualizes the same data in the Scene view.

### Timeline

The timeline shows the captured frames and the ticks within them. Ticks that contain a difference between the client and the server are highlighted, so you can scan a long capture for the first point of divergence. Each tick is labeled with its tick index, prefixed with `Partial` for a tick that covers only part of a network tick, and `Snapshot` for a tick that the tool captured to compare ghost sending against ghost updating.

Because a capture can contain thousands of frames, the timeline shows a window onto the capture instead of the whole capture at once. By default, this window covers the first 100 frames, or the first quarter of the capture if you captured fewer than 400 frames. Use the range slider to resize the window and move it across the capture, or type a frame number into the **Frame** field in the toolbar.

The playback controls move the selection through the capture:

| **Control** | **Description** |
|:------------|:----------------|
| **Go to previous frame** | Select the previous frame in the capture. |
| **Go to previous diff** | Select the closest earlier tick that contains a difference. |
| **Play** | Replay the capture in the Scene view. Refer to [Scene view visualization](#scene-view-visualization). |
| **Go to next diff** | Select the closest later tick that contains a difference. |
| **Go to next frame** | Select the next frame in the capture. |

Use **Go to next diff** and **Go to previous diff** rather than stepping frame by frame: in a typical capture, the majority of frames contain no difference at all. Both controls respect the active filters, so they skip the differences that you filtered out.

### Tick Inspector

The Tick Inspector shows the detail of the selected tick as a tree. Each system group contains the systems that ran inside it, each system lists the ghosts it touched, and each ghost lists the traced components on it. Systems appear in execution order, and ghosts and components appear in alphabetical order. Rows that contain a difference are highlighted, and each highlighted row is labeled with one or more tags that name the reason for the difference.

| **Tag** | **Description** |
|:--------|:----------------|
| **Component Data** | A traced component value differs between the client and the server. |
| **Missing Component** | The component is on the server but not on the client. |
| **Extra Component** | The component is on the client but not on the server. |
| **Missing Ghost** | The ghost is on the server but not on the client. |
| **Extra Ghost** | The ghost is on the client but not on the server. |
| **Missing System** | The system ran on the server but not on the client. |
| **Extra System** | The system ran on the client but not on the server. |
| **System Order** | The traced systems ran in a different order on each side of the connection. |
| **Batched Tick** | The server simulated this tick as part of a batch. |
| **Delta Time** | The client and server tick delta times differ. |
| **Partial Tick** | The difference is on a partial tick, where mismatches are expected. |

No system, ghost, or component row can carry a **Batched Tick**, **Delta Time**, or **Partial Tick** tag, so these three appear in the **Metadata** foldout at the top of the Tick Inspector instead. The foldout also shows the client and server delta times, the frame delta time, the server batch size, and the fraction of a tick that a partial tick covers. This is the data that explains those tags.

A tag appears highlighted while the **Diff Tags** section of the **Filter Options** panel selects its reason, and grayed out while that section deselects it. To stop displaying a reason, deselect it in that panel. Refer to [Filter a trace](#filter-a-trace).

Expand a component row to open a comparison table with a **Predicted Tick** column and a **Server Authority** column. Each column shows the component's field values **Before** and **After** the system ran, and the fields that don't match are highlighted. This tells you whether the system received divergent input or produced a divergent result, which is usually enough to distinguish a bug in the system itself from a bug earlier in the frame.

If only one side has the component, the column for the other side shows `No data`. If the component has no fields, both columns show `Present` or `Not present`, because presence is the only thing to compare.

### Scene view visualization

The **Prediction Tracing** overlay in the Scene view draws the recorded transforms of your traced ghosts, so you can see a divergence in space rather than reading it out of a table. Enable it with the **Show visualization** toggle.

The overlay draws three categories, and you can show, hide, and recolor each one independently:

| **Category** | **Description** |
|:-------------|:----------------|
| **Predicted** | The client's predicted state. |
| **Authority** | The server's authoritative state. |
| **Difference** | The line between the two, drawn when they don't match. |

Select **Play** in the toolbar to replay the capture in the Scene view. Use the dropdown next to **Play** to control how often the replay renders and how fast it runs:

| **Replay rendering frequency** | **Description** |
|:-------------------------------|:----------------|
| **Once per frame (last value)** | Render the last recorded value of each frame. |
| **Once per tick (last value)** | Render the last recorded value of each tick. |
| **Once per system** | Render every recorded value, one system at a time. This is the most detailed option and shows exactly which system moved a ghost. |

The replay speed multiplier ranges from `0.5` to `3` and applies to all three rendering frequencies.

## Filter a trace

Use the **Filter Options** panel to control how much detail the timeline and the Tick Inspector display. To open the panel, select the leftmost button in the toolbar. The panel also opens automatically the first time a trace finishes processing, unless you have already opened or closed it yourself.

Filtering only changes what the tool displays. It never reprocesses or discards captured data, so you can widen a filter again at any time.

The panel contains the following sections, from top to bottom:

| **Section** | **Description** |
|:------------|:----------------|
| **Fuzzy Diff Range** | Hide **Component Data** differences whose largest per-field difference doesn't exceed this value. Use it to suppress noise from floating point imprecision. Set it to `0` to turn the fuzzy filter off. The slider covers `0.01` to `10` on a logarithmic scale, and the field beside it accepts any value of `0` or greater. |
| **Changed Values Only** | Show component values only when the traced system changed them. Turn it on to cut a long component list down to the rows that did something. |
| **Diff Tags** | Show or hide each diff reason. Select the eye icon on a row to toggle that reason, or the eye icon in the header to toggle every reason at once. |
| **Tracing Targets** | Show or hide each recorded system and component. Select the eye icon on a row to toggle that target, or the eye icon in the header to toggle every target at once. |

When a filter is active, the Tick Inspector reports how much it hides at the top of the tree, for example `Showing 4/9 tracing targets and 8/11 diff tags`.

Differing delta times, batched ticks, and partial ticks are expected, and they're rarely the cause of a bug that you're chasing, so hide the **Delta Time**, **Batched Tick**, and **Partial Tick** tags when they crowd out real differences. To remove one of these causes from the capture altogether rather than hiding it, refer to [Best practices](#best-practices).

The **Tracing Targets** section also marks the targets that don't match the current recording:

- A target that you added after the recording started is marked as not yet traced. Resume or restart tracing to capture it.
- A target that you removed from the tracing target is marked as removed, but keeps its recorded data and stays viewable. Add it back to the tracing target to trace it again.

## Memory use

Tracing uses a fixed memory budget of 2048 MB, which you can't configure.

The trace buffer is a ring buffer. When a capture reaches the memory limit, the tool overwrites its oldest traces instead of stopping, so a long capture keeps only its most recent frames. To make sure the frames you care about survive, keep your tracing target small and start recording close to the behavior you want to investigate.

## Best practices

- Start narrow. Trace the one component that's visibly wrong, such as `LocalTransform`, across a handful of systems, then add systems as you narrow down where the divergence appears. A large tracing target increases memory use and processing time and produces more differences to read through.
- Work backwards from the first difference. A misprediction that's visible on screen is usually several systems downstream of its cause, so use **Go to previous diff** to walk back to the earliest tick that reports a difference.
- Expect some differences to be benign. Differing delta times, ticks that the server batched, partial ticks, and systems that legitimately only exist on the client or only on the server all show up as differences. Hide their diff tags in the **Filter Options** panel so that real problems stand out.
- Remove an expected cause of misprediction instead of filtering it out. For example, to stop tick batching from producing differences at all, set `ClientServerTickRate.MaxSimulationStepBatchSize` to `1` while you investigate. This narrows the capture to the mispredictions that your own code causes, which are the ones you can fix.
- Test under representative conditions. Use the [PlayMode Tool](playmode-tool.md) to add latency and packet loss. Many prediction errors only appear once the client is far enough ahead of the server.
- Reduce the capture length rather than the detail. It's usually more useful to capture a few seconds around the problem with a detailed tracing target than to capture a long session with a shallow one, and a long session loses its earliest frames to the memory limit anyway.

## Additional resources

- [Prediction](../prediction.md)
- [Prediction edge cases and known issues](../prediction-details.md)
- [PlayMode Tool](playmode-tool.md)
- [Network Profiler tool](network-profiler.md)
- [Netcode Project Settings reference](../project-settings.md)
