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

namespace AlwaysInFocus
{

    public class WindowOption : INotifyPropertyChanged
    {
        private string displayText;
        private string id;
        private bool isSelected;
        public string DisplayText { get => displayText; set { displayText = value; OnPropertyChanged(); } }
        public string Id { get => id; set { id = value; OnPropertyChanged(); } }
        public bool IsSelected { get => isSelected; set { isSelected = value; OnPropertyChanged(); } }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class MainViewModel : INotifyPropertyChanged
    {

// Import FindWindow from user32.dll
	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

	// Import SendMessage from user32.dll
	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

	// Import GetWindowThreadProcessId from user32.dll
	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	// Import GetForegroundWindow from user32.dll
	//[DllImport("user32.dll")]
	//private static extern IntPtr GetForegroundWindow();

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

	private static IntPtr presenterHwnd;
        private static uint presenterProcessId;

        public bool IsPowerPointSelected { get => isPowerPointSelected; set { isPowerPointSelected = value; OnPropertyChanged(); } }
        private bool isPowerPointSelected;
        public ObservableCollection<WindowOption> DynamicOptions { get; set; }
        public ICommand EditOptionCommand { get; set; }
        public ICommand DeleteOptionCommand { get; set; }
        private const int MaxOptions = 5;
        private readonly string savePath = "window_options.csv";
        private bool isOn = true;
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
                    _selectedOption = value;
                    OnPropertyChanged();
                }
            }
        }
        public bool IsThisSelected => ReferenceEquals(this, SelectedOption);
        private IntPtr _winEventHook = IntPtr.Zero;
        private void OnMethod()
        {
            if (SelectedOption == null)
            {
                System.Windows.MessageBox.Show("Please select a window option before turning on.", "No Option Selected");
                return;
            }
            // Use SelectedOption.Id to find the window
            // If Id is a process name, get the main window handle
            var procs = System.Diagnostics.Process.GetProcessesByName(SelectedOption.Id);
            if (procs.Length > 0)
            {
                presenterHwnd = procs[0].MainWindowHandle;
                GetWindowThreadProcessId(presenterHwnd, out presenterProcessId);
                _winEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, WinEventCallback, 0, 0, WINEVENT_OUTOFCONTEXT);
                System.Diagnostics.Debug.WriteLine($"Turned ON for {SelectedOption.Id}");
                SendMessage(presenterHwnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
            }
            else
            {
                System.Windows.MessageBox.Show($"Could not find process: {SelectedOption.Id}", "Error");
            }
        }
        private void OffMethod()
        {
            if (_winEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }
            System.Diagnostics.Debug.WriteLine("Turned OFF");
        }

private static void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
	{
		if (hwnd == IntPtr.Zero) { 
			Console.WriteLine($"hwnd was Zero...");
		return;
		}

		// Get the Process ID of the newly focused window
		uint activeProcessId;
		GetWindowThreadProcessId(hwnd, out activeProcessId);

		//LastKnowProcess = activeProcessId;

		// Check if the active window has changed from Presenter View
		if (activeProcessId != presenterProcessId)
		{
			Console.WriteLine($"Window focus changed to Process ID: {activeProcessId}. Restoring Presenter View...");
			SendMessage(presenterHwnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
			Thread.Sleep(2);
			SendMessage(presenterHwnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
		}
    }

        public MainViewModel()
        {
            DynamicOptions = new ObservableCollection<WindowOption>();
            EditOptionCommand = new RelayCommand(EditOption);
            DeleteOptionCommand = new RelayCommand(DeleteOption);

            LoadOptions();

            // Ensure PowerPoint option is always first
            if (!DynamicOptions.Any(opt => opt.Id == "POWERPNT"))
                DynamicOptions.Insert(0, new WindowOption { DisplayText = "PowerPoint Presentation View", Id = "POWERPNT" });

            SelectedOption = DynamicOptions[0];
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
        public void SaveOptions()
        {
            File.WriteAllLines(savePath, DynamicOptions.Select(opt => $"{opt.DisplayText},{opt.Id}"));
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

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private bool isSelectingWindow = false;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            InitializeTrayIcon();
        }

        private void InitializeTrayIcon()
        {
            trayIcon = new NotifyIcon();
            trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            trayIcon.Text = "Window Finder";
            trayIcon.Visible = true;

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            var openMenuItem = new System.Windows.Forms.ToolStripMenuItem("Open");
            var exitMenuItem = new System.Windows.Forms.ToolStripMenuItem("Exit");

            openMenuItem.Click += (s, e) => 
            {
                Show();
                WindowState = WindowState.Normal;
            };

            exitMenuItem.Click += (s, e) => 
            {
                trayIcon.Visible = false;
                WPFApp.Current.Shutdown();
            };

            contextMenu.Items.Add(openMenuItem);
            contextMenu.Items.Add(exitMenuItem);
            trayIcon.ContextMenuStrip = contextMenu;

            trayIcon.DoubleClick += (s, e) =>
            {
                Show();
                WindowState = WindowState.Normal;
            };
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
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
                try {
                    var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                    processName = proc.ProcessName;
                } catch {}

                // Add new option if under limit
                Dispatcher.Invoke(() => {
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