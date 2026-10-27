using System.Collections.Generic;
using Gameplay.Leaderboard;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS;
using Unity.MP_FPS.UI;
using Unity.Entities;
using Unity.NetCode;

namespace Gameplay.BuildPoints.UI
{
    public class BuildShopUi : MonoBehaviour
    {
        [Header("UI Toolkit Assets")]
        public VisualTreeAsset ShopItemTemplate; // UXML template for individual shop items

        private VisualElement _rootElement;
        private ScrollView _buildItemScrollView;
        private Label _pointsLabel;

        [System.Serializable]
        public struct ShopItemDefinition
        {
            public int ItemId;
            public string ItemName;
            public int Cost;
            public Sprite ItemIcon;
        }

        [Header("Shop Catalog")]
        [SerializeField] private List<ShopItemDefinition> _availableItems = new List<ShopItemDefinition>
        {
            new ShopItemDefinition { ItemId = 1, ItemName = "Wall", Cost = 5 },
            new ShopItemDefinition { ItemId = 2, ItemName = "Bounce Pad", Cost = 3 },
            new ShopItemDefinition { ItemId = 3, ItemName = "Spike Trap", Cost = 7 },
            new ShopItemDefinition { ItemId = 4, ItemName = "Health Pack", Cost = 3 },
            new ShopItemDefinition { ItemId = 5, ItemName = "One Way Wall", Cost = 8 },
            new ShopItemDefinition { ItemId = 6, ItemName = "Honey", Cost = 4 },
            new ShopItemDefinition { ItemId = 7, ItemName = "Ice", Cost = 6 },
            new ShopItemDefinition { ItemId = 8, ItemName = "Tower", Cost = 10 },
            new ShopItemDefinition { ItemId = 9, ItemName = "TrenchBarricade", Cost = 5 }
        };

        private void Awake()
        {
            var uiDocument = GetComponent<UIDocument>();
            if (uiDocument != null)
            {
                _rootElement = uiDocument.rootVisualElement;
                _rootElement.style.display = DisplayStyle.None; // Start hidden

                _pointsLabel = _rootElement.Q<Label>("points-label");
                _buildItemScrollView = _rootElement.Q<ScrollView>("build-item-scrollview");

                if (_buildItemScrollView != null)
                {
                    // Force the ScrollView mode to horizontal for the carousel effect
                    _buildItemScrollView.mode = ScrollViewMode.Horizontal;
                }
                else
                {
                    Debug.LogError("[BuildShopUi] Could not find 'shop-scroll-view' in the UI Document!");
                }
            }
            else
            {
                Debug.LogError("[BuildShopUi] No UIDocument component found on this GameObject!");
            }
        }

        private void Start()
        {
            LoadCatalogIntoScrollView();

            // Start a coroutine to wait for LeaderboardManager to exist and apply initial state
            StartCoroutine(WaitForLeaderboardManager());
        }

        private void OnEnable()
        {
            LeaderboardManager.BuildModeChanged += HandleBuildModeChanged;
        }

        private void OnDisable()
        {
            LeaderboardManager.BuildModeChanged -= HandleBuildModeChanged;
        }

        private void HandleBuildModeChanged(bool isBuildMode)
        {
            if (_rootElement != null)
            {
                bool isInGame = GameSettings.Instance != null && GameSettings.Instance.GameState == GlobalGameState.InGame;
                bool shouldShow = isBuildMode && isInGame;
                _rootElement.style.display = isBuildMode ? DisplayStyle.Flex : DisplayStyle.None;
                
                if (shouldShow)
                {
                    UnityEngine.Cursor.visible = true;
                    UnityEngine.Cursor.lockState = CursorLockMode.None;
                }
                else if (isBuildMode)
                {
                    UnityEngine.Cursor.visible = false;
                    UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                }
            }
        }

