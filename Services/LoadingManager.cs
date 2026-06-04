using System;
using System.Threading.Tasks;
using Zer0Talk.ViewModels;
using Zer0Talk.Utilities;
using Sodium;

namespace Zer0Talk.Services;

public class LoadingManager
{
    private readonly LoadingWindowViewModel? _viewModel;
    private int _currentStep;
    private readonly string[] _stepDescriptions;

    private string LocalizedInitCrypto => AppServices.Localization.GetString("Loading.InitCrypto", "Initializing cryptography");
    private string LocalizedPreloadAudio => AppServices.Localization.GetString("Loading.PreloadAudio", "Preloading audio system");
    private string LocalizedLoadConfig => AppServices.Localization.GetString("Loading.LoadConfig", "Loading configuration");
    private string LocalizedApplyTheme => AppServices.Localization.GetString("Loading.ApplyTheme", "Applying theme settings");
    private string LocalizedPrepareUI => AppServices.Localization.GetString("Loading.PrepareUI", "Preparing user interface");
    private string LocalizedStartNetwork => AppServices.Localization.GetString("Loading.StartNetwork", "Starting network services");
    private string LocalizedGettingReady => AppServices.Localization.GetString("Loading.GettingReady", "Getting things ready for you...");
    private string LocalizedInitializing => AppServices.Localization.GetString("Loading.Initializing", "Initializing secure messaging system...");
    private string LocalizedReadyOpening => AppServices.Localization.GetString("Loading.ReadyOpening", "Ready! Opening Zer0Talk...");
    private string LocalizedWelcome => AppServices.Localization.GetString("Loading.Welcome", "Welcome to Zer0Talk!");
    private string LocalizedInitializationFailed => AppServices.Localization.GetString("Loading.InitializationFailed", "Initialization failed. Please restart the application.");
    private string LocalizedErrorPrefix => AppServices.Localization.GetString("Loading.ErrorPrefix", "Error:");
    private string LocalizedInitializingCryptography => AppServices.Localization.GetString("Loading.InitializingCryptography", "Initializing secure cryptography library...");
    private string LocalizedPreloadingAudio => AppServices.Localization.GetString("Loading.PreloadingAudio", "Preloading audio files for instant messaging sounds...");
    private string LocalizedLoadingConfiguration => AppServices.Localization.GetString("Loading.LoadingConfiguration", "Loading application configuration...");
    private string LocalizedApplyingTheme => AppServices.Localization.GetString("Loading.ApplyingTheme", "Applying visual theme and performance settings...");
    private string LocalizedPreparingUI => AppServices.Localization.GetString("Loading.PreparingUI", "Preparing secure messaging interface...");
    private string LocalizedStartingNetwork => AppServices.Localization.GetString("Loading.StartingNetwork", "Initializing secure network connections...");

    public LoadingManager(LoadingWindowViewModel? viewModel)
    {
        _viewModel = viewModel;
        _stepDescriptions = new[]
        {
            LocalizedInitCrypto,
            LocalizedPreloadAudio,
            LocalizedLoadConfig,
            LocalizedApplyTheme,
            LocalizedPrepareUI,
            LocalizedStartNetwork
        };
    }

    public async Task<bool> InitializeApplicationAsync()
    {
        try
        {
            if (_viewModel != null)
            {
                _viewModel.MainMessage = LocalizedGettingReady;
                _viewModel.Progress = 0;
            }

            // Play startup sound immediately using AudioHelper
            _ = Task.Run(async () =>
            {
                try
                {
                    SafeLog("Init.StartupSound.Begin", null);
                    await Task.Delay(100); // Small delay to ensure audio system is ready

                    var startupSoundPath = System.IO.Path.Combine("Assets", "Sounds", "short-modern-logo-242224.mp3");
                    if (System.IO.File.Exists(startupSoundPath))
                    {
                        await Zer0Talk.Services.AudioHelper.PlayCustomSoundAsync("short-modern-logo-242224.mp3");
                        SafeLog("Init.StartupSound.Complete", null);
                    }
                    else
                    {
                        SafeLog("Init.StartupSound.NotFound", null);
                    }
                }
                catch (Exception ex)
                {
                    SafeLog("Init.StartupSound.Error", ex);
                }
            });

            // Step 1: Initialize cryptography
            await UpdateProgress(LocalizedInitializingCryptography, 0);
            await Task.Run(() =>
            {
                try
                {
                    SodiumCore.Init();
                    SafeLog("Init.Sodium.Done", null);
                }
                catch (Exception ex)
                {
                    SafeLog("Init.Sodium.Error", ex);
                    throw;
                }
            });
            CompleteCurrentStep();

            // Step 2: Preload audio system
            await UpdateProgress(LocalizedPreloadingAudio, 16);
            await Task.Run(() =>
            {
                try
                {
                    SafeLog("Init.Audio.Preload.Begin", null);
                    // Force immediate initialization and preloading
                    _ = AppServices.AudioNotifications; // Triggers singleton creation with preloading
                    // Allow time for preloading to complete
                    System.Threading.Thread.Sleep(500);
                    SafeLog("Init.Audio.Preload.Complete", null);
                }
                catch (Exception ex)
                {
                    SafeLog("Init.Audio.Preload.Error", ex);
                    throw;
                }
            });
            CompleteCurrentStep();

            // Step 3: Load configuration (if account exists)
            await UpdateProgress(LocalizedLoadingConfiguration, 33);
            await Task.Delay(300); // Visual feedback
            CompleteCurrentStep();

            // Step 4: Apply theme settings
            await UpdateProgress(LocalizedApplyingTheme, 50);
            try
            {
                // Apply initial theme and performance settings (must be on UI thread)
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        AppServices.Theme.SetTheme(AppServices.Settings.Settings.Theme);

                        var sPerf = AppServices.Settings.Settings;
                        var app = Avalonia.Application.Current;
                        if (app != null)
                        {
                            app.Resources["App.AvatarInterpolation"] = sPerf.DisableGpuAcceleration ? "None" : "HighQuality";
                        }
                    }
                    catch (Exception ex)
                    {
                        SafeLog("Init.Theme.Apply.Error", ex);
                    }
                });

