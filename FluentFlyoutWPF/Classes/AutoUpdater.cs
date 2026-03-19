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
    /// The expected MSIX publisher identity (CN from the signing certificate subject).
    /// This is the same across all releases even though the certificate is regenerated each build.
    /// </summary>
    private const string ExpectedPublisher = "CN=49793F74-1457-4B66-A672-4ED3A640FC76";

    private static readonly object _lock = new();
    private static bool _isRunning;

    /// <summary>
    /// Downloads the installer ZIP from the given URL, extracts the .msixbundle and .cer, and returns the bundle path.
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

            // Extract the .msixbundle and .cer from the ZIP
            var (bundlePath, certPath) = await Task.Run(() => ExtractFromZip(zipPath, updateDir), cancellationToken);

            // Clean up the ZIP after extraction
            try { File.Delete(zipPath); } catch { }

            if (bundlePath == null)
            {
                UpdateState.Current.UpdateError = "Could not find .msixbundle in downloaded package";
                return null;
            }

            Logger.Info("Extracted .msixbundle: {Path}", bundlePath);
            if (certPath != null)
                Logger.Info("Extracted .cer: {Path}", certPath);

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
    /// Extracts the .msixbundle and .cer files from the ZIP to the target directory.
    /// </summary>
    private static (string? bundlePath, string? certPath) ExtractFromZip(string zipPath, string targetDir)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            string? bundlePath = ExtractEntry(archive, ".msixbundle", targetDir);
            string? certPath = ExtractEntry(archive, ".cer", targetDir);

            return (bundlePath, certPath);
        }
        catch (InvalidDataException ex)
        {
            Logger.Error(ex, "ZIP file is corrupted");
            return (null, null);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to extract files from ZIP");
            return (null, null);
        }
    }

    /// <summary>
    /// Extracts the first entry matching the given extension from a ZIP archive.
    /// </summary>
    private static string? ExtractEntry(ZipArchive archive, string extension, string targetDir)
    {
        var entry = archive.Entries.FirstOrDefault(
            e => e.FullName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                 && !string.IsNullOrEmpty(e.Name));

        if (entry == null)
        {
            Logger.Warn("No {Extension} found inside ZIP", extension);
            return null;
        }

        // Security: sanitize the extracted filename
        var extractedName = Path.GetFileName(entry.Name);
        if (string.IsNullOrEmpty(extractedName) || extractedName.Contains("..") ||
            extractedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Logger.Error("Invalid {Extension} filename inside ZIP: {Name}", extension, entry.Name);
            return null;
        }

        var extractedPath = Path.Combine(targetDir, extractedName);

        if (File.Exists(extractedPath))
            File.Delete(extractedPath);

        entry.ExtractToFile(extractedPath);

        return extractedPath;
    }

    /// <summary>
    /// Verifies that the downloaded MSIX bundle is signed by the expected publisher.
    /// Checks that the package is signed, the signature is not tampered with, and the
    /// publisher CN matches the expected value.
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

                // Use PowerShell Get-AuthenticodeSignature to verify the package signature.
                // We check Status and SignerCertificate.Subject (publisher CN).
                // Status can be "Valid" (cert is trusted) or "UnknownError" (cert not yet in
                // trusted store, which is normal before cert installation). Both are acceptable
                // as long as the publisher CN matches. "HashMismatch" or "NotSigned" indicate
                // tampering or missing signature and are always rejected.
                var escapedPath = fullPath.Replace("'", "''");
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"$sig = Get-AuthenticodeSignature -LiteralPath '{escapedPath}'; Write-Output $sig.Status; Write-Output $sig.SignerCertificate.Subject\"",
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
                var subject = lines[1].Trim();

                // Reject signatures that indicate tampering or missing signature
                if (status.Equals("HashMismatch", StringComparison.OrdinalIgnoreCase) ||
                    status.Equals("NotSigned", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Error("Package signature indicates tampering or is missing: {Status}", status);
                    return false;
                }

                // Accept "Valid" (cert trusted) or "UnknownError" (cert not yet installed, normal for new releases)
                if (!status.Equals("Valid", StringComparison.OrdinalIgnoreCase) &&
                    !status.Equals("UnknownError", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Error("Unexpected package signature status: {Status}", status);
                    return false;
                }

                // Verify the publisher CN matches the expected value
                if (!subject.Equals(ExpectedPublisher, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Error("Package publisher mismatch: expected '{Expected}', got '{Actual}'",
                        ExpectedPublisher, subject);
                    return false;
                }

                Logger.Info("Package signature verified: status={Status}, publisher={Publisher}", status, subject);
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
    /// Installs the signing certificate to the TrustedPeople store using certutil.
    /// This requires elevation (UAC prompt will appear).
    /// </summary>
    /// <param name="certPath">Path to the .cer file</param>
    /// <returns>True if the certificate was installed successfully</returns>
    private static async Task<bool> InstallCertificateAsync(string certPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(certPath);
            var escapedPath = fullPath.Replace("'", "''");

            Logger.Info("Installing signing certificate: {Path}", fullPath);

            // Use certutil via an elevated PowerShell process
            // Start-Process with -Verb RunAs triggers the UAC prompt
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"Start-Process -FilePath 'certutil.exe' -ArgumentList '-addstore','TrustedPeople','{escapedPath}' -Verb RunAs -Wait -PassThru | ForEach-Object {{ exit $_.ExitCode }}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                Logger.Error("Failed to start certificate installation process");
                return false;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Logger.Error("Certificate installation failed (exit code {Code}): {Stdout} {Stderr}",
                    process.ExitCode, stdout, stderr);
                return false;
            }

            Logger.Info("Certificate installed successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to install signing certificate");
            return false;
        }
    }

    /// <summary>
    /// Installs the downloaded .msixbundle using Add-AppxPackage.
    /// If a .cer file exists alongside the bundle, it will be installed first (triggers UAC).
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

            // Install the signing certificate if present (triggers UAC prompt).
            // The certificate changes each release, so the new one must be trusted before Add-AppxPackage.
            var certPath = Directory.GetFiles(expectedDir, "*.cer").FirstOrDefault();
            if (certPath != null)
            {
                if (!await InstallCertificateAsync(certPath))
                {
                    UpdateState.Current.UpdateError = "Failed to install signing certificate. The update requires administrator approval.";
                    return false;
                }
            }
            else
            {
                Logger.Warn("No .cer file found alongside bundle, proceeding without certificate installation");
            }

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
