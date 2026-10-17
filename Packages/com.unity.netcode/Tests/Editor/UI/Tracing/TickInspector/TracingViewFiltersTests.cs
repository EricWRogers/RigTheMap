using System;
using NUnit.Framework;
using Unity.Entities;
using Unity.Netcode;
using Unity.Netcode.Tracing;

namespace Unity.Netcode.Editor.Tracing.UI.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TracingViewFilters"/> evaluating stored <see cref="DiffAggregate"/>s.
    /// </summary>
    class TracingViewFiltersTests
    {
        DiffInfo m_DiffInfo;
        string m_SettingsBackup;

        [SetUp]
        public void SetUp()
        {
            TypeManager.Initialize();
            m_DiffInfo = default;
            // Target visibility lives in the store; start from an empty selection and keep the user's.
            m_SettingsBackup = TracingToolUserSettings.instance.CaptureStateForTests();
            TracingToolUserSettings.instance.ResetStateToDefaultForTests();
            // GhostInstance is only shown in the views while selected.
            TracingTargetStore.SetSelected(typeof(GhostInstance), true);
        }

        [TearDown]
        public void TearDown()
        {
            m_DiffInfo.Dispose();
            TracingToolUserSettings.instance.RestoreStateForTests(m_SettingsBackup);
        }

        // Hiding goes through the store, which only knows selected (or recorded) targets.
        static void Hide(Type type, bool hidden)
        {
            TracingTargetStore.SetSelected(type, true);
            TracingTargetStore.SetHidden(TracingTargetId.Of(type), hidden);
        }

        [Test(Description = "TracingTargetId must resolve type indices to the identity the selection uses, or " +
                            "hiding a target silently no-ops.")]
        public void TargetIds_FromTypeIndices_MatchTheSelectionIdentity()
        {
            var system = TypeManager.GetSystemTypeIndex<GhostSendSystem>();
            Assert.AreEqual(TracingTargetId.Of(typeof(GhostSendSystem)), TracingTargetId.Of(system));

            var component = TypeManager.GetTypeIndex<GhostInstance>();
            Assert.AreEqual(TracingTargetId.Of(typeof(GhostInstance)), TracingTargetId.Of(component));
        }

        static DiffAggregate MakeAggregate(bool valueChanged = false)
        {
            return new DiffAggregate
            {
                System = TypeManager.GetSystemTypeIndex<SimulationSystemGroup>(),
                GhostId = 1,
                Component = TypeManager.GetTypeIndex<GhostInstance>(),
                Reasons = DiffInfo.DiffReasons.ComponentData,
                HasValueChanged = valueChanged,
            };
        }

        static TracingViewFilters MakeFilters() => new();

        [Test]
        public void HiddenTargets_HideOnlyTheirOwnAggregates()
        {
            m_DiffInfo.AddDiff(MakeAggregate());
            var filters = MakeFilters();

            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.True, "precondition: visible with nothing hidden");

            Hide(typeof(PresentationSystemGroup), true);
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.True, "hiding an unrelated system changes nothing");

            Hide(typeof(SimulationSystemGroup), true);
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.False, "hiding the causing system hides the diff");

            Hide(typeof(PresentationSystemGroup), false);
            Hide(typeof(SimulationSystemGroup), false);
            Hide(typeof(GhostInstance), true);
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.False, "hiding the causing component hides the diff");

            // DeltaTime aggregates carry no system/component, so target hiding can't touch them.
            m_DiffInfo.AddDiff(DiffInfo.DiffReasons.DeltaTime);
            Hide(typeof(SimulationSystemGroup), true);
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.True, "untargeted aggregates are unaffected by target hiding");

            // The changed-values filter can't erase them either.
            filters.ChangedValuesOnly = true;
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.True, "untargeted aggregates are unaffected by the changed-values filter");

            // Deselecting their diff tag is the one mechanism that dims them.
            filters.EnabledDiffReasons = DiffInfo.AllDiffReasons & ~DiffInfo.DiffReasons.DeltaTime;
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.False, "deselecting the tag still hides the tick-scoped diff");
        }

        [Test]
        public void HiddenTargets_CombineWithReasonAndValueChangedFilters()
        {
            // A value-changed ComponentData on SimulationSystemGroup, an unchanged MissingSystem on PresentationSystemGroup.
            m_DiffInfo.AddDiff(MakeAggregate(valueChanged: true));
            m_DiffInfo.AddDiff(new DiffAggregate
            {
                System = TypeManager.GetSystemTypeIndex<PresentationSystemGroup>(),
                GhostId = DiffAggregate.InvalidGhostId,
                Reasons = DiffInfo.DiffReasons.MissingSystem,
            });

            // Nothing visible: the changed entry is target-hidden, the other has no value change.
            var filters = new TracingViewFilters { ChangedValuesOnly = true };
            Hide(typeof(SimulationSystemGroup), true);
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.False);

            // Unhide the system: the value-changed entry passes again.
            Hide(typeof(SimulationSystemGroup), false);
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.True);

            // One entry target-hidden, the other reason-filtered: nothing visible.
            filters.ChangedValuesOnly = false;
            Hide(typeof(SimulationSystemGroup), true);
            filters.EnabledDiffReasons = DiffInfo.AllDiffReasons & ~DiffInfo.DiffReasons.MissingSystem;
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.False);
        }

        [Test(Description = "Deselected tick-scope tags act as tick filters: an aggregate stamped with one " +
                            "never passes, so the whole tick reports no diff, while aggregates from other " +
                            "ticks in the same roll-up stay visible.")]
        public void DeselectedTickScopeTags_MuteTheWholeTick()
        {
            // A ComponentData aggregate stamped PartialTick (as TickData.ProcessDiff stamps every aggregate
            // of a partial tick) plus the tick's own untargeted PartialTick aggregate.
            var stamped = MakeAggregate();
            stamped.Reasons |= DiffInfo.DiffReasons.PartialTick;
            m_DiffInfo.AddDiff(stamped);
            m_DiffInfo.AddDiff(DiffInfo.DiffReasons.PartialTick);
            var filters = MakeFilters();

            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.True, "everything selected: the tick reports its diffs");

            filters.EnabledDiffReasons = DiffInfo.AllDiffReasons & ~DiffInfo.DiffReasons.PartialTick;
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.False,
                "deselecting a tick-scope tag mutes the tick's other diffs too");

            // A frame roll-up mixing a muted tick with a clean full tick keeps reporting the full tick's diff.
            m_DiffInfo.AddDiff(MakeAggregate());
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.True,
                "aggregates from ticks without the deselected tick-scope tag stay visible");
        }

        [Test(Description = "On a targeted aggregate a tick-scope bit is only context stamped by " +
                            "TagAggregates: it vetoes when deselected but never counts as the positive " +
                            "match — that role belongs to the tick's own untargeted aggregate.")]
        public void SelectedTickScopeTags_DoNotKeepTargetedAggregatesVisible()
        {
            var stamped = MakeAggregate();
            stamped.Reasons |= DiffInfo.DiffReasons.PartialTick;
            m_DiffInfo.AddDiff(stamped);
            var filters = MakeFilters();

            filters.EnabledDiffReasons = DiffInfo.AllDiffReasons & ~DiffInfo.DiffReasons.ComponentData;
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.False,
                "deselecting the diff's own tag hides it; the stamped PartialTick context is not a match");

            m_DiffInfo.AddDiff(DiffInfo.DiffReasons.PartialTick);
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.True,
                "the tick's untargeted aggregate is what highlights for the selected tick-scope tag");
        }

        [Test]
        public void IsTickMuted_FollowsTheTickScopeSelection()
        {
            var filters = MakeFilters();
            Assert.That(filters.IsTickMuted(DiffInfo.DiffReasons.PartialTick), Is.False, "selected tags mute nothing");
            Assert.That(filters.EffectiveEnabledReasons(DiffInfo.DiffReasons.PartialTick),
                Is.EqualTo(DiffInfo.AllDiffReasons));

            filters.EnabledDiffReasons = DiffInfo.AllDiffReasons & ~DiffInfo.DiffReasons.BatchedTick;
            Assert.That(filters.IsTickMuted(DiffInfo.DiffReasons.PartialTick), Is.False);
            Assert.That(filters.IsTickMuted(DiffInfo.DiffReasons.BatchedTick | DiffInfo.DiffReasons.PartialTick),
                Is.True, "any deselected tick-scope tag carried by the tick mutes it");
            Assert.That(filters.EffectiveEnabledReasons(DiffInfo.DiffReasons.BatchedTick),
                Is.EqualTo(DiffInfo.DiffReasons.Undefined), "a muted tick highlights nothing");

            // Deselecting a highlighted tag never mutes a tick.
            filters.EnabledDiffReasons = DiffInfo.AllDiffReasons & ~DiffInfo.DiffReasons.ComponentData;
            Assert.That(filters.IsTickMuted(DiffInfo.DiffReasons.PartialTick), Is.False);
        }

        [Test(Description = "The fuzzy threshold filters ComponentData by stored amount, no reprocessing: 0 " +
                            "disables it, amounts must strictly exceed it, structural reasons are immune.")]
        public void FuzzyThreshold_FiltersComponentDataByStoredAmount()
        {
            m_DiffInfo.AddDiff(MakeAggregate(), 0.5f);
            var filters = MakeFilters();

            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.True, "threshold 0 disables the fuzzy filter");
            Assert.That(filters.ApplyFuzzyThreshold(DiffInfo.DiffReasons.ComponentData, 0.5f),
                Is.EqualTo(DiffInfo.DiffReasons.ComponentData));

            filters.FuzzyDiffThreshold = 0.4f;
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.True, "amounts above the threshold stay visible");
            Assert.That(filters.ApplyFuzzyThreshold(DiffInfo.DiffReasons.ComponentData, 0.5f),
                Is.EqualTo(DiffInfo.DiffReasons.ComponentData));

            filters.FuzzyDiffThreshold = 0.5f;
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.False, "amounts at or below the threshold are filtered");
            Assert.That(filters.ApplyFuzzyThreshold(DiffInfo.DiffReasons.ComponentData, 0.5f),
                Is.EqualTo(DiffInfo.DiffReasons.Undefined),
                "the component row's Diff tag disappears with it");
            Assert.That(filters.ApplyFuzzyThreshold(DiffInfo.DiffReasons.MissingComponent, 0f),
                Is.EqualTo(DiffInfo.DiffReasons.MissingComponent),
                "structural reasons are immune to the threshold");

            // Structural diffs carry no amount and can never be fuzzy-filtered.
            m_DiffInfo.AddDiff(new DiffAggregate
            {
                System = TypeManager.GetSystemTypeIndex<SimulationSystemGroup>(),
                GhostId = 2,
                Reasons = DiffInfo.DiffReasons.MissingGhost,
            });
            filters.FuzzyDiffThreshold = float.MaxValue;
            Assert.That(filters.HasVisibleDiff(m_DiffInfo), Is.True, "structural reasons ignore the threshold");
        }
    }
}