                // These can run on background thread
                await Task.Run(() =>
                {
                    try
                    {
                        var sPerf = AppServices.Settings.Settings;

                        // Apply FPS and refresh rate throttling
                        var fps = Math.Max(0, sPerf.FpsThrottle);
                        var interval = fps <= 0 ? 16 : Math.Max(5, 1000 / Math.Max(1, fps));
                        AppServices.Updates.UpdateUiInterval("App.UI.Pulse", interval);

                        var hz = Math.Max(0, sPerf.RefreshRateThrottle);
                        const string key = "App.UI.Refresh";
                        if (hz <= 0) AppServices.Updates.UnregisterUi(key);
                        else
                        {
                            var refreshInterval = Math.Max(5, 1000 / Math.Max(1, hz));
                            AppServices.Updates.RegisterUiInterval(key, refreshInterval, () =>
                            {
                                try { AppServices.Events.RaiseUiPulse(); } catch { }
                            });
                        }

                        FocusFramerateService.Initialize();
                        FocusFramerateService.ApplyCurrentPolicy();
                    }
                    catch (Exception ex)
                    {
                        SafeLog("Init.Performance.Error", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                SafeLog("Init.Theme.Error", ex);
                // Non-critical, continue
            }
            CompleteCurrentStep();

            // Step 5: Prepare user interface
            await UpdateProgress(LocalizedPreparingUI, 66);
            await Task.Run(() =>
            {
                try
                {
                    // Discovery wiring is handled centrally in AppServices/NetworkService.
                    SafeLog("Init.Network.Events.Ready", null);
                }
                catch (Exception ex)
                {
                    SafeLog("Init.UI.Prepare.Error", ex);
                    // Non-critical, continue
                }
            });
            CompleteCurrentStep();

            // Step 6: Start network services
            await UpdateProgress(LocalizedStartingNetwork, 83);
            await Task.Run(() =>
            {
                try
                {
                    if (!Zer0Talk.Utilities.RuntimeFlags.SafeMode)
                    {
                        SafeLog("Init.Network.Discovery.ManagedByServices", null);
                    }
                    else
                    {
                        SafeLog("Init.Network.Discovery.Skipped.SafeMode", null);
                    }
                }
                catch (Exception ex)
                {
                    SafeLog("Init.Network.Start.Error", ex);
                    // Non-critical for app startup
                }
            });
            CompleteCurrentStep();

            // Final completion
            await UpdateProgress(LocalizedReadyOpening, 100);
            if (_viewModel != null)
            {
                _viewModel.MainMessage = LocalizedWelcome;
            }
            await Task.Delay(1000); // Longer pause to ensure UI is ready

            SafeLog("LoadingManager.InitializeApplicationAsync.Success", null);
            return true;
        }
        catch (Exception ex)
        {
            SafeLog("LoadingManager.InitializeApplicationAsync.Error", ex);
            if (_viewModel != null)
            {
                _viewModel.MainMessage = LocalizedInitializationFailed;
                _viewModel.CurrentTask = $"{LocalizedErrorPrefix} {ex.Message}";

                if (_currentStep < _stepDescriptions.Length)
                {
                    _viewModel.UpdateStep(_stepDescriptions[_currentStep], LoadingStatus.Error);
                }
            }

            return false;
        }
    }

    private async Task UpdateProgress(string taskDescription, double progress)
    {
        if (_viewModel != null)
        {
            _viewModel.CurrentTask = taskDescription;
            _viewModel.Progress = progress;

            if (_currentStep < _stepDescriptions.Length)
            {
                _viewModel.UpdateStep(_stepDescriptions[_currentStep], LoadingStatus.InProgress);
            }
        }

        // Small delay for visual feedback
        await Task.Delay(200);
    }

    private void CompleteCurrentStep()
    {
        if (_viewModel != null && _currentStep < _stepDescriptions.Length)
        {
            _viewModel.UpdateStep(_stepDescriptions[_currentStep], LoadingStatus.Complete);
        }
        _currentStep++;
    }

    private static void SafeLog(string header, Exception? ex)
    {
        try
        {
            if (ex is null)
                Zer0Talk.Utilities.ErrorLogger.LogException(new InvalidOperationException(header), source: "LoadingTrace");
            else
                Zer0Talk.Utilities.ErrorLogger.LogException(ex, source: header);
        }
        catch { }
    }
}
