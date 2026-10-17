using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Unity.Netcode.Tests
{
    [Serializable]
    internal struct DummyInput : IInputComponentData
    {
        public int value;
    }

    [Serializable]
    internal struct SomeBridgedValue : IComponentData
    {
        [GhostField] public int value;
    }
    /// <summary>
    /// Test helper so we don't have to write tons of different GhostBehaviours for prediction tests.
    /// </summary>
    internal partial class PredictionCallbackHelper : GhostBehaviour
    {
        public static List<PredictionCallbackHelper> ServerInstances;
        public static List<PredictionCallbackHelper> ClientInstances;
        public event Action<GameObject> OnPredictionEvent;
        public event Action<GameObject> OnInputEvent;
        public event Action<GameObject> OnFixedPredictionEvent;
        public event Action<GameObject> OnStart;
        public event Action<GameObject> OnUpdate;
        public event Action<GameObject> OnFixedUpdate;
        public event Action<GameObject> OnLateUpdate;
        public event Action<GameObject> OnEnableEvent;
        public event Action<GameObject> OnDisableEvent;
        public event Action<GameObject> OnDestroyEvent;
        public event Action<GameObject> OnGhostSpawnEvent;
        public event Action<GameObject> OnGhostDespawnEvent;

        public MonoEventCallbackScriptableObject CallbackHolder;

        public GhostComponentRef<DummyInput> Input;
        public GhostField<int> SomeGhostField;
        public GhostComponentRef<SomeBridgedValue> SomeBridgedVar;

        static PredictionCallbackHelper()
        {
            Reset();
        }
        public static void Reset()
        {
            ServerInstances = new List<PredictionCallbackHelper>();
            ClientInstances = new List<PredictionCallbackHelper>();
        }

        /// <summary>
        /// Called before <see cref="Awake"/> when <see cref="GhostObject.DelaySpawning"/>=false.
        /// Shouldn't be invoked on prefabs.
        /// </summary>
        public override void OnGhostSpawn()
        {
            if (!Ghost.World.IsServer()) ClientInstances.Add(this);
            if (Ghost.World.IsServer()) ServerInstances.Add(this);

            base.OnGhostSpawn();

            OnGhostSpawnEvent?.Invoke(gameObject);
            CallbackHolder?.TriggerOnNetworkSpawn(gameObject);
        }

        public override void Awake()
        {
            base.Awake();
            if (Ghost.IsPrefab())
            {
                return;
            }
            CallbackHolder?.TriggerAwake(gameObject);
        }

        protected void Start()
        {
            if (Ghost.IsPrefab())
            {
                return;
            }
            OnStart?.Invoke(gameObject);
            CallbackHolder?.TriggerStart(gameObject);
        }

        public void Update()
        {
            if (Ghost.IsPrefab())
                return;
            OnUpdate?.Invoke(gameObject);
        }

        public void FixedUpdate()
        {
            if (Ghost.IsPrefab())
                return;
            OnFixedUpdate?.Invoke(gameObject);
        }

        public void LateUpdate()
        {
            if (Ghost.IsPrefab())
                return;
            OnLateUpdate?.Invoke(gameObject);
        }

        public void OnEnable()
        {
            if (Ghost.IsPrefab())
                return;
            OnEnableEvent?.Invoke(gameObject);
            CallbackHolder?.TriggerOnEnable(gameObject);
        }

        public void OnDisable()
        {
            if (Ghost.IsPrefab())
                return;
            OnDisableEvent?.Invoke(gameObject);
        }

        /// <summary>
        /// Called before <see cref="OnDestroy"/>.
        /// Shouldn't be invoked on prefabs.
        /// </summary>
        public override void OnGhostDespawn()
        {
            ServerInstances.Remove(this);
            ClientInstances.Remove(this);
            OnGhostDespawnEvent?.Invoke(gameObject);
            CallbackHolder?.TriggerOnNetworkDespawn(gameObject);
            base.OnGhostDespawn();
        }

        public override void OnDestroy()
        {
            if (Ghost.IsPrefab())
                return;
            OnDestroyEvent?.Invoke(gameObject);
            CallbackHolder?.TriggerOnDestroy(this.gameObject);
            base.OnDestroy(); // TODO-release this flow is still not great... easy to forget to call base.Awake and base.OnDestroy? Is it that bad?
            // TODO-release it's also tricky, since base.OnDestroy needs to be called at the end of the OnDestroy, so that the rest of the OnDestroy above can still access entity things
        }

        public override void PredictionGatherInput(PredictionGatherInputContext context)
        {
            OnInputEvent?.Invoke(gameObject);
        }

        public override void PredictionUpdate(PredictionUpdateContext context)
        {
            OnPredictionEvent?.Invoke(gameObject);
            CallbackHolder?.TriggerOnPrediction(gameObject);
        }

        public override void PredictionPhysicsUpdate(PredictionPhysicsUpdateContext context)
        {
            OnFixedPredictionEvent?.Invoke(gameObject);
            CallbackHolder?.TriggerOnFixedPrediction(gameObject);
        }

        public void Dispose()
        {
            ClearEvents();
            CallbackHolder = null;
        }

        public void ClearEvents()
        {
            // Note: those two are the most important to dispose, as they'll be called while the containing scene is unloaded in Teardown(),
            // potentially executing test logic while tearing down the test.
            OnDestroyEvent = null;
            OnDisableEvent = null;
            // Other events just for good measure
            OnPredictionEvent = null;
            OnInputEvent = null;
            OnFixedPredictionEvent = null;
            OnUpdate = null;
            OnFixedUpdate = null;
            OnLateUpdate = null;
            OnEnableEvent = null;
            OnGhostSpawnEvent = null;
            OnGhostDespawnEvent = null;
            CallbackHolder?.ClearEvents();
        }
    }
}
