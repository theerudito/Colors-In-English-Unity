using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

#pragma warning disable CS0618 // Legacy IAP listener is required by the callback API used in this manager.
public class InAppManager : MonoBehaviour, IDetailedStoreListener
#pragma warning restore CS0618
{
    public static InAppManager Instance { get; private set; }

    [Serializable]
    public class NonConsumableItem
    {
        public string id;
        public string title;
        public string desc;
        public float price;
    }

    [SerializeField] private NonConsumableItem[] nonConsumableItems;

    public static event Action<string> OnPurchaseSuccess;
    public static event Action<string> OnPurchaseError;
    public static event Action OnInitializedSuccess;

    private IStoreController m_StoreController;
    private IExtensionProvider m_StoreExtensionProvider;
    private string purchaseMessage = "";

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        InitializePurchasing();
    }

    public void InitializePurchasing()
    {
        if (IsInitialized()) return;

#pragma warning disable CS0618 // Legacy initialization is required by the IDetailedStoreListener API used below.
        var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
#pragma warning restore CS0618

        if (nonConsumableItems == null || nonConsumableItems.Length == 0)
        {
            Debug.LogWarning("[IAP] No non-consumable items configured in inspector.");
            return;
        }

        foreach (var item in nonConsumableItems)
        {
            if (!string.IsNullOrEmpty(item.id))
            {
                builder.AddProduct(item.id, ProductType.NonConsumable, new StoreSpecificIds
                {
                    { item.id, GooglePlay.Name },
                    { item.id, AppleAppStore.Name }
                });
            }
        }

#pragma warning disable CS0618 // Legacy initialization is required by the IDetailedStoreListener API used below.
        UnityPurchasing.Initialize(this, builder);
#pragma warning restore CS0618
    }

    public bool IsInitialized()
    {
        return m_StoreController != null && m_StoreExtensionProvider != null;
    }

    // --- IDetailedStoreListener Callbacks ---

#pragma warning disable CS0618 // Required by the legacy IDetailedStoreListener API.
    public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
    {
        m_StoreController = controller;
        m_StoreExtensionProvider = extensions;
        Debug.Log("[IAP] Store successfully initialized.");
        OnInitializedSuccess?.Invoke();
    }
#pragma warning restore CS0618

    public void OnInitializeFailed(InitializationFailureReason error)
    {
        SetPurchaseMessage($"Store Initialization Failed: {error}");
        Debug.LogError($"[IAP] Init Failed: {error}");
    }

    public void OnInitializeFailed(InitializationFailureReason error, string message)
    {
        SetPurchaseMessage($"Store Initialization Failed: {error} - {message}");
        Debug.LogError($"[IAP] Init Failed: {error} - {message}");
    }

    public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
    {
        SetPurchaseMessage($"Purchase Failed: {failureReason}");
        Debug.LogWarning($"[IAP] Purchase failed for {product?.definition?.id}: {failureReason}");
        OnPurchaseError?.Invoke(product != null ? product.definition.id : "unknown");
    }

    public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
    {
        SetPurchaseMessage($"Purchase Failed: {failureDescription.reason} - {failureDescription.message}");
        Debug.LogWarning($"[IAP] Purchase failed: {failureDescription.message}");
        OnPurchaseError?.Invoke(product != null ? product.definition.id : "unknown");
    }

    public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs purchaseEvent)
    {
        Product purchasedProduct = purchaseEvent.purchasedProduct;

        if (purchasedProduct == null)
        {
            SetPurchaseMessage("Purchase Failed: Invalid product received.");
            OnPurchaseError?.Invoke("unknown");
            return PurchaseProcessingResult.Complete;
        }

        string productId = purchasedProduct.definition.id;

        // Dynamically verify against all registered non-consumables
        bool isValidItem = nonConsumableItems.Any(item => item.id == productId);

        if (isValidItem)
        {
            SetPurchaseMessage($"Purchase Successful: {productId}");
            Debug.Log($"[IAP] Successfully processed purchase for: {productId}");
            OnPurchaseSuccess?.Invoke(productId);
        }
        else
        {
            SetPurchaseMessage($"Purchase Unrecognized Product: {productId}");
            Debug.LogWarning($"[IAP] Unrecognized Product ID purchased: {productId}");
            OnPurchaseError?.Invoke(productId);
        }

        return PurchaseProcessingResult.Complete;
    }

    // --- Public Store Operations ---

    public void BuyNonConsumable(string productId)
    {
        if (!IsInitialized())
        {
            Debug.LogWarning("[IAP] Purchase attempted before initialization completed.");
            return;
        }

        Product product = m_StoreController.products.WithID(productId);
        if (product != null && product.availableToPurchase)
        {
            Debug.Log($"[IAP] Initiating purchase for: {product.definition.id}");
            m_StoreController.InitiatePurchase(product);
        }
        else
        {
            Debug.LogWarning($"[IAP] Product not available for purchase: {productId}");
        }
    }

    public bool HasPurchasedNonConsumable(string productId)
    {
        if (!IsInitialized()) return false;

        Product product = m_StoreController.products.WithID(productId);
        return product != null && product.hasReceipt;
    }

    public void RestorePurchases(Action<bool, string> onComplete = null)
    {
        if (!IsInitialized())
        {
            Debug.LogWarning("[IAP] Restore attempted before initialization completed.");
            onComplete?.Invoke(false, "Store not initialized.");
            return;
        }

        if (Application.platform == RuntimePlatform.IPhonePlayer ||
            Application.platform == RuntimePlatform.OSXPlayer)
        {
            var apple = m_StoreExtensionProvider.GetExtension<IAppleExtensions>();
            apple.RestoreTransactions((result, msg) =>
            {
                Debug.Log($"[IAP] Restore Transactions result: {result} - {msg}");
                onComplete?.Invoke(result, msg);
            });
        }
        else
        {
            Debug.Log($"[IAP] Restore transactions is not required on {Application.platform}.");
            onComplete?.Invoke(true, "Restore not required on this platform.");
        }
    }

    public string GetLocalizedPriceString(string productId)
    {
        if (IsInitialized())
        {
            Product product = m_StoreController.products.WithID(productId);
            if (product != null)
            {
                return product.metadata.localizedPriceString;
            }
        }
        return string.Empty;
    }

    public string GetPurchaseMessage() => purchaseMessage;
    public void SetPurchaseMessage(string message) => purchaseMessage = message;
}