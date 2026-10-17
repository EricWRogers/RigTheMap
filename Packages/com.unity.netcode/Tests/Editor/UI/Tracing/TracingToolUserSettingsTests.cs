using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode.Tracing;
using UnityEngine;

namespace Unity.Netcode.Editor.Tracing.UI.Tests
{
    [TestFixture]
    class TracingToolUserSettingsTests
    {
        string m_SettingsBackup;

        static TracingToolUserSettings Settings => TracingToolUserSettings.instance;

        [SetUp]
        public void SetUp()
        {
            m_SettingsBackup = Settings.CaptureStateForTests();
            Settings.ResetStateToDefaultForTests();
        }

        [TearDown]
        public void TearDown()
        {
            Settings.RestoreStateForTests(m_SettingsBackup);
        }

        [Test]
        public void CaptureAndRestore_RoundTripsEveryPersistedValue()
        {
            Settings.SetTargetSelection(new List<TracingTargetSelectionEntry>
            {
                new() { assemblyQualifiedName = typeof(GhostInstance).AssemblyQualifiedName, hidden = true },
            });
            Settings.SelectedDiffReasons = DiffInfo.AllDiffReasons & ~DiffInfo.DiffReasons.PartialTick;
            Settings.FuzzyDiffThreshold = 0.25f;
            Settings.ChangedValuesOnly = true;
            Settings.FilterPanelVisibility = FilterPanelVisibility.UserClosed;
            Settings.ReplayFrequency = ReplayRenderingFrequency.OncePerSystem;
            Settings.ReplaySpeed = 2f;
            Settings.TargetSelectionTab = 2;
            Settings.SceneVisEnabled = false;
            Settings.SceneVisShowAuthority = false;
            Settings.SceneVisPredictedColor = Color.blue;

            var json = Settings.CaptureStateForTests();
            Settings.ResetStateToDefaultForTests();
            Settings.RestoreStateForTests(json);

            Assert.That(Settings.TargetSelection.Select(e => e.assemblyQualifiedName),
                Is.EqualTo(new[] { typeof(GhostInstance).AssemblyQualifiedName }));
            Assert.That(Settings.TargetSelection[0].hidden, Is.True);
            Assert.That(Settings.SelectedDiffReasons,
                Is.EqualTo(DiffInfo.AllDiffReasons & ~DiffInfo.DiffReasons.PartialTick));
            Assert.That(Settings.FuzzyDiffThreshold, Is.EqualTo(0.25f));
            Assert.That(Settings.ChangedValuesOnly, Is.True);
            Assert.That(Settings.FilterPanelVisibility, Is.EqualTo(FilterPanelVisibility.UserClosed));
            Assert.That(Settings.ReplayFrequency, Is.EqualTo(ReplayRenderingFrequency.OncePerSystem));
            Assert.That(Settings.ReplaySpeed, Is.EqualTo(2f));
            Assert.That(Settings.TargetSelectionTab, Is.EqualTo(2));
            Assert.That(Settings.SceneVisEnabled, Is.False);
            Assert.That(Settings.SceneVisShowAuthority, Is.False);
            Assert.That(Settings.SceneVisPredictedColor, Is.EqualTo(Color.blue));
        }

        [Test]
        public void SelectedDiffReasons_UnknownPersistedTagName_IsIgnored()
        {
            Settings.RestoreStateForTests("{\"m_DisabledDiffTags\":[\"RemovedByRefactor\",\"PartialTick\"]}");

            Assert.That(Settings.SelectedDiffReasons,
                Is.EqualTo(DiffInfo.AllDiffReasons & ~DiffInfo.DiffReasons.PartialTick),
                "an unknown tag name must deselect nothing; a known one must stay deselected");
        }

        [Test]
        public void SelectedDiffReasons_NewTagsDefaultToSelected()
        {
            Settings.SelectedDiffReasons = DiffInfo.AllDiffReasons & ~DiffInfo.DiffReasons.DeltaTime;

            // Only the deselected tag is persisted, so any reason added later is selected by default.
            Assert.That((Settings.SelectedDiffReasons & DiffInfo.DiffReasons.ComponentData), Is.Not.EqualTo(DiffInfo.DiffReasons.Undefined));
            Assert.That((Settings.SelectedDiffReasons & DiffInfo.DiffReasons.DeltaTime), Is.EqualTo(DiffInfo.DiffReasons.Undefined));
        }

        [Test]
        public void ReplayFrequency_OutOfRangePersistedValue_FallsBackToDefault()
        {
            Settings.RestoreStateForTests("{\"m_ReplayFrequency\":99}");
            Assert.That(Settings.ReplayFrequency, Is.EqualTo(ReplayRenderingFrequency.OncePerFrame));
        }

        [Test]
        public void ReplaySpeed_OutOfRangePersistedValue_IsClamped()
        {
            Settings.RestoreStateForTests("{\"m_ReplaySpeed\":100}");
            Assert.That(Settings.ReplaySpeed, Is.EqualTo(ReplaySpeed.Max));

            Settings.RestoreStateForTests("{\"m_ReplaySpeed\":0}");
            Assert.That(Settings.ReplaySpeed, Is.EqualTo(ReplaySpeed.Min));
        }

        [Test]
        public void FuzzyDiffThreshold_NegativePersistedValue_ReadsAsOff()
        {
            Settings.RestoreStateForTests("{\"m_FuzzyDiffThreshold\":-1}");
            Assert.That(Settings.FuzzyDiffThreshold, Is.EqualTo(0f));
        }

        [Test]
        public void FilterPanelVisibility_OutOfRangePersistedValue_FallsBackToAutoOpen()
        {
            Settings.RestoreStateForTests("{\"m_FilterPanelVisibility\":7}");
            Assert.That(Settings.FilterPanelVisibility, Is.EqualTo(FilterPanelVisibility.AutoOpen));
        }

