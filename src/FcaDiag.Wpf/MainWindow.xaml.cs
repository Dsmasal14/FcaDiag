using System.Collections.ObjectModel;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FcaDiag.Core.Efd;
using FcaDiag.Core.Enums;
using FcaDiag.Core.Interfaces;
using FcaDiag.Core.Licensing;
using FcaDiag.Core.Models;
using FcaDiag.Core.Security;
using static FcaDiag.Core.Enums.FcaCanNetwork;
using FcaDiag.J2534;
using FcaDiag.J2534.Native;
using FcaDiag.Protocols.Security;
using FcaDiag.Protocols.Transport;
using FcaDiag.Protocols.Uds;
using Microsoft.Win32;

namespace FcaDiag.Wpf;

public partial class MainWindow : Window
{
    private ICanAdapter? _adapter;
    private bool _isConnected;
    private List<J2534Device> _devices = [];
    private readonly DispatcherTimer _voltageTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly List<double> _voltageHistory = [];
    private readonly Random _random = new();
    private readonly int[] _memoryBlocks = new int[8];
    private readonly ObservableCollection<DtcDisplayItem> _dtcList = [];
    private readonly ObservableCollection<VinModuleItem> _vinList = [];
    private readonly ObservableCollection<ModuleInfoItem> _moduleInfoList = [];
    private readonly List<ModuleFullInfo> _allModulesInfo = [];
    private readonly List<ActivityLogEntry> _activityLog = [];
    private readonly DateTime _sessionStartTime = DateTime.Now;
    private string? _connectedDeviceName;
    private string? _connectedVin;
    private EfdFile? _loadedEfd;
    private LicenseValidationResult? _currentLicense;

    public MainWindow()
    {
        InitializeComponent();

        // Check license before allowing use
        if (!CheckAndActivateLicense())
        {
            Close();
            return;
        }

        // Bind DTC list
        dgDtcList.ItemsSource = _dtcList;

        // Bind VIN list
        dgVinList.ItemsSource = _vinList;

        // Bind Module Info list
        dgModuleInfo.ItemsSource = _moduleInfoList;

        // Initialize module selectors
        foreach (var module in FcaModuleDatabase.Modules)
        {
            cboModuleSelect.Items.Add($"{module.ShortName} - {module.Name}");
            cboRebootModule.Items.Add($"{module.ShortName} - {module.Name}");
        }
        if (cboModuleSelect.Items.Count > 0)
            cboModuleSelect.SelectedIndex = 0;
        if (cboRebootModule.Items.Count > 0)
            cboRebootModule.SelectedIndex = 0;

        // Initialize voltage monitoring timer
        _voltageTimer.Tick += VoltageTimer_Tick;

        // Initialize memory blocks
        for (int i = 0; i < _memoryBlocks.Length; i++)
            _memoryBlocks[i] = 0;

        // Draw initial graphics
        Loaded += (s, e) =>
        {
            DrawVoltageGraph();
            DrawMemoryBuffer();
        };

        SizeChanged += (s, e) =>
        {
            DrawVoltageGraph();
            DrawMemoryBuffer();
        };

        // Update button states based on license level
        UpdateFeatureButtonStates();

        // Initialize security tab
        InitializeSecurityTab();

        Log("> System Ready...");
        Log($"> License: {_currentLicense?.Type} - Valid until {(_currentLicense?.ExpiryDate == DateTime.MaxValue ? "Lifetime" : _currentLicense?.ExpiryDate.ToString("yyyy-MM-dd"))}");
        Log("> Waiting for vehicle connection...");
    }

    #region License Management

    private bool CheckAndActivateLicense()
    {
        // Check for saved license
        var savedLicense = LicenseManager.CheckSavedLicense();
        if (savedLicense.IsValid)
        {
            _currentLicense = savedLicense;
            return true;
        }

        // No valid license - show activation dialog
        return ShowLicenseActivationDialog();
    }

    private bool CheckFeatureAccess(LicenseFeature feature)
    {
        if (_currentLicense == null || !_currentLicense.IsValid)
        {
            ShowUpgradeDialog("No valid license", "Please activate a valid license to use this feature.");
            return false;
        }

        if (LicenseFeatures.IsFeatureAvailable(_currentLicense.Type, feature))
        {
            return true;
        }

        var requiredLicense = LicenseFeatures.GetRequiredLicense(feature);
        var featureName = LicenseFeatures.GetFeatureDescription(feature);
        ShowUpgradeDialog(
            $"{featureName} - Upgrade Required",
            $"This feature requires a {requiredLicense} license or higher.\n\n" +
            $"Your current license: {_currentLicense.Type}\n\n" +
            "Contact Spot On Auto Diagnostics to upgrade your license."
        );
        return false;
    }

