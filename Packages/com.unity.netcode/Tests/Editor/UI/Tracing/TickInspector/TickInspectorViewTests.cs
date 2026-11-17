using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Netcode;
using Unity.Netcode.Editor.Tracing;
using Unity.Netcode.Editor.Tracing.UI;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEngine.TestTools;
using Unity.Netcode.Editor.Tracing.UI.TickInspector;
using Unity.Netcode.Tracing;

namespace Tests.Editor.UI.Tracing.TickInspector
{
    /// <summary>
    /// Tests for TickInspectorView.
    /// </summary>
    class TickInspectorViewTests : UITestFixture
    {
        TickInspectorView m_View;

        [SetUp]
        public void Setup()
        {
            m_View = new TickInspectorView();
            rootVisualElement.Add(m_View.Create());
            simulate.FrameUpdate();
        }

        [TearDown]
        public void TearDown()
        {
            m_View?.Dispose();
            rootVisualElement.Clear();
        }

        [UnityTest]
        public IEnumerator WhenDataAvailable_Initial_ViewIsHidden()
        {
            var data = CreateTracingData(firstFrame: 1, lastFrame: 3, withTicks: true);

            yield return m_View.OnDataAvailable(data);

            var root = rootVisualElement.Q(className: TickInspectorUssClasses.Base);
            Assert.That(root?.ClassListContains(TracingWindowUssClasses.Hidden), Is.True);

            data.ClientWorldData.Dispose();
            data.ServerWorldData.Dispose();
        }

        [UnityTest]
        public IEnumerator WhenDataAvailable_WithoutSelectedTick_ShowsSelectATickEmptyState()
        {
            var data = CreateTracingData(firstFrame: 1, lastFrame: 3, withTicks: true);

            yield return m_View.OnDataAvailable(data);

            var root = rootVisualElement.Q(className: TickInspectorUssClasses.Base);

            Assert.That(root?.ClassListContains(TracingWindowUssClasses.Hidden), Is.True);

            // Change to a valid tick first to ensure it's visible
            var tickId = new TickID { value = new NetworkTick(10) };
            var frameId = new FrameID { value = 1 };

            yield return m_View.OnSelectedTickOrFrameChanged(new TracingSelectionChange() { tickID = tickId, frameID = frameId });

            simulate.FrameUpdate();
            Assert.That(root?.ClassListContains(TracingWindowUssClasses.Hidden), Is.False);

            // Deselecting the tick while a frame is still selected keeps the view visible and
            // shows the "Select a tick to view detail" empty state instead of the tree.
            yield return m_View.OnSelectedTickOrFrameChanged(new TracingSelectionChange() { tickID = default, frameID = frameId });
            simulate.FrameUpdate();

            Assert.That(root?.ClassListContains(TracingWindowUssClasses.Hidden), Is.False);

            var emptyStateLabel = root.Query<Label>().Where(l => l.text == TickInspectorView.k_SelectTickText).First();
            Assert.That(emptyStateLabel, Is.Not.Null);
            Assert.That(emptyStateLabel.parent.ClassListContains(TracingWindowUssClasses.Hidden), Is.False);

            data.ClientWorldData.Dispose();
            data.ServerWorldData.Dispose();
        }


        [UnityTest]
        public IEnumerator OnSelectedTickOrFrameChanged_UpdatesToolbarLabels()
        {
            var data = CreateTracingData(firstFrame: 1, lastFrame: 3, withTicks: true);
            yield return m_View.OnDataAvailable(data);


            var frameId = new FrameID { value = 2 };
            var tickId = new TickID { value = new NetworkTick(20) };
            var change = new TracingSelectionChange { frameID = frameId, tickID = tickId };

            yield return m_View.OnSelectedTickOrFrameChanged(change);
            simulate.FrameUpdate();

            var root = rootVisualElement.Q(className: TickInspectorUssClasses.Base);
            Assert.That(root?.ClassListContains(TracingWindowUssClasses.Hidden), Is.False);

            var frameLabel = rootVisualElement.Q<Label>(TickInspectorView.k_TimelineFrameLabel);
            Assert.That(frameLabel?.text, Does.Contain("Frame 2"));

            var tickLabel = rootVisualElement.Q<Label>(TickInspectorView.k_TimelineTickLabel);
            Assert.That(tickLabel?.text, Does.Contain("Tick 20"));

            data.ClientWorldData.Dispose();
            data.ServerWorldData.Dispose();
        }

