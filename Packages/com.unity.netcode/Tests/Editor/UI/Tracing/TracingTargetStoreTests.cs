using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Entities;
using Unity.Netcode.Tracing;

namespace Unity.Netcode.Editor.Tracing.UI.Tests
{
    /// <summary>
    /// The rules of <see cref="TracingTargetStore"/>: one notification per edit, no-ops stay silent, the runtime
    /// config and the persisted selection mirror the store, the recording set and GhostInstance follow their
    /// special cases, and the panel rows group by recording state.
    /// </summary>
    class TracingTargetStoreTests
    {
        const TracingTargetChanges k_AllChanges = TracingTargetChanges.Selection | TracingTargetChanges.Requirement
            | TracingTargetChanges.Recording | TracingTargetChanges.Visibility;

        string m_SettingsBackup;
        readonly List<TracingTargetChanges> m_Changes = new();

        static IReadOnlyList<TracingTargetSelectionEntry> Persisted => TracingToolUserSettings.instance.TargetSelection;

        [SetUp]
        public void SetUp()
        {
            TypeManager.Initialize();
            m_SettingsBackup = TracingToolUserSettings.instance.CaptureStateForTests();
            TracingToolUserSettings.instance.ResetStateToDefaultForTests();
            TracingDisplayState.SetShowingTraces(false);
            m_Changes.Clear();
            TracingTargetStore.Changed += m_Changes.Add;
        }

        [TearDown]
        public void TearDown()
        {
            TracingTargetStore.Changed -= m_Changes.Add;
            TracingDisplayState.SetShowingTraces(false);
            TracingToolUserSettings.instance.RestoreStateForTests(m_SettingsBackup);
        }

        [Test]
        public void Select_RaisesSelectionOnce_AndNoOpsRaiseNothing()
        {
            TracingTargetStore.SetSelected(typeof(GhostSendSystem), true);
            Assert.That(m_Changes, Is.EqualTo(new[] { TracingTargetChanges.Selection }));

            TracingTargetStore.SetSelected(typeof(GhostSendSystem), true);
            Assert.That(m_Changes.Count, Is.EqualTo(1), "re-selecting raises nothing");
            TracingTargetStore.SetSelected(typeof(GhostOwner), false);
            Assert.That(m_Changes.Count, Is.EqualTo(1), "deselecting an unknown target raises nothing");
            TracingTargetStore.SetHidden(TracingTargetId.Of(typeof(GhostOwner)), true);
            Assert.That(m_Changes.Count, Is.EqualTo(1), "hiding an unknown target raises nothing");
        }

        [Test]
        public void BulkSelect_RaisesOnce_AndMirrorsTheRuntimeConfig()
        {
            var types = new[] { typeof(GhostSendSystem), typeof(GhostOwner), typeof(GhostInstance) };
            TracingTargetStore.SetSelected(types, true);
            Assert.That(m_Changes, Is.EqualTo(new[] { TracingTargetChanges.Selection }));

            ref var config = ref TracingDataAccess.Config.Data;
            Assert.IsTrue(config.Initialized);
            Assert.IsTrue(config.SystemTypesToTrace.Contains(TypeManager.GetSystemTypeIndex<GhostSendSystem>()));
            Assert.IsTrue(config.OptionalTypesToTrace.Contains(ComponentType.ReadWrite<GhostOwner>()));
            Assert.IsFalse(config.OptionalTypesToTrace.Contains(ComponentType.ReadWrite<GhostInstance>()),
                "GhostInstance is always traced");
            Assert.AreEqual(1, config.SystemTypesToTrace.Count);
            Assert.AreEqual(1, config.OptionalTypesToTrace.Count);
            Assert.AreEqual(0, config.RequiredTypesToTrace.Count);

            TracingTargetStore.SetSelected(types, false);
            Assert.That(m_Changes,
                Is.EqualTo(new[] { TracingTargetChanges.Selection, TracingTargetChanges.Selection }));
            Assert.AreEqual(0, config.SystemTypesToTrace.Count);
            Assert.AreEqual(0, config.OptionalTypesToTrace.Count);
            Assert.AreEqual(0, TracingTargetStore.Targets.Count);
            Assert.That(Persisted, Is.Empty);
        }

