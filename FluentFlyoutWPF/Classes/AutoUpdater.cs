// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

#if GITHUB_RELEASE

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.ViewModels;
using NLog;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace FluentFlyout.Classes;

/// <summary>
/// Handles downloading and installing MSIX updates from GitHub Releases.
/// Only compiled for GitHub Release builds.
/// </summary>
public static class AutoUpdater
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// The expected certificate thumbprint for the MSIX signing certificate.
    /// Using thumbprint (SHA-1 hash of the certificate) rather than subject CN because the
    /// certificate is self-signed — anyone could create a cert with the same CN, but the
    /// thumbprint uniquely identifies the actual certificate used by the project maintainer.
    /// </summary>
    private const string ExpectedThumbprint = "EB5DAEE797E40F20D5AFB6C60EA6A510AE519183";

    private static readonly object _lock = new();
    private static bool _isRunning;

    /// <summary>
    /// Downloads the installer ZIP from the given URL, extracts the .msixbundle, and returns its path.
    /// </summary>
    /// <param name="downloadUrl">The HTTPS URL to download from (must be HTTPS)</param>
    /// <param name="expectedSize">Expected file size in bytes from the GitHub API</param>
    /// <param name="fileName">The asset filename from the GitHub API</param>
    /// <param name="progress">Optional progress reporter (0-100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The local file path of the extracted .msixbundle, or null on failure</returns>
    public static async Task<string?> DownloadUpdateAsync(
        string downloadUrl,
        long expectedSize,
        string fileName,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isRunning) return null;
            _isRunning = true;
        }

        try
        {
            // Security: enforce HTTPS
            if (!downloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Error("Rejected non-HTTPS download URL");
                UpdateState.Current.UpdateError = "Download URL must use HTTPS";
                return null;
            }

            // Security: prevent path traversal in filename
            var safeName = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(safeName) || safeName.Contains("..") ||
                safeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                Logger.Error("Invalid filename in release asset: {Name}", fileName);
                UpdateState.Current.UpdateError = "Invalid filename in release";
                return null;
            }

            // Security: only accept .zip files (the release bundles the .msixbundle inside a ZIP)
            if (!safeName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Error("Unexpected file extension: {Name}", safeName);
                UpdateState.Current.UpdateError = "Unexpected file type";
                return null;
            }

            // Use a dedicated subdirectory in temp
            var updateDir = Path.Combine(Path.GetTempPath(), "FluentFlyout_Update");
            Directory.CreateDirectory(updateDir);

            var zipPath = Path.Combine(updateDir, safeName);

            // Clean up any previous download
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            UpdateState.Current.IsDownloading = true;
            UpdateState.Current.DownloadProgress = 0;
            UpdateState.Current.UpdateError = string.Empty;

            using var response = await HttpClient.GetAsync(downloadUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? expectedSize;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(zipPath, FileMode.Create,
                FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long bytesWritten = 0;
            int read;

            while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                bytesWritten += read;

                if (totalBytes > 0)
                {
                    var pct = (double)bytesWritten / totalBytes * 100;
                    UpdateState.Current.DownloadProgress = pct;
                    progress?.Report(pct);
                }
            }

            // Security: verify file size matches API-reported size
            if (expectedSize > 0 && bytesWritten != expectedSize)
            {
                Logger.Error("File size mismatch: expected {Expected}, got {Actual}", expectedSize, bytesWritten);
                UpdateState.Current.UpdateError = "Downloaded file size does not match expected size";
                try { File.Delete(zipPath); } catch { }
                return null;
            }

            // Close the file stream before extracting
            await fileStream.DisposeAsync();

            Logger.Info("ZIP downloaded successfully: {Path} ({Bytes} bytes)", zipPath, bytesWritten);

            // Extract the .msixbundle from the ZIP
            var bundlePath = await Task.Run(() => ExtractMsixBundleFromZip(zipPath, updateDir), cancellationToken);

            // Clean up the ZIP after extraction
            try { File.Delete(zipPath); } catch { }

            if (bundlePath == null)
            {
                UpdateState.Current.UpdateError = "Could not find .msixbundle in downloaded package";
                return null;
            }

            Logger.Info("Extracted .msixbundle: {Path}", bundlePath);
            UpdateState.Current.DownloadedBundlePath = bundlePath;
            return bundlePath;
        }
        catch (OperationCanceledException)
        {
            Logger.Info("Update download was cancelled");
            UpdateState.Current.UpdateError = "Download cancelled";
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to download update");
            UpdateState.Current.UpdateError = $"Download failed: {ex.Message}";
            return null;
        }
        finally
        {
            UpdateState.Current.IsDownloading = false;
            lock (_lock) { _isRunning = false; }
        }
    }

    /// <summary>
    /// Extracts the first .msixbundle file found inside the ZIP to the target directory.
    /// </summary>
    private static string? ExtractMsixBundleFromZip(string zipPath, string targetDir)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            var bundleEntry = archive.Entries.FirstOrDefault(
                e => e.FullName.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrEmpty(e.Name));

            if (bundleEntry == null)
            {
                Logger.Error("No .msixbundle found inside ZIP");
                return null;
            }

            // Security: sanitize the extracted filename
            var extractedName = Path.GetFileName(bundleEntry.Name);
            if (string.IsNullOrEmpty(extractedName) || extractedName.Contains("..") ||
                extractedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                Logger.Error("Invalid .msixbundle filename inside ZIP: {Name}", bundleEntry.Name);
                return null;
            }

            var extractedPath = Path.Combine(targetDir, extractedName);

            if (File.Exists(extractedPath))
                File.Delete(extractedPath);

            bundleEntry.ExtractToFile(extractedPath);

            return extractedPath;
        }
        catch (InvalidDataException ex)
        {
            Logger.Error(ex, "ZIP file is corrupted");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to extract .msixbundle from ZIP");
            return null;
        }
    }

    /// <summary>
    /// Verifies that the downloaded MSIX bundle is signed by the expected publisher.
    /// </summary>
    /// <param name="filePath">Path to the .msixbundle file</param>
    /// <returns>True if the signature is valid and matches the expected publisher</returns>
    public static async Task<bool> VerifyPackageSignatureAsync(string filePath)
    {
        try
        {
            return await Task.Run(() =>
            {
                // Security: verify the file is within the expected directory
                var fullPath = Path.GetFullPath(filePath);
                var expectedDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FluentFlyout_Update"));
                if (!fullPath.StartsWith(expectedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !fullPath.Equals(expectedDir, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Error("File path outside expected directory: {Path}", fullPath);
                    return false;
                }

                if (!File.Exists(fullPath))
                {
                    Logger.Error("File not found for signature verification: {Path}", fullPath);
                    return false;
                }

                // Use PowerShell Get-AuthenticodeSignature for robust Authenticode verification
                var escapedPath = fullPath.Replace("'", "''");
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"$sig = Get-AuthenticodeSignature -LiteralPath '{escapedPath}'; Write-Output $sig.Status; Write-Output $sig.SignerCertificate.Thumbprint\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    Logger.Error("Failed to start PowerShell for signature verification");
                    return false;
                }

                // Read stdout and stderr fully before WaitForExit to avoid deadlocks
                var output = process.StandardOutput.ReadToEnd().Trim();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(30000); // 30 second timeout

                if (!string.IsNullOrEmpty(stderr))
                {
                    Logger.Warn("Signature verification stderr: {Error}", stderr);
                }

                var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 2)
                {
                    Logger.Error("No digital signature found on package");
                    return false;
                }

                var status = lines[0].Trim();
                var thumbprint = lines[1].Trim();

                // Verify the signature status is Valid
                if (!status.Equals("Valid", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Error("Package signature status is not valid: {Status}", status);
                    return false;
                }

                // Verify the certificate thumbprint matches the expected value
                if (!thumbprint.Equals(ExpectedThumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Error("Certificate thumbprint mismatch: expected '{Expected}', got '{Actual}'",
                        ExpectedThumbprint, thumbprint);
                    return false;
                }

                Logger.Info("Package signature verified: thumbprint={Thumbprint}", thumbprint);
                return true;
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to verify package signature");
            return false;
        }
    }

    /// <summary>
    /// Installs the downloaded .msixbundle using Add-AppxPackage.
    /// This will close the running app via -ForceApplicationShutdown.
    /// </summary>
    /// <param name="filePath">Path to the .msixbundle file</param>
    /// <returns>True if the installation was started successfully</returns>
    public static async Task<bool> InstallUpdateAsync(string filePath)
    {
        try
        {
            // Security: verify path is within expected directory
            var fullPath = Path.GetFullPath(filePath);
            var expectedDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FluentFlyout_Update"));
            if (!fullPath.StartsWith(expectedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !fullPath.Equals(expectedDir, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Error("Install path outside expected directory: {Path}", fullPath);
                UpdateState.Current.UpdateError = "Invalid install path";
                return false;
            }

            if (!File.Exists(fullPath))
            {
                Logger.Error("File not found for installation: {Path}", fullPath);
                UpdateState.Current.UpdateError = "Update file not found";
                return false;
            }

            // Verify signature before installing
            if (!await VerifyPackageSignatureAsync(fullPath))
            {
                UpdateState.Current.UpdateError = "Package signature verification failed. The update will not be installed.";
                return false;
            }

            UpdateState.Current.IsInstalling = true;
            UpdateState.Current.UpdateError = string.Empty;

            // Save settings before the app gets shut down by the installer
            try
            {
                SettingsManager.SaveSettings();
                Logger.Info("Settings saved before update installation");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to save settings before update installation");
            }

            Logger.Info("Starting MSIX installation: {Path}", fullPath);

            // Escape the path for PowerShell single-quoted string
            var escapedPath = fullPath.Replace("'", "''");

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path '{escapedPath}' -ForceApplicationShutdown\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                Logger.Error("Failed to start PowerShell process for installation");
                UpdateState.Current.UpdateError = "Failed to start installer";
                return false;
            }

            // Read stdout and stderr concurrently to avoid deadlocks
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Logger.Error("MSIX installation failed (exit code {Code}): {Error}",
                    process.ExitCode, stderr);
                UpdateState.Current.UpdateError = $"Installation failed: {stderr}";
                return false;
            }

            Logger.Info("MSIX installation completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to install update");
            UpdateState.Current.UpdateError = $"Installation error: {ex.Message}";
            return false;
        }
        finally
        {
            UpdateState.Current.IsInstalling = false;
        }
    }

    /// <summary>
    /// Cleans up downloaded update files from the temp directory.
    /// </summary>
    public static void CleanupDownloadedFiles()
    {
        try
        {
            var updateDir = Path.Combine(Path.GetTempPath(), "FluentFlyout_Update");
            if (Directory.Exists(updateDir))
            {
                Directory.Delete(updateDir, recursive: true);
                Logger.Info("Cleaned up update directory");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to clean up update directory");
        }
    }
}

#endif