        [Test]
        public void SetFilters_ShowsFilterStatus_OnlyWhenTheViewIsNarrowed()
        {
            var statusLabel = rootVisualElement.Q<Label>(TickInspectorView.k_FilterStatusLabel);
            Assert.That(statusLabel, Is.Not.Null);

            // The target rows come from the store; start from an empty selection and keep the user's.
            var settingsBackup = TracingToolUserSettings.instance.CaptureStateForTests();
            TracingToolUserSettings.instance.ResetStateToDefaultForTests();
            try
            {
                var tracedSystem = TracingTargetId.Of(typeof(SimulationSystemGroup));
                TracingTargetStore.SetSelected(tracedSystem.Type, true);

                var rows = new List<TracingTarget>();
                TracingTargetStore.GetPanelRows(rows);
                var totalTargets = rows.Count;
                Assert.That(totalTargets, Is.EqualTo(1), "the traced system must be a target");

                // Nothing narrowed: the status note stays hidden.
                m_View.SetFilters(new TracingViewFilters());
                Assert.That(statusLabel.ClassListContains(TracingWindowUssClasses.Hidden), Is.True);

                // One hidden target, one deselected tag, changed values only: every narrowing is spelled out.
                TracingTargetStore.SetHidden(tracedSystem, true);
                var narrowed = new TracingViewFilters { ChangedValuesOnly = true };
                narrowed.EnabledDiffReasons = DiffInfo.AllDiffReasons & ~DiffInfo.DiffReasons.ComponentData;
                m_View.SetFilters(narrowed);

                var total = DiffReasonCatalog.All.Length;
                Assert.That(statusLabel.ClassListContains(TracingWindowUssClasses.Hidden), Is.False);
                Assert.That(statusLabel.text,
                    Is.EqualTo($"Showing {totalTargets - 1}/{totalTargets} tracing targets and {total - 1}/{total} diff tags, changed values only"));

                // With every target shown, the narrowed tags still spell the targets out as "all".
                TracingTargetStore.SetHidden(tracedSystem, false);
                var tagsOnly = new TracingViewFilters
                {
                    EnabledDiffReasons = DiffInfo.AllDiffReasons & ~DiffInfo.DiffReasons.ComponentData,
                };
                m_View.SetFilters(tagsOnly);
                Assert.That(statusLabel.text,
                    Is.EqualTo($"Showing all tracing targets and {total - 1}/{total} diff tags"));

                // A fuzzy threshold alone is also a narrowing and is spelled out.
                m_View.SetFilters(new TracingViewFilters { FuzzyDiffThreshold = 0.5f });
                Assert.That(statusLabel.ClassListContains(TracingWindowUssClasses.Hidden), Is.False);
                Assert.That(statusLabel.text, Does.EndWith("fuzzy threshold > 0.5"));

                // Widening back out hides the note again.
                m_View.SetFilters(new TracingViewFilters());
                Assert.That(statusLabel.ClassListContains(TracingWindowUssClasses.Hidden), Is.True);
            }
            finally
            {
                TracingToolUserSettings.instance.RestoreStateForTests(settingsBackup);
                TracingDataAccess.Config.Data.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator Clear_HidesView()
        {
            var data = CreateTracingData(firstFrame: 1, lastFrame: 3, withTicks: true);
            yield return m_View.OnDataAvailable(data);


            var frameId = new FrameID { value = 2 };
            var tickId = new TickID { value = new NetworkTick(20) };
            var change = new TracingSelectionChange { frameID = frameId, tickID = tickId };

            yield return m_View.OnSelectedTickOrFrameChanged(change);
            simulate.FrameUpdate();

            var root = rootVisualElement.Q(className: TickInspectorUssClasses.Base);
            Assert.That(root?.ClassListContains(TracingWindowUssClasses.Hidden), Is.False);

            m_View.Clear();
            simulate.FrameUpdate();

            Assert.That(root?.ClassListContains(TracingWindowUssClasses.Hidden), Is.True);

            data.ClientWorldData.Dispose();
            data.ServerWorldData.Dispose();
        }

        static TracedComponent MakeDiffingComponent() => new()
        {
            HasDiff = true,
            DiffReasonFlags = DiffInfo.DiffReasons.ComponentData,
            RowDiffReasonFlags = DiffInfo.DiffReasons.ComponentData,
        };

        static GhostComponentData MakeDiffingGhost(params Type[] componentTypes)
        {
            var components = new Dictionary<Type, TracedComponent>();
            foreach (var type in componentTypes)
                components[type] = MakeDiffingComponent();
            return new GhostComponentData
            {
                GhostName = "Ghost",
                HasDiff = true,
                DiffReasonFlags = DiffInfo.DiffReasons.ComponentData,
                RowDiffReasonFlags = DiffInfo.DiffReasons.ComponentData,
                Components = components,
            };
        }

        static DiffAggregate MakeAggregate(int ghostId, TypeIndex component, bool valueChanged) => new()
        {
            GhostId = ghostId,
            Component = component,
            Reasons = DiffInfo.DiffReasons.ComponentData,
            HasValueChanged = valueChanged,
        };

        static TickInspectorTreeView MakeTreeViewWithFilters(TracingViewFilters filters)
        {
            var treeView = new TickInspectorTreeView();
            treeView.OnSetFilters(filters);
            return treeView;
        }

        [Test(Description = "'Changed values only' is a highlight filter like the fuzzy threshold and the " +
                            "diff tags: it strips the highlight reasons of rows whose scope recorded no " +
                            "value change, and like them it never removes rows from the tree.")]
        public void ApplyReasonMasks_ChangedValuesOnly_MutesHighlightsByScope_LikeTheOtherFilters()
        {
            var changedType = TypeManager.GetTypeIndex<GhostInstance>();
            var unchangedType = TypeManager.GetTypeIndex<GhostOwner>();
            var systemData = new ManagedSystemData
            {
                SystemName = "MoveSystem",
                HasDiff = true,
                DiffReasonFlags = DiffInfo.DiffReasons.ComponentData,
                RowDiffReasonFlags = DiffInfo.DiffReasons.ComponentData,
                GhostComponents = new Dictionary<int, GhostComponentData>
                {
                    { 1, MakeDiffingGhost(typeof(GhostInstance), typeof(GhostOwner)) },
                    { 2, MakeDiffingGhost(typeof(GhostInstance)) },
                },
                // Only ghost 1's GhostInstance recorded an actual value change.
                DiffAggregates = new Dictionary<DiffAggregate, float>
                {
                    { MakeAggregate(1, changedType, valueChanged: true), 0f },
                    { MakeAggregate(1, unchangedType, valueChanged: false), 0f },
                    { MakeAggregate(2, changedType, valueChanged: false), 0f },
                },
            };
            var treeView = MakeTreeViewWithFilters(new TracingViewFilters { ChangedValuesOnly = true });

            var masked = treeView.ApplyReasonMasks(systemData, DiffInfo.AllDiffReasons);

            Assert.That(masked.HasDiff, Is.True, "a value change anywhere in the system keeps the system highlighted");

            var changedGhost = masked.GhostComponents[1];
            Assert.That(changedGhost.HasDiff, Is.True);
            Assert.That(changedGhost.Components[typeof(GhostInstance)].HasDiff, Is.True,
                "the component with the value change keeps its highlight");
            Assert.That(changedGhost.Components[typeof(GhostOwner)].HasDiff, Is.False,
                "a sibling component without a value change is muted");
            Assert.That(changedGhost.Components[typeof(GhostOwner)].DiffReasonFlags,
                Is.EqualTo(DiffInfo.DiffReasons.Undefined));

            Assert.That(masked.GhostComponents.ContainsKey(2), Is.True,
                "like the fuzzy threshold and the diff tags, the filter mutes highlights, it does not remove rows");
            var mutedGhost = masked.GhostComponents[2];
            Assert.That(mutedGhost.HasDiff, Is.False);
            Assert.That(mutedGhost.Components[typeof(GhostInstance)].HasDiff, Is.False);
        }

        [Test(Description = "With no value change anywhere in the system, every row's highlight goes " +
                            "quiet — system included — while the rows stay for context, exactly how a " +
                            "deselected diff tag or an unmet fuzzy threshold behaves.")]
        public void ApplyReasonMasks_ChangedValuesOnly_WithNoValueChangeAnywhere_MutesEveryHighlight()
        {
            var componentType = TypeManager.GetTypeIndex<GhostInstance>();
            var systemData = new ManagedSystemData
            {
                SystemName = "MoveSystem",
                HasDiff = true,
                DiffReasonFlags = DiffInfo.DiffReasons.ComponentData,
                RowDiffReasonFlags = DiffInfo.DiffReasons.ComponentData,
                GhostComponents = new Dictionary<int, GhostComponentData>
                {
                    { 1, MakeDiffingGhost(typeof(GhostInstance)) },
                },
                DiffAggregates = new Dictionary<DiffAggregate, float>
                {
                    { MakeAggregate(1, componentType, valueChanged: false), 0f },
                },
            };

            // Control: with the filter off, every highlight survives.
            var plain = MakeTreeViewWithFilters(new TracingViewFilters())
                .ApplyReasonMasks(systemData, DiffInfo.AllDiffReasons);
            Assert.That(plain.HasDiff, Is.True);
            Assert.That(plain.GhostComponents[1].Components[typeof(GhostInstance)].HasDiff, Is.True);

            var masked = MakeTreeViewWithFilters(new TracingViewFilters { ChangedValuesOnly = true })
                .ApplyReasonMasks(systemData, DiffInfo.AllDiffReasons);

            Assert.That(masked.HasDiff, Is.False, "no value change in the system mutes its own row");
            Assert.That(masked.DiffReasonFlags, Is.EqualTo(DiffInfo.DiffReasons.Undefined));
            Assert.That(masked.GhostComponents.ContainsKey(1), Is.True,
                "the ghost and component rows stay for context, as with every other highlight filter");
            Assert.That(masked.GhostComponents[1].HasDiff, Is.False);
            Assert.That(masked.GhostComponents[1].Components[typeof(GhostInstance)].HasDiff, Is.False);
        }

        // Creates minimal TracingData with the given frame range. Caller is responsible for disposing.
        static TracingData CreateTracingData(int firstFrame, int lastFrame, bool withTicks = false)
        {
            var clientWorldData = new WorldData(Allocator.Persistent);
            var serverWorldData = new WorldData(Allocator.Persistent);

            for (int f = firstFrame; f <= lastFrame; f++)
            {
                var frameId = new FrameID { value = f };
                var frameData = new FrameData(0.016f);

                if (withTicks)
                {
                    // Two ticks per frame; tick indices are f*10 and f*10+1.
                    var tickId1 = new TickID { value = new NetworkTick((uint)(f * 10)) };
                    var tickId2 = new TickID { value = new NetworkTick((uint)(f * 10 + 1)) };
                    var tickData1 = new TickData(0.016f, default, TraceType.Default);
                    var tickData2 = new TickData(0.016f, default, TraceType.Default);

                    frameData.TickIDs.Add(tickId1);
                    frameData.TickIDs.Add(tickId2);
                    frameData.PerTickData.Add(tickId1, tickData1);
                    frameData.PerTickData.Add(tickId2, tickData2);

                    var serverTickData1 = new TickData(0.016f, default, TraceType.Default);
                    var serverTickData2 = new TickData(0.016f, default, TraceType.Default);
                    serverWorldData.PerTickData.Add(tickId1, serverTickData1);
                    serverWorldData.PerTickData.Add(tickId2, serverTickData2);
                }

                clientWorldData.FrameIDs.Add(frameId);
                clientWorldData.PerFrameData.Add(frameId, frameData);
            }

            return new TracingData
            {
                ClientWorldData = clientWorldData,
                ServerWorldData = serverWorldData
            };
        }
    }
}