        [Test]
        public void Required_MovesBetweenRuntimeSets_AndSelectsUnselectedComponents()
        {
            TracingTargetStore.SetRequired(typeof(GhostOwner), true);
            Assert.That(m_Changes,
                Is.EqualTo(new[] { TracingTargetChanges.Selection | TracingTargetChanges.Requirement }));
            Assert.IsTrue(TracingTargetStore.IsSelected(typeof(GhostOwner)));
            Assert.IsTrue(TracingTargetStore.IsRequired(typeof(GhostOwner)));
            ref var config = ref TracingDataAccess.Config.Data;
            Assert.IsTrue(config.RequiredTypesToTrace.Contains(ComponentType.ReadWrite<GhostOwner>()));
            Assert.IsTrue(Persisted[0].required);

            m_Changes.Clear();
            TracingTargetStore.SetRequired(typeof(GhostOwner), false);
            Assert.That(m_Changes, Is.EqualTo(new[] { TracingTargetChanges.Requirement }));
            Assert.IsTrue(config.OptionalTypesToTrace.Contains(ComponentType.ReadWrite<GhostOwner>()));
            Assert.IsFalse(config.RequiredTypesToTrace.Contains(ComponentType.ReadWrite<GhostOwner>()));
            Assert.IsFalse(Persisted[0].required);

            m_Changes.Clear();
            TracingTargetStore.SetRequired(typeof(GhostOwner), false);
            Assert.That(m_Changes, Is.Empty, "a no-op requirement change raises nothing");
            TracingTargetStore.SetRequired(typeof(GhostSendSystem), true);
            Assert.That(m_Changes, Is.Empty, "systems have no requirement");
            Assert.IsFalse(TracingTargetStore.IsSelected(typeof(GhostSendSystem)));

            // Deselecting forgets the requirement.
            TracingTargetStore.SetRequired(typeof(GhostOwner), true);
            TracingTargetStore.SetSelected(typeof(GhostOwner), false);
            TracingTargetStore.SetSelected(typeof(GhostOwner), true);
            Assert.IsFalse(TracingTargetStore.IsRequired(typeof(GhostOwner)));
        }

        [Test]
        public void RequiredAndHidden_RoundTripThroughTheSettings_InSelectionOrder()
        {
            TracingTargetStore.SetRequired(typeof(GhostOwner), true);
            TracingTargetStore.SetHidden(TracingTargetId.Of(typeof(GhostOwner)), true);
            TracingTargetStore.SetSelected(typeof(GhostSendSystem), true);
            TracingTargetStore.CaptureRecording();
            var json = TracingToolUserSettings.instance.CaptureStateForTests();

            TracingToolUserSettings.instance.ResetStateToDefaultForTests();
            Assert.AreEqual(0, TracingTargetStore.Targets.Count);

            TracingToolUserSettings.instance.RestoreStateForTests(json);
            Assert.That(TracingTargetStore.Targets.Select(t => t.Id.Type),
                Is.EqualTo(new[] { typeof(GhostOwner), typeof(GhostSendSystem) }), "selection order survives");
            Assert.IsTrue(TracingTargetStore.IsRequired(typeof(GhostOwner)));
            Assert.IsTrue(TracingTargetStore.IsHidden(TracingTargetId.Of(typeof(GhostOwner))));
            Assert.IsFalse(TracingTargetStore.IsHidden(TracingTargetId.Of(typeof(GhostSendSystem))));
            Assert.IsTrue(TracingTargetStore.Targets.All(t => !t.Recorded), "the recording set does not persist");
        }