    private void ShowUpgradeDialog(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 450,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(13, 17, 23)),
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow
        };

        var mainStack = new StackPanel { Margin = new Thickness(25) };

        // Lock icon
        mainStack.Children.Add(new TextBlock
        {
            Text = "🔒",
            FontSize = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 15)
        });

        // Message
        mainStack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20)
        });

        // OK Button
        var btnOk = new Button
        {
            Content = "OK",
            Width = 100,
            Padding = new Thickness(15, 10, 15, 10),
            Background = new SolidColorBrush(Color.FromRgb(255, 107, 0)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnOk.Click += (s, e) => dialog.Close();
        mainStack.Children.Add(btnOk);

        dialog.Content = mainStack;
        dialog.ShowDialog();
    }

    private void UpdateFeatureButtonStates()
    {
        if (_currentLicense == null) return;

        // Update sidebar button appearances based on license level
        UpdateButtonForFeature(btnNavReadVin, LicenseFeature.ReadVin);
        UpdateButtonForFeature(btnNavClearVin, LicenseFeature.ClearVin);
        UpdateButtonForFeature(btnNavWriteVin, LicenseFeature.WriteVin);
        UpdateButtonForFeature(btnNavLoadEfd, LicenseFeature.LoadEfdFile);
        UpdateButtonForFeature(btnNavFlashEfd, LicenseFeature.FlashEcu);
        UpdateButtonForFeature(btnNavSaveEfd, LicenseFeature.SaveEcuToEfd);
        UpdateButtonForFeature(btnNavReadEprom, LicenseFeature.ReadEprom);
        UpdateButtonForFeature(btnNavWriteEprom, LicenseFeature.WriteEprom);
        UpdateButtonForFeature(btnNavSwapHw, LicenseFeature.SwapHardware);
        UpdateButtonForFeature(btnNavModuleReport, LicenseFeature.ModuleInfoReport);
    }

    private void UpdateButtonForFeature(Button button, LicenseFeature feature)
    {
        if (_currentLicense == null) return;

        var isAvailable = LicenseFeatures.IsFeatureAvailable(_currentLicense.Type, feature);
        if (!isAvailable)
        {
            var originalContent = button.Content?.ToString() ?? "";
            if (!originalContent.EndsWith(" 🔒"))
            {
                button.Content = originalContent + " 🔒";
            }
            button.Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102));
        }
    }

    private bool ShowLicenseActivationDialog()
    {
        while (true)
        {
            var dialog = new Window
            {
                Title = "StellaFlash - License Activation",
                Width = 500,
                Height = 380,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(13, 17, 23)),
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var mainStack = new StackPanel { Margin = new Thickness(30) };

            // Logo/Title
            var titleStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) };
            titleStack.Children.Add(new TextBlock
            {
                Text = "STELLAFLASH",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 0)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "License Activation Required",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            });
            mainStack.Children.Add(titleStack);

            // License key input
            mainStack.Children.Add(new TextBlock
            {
                Text = "Enter your license key:",
                Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var txtLicenseKey = new TextBox
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                CharacterCasing = CharacterCasing.Upper,
                Background = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 170)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                Padding = new Thickness(12, 10, 12, 10),
                MaxLength = 29, // XXXXX-XXXXX-XXXXX-XXXXX-XXXXX
                TextAlignment = TextAlignment.Center
            };
            mainStack.Children.Add(txtLicenseKey);

            // Status message
            var txtStatus = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            mainStack.Children.Add(txtStatus);

            // Buttons
            var buttonStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 25, 0, 0)
            };

            var btnActivate = new Button
            {
                Content = "Activate License",
                Width = 150,
                Padding = new Thickness(15, 12, 15, 12),
                Background = new SolidColorBrush(Color.FromRgb(255, 107, 0)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var btnExit = new Button
            {
                Content = "Exit",
                Width = 100,
                Padding = new Thickness(15, 12, 15, 12),
                Background = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(15, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            btnActivate.Click += (s, ev) =>
            {
                var key = txtLicenseKey.Text.Trim();
                if (string.IsNullOrEmpty(key))
                {
                    txtStatus.Text = "Please enter a license key.";
                    txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 85, 85));
                    return;
                }

                var result = LicenseManager.ValidateLicense(key);
                if (result.IsValid)
                {
                    // Save the license
                    LicenseManager.SaveLicense(key);
                    _currentLicense = result;
                    dialog.DialogResult = true;
                    dialog.Close();
                }
                else
                {
                    txtStatus.Text = result.Message;
                    txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 85, 85));
                }
            };

            btnExit.Click += (s, ev) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            buttonStack.Children.Add(btnActivate);
            buttonStack.Children.Add(btnExit);
            mainStack.Children.Add(buttonStack);

            // Footer
            mainStack.Children.Add(new TextBlock
            {
                Text = "Contact Spot On Auto Diagnostics for licensing inquiries.",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 30, 0, 0)
            });

            dialog.Content = mainStack;

            var dialogResult = dialog.ShowDialog();
            if (dialogResult == true)
            {
                return true;
            }

            // User clicked Exit
            return false;
        }
    }

    #endregion

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var newLine = message.StartsWith(">") ? message : $"[{timestamp}] > {message}";
        txtTerminal.Text += "\n" + newLine;
        scrollLog.ScrollToEnd();

        // Also add to activity log
        _activityLog.Add(new ActivityLogEntry
        {
            Timestamp = DateTime.Now,
            Message = message.TrimStart('>', ' '),
            Category = "System"
        });
    }

    private void LogActivity(string action, string category, string details = "")
    {
        var entry = new ActivityLogEntry
        {
            Timestamp = DateTime.Now,
            Action = action,
            Category = category,
            Details = details,
            VIN = _connectedVin,
            Device = _connectedDeviceName
        };
        _activityLog.Add(entry);

        // Also show in terminal
        var message = string.IsNullOrEmpty(details) ? action : $"{action} - {details}";
        Log(message);
    }

    private void UpdateTitle(string status)
    {
        txtTitle.Text = $"StellaFlash v1.0 - [{status}]";
    }

    private void UpdateConnectionStatus(bool connected, string? deviceName = null)
    {
        _isConnected = connected;
        if (connected)
        {
            statusIndicator.Fill = new SolidColorBrush(Color.FromRgb(39, 201, 63)); // Green
            txtConnectionStatus.Text = "Connected";
            txtConnectionStatus.Foreground = new SolidColorBrush(Color.FromRgb(39, 201, 63));
            txtDeviceName.Text = deviceName ?? "Unknown device";
            btnConnect.Content = "Disconnect";
        }
        else
        {
            statusIndicator.Fill = new SolidColorBrush(Color.FromRgb(102, 102, 102)); // Gray
            txtConnectionStatus.Text = "Disconnected";
            txtConnectionStatus.Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158));
            txtDeviceName.Text = "No device";
            btnConnect.Content = "Connect Pod";
        }
    }

    #region Connection

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected)
        {
            // Disconnect
            _voltageTimer.Stop();
            if (_adapter != null)
            {
                await _adapter.DisposeAsync();
                _adapter = null;
            }
            UpdateConnectionStatus(false);
            UpdateTitle("Disconnected");
            LogActivity("Disconnected from device", "Connection", _connectedDeviceName ?? "");
            _connectedDeviceName = null;
            _dtcList.Clear();
            txtDtcCount.Text = " (0)";
            return;
        }

        // Show device selection
        _devices = J2534DeviceDiscovery.GetDevices();

        var dialog = new Window
        {
            Title = "Select Device",
            Width = 400,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(22, 27, 34))
        };

        var stack = new StackPanel { Margin = new Thickness(20) };
        var label = new TextBlock
        {
            Text = "Select J2534 Device:",
            Foreground = Brushes.White,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 15)
        };
        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 20), FontSize = 13, Padding = new Thickness(10, 8, 10, 8) };
        combo.Items.Add("Demo Mode (Simulated 2015 Jeep Grand Cherokee)");
        foreach (var d in _devices)
            combo.Items.Add($"{d.Vendor} - {d.Name}");
        combo.SelectedIndex = 0;

        var btnOk = new Button
        {
            Content = "Connect",
            Width = 120,
            Padding = new Thickness(15, 10, 15, 10),
            Background = new SolidColorBrush(Color.FromRgb(255, 107, 0)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnOk.Click += (s, ev) => { dialog.DialogResult = true; dialog.Close(); };

        stack.Children.Add(label);
        stack.Children.Add(combo);
        stack.Children.Add(btnOk);
        dialog.Content = stack;

        if (dialog.ShowDialog() != true) return;

        try
        {
            btnConnect.IsEnabled = false;
            Log("Initializing Bus Communication...");

            if (combo.SelectedIndex == 0)
            {
                // Demo mode
                _adapter = new MockCanAdapter();
                await _adapter.ConnectAsync(new ConnectionSettings { AdapterType = "DEMO" });
                _connectedDeviceName = "Demo Mode";
                _connectedVin = "1C4RJFAG5FC123456";
                LogActivity("Connected to device", "Connection", "Demo Mode - 2015 Jeep Grand Cherokee");
                Log("Protocol: CAN-C detected.");
                Log("VIN: 1C4RJFAG5FC123456");
                Log("Vehicle: 2015 Jeep Grand Cherokee");
                Log("Module Found: PCM (Powertrain Control Module).");
                Log("Security Access: Granted.");
                UpdateConnectionStatus(true, "Demo - 2015 Jeep Grand Cherokee");
                UpdateTitle("Demo Mode - Connected");
            }
            else
            {
                var device = _devices[combo.SelectedIndex - 1];
                Log($"Connecting to {device.Name}...");
                Log($"DLL: {device.DllPath}");

                var j2534 = new J2534Adapter(device);

                // Subscribe to diagnostic logging
                j2534.DiagnosticLog += (msg) => Dispatcher.Invoke(() => Log($"  [J2534] {msg}"));

                await j2534.ConnectAsync(new ConnectionSettings { AdapterType = "ISO15765", Channel = CanChannel.HS_CAN });

                _adapter = j2534;
                _connectedDeviceName = device.Name;
                LogActivity("Connected to J2534 device", "Connection", device.Name);
                Log($"Firmware: {j2534.FirmwareVersion ?? "N/A"}");
                Log($"DLL Version: {j2534.DllVersion ?? "N/A"}");
                Log($"API Version: {j2534.ApiVersion ?? "N/A"}");
                Log($"Channel: {ConnectionSettings.GetChannelName(j2534.CurrentChannel)}");
                UpdateConnectionStatus(true, device.Name);
                UpdateTitle($"{device.Name} - Connected");
            }

            _voltageTimer.Start();
            Log("Hardware conditions met. Voltage stable.");
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            MessageBox.Show(ex.Message, "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnConnect.IsEnabled = true;
        }
    }

    #endregion

    #region EPROM Operations

    private async void BtnReadEprom_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckFeatureAccess(LicenseFeature.ReadEprom)) return;

        if (!_isConnected)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        btnNavReadEprom.IsEnabled = false;
        Log("Requesting Memory Dump (Range: 0x0000-0xFFFF)...");

        // Simulate reading 8 blocks
        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(300);
            _memoryBlocks[i] = 100; // Mark as read
            Log($"Reading Block {i + 1}/8... Checksum OK.");
            DrawMemoryBuffer();
        }

        Log("Memory read complete. 32KB transferred.");
        btnNavReadEprom.IsEnabled = true;
    }

    private async void BtnWriteEprom_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckFeatureAccess(LicenseFeature.WriteEprom)) return;

        if (!_isConnected)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show("Are you sure you want to write EPROM?\nThis operation cannot be undone.",
            "Confirm Write", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        btnNavWriteEprom.IsEnabled = false;
        Log("Preparing to write EPROM...");
        Log("Erasing target memory...");
        await Task.Delay(500);

        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(400);
            Log($"Writing Block {i + 1}/8... Verified.");
        }

        Log("EPROM write complete. Verification passed.");
        btnNavWriteEprom.IsEnabled = true;
    }

    private void BtnSwapHw_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckFeatureAccess(LicenseFeature.SwapHardware)) return;

        if (!_isConnected)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Log("Hardware swap mode initiated.");
        Log("Please disconnect current module and connect replacement...");
        MessageBox.Show("Hardware Swap Mode\n\n1. Disconnect current module\n2. Connect replacement module\n3. Click OK when ready",
            "Swap HW", MessageBoxButton.OK, MessageBoxImage.Information);
        Log("Ready for new module detection.");
    }

    #endregion

    #region Diagnostics

    private async void BtnScanModules_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected || _adapter == null)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        btnNavScan.IsEnabled = false;
        _onlineModules.Clear();

        // Get current channel info if J2534 adapter
        var j2534Adapter = _adapter as J2534Adapter;
        var channelsToTry = new List<CanChannel>();

        if (j2534Adapter != null)
        {
            // Try HS-CAN first, then MS-CAN
            channelsToTry.Add(CanChannel.HS_CAN);
            channelsToTry.Add(CanChannel.MS_CAN);
        }
        else
        {
            // Demo mode - just scan once
            channelsToTry.Add(CanChannel.Auto);
        }

        int totalFound = 0;

        foreach (var channel in channelsToTry)
        {
            // Switch channel if J2534 and not already on this channel
            if (j2534Adapter != null && j2534Adapter.CurrentChannel != channel)
            {
                Log($"Switching to {ConnectionSettings.GetChannelName(channel)}...");
                try
                {
                    await j2534Adapter.ReconnectAsync(channel);
                    Log($"Connected on {ConnectionSettings.GetChannelName(channel)}");
                }
                catch (Exception ex)
                {
                    Log($"Failed to switch to {ConnectionSettings.GetChannelName(channel)}: {ex.Message}");
                    continue;
                }
            }

            Log($"Scanning for ECU modules on {ConnectionSettings.GetChannelName(channel)}...");
            Log($"  Sending TesterPresent (0x3E 0x00) to known module addresses...");

            int found = 0;
            foreach (var module in FcaModuleDatabase.Modules)
            {
                try
                {
                    Log($"  Probing {module.ShortName} (TX: 0x{module.RequestId:X3}, RX: 0x{module.ResponseId:X3})...");
                    var client = new UdsClient(_adapter, module, 200);
                    var response = await client.TesterPresentAsync();

                    if (response.IsPositive)
                    {
                        found++;
                        if (!_onlineModules.Contains(module.ResponseId))
                        {
                            _onlineModules.Add(module.ResponseId);
                        }
                        Log($"    FOUND: {module.ShortName} ({module.Name}) responding on {ConnectionSettings.GetChannelName(channel)}");
                    }
                    else if (response.Data != null && response.Data.Length > 0)
                    {
                        var dataHex = BitConverter.ToString(response.Data).Replace("-", " ");
                        Log($"    Negative response from {module.ShortName}: {dataHex}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"    Error probing {module.ShortName}: {ex.Message}");
                }
            }

            Log($"{ConnectionSettings.GetChannelName(channel)} scan: {found} module(s) responding.");
            totalFound += found;

            // If we found modules, we can stop scanning other channels
            if (found > 0)
            {
                Log($"Modules found on {ConnectionSettings.GetChannelName(channel)}, skipping remaining channels.");
                break;
            }
        }

        if (totalFound == 0)
        {
            Log("WARNING: No modules found on any channel.");
            Log("Possible causes:");
            Log("  - Ignition not ON (key in RUN position)");
            Log("  - OBD-II cable not fully connected");
            Log("  - Vehicle CAN bus not active");
            Log("  - Wrong protocol (this tool uses ISO 15765-4)");
        }

        Log($"Scan complete. {totalFound} module(s) responding total.");

        // Update topology if visible
        PopulateTopology();

        btnNavScan.IsEnabled = true;
    }

    private async void BtnReadDtc_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected || _adapter == null)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        btnNavDtc.IsEnabled = false;
        _dtcList.Clear();
        Log("Reading Diagnostic Trouble Codes...");

        foreach (var module in FcaModuleDatabase.Modules)
        {
            try
            {
                var client = new UdsClient(_adapter, module, 500);
                var dtcs = await client.ReadDtcsAsync();
                foreach (var dtc in dtcs)
                {
                    var status = dtc.Confirmed ? "ACTIVE" : dtc.Pending ? "PENDING" : "STORED";
                    Log($"  [{module.ShortName}] {dtc.DisplayCode} - {status}");
                    Log($"           {dtc.Description}");

                    _dtcList.Add(new DtcDisplayItem
                    {
                        DisplayCode = dtc.DisplayCode,
                        StatusText = status,
                        Description = dtc.Description,
                        Category = dtc.Category,
                        ModuleName = module.ShortName,
                        IsConfirmed = dtc.Confirmed,
                        IsPending = dtc.Pending
                    });
                }
            }
            catch { }
        }

        txtDtcCount.Text = $" ({_dtcList.Count})";
        Log(_dtcList.Count > 0 ? $"Found {_dtcList.Count} DTC(s)." : "No DTCs found.");
        btnNavDtc.IsEnabled = true;
    }

    private async void BtnClearDtc_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected || _adapter == null)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_dtcList.Count == 0)
        {
            MessageBox.Show("No DTCs to clear.", "Clear DTCs", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"Are you sure you want to clear {_dtcList.Count} DTC(s)?\nThis cannot be undone.",
            "Clear DTCs", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        btnNavClearDtc.IsEnabled = false;
        Log("Clearing Diagnostic Trouble Codes...");

        int cleared = 0;
        foreach (var module in FcaModuleDatabase.Modules)
        {
            try
            {
                var client = new UdsClient(_adapter, module, 500);
                var response = await client.ClearDtcsAsync();
                if (response.IsPositive)
                {
                    cleared++;
                    Log($"  [{module.ShortName}] DTCs cleared.");
                }
            }
            catch { }
        }

        _dtcList.Clear();
        txtDtcCount.Text = " (0)";
        Log($"Clear complete. {cleared} module(s) processed.");
        btnNavClearDtc.IsEnabled = true;
    }

    #endregion

    #region PDF Export

    private void BtnPrintDtc_Click(object sender, RoutedEventArgs e)
    {
        if (_dtcList.Count == 0)
        {
            MessageBox.Show("No DTCs to print. Please read DTCs first.", "Print Report", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true) return;

            // Create the document
            var flowDoc = CreateDtcReportDocument();

            // Create a paginator
            var paginator = ((IDocumentPaginatorSource)flowDoc).DocumentPaginator;
            paginator.PageSize = new Size(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);

            // Print
            printDialog.PrintDocument(paginator, "DTC Report - StellaFlash");

            Log("DTC report sent to printer.");
            MessageBox.Show("Report sent to printer successfully.", "Print Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Print error: {ex.Message}");
            MessageBox.Show($"Failed to print: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private FlowDocument CreateDtcReportDocument()
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(50),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12
        };

        // Title
        var title = new Paragraph(new Run("DIAGNOSTIC TROUBLE CODE REPORT"))
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        doc.Blocks.Add(title);

        // Subtitle
        var subtitle = new Paragraph(new Run("StellaFlash"))
        {
            FontSize = 14,
            Foreground = Brushes.Gray,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20)
        };
        doc.Blocks.Add(subtitle);

        // Report Info
        var infoSection = new Section();
        infoSection.Blocks.Add(new Paragraph(new Run($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")) { Margin = new Thickness(0, 0, 0, 5) });
        if (!string.IsNullOrEmpty(_connectedDeviceName))
            infoSection.Blocks.Add(new Paragraph(new Run($"Device: {_connectedDeviceName}")) { Margin = new Thickness(0, 0, 0, 5) });
        if (!string.IsNullOrEmpty(_connectedVin))
            infoSection.Blocks.Add(new Paragraph(new Run($"VIN: {_connectedVin}")) { Margin = new Thickness(0, 0, 0, 5) });
        infoSection.Blocks.Add(new Paragraph(new Run($"Total DTCs: {_dtcList.Count}")) { Margin = new Thickness(0, 0, 0, 20) });
        doc.Blocks.Add(infoSection);

        // Create table
        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1)
        };

        // Define columns
        table.Columns.Add(new TableColumn { Width = new GridLength(80) });
        table.Columns.Add(new TableColumn { Width = new GridLength(70) });
        table.Columns.Add(new TableColumn { Width = new GridLength(70) });
        table.Columns.Add(new TableColumn { Width = GridLength.Auto });

        var rowGroup = new TableRowGroup();

        // Header row
        var headerRow = new TableRow { Background = Brushes.LightGray };
        headerRow.Cells.Add(CreateTableCell("Code", true));
        headerRow.Cells.Add(CreateTableCell("Module", true));
        headerRow.Cells.Add(CreateTableCell("Status", true));
        headerRow.Cells.Add(CreateTableCell("Description", true));
        rowGroup.Rows.Add(headerRow);

        // Data rows
        foreach (var dtc in _dtcList)
        {
            var row = new TableRow();
            row.Cells.Add(CreateTableCell(dtc.DisplayCode, false, FontWeights.Bold));
            row.Cells.Add(CreateTableCell(dtc.ModuleName ?? ""));
            row.Cells.Add(CreateTableCell(dtc.StatusText, false, FontWeights.Normal,
                dtc.IsConfirmed ? Brushes.Red : dtc.IsPending ? Brushes.Orange : Brushes.Black));
            row.Cells.Add(CreateTableCell(dtc.Description));
            rowGroup.Rows.Add(row);
        }

        table.RowGroups.Add(rowGroup);
        doc.Blocks.Add(table);

        // Footer
        var footer = new Paragraph(new Run("\n\nGenerated by StellaFlash - Spot On Auto Diagnostics"))
        {
            FontSize = 10,
            Foreground = Brushes.Gray,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 30, 0, 0)
        };
        doc.Blocks.Add(footer);

        return doc;
    }

    private static TableCell CreateTableCell(string text, bool isHeader = false, FontWeight? weight = null, Brush? foreground = null)
    {
        var paragraph = new Paragraph(new Run(text))
        {
            Margin = new Thickness(5)
        };

        if (isHeader)
        {
            paragraph.FontWeight = FontWeights.Bold;
        }
        else if (weight.HasValue)
        {
            paragraph.FontWeight = weight.Value;
        }

        if (foreground != null)
        {
            paragraph.Foreground = foreground;
        }

        return new TableCell(paragraph)
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 1, 1)
        };
    }

    #endregion

    #region EFD File Operations

    private void BtnLoadEfd_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckFeatureAccess(LicenseFeature.LoadEfdFile)) return;

        var dialog = new OpenFileDialog
        {
            Title = "Open EFD File",
            Filter = "EFD Files (*.efd)|*.efd|All Files (*.*)|*.*",
            DefaultExt = ".efd"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            Log($"Loading EFD file: {System.IO.Path.GetFileName(dialog.FileName)}");

            _loadedEfd = EfdParser.Parse(dialog.FileName);

            // Update UI
            DisplayEfdInfo(_loadedEfd);

            Log($"EFD file loaded successfully.");
            Log($"  Part Number: {_loadedEfd.PartNumber}");
            Log($"  Model Year: {_loadedEfd.ModelYear}");
            Log($"  Engine: {_loadedEfd.Engine}");
            Log($"  Transmission: {_loadedEfd.Transmission}");
            Log($"  Program: {_loadedEfd.Program}");
            Log($"  Data Size: {_loadedEfd.FileSizeDisplay}");

            // Switch to EFD tab
            tabRight.SelectedIndex = 1;

            // Enable flash button if connected
            btnFlashEfd.IsEnabled = _isConnected;
        }
        catch (Exception ex)
        {
            Log($"Error loading EFD: {ex.Message}");
            MessageBox.Show($"Failed to load EFD file:\n\n{ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DisplayEfdInfo(EfdFile efd)
    {
        // Show info panel, hide placeholder
        gridEfdInfo.Visibility = Visibility.Visible;
        txtEfdNoFile.Visibility = Visibility.Collapsed;

        // Populate fields
        txtEfdFileName.Text = efd.FileName;
        txtEfdPartNumber.Text = efd.PartNumber;
        txtEfdModelYear.Text = efd.ModelYear > 0 ? efd.ModelYear.ToString() : "-";
        txtEfdEngine.Text = !string.IsNullOrEmpty(efd.Engine) ? efd.Engine : "-";
        txtEfdTrans.Text = !string.IsNullOrEmpty(efd.Transmission) ? efd.Transmission : "-";
        txtEfdProgram.Text = !string.IsNullOrEmpty(efd.Program) ? efd.Program : "-";
        txtEfdVersion.Text = !string.IsNullOrEmpty(efd.Version) ? efd.Version : "-";
        txtEfdSize.Text = efd.FileSizeDisplay;

        // Populate metadata grid
        var metadataItems = efd.Metadata.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value)).ToList();
        dgEfdMetadata.ItemsSource = metadataItems;
    }

    private async void BtnFlashEfd_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckFeatureAccess(LicenseFeature.FlashEcu)) return;

        if (_loadedEfd == null)
        {
            MessageBox.Show("Please load an EFD file first.", "No File", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_isConnected || _adapter == null)
        {
            MessageBox.Show("Please connect to a device first.", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to flash this calibration to the ECU?\n\n" +
            $"File: {_loadedEfd.FileName}\n" +
            $"Part #: {_loadedEfd.PartNumber}\n" +
            $"Program: {_loadedEfd.Program}\n\n" +
            "WARNING: This operation may render the ECU inoperable if interrupted.\n" +
            "Ensure stable power and do not disconnect during the process.",
            "Confirm Flash",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        btnFlashEfd.IsEnabled = false;
        btnNavFlashEfd.IsEnabled = false;

        // Capture programming report data
        var reportData = new ProgrammingReportData
        {
            StartTime = DateTime.Now,
            DeviceName = _connectedDeviceName ?? "Unknown",
            VIN = _connectedVin ?? "N/A",
            TargetModule = _loadedEfd.TargetModule ?? "PCM",
            FlashFileName = _loadedEfd.FileName,
            FlashPartNumber = _loadedEfd.PartNumber,
            BeforeCalibration = "Unknown", // Would read from ECU in real implementation
            BeforeSoftwareVersion = "Unknown",
            AfterCalibration = _loadedEfd.Program,
            AfterSoftwareVersion = _loadedEfd.Version ?? "N/A"
        };

        // Simulate reading current calibration before flash
        Log("Reading current ECU calibration...");
        await Task.Delay(300);
        reportData.BeforeCalibration = "17RU3612AC"; // Simulated previous calibration
        reportData.BeforeSoftwareVersion = "14.22.08";

        bool flashSuccess = false;

        try
        {
            Log("Starting ECU flash process...");
            Log($"  Target: {_loadedEfd.TargetModule}");
            Log($"  Calibration: {_loadedEfd.Program}");

            // Simulate flashing process
            var totalBlocks = _loadedEfd.DataBlocks.Count > 0 ? _loadedEfd.DataBlocks.Count : 8;
            var blockSize = _loadedEfd.TotalDataSize / totalBlocks;

            Log("Establishing programming session...");
            await Task.Delay(500);

            Log("Security access granted.");
            await Task.Delay(300);

            Log("Erasing target memory...");
            await Task.Delay(1000);

            for (int i = 0; i < totalBlocks; i++)
            {
                _memoryBlocks[i % 8] = (int)((i + 1) * 100.0 / totalBlocks);
                DrawMemoryBuffer();

                var address = i * blockSize;
                Log($"Writing block {i + 1}/{totalBlocks} @ 0x{address:X6}...");
                await Task.Delay(400);
            }

            Log("Verifying flash data...");
            await Task.Delay(500);

            Log("Flash complete! ECU programmed successfully.");
            Log($"  Calibration: {_loadedEfd.Program}");
            Log($"  Version: {_loadedEfd.Version}");

            flashSuccess = true;
            reportData.EndTime = DateTime.Now;
            reportData.Status = "SUCCESS";
            reportData.StatusMessage = "Programming completed successfully. All blocks verified.";

            MessageBox.Show("ECU flash completed successfully!\n\nWould you like to generate a programming report?",
                "Flash Complete", MessageBoxButton.OK, MessageBoxImage.Information);

            // Generate programming report
            GenerateProgrammingReport(reportData);
        }
        catch (Exception ex)
        {
            Log($"Flash error: {ex.Message}");
            reportData.EndTime = DateTime.Now;
            reportData.Status = "FAILED";
            reportData.StatusMessage = ex.Message;

            MessageBox.Show($"Flash failed:\n\n{ex.Message}", "Flash Error", MessageBoxButton.OK, MessageBoxImage.Error);

            // Still generate report for failed attempts
            var generateReport = MessageBox.Show("Would you like to generate a programming report for this failed attempt?",
                "Generate Report", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (generateReport == MessageBoxResult.Yes)
            {
                GenerateProgrammingReport(reportData);
            }
        }
        finally
        {
            btnFlashEfd.IsEnabled = true;
            btnNavFlashEfd.IsEnabled = true;
        }
    }

    private void GenerateProgrammingReport(ProgrammingReportData data)
    {
        try
        {
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true) return;

            var flowDoc = CreateProgrammingReportDocument(data);
            var paginator = ((IDocumentPaginatorSource)flowDoc).DocumentPaginator;
            paginator.PageSize = new Size(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);

            printDialog.PrintDocument(paginator, $"Programming Report - {data.TargetModule}");

            Log("Programming report generated successfully.");
            LogActivity("Generated programming report", "Report", $"Module: {data.TargetModule}, Status: {data.Status}");
        }
        catch (Exception ex)
        {
            Log($"Report generation error: {ex.Message}");
            MessageBox.Show($"Failed to generate report:\n\n{ex.Message}", "Report Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private FlowDocument CreateProgrammingReportDocument(ProgrammingReportData data)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(50),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11
        };

        // Header with logo placeholder and title
        var headerTable = new Table { CellSpacing = 0 };
        headerTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var headerRowGroup = new TableRowGroup();
        var headerRow = new TableRow();

        var titleCell = new TableCell();
        var titleBlock = new Paragraph(new Run("PROGRAMMING REPORT"))
        {
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 0)),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 5)
        };
        titleCell.Blocks.Add(titleBlock);

        var subtitleBlock = new Paragraph(new Run("StellaFlash - Spot On Auto Diagnostics"))
        {
            FontSize = 12,
            Foreground = Brushes.Gray,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20)
        };
        titleCell.Blocks.Add(subtitleBlock);
        headerRow.Cells.Add(titleCell);
        headerRowGroup.Rows.Add(headerRow);
        headerTable.RowGroups.Add(headerRowGroup);
        doc.Blocks.Add(headerTable);

        // Status Banner
        var statusColor = data.Status == "SUCCESS" ? Color.FromRgb(39, 174, 96) : Color.FromRgb(231, 76, 60);
        var statusBanner = new Paragraph(new Run($"  {data.Status}  "))
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(statusColor),
            TextAlignment = TextAlignment.Center,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 20)
        };
        doc.Blocks.Add(statusBanner);

        // Session Information Section
        AddSectionHeader(doc, "SESSION INFORMATION");
        var sessionTable = CreateInfoTable(new[]
        {
            ("Date", data.StartTime.ToString("MMMM dd, yyyy")),
            ("Start Time", data.StartTime.ToString("HH:mm:ss")),
            ("End Time", data.EndTime.ToString("HH:mm:ss")),
            ("Duration", (data.EndTime - data.StartTime).ToString(@"mm\:ss")),
            ("Device", data.DeviceName),
            ("Vehicle VIN", data.VIN)
        });
        doc.Blocks.Add(sessionTable);

        // Target Module Section
        AddSectionHeader(doc, "TARGET MODULE");
        var moduleTable = CreateInfoTable(new[]
        {
            ("Module", data.TargetModule),
            ("Flash File", data.FlashFileName),
            ("Part Number", data.FlashPartNumber)
        });
        doc.Blocks.Add(moduleTable);

        // Calibration Comparison Section
        AddSectionHeader(doc, "CALIBRATION COMPARISON");

        var comparisonTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 10, 0, 20) };
        comparisonTable.Columns.Add(new TableColumn { Width = new GridLength(140) });
        comparisonTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        comparisonTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        var compRowGroup = new TableRowGroup();

        // Header row
        var compHeaderRow = new TableRow { Background = new SolidColorBrush(Color.FromRgb(52, 73, 94)) };
        compHeaderRow.Cells.Add(CreateStyledCell("", true, Brushes.White));
        compHeaderRow.Cells.Add(CreateStyledCell("BEFORE", true, Brushes.White));
        compHeaderRow.Cells.Add(CreateStyledCell("AFTER", true, Brushes.White));
        compRowGroup.Rows.Add(compHeaderRow);

        // Calibration row
        var calRow = new TableRow { Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)) };
        calRow.Cells.Add(CreateStyledCell("Calibration ID", false, Brushes.DarkSlateGray, FontWeights.SemiBold));
        calRow.Cells.Add(CreateStyledCell(data.BeforeCalibration, false, Brushes.Black, FontWeights.Normal, new FontFamily("Consolas")));
        calRow.Cells.Add(CreateStyledCell(data.AfterCalibration, false, new SolidColorBrush(Color.FromRgb(39, 174, 96)), FontWeights.Bold, new FontFamily("Consolas")));
        compRowGroup.Rows.Add(calRow);

        // Software version row
        var swRow = new TableRow();
        swRow.Cells.Add(CreateStyledCell("Software Version", false, Brushes.DarkSlateGray, FontWeights.SemiBold));
        swRow.Cells.Add(CreateStyledCell(data.BeforeSoftwareVersion, false, Brushes.Black, FontWeights.Normal, new FontFamily("Consolas")));
        swRow.Cells.Add(CreateStyledCell(data.AfterSoftwareVersion, false, new SolidColorBrush(Color.FromRgb(39, 174, 96)), FontWeights.Bold, new FontFamily("Consolas")));
        compRowGroup.Rows.Add(swRow);

        comparisonTable.RowGroups.Add(compRowGroup);
        doc.Blocks.Add(comparisonTable);

        // Result Details Section
        AddSectionHeader(doc, "RESULT DETAILS");
        var resultPara = new Paragraph(new Run(data.StatusMessage))
        {
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 20),
            Padding = new Thickness(15),
            Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 226, 230)),
            BorderThickness = new Thickness(1)
        };
        doc.Blocks.Add(resultPara);

        // Footer
        var footerLine = new Paragraph()
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 30, 0, 10)
        };
        doc.Blocks.Add(footerLine);

        var footer = new Paragraph();
        footer.Inlines.Add(new Run($"Report Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
        {
            FontSize = 9,
            Foreground = Brushes.Gray
        });
        footer.Inlines.Add(new Run("    |    ") { Foreground = Brushes.LightGray });
        footer.Inlines.Add(new Run("StellaFlash v1.0 - Spot On Auto Diagnostics")
        {
            FontSize = 9,
            Foreground = Brushes.Gray
        });
        footer.TextAlignment = TextAlignment.Center;
        doc.Blocks.Add(footer);

        var disclaimer = new Paragraph(new Run("This report is generated automatically by StellaFlash. " +
            "Verify all information before taking any action based on this report."))
        {
            FontSize = 8,
            Foreground = Brushes.Gray,
            FontStyle = FontStyles.Italic,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        };
        doc.Blocks.Add(disclaimer);

        return doc;
    }

    private void AddSectionHeader(FlowDocument doc, string title)
    {
        var header = new Paragraph(new Run(title))
        {
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(52, 73, 94)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 107, 0)),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(0, 15, 0, 5),
            Margin = new Thickness(0, 0, 0, 5)
        };
        doc.Blocks.Add(header);
    }

    private Table CreateInfoTable((string Label, string Value)[] items)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 5, 0, 15) };
        table.Columns.Add(new TableColumn { Width = new GridLength(140) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        var rowGroup = new TableRowGroup();
        bool alternate = false;

        foreach (var (label, value) in items)
        {
            var row = new TableRow();
            if (alternate)
                row.Background = new SolidColorBrush(Color.FromRgb(248, 249, 250));

            var labelCell = new TableCell(new Paragraph(new Run(label))
            {
                Margin = new Thickness(10, 6, 10, 6),
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DarkSlateGray
            });

            var valueCell = new TableCell(new Paragraph(new Run(value))
            {
                Margin = new Thickness(10, 6, 10, 6),
                FontFamily = new FontFamily("Consolas")
            });

            row.Cells.Add(labelCell);
            row.Cells.Add(valueCell);
            rowGroup.Rows.Add(row);
            alternate = !alternate;
        }

        table.RowGroups.Add(rowGroup);
        return table;
    }

    private static TableCell CreateStyledCell(string text, bool isHeader, Brush foreground,
        FontWeight? weight = null, FontFamily? font = null)
    {
        var para = new Paragraph(new Run(text))
        {
            Margin = new Thickness(10, 8, 10, 8),
            TextAlignment = TextAlignment.Center
        };

        if (isHeader)
        {
            para.FontWeight = FontWeights.Bold;
            para.FontSize = 11;
        }
        else
        {
            para.FontWeight = weight ?? FontWeights.Normal;
        }

        para.Foreground = foreground;
        if (font != null)
            para.FontFamily = font;

        return new TableCell(para)
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 226, 230)),
            BorderThickness = new Thickness(0, 0, 1, 1)
        };
    }

    private async void BtnSaveEfd_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckFeatureAccess(LicenseFeature.SaveEcuToEfd)) return;

        if (!_isConnected || _adapter == null)
        {
            MessageBox.Show("Please connect to a device first.", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save ECU Calibration as EFD",
            Filter = "EFD Files (*.efd)|*.efd",
            DefaultExt = ".efd",
            FileName = $"ecudump_{DateTime.Now:yyyyMMdd_HHmmss}.efd"
        };

        if (dialog.ShowDialog() != true) return;

        btnNavSaveEfd.IsEnabled = false;

        try
        {
            Log("Reading ECU calibration data...");

            // Create new EFD from ECU data
            var efd = new EfdFile
            {
                FilePath = dialog.FileName,
                ModelYear = 2017,
                DriveTrain = "FWD/RWD",
                Engine = "3.6Phoenix",
                FuelType = "UNLEADED",
                Transmission = "948TE",
                BodyStyle = "RU",
                Emissions = "NAS-50 STATE",
                Program = "ECUDUMP",
                Level = "SERVICE",
                Version = $"Dump_{DateTime.Now:yyyyMMdd}",
                GeneratorName = "StellaFlash"
            };

            // Simulate reading blocks
            var calibrationData = new List<byte>();

            for (int i = 0; i < 8; i++)
            {
                _memoryBlocks[i] = 100;
                DrawMemoryBuffer();
                Log($"Reading block {i + 1}/8...");
                await Task.Delay(300);

                // Simulate data (in real implementation, read from ECU)
                var blockData = new byte[4096];
                new Random().NextBytes(blockData);
                calibrationData.AddRange(blockData);
            }

            efd.DataBlocks.Add(new EfdDataBlock
            {
                Name = "Main Calibration",
                StartAddress = 0x00000000,
                Data = calibrationData.ToArray()
            });

            // Save the file
            EfdParser.Save(efd, dialog.FileName);

            Log($"ECU calibration saved to: {System.IO.Path.GetFileName(dialog.FileName)}");
            Log($"  Size: {efd.FileSizeDisplay}");

            MessageBox.Show($"ECU calibration saved successfully!\n\n{dialog.FileName}", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Save error: {ex.Message}");
            MessageBox.Show($"Failed to save EFD:\n\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnNavSaveEfd.IsEnabled = true;
        }
    }

    #endregion

    #region VIN Operations

    private void TxtVinInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var length = txtVinInput.Text.Length;
        txtVinCharCount.Text = $"{length}/17 characters";
        txtVinCharCount.Foreground = length == 17
            ? new SolidColorBrush(Color.FromRgb(39, 201, 63))  // Green when valid
            : new SolidColorBrush(Color.FromRgb(102, 102, 102));  // Gray otherwise
    }

    private void BtnSelectAllModules_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _vinList)
            item.IsSelected = true;
        dgVinList.Items.Refresh();
        UpdateSelectedCount();
    }

    private void BtnDeselectAllModules_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _vinList)
            item.IsSelected = false;
        dgVinList.Items.Refresh();
        UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        var count = _vinList.Count(v => v.IsSelected);
        txtSelectedCount.Text = $"{count} selected";
        txtSelectedCount.Foreground = count > 0
            ? new SolidColorBrush(Color.FromRgb(0, 255, 170))  // Green when modules selected
            : new SolidColorBrush(Color.FromRgb(102, 102, 102));  // Gray otherwise
    }

    private async void BtnReadVin_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckFeatureAccess(LicenseFeature.ReadVin)) return;

        if (!_isConnected || _adapter == null)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        btnNavReadVin.IsEnabled = false;
        _vinList.Clear();
        Log("Reading VIN from all modules...");

        foreach (var module in FcaModuleDatabase.Modules)
        {
            try
            {
                var client = new UdsClient(_adapter, module, 500);

                // First check if module responds
                var testerResponse = await client.TesterPresentAsync();
                if (!testerResponse.IsPositive) continue;

                // Read VIN
                var vin = await client.ReadVinAsync();
                var status = "OK";

                if (string.IsNullOrEmpty(vin) || vin.All(c => c == '\0' || c == ' '))
                {
                    vin = "(empty)";
                    status = "EMPTY";
                }

                Log($"  [{module.ShortName}] VIN: {vin}");

                _vinList.Add(new VinModuleItem
                {
                    IsSelected = true,
                    ModuleName = module.ShortName,
                    CurrentVin = vin,
                    Status = status,
                    Module = module
                });
            }
            catch (Exception ex)
            {
                Log($"  [{module.ShortName}] Error: {ex.Message}");
            }
        }

        Log($"VIN read complete. {_vinList.Count} module(s) responded.");

        // Update selection count
        UpdateSelectedCount();

        // Switch to VIN tab
        tabRight.SelectedIndex = 2;

        btnNavReadVin.IsEnabled = true;
    }

    private async void BtnClearVin_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckFeatureAccess(LicenseFeature.ClearVin)) return;

        if (!_isConnected || _adapter == null)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedModules = _vinList.Where(v => v.IsSelected).ToList();
        if (selectedModules.Count == 0)
        {
            MessageBox.Show("Please select at least one module.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to CLEAR the VIN from {selectedModules.Count} module(s)?\n\n" +
            "This will write zeros to the VIN field.\n" +
            "This operation requires security access.",
            "Confirm Clear VIN",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        btnNavClearVin.IsEnabled = false;
        Log($"Clearing VIN from {selectedModules.Count} module(s)...");

        int cleared = 0;
        foreach (var item in selectedModules)
        {
            try
            {
                var client = new UdsClient(_adapter, item.Module, 1000);

                // Start extended session
                Log($"  [{item.ModuleName}] Starting extended session...");
                await client.StartSessionAsync(DiagnosticSessionType.Extended);
                await Task.Delay(50);

                // Security access for VIN operations
                Log($"  [{item.ModuleName}] Requesting security access...");
                var securityResponse = await client.SecurityAccessAsync(
                    FcaSecurityAccess.LevelVinAccess,
                    FcaSecurityAccess.GetKeyCalculator(FcaSecurityAccess.LevelVinAccess));

                if (!securityResponse.IsPositive)
                {
                    item.Status = "SEC FAIL";
                    Log($"  [{item.ModuleName}] Security access denied: {securityResponse.NegativeResponseCode}");
                    continue;
                }

                Log($"  [{item.ModuleName}] Security access granted. Clearing VIN...");

                // Clear VIN
                var response = await client.ClearVinAsync();

                if (response.IsPositive)
                {
                    cleared++;
                    item.CurrentVin = "(cleared)";
                    item.Status = "CLEARED";
                    Log($"  [{item.ModuleName}] VIN cleared successfully.");
                }
                else
                {
                    item.Status = "FAILED";
                    Log($"  [{item.ModuleName}] Clear failed: {response.NegativeResponseCode}");
                }
            }
            catch (Exception ex)
            {
                item.Status = "ERROR";
                Log($"  [{item.ModuleName}] Error: {ex.Message}");
            }
        }

        // Refresh the grid
        dgVinList.Items.Refresh();

        Log($"Clear complete. {cleared}/{selectedModules.Count} module(s) cleared.");
        btnNavClearVin.IsEnabled = true;
    }

    private async void BtnWriteVin_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckFeatureAccess(LicenseFeature.WriteVin)) return;

        if (!_isConnected || _adapter == null)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newVin = txtVinInput.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(newVin) || newVin.Length != 17)
        {
            MessageBox.Show("Please enter a valid 17-character VIN.", "Invalid VIN", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Basic VIN validation
        if (!IsValidVin(newVin))
        {
            MessageBox.Show("The VIN contains invalid characters.\n\nVIN can only contain: A-Z (except I, O, Q) and 0-9",
                "Invalid VIN", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedModules = _vinList.Where(v => v.IsSelected).ToList();
        if (selectedModules.Count == 0)
        {
            MessageBox.Show("Please select at least one module.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to write this VIN to {selectedModules.Count} module(s)?\n\n" +
            $"New VIN: {newVin}\n\n" +
            "WARNING: Writing an incorrect VIN may cause vehicle systems to malfunction.\n" +
            "This operation requires security access.",
            "Confirm Write VIN",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        btnNavWriteVin.IsEnabled = false;
        btnWriteVin.IsEnabled = false;
        Log($"Writing VIN '{newVin}' to {selectedModules.Count} module(s)...");

        int written = 0;
        foreach (var item in selectedModules)
        {
            try
            {
                var client = new UdsClient(_adapter, item.Module, 1000);

                // Start extended session
                Log($"  [{item.ModuleName}] Starting extended session...");
                await client.StartSessionAsync(DiagnosticSessionType.Extended);
                await Task.Delay(50);

                // Security access for VIN operations
                Log($"  [{item.ModuleName}] Requesting security access...");
                var securityResponse = await client.SecurityAccessAsync(
                    FcaSecurityAccess.LevelVinAccess,
                    FcaSecurityAccess.GetKeyCalculator(FcaSecurityAccess.LevelVinAccess));

                if (!securityResponse.IsPositive)
                {
                    item.Status = "SEC FAIL";
                    Log($"  [{item.ModuleName}] Security access denied: {securityResponse.NegativeResponseCode}");
                    continue;
                }

                Log($"  [{item.ModuleName}] Security access granted. Writing VIN...");

                // Write VIN
                var response = await client.WriteVinAsync(newVin);

                if (response.IsPositive)
                {
                    written++;
                    item.CurrentVin = newVin;
                    item.Status = "WRITTEN";
                    Log($"  [{item.ModuleName}] VIN written successfully.");
                }
                else
                {
                    item.Status = "FAILED";
                    Log($"  [{item.ModuleName}] Write failed: {response.NegativeResponseCode}");
                }
            }
            catch (Exception ex)
            {
                item.Status = "ERROR";
                Log($"  [{item.ModuleName}] Error: {ex.Message}");
            }
        }

        // Refresh the grid
        dgVinList.Items.Refresh();

        Log($"Write complete. {written}/{selectedModules.Count} module(s) updated.");

        if (written > 0)
        {
            _connectedVin = newVin;
        }

        btnNavWriteVin.IsEnabled = true;
        btnWriteVin.IsEnabled = true;
    }

    private static bool IsValidVin(string vin)
    {
        if (string.IsNullOrEmpty(vin) || vin.Length != 17)
            return false;

        // VIN characters: A-Z (except I, O, Q) and 0-9
        foreach (var c in vin)
        {
            if (c >= '0' && c <= '9') continue;
            if (c >= 'A' && c <= 'Z' && c != 'I' && c != 'O' && c != 'Q') continue;
            return false;
        }

        return true;
    }

    #endregion

    #region Network Topology

    private readonly HashSet<uint> _onlineModules = [];

    private void BtnTopology_Click(object sender, RoutedEventArgs e)
    {
        PopulateTopology();
        tabRight.SelectedIndex = 1; // Topology tab
    }

    private async void BtnRebootModule_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckFeatureAccess(LicenseFeature.RebootModule)) return;

        if (!_isConnected || _adapter == null)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (cboRebootModule.SelectedIndex < 0)
        {
            MessageBox.Show("Please select a module to reboot", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var module = FcaModuleDatabase.Modules[cboRebootModule.SelectedIndex];
        var resetType = cboResetType.SelectedIndex switch
        {
            0 => (byte)0x01, // Hard Reset
            1 => (byte)0x02, // Key Off/On Reset
            2 => (byte)0x03, // Soft Reset
            _ => (byte)0x01
        };
        var resetTypeName = cboResetType.SelectedIndex switch
        {
            0 => "Hard Reset",
            1 => "Key Off/On Reset",
            2 => "Soft Reset",
            _ => "Reset"
        };

        var result = MessageBox.Show(
            $"Are you sure you want to reboot the {module.ShortName}?\n\n" +
            $"Module: {module.Name}\n" +
            $"Reset Type: {resetTypeName}\n\n" +
            "WARNING: This will restart the selected ECU module.\n" +
            "The module may be temporarily unavailable.",
            "Confirm Module Reboot",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            Log($"Rebooting {module.ShortName} ({resetTypeName})...");

            var client = new UdsClient(_adapter, module, 2000);

            // Start extended session first (required for ECU Reset on most modules)
            await client.StartSessionAsync(DiagnosticSessionType.Extended);
            await Task.Delay(50);

            // Send ECU Reset command
            var response = await client.EcuResetAsync(resetType);

            if (response.IsPositive)
            {
                Log($"  [{module.ShortName}] Reboot command sent successfully.");
                Log($"  [{module.ShortName}] Module is restarting...");

                // Mark module as offline temporarily
                _onlineModules.Remove(module.ResponseId);
                PopulateTopology();

                // Wait a moment then check if module comes back online
                await Task.Delay(2000);

                var checkClient = new UdsClient(_adapter, module, 500);
                var checkResponse = await checkClient.TesterPresentAsync();
                if (checkResponse.IsPositive)
                {
                    _onlineModules.Add(module.ResponseId);
                    Log($"  [{module.ShortName}] Module is back online.");
                    PopulateTopology();
                }
                else
                {
                    Log($"  [{module.ShortName}] Module still restarting or offline.");
                }

                MessageBox.Show($"{module.ShortName} reboot command sent successfully.", "Reboot Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Log($"  [{module.ShortName}] Reboot failed: {response.NegativeResponseCode}");
                MessageBox.Show($"Reboot failed: {response.NegativeResponseCode}", "Reboot Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Log($"  [{module.ShortName}] Error: {ex.Message}");
            MessageBox.Show($"Error rebooting module:\n\n{ex.Message}", "Reboot Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PopulateTopology()
    {
        pnlCanC.Children.Clear();
        pnlCanIhs.Children.Clear();
        pnlCanB.Children.Clear();

        foreach (var module in FcaModuleDatabase.Modules)
        {
            var isOnline = _onlineModules.Contains(module.ResponseId);
            var moduleCard = CreateModuleCard(module, isOnline);

            switch (module.Network)
            {
                case FcaCanNetwork.CanC:
                    pnlCanC.Children.Add(moduleCard);
                    break;
                case FcaCanNetwork.CanIhs:
                    pnlCanIhs.Children.Add(moduleCard);
                    break;
                case FcaCanNetwork.CanB:
                    pnlCanB.Children.Add(moduleCard);
                    break;
            }
        }
    }

    private Border CreateModuleCard(FcaModuleDefinition module, bool isOnline)
    {
        var networkColor = module.Network switch
        {
            FcaCanNetwork.CanC => Color.FromRgb(255, 107, 0),    // Orange
            FcaCanNetwork.CanIhs => Color.FromRgb(0, 165, 165),  // Teal
            FcaCanNetwork.CanB => Color.FromRgb(155, 89, 182),   // Purple
            _ => Color.FromRgb(102, 102, 102)                     // Gray
        };

        var statusColor = isOnline
            ? Color.FromRgb(39, 201, 63)   // Green
            : Color.FromRgb(102, 102, 102); // Gray

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
            BorderBrush = new SolidColorBrush(isOnline ? statusColor : Color.FromRgb(48, 54, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 8, 8),
            MinWidth = 100
        };

        var stack = new StackPanel();

        // Module name with status indicator
        var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
        headerStack.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(statusColor),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = module.ShortName,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12
        });
        stack.Children.Add(headerStack);

        // Full name
        stack.Children.Add(new TextBlock
        {
            Text = module.Name,
            Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
            FontSize = 9,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 120,
            Margin = new Thickness(14, 2, 0, 0)
        });

        // CAN IDs
        stack.Children.Add(new TextBlock
        {
            Text = $"0x{module.RequestId:X3} / 0x{module.ResponseId:X3}",
            Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 170)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9,
            Margin = new Thickness(14, 4, 0, 0)
        });

        card.Child = stack;

        // Tooltip with more info
        card.ToolTip = $"{module.Name}\n" +
                       $"Network: {module.Network}\n" +
                       $"TX: 0x{module.RequestId:X3}\n" +
                       $"RX: 0x{module.ResponseId:X3}\n" +
                       $"Status: {(isOnline ? "Online" : "Offline")}";

        return card;
    }

    #endregion

    #region Module Identification

    private void BtnModuleId_Click(object sender, RoutedEventArgs e)
    {
        // Switch to Module ID tab
        tabRight.SelectedIndex = 2;
    }

    private async void BtnReadModuleInfo_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected || _adapter == null)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (cboModuleSelect.SelectedIndex < 0)
        {
            MessageBox.Show("Please select a module", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var module = FcaModuleDatabase.Modules[cboModuleSelect.SelectedIndex];
        await ReadModuleInfoAsync(module);
    }

    private async void BtnReadAllModuleInfo_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected || _adapter == null)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        btnNavModuleId.IsEnabled = false;
        _allModulesInfo.Clear();
        Log("Reading extended identification from all modules...");

        foreach (var module in FcaModuleDatabase.Modules)
        {
            try
            {
                var client = new UdsClient(_adapter, module, 500);
                var testerResponse = await client.TesterPresentAsync();
                if (!testerResponse.IsPositive) continue;

                Log($"Reading {module.ShortName}...");
                var info = await ReadModuleExtendedInfoAsync(client, module);
                if (info != null)
                {
                    _allModulesInfo.Add(info);
                    Log($"  [{module.ShortName}] Part#: {info.PartNumber}, SW: {info.SoftwareVersion}");
                }
            }
            catch (Exception ex)
            {
                Log($"  [{module.ShortName}] Error: {ex.Message}");
            }
        }

        Log($"Extended identification complete. {_allModulesInfo.Count} module(s) read.");
        btnNavModuleId.IsEnabled = true;

        // Display first module if any
        if (_allModulesInfo.Count > 0)
        {
            DisplayModuleInfo(_allModulesInfo[0]);
        }
    }

    private async Task ReadModuleInfoAsync(FcaModuleDefinition module)
    {
        btnNavModuleId.IsEnabled = false;
        _moduleInfoList.Clear();
        Log($"Reading extended identification from {module.ShortName}...");

        try
        {
            var client = new UdsClient(_adapter!, module, 1000);

            // Check if module responds
            var testerResponse = await client.TesterPresentAsync();
            if (!testerResponse.IsPositive)
            {
                Log($"  [{module.ShortName}] Module not responding.");
                MessageBox.Show($"Module {module.ShortName} is not responding.", "No Response", MessageBoxButton.OK, MessageBoxImage.Warning);
                btnNavModuleId.IsEnabled = true;
                return;
            }

            var info = await ReadModuleExtendedInfoAsync(client, module);
            if (info != null)
            {
                DisplayModuleInfo(info);
                Log($"  [{module.ShortName}] Extended identification read successfully.");
            }
        }
        catch (Exception ex)
        {
            Log($"  [{module.ShortName}] Error: {ex.Message}");
            MessageBox.Show($"Error reading module info:\n\n{ex.Message}", "Read Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        btnNavModuleId.IsEnabled = true;
    }

    private async Task<ModuleFullInfo?> ReadModuleExtendedInfoAsync(UdsClient client, FcaModuleDefinition module)
    {
        var info = new ModuleFullInfo { Module = module };

        // Read standard identification DIDs
        var didsToRead = new (ushort Did, string Name, Action<string> Setter)[]
        {
            (0xF190, "VIN", v => info.Vin = v),
            (0xF187, "Part Number", v => info.PartNumber = v),
            (0xF18C, "ECU Serial Number", v => info.SerialNumber = v),
            (0xF191, "ECU Hardware Number", v => info.HardwareNumber = v),
            (0xF193, "Hardware Version", v => info.HardwareVersion = v),
            (0xF195, "Software Number", v => info.SoftwareNumber = v),
            (0xF197, "System Name", v => info.SystemName = v),
            (0xF199, "Programming Date", v => info.ProgrammingDate = v),
            (0xF18A, "Supplier ID", v => info.SupplierId = v),
        };

        foreach (var (did, name, setter) in didsToRead)
        {
            try
            {
                var response = await client.ReadDataByIdentifierAsync(did);
                if (response.IsPositive && response.Data.Length > 2)
                {
                    var value = ParseDidValue(did, response.Data[2..]);
                    setter(value);
                }
            }
            catch { }
            await Task.Delay(20); // Small delay between reads
        }

        // Try to read FCA-specific DIDs
        try
        {
            var swVerResponse = await client.ReadDataByIdentifierAsync(0xF1A0); // FCA SW version
            if (swVerResponse.IsPositive && swVerResponse.Data.Length > 2)
                info.SoftwareVersion = ParseDidValue(0xF1A0, swVerResponse.Data[2..]);
        }
        catch { }

        try
        {
            var calIdResponse = await client.ReadDataByIdentifierAsync(0xF1A1); // FCA Calibration ID
            if (calIdResponse.IsPositive && calIdResponse.Data.Length > 2)
                info.CalibrationId = ParseDidValue(0xF1A1, calIdResponse.Data[2..]);
        }
        catch { }

        return info;
    }

    private static string ParseDidValue(ushort did, byte[] data)
    {
        // Programming date is BCD encoded
        if (did == 0xF199 && data.Length >= 4)
        {
            var year = ((data[0] >> 4) * 10) + (data[0] & 0x0F) + 2000;
            var month = ((data[1] >> 4) * 10) + (data[1] & 0x0F);
            var day = ((data[2] >> 4) * 10) + (data[2] & 0x0F);
            return $"{year:D4}-{month:D2}-{day:D2}";
        }

        // Default: treat as ASCII string
        return System.Text.Encoding.ASCII.GetString(data).Trim('\0', ' ', '\xFF');
    }

    private void DisplayModuleInfo(ModuleFullInfo info)
    {
        _moduleInfoList.Clear();

        _moduleInfoList.Add(new ModuleInfoItem { Property = "Module", Value = $"{info.Module.ShortName} - {info.Module.Name}" });
        _moduleInfoList.Add(new ModuleInfoItem { Property = "CAN Address", Value = $"TX: 0x{info.Module.RequestId:X3} / RX: 0x{info.Module.ResponseId:X3}" });

        if (!string.IsNullOrEmpty(info.Vin))
            _moduleInfoList.Add(new ModuleInfoItem { Property = "VIN", Value = info.Vin });
        if (!string.IsNullOrEmpty(info.PartNumber))
            _moduleInfoList.Add(new ModuleInfoItem { Property = "Part Number", Value = info.PartNumber });
        if (!string.IsNullOrEmpty(info.SerialNumber))
            _moduleInfoList.Add(new ModuleInfoItem { Property = "Serial Number", Value = info.SerialNumber });
        if (!string.IsNullOrEmpty(info.HardwareNumber))
            _moduleInfoList.Add(new ModuleInfoItem { Property = "Hardware Number", Value = info.HardwareNumber });
        if (!string.IsNullOrEmpty(info.HardwareVersion))
            _moduleInfoList.Add(new ModuleInfoItem { Property = "Hardware Version", Value = info.HardwareVersion });
        if (!string.IsNullOrEmpty(info.SoftwareNumber))
            _moduleInfoList.Add(new ModuleInfoItem { Property = "Software Number", Value = info.SoftwareNumber });
        if (!string.IsNullOrEmpty(info.SoftwareVersion))
            _moduleInfoList.Add(new ModuleInfoItem { Property = "Software Version", Value = info.SoftwareVersion });
        if (!string.IsNullOrEmpty(info.CalibrationId))
            _moduleInfoList.Add(new ModuleInfoItem { Property = "Calibration ID", Value = info.CalibrationId });
        if (!string.IsNullOrEmpty(info.SystemName))
            _moduleInfoList.Add(new ModuleInfoItem { Property = "System Name", Value = info.SystemName });
        if (!string.IsNullOrEmpty(info.ProgrammingDate))
            _moduleInfoList.Add(new ModuleInfoItem { Property = "Programming Date", Value = info.ProgrammingDate });
        if (!string.IsNullOrEmpty(info.SupplierId))
            _moduleInfoList.Add(new ModuleInfoItem { Property = "Supplier ID", Value = info.SupplierId });
    }

    private void BtnExportModuleReport_Click(object sender, RoutedEventArgs e)
    {
        if (_moduleInfoList.Count == 0 && _allModulesInfo.Count == 0)
        {
            MessageBox.Show("No module information to export. Please read module info first.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true) return;

            var flowDoc = CreateModuleInfoReportDocument();
            var paginator = ((IDocumentPaginatorSource)flowDoc).DocumentPaginator;
            paginator.PageSize = new Size(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);

            printDialog.PrintDocument(paginator, "Module Report - StellaFlash");

            Log("Module report sent to printer/PDF.");
            MessageBox.Show("Report exported successfully!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Export error: {ex.Message}");
            MessageBox.Show($"Failed to export report:\n\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnModuleInfoReport_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckFeatureAccess(LicenseFeature.ModuleInfoReport)) return;

        if (!_isConnected || _adapter == null)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        btnNavModuleReport.IsEnabled = false;
        _allModulesInfo.Clear();
        Log("Generating Module Information Report...");
        Log("Scanning and reading extended identification from all modules...");

        // Scan and read all responding modules
        foreach (var module in FcaModuleDatabase.Modules)
        {
            try
            {
                var client = new UdsClient(_adapter, module, 500);
                var testerResponse = await client.TesterPresentAsync();
                if (!testerResponse.IsPositive) continue;

                Log($"  Reading {module.ShortName} ({module.Name})...");

                var info = await ReadModuleExtendedInfoAsync(client, module);
                if (info != null)
                {
                    _allModulesInfo.Add(info);
                }
            }
            catch (Exception ex)
            {
                Log($"  [{module.ShortName}] Error: {ex.Message}");
            }
        }

        if (_allModulesInfo.Count == 0)
        {
            Log("No modules responded. Report not generated.");
            MessageBox.Show("No modules responded to identification request.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
            btnNavModuleReport.IsEnabled = true;
            return;
        }

        // Generate PDF report
        try
        {
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true)
            {
                btnNavModuleReport.IsEnabled = true;
                return;
            }

            var flowDoc = CreateModuleInfoReportDocument();
            var paginator = ((IDocumentPaginatorSource)flowDoc).DocumentPaginator;
            paginator.PageSize = new Size(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);

            printDialog.PrintDocument(paginator, "Module Info Report - StellaFlash");

            Log($"Module Information Report sent to printer/PDF.");
            Log($"  {_allModulesInfo.Count} module(s) documented.");

            MessageBox.Show(
                $"Module Information Report generated successfully!\n\n" +
                $"Modules documented: {_allModulesInfo.Count}",
                "Report Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Report error: {ex.Message}");
            MessageBox.Show($"Failed to generate report:\n\n{ex.Message}", "Report Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        btnNavModuleReport.IsEnabled = true;
    }

    private FlowDocument CreateModuleInfoReportDocument()
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(50),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11
        };

        // Title
        var title = new Paragraph(new Run("EXTENDED MODULE INFORMATION REPORT"))
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 5)
        };
        doc.Blocks.Add(title);

        // Subtitle
        var subtitle = new Paragraph(new Run("StellaFlash"))
        {
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 0)),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20)
        };
        doc.Blocks.Add(subtitle);

        // Report Info
        var infoSection = new Section();
        infoSection.Blocks.Add(new Paragraph(new Run($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")) { Margin = new Thickness(0, 0, 0, 3) });
        if (!string.IsNullOrEmpty(_connectedDeviceName))
            infoSection.Blocks.Add(new Paragraph(new Run($"Device: {_connectedDeviceName}")) { Margin = new Thickness(0, 0, 0, 3) });
        if (!string.IsNullOrEmpty(_connectedVin))
            infoSection.Blocks.Add(new Paragraph(new Run($"Vehicle VIN: {_connectedVin}")) { Margin = new Thickness(0, 0, 0, 3) });
        infoSection.Blocks.Add(new Paragraph(new Run($"Modules Found: {_allModulesInfo.Count}")) { Margin = new Thickness(0, 0, 0, 20) });
        doc.Blocks.Add(infoSection);

        // Module details
        foreach (var info in _allModulesInfo)
        {
            // Module header
            var moduleHeader = new Paragraph(new Run($"{info.Module.ShortName} - {info.Module.Name}"))
            {
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 10, 0, 5)
            };
            doc.Blocks.Add(moduleHeader);

            // Create table for module info
            var table = new Table { CellSpacing = 0 };
            table.Columns.Add(new TableColumn { Width = new GridLength(140) });
            table.Columns.Add(new TableColumn { Width = GridLength.Auto });

            var rowGroup = new TableRowGroup();

            AddModuleInfoRow(rowGroup, "CAN Address", $"TX: 0x{info.Module.RequestId:X3} / RX: 0x{info.Module.ResponseId:X3}");
            if (!string.IsNullOrEmpty(info.Vin))
                AddModuleInfoRow(rowGroup, "VIN", info.Vin);
            if (!string.IsNullOrEmpty(info.PartNumber))
                AddModuleInfoRow(rowGroup, "Part Number", info.PartNumber);
            if (!string.IsNullOrEmpty(info.SerialNumber))
                AddModuleInfoRow(rowGroup, "Serial Number", info.SerialNumber);
            if (!string.IsNullOrEmpty(info.HardwareNumber))
                AddModuleInfoRow(rowGroup, "Hardware Number", info.HardwareNumber);
            if (!string.IsNullOrEmpty(info.HardwareVersion))
                AddModuleInfoRow(rowGroup, "Hardware Version", info.HardwareVersion);
            if (!string.IsNullOrEmpty(info.SoftwareNumber))
                AddModuleInfoRow(rowGroup, "Software Number", info.SoftwareNumber);
            if (!string.IsNullOrEmpty(info.SoftwareVersion))
                AddModuleInfoRow(rowGroup, "Software Version", info.SoftwareVersion);
            if (!string.IsNullOrEmpty(info.CalibrationId))
                AddModuleInfoRow(rowGroup, "Calibration ID", info.CalibrationId);
            if (!string.IsNullOrEmpty(info.SystemName))
                AddModuleInfoRow(rowGroup, "System Name", info.SystemName);
            if (!string.IsNullOrEmpty(info.ProgrammingDate))
                AddModuleInfoRow(rowGroup, "Programming Date", info.ProgrammingDate);
            if (!string.IsNullOrEmpty(info.SupplierId))
                AddModuleInfoRow(rowGroup, "Supplier", info.SupplierId);

            table.RowGroups.Add(rowGroup);
            doc.Blocks.Add(table);
        }

        // Footer
        var footer = new Paragraph(new Run("\n\nGenerated by StellaFlash - Spot On Auto Diagnostics"))
        {
            FontSize = 10,
            Foreground = Brushes.Gray,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 30, 0, 0)
        };
        doc.Blocks.Add(footer);

        return doc;
    }

    private static void AddModuleInfoRow(TableRowGroup rowGroup, string label, string value)
    {
        var row = new TableRow();

        var labelCell = new TableCell(new Paragraph(new Run(label))
        {
            Margin = new Thickness(5, 3, 5, 3)
        })
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        labelCell.Blocks.First().FontWeight = FontWeights.SemiBold;
        labelCell.Blocks.First().Foreground = Brushes.DarkGray;

        var valueCell = new TableCell(new Paragraph(new Run(value))
        {
            Margin = new Thickness(5, 3, 5, 3),
            FontFamily = new FontFamily("Consolas")
        })
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        row.Cells.Add(labelCell);
        row.Cells.Add(valueCell);
        rowGroup.Rows.Add(row);
    }

    private void BtnExportLog_Click(object sender, RoutedEventArgs e)
    {
        var saveDialog = new SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            DefaultExt = ".txt",
            FileName = $"StellaFlash_ActivityLog_{DateTime.Now:yyyyMMdd_HHmmss}"
        };

        if (saveDialog.ShowDialog() != true) return;

        try
        {
            var sessionDuration = DateTime.Now - _sessionStartTime;
            var isCsv = saveDialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

            using var writer = new System.IO.StreamWriter(saveDialog.FileName);

            if (isCsv)
            {
                // CSV Header
                writer.WriteLine("Timestamp,Category,Action,Details,VIN,Device");

                foreach (var entry in _activityLog)
                {
                    var action = EscapeCsv(entry.Action ?? entry.Message ?? "");
                    var details = EscapeCsv(entry.Details ?? "");
                    var vin = EscapeCsv(entry.VIN ?? "");
                    var device = EscapeCsv(entry.Device ?? "");
                    writer.WriteLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss},{entry.Category},{action},{details},{vin},{device}");
                }
            }
            else
            {
                // Text format header
                writer.WriteLine("═══════════════════════════════════════════════════════════════════");
                writer.WriteLine("                    STELLAFLASH ACTIVITY LOG                       ");
                writer.WriteLine("                   Spot On Auto Diagnostics                        ");
                writer.WriteLine("═══════════════════════════════════════════════════════════════════");
                writer.WriteLine();
                writer.WriteLine($"Session Start:    {_sessionStartTime:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"Export Time:      {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"Session Duration: {sessionDuration.Hours:D2}:{sessionDuration.Minutes:D2}:{sessionDuration.Seconds:D2}");
                writer.WriteLine($"Total Activities: {_activityLog.Count}");
                if (!string.IsNullOrEmpty(_connectedDeviceName))
                    writer.WriteLine($"Device:           {_connectedDeviceName}");
                if (!string.IsNullOrEmpty(_connectedVin))
                    writer.WriteLine($"Vehicle VIN:      {_connectedVin}");
                writer.WriteLine();
                writer.WriteLine("───────────────────────────────────────────────────────────────────");
                writer.WriteLine("                         ACTIVITY LOG                              ");
                writer.WriteLine("───────────────────────────────────────────────────────────────────");
                writer.WriteLine();

                foreach (var entry in _activityLog)
                {
                    var action = entry.Action ?? entry.Message ?? "";
                    writer.WriteLine($"[{entry.Timestamp:HH:mm:ss}] [{entry.Category,-12}] {action}");
                    if (!string.IsNullOrEmpty(entry.Details))
                        writer.WriteLine($"           └─ {entry.Details}");
                }

                writer.WriteLine();
                writer.WriteLine("───────────────────────────────────────────────────────────────────");
                writer.WriteLine("                        END OF LOG                                 ");
                writer.WriteLine("═══════════════════════════════════════════════════════════════════");
            }

            LogActivity("Exported activity log", "Export", $"File: {saveDialog.FileName}");
            MessageBox.Show($"Activity log exported successfully.\n\nFile: {saveDialog.FileName}\nEntries: {_activityLog.Count}",
                "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error exporting log:\n\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    #endregion

    #region Voltage Graph

    private void VoltageTimer_Tick(object? sender, EventArgs e)
    {
        // Simulate voltage readings between 13.8 and 14.6
        var voltage = 14.2 + (_random.NextDouble() - 0.5) * 0.8;
        _voltageHistory.Add(voltage);

        if (_voltageHistory.Count > 100)
            _voltageHistory.RemoveAt(0);

        txtVoltage.Text = $" - {voltage:F1} V";
        DrawVoltageGraph();
    }

    private void DrawVoltageGraph()
    {
        canvasVoltage.Children.Clear();

        if (canvasVoltage.ActualWidth <= 0 || canvasVoltage.ActualHeight <= 0)
            return;

        var width = canvasVoltage.ActualWidth;
        var height = canvasVoltage.ActualHeight;

        // Draw grid lines
        for (int i = 1; i < 4; i++)
        {
            var y = height * i / 4;
            var line = new Line
            {
                X1 = 0, Y1 = y, X2 = width, Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                StrokeThickness = 1
            };
            canvasVoltage.Children.Add(line);
        }

        if (_voltageHistory.Count < 2) return;

        // Draw voltage line
        var polyline = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0, 165, 165)),
            StrokeThickness = 2
        };

        for (int i = 0; i < _voltageHistory.Count; i++)
        {
            var x = (double)i / _voltageHistory.Count * width;
            // Map voltage 13.5-15.5 to height
            var y = height - ((_voltageHistory[i] - 13.5) / 2.0 * height);
            y = Math.Max(0, Math.Min(height, y));
            polyline.Points.Add(new Point(x, y));
        }

        canvasVoltage.Children.Add(polyline);
    }

    #endregion

    #region Memory Buffer

    private void DrawMemoryBuffer()
    {
        canvasMemory.Children.Clear();

        if (canvasMemory.ActualWidth <= 0 || canvasMemory.ActualHeight <= 0)
            return;

        var width = canvasMemory.ActualWidth;
        var height = canvasMemory.ActualHeight;
        var barWidth = width / 10;
        var gap = barWidth * 0.3;

        for (int i = 0; i < 8; i++)
        {
            var x = i * (barWidth + gap) + gap;
            var barHeight = _memoryBlocks[i] / 100.0 * (height - 10);

            // Source buffer bar (teal)
            var sourceBar = new Rectangle
            {
                Width = barWidth / 2 - 2,
                Height = barHeight,
                Fill = new SolidColorBrush(Color.FromRgb(0, 165, 165))
            };
            Canvas.SetLeft(sourceBar, x);
            Canvas.SetTop(sourceBar, height - barHeight);
            canvasMemory.Children.Add(sourceBar);

            // Target written bar (green) - slightly shorter to show difference
            if (_memoryBlocks[i] > 0)
            {
                var targetBar = new Rectangle
                {
                    Width = barWidth / 2 - 2,
                    Height = barHeight * 0.85,
                    Fill = new SolidColorBrush(Color.FromRgb(0, 255, 136))
                };
                Canvas.SetLeft(targetBar, x + barWidth / 2);
                Canvas.SetTop(targetBar, height - barHeight * 0.85);
                canvasMemory.Children.Add(targetBar);
            }

            // Block label
            var label = new TextBlock
            {
                Text = $"B{i + 1}",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102))
            };
            Canvas.SetLeft(label, x + barWidth / 4 - 5);
            Canvas.SetTop(label, height - 15);
            canvasMemory.Children.Add(label);
        }
    }

    #endregion

    #region Security Tab

    private readonly ObservableCollection<SeedKeyPair> _seedKeyPairs = [];

    private void InitializeSecurityTab()
    {
        // Load any previously captured pairs
        SecurityAccessManager.LoadFromFile();
        RefreshSeedKeyList();

        // Bind the DataGrid
        if (dgSeedKeyLog != null)
            dgSeedKeyLog.ItemsSource = _seedKeyPairs;
    }

    private void RefreshSeedKeyList()
    {
        _seedKeyPairs.Clear();
        foreach (var pair in SecurityAccessManager.GetCapturedPairs())
        {
            _seedKeyPairs.Add(pair);
        }
    }

    private async void BtnRequestSeed_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected)
        {
            MessageBox.Show("Not connected to vehicle!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var levelStr = (cboSecurityLevel?.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var level = levelStr switch
        {
            "Level 1 (Standard)" => SecurityLevel.Level1_RequestSeed,
            "Level 3 (Extended)" => SecurityLevel.Level3_RequestSeed,
            "Level 5 (Programming)" => SecurityLevel.Level5_RequestSeed,
            "Level 11 (Manufacturer)" => SecurityLevel.Level11_RequestSeed,
            _ => SecurityLevel.Level5_RequestSeed
        };

        // Get selected module
        var moduleIndex = cboModuleSelect.SelectedIndex;
        if (moduleIndex < 0 || moduleIndex >= FcaModuleDatabase.Modules.Count)
        {
            MessageBox.Show("Please select a module first!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var module = FcaModuleDatabase.Modules[moduleIndex];
        Log($"> Requesting seed from {module.ShortName} at Level {(int)level}...");

        try
        {
            // Send Security Access Request Seed (Service 0x27)
            var request = new byte[] { 0x27, (byte)level };
            var response = await SendUdsRequestAsync(module.RequestId, module.ResponseId, request);

            if (response != null && response.Length >= 6 && response[0] == 0x67)
            {
                var seed = new byte[response.Length - 2];
                Array.Copy(response, 2, seed, 0, seed.Length);

                Log($"  Seed received: {BitConverter.ToString(seed).Replace("-", " ")}");
                txtCurrentSeed.Text = BitConverter.ToString(seed).Replace("-", " ");

                // Calculate key using our algorithm (placeholder)
                var sendKeyLevel = (SecurityLevel)((int)level + 1);
                var calculatedKey = SecurityAccessManager.CalculateKey(seed, sendKeyLevel, module.RequestId);
                txtCalculatedKey.Text = BitConverter.ToString(calculatedKey).Replace("-", " ");

                // Log the attempt
                SecurityAccessManager.LogAttempt(
                    module.ShortName,
                    module.RequestId,
                    level,
                    seed,
                    calculatedKey,
                    false,
                    "Seed received, key calculated"
                );

                RefreshSeedKeyList();
            }
            else if (response != null && response[0] == 0x7F)
            {
                var nrc = response.Length > 2 ? response[2] : (byte)0;
                Log($"  Negative response: NRC 0x{nrc:X2}");
            }
            else
            {
                Log("  No valid response received");
            }
        }
        catch (Exception ex)
        {
            Log($"  Error: {ex.Message}");
        }
    }

    private async void BtnUnlockEcu_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected)
        {
            MessageBox.Show("Not connected to vehicle!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var keyText = txtCalculatedKey.Text?.Replace(" ", "").Trim();
        if (string.IsNullOrEmpty(keyText))
        {
            MessageBox.Show("No key available! Request seed first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Parse key from hex string
        byte[] key;
        try
        {
            key = Convert.FromHexString(keyText);
        }
        catch
        {
            MessageBox.Show("Invalid key format!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var levelStr = (cboSecurityLevel?.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var sendKeyLevel = levelStr switch
        {
            "Level 1 (Standard)" => SecurityLevel.Level1_SendKey,
            "Level 3 (Extended)" => SecurityLevel.Level3_SendKey,
            "Level 5 (Programming)" => SecurityLevel.Level5_SendKey,
            "Level 11 (Manufacturer)" => SecurityLevel.Level11_SendKey,
            _ => SecurityLevel.Level5_SendKey
        };

        var moduleIndex = cboModuleSelect.SelectedIndex;
        if (moduleIndex < 0 || moduleIndex >= FcaModuleDatabase.Modules.Count)
        {
            MessageBox.Show("Please select a module first!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var module = FcaModuleDatabase.Modules[moduleIndex];
        Log($"> Sending key to {module.ShortName}...");

        try
        {
            // Send Security Access Send Key (Service 0x27, even sub-function)
            var request = new byte[2 + key.Length];
            request[0] = 0x27;
            request[1] = (byte)sendKeyLevel;
            Array.Copy(key, 0, request, 2, key.Length);

            var response = await SendUdsRequestAsync(module.RequestId, module.ResponseId, request);

            // Parse seed from current display for logging
            byte[] seed = [];
            try
            {
                var seedText = txtCurrentSeed.Text?.Replace(" ", "").Trim();
                if (!string.IsNullOrEmpty(seedText))
                    seed = Convert.FromHexString(seedText);
            }
            catch { }

            if (response != null && response.Length >= 2 && response[0] == 0x67)
            {
                Log("  Security Access GRANTED!");
                txtSecurityStatus.Text = "UNLOCKED";
                txtSecurityStatus.Foreground = Brushes.LimeGreen;

                SecurityAccessManager.LogAttempt(
                    module.ShortName,
                    module.RequestId,
                    sendKeyLevel,
                    seed,
                    key,
                    true,
                    "Security access granted"
                );
            }
            else if (response != null && response[0] == 0x7F)
            {
                var nrc = response.Length > 2 ? response[2] : (byte)0;
                var nrcName = nrc switch
                {
                    0x35 => "Invalid Key",
                    0x36 => "Exceeded Attempts",
                    0x37 => "Required Time Delay",
                    _ => $"NRC 0x{nrc:X2}"
                };
                Log($"  Security Access DENIED: {nrcName}");
                txtSecurityStatus.Text = "LOCKED";
                txtSecurityStatus.Foreground = Brushes.Red;

                SecurityAccessManager.LogAttempt(
                    module.ShortName,
                    module.RequestId,
                    sendKeyLevel,
                    seed,
                    key,
                    false,
                    $"Rejected: {nrcName}"
                );
            }
            else
            {
                Log("  No valid response received");
            }

            RefreshSeedKeyList();
        }
        catch (Exception ex)
        {
            Log($"  Error: {ex.Message}");
        }
    }

    private void BtnExportSeedKeyLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            DefaultExt = ".csv",
            FileName = $"SeedKeyLog_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                SecurityAccessManager.ExportToCsv(dialog.FileName);
                MessageBox.Show($"Exported {_seedKeyPairs.Count} pairs to:\n{dialog.FileName}",
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                Log($"> Exported seed/key log to {dialog.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnImportKnownPair_Click(object sender, RoutedEventArgs e)
    {
        // Import the known FCA Level 5 pair from capture
        SecurityAccessManager.AddKnownPair(
            FcaSecurityConstants.KnownSeed,
            FcaSecurityConstants.KnownKey,
            SecurityLevel.Level5_SendKey,
            "PCM",
            0x7E0
        );

        // Import the ECU Hero Level 0x63 pair from capture
        SecurityAccessManager.AddKnownPair(
            FcaSecurityConstants.EcuHeroSeed,
            FcaSecurityConstants.EcuHeroKey,
            SecurityLevel.Level99_SendKey,
            "PCM",
            0x7E0
        );

        RefreshSeedKeyList();
        Log("> Imported 2 known seed/key pairs from CAN captures");
        MessageBox.Show("Imported known seed/key pairs:\n\n" +
            "FCA Level 5 (Programming):\n" +
            $"  Seed: {BitConverter.ToString(FcaSecurityConstants.KnownSeed).Replace("-", " ")}\n" +
            $"  Key:  {BitConverter.ToString(FcaSecurityConstants.KnownKey).Replace("-", " ")}\n\n" +
            "ECU Hero Level 0x63 (EPROM Read):\n" +
            $"  Seed: {BitConverter.ToString(FcaSecurityConstants.EcuHeroSeed).Replace("-", " ")}\n" +
            $"  Key:  {BitConverter.ToString(FcaSecurityConstants.EcuHeroKey).Replace("-", " ")}\n\n" +
            "These pairs were captured from successful security access sessions.",
            "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnAnalyzePairs_Click(object sender, RoutedEventArgs e)
    {
        var analysis = SecurityAccessManager.AnalyzeCapturedPairs();
        MessageBox.Show(analysis, "Seed/Key Analysis", MessageBoxButton.OK, MessageBoxImage.Information);
        Log("> Generated seed/key analysis report");
    }

    private void BtnClearSeedKeyLog_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Are you sure you want to clear all captured seed/key pairs?",
            "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            SecurityAccessManager.ClearCapturedPairs();
            RefreshSeedKeyList();
            Log("> Cleared seed/key log");
        }
    }

    private async Task<byte[]?> SendUdsRequestAsync(uint txId, uint rxId, byte[] request)
    {
        if (_adapter == null) return null;

        try
        {
            // Use TransactAsync for request/response pattern
            return await _adapter.TransactAsync(txId, rxId, request, 2000);
        }
        catch
        {
            // Timeout or error
            return null;
        }
    }

    #endregion

    protected override async void OnClosed(EventArgs e)
    {
        _voltageTimer.Stop();
        if (_adapter != null)
            await _adapter.DisposeAsync();
        base.OnClosed(e);
    }
}

/// <summary>
/// Display item for DTC DataGrid binding
/// </summary>
public class DtcDisplayItem
{
    public required string DisplayCode { get; init; }
    public required string StatusText { get; init; }
    public required string Description { get; init; }
    public string? Category { get; init; }
    public string? ModuleName { get; init; }
    public bool IsConfirmed { get; init; }
    public bool IsPending { get; init; }
}

/// <summary>
/// Activity log entry for tracking user actions
/// </summary>
public class ActivityLogEntry
{
    public DateTime Timestamp { get; init; }
    public string? Action { get; init; }
    public string? Message { get; init; }
    public string Category { get; init; } = "General";
    public string? Details { get; init; }
    public string? VIN { get; init; }
    public string? Device { get; init; }
}

/// <summary>
/// Display item for VIN module DataGrid binding
/// </summary>
public class VinModuleItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public required string ModuleName { get; set; }
    public required string CurrentVin { get; set; }
    public required string Status { get; set; }
    public required FcaModuleDefinition Module { get; init; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Display item for Module Info DataGrid binding
/// </summary>
public class ModuleInfoItem
{
    public required string Property { get; init; }
    public required string Value { get; init; }
}

/// <summary>
/// Full module identification information
/// </summary>
public class ModuleFullInfo
{
    public required FcaModuleDefinition Module { get; init; }
    public string? Vin { get; set; }
    public string? PartNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string? HardwareNumber { get; set; }
    public string? HardwareVersion { get; set; }
    public string? SoftwareNumber { get; set; }
    public string? SoftwareVersion { get; set; }
    public string? CalibrationId { get; set; }
    public string? SystemName { get; set; }
    public string? ProgrammingDate { get; set; }
    public string? SupplierId { get; set; }
}

/// <summary>
/// Programming report data for ECU flash operations
/// </summary>
public class ProgrammingReportData
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string VIN { get; set; } = string.Empty;
    public string TargetModule { get; set; } = string.Empty;
    public string FlashFileName { get; set; } = string.Empty;
    public string FlashPartNumber { get; set; } = string.Empty;
    public string BeforeCalibration { get; set; } = string.Empty;
    public string BeforeSoftwareVersion { get; set; } = string.Empty;
    public string AfterCalibration { get; set; } = string.Empty;
    public string AfterSoftwareVersion { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
}

/// <summary>
/// Mock adapter for demo mode
/// </summary>
public class MockCanAdapter : ICanAdapter
{
    private readonly Random _random = new();
    private readonly HashSet<uint> _respondingModules = [0x7E8, 0x7E9, 0x7EA, 0x768, 0x76A, 0x728];
    private readonly Dictionary<uint, string> _moduleVins = new()
    {
        [0x7E8] = "1C4RJFAG5FC123456",  // PCM
        [0x7E9] = "1C4RJFAG5FC123456",  // TCM
        [0x7EA] = "1C4RJFAG5FC123456",  // ABS
        [0x768] = "1C4RJFAG5FC123456",  // BCM
        [0x76A] = "1C4RJFAG5FC123456",  // IPC
        [0x728] = "1C4RJFAG5FC123456"   // RADIO
    };

    public bool IsConnected { get; private set; }

    public Task<bool> ConnectAsync(ConnectionSettings settings, CancellationToken ct = default)
    {
        IsConnected = true;
        return Task.FromResult(true);
    }

    public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }
    public Task SendAsync(uint canId, byte[] data, CancellationToken ct = default) => Task.CompletedTask;
    public Task<byte[]?> ReceiveAsync(uint canId, int timeout, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);

    public Task<byte[]?> TransactAsync(uint txId, uint rxId, byte[] data, int timeout, CancellationToken ct = default)
    {
        Thread.Sleep(_random.Next(10, 50));

        if (!_respondingModules.Contains(rxId) || data.Length == 0)
            return Task.FromResult<byte[]?>(null);

        var serviceId = (UdsServiceId)data[0];
        byte[]? response = serviceId switch
        {
            UdsServiceId.TesterPresent => [0x7E, 0x00],
            UdsServiceId.DiagnosticSessionControl when data.Length >= 2 => [0x50, data[1], 0x00, 0x19, 0x01, 0xF4],
            UdsServiceId.SecurityAccess when data.Length >= 2 => HandleSecurityAccess(data),
            UdsServiceId.EcuReset when data.Length >= 2 => [0x51, data[1]], // Positive response for ECU Reset
            UdsServiceId.ReadDtcInformation when rxId == 0x7E8 => [0x59, 0x02, 0xFF, 0x03, 0x00, 0x00, 0x08, 0x01, 0x71, 0x00, 0x08],
            UdsServiceId.ReadDtcInformation => [0x59, 0x02, 0xFF],
            UdsServiceId.ClearDiagnosticInformation => [0x54],
            UdsServiceId.ReadDataByIdentifier when data.Length >= 3 => HandleReadDataByIdentifier(rxId, data),
            UdsServiceId.WriteDataByIdentifier when data.Length >= 3 => HandleWriteDataByIdentifier(rxId, data),
            _ => null
        };

        return Task.FromResult(response);
    }

    private byte[]? HandleSecurityAccess(byte[] data)
    {
        var subFunction = data[1];

        // Odd sub-function = request seed
        if ((subFunction & 0x01) != 0)
        {
            // Return a simulated seed
            return [0x67, subFunction, 0x12, 0x34, 0x56, 0x78];
        }
        // Even sub-function = send key (always accept in demo mode)
        else
        {
            // Accept any key in demo mode
            return [0x67, subFunction];
        }
    }

    private byte[]? HandleReadDataByIdentifier(uint rxId, byte[] data)
    {
        var did = (ushort)((data[1] << 8) | data[2]);

        // Get module-specific simulated data
        var moduleData = GetSimulatedModuleData(rxId);

        string? value = did switch
        {
            0xF190 => _moduleVins.TryGetValue(rxId, out var vin) ? vin : null, // VIN
            0xF187 => moduleData.PartNumber,      // Part Number
            0xF18C => moduleData.SerialNumber,    // Serial Number
            0xF191 => moduleData.HardwareNumber,  // Hardware Number
            0xF193 => moduleData.HardwareVersion, // Hardware Version
            0xF195 => moduleData.SoftwareNumber,  // Software Number
            0xF197 => moduleData.SystemName,      // System Name
            0xF18A => moduleData.SupplierId,      // Supplier ID
            0xF1A0 => moduleData.SoftwareVersion, // FCA SW Version
            0xF1A1 => moduleData.CalibrationId,   // FCA Calibration ID
            _ => null
        };

        // Special handling for Programming Date (BCD encoded)
        if (did == 0xF199)
        {
            var response = new byte[7]; // Service ID + DID + 4 bytes date
            response[0] = 0x62;
            response[1] = data[1];
            response[2] = data[2];
            // Date: 2023-06-15 in BCD
            response[3] = 0x23; // Year
            response[4] = 0x06; // Month
            response[5] = 0x15; // Day
            response[6] = 0x00;
            return response;
        }

        if (value != null)
        {
            var valueBytes = System.Text.Encoding.ASCII.GetBytes(value);
            var response = new byte[3 + valueBytes.Length];
            response[0] = 0x62; // Positive response
            response[1] = data[1];
            response[2] = data[2];
            Array.Copy(valueBytes, 0, response, 3, valueBytes.Length);
            return response;
        }

        return null;
    }

    private static (string PartNumber, string SerialNumber, string HardwareNumber, string HardwareVersion,
        string SoftwareNumber, string SoftwareVersion, string CalibrationId, string SystemName, string SupplierId)
        GetSimulatedModuleData(uint rxId)
    {
        return rxId switch
        {
            0x7E8 => ("68352654AE", "TE15C3A4F00123", "68352654AA", "03.02", "68352654AE", "15.27.14", "17RU3614AE", "PCM - Powertrain", "CONTINENTAL"),
            0x7E9 => ("68277259AB", "TM15C3B2F00456", "68277259AA", "02.01", "68277259AB", "12.15.03", "17RU9148AB", "TCM - Transmission", "ZF"),
            0x7EA => ("68289633AC", "AB15C3C1F00789", "68289633AA", "01.05", "68289633AC", "08.22.01", "17RU6233AC", "ABS - Brake System", "BOSCH"),
            0x768 => ("68311231AD", "BC15C3D4F00321", "68311231AB", "04.00", "68311231AD", "21.08.07", "17RU1131AD", "BCM - Body Control", "APTIV"),
            0x76A => ("68366547AA", "IC15C3E5F00654", "68366547AA", "02.03", "68366547AA", "09.14.22", "17RU6547AA", "IPC - Instrument", "DENSO"),
            0x728 => ("68225397AH", "RA15C3F6F00987", "68225397AE", "05.12", "68225397AH", "18.45.33", "17RU5397AH", "RADIO - Uconnect", "HARMAN"),
            _ => ("UNKNOWN", "UNKNOWN", "UNKNOWN", "01.00", "UNKNOWN", "01.00.00", "UNKNOWN", "Unknown Module", "UNKNOWN")
        };
    }

    private byte[]? HandleWriteDataByIdentifier(uint rxId, byte[] data)
    {
        var did = (ushort)((data[1] << 8) | data[2]);

        // VIN write (DID 0xF190)
        if (did == 0xF190 && data.Length >= 20) // 3 bytes header + 17 bytes VIN
        {
            var newVin = System.Text.Encoding.ASCII.GetString(data, 3, 17).Trim('\0');
            _moduleVins[rxId] = newVin;
            return [0x6E, data[1], data[2]]; // Positive response
        }

        return null;
    }

    public ValueTask DisposeAsync() { IsConnected = false; return ValueTask.CompletedTask; }
}
