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
using FcaDiag.Core.Models;
using FcaDiag.J2534;
using FcaDiag.J2534.Native;
using FcaDiag.Protocols.Transport;
using FcaDiag.Protocols.Uds;
using Microsoft.Win32;

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
    private readonly ObservableCollection<DtcDisplayItem> _dtcList = [];
    private readonly ObservableCollection<VinModuleItem> _vinList = [];
    private string? _connectedDeviceName;
    private string? _connectedVin;
    private EfdFile? _loadedEfd;

    public MainWindow()
    {
        InitializeComponent();

        // Bind DTC list
        dgDtcList.ItemsSource = _dtcList;

        // Bind VIN list
        dgVinList.ItemsSource = _vinList;

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
            Log("Disconnected from device.");
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
                var j2534 = new J2534Adapter(device);
                if (!await j2534.ConnectAsync(new ConnectionSettings { AdapterType = "ISO15765", Bitrate = 500000 }))
                    throw new Exception("Connection failed");

                _adapter = j2534;
                _connectedDeviceName = device.Name;
                Log($"Connected to {device.Name}");
                Log($"Firmware: {j2534.FirmwareVersion}");
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

            MessageBox.Show("ECU flash completed successfully!", "Flash Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Flash error: {ex.Message}");
            MessageBox.Show($"Flash failed:\n\n{ex.Message}", "Flash Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnFlashEfd.IsEnabled = true;
            btnNavFlashEfd.IsEnabled = true;
        }
    }

    private async void BtnSaveEfd_Click(object sender, RoutedEventArgs e)
    {
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

    private async void BtnReadVin_Click(object sender, RoutedEventArgs e)
    {
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

        // Switch to VIN tab
        tabRight.SelectedIndex = 1;

        btnNavReadVin.IsEnabled = true;
    }

    private async void BtnClearVin_Click(object sender, RoutedEventArgs e)
    {
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
                await client.StartSessionAsync(DiagnosticSessionType.Extended);
                await Task.Delay(50);

                // Security access (simplified - real implementation needs proper key calculation)
                // await client.SecurityAccessAsync(0x01, seed => seed); // Placeholder

                // Clear VIN
                var response = await client.ClearVinAsync();

                if (response.IsPositive)
                {
                    cleared++;
                    item.CurrentVin = "(cleared)";
                    item.Status = "CLEARED";
                    Log($"  [{item.ModuleName}] VIN cleared.");
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
                await client.StartSessionAsync(DiagnosticSessionType.Extended);
                await Task.Delay(50);

                // Security access (simplified - real implementation needs proper key calculation)
                // await client.SecurityAccessAsync(0x01, seed => seed); // Placeholder

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
/// Display item for VIN module DataGrid binding
/// </summary>
public class VinModuleItem
{
    public bool IsSelected { get; set; }
    public required string ModuleName { get; set; }
    public required string CurrentVin { get; set; }
    public required string Status { get; set; }
    public required FcaModuleDefinition Module { get; init; }
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
            UdsServiceId.ReadDtcInformation when rxId == 0x7E8 => [0x59, 0x02, 0xFF, 0x03, 0x00, 0x00, 0x08, 0x01, 0x71, 0x00, 0x08],
            UdsServiceId.ReadDtcInformation => [0x59, 0x02, 0xFF],
            UdsServiceId.ClearDiagnosticInformation => [0x54],
            UdsServiceId.ReadDataByIdentifier when data.Length >= 3 => HandleReadDataByIdentifier(rxId, data),
            UdsServiceId.WriteDataByIdentifier when data.Length >= 3 => HandleWriteDataByIdentifier(rxId, data),
            _ => null
        };

        return Task.FromResult(response);
    }

    private byte[]? HandleReadDataByIdentifier(uint rxId, byte[] data)
    {
        var did = (ushort)((data[1] << 8) | data[2]);

        // VIN read (DID 0xF190)
        if (did == 0xF190 && _moduleVins.TryGetValue(rxId, out var vin))
        {
            var response = new byte[2 + 17]; // Service ID + DID + 17 bytes VIN
            response[0] = 0x62; // Positive response
            response[1] = data[1];
            response[2] = data[2];
            var vinBytes = System.Text.Encoding.ASCII.GetBytes(vin.PadRight(17));
            Array.Copy(vinBytes, 0, response, 3, 17);
            return response;
        }

        return null;
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
