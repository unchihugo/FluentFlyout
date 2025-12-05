using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using Windows.Services.Store;

namespace FluentFlyout.Classes;

/// <summary>
/// Manages app licensing and premium features through the Microsoft Store
/// </summary>
public class LicenseManager
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    
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
            Logger.Info("LicenseManager: Initializing");
#if GITHUB_RELEASE
            _isStoreVersion = false;
            _isPremiumUnlocked = true;
            _isInitialized = true;
            return;
#endif
            // Get Store context
            _storeContext = StoreContext.GetDefault();

            var interop = new WindowInteropHelper(Application.Current.MainWindow);
            IntPtr hwnd = interop.Handle;
            WinRT.Interop.InitializeWithWindow.Initialize(_storeContext, hwnd);

            // Get app license
            _appLicense = await _storeContext.GetAppLicenseAsync();

            // if user ever ran a self-compiled or GitHub version, set store version to false
            //if (!String.IsNullOrEmpty(SettingsManager.Current.LastKnownVersion) && SettingsManager.Current.IsStoreVersion == false)
            //{
            //    Debug.WriteLine("LicenseManager: Previous non-Store version detected.");
            //    //_isStoreVersion = false;
            //}
            //else
            //{
            //    // Check if this is a Store version
            //    _isStoreVersion = !string.IsNullOrEmpty(_appLicense?.SkuStoreId);
            //}

            _isStoreVersion = !string.IsNullOrEmpty(_appLicense?.SkuStoreId);

            if (!_isStoreVersion)
            {
                // Self-compiled or GitHub version - unlock premium for free
                Logger.Info("Non-Store version detected. Premium unlocked.");

                _isPremiumUnlocked = true;
            }
            else
            {
                // Store version - check if premium add-on is purchased
                Logger.Info($"Store version detected (SKU: {_appLicense?.SkuStoreId})");
                await CheckPremiumStatusAsync();
            }
            
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error initializing");
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

            // works offline
            if (_appLicense == null)
                _appLicense = await _storeContext.GetAppLicenseAsync();

            if (_appLicense == null)
            {
                Logger.Warn("App license is null");
                return;
            }

            // check for premium
            foreach (var addOnLicense in _appLicense.AddOnLicenses)
            {
                StoreLicense license = addOnLicense.Value;

                if (license.IsActive)
                {
                    _isPremiumUnlocked = true;
                    return;
                }
            }

            Logger.Debug("Premium not owned by user.");

            // COMMENTED OUT: unreliable online check
            // refresh license from the Store to ensure up-to-date status
            //var addOnResult = await _storeContext.GetStoreProductsAsync(new[] { "Durable" }, new[] { PremiumAddOnId });

            //if (addOnResult.ExtendedError != null)
            //{
            //    Debug.WriteLine($"LicenseManager: Error refreshing licenses - {addOnResult.ExtendedError.Message}");
            //    return;
            //}

            //if (addOnResult.Products.TryGetValue(PremiumAddOnId, out StoreProduct storeProduct))
            //{
            //    if (storeProduct.IsInUserCollection) {
            //        _isPremiumUnlocked = true;
            //        Debug.WriteLine("LicenseManager: Premium confirmed in user collection.");
            //    }
            //    else
            //    {
            //        Debug.WriteLine("LicenseManager: Premium not owned by user.");
            //    }
            //}
            //else
            //{
            //    Debug.WriteLine("LicenseManager: Premium add-on not found in refreshed licenses.");
            //}
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error checking premium status");
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
                Logger.Warn("Store context not initialized");
                return false;
            }
            
            if (!_isStoreVersion)
            {
                Logger.Warn("Cannot purchase - not a Store version");
                return false;
            }
            
            if (_isPremiumUnlocked)
            {
                Logger.Debug("Premium already unlocked");
                return true;
            }
            
            // Get the add-on
            var addOnResult = await _storeContext.GetStoreProductsAsync(new[] { "Durable" }, new[] { PremiumAddOnId });

            if (addOnResult.ExtendedError != null)
            {
                Logger.Error(addOnResult.ExtendedError.Message, "Error getting add-ons");
                return false;
            }
            
            if (!addOnResult.Products.TryGetValue(PremiumAddOnId, out var premiumProduct))
            {
                Logger.Warn("Premium add-on not found in store - " + PremiumAddOnId);
                return false;
            }

            // Request purchase
            var purchaseResult = await _storeContext.RequestPurchaseAsync(PremiumAddOnId);
            
            if (purchaseResult.ExtendedError != null)
            {
                Logger.Error(purchaseResult.ExtendedError.Message, "Purchase error");
                return false;
            }
            
            var status = purchaseResult.Status;
            
            if (status == StorePurchaseStatus.Succeeded)
            {
                _isPremiumUnlocked = true;
                Logger.Info("Premium purchase successful");
                return true;
            }
            else if (status == StorePurchaseStatus.AlreadyPurchased)
            {
                _isPremiumUnlocked = true;
                Logger.Info("Premium already purchased");
                return true;
            }
            else
            {
                Logger.Info($"Purchase failed - Status: {purchaseResult.Status}");
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error during purchase");
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
            Logger.Error(ex, "Error getting product info");
            return null;
        }
    }
}
