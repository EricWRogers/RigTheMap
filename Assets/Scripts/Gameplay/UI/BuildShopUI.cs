using System.Collections.Generic;
using Gameplay.Leaderboard;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gameplay.BuildPoints.UI
{
    public class BuildShopUi : MonoBehaviour
    {
        [Header("UI Toolkit Assets")]
        public VisualTreeAsset ShopItemTemplate; // UXML template for individual shop items

        private VisualElement _rootElement;
        private ListView _listView;
        private Label _pointsLabel;
        private readonly List<ShopItemUiInfo> _items = new();

        [System.Serializable]
        public struct ShopItemDefinition
        {
            public int ItemId;
            public string ItemName;
            public int Cost;
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
            _rootElement = uiDocument.rootVisualElement;
            _rootElement.style.display = DisplayStyle.None; // Hidden by default

            _pointsLabel = _rootElement.Q<Label>("points-label");
            _listView = _rootElement.Q<ListView>("shop-items-list");

            if (_listView != null && ShopItemTemplate != null)
            {
                _listView.itemsSource = _items;
                _listView.makeItem = () => ShopItemTemplate.Instantiate();
                _listView.bindItem = (element, i) =>
                {
                    var shopItem = _items[i];
                    var nameLabel = element.Q<Label>("item-name");
                    var costLabel = element.Q<Label>("item-cost");
                    var buyButton = element.Q<Button>("buy-button");

                    if (nameLabel != null) nameLabel.text = shopItem.ItemName;
                    if (costLabel != null) costLabel.text = $"{shopItem.Cost} pts";

                    if (buyButton != null)
                    {
                        buyButton.clicked -= shopItem.ClickAction; // Prevent multiple bindings
                        shopItem.ClickAction = () => OnBuyItemClicked(shopItem.ItemId, shopItem.Cost);
                        buyButton.clicked += shopItem.ClickAction;
                    }
                };
                _listView.fixedItemHeight = 35;
                _listView.selectionType = SelectionType.None;
            }

            LoadCatalogIntoList();
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
                _rootElement.style.display = isBuildMode ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void LoadCatalogIntoList()
        {
            _items.Clear();
            foreach (var item in _availableItems)
            {
                _items.Add(new ShopItemUiInfo(item.ItemId, item.ItemName, item.Cost));
            }
            _listView?.RefreshItems();
        }

        private void Start()
        {
            var uiDocument = GetComponent<UIDocument>();
            _rootElement = uiDocument.rootVisualElement;
            _rootElement.style.display = DisplayStyle.None; // Start hidden

            _pointsLabel = _rootElement.Q<Label>("points-label");
            _listView = _rootElement.Q<ListView>("shop-items-list");

            if (_listView != null && ShopItemTemplate != null)
            {
                _listView.itemsSource = _items;
                _listView.makeItem = () => ShopItemTemplate.Instantiate();
                _listView.bindItem = (element, i) =>
                {
                    var shopItem = _items[i];
                    var nameLabel = element.Q<Label>("item-name");
                    var costLabel = element.Q<Label>("item-cost");
                    var buyButton = element.Q<Button>("buy-button");

                    if (nameLabel != null) nameLabel.text = shopItem.ItemName;
                    if (costLabel != null) costLabel.text = $"{shopItem.Cost} pts";

                    if (buyButton != null)
                    {
                        buyButton.clicked -= shopItem.ClickAction; 
                        shopItem.ClickAction = () => OnBuyItemClicked(shopItem.ItemId, shopItem.Cost);
                        buyButton.clicked += shopItem.ClickAction;
                    }
                };
                _listView.fixedItemHeight = 35;
                _listView.selectionType = SelectionType.None;
            }

            LoadCatalogIntoList();

            // Start a coroutine to wait for LeaderboardManager to exist
            StartCoroutine(WaitForLeaderboardManager());
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

        private void Update()
        {
            if (_rootElement.style.display == DisplayStyle.Flex)
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
            }
        }
    }

    public class ShopItemUiInfo
    {
        public int ItemId;
        public string ItemName;
        public int Cost;
        public System.Action ClickAction;

        public ShopItemUiInfo(int id, string name, int cost)
        {
            ItemId = id;
            ItemName = name;
            Cost = cost;
        }
    }
}