        [Test]
        public void ColumnFilters_Defaults_HideNotPredictedAndManaged()
        {
            var filters = new TracingColumnFilters();
            filters.LoadFromSettings();

            Assert.That(filters.IsColumnFilterEnabled(TracingColumnFilters.k_ColumnTracingPredicted), Is.True);
            Assert.That(filters.IsColumnFilterValueHidden(TracingColumnFilters.k_ColumnTracingPredicted,
                TracingColumnFilters.k_PredictedFilterValueNotPredicted), Is.True);
            Assert.That(filters.IsColumnFilterEnabled(TracingColumnFilters.k_ColumnTracingUnmanaged), Is.True);
        }

        [Test]
        public void ColumnFilters_DisabledFilterWithHiddenValues_RoundTrips()
        {
            var filters = new TracingColumnFilters();
            filters.LoadFromSettings();
            filters.SetColumnFilterValueHidden(TracingColumnFilters.k_ColumnTracingWorld, "TestWorld", true);
            filters.SetColumnFilterEnabled(TracingColumnFilters.k_ColumnTracingWorld, false);

            var restored = new TracingColumnFilters();
            restored.LoadFromSettings();

            Assert.That(restored.IsColumnFilterEnabled(TracingColumnFilters.k_ColumnTracingWorld), Is.False,
                "the disabled state must survive persistence");
            Assert.That(restored.IsColumnFilterValueHidden(TracingColumnFilters.k_ColumnTracingWorld, "TestWorld"),
                Is.True, "hidden values must survive persistence even while the filter is disabled");
        }

        [Test]
        public void ColumnFilters_ClearedFilters_RoundTripAsCleared()
        {
            var filters = new TracingColumnFilters();
            filters.LoadFromSettings();
            filters.SetColumnFilterValuesHidden(TracingColumnFilters.k_ColumnTracingPredicted,
                new[] { TracingColumnFilters.k_PredictedFilterValueNotPredicted }, false);
            filters.SetColumnFilterValuesHidden(TracingColumnFilters.k_ColumnTracingUnmanaged,
                new[] { TracingColumnFilters.k_UnmanagedFilterValueManaged }, false);

            var restored = new TracingColumnFilters();
            restored.LoadFromSettings();

            Assert.That(restored.IsColumnFilterEnabled(TracingColumnFilters.k_ColumnTracingPredicted), Is.False,
                "a user-cleared filter must not come back as the code default");
            Assert.That(restored.HasHiddenValues(TracingColumnFilters.k_ColumnTracingPredicted), Is.False);
        }

        [Test]
        public void ColumnFilters_UnknownPersistedColumn_IsDropped()
        {
            Settings.RestoreStateForTests("{\"m_ColumnFilters\":["
                + "{\"columnName\":\"tracing-column-removed\",\"enabled\":true,\"hiddenValues\":[\"x\"]},"
                + $"{{\"columnName\":\"{TracingColumnFilters.k_ColumnTracingWorld}\",\"enabled\":true,\"hiddenValues\":[\"W1\"]}}]}}");

            var filters = new TracingColumnFilters();
            filters.LoadFromSettings();

            Assert.That(filters.IsColumnFilterEnabled("tracing-column-removed"), Is.False);
            Assert.That(filters.IsColumnFilterValueHidden("tracing-column-removed", "x"), Is.False);
            Assert.That(filters.IsColumnFilterValueHidden(TracingColumnFilters.k_ColumnTracingWorld, "W1"), Is.True);
        }

        [Test]
        public void SceneVisColors_EditPersists_AndResetRestoresTheDefault()
        {
            var defaultColor = SceneVisualizationView.PredictedColor;

            SceneVisualizationView.PredictedColor = Color.magenta;

            Assert.That(Settings.SceneVisPredictedColor, Is.EqualTo(Color.magenta));
            Assert.That(SceneVisualizationView.PredictedColor, Is.EqualTo(Color.magenta));

            Settings.ResetStateToDefaultForTests();
            Assert.That(SceneVisualizationView.PredictedColor, Is.EqualTo(defaultColor));
        }

        [Test]
        public void SceneVisDifferenceColor_UntilCustomized_FollowsTheThemeDefault()
        {
            Assert.That(Settings.SceneVisDifferenceColor,
                Is.EqualTo(TracingToolUserSettings.DefaultDifferenceColor));

            Settings.SceneVisDifferenceColor = Color.green;
            Assert.That(Settings.SceneVisDifferenceColor, Is.EqualTo(Color.green));
        }

        [Test]
        public void SceneVisDifferenceColor_Customized_RoundTrips()
        {
            Settings.SceneVisDifferenceColor = Color.green;
            var json = Settings.CaptureStateForTests();

            Settings.ResetStateToDefaultForTests();
            Assert.That(Settings.SceneVisDifferenceColor,
                Is.EqualTo(TracingToolUserSettings.DefaultDifferenceColor));

            Settings.RestoreStateForTests(json);
            Assert.That(Settings.SceneVisDifferenceColor, Is.EqualTo(Color.green));
        }

        [Test]
        public void SceneVisColors_EditingOneColor_KeepsTheOthersAtTheirDefault()
        {
            var authorityDefault = Settings.SceneVisAuthorityColor;
            var differenceDefault = Settings.SceneVisDifferenceColor;

            Settings.SceneVisPredictedColor = Color.magenta;

            Assert.That(Settings.SceneVisAuthorityColor, Is.EqualTo(authorityDefault));
            Assert.That(Settings.SceneVisDifferenceColor, Is.EqualTo(differenceDefault));
        }
    }
}