        [Test]
        public void Load_KeepsOneTargetPerType_DropsUnknownTypes_AndRaisesEveryFlagOnce()
        {
            // A hand-written short key and its canonical twin, a type that no longer exists, a class that is
            // not a registered component, and a system type that became abstract.
            var ghostInstance = typeof(GhostInstance);
            TracingToolUserSettings.instance.SetTargetSelection(new List<TracingTargetSelectionEntry>
            {
                new() { assemblyQualifiedName = $"{ghostInstance.FullName}, {ghostInstance.Assembly.GetName().Name}" },
                new() { assemblyQualifiedName = ghostInstance.AssemblyQualifiedName },
                new() { assemblyQualifiedName = "Removed.By.Refactor, Nowhere" },
                new() { assemblyQualifiedName = typeof(TracingTargetStoreTests).AssemblyQualifiedName },
                new() { assemblyQualifiedName = typeof(EntityCommandBufferSystem).AssemblyQualifiedName },
            });

            TracingTargetStore.Load();

            Assert.That(m_Changes, Is.EqualTo(new[] { k_AllChanges }));
            Assert.That(TracingTargetStore.Targets.Select(t => t.Id.Type), Is.EqualTo(new[] { ghostInstance }),
                "both spellings name the same target; unresolvable, unregistered and abstract types are dropped");
            Assert.That(Persisted.Select(e => e.assemblyQualifiedName),
                Is.EqualTo(new[] { ghostInstance.AssemblyQualifiedName }),
                "the canonical, pruned selection is written back to the settings");
        }

        [Test]
        public void Recording_CaptureMergeAndPrune()
        {
            TracingTargetStore.SetSelected(new[] { typeof(GhostSendSystem), typeof(GhostOwner) }, true);
            m_Changes.Clear();
            TracingTargetStore.CaptureRecording();
            Assert.That(m_Changes, Is.EqualTo(new[] { TracingTargetChanges.Recording }));
            Assert.IsTrue(TracingTargetStore.Targets.All(t => t.Recorded));

            // Deselecting a recorded target keeps it as removed; the next capture prunes it.
            TracingTargetStore.SetSelected(typeof(GhostOwner), false);
            Assert.AreEqual(2, TracingTargetStore.Targets.Count);
            Assert.IsTrue(TracingTargetStore.TryGet(TracingTargetId.Of(typeof(GhostOwner)), out var removed));
            Assert.IsTrue(removed.IsRemoved);
            var ghostOwner = ComponentType.ReadWrite<GhostOwner>();
            Assert.IsFalse(TracingDataAccess.Config.Data.OptionalTypesToTrace.Contains(ghostOwner),
                "a removed target leaves the runtime config even while its row stays");
            TracingTargetStore.CaptureRecording();
            Assert.AreEqual(1, TracingTargetStore.Targets.Count);

            // A target selected after the capture is not recorded until a merge.
            TracingTargetStore.SetSelected(typeof(GhostOwner), true);
            TracingTargetStore.TryGet(TracingTargetId.Of(typeof(GhostOwner)), out var added);
            Assert.IsFalse(added.Recorded);
            m_Changes.Clear();
            TracingTargetStore.MergeRecording();
            Assert.That(m_Changes, Is.EqualTo(new[] { TracingTargetChanges.Recording }));
            Assert.IsTrue(added.Recorded);
        }

        [Test]
        public void PanelRows_GroupRecordedThenRemovedThenUntraced_OnlyWhileTracesAreShown()
        {
            TracingTargetStore.SetSelected(new[] { typeof(GhostSendSystem), typeof(GhostReceiveSystem) }, true);
            TracingTargetStore.CaptureRecording();
            TracingTargetStore.SetSelected(typeof(GhostReceiveSystem), false);
            TracingTargetStore.SetSelected(typeof(GhostUpdateSystem), true);
            var rows = new List<TracingTarget>();

            // Rows are described as (type, removed, untraced).
            // No traces on screen: the plain selection, no removed row, no untraced flag.
            TracingTargetStore.GetPanelRows(rows);
            Assert.That(Describe(rows), Is.EqualTo(new[]
            {
                (typeof(GhostSendSystem), false, false),
                (typeof(GhostUpdateSystem), false, false),
            }));

            // Traces shown: recorded and selected first, then removed, then selected since the capture.
            TracingDisplayState.SetShowingTraces(true);
            TracingTargetStore.GetPanelRows(rows);
            Assert.That(Describe(rows), Is.EqualTo(new[]
            {
                (typeof(GhostSendSystem), false, false),
                (typeof(GhostReceiveSystem), true, false),
                (typeof(GhostUpdateSystem), false, true),
            }));

            // Resuming records the added target; the removed one's traces are still in the data.
            TracingTargetStore.MergeRecording();
            TracingTargetStore.GetPanelRows(rows);
            Assert.That(Describe(rows), Is.EqualTo(new[]
            {
                (typeof(GhostSendSystem), false, false),
                (typeof(GhostUpdateSystem), false, false),
                (typeof(GhostReceiveSystem), true, false),
            }));
        }

