using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Entities;
using Unity.Netcode;
using Unity.Netcode.Editor.Tracing;
using Unity.Netcode.Editor.Tracing.UI;
using Unity.Netcode.Editor.Tracing.UI.FilterPanel;
using Unity.Netcode.Tracing;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;

namespace Tests.Editor.UI.Tracing.FilterPanel
{
    [TestFixture]
    class TracingFilterPanelTests : UITestFixture
    {
        TracingFilterPanel m_Panel;
        bool m_PreviousEnableTracing;
        readonly List<VisualElement> m_TestRows = new();
        readonly List<FilterChangeKind> m_RaisedChanges = new();
        readonly List<(DiffInfo.DiffReasons reasons, bool changedValuesOnly, float threshold)> m_RaisedDiffValues =
            new();
        readonly List<TracingTargetChanges> m_StoreChanges = new();

        string m_SettingsBackup;

        [SetUp]
        public void SetUp()
        {
            // The panel reads TracingToolUserSettings on construction; start from first-run defaults.
            m_SettingsBackup = TracingToolUserSettings.instance.CaptureStateForTests();
            TracingToolUserSettings.instance.ResetStateToDefaultForTests();

            // Rows bound for the tests stack below the (tall) panel; keep them inside the clickable viewport.
            panelSize = new Vector2(900, 900);
            m_PreviousEnableTracing = TracingDataAccess.Config.Data.EnableTracing;
            // Start from an uninitialized config so leftovers from other fixtures never leak into the rows.
            TracingDataAccess.Config.Data.Dispose();
            TracingDataAccess.Config.Data.EnableTracing = false;
            CloseTracingToolWindows();

            m_Panel = new TracingFilterPanel();
            m_Panel.OnFiltersChanged += CaptureFilterChange;
            TracingTargetStore.Changed += m_StoreChanges.Add;
            rootVisualElement.Add(m_Panel);
            simulate.FrameUpdate();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var row in m_TestRows)
            {
                rootVisualElement.Remove(row);
            }
            m_TestRows.Clear();
            m_RaisedChanges.Clear();
            m_RaisedDiffValues.Clear();
            m_StoreChanges.Clear();

            TracingTargetStore.Changed -= m_StoreChanges.Add;
            m_Panel.OnFiltersChanged -= CaptureFilterChange;
            rootVisualElement.Remove(m_Panel);
            m_Panel = null;
            TracingDataAccess.Config.Data.Dispose();
            TracingDisplayState.SetShowingTraces(false);

            // Restore the prior global state so this fixture does not affect other suites.
            TracingDataAccess.Config.Data.EnableTracing = m_PreviousEnableTracing;
            TracingToolUserSettings.instance.RestoreStateForTests(m_SettingsBackup);
        }