        private System.Collections.IEnumerator WaitForLeaderboardManager()
        {
            // Keep waiting until the singleton instance is spawned and linked
            while (LeaderboardManager.Instance == null || !LeaderboardManager.Instance.gameObject.activeInHierarchy)
            {
                yield return null;
            }

            // Once found, check the phase immediately
            bool isBuildMode = LeaderboardManager.Instance.CurrentPhase == LeaderboardManager.RoundPhase.BuildMode;
            HandleBuildModeChanged(isBuildMode);
        }

        private void LoadCatalogIntoScrollView()
        {
            if (_buildItemScrollView == null || ShopItemTemplate == null) return;

            // Clear any existing children
            _buildItemScrollView.contentContainer.Clear();

            foreach (var item in _availableItems)
            {
                // Instantiate your UXML template for each shop item
                var itemElement = ShopItemTemplate.Instantiate();

                var iconElement = itemElement.Q<VisualElement>("item-icon");
                var nameLabel = itemElement.Q<Label>("item-name");
                var costLabel = itemElement.Q<Label>("item-cost");
                var buyButton = itemElement.Q<Button>("select-button");

                if (iconElement != null && item.ItemIcon != null)
                {
                    iconElement.style.backgroundImage = new StyleBackground(item.ItemIcon);
                }

                if (nameLabel != null) nameLabel.text = item.ItemName;
                if (costLabel != null) costLabel.text = $"{item.Cost} pts";

                if (buyButton != null)
                {
                    int capturedId = item.ItemId;
                    int capturedCost = item.Cost;
                    buyButton.clicked += () => OnBuyItemClicked(capturedId, capturedCost);
                }

                // Add directly to the ScrollView content container
                _buildItemScrollView.contentContainer.Add(itemElement);
            }
        }

        private void Update()
        {
            if (_rootElement != null && _rootElement.style.display == DisplayStyle.Flex)
            {
                UpdateLocalPlayerPoints();
            }
        }

        private void UpdateLocalPlayerPoints()
        {
            if (LeaderboardManager.Instance == null || _pointsLabel == null) return;

            var scores = LeaderboardManager.Instance.GetScores();
            if (scores.Count > 0)
            {
                int currentPoints = scores[0].BuildPoints;
                _pointsLabel.text = $"Build Points: {currentPoints}";
            }
        }

        private void OnBuyItemClicked(int itemId, int cost)
        {
            if (LeaderboardManager.Instance == null) return;

            var scores = LeaderboardManager.Instance.GetScores();
            if (scores.Count > 0)
            {
                int localNetworkId = scores[0].NetworkId;
                LeaderboardManager.Instance.RequestBuyBuildItem(localNetworkId, itemId, cost);
                Debug.Log($"[BuildShopUi] Sent purchase request for item ID {itemId} costing {cost} points.");
                UpdateLocalPlayerSelection(itemId);
            }
        }

        private void UpdateLocalPlayerSelection(int itemId)
        {
            int targetIndex = Mathf.Max(0, itemId - 1);

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            var entityManager = world.EntityManager;

            EntityQuery playerQuery = entityManager.CreateEntityQuery(
                typeof(PredictedPlayerGhost),
                typeof(Unity.NetCode.GhostOwner)
            );

            if (!playerQuery.IsEmpty)
            {
                using var entities = playerQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                foreach (var entity in entities)
                {
                    var ghostOwner = entityManager.GetComponentData<Unity.NetCode.GhostOwner>(entity);

                    if (entityManager.HasComponent<Unity.NetCode.GhostOwnerIsLocal>(entity))
                    {
                        var playerData = entityManager.GetComponentData<PredictedPlayerGhost>(entity);
                        playerData.SelectedPlacementPrefabIndex = targetIndex;
                        entityManager.SetComponentData(entity, playerData);

                        Debug.Log($"[BuildShopUI] Updated SelectedPlacementPrefabIndex to {targetIndex} for item ID {itemId}");
                        break;
                    }
                }
            }
            playerQuery.Dispose();
        }
    }
}