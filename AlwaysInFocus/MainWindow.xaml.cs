using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using WPFApp = System.Windows.Application;
using System.Linq;
using System.Windows.Interop;
using System.IO;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Threading;
using System.Threading.Tasks;

namespace AlwaysInFocus
{

    public class WindowOption : INotifyPropertyChanged
    {
        private string displayText;
        private string id;
        private bool isSelected;
        public string DisplayText { get => displayText; set { displayText = value; OnPropertyChanged(); } }
        public string Id { get => id; set { id = value; OnPropertyChanged(); } }
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected != value)
                {
                    isSelected = value;
                    OnPropertyChanged();
                }
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class MainViewModel : INotifyPropertyChanged
    {

        // Import FindWindow from user32.dll
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        // Import SendMessage from user32.dll (kept for compatibility but not used in shutdown-sensitive flows)
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // Use PostMessage (non-blocking) instead of SendMessage when restoring focus so the system shutdown won't deadlock
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // Import GetWindowThreadProcessId from user32.dll
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // Helper to check validity of hwnd
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        // Import SetWinEventHook from user32.dll
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        // Import UnhookWinEvent from user32.dll
        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        // Constants for window messages
        private const uint WM_ACTIVATE = 0x0006;
        private const int WA_ACTIVE = 1;

        // Constants for event hook
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0;

        // Delegate for event hook callback
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        // Keep a static reference to the delegate so the GC cannot collect it while Windows may call it
        private static WinEventDelegate _staticWinEventDelegate;

        private static IntPtr presenterHwnd;
        private static uint presenterProcessId;
        private static string ProcessName = "POWERPNT"; // Default to PowerPoint
        private static readonly object presenterSync = new object();

        public bool IsPowerPointSelected { get => isPowerPointSelected; set { isPowerPointSelected = value; OnPropertyChanged(); } }
        private bool isPowerPointSelected;
        public ObservableCollection<WindowOption> DynamicOptions { get; set; }
        public ICommand EditOptionCommand { get; set; }
        public ICommand DeleteOptionCommand { get; set; }
        private const int MaxOptions = 5;
        private readonly string savePath = "window_options.csv";
        private readonly string statePath = "window_state.csv";
        private bool isOn = false;
        public bool IsOn
        {
            get => isOn;
            set
            {
                if (isOn != value)
                {
                    isOn = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(OnOffLabel));
                    if (isOn) OnMethod();
                    else OffMethod();
                    SaveState();
                }
            }
        }
        public string OnOffLabel => IsOn ? "On" : "Off";
        private WindowOption _selectedOption;
        public WindowOption SelectedOption
        {
            get => _selectedOption;
            set
            {
                if (_selectedOption != value)
                {
                    if (_selectedOption != null)
                        _selectedOption.IsSelected = false;

                    _selectedOption = value;

                    if (_selectedOption != null)
                        _selectedOption.IsSelected = true;

                    OnPropertyChanged();
                }
            }
        }
        public bool IsThisSelected => ReferenceEquals(this, SelectedOption);
        private IntPtr _winEventHook = IntPtr.Zero;
        private string lastSelectedId;
        private CancellationTokenSource? processMonitorCts;
        private static readonly TimeSpan ProcessMonitorDelay = TimeSpan.FromSeconds(2);

        private void OnMethod()
        {
            if (SelectedOption == null)
            {
                System.Windows.MessageBox.Show("Please select a window option before turning on.", "No Option Selected");
                return;
            }

            ProcessName = SelectedOption.Id.ToUpperInvariant(); // Ensure case-insensitive comparison
            EnsureWinEventHookInstalled();
            StartProcessMonitor();
            System.Diagnostics.Debug.WriteLine($"Turned ON for {SelectedOption.Id}");
        }
        private void OffMethod()
        {
            try
            {
                StopProcessMonitor();
                RemoveWinEventHook();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error unhooking win event: {ex.Message}");
            }
            finally
            {
                lock (presenterSync)
                {
                    presenterHwnd = IntPtr.Zero;
                    presenterProcessId = 0;
                }
                System.Diagnostics.Debug.WriteLine("Turned OFF");
            }
        }

        private void EnsureWinEventHookInstalled()
        {
            if (_winEventHook != IntPtr.Zero)
                return;

            _staticWinEventDelegate = WinEventCallback;
            _winEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _staticWinEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        }