        static IEnumerable<(Type, bool, bool)> Describe(List<TracingTarget> rows)
            => rows.Select(r => (r.Id.Type, r.IsRemoved, r.IsUntraced));

        [Test]
        public void Hidden_SurvivesDeselection_WhileTheTargetStaysRecorded()
        {
            var id = TracingTargetId.Of(typeof(GhostSendSystem));
            TracingTargetStore.SetSelected(id.Type, true);
            TracingTargetStore.CaptureRecording();
            TracingTargetStore.SetHidden(id, true);

            // The removed row keeps its eye state while its traces are still browsable; only selected targets persist.
            TracingTargetStore.SetSelected(id.Type, false);
            Assert.IsTrue(TracingTargetStore.IsHidden(id));
            Assert.That(Persisted, Is.Empty);

            TracingTargetStore.SetSelected(id.Type, true);
            Assert.IsTrue(Persisted[0].hidden, "reselecting persists the hidden state again");
        }

        [Test]
        public void GhostInstance_NeverLingersNorShowsUntraced()
        {
            TracingDisplayState.SetShowingTraces(true);
            TracingTargetStore.CaptureRecording();
            TracingTargetStore.SetSelected(typeof(GhostInstance), true);
            Assert.IsTrue(TracingTargetStore.TryGet(TracingTargetId.Of(typeof(GhostInstance)), out var ghostInstance));
            Assert.IsFalse(ghostInstance.IsUntraced, "always traced by the backend");
            Assert.IsFalse(TracingTargetStore.IsHiddenInViews(ghostInstance.Id));

            TracingTargetStore.MergeRecording();
            Assert.IsTrue(ghostInstance.Recorded);
            TracingTargetStore.SetSelected(typeof(GhostInstance), false);
            Assert.IsFalse(TracingTargetStore.TryGet(TracingTargetId.Of(typeof(GhostInstance)), out _),
                "never kept as a removed target");
            Assert.IsTrue(TracingTargetStore.IsHiddenInViews(TracingTargetId.Of(typeof(GhostInstance))));
        }

        [Test]
        public void PushToRuntimeConfig_RestoresADisposedConfig()
        {
            TracingTargetStore.SetSelected(typeof(GhostSendSystem), true);
            TracingDataAccess.Config.Data.Dispose();
            Assert.IsFalse(TracingDataAccess.Config.Data.Initialized);

            TracingTargetStore.PushToRuntimeConfig();

            Assert.IsTrue(TracingDataAccess.Config.Data.Initialized);
            var ghostSendSystem = TypeManager.GetSystemTypeIndex<GhostSendSystem>();
            Assert.IsTrue(TracingDataAccess.Config.Data.SystemTypesToTrace.Contains(ghostSendSystem));
        }

        [Test]
        public void Counts_AndValidSelection()
        {
            var types = new[] { typeof(GhostSendSystem), typeof(GhostOwner), typeof(GhostInstance) };
            TracingTargetStore.SetSelected(types, true);
            TracingTargetStore.CountSelected(out var systems, out var components);
            Assert.AreEqual(1, systems);
            Assert.AreEqual(2, components, "GhostInstance counts as a target while explicitly selected");
            Assert.AreEqual(3, TracingTargetStore.GetSelectedCount());
            Assert.IsTrue(TracingTargetStore.HasValidSelection());

            TracingTargetStore.SetSelected(typeof(GhostSendSystem), false);
            Assert.IsFalse(TracingTargetStore.HasValidSelection(), "a recording needs a system and a component");
        }
    }
}