        static void CloseTracingToolWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>()
                         .Where(w => w.titleContent != null && w.titleContent.text == "Prediction Tracing Tool")
                         .ToArray())
            {
                window.Close();
            }
        }

        // Snapshots the panel state per change kind, the same way the window reads it back on the event.
        void CaptureFilterChange(FilterChangeKind change)
        {
            m_RaisedChanges.Add(change);
            m_RaisedDiffValues.Add((m_Panel.SelectedDiffReasons, m_Panel.ChangedValuesOnly,
                m_Panel.FuzzyDiffThreshold));
        }

        [Test]
        public void Build_HasHeaderAndBothSections()
        {
            var title = m_Panel.Q<Label>(className: TracingFilterPanelUssClasses.HeaderLabel);
            Assert.IsNotNull(title, "The panel title must exist");
            Assert.That(title.text, Is.EqualTo(TracingFilterPanel.k_TextTitle));
            Assert.That(m_Panel.TagsSection.text, Is.EqualTo(TracingFilterPanel.k_TextDiffTags));
            Assert.That(m_Panel.TargetsSection.text, Is.EqualTo(TracingFilterPanel.k_TextTracingTarget));
            Assert.IsNotNull(m_Panel.Q<Slider>(), "The threshold slider must exist");
            Assert.IsNotNull(m_Panel.Q<FloatField>(), "The threshold field must exist");
            Assert.IsNotNull(GetChangedValuesToggle(), "The changed-values-only toggle must exist");
        }

        [Test]
        public void Build_ShowsTheDefaultSelectionInTheSectionCount()
        {
            // Tick-level tags start disabled, so the first-run count is already short of the total.
            var total = DiffReasonCatalog.All.Length;
            Assert.That(m_Panel.TagsCountLabel.text, Is.EqualTo($"{total - 3}/{total}"));
        }

        [Test]
        public void Default_FiltersOnlyTheTickLevelDiffs()
        {
            Assert.AreEqual(0, m_Panel.TargetListView.itemsSource.Count);
            Assert.AreEqual(DiffInfo.AllDiffReasons & ~DiffInfo.TickScopeReasons, m_Panel.SelectedDiffReasons);
            Assert.IsFalse(m_Panel.ChangedValuesOnly);
            Assert.AreEqual(0f, m_Panel.FuzzyDiffThreshold);
        }

        [Test]
        public void SectionEyes_SitInsideTheSectionHeaders()
        {
            Assert.AreSame(m_Panel.TagsSection, m_Panel.HighlightedTagsEye.GetFirstAncestorOfType<Foldout>());
            Assert.AreSame(m_Panel.TagsSection, m_Panel.TickTagsEye.GetFirstAncestorOfType<Foldout>());
            Assert.AreSame(m_Panel.TargetsSection, m_Panel.TargetsEye.GetFirstAncestorOfType<Foldout>());
        }

        [Test]
        public void SectionEye_Click_DoesNotCollapseTheSection()
        {
            simulate.Click(m_Panel.HighlightedTagsEye);
            Assert.IsTrue(m_Panel.TagsSection.value, "The eye swallows its pointer events; the foldout must stay open");
        }

        [Test]
        public void TagGroups_ShowTheirHeadersWithTooltips()
        {
            var headerLabels = m_Panel.TagsSection
                .Query<Label>(className: TracingFilterPanelUssClasses.ListHeaderLabel).ToList();
            Assert.That(headerLabels.Select(l => l.text), Is.EqualTo(new[]
            {
                TracingFilterPanel.k_TextHighlightedDiffs,
                TracingFilterPanel.k_TextTickFiltering,
            }));
            Assert.AreEqual(TracingFilterPanel.k_TooltipHighlightedDiffs, headerLabels[0].parent.tooltip,
                "the group headers explain the two tag behaviors");
            Assert.AreEqual(TracingFilterPanel.k_TooltipTickFiltering, headerLabels[1].parent.tooltip);
        }

        [Test]
        public void TagToggle_RemovesTheReason_AndRaisesTheReducedReasons()
        {
            var entry = DiffReasonCatalog.All[0];
            var toggle = m_Panel.GetDiffTagToggle(0);
            simulate.FrameUpdate();
            Assert.IsTrue(toggle.ClassListContains(TracingFilterPanelUssClasses.IconVisible));

            simulate.Click(toggle);

            Assert.AreEqual(0, (uint)(m_Panel.SelectedDiffReasons & entry.Reason));
            Assert.AreEqual(1, m_RaisedDiffValues.Count);
            Assert.AreEqual(0, (uint)(m_RaisedDiffValues[0].reasons & entry.Reason),
                "The views must receive the reduced reasons");
            Assert.IsTrue(toggle.ClassListContains(TracingFilterPanelUssClasses.IconHidden));
            Assert.IsFalse(toggle.ClassListContains(TracingFilterPanelUssClasses.IconVisible));
        }

        [Test]
        public void TagToggle_SecondClick_RestoresTheReason()
        {
            var entry = DiffReasonCatalog.All[0];
            var toggle = m_Panel.GetDiffTagToggle(0);
            simulate.FrameUpdate();

            simulate.Click(toggle);
            simulate.Click(toggle);

            Assert.AreEqual(DiffInfo.AllDiffReasons & ~DiffInfo.TickScopeReasons, m_Panel.SelectedDiffReasons);
            Assert.AreNotEqual(0, (uint)(m_RaisedDiffValues[^1].reasons & entry.Reason));
            Assert.IsTrue(toggle.ClassListContains(TracingFilterPanelUssClasses.IconVisible));
            Assert.IsFalse(toggle.ClassListContains(TracingFilterPanelUssClasses.IconHidden));
        }

        [Test]
        public void TagChip_TintsRedOnlyForReasonsPresentInTheTraces()
        {
            var chip = m_Panel.GetDiffTagChip(0);
            simulate.FrameUpdate();

            Assert.IsFalse(chip.ClassListContains(TracingFilterPanelUssClasses.DiffTagChipPresent),
                "Without traces every chip starts gray");

            m_Panel.SetPresentDiffReasons(DiffReasonCatalog.All[0].Reason);
            Assert.IsTrue(chip.ClassListContains(TracingFilterPanelUssClasses.DiffTagChipPresent),
                "A reason observed in the traces tints its chip red");
            Assert.IsFalse(m_Panel.GetDiffTagChip(1).ClassListContains(TracingFilterPanelUssClasses.DiffTagChipPresent),
                "Reasons the traces never produced stay gray");

            m_Panel.SetPresentDiffReasons(default);
            Assert.IsFalse(chip.ClassListContains(TracingFilterPanelUssClasses.DiffTagChipPresent),
                "Clearing the traces grays the chips again");
        }

        [Test]
        public void TagChip_TintIsIndependentOfTheEyeSelection()
        {
            var chip = m_Panel.GetDiffTagChip(0);
            m_Panel.SetPresentDiffReasons(DiffReasonCatalog.All[0].Reason);
            simulate.FrameUpdate();

            simulate.Click(m_Panel.GetDiffTagToggle(0));

            Assert.IsTrue(chip.ClassListContains(TracingFilterPanelUssClasses.DiffTagChipPresent),
                "Deselecting the tag hides its diffs but the chip still reports the data");
        }

        [Test]
        public void TargetsGear_SitsInTheTargetsSectionHeader()
        {
            Assert.IsNotNull(m_Panel.TargetsGear, "The Tracing Targets section must expose its gear button");
            Assert.AreSame(m_Panel.TargetsSection, m_Panel.TargetsGear.GetFirstAncestorOfType<Foldout>());
            Assert.AreEqual(TracingFilterPanel.k_TooltipChangeTargets, m_Panel.TargetsGear.tooltip);
        }

        [Test]
        public void TagToggle_UpdatesTheSectionCount()
        {
            simulate.FrameUpdate();

            simulate.Click(m_Panel.GetDiffTagToggle(0));

            var total = DiffReasonCatalog.All.Length;
            Assert.That(m_Panel.TagsCountLabel.text, Is.EqualTo($"{total - 4}/{total}"),
                "one highlighted tag toggled off on top of the three default-disabled tick tags");
        }

        [Test]
        public void HighlightedTagsEye_TogglesOnlyTheHighlightedGroup()
        {
            simulate.FrameUpdate();

            simulate.Click(m_Panel.HighlightedTagsEye);
            Assert.AreEqual(0, (uint)(m_Panel.SelectedDiffReasons & DiffReasonCatalog.HighlightedMask));
            Assert.AreEqual(0, (uint)(m_Panel.SelectedDiffReasons & DiffReasonCatalog.TickScopeMask),
                "the tick filtering group is independent of the highlighted group: it stays at its default (off)");
            Assert.IsTrue(m_Panel.GetDiffTagToggle(0).ClassListContains(TracingFilterPanelUssClasses.IconHidden));

            simulate.Click(m_Panel.HighlightedTagsEye);
            Assert.AreEqual(DiffReasonCatalog.HighlightedMask,
                m_Panel.SelectedDiffReasons & DiffReasonCatalog.SelectableMask);
            Assert.IsTrue(m_Panel.GetDiffTagToggle(0).ClassListContains(TracingFilterPanelUssClasses.IconVisible));
            Assert.AreEqual(2, m_RaisedDiffValues.Count);
        }

        [Test]
        public void TickTagsEye_TogglesOnlyTheTickFilteringGroup()
        {
            simulate.FrameUpdate();

            // Tick tags start disabled, so the first click on their eye turns the whole group on.
            simulate.Click(m_Panel.TickTagsEye);
            Assert.AreEqual(DiffReasonCatalog.TickScopeMask,
                m_Panel.SelectedDiffReasons & DiffReasonCatalog.TickScopeMask);
            Assert.AreEqual(DiffReasonCatalog.HighlightedMask,
                m_Panel.SelectedDiffReasons & DiffReasonCatalog.HighlightedMask,
                "the highlighted group is independent of the tick filtering group");

            // The section count still spans both groups.
            var total = DiffReasonCatalog.All.Length;
            Assert.That(m_Panel.TagsCountLabel.text, Is.EqualTo($"{total}/{total}"));

            simulate.Click(m_Panel.TickTagsEye);
            Assert.AreEqual(0, (uint)(m_Panel.SelectedDiffReasons & DiffReasonCatalog.TickScopeMask));
            Assert.That(m_Panel.TagsCountLabel.text, Is.EqualTo($"{total - 3}/{total}"));
        }

        [Test]
        public void ChangedValuesOnlyToggle_RaisesTheDiffFilters()
        {
            GetChangedValuesToggle().value = true;

            Assert.IsTrue(m_Panel.ChangedValuesOnly);
            Assert.AreEqual(1, m_RaisedDiffValues.Count);
            Assert.IsTrue(m_RaisedDiffValues[0].changedValuesOnly);
        }

        [Test]
        public void FilterGestures_ReportTheirChangeKind()
        {
            m_Panel.Q<FloatField>().value = 0.5f;
            GetChangedValuesToggle().value = true;
            simulate.Click(m_Panel.GetDiffTagToggle(0));

            // The window debounces the tick inspector only for FuzzyDiffThreshold, so each gesture
            // must report the filter it changed.
            Assert.That(m_RaisedChanges, Is.EqualTo(new[]
            {
                FilterChangeKind.FuzzyDiffThreshold,
                FilterChangeKind.ChangedValuesOnly,
                FilterChangeKind.DiffReasons,
            }));
        }

        [Test]
        public void ThresholdField_SetsTheThreshold_AndSyncsTheSlider()
        {
            m_Panel.Q<FloatField>().value = 0.5f;

            Assert.AreEqual(0.5f, m_Panel.FuzzyDiffThreshold);
            Assert.AreEqual(1, m_RaisedDiffValues.Count);
            Assert.AreEqual(0.5f, m_RaisedDiffValues[0].threshold);
            Assert.AreEqual(FuzzyThresholdSlider.ThresholdToPosition(0.5f), m_Panel.Q<Slider>().value, 1e-4f);
        }

        [Test]
        public void ThresholdField_NegativeValues_ClampToZero()
        {
            m_Panel.Q<FloatField>().value = -3f;

            Assert.AreEqual(0f, m_Panel.FuzzyDiffThreshold);
            Assert.AreEqual(0f, m_Panel.Q<FloatField>().value);
        }

        [Test]
        public void ThresholdSlider_DrivesTheFieldThroughTheLogMapping()
        {
            m_Panel.Q<Slider>().value = 1f;

            Assert.AreEqual(FuzzyThresholdSlider.k_Max, m_Panel.FuzzyDiffThreshold, 1e-3f);
            Assert.AreEqual(m_Panel.FuzzyDiffThreshold, m_Panel.Q<FloatField>().value);
        }

        [Test]
        public void Rows_FollowTheStoreAndTheDisplayState_OnTheNextFrame()
        {
            Assert.AreEqual(0, m_Panel.TargetListView.itemsSource.Count);

            // No explicit refresh: the panel listens to the store and rebuilds its rows on the next frame.
            TracingTargetStore.SetSelected(typeof(GhostSendSystem), true);
            simulate.FrameUpdate();
            Assert.AreEqual(1, m_Panel.TargetListView.itemsSource.Count);

            TracingTargetStore.CaptureRecording();
            TracingTargetStore.SetSelected(typeof(GhostReceiveSystem), true);
            TracingDisplayState.SetShowingTraces(true);
            simulate.FrameUpdate();
            Assert.AreEqual(2, m_Panel.TargetListView.itemsSource.Count);
            Assert.IsTrue(((TracingTarget)m_Panel.TargetListView.itemsSource[1]).IsUntraced,
                "showing traces regroups the rows: the target selected after the capture moves to the bottom");
        }

        [Test]
        public void ToggleTargetRow_HidesThenShowsTheTarget_RaisingOncePerClick()
        {
            SelectTargets(typeof(GhostSendSystem));
            var toggle = BindTargetRow(0).Q<Button>("toggle");
            simulate.FrameUpdate();
            Assert.IsTrue(toggle.ClassListContains(TracingFilterPanelUssClasses.IconVisible));
            m_StoreChanges.Clear();

            simulate.Click(toggle);
            Assert.IsTrue(TracingTargetStore.IsHidden(Id(typeof(GhostSendSystem))));
            Assert.That(m_StoreChanges, Is.EqualTo(new[] { TracingTargetChanges.Visibility }));
            Assert.IsTrue(toggle.ClassListContains(TracingFilterPanelUssClasses.IconHidden));
            Assert.IsFalse(toggle.ClassListContains(TracingFilterPanelUssClasses.IconVisible));

            simulate.Click(toggle);
            Assert.IsFalse(TracingTargetStore.IsHidden(Id(typeof(GhostSendSystem))));
            Assert.That(m_StoreChanges,
                Is.EqualTo(new[] { TracingTargetChanges.Visibility, TracingTargetChanges.Visibility }));
            Assert.IsTrue(toggle.ClassListContains(TracingFilterPanelUssClasses.IconVisible));
            Assert.IsFalse(toggle.ClassListContains(TracingFilterPanelUssClasses.IconHidden));
        }

        [Test]
        public void ToggleTargetRows_HideEachTargetOnItsOwn_AndUpdateTheSectionCount()
        {
            SelectTargets(typeof(GhostSendSystem), typeof(GhostOwner), typeof(PredictedGhost));
            var systemToggle = BindTargetRow(0).Q<Button>("toggle");
            var componentToggle = BindTargetRow(1).Q<Button>("toggle");
            simulate.FrameUpdate();

            simulate.Click(systemToggle);
            Assert.That(m_Panel.TargetsCountLabel.text, Is.EqualTo("2/3"));
            simulate.Click(componentToggle);
            Assert.That(m_Panel.TargetsCountLabel.text, Is.EqualTo("1/3"));
            Assert.IsTrue(TracingTargetStore.IsHidden(Id(typeof(GhostSendSystem))));
            Assert.IsTrue(TracingTargetStore.IsHidden(Id(typeof(GhostOwner))));
            Assert.IsFalse(TracingTargetStore.IsHidden(Id(typeof(PredictedGhost))), "each eye hides its own target only");

            simulate.Click(systemToggle);
            Assert.That(m_Panel.TargetsCountLabel.text, Is.EqualTo("2/3"));
            Assert.IsFalse(TracingTargetStore.IsHidden(Id(typeof(GhostSendSystem))));
            Assert.IsTrue(TracingTargetStore.IsHidden(Id(typeof(GhostOwner))), "showing one target leaves the others hidden");
        }

        [Test]
        public void BindTargetRow_SetsKindIconNameAndEvenClass()
        {
            SelectTargets(typeof(GhostSendSystem), typeof(GhostOwner));

            var systemRow = BindTargetRow(0);
            Assert.IsTrue(systemRow.Q<VisualElement>("tracing-type")
                .ClassListContains(TracingFilterPanelUssClasses.IconSystem));
            Assert.AreEqual("GhostSendSystem", systemRow.Q<Label>("name").text,
                "Only the type name is displayed");
            Assert.AreEqual(typeof(GhostSendSystem).FullName, systemRow.Q<Label>("name").tooltip);
            Assert.IsTrue(systemRow.ClassListContains(TracingFilterPanelUssClasses.RowEven));

            var componentRow = BindTargetRow(1);
            Assert.IsTrue(componentRow.Q<VisualElement>("tracing-type")
                .ClassListContains(TracingFilterPanelUssClasses.IconComponent));
            Assert.IsFalse(componentRow.ClassListContains(TracingFilterPanelUssClasses.RowEven));
        }

        [Test]
        public void BindTargetRow_NestedType_ShowsTheSameSimpleNameAsThePicker()
        {
            SelectTargets(typeof(NestedRowComponent));

            var row = BindTargetRow(0);
            Assert.AreEqual(nameof(NestedRowComponent), row.Q<Label>("name").text);
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void BindTargetRow_ManagedComponent_ShowsTheInfoBadgeInsteadOfAnEye()
        {
            SelectTargets(typeof(ManagedRowComponent));

            var row = BindTargetRow(0);
            var toggle = row.Q<Button>("toggle");
            Assert.IsTrue(toggle.ClassListContains(TracingFilterPanelUssClasses.IconInfo));
            Assert.IsFalse(toggle.ClassListContains(TracingFilterPanelUssClasses.IconVisible));
            Assert.AreEqual(PickingMode.Ignore, toggle.pickingMode);
            Assert.AreEqual(TracingFilterPanel.k_TooltipManagedComponent, row.tooltip);
        }

        [Test]
        public void TargetsEye_HidesEverythingButManagedComponents_ThenShowsAllAgain()
        {
            SelectTargets(typeof(GhostSendSystem), typeof(GhostOwner), typeof(ManagedRowComponent));
            simulate.FrameUpdate();
            m_StoreChanges.Clear();

            simulate.Click(m_Panel.TargetsEye);
            Assert.IsTrue(TracingTargetStore.IsHidden(Id(typeof(GhostSendSystem))));
            Assert.IsTrue(TracingTargetStore.IsHidden(Id(typeof(GhostOwner))));
            Assert.IsFalse(TracingTargetStore.IsHidden(Id(typeof(ManagedRowComponent))),
                "Managed component values are never captured: nothing to hide");
            Assert.IsTrue(m_Panel.TargetsEye.ClassListContains(TracingFilterPanelUssClasses.IconHidden));

            simulate.Click(m_Panel.TargetsEye);
            Assert.IsFalse(TracingTargetStore.IsHidden(Id(typeof(GhostSendSystem))));
            Assert.IsFalse(TracingTargetStore.IsHidden(Id(typeof(GhostOwner))));
            Assert.IsTrue(m_Panel.TargetsEye.ClassListContains(TracingFilterPanelUssClasses.IconVisible));
            Assert.AreEqual(2, m_StoreChanges.Count(change => change == TracingTargetChanges.Visibility),
                "each eye click notifies once for the whole batch");
        }
#endif

        [Test]
        public void UnbindTargetRow_ClearsTheToggleAction()
        {
            SelectTargets(typeof(GhostSendSystem));
            var row = BindTargetRow(0);
            var toggle = row.Q<Button>("toggle");
            Assert.IsNotNull(toggle.userData, "Toggle should have its click action set after bind");

            m_Panel.TargetListView.unbindItem(row, 0);

            Assert.IsNull(toggle.userData, "Toggle action should be cleared after unbind");
        }

        [Test]
        public void FilterState_SurvivesDetachAndReattach()
        {
            SelectTargets(typeof(GhostSendSystem));
            var toggle = BindTargetRow(0).Q<Button>("toggle");
            simulate.FrameUpdate();
            simulate.Click(toggle);
            simulate.Click(m_Panel.GetDiffTagToggle(0));

            var parent = m_Panel.parent;
            m_Panel.RemoveFromHierarchy();
            simulate.FrameUpdate();
            parent.Add(m_Panel);
            simulate.FrameUpdate();

            Assert.IsTrue(((TracingTarget)m_Panel.TargetListView.itemsSource[0]).Hidden,
                "Hidden targets must survive detach/reattach");
            Assert.AreEqual(0, (uint)(m_Panel.SelectedDiffReasons & DiffReasonCatalog.All[0].Reason),
                "Deselected diff tags must survive detach/reattach");
        }

        [Test]
        public void UntracedItem_ShowsInfoIconAndHidesEye()
        {
            TracingDisplayState.SetShowingTraces(true);
            TracingTargetStore.CaptureRecording();
            SelectTargets(typeof(GhostSendSystem));

            var row = BindTargetRow(0);
            var toggle = row.Q<Button>("toggle");
            var info = row.Q<VisualElement>("info");
            simulate.FrameUpdate();

            Assert.IsFalse(info.ClassListContains(TracingFilterPanelUssClasses.RowInfoHidden));
            // Invisible, not removed from layout: the row keeps its height and the eye column its width.
            Assert.IsTrue(toggle.ClassListContains(TracingFilterPanelUssClasses.RowToggleInvisible));
            Assert.AreEqual(TracingFilterPanel.k_TooltipUntracedTarget, info.tooltip);
            Assert.IsNull(toggle.userData, "no visibility action is bound for a target with no data");
        }

        [Test]
        public void RemovedItem_KeepsEyeAndShowsInfo()
        {
            TracingDisplayState.SetShowingTraces(true);
            TracingTargetStore.SetSelected(typeof(GhostSendSystem), true);
            TracingTargetStore.CaptureRecording();
            TracingTargetStore.SetSelected(typeof(GhostSendSystem), false);
            m_Panel.RefreshTargets();

            var row = BindTargetRow(0);
            var toggle = row.Q<Button>("toggle");
            var info = row.Q<VisualElement>("info");
            simulate.FrameUpdate();

            Assert.IsFalse(info.ClassListContains(TracingFilterPanelUssClasses.RowInfoHidden));
            Assert.AreEqual(TracingFilterPanel.k_TooltipRemovedTarget, info.tooltip);
            Assert.IsFalse(toggle.ClassListContains(TracingFilterPanelUssClasses.RowToggleInvisible));
            Assert.IsTrue(toggle.ClassListContains(TracingFilterPanelUssClasses.IconVisible));

            // The eye still works: its recorded data can be shown and hidden.
            simulate.Click(toggle);
            Assert.IsTrue(TracingTargetStore.IsHidden(Id(typeof(GhostSendSystem))));
        }

        [Test]
        public void RecycledRow_TracedAfterUntraced_RestoresEye()
        {
            TracingDisplayState.SetShowingTraces(true);
            TracingTargetStore.SetSelected(typeof(GhostReceiveSystem), true);
            TracingTargetStore.CaptureRecording();
            // Rows: the traced GhostReceiveSystem first, the untraced GhostSendSystem last.
            SelectTargets(typeof(GhostSendSystem));

            // Bind the untraced item, then rebind the same (recycled) row to the traced one.
            var row = BindTargetRow(1);
            var toggle = row.Q<Button>("toggle");
            var info = row.Q<VisualElement>("info");
            m_Panel.TargetListView.bindItem(row, 0);
            simulate.FrameUpdate();

            Assert.IsTrue(info.ClassListContains(TracingFilterPanelUssClasses.RowInfoHidden));
            Assert.IsFalse(toggle.ClassListContains(TracingFilterPanelUssClasses.RowToggleInvisible));
            Assert.IsTrue(toggle.ClassListContains(TracingFilterPanelUssClasses.IconVisible));
            Assert.AreEqual(TracingFilterPanel.k_TooltipToggleVisibility, toggle.tooltip);
        }

        [Test]
        public void TargetsEye_SkipsUntracedTargets()
        {
            TracingDisplayState.SetShowingTraces(true);
            TracingTargetStore.SetSelected(typeof(GhostReceiveSystem), true);
            TracingTargetStore.CaptureRecording();
            SelectTargets(typeof(GhostSendSystem));
            simulate.FrameUpdate();

            simulate.Click(m_Panel.TargetsEye);

            Assert.IsTrue(TracingTargetStore.IsHidden(Id(typeof(GhostReceiveSystem))));
            Assert.IsFalse(TracingTargetStore.IsHidden(Id(typeof(GhostSendSystem))),
                "an untraced target has no data to hide");
        }

        [Test]
        public void SliderMapping_LeftmostPositionIsOff()
        {
            Assert.AreEqual(0f, FuzzyThresholdSlider.PositionToThreshold(0f));
            Assert.AreEqual(0f, FuzzyThresholdSlider.PositionToThreshold(-1f));
        }

        [Test]
        public void SliderMapping_TrackIsLogScaleFromMinToMax()
        {
            Assert.AreEqual(FuzzyThresholdSlider.k_Max, FuzzyThresholdSlider.PositionToThreshold(1f), 1e-3f);
            // The log midpoint of 0.01..10 is 10^-0.5.
            Assert.AreEqual(0.3162f, FuzzyThresholdSlider.PositionToThreshold(0.5f), 1e-4f);
        }

        [Test]
        public void SliderMapping_ThresholdToPositionInvertsTheTrack()
        {
            Assert.AreEqual(0f, FuzzyThresholdSlider.ThresholdToPosition(0f));
            Assert.AreEqual(0f, FuzzyThresholdSlider.ThresholdToPosition(FuzzyThresholdSlider.k_Min), 1e-4f);
            Assert.AreEqual(1f, FuzzyThresholdSlider.ThresholdToPosition(FuzzyThresholdSlider.k_Max), 1e-4f);
            Assert.AreEqual(0.5f, FuzzyThresholdSlider.ThresholdToPosition(0.31623f), 1e-3f);
        }

        [Test]
        public void SliderMapping_FieldValuesOutsideTheTrackClampToItsEnds()
        {
            // The field accepts any value >= 0; the slider just parks at the nearest end.
            Assert.AreEqual(0f, FuzzyThresholdSlider.ThresholdToPosition(0.0001f));
            Assert.AreEqual(1f, FuzzyThresholdSlider.ThresholdToPosition(100f));
        }

        Toggle GetChangedValuesToggle()
            => m_Panel.Q<Toggle>(className: TracingFilterPanelUssClasses.ChangedValuesToggle);

        // Selects the types through the store and rebuilds the rows so BindTargetRow can bind them by index.
        void SelectTargets(params Type[] types)
        {
            TracingTargetStore.SetSelected(types, true);
            m_Panel.RefreshTargets();
        }

        static TracingTargetId Id(Type type) => TracingTargetId.Of(type);

        // Nested type: rows show the type name only, never the declaring type from FullName
        struct NestedRowComponent : IComponentData
        {
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        // Class component: listed, never value-captured, so its row carries an info badge instead of an eye.
        class ManagedRowComponent : IComponentData
        {
        }
#endif

        // Rows are bound through the panel's own ListView bindings, so the tests exercise the real event
        // chain (row eye -> panel state -> window) against the panel's persistent state.
        VisualElement BindTargetRow(int index)
        {
            var row = m_Panel.MakeItemRow();
            rootVisualElement.Add(row);
            m_TestRows.Add(row);
            simulate.FrameUpdate();
            m_Panel.TargetListView.bindItem(row, index);
            return row;
        }
    }
}