        private void RemoveWinEventHook()
        {
            if (_winEventHook == IntPtr.Zero)
                return;

            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }

        private void StartProcessMonitor()
        {
            StopProcessMonitor();
            processMonitorCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    await MonitorTargetProcessAsync(processMonitorCts.Token);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Process monitor failed: {ex.Message}");
                }
            });
        }

        private void StopProcessMonitor()
        {
            if (processMonitorCts == null)
                return;

            processMonitorCts.Cancel();
            processMonitorCts.Dispose();
            processMonitorCts = null;
        }

        private async Task MonitorTargetProcessAsync(CancellationToken cancellationToken)
        {
            bool hadTarget = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                bool foundTarget = TryRefreshPresenterTarget();
                if (foundTarget && !hadTarget)
                {
                    TryActivatePresenterWindow();
                }

                hadTarget = foundTarget;

                try
                {
                    await Task.Delay(ProcessMonitorDelay, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private static bool TryRefreshPresenterTarget()
        {
            try
            {
                var foundProcs = System.Diagnostics.Process.GetProcessesByName(ProcessName);
                foreach (var proc in foundProcs)
                {
                    var hwnd = proc.MainWindowHandle;
                    if (hwnd != IntPtr.Zero && IsWindow(hwnd))
                    {
                        uint pid;
                        GetWindowThreadProcessId(hwnd, out pid);
                        lock (presenterSync)
                        {
                            presenterHwnd = hwnd;
                            presenterProcessId = pid;
                        }
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Target refresh failed: {ex.Message}");
            }

            lock (presenterSync)
            {
                presenterHwnd = IntPtr.Zero;
                presenterProcessId = 0;
            }
            return false;
        }

        private static void TryActivatePresenterWindow()
        {
            try
            {
                IntPtr hwnd;
                lock (presenterSync)
                {
                    hwnd = presenterHwnd;
                }

                if (hwnd != IntPtr.Zero && IsWindow(hwnd))
                {
                    PostMessage(hwnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error posting activate message: {ex.Message}");
            }
        }

        private static void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Defensive: any exception in this callback can crash the process when OS is shutting down.
            try
            {
                IntPtr targetHwnd;
                uint targetPid;
                lock (presenterSync)
                {
                    targetHwnd = presenterHwnd;
                    targetPid = presenterProcessId;
                }

                if (targetHwnd == IntPtr.Zero || targetPid == 0 || !IsWindow(targetHwnd))
                {
                    return;
                }

                if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                {
                    return;
                }

                // Get the Process ID of the newly focused window
                uint activeProcessId;
                GetWindowThreadProcessId(hwnd, out activeProcessId);

                // Check if the active window has changed from Presenter View
                if (activeProcessId != targetPid)
                {
                    // Use PostMessage (non-blocking). Optionally post twice with a short delay to increase chance of success.
                    try
                    {
                        PostMessage(targetHwnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
                        // schedule a second attempt shortly after to avoid timing issues during OS state changes
                        Task.Run(() =>
                        {
                            try
                            {
                                Thread.Sleep(10);
                                PostMessage(targetHwnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
                            }
                            catch { }
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to post activate message: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Do not rethrow; swallow to protect shutdown flow
                Console.WriteLine($"WinEventCallback exception: {ex.Message}");
            }
        }

        public MainViewModel()
        {
            DynamicOptions = new ObservableCollection<WindowOption>();
            EditOptionCommand = new RelayCommand(EditOption);
            DeleteOptionCommand = new RelayCommand(DeleteOption);

            // Ensure we unhook on application exit to avoid callbacks after shutdown begins
            if (WPFApp.Current != null)
            {
                WPFApp.Current.Exit += (s, e) =>
                {
                    try
                    {
                        OffMethod();
                    }
                    catch { }
                };
            }

            // Load state first to get the last selected ID
            LoadState();

            // Then load options
            LoadOptions();

            // Ensure PowerPoint option is always first
            if (!DynamicOptions.Any(opt => opt.Id == "POWERPNT"))
                DynamicOptions.Insert(0, new WindowOption { DisplayText = "PowerPoint Presentation View", Id = "POWERPNT" });

            // Now try to restore the selected option
            if (!string.IsNullOrEmpty(lastSelectedId))
            {
                var option = DynamicOptions.FirstOrDefault(opt => opt.Id == lastSelectedId);
                if (option != null)
                {
                    SelectedOption = option;
                }
            }

            // If still no option is selected, select the first one
            if (SelectedOption == null && DynamicOptions.Count > 0)
            {
                SelectedOption = DynamicOptions[0];
            }
        }
        public void AddOption(string displayText, string id)
        {
            if (DynamicOptions.Count >= MaxOptions)
            {
                System.Windows.MessageBox.Show($"Please delete one of the existing options (max {MaxOptions}).", "Limit Reached");
                return;
            }
            DynamicOptions.Add(new WindowOption { DisplayText = displayText, Id = id });
        }
        private void EditOption(object param)
        {
            if (param is WindowOption option)
            {
                var dialog = new EditOptionDialog(option.DisplayText, option.Id) { Owner = WPFApp.Current.MainWindow };
                if (dialog.ShowDialog() == true)
                {
                    option.DisplayText = dialog.DisplayText;
                    option.Id = dialog.Id;
                }
            }
        }
        private void DeleteOption(object param)
        {
            if (param is WindowOption option)
            {
                DynamicOptions.Remove(option);
            }
        }
        public void LoadOptions()
        {
            DynamicOptions.Clear();
            if (File.Exists(savePath))
            {
                foreach (var line in File.ReadAllLines(savePath))
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 2)
                        DynamicOptions.Add(new WindowOption { DisplayText = parts[0], Id = parts[1] });
                }
            }
        }
        private void LoadState()
        {
            if (File.Exists(statePath))
            {
                try
                {
                    var lines = File.ReadAllLines(statePath);
                    if (lines.Length >= 2)
                    {
                        // Load On/Off state
                        isOn = bool.Parse(lines[0]);
                        OnPropertyChanged(nameof(IsOn));
                        OnPropertyChanged(nameof(OnOffLabel));

                        // Store the selected ID for later use
                        lastSelectedId = lines[1];

                        UpdateWindowsStartupRegistration();

                        // If was on, trigger OnMethod after a short delay to ensure everything is initialized
                        if (isOn)
                        {
                            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                                new Action(() => OnMethod()),
                                System.Windows.Threading.DispatcherPriority.Loaded);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading state: {ex.Message}");
                }
            }
        }
        public void SaveState()
        {
            try
            {
                var lines = new[]
                {
                    isOn.ToString(),
                    SelectedOption?.Id ?? ""
                };
                File.WriteAllLines(statePath, lines);
                UpdateWindowsStartupRegistration();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving state: {ex.Message}");
            }
        }

        private void UpdateWindowsStartupRegistration()
        {
            try
            {
                using var runKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (runKey == null)
                {
                    return;
                }

                const string startupValueName = "AlwaysInFocus";
                if (isOn)
                {
                    var executablePath = Environment.ProcessPath
                        ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

                    if (string.IsNullOrWhiteSpace(executablePath))
                    {
                        executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    }

                    if (!string.IsNullOrWhiteSpace(executablePath))
                    {
                        runKey.SetValue(startupValueName, $"\"{executablePath}\"");
                    }
                }
                else
                {
                    runKey.DeleteValue(startupValueName, false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating Windows startup registration: {ex.Message}");
            }
        }
        public void SaveOptions()
        {
            File.WriteAllLines(savePath, DynamicOptions.Select(opt => $"{opt.DisplayText},{opt.Id}"));
            SaveState();
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> execute;
        private readonly Predicate<object> canExecute;
        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }
        public bool CanExecute(object parameter) => canExecute == null || canExecute(parameter);
        public void Execute(object parameter) => execute(parameter);
        public event EventHandler CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private NotifyIcon trayIcon;
        private System.Windows.Forms.ToolStripMenuItem toggleMenuItem;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(System.Drawing.Point p);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetCapture(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private IntPtr _mouseHook = IntPtr.Zero;
        private LowLevelMouseProc _mouseProc;
        private bool _isExiting;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private bool isSelectingWindow = false;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            InitializeTrayIcon();

            // Subscribe to property changes
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.IsOn))
                    {
                        UpdateTrayMenuText(vm.IsOn);
                    }
                };
            }
        }

        private void InitializeTrayIcon()
        {
            trayIcon = new NotifyIcon();
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AlwaysInFocus.ico");
            if (System.IO.File.Exists(iconPath))
            {
                trayIcon.Icon = new System.Drawing.Icon(iconPath);
            }
            else
            {
                trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
            trayIcon.Text = "Window Finder";
            trayIcon.Visible = true;

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            var openMenuItem = new System.Windows.Forms.ToolStripMenuItem("🖥 Show");
            toggleMenuItem = new System.Windows.Forms.ToolStripMenuItem("⚡ Toggle On/Off");
            var exitMenuItem = new System.Windows.Forms.ToolStripMenuItem("✖️ Exit");

            openMenuItem.Click += (s, e) =>
            {
                // Ensure window appears in taskbar and becomes visible
                ShowInTaskbar = true;
                Show();
                WindowState = WindowState.Normal;
                Activate();
            };

            toggleMenuItem.Click += (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.IsOn = !vm.IsOn;
                    UpdateTrayMenuText(vm.IsOn);
                }
            };

            exitMenuItem.Click += (s, e) =>
            {
                BeginExit();
            };

            contextMenu.Items.Add(openMenuItem);
            contextMenu.Items.Add(toggleMenuItem);
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add(exitMenuItem);
            trayIcon.ContextMenuStrip = contextMenu;

            trayIcon.DoubleClick += (s, e) =>
            {
                // Same behaviour as open: show window and put it in taskbar
                ShowInTaskbar = true;
                Show();
                WindowState = WindowState.Normal;
                Activate();
            };

            // Initialize the menu text based on current state
            if (DataContext is MainViewModel vm)
            {
                UpdateTrayMenuText(vm.IsOn);
            }
        }

        private void UpdateTrayMenuText(bool isOn)
        {
            if (toggleMenuItem != null)
            {
                toggleMenuItem.Text = isOn ? "⚡ Turn Off" : "⚡ Turn On";
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isExiting)
            {
                e.Cancel = false;
                return;
            }

            // Instead of exiting, hide to tray. Remove from taskbar so it behaves like normal tray apps.
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
        }

        private void BeginExit()
        {
            if (_isExiting)
            {
                return;
            }

            _isExiting = true;
            if (DataContext is MainViewModel vm)
            {
                vm.IsOn = false;
            }

            trayIcon.Visible = false;
            trayIcon.Dispose();
            Close();
            WPFApp.Current.Shutdown();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // this.PreviewMouseDown += MainWindow_PreviewMouseDown;
        }

        private void SetGlobalMouseHook()
        {
            _mouseProc = MouseHookCallback;
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private void RemoveGlobalMouseHook()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN && isSelectingWindow)
            {
                isSelectingWindow = false;
                Mouse.OverrideCursor = null;
                RemoveGlobalMouseHook();

                // Get global mouse position
                System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
                IntPtr hWnd = WindowFromPoint(cursor);
                System.Text.StringBuilder className = new System.Text.StringBuilder(100);
                GetClassName(hWnd, className, className.Capacity);

                // Get process name
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                string processName = "";
                try
                {
                    var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                    processName = proc.ProcessName;
                }
                catch { }

                // Add new option if under limit
                Dispatcher.Invoke(() =>
                {
                    if (DataContext is MainViewModel vm)
                    {
                        if (vm.DynamicOptions.Count < 5)
                        {
                            vm.AddOption($"{className} ({processName})", processName);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show($"Please delete one of the existing options (max 5).", "Limit Reached");
                        }
                        // Turn off after picking
                        vm.IsOn = false;
                    }
                });
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private void FindWindow_Click(object sender, RoutedEventArgs e)
        {
            isSelectingWindow = true;
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Cross;
            SetGlobalMouseHook();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (DataContext is MainViewModel vm)
                vm.SaveOptions();
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {


            //string name = ((System.Windows.Controls.RadioButton)sender).Name;

            if (DataContext is MainViewModel vm && sender is System.Windows.Controls.RadioButton rb && rb.DataContext is WindowOption option)
            {
                vm.SelectedOption = option;
            }
        }
    }

    public class ReferenceEqualsConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return ReferenceEquals(value, parameter);
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if ((bool)value) return parameter;
            return System.Windows.Data.Binding.DoNothing;
        }
    }

    public class ReferenceEqualsMultiConverter : System.Windows.Data.IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values.Length < 2) return false;
            return ReferenceEquals(values[0], values[1]);
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            if ((bool)value) return new object[] { null, null }; // Handled in SelectedOption setter
            return null;
        }
    }
}
