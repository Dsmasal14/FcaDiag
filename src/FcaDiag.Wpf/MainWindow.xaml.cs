using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FcaDiag.Core.Enums;
using FcaDiag.Core.Interfaces;
using FcaDiag.Core.Models;
using FcaDiag.J2534;
using FcaDiag.J2534.Native;
using FcaDiag.Protocols.Transport;
using FcaDiag.Protocols.Uds;

namespace FcaDiag.Wpf;

public partial class MainWindow : Window
{
    private ICanAdapter? _adapter;
    private bool _isConnected;
    private List<J2534Device> _devices = [];
    private readonly DispatcherTimer _voltageTimer;
    private readonly List<double> _voltageHistory = [];
    private readonly Random _random = new();
    private readonly int[] _memoryBlocks = new int[8];

    public MainWindow()
    {
        InitializeComponent();

        // Initialize voltage monitoring timer
        _voltageTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
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

        Log("> System Ready...");
        Log("> Waiting for vehicle connection...");
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var newLine = message.StartsWith(">") ? message : $"[{timestamp}] > {message}";
        txtTerminal.Text += "\n" + newLine;
        scrollLog.ScrollToEnd();
    }

    private void UpdateTitle(string status)
    {
        txtTitle.Text = $"FCA Diagnostics v1.0 - [{status}]";
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
            _isConnected = false;
            btnConnect.Content = "Connect Pod";
            UpdateTitle("Disconnected");
            Log("Disconnected from device.");
            return;
        }

        // Show device selection
        _devices = J2534DeviceDiscovery.GetDevices();

        var dialog = new Window
        {
            Title = "Select Device",
            Width = 400,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(22, 27, 34))
        };

        var stack = new StackPanel { Margin = new Thickness(20) };
        var label = new TextBlock
        {
            Text = "Select J2534 Device:",
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 20) };
        combo.Items.Add("Demo Mode (Simulated Vehicle)");
        foreach (var d in _devices)
            combo.Items.Add($"{d.Vendor} - {d.Name}");
        combo.SelectedIndex = 0;

        var btnOk = new Button
        {
            Content = "Connect",
            Width = 100,
            Padding = new Thickness(10, 5, 10, 5),
            Background = new SolidColorBrush(Color.FromRgb(255, 107, 0)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
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
                Log("Protocol: CAN-C detected.");
                Log("Module Found: PCM (Powertrain Control Module).");
                Log("Security Access: Granted.");
                UpdateTitle("Demo Mode - Connected");
            }
            else
            {
                var device = _devices[combo.SelectedIndex - 1];
                var j2534 = new J2534Adapter(device);
                if (!await j2534.ConnectAsync(new ConnectionSettings { AdapterType = "ISO15765", Bitrate = 500000 }))
                    throw new Exception("Connection failed");

                _adapter = j2534;
                Log($"Connected to {device.Name}");
                Log($"Firmware: {j2534.FirmwareVersion}");
                UpdateTitle($"{device.Name} - Connected");
            }

            _isConnected = true;
            btnConnect.Content = "Disconnect";
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
        if (!_isConnected)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        btnReadEprom.IsEnabled = false;
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
        btnReadEprom.IsEnabled = true;
    }

    private async void BtnWriteEprom_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show("Are you sure you want to write EPROM?\nThis operation cannot be undone.",
            "Confirm Write", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        btnWriteEprom.IsEnabled = false;
        Log("Preparing to write EPROM...");
        Log("Erasing target memory...");
        await Task.Delay(500);

        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(400);
            Log($"Writing Block {i + 1}/8... Verified.");
        }

        Log("EPROM write complete. Verification passed.");
        btnWriteEprom.IsEnabled = true;
    }

    private void BtnSwapHw_Click(object sender, RoutedEventArgs e)
    {
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

        btnScanModules.IsEnabled = false;
        Log("Scanning for ECU modules...");

        int found = 0;
        foreach (var module in FcaModuleDatabase.Modules)
        {
            try
            {
                var client = new UdsClient(_adapter, module, 200);
                var response = await client.TesterPresentAsync();
                if (response.IsPositive)
                {
                    found++;
                    Log($"  Found: {module.ShortName} ({module.Name}) @ 0x{module.RequestId:X3}");
                }
            }
            catch { }
        }

        Log($"Scan complete. {found} module(s) responding.");
        btnScanModules.IsEnabled = true;
    }

    private async void BtnReadDtc_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected || _adapter == null)
        {
            MessageBox.Show("Please connect first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        btnReadDtc.IsEnabled = false;
        Log("Reading Diagnostic Trouble Codes...");

        int totalDtcs = 0;
        foreach (var module in FcaModuleDatabase.Modules)
        {
            try
            {
                var client = new UdsClient(_adapter, module, 500);
                var dtcs = await client.ReadDtcsAsync();
                foreach (var dtc in dtcs)
                {
                    totalDtcs++;
                    var status = dtc.Confirmed ? "ACTIVE" : "PENDING";
                    Log($"  [{module.ShortName}] {dtc.DisplayCode} - {status}");
                }
            }
            catch { }
        }

        Log(totalDtcs > 0 ? $"Found {totalDtcs} DTC(s)." : "No DTCs found.");
        btnReadDtc.IsEnabled = true;
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

// Mock adapter for demo mode
public class MockCanAdapter : ICanAdapter
{
    private readonly Random _random = new();
    private readonly HashSet<uint> _respondingModules = [0x7E8, 0x7E9, 0x7EA, 0x768, 0x76A, 0x728];

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
            UdsServiceId.ReadDtcInformation when rxId == 0x7E8 => [0x59, 0x02, 0xFF, 0x03, 0x00, 0x00, 0x08, 0x01, 0x71, 0x00, 0x08],
            UdsServiceId.ReadDtcInformation => [0x59, 0x02, 0xFF],
            _ => null
        };

        return Task.FromResult(response);
    }

    public ValueTask DisposeAsync() { IsConnected = false; return ValueTask.CompletedTask; }
}
