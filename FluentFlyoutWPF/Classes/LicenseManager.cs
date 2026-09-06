// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Services;
using System.Windows;
using System.Windows.Interop;
using Windows.Services.Store;
using MessageBox = Wpf.Ui.Controls.MessageBox;

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
    private StoreProduct? _productResult;

    private const string PremiumOtpAddonOnId = "9N3XXQFPGFW5";
    private const string PremiumSubscriptionAddonOnId = "9P75DCVR4FRC";
    private const string PremiumTypeExperiment = "premiumType";

    private bool _isInitialized;
    private bool _isPremiumUnlocked;
    private bool _isStoreVersion;
    private bool _isSubscriptionCohort;
    private bool _subscriptionTrialAvailable;

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

    /// <summary>
    /// Gets whether this user is in the subscription cohort for the premium experiment.
    /// </summary>
    public bool IsSubscriptionCohort => _isSubscriptionCohort;

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
                Logger.Info("Store version detected (SKU: {Sku})", _appLicense?.SkuStoreId);
                _isSubscriptionCohort = ExperimentsService.CheckUuidInExperiment(PremiumTypeExperiment) == "B";
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

            _isPremiumUnlocked = false;

            // check for premium
            // If the user has purchased either the premium OTP or the subscription, we consider premium unlocked
            foreach (var addOnLicense in _appLicense.AddOnLicenses)
            {
                StoreLicense license = addOnLicense.Value;

                bool isPremiumSku =
                    license.SkuStoreId.Contains(PremiumOtpAddonOnId, StringComparison.OrdinalIgnoreCase) ||
                    license.SkuStoreId.Contains(PremiumSubscriptionAddonOnId, StringComparison.OrdinalIgnoreCase);

                if (!isPremiumSku)
                    continue;

                if (license.IsActive)
                {
                    _isPremiumUnlocked = true;
                    Logger.Info($"Premium unlocked via SKU {license.SkuStoreId}");
                    return;
                }
            }

            Logger.Debug("Premium not owned by user.");
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
    private async Task<(bool, string)> PurchasePremiumAsync()
    {
        try
        {
            if (_storeContext == null)
            {
                Logger.Warn("Store context not initialized");
                return (false, "Store context not initialized");
            }

            if (!_isStoreVersion)
            {
                Logger.Warn("Cannot purchase - not a Store version");
                return (false, "Cannot purchase - not a Store version");
            }

            if (_isPremiumUnlocked)
            {
                Logger.Debug("Premium already unlocked");
                return (true, string.Empty);
            }

            string addOnId = _isSubscriptionCohort ? PremiumSubscriptionAddonOnId : PremiumOtpAddonOnId;
            string productKind = "Durable";

            _ = TelemetryService.SendTelemetryEventAsync("premium_purchase_started", PremiumTypeExperiment);

            // Get the add-on
            var addOnResult = await _storeContext.GetStoreProductsAsync(new[] { productKind }, new[] { addOnId });

            if (addOnResult.ExtendedError != null)
            {
                Logger.Error("Error getting add-ons - {Message}", addOnResult.ExtendedError.Message);
                return (false, "Error getting add-ons - " + addOnResult.ExtendedError.Message);
            }

            if (!addOnResult.Products.TryGetValue(addOnId, out _productResult))
            {
                Logger.Warn("Premium add-on not found in store - {AddOnId}", addOnId);
                return (false, "Premium add-on not found in store");
            }

            // Request purchase
            var purchaseResult = await _storeContext.RequestPurchaseAsync(addOnId);

            if (purchaseResult.ExtendedError != null)
            {
                Logger.Error("Error during purchase - {Message}", purchaseResult.ExtendedError.Message);
                return (false, "Purchase error - " + purchaseResult.ExtendedError.Message);
            }

            var status = purchaseResult.Status;

            if (status == StorePurchaseStatus.Succeeded)
            {
                _isPremiumUnlocked = true;
                Logger.Info("Premium purchase successful");
                _ = TelemetryService.SendTelemetryEventAsync("premium_purchase_succeeded", PremiumTypeExperiment);
                return (true, string.Empty);
            }
            else if (status == StorePurchaseStatus.AlreadyPurchased)
            {
                _isPremiumUnlocked = true;
                Logger.Info("Premium already purchased");
                _ = TelemetryService.SendTelemetryEventAsync("premium_purchase_succeeded", PremiumTypeExperiment);
                return (true, string.Empty);
            }
            else
            {
                Logger.Info("Purchase failed - Status: {Status}", purchaseResult.Status);
                return (false, $"Purchase failed - Status: {purchaseResult.Status}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error during purchase");
            return (false, "Error during purchase - " + ex.Message);
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
        SettingsManager.Current.IsPremiumUnlocked = _isPremiumUnlocked;
    }

    /// <summary>
    /// Gets premium product information for display
    /// </summary>
    private async Task<string?> GetPremiumProductInfoAsync()
    {
        try
        {
            if (_storeContext == null || !_isStoreVersion)
                return null;

            string price;

            // previous price is cached - can change implementation to refresh if needed later
            if (_productResult != null)
            {
                price = _isSubscriptionCohort ? _productResult.Price?.FormattedRecurrencePrice ?? "N/A" : _productResult.Price?.FormattedPrice ?? "N/A";
                UpdatePremiumOfferDisplay(price);

                return price;
            }

            string addOnId = _isSubscriptionCohort ? PremiumSubscriptionAddonOnId : PremiumOtpAddonOnId;
            string productKind = "Durable";
            var addOnResult = await _storeContext.GetStoreProductsAsync(new[] { productKind }, new[] { addOnId });

            if (addOnResult.ExtendedError != null)
            {
                Logger.Error("Error getting premium add-on - {Message}", addOnResult.ExtendedError.Message);
                return null;
            }

            if (!addOnResult.Products.TryGetValue(addOnId, out _productResult))
            {
                Logger.Warn("Premium add-on not found in store - {AddOnId}", addOnId);
                return null;
            }

            price = _isSubscriptionCohort ? _productResult.Price?.FormattedRecurrencePrice ?? "N/A" : _productResult.Price?.FormattedPrice ?? "N/A";
            UpdatePremiumOfferDisplay(price);

            return price;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error getting product info");
            return null;
        }
    }

    public static async void GetPremiumProductInfo()
    {
        try
        {
            await Instance.GetPremiumProductInfoAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error updating premium product info");
        }
    }

    private void UpdatePremiumOfferDisplay(string price)
    {
        if (!_isSubscriptionCohort)
        {
            SettingsManager.Current.PremiumPurchaseAction = Application.Current.TryFindResource("UnlockPremiumButton") as string ?? "Unlock Premium";
            SettingsManager.Current.PremiumPrice = price;
            return;
        }

        _subscriptionTrialAvailable = _productResult?.Skus.Any(sku =>
            sku.IsSubscription && sku.SubscriptionInfo?.HasTrialPeriod == true) == true;
        SettingsManager.Current.PremiumPurchaseAction = _subscriptionTrialAvailable ? "Start 1-week free trial" : "Subscribe";
        SettingsManager.Current.PremiumPrice = _subscriptionTrialAvailable
            ? $"{price}/mo after"
            : $"{price}/month";
    }

    public static async void UnlockPremium(object sender)
    {
        object? originalContent = null;

        try
        {
            if (sender is Wpf.Ui.Controls.Button button)
            {
                originalContent = button.Content;
                button.IsEnabled = false;
                button.Content = "Processing...";
            }

            (bool success, string result) = await Instance.PurchasePremiumAsync();

            if (success)
            {
                SettingsManager.Current.IsPremiumUnlocked = true;

                MessageBox messageBox = new()
                {
                    Title = "Success",
                    Content = Application.Current.TryFindResource("PremiumPurchaseSuccess").ToString(),
                    CloseButtonText = "OK",
                };

                await messageBox.ShowDialogAsync();
            }
            else
            {
                MessageBox messageBox = new()
                {
                    Title = "Purchase Failed",
                    Content = $"{Application.Current.TryFindResource("PremiumPurchaseFailed")} ({result})",
                    CloseButtonText = "OK",
                };

                await messageBox.ShowDialogAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox messageBox = new()
            {
                Title = "Error",
                Content = $"An error occurred: {ex.Message}",
                CloseButtonText = "OK",
            };

            await messageBox.ShowDialogAsync();
        }
        finally
        {
            if (sender is Wpf.Ui.Controls.Button button)
            {
                button.IsEnabled = true;
                button.Content = originalContent;
            }
        }
    }
}