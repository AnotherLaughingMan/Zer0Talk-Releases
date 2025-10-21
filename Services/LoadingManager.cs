using System;
using System.Threading.Tasks;
using Zer0Talk.ViewModels;
using Zer0Talk.Utilities;
using Sodium;

namespace Zer0Talk.Services;

public class LoadingManager
{
    private readonly LoadingWindowViewModel? _viewModel;
    private int _currentStep = 0;
    private readonly string[] _stepDescriptions = 
    {
        "Initializing cryptography",
        "Preloading audio system", 
        "Loading configuration",
        "Applying theme settings",
        "Preparing user interface",
        "Starting network services"
    };
    
    public LoadingManager(LoadingWindowViewModel? viewModel)
    {
        _viewModel = viewModel;
    }
    
    public async Task<bool> InitializeApplicationAsync()
    {
        try
        {
            if (_viewModel != null)
            {
                _viewModel.MainMessage = "Getting things ready for you...";
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
            await UpdateProgress("Initializing secure cryptography library...", 0);
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
            await UpdateProgress("Preloading audio files for instant messaging sounds...", 16);
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
            await UpdateProgress("Loading application configuration...", 33);
            await Task.Delay(300); // Visual feedback
            CompleteCurrentStep();
            
            // Step 4: Apply theme settings
            await UpdateProgress("Applying visual theme and performance settings...", 50);
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
            await UpdateProgress("Preparing secure messaging interface...", 66);
            await Task.Run(() =>
            {
                try
                {
                    // Set up WAN/network event handlers
                    AppServices.Crawler.DiscoveredChanged += peers => AppServices.Peers.SetDiscovered(peers);
                    SafeLog("Init.WAN.Events.Ready", null);
                }
                catch (Exception ex)
                {
                    SafeLog("Init.UI.Prepare.Error", ex);
                    // Non-critical, continue
                }
            });
            CompleteCurrentStep();
            
            // Step 6: Start network services
            await UpdateProgress("Initializing secure network connections...", 83);
            await Task.Run(() =>
            {
                try
                {
                    if (!Zer0Talk.Utilities.RuntimeFlags.SafeMode)
                    {
                        if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                        {
                            AppServices.Crawler.Start();
                            SafeLog("Init.Network.Crawler.Started", null);
                        }
                        else
                        {
                            SafeLog("Init.Network.Crawler.Skipped.NoInternet", null);
                        }
                    }
                    else
                    {
                        SafeLog("Init.Network.Crawler.Skipped.SafeMode", null);
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
            await UpdateProgress("Ready! Opening Zer0Talk...", 100);
            if (_viewModel != null)
            {
                _viewModel.MainMessage = "Welcome to Zer0Talk!";
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
                _viewModel.MainMessage = "Initialization failed. Please restart the application.";
                _viewModel.CurrentTask = $"Error: {ex.Message}";

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
    }    private void CompleteCurrentStep()
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

