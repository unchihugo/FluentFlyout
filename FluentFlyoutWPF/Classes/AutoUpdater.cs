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
using System.Security.Cryptography.X509Certificates;

namespace FluentFlyout.Classes;

/// <summary>
/// Handles downloading and installing MSIX updates from GitHub Releases.
/// Only compiled for GitHub Release builds.
///
/// Trust model (defense in depth, each layer independent):
///   1. Source pinning — downloads only accepted from the pinned GitHub repository URL
///      prefix (GitHub HTTPS/TLS provides source authenticity).
///   2. Authenticode verification — package must carry a valid Authenticode signature;
///      HashMismatch and NotSigned statuses are rejected outright.
///   3. Publisher identity — signer certificate subject must equal the expected CN.
///   4. Certificate cross-verification — before installing the .cer from the ZIP, its
///      thumbprint is compared against the .msixbundle's signer thumbprint. This breaks
///      circular trust: an attacker who injects a different .cer into the ZIP cannot get
///      it installed because it won't match the bundle's embedded signer.
///   5. User consent — certificate installation triggers a UAC elevation prompt.
///   6. OS enforcement — Windows Add-AppxPackage rejects updates whose publisher
///      doesn't match the currently installed package.
/// </summary>
public static class AutoUpdater
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// Pinned download URL prefix. Only downloads from this GitHub repository are accepted.
    /// This is the first trust layer — it ensures the artifact originates from the
    /// authenticated GitHub Release infrastructure for this specific repository.
    /// </summary>
    private const string AllowedDownloadUrlPrefix =
        "https://github.com/unchihugo/FluentFlyout/releases/download/";

    /// <summary>
    /// The expected MSIX publisher identity (CN from the signing certificate subject).
    /// This is the same across all releases even though the certificate is regenerated each build.
    /// </summary>
    private const string ExpectedPublisher = "CN=49793F74-1457-4B66-A672-4ED3A640FC76";

    private static readonly object _lock = new();
    private static bool _isRunning;

    /// <summary>
    /// Internal result from Authenticode signature verification.
    /// Carries the signer thumbprint for cross-verification against the .cer file.
    /// </summary>
    private class SignatureVerificationResult
    {
        public bool IsValid { get; init; }
        public string SignerThumbprint { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Subject { get; init; } = string.Empty;
    }

    /// <summary>
    /// Downloads the installer ZIP from the given URL, extracts the .msixbundle and .cer, and returns the bundle path.
    /// </summary>
    /// <param name="downloadUrl">The HTTPS URL to download from (must match pinned GitHub repository prefix)</param>
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
            // Trust layer 1: verify download originates from the pinned GitHub repository
            if (!downloadUrl.StartsWith(AllowedDownloadUrlPrefix, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Error("Download URL does not match pinned repository: {Url}", downloadUrl);
                UpdateState.Current.UpdateError = "Download rejected: URL does not match expected source";
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
        catch (HttpRequestException ex)
        {
            Logger.Error(ex, "Network error downloading update");
            UpdateState.Current.UpdateError = "Download failed due to a network error. Please check your internet connection and try again.";
            return null;
        }
        catch (InvalidDataException ex)
        {
            Logger.Error(ex, "Downloaded file is corrupted");
            UpdateState.Current.UpdateError = "Downloaded file is corrupted. Please try again.";
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
    /// Public convenience method — returns true/false only.
    /// </summary>
    public static async Task<bool> VerifyPackageSignatureAsync(string filePath)
    {
        var result = await VerifySignatureAsync(filePath);
        return result.IsValid;
    }

    /// <summary>
    /// Verifies the Authenticode signature on the MSIX bundle and returns detailed results
    /// including the signer thumbprint (needed for cross-verification with the .cer file).
    ///
    /// Trust layers applied:
    ///   - Rejects HashMismatch (tampered) and NotSigned (unsigned) statuses.
    ///   - Accepts Valid (cert trusted) and UnknownError (cert not yet in trust store,
    ///     normal for new releases before certificate installation).
    ///   - Verifies the signer certificate subject matches the expected publisher CN.
    /// </summary>
    private static async Task<SignatureVerificationResult> VerifySignatureAsync(string filePath)
    {
        var fail = new SignatureVerificationResult { IsValid = false };

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
                    return fail;
                }

                if (!File.Exists(fullPath))
                {
                    Logger.Error("File not found for signature verification: {Path}", fullPath);
                    return fail;
                }

                var escapedPath = fullPath.Replace("'", "''");
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"$sig = Get-AuthenticodeSignature -LiteralPath '{escapedPath}'; Write-Output $sig.Status; Write-Output $sig.SignerCertificate.Subject; Write-Output $sig.SignerCertificate.Thumbprint\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    Logger.Error("Failed to start PowerShell for signature verification");
                    return fail;
                }

                var output = process.StandardOutput.ReadToEnd().Trim();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(30000);

                if (!string.IsNullOrEmpty(stderr))
                {
                    Logger.Warn("Signature verification stderr: {Error}", stderr);
                }

                var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 3)
                {
                    Logger.Error("Incomplete signature data from package (got {Count} fields, expected 3)", lines.Length);
                    return fail;
                }

                var status = lines[0].Trim();
                var subject = lines[1].Trim();
                var thumbprint = lines[2].Trim();

                // Trust layer 2: reject signatures indicating tampering or absence
                if (status.Equals("HashMismatch", StringComparison.OrdinalIgnoreCase) ||
                    status.Equals("NotSigned", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Error("Package signature indicates tampering or is missing: {Status}", status);
                    return fail;
                }

                // Accept "Valid" (cert trusted) or "UnknownError" (cert not yet installed)
                if (!status.Equals("Valid", StringComparison.OrdinalIgnoreCase) &&
                    !status.Equals("UnknownError", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Error("Unexpected package signature status: {Status}", status);
                    return fail;
                }

                // Trust layer 3: verify the publisher identity
                if (!subject.Equals(ExpectedPublisher, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Error("Package publisher mismatch: expected '{Expected}', got '{Actual}'",
                        ExpectedPublisher, subject);
                    return fail;
                }

                Logger.Info("Package signature verified: status={Status}, publisher={Publisher}, thumbprint={Thumbprint}",
                    status, subject, thumbprint);

                return new SignatureVerificationResult
                {
                    IsValid = true,
                    SignerThumbprint = thumbprint,
                    Status = status,
                    Subject = subject
                };
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to verify package signature");
            return fail;
        }
    }

    /// <summary>
    /// Trust layer 4: cross-verifies that the .cer file's thumbprint matches the .msixbundle's
    /// signer thumbprint. This prevents an attacker from injecting a different certificate into
    /// the ZIP — even if the attacker's cert has the same CN, its thumbprint will not match the
    /// one embedded in the bundle's Authenticode signature.
    /// </summary>
    /// <param name="certPath">Path to the .cer file extracted from the ZIP</param>
    /// <param name="signerThumbprint">Thumbprint from the .msixbundle's Authenticode signer</param>
    /// <returns>True if the .cer matches the bundle signer</returns>
    private static bool VerifyCertMatchesSigner(string certPath, string signerThumbprint)
    {
        try
        {
            using var cert = new X509Certificate2(certPath);

            if (cert.Thumbprint.Equals(signerThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info("Certificate cross-verification passed: .cer thumbprint matches bundle signer");
                return true;
            }

            Logger.Error(
                "Certificate cross-verification FAILED: .cer thumbprint {CertThumbprint} does not match bundle signer {SignerThumbprint}",
                cert.Thumbprint, signerThumbprint);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to read certificate for cross-verification");
            return false;
        }
    }

    /// <summary>
    /// Installs the signing certificate to the TrustedPeople store using certutil.
    /// This requires elevation — Verb = "runas" triggers the UAC prompt directly.
    /// </summary>
    private static async Task<bool> InstallCertificateAsync(string certPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(certPath);

            Logger.Info("Installing signing certificate: {Path}", fullPath);

            // Run certutil directly with UAC elevation (no PowerShell wrapper needed).
            // UseShellExecute = true is required for Verb = "runas".
            var psi = new ProcessStartInfo
            {
                FileName = "certutil.exe",
                Arguments = $"-addstore TrustedPeople \"{fullPath}\"",
                UseShellExecute = true,
                Verb = "runas"
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                Logger.Error("Failed to start certificate installation process");
                return false;
            }

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Logger.Error("Certificate installation failed (exit code {Code})", process.ExitCode);
                return false;
            }

            Logger.Info("Certificate installed successfully");
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — user declined the UAC prompt
            Logger.Warn("User declined UAC prompt for certificate installation");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to install signing certificate");
            return false;
        }
    }

    /// <summary>
    /// Installs the downloaded .msixbundle using Add-AppxPackage.
    /// Applies the full trust verification chain before installation:
    ///   1. Authenticode signature check (status + publisher CN)
    ///   2. Cross-verify .cer against bundle signer (if .cer present)
    ///   3. Install certificate with UAC elevation (if .cer present)
    ///   4. Save settings before app shutdown
    ///   5. Add-AppxPackage -ForceApplicationShutdown
    ///
    /// On any failure, sets UpdateState.Current.UpdateError with an actionable message
    /// and returns false. The app remains in a usable state and the user can retry.
    /// </summary>
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
                UpdateState.Current.UpdateError = "Update file not found. Please try downloading the update again.";
                return false;
            }

            // Trust layers 2-3: verify Authenticode signature and publisher identity
            var sigResult = await VerifySignatureAsync(fullPath);
            if (!sigResult.IsValid)
            {
                UpdateState.Current.UpdateError = "Package signature verification failed. The update may be corrupted or tampered with.";
                return false;
            }

            UpdateState.Current.IsInstalling = true;
            UpdateState.Current.UpdateError = string.Empty;

            // Install the signing certificate if present
            var certPath = Directory.GetFiles(expectedDir, "*.cer").FirstOrDefault();
            if (certPath != null)
            {
                // Trust layer 4: cross-verify .cer matches the bundle's signer
                if (!VerifyCertMatchesSigner(certPath, sigResult.SignerThumbprint))
                {
                    UpdateState.Current.UpdateError = "Certificate does not match the package signer. The update will not be installed.";
                    return false;
                }

                // Trust layer 5: install cert (UAC prompt gives user the final say)
                if (!await InstallCertificateAsync(certPath))
                {
                    UpdateState.Current.UpdateError =
                        "Certificate installation was denied or failed. " +
                        "Administrator approval is required to install the update. " +
                        "Please click Install Update and accept the prompt to continue.";
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
                Logger.Warn(ex, "Failed to save settings before update — settings may need to be reconfigured");
            }

            Logger.Info("Starting MSIX installation: {Path}", fullPath);

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
                UpdateState.Current.UpdateError = "Failed to start installer. Please try again.";
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
                UpdateState.Current.UpdateError =
                    "Installation failed. You can retry, or download the update manually from the GitHub Releases page.";
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
