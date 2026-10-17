using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using Object = UnityEngine.Object;

namespace Unity.Netcode.Editor.Tests.UI.Inspection
{
    class GhostMonoBehaviourEditorTests : UITestFixture
    {
        GameObject m_GameObject;
        UnityEditor.Editor m_Editor;

        [TearDown]
        public void TearDown()
        {
            if (m_Editor != null)
            {
                Object.DestroyImmediate(m_Editor);
                m_Editor = null;
            }
            if (m_GameObject != null)
            {
                Object.DestroyImmediate(m_GameObject);
                m_GameObject = null;
            }
            Undo.ClearAll();

            // Selection and the shared tracker outlive the test, so release the tracker's editors.
            Selection.activeObject = null;
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            rootVisualElement.Clear();
        }

        /// <summary>
        /// Finds the current inspector window for the Rigidbody component.
        /// We need to test that the <see cref="ActiveEditorTracker.ForceRebuild"/> is called each time the toggle is toggled.
        /// This means we have to use the real inspector window rather than a new one
        /// </summary>
        /// <param name="expectedToggleValue">The expected current value of the toggle when the inspector is created.</param>
        Toggle FindCurrentInspector(bool expectedToggleValue)
        {
            m_Editor = FindActiveEditor();
            Assume.That(m_Editor, Is.Not.Null, "Failed to find an inspector.");

            var inspectorRoot = m_Editor.CreateInspectorGUI();
            rootVisualElement.Add(inspectorRoot);
            simulate.FrameUpdate();

            var toggle = FindReplicationToggle(inspectorRoot);
            Assert.That(toggle, Is.Not.Null, "Expected to find a replication toggle on the Rigidbody inspector.");
            Assert.That(toggle.value, Is.EqualTo(expectedToggleValue), "Incorrect toggle value.");

            return toggle;
        }

        UnityEditor.Editor FindActiveEditor()
        {
            foreach (var editor in ActiveEditorTracker.sharedTracker.activeEditors)
            {
                if (editor.target is Rigidbody)
                {
                    return editor;
                }
            }
            return null;
        }

        /// <summary>
        /// Find the "Netcode Replication" toggle to test against
        /// </summary>
        Toggle FindReplicationToggle(VisualElement inspectorRoot)
        {
            foreach (var label in inspectorRoot.Query<Label>().ToList())
            {
                if (label.text == GhostMonoBehaviourEditor<Rigidbody, GhostRigidbody>.k_ToggleName)
                {
                    return label.parent?.Q<Toggle>();
                }
            }
            return null;
        }

        /// <summary>
        /// Ensures:
        ///     - the replication toggle adds and removes the hidden component
        ///     - Undo add and remove works
        ///     - Redo add and remove works
        ///     - That the inspector window is rebuilt anytime the hidden component is added or removed
        /// </summary>
        [Test]
        public void ReplicationToggleTest()
        {
            m_GameObject = new GameObject("GhostMonoBehaviourEditorTestTarget");
            var rigidbody = m_GameObject.AddComponent<Rigidbody>();
            Selection.activeGameObject = rigidbody.gameObject;
            ActiveEditorTracker.sharedTracker.ForceRebuild();


            // Enable the toggle
            var toggle = FindCurrentInspector(false);
            toggle.value = true;
            simulate.FrameUpdate();
            AssertComponentState(true, "Toggle Enabled");

            // Disable the toggle
            toggle = FindCurrentInspector(true);
            toggle.value = false;
            simulate.FrameUpdate();
            AssertComponentState(false, "Toggle Disabled");

            // Undo disable
            FindCurrentInspector(false);
            Undo.PerformUndo();
            simulate.FrameUpdate();
            AssertComponentState(true, "Undo toggle disable");

            // Undo enable
            FindCurrentInspector(true);
            Undo.PerformUndo();
            simulate.FrameUpdate();
            AssertComponentState(false, "Undo toggle enable");

            // Redo the enable
            FindCurrentInspector(false);
            Undo.PerformRedo();
            simulate.FrameUpdate();
            AssertComponentState(true, "Redo toggle enable");

            // Redo the disable
            FindCurrentInspector(true);
            Undo.PerformRedo();
            simulate.FrameUpdate();
            AssertComponentState(false, "Redo toggle disable");
        }

        [HideInCallstack]
        private void AssertComponentState(bool shouldExist, string checkName)
        {
            var ghostRigidbody = m_GameObject.GetComponent<GhostRigidbody>();
            if (shouldExist)
            {
                Assert.That(ghostRigidbody, Is.Not.Null, $"[{checkName}] Enabling the toggle must add the GhostBehaviour.");
                Assert.That(ghostRigidbody.hideFlags, Is.EqualTo(HideFlags.HideInInspector), $"[{checkName}] The added component should be hidden in the inspector.");
            }
            else
            {
                Assert.That(ghostRigidbody, Is.Null, $"[{checkName}] Disabling the toggle must remove the GhostBehaviour.");
            }

            Assert.That(m_Editor == null, Is.True, $"[{checkName}] The toggle should have forced an inspector rebuild, destroying the previous editor");
        }
    }
}
