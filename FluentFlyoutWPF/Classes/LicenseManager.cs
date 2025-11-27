using FluentFlyout.Classes.Settings;
using System.Diagnostics;
using Windows.Services.Store;
using Wpf.Ui.Controls;

namespace FluentFlyout.Classes;

/// <summary>
/// Manages app licensing and premium features through the Microsoft Store
/// </summary>
public class LicenseManager
{
    private static LicenseManager? _instance;
    private static readonly object _lock = new();
    
    private StoreContext? _storeContext;
    private StoreAppLicense? _appLicense;
    private StoreProductResult? _productResult;
    
    private const string PremiumAddOnId = "9N3XXQFPGFW5";
    
    private bool _isInitialized;
    private bool _isPremiumUnlocked;
    private bool _isStoreVersion;
    
    /// <summary>
    /// Gets the singleton instance of the LicenseManager
    /// </summary>
    public static LicenseManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new LicenseManager();
                }
            }
            return _instance;
        }
    }
    
    /// <summary>
    /// Gets whether the app is a Store version (has Store Product ID)
    /// </summary>
    public bool IsStoreVersion => _isStoreVersion;
    
    /// <summary>
    /// Gets whether premium features are unlocked
    /// </summary>
    public bool IsPremiumUnlocked => _isPremiumUnlocked;
    
    private LicenseManager()
    {
        _isInitialized = false;
        _isPremiumUnlocked = false;
    }
    
    /// <summary>
    /// Initializes the license manager and checks license status
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;
            
        try
        {
            // Get Store context
            _storeContext = StoreContext.GetDefault();
            
            // Get app license
            _appLicense = await _storeContext.GetAppLicenseAsync();
            
            // if user ever ran a self-compiled or GitHub version, set store version to false
            if (!String.IsNullOrEmpty(SettingsManager.Current.LastKnownVersion) && SettingsManager.Current.IsStoreVersion == false)
            {
                Debug.WriteLine("LicenseManager: Previous non-Store version detected. Treating as non-Store version.");
                _isStoreVersion = false;
            }
            else
            {
                // Check if this is a Store version
                _isStoreVersion = !string.IsNullOrEmpty(_appLicense?.SkuStoreId);
            }

            if (!_isStoreVersion)
            {
                // Self-compiled or GitHub version - unlock premium for free
                Debug.WriteLine("LicenseManager: Non-Store version detected. Premium unlocked.");
                _isPremiumUnlocked = true;
            }
            else
            {
                // Store version - check if premium add-on is purchased
                Debug.WriteLine($"LicenseManager: Store version detected (SKU: {_appLicense?.SkuStoreId})");
                await CheckPremiumStatusAsync();
            }
            
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LicenseManager: Error initializing - {ex.Message}");
            _isInitialized = true;
        }
    }
    
    /// <summary>
    /// Checks if the premium add-on is purchased
    /// </summary>
    private async Task CheckPremiumStatusAsync()
    {
        try
        {
            if (_storeContext == null)
                return;
                
            // Get add-on license status
            var addOns = await _storeContext.GetUserCollectionAsync(new[] { PremiumAddOnId });
            
            if (addOns.ExtendedError != null)
            {
                Debug.WriteLine($"LicenseManager: Error getting add-ons - {addOns.ExtendedError.Message}");
                return;
            }
            
            // Check if premium add-on is in user's collection
            _isPremiumUnlocked = addOns.Products.ContainsKey(PremiumAddOnId);
            
            Debug.WriteLine($"LicenseManager: Premium status = {_isPremiumUnlocked}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LicenseManager: Error checking premium status - {ex.Message}");
        }
    }
    
    /// <summary>
    /// Prompts the user to purchase the premium add-on
    /// </summary>
    /// <returns>True if purchase was successful, false otherwise</returns>
    public async Task<bool> PurchasePremiumAsync()
    {
        try
        {
            if (_storeContext == null)
            {
                Debug.WriteLine("LicenseManager: Store context not initialized");
                return false;
            }
            
            if (!_isStoreVersion)
            {
                Debug.WriteLine("LicenseManager: Cannot purchase - not a Store version");
                return false;
            }
            
            if (_isPremiumUnlocked)
            {
                Debug.WriteLine("LicenseManager: Premium already unlocked");
                return true;
            }
            
            // Get product info
            if (_productResult == null)
            {
                _productResult = await _storeContext.GetStoreProductForCurrentAppAsync();
                
                if (_productResult.ExtendedError != null)
                {
                    Debug.WriteLine($"LicenseManager: Error getting product - {_productResult.ExtendedError.Message}");
                    return false;
                }
            }
            
            // Get the add-on
            var addOnResult = await _storeContext.GetAssociatedStoreProductsAsync(new[] { "Durable" });
            
            if (addOnResult.ExtendedError != null)
            {
                Debug.WriteLine($"LicenseManager: Error getting add-ons - {addOnResult.ExtendedError.Message}");
                return false;
            }
            
            if (!addOnResult.Products.TryGetValue(PremiumAddOnId, out var premiumProduct))
            {
                Debug.WriteLine("LicenseManager: Premium add-on not found in store");
                return false;
            }
            
            // Request purchase
            var purchaseResult = await _storeContext.RequestPurchaseAsync(PremiumAddOnId);
            
            if (purchaseResult.ExtendedError != null)
            {
                Debug.WriteLine($"LicenseManager: Purchase error - {purchaseResult.ExtendedError.Message}");
                return false;
            }
            
            bool success = purchaseResult.Status == StorePurchaseStatus.Succeeded;
            
            if (success)
            {
                _isPremiumUnlocked = true;
                Debug.WriteLine("LicenseManager: Premium purchase successful");
            }
            else
            {
                Debug.WriteLine($"LicenseManager: Purchase failed - Status: {purchaseResult.Status}");
            }
            
            return success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LicenseManager: Error during purchase - {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Refreshes the license status (checks for changes)
    /// </summary>
    public async Task RefreshLicenseAsync()
    {
        if (!_isStoreVersion)
            return;
            
        await CheckPremiumStatusAsync();
    }
    
    /// <summary>
    /// Gets premium product information for display
    /// </summary>
    /// <returns>Tuple of (Title, Description, FormattedPrice) or null if unavailable</returns>
    public async Task<(string Title, string Description, string Price)?> GetPremiumProductInfoAsync()
    {
        try
        {
            if (_storeContext == null || !_isStoreVersion)
                return null;
                
            var addOnResult = await _storeContext.GetAssociatedStoreProductsAsync(new[] { "Durable" });
            
            if (addOnResult.ExtendedError != null || !addOnResult.Products.TryGetValue(PremiumAddOnId, out var product))
                return null;
                
            return (
                product.Title ?? "Premium Features",
                product.Description ?? "Unlock premium features",
                product.Price?.FormattedPrice ?? "N/A"
            );
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LicenseManager: Error getting product info - {ex.Message}");
            return null;
        }
    }
}
