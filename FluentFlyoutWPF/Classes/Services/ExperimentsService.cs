// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Clients;
using NLog;
using System.Net.Http;
using System.Security.Cryptography;

namespace FluentFlyoutWPF.Classes.Services;

internal class ExperimentsService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const string ApiEndpoint = "experiments";

    private static List<Experiment> _experiments = new();
    private static bool _hasExperiments = false;

    /// <summary>
    /// Result of experiments
    /// </summary>
    public class ExperimentsResult
    {
        public List<Experiment> Experiments { get; set; } = new();
        public bool Success { get; set; }
    }

    public class Experiment
    {
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("variants")]
        public Dictionary<string, double> Variants { get; set; } = new();

        // If Enabled is true, ForceVariant is ignored.
        [System.Text.Json.Serialization.JsonPropertyName("forceVariant")]
        public string? ForceVariant { get; set; }
    }

    public static List<Experiment> GetExperiments => _experiments;

    public static bool HasExperiments => _hasExperiments;

    public static async Task<ExperimentsResult> GetExperimentsAsync()
    {
        var result = new ExperimentsResult();
        try
        {
            var response = await FluentFlyoutApiClient.GetStringAsync(ApiEndpoint);
            var experimentDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Experiment>>(response);
            if (experimentDict != null)
            {
                // Convert dictionary to list and set Name property from key
                result.Experiments = experimentDict.Select(kvp =>
                {
                    kvp.Value.Name = kvp.Key;
                    return kvp.Value;
                }).ToList();
                result.Success = true;
                Logger.Debug($"Fetched {result.Experiments.Count} experiments successfully.");
            }
            else
            {
                Logger.Debug("No experiments found.");
                result.Success = false;
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.Info($"Failed to fetch experiments - network error: {ex.Message}");
            result.Success = false;
        }
        catch (TaskCanceledException ex)
        {
            Logger.Info(ex, "Failed to fetch experiments - request timed out.");
            result.Success = false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to fetch experiments.");
            result.Success = false;
        }

        _experiments = result.Experiments;
        _hasExperiments = result.Experiments.Count > 0;

        return result;
    }

    // returns variantName
    public static string CheckUuidInExperiment(string experimentName)
    {
        var experiment = _experiments.FirstOrDefault(e => e.Name.Equals(experimentName, StringComparison.OrdinalIgnoreCase));
        Guid uuid = SettingsManager.Current.Uuid;

        if (experiment == null)
        {
            Logger.Debug($"Experiment '{experimentName}' not found.");
            return string.Empty;
        }
        if (!experiment.Enabled)
        {
            Logger.Debug($"Experiment '{experimentName}' is not enabled.");
            return !string.IsNullOrEmpty(experiment.ForceVariant) ? experiment.ForceVariant : string.Empty;
        }

        // Calculate a hash of the UUID to determine the variant
        byte[] hashBytes = MD5.HashData(uuid.ToByteArray());
        uint hash = BitConverter.ToUInt32(hashBytes, 0);
        double totalWeight = experiment.Variants.Values.Sum();
        double cumulativeWeight = 0;
        foreach (var variant in experiment.Variants)
        {
            cumulativeWeight += variant.Value;
            if ((hash % totalWeight) < cumulativeWeight)
            {
                Logger.Debug($"UUID '{uuid}' is assigned to variant '{variant.Key}' of experiment '{experimentName}'.");
                return variant.Key; // UUID is in the experiment
            }
        }
        Logger.Debug($"UUID '{uuid}' is not assigned to any variant of experiment '{experimentName}'.");
        return string.Empty; // UUID is not in the experiment
    }
}