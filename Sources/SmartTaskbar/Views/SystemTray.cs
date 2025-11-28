using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.ViewManagement;
using SmartTaskbar.Helpers;
using SmartTaskbar.Languages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;

namespace SmartTaskbar
{
    internal class SystemTray
    {
        private const int TrayTolerance = 4;
        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 1;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        
        private readonly IntPtr _windowHandle;
        private readonly WndProcDelegate _wndProcDelegate;
        private readonly Engine _engine;
        private readonly ResourceCulture _resourceCulture = new();
        
        private readonly string _aboutText;
        private readonly string _animationText;
        private readonly string _autoModeText;
        private readonly string _exitText;
        private readonly string _showBarOnExitText;
        
        private bool _animationChecked;
        private bool _autoModeChecked;
        private bool _showBarOnExitChecked;
        
        private MenuFlyout _contextMenu;
        private DispatcherQueue _dispatcherQueue;
        
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public IntPtr guidItem;
            public IntPtr hBalloonIcon;
        }
        
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        
        [DllImport("shell32.dll", SetLastError = true)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterClassEx(ref WNDCLASSEX wcex);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern ushort RegisterWindowMessage(string lpString);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);
        
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }
        
        public SystemTray()
        {
            // Initialize WinUI dispatcher queue
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            
            // Initialize engine
            _engine = new Engine(null);
            
            // Initialize localized strings
            _aboutText = _resourceCulture.GetString(LangName.About);
            _animationText = _resourceCulture.GetString(LangName.Animation);
            _autoModeText = _resourceCulture.GetString(LangName.Auto);
            _exitText = _resourceCulture.GetString(LangName.Exit);
            _showBarOnExitText = _resourceCulture.GetString(LangName.ShowBarOnExit);
            
            // Create message-only window for tray icon
            _wndProcDelegate = WndProc;
            _windowHandle = CreateMessageOnlyWindow();
            
            // Create context menu
            CreateContextMenu();
            
            // Add tray icon
            AddTrayIcon();
            
            // Subscribe to theme changes
            Fun.UiSettings.ColorValuesChanged += UISettingsOnColorValuesChanged;
        }
        
        private IntPtr CreateMessageOnlyWindow()
        {
            var className = "SmartTaskbarTrayWindow";
            var hInstance = GetModuleHandle(null);
            
            var wcex = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
                style = 0,
                lpfnWndProc = _wndProcDelegate,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = hInstance,
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = className,
                hIconSm = IntPtr.Zero
            };
            
            RegisterClassEx(ref wcex);
            
            return CreateWindowEx(
                0,
                className,
                "SmartTaskbar Tray Window",
                0,
                0,
                0,
                0,
                0,
                new IntPtr(-3), // HWND_MESSAGE
                IntPtr.Zero,
                hInstance,
                IntPtr.Zero);
        }
        
        private void CreateContextMenu()
        {
            _contextMenu = new MenuFlyout();
            
            // About item
            var aboutItem = new MenuFlyoutItem 
            {
                Text = _aboutText,
                AccessKey = "A",
                AutomationProperties.Name = _aboutText,
                AutomationProperties.HelpText = "Open SmartTaskbar GitHub page"
            };
            aboutItem.Click += AboutOnClick;
            _contextMenu.Items.Add(aboutItem);
            
            // Animation item
            var animationItem = new ToggleMenuFlyoutItem 
            {
                Text = _animationText,
                AccessKey = "N",
                AutomationProperties.Name = _animationText,
                AutomationProperties.HelpText = "Toggle taskbar animation"
            };
            animationItem.Click += AnimationInBarOnClick;
            _contextMenu.Items.Add(animationItem);
            
            // Separator
            _contextMenu.Items.Add(new MenuFlyoutSeparator());
            
            // Auto mode item
            var autoModeItem = new ToggleMenuFlyoutItem 
            {
                Text = _autoModeText,
                AccessKey = "U",
                AutomationProperties.Name = _autoModeText,
                AutomationProperties.HelpText = "Toggle auto mode"
            };
            autoModeItem.Click += AutoModeOnClick;
            _contextMenu.Items.Add(autoModeItem);
            
            // Separator
            _contextMenu.Items.Add(new MenuFlyoutSeparator());
            
            // Show bar on exit item
            var showBarOnExitItem = new ToggleMenuFlyoutItem 
            {
                Text = _showBarOnExitText,
                AccessKey = "S",
                AutomationProperties.Name = _showBarOnExitText,
                AutomationProperties.HelpText = "Show taskbar on exit"
            };
            showBarOnExitItem.Click += ShowBarOnExitOnClick;
            _contextMenu.Items.Add(showBarOnExitItem);
            
            // Exit item
            var exitItem = new MenuFlyoutItem 
            {
                Text = _exitText,
                AccessKey = "E",
                AutomationProperties.Name = _exitText,
                AutomationProperties.HelpText = "Exit SmartTaskbar"
            };
            exitItem.Click += ExitOnClick;
            _contextMenu.Items.Add(exitItem);
        }
        
        private void AddTrayIcon()
        {
            var icon = Fun.IsLightTheme() ? IconResource.Logo_Black : IconResource.Logo_White;
            var hIcon = icon.Handle;
            
            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _windowHandle,
                uID = 1,
                uFlags = 0x00000001 | 0x00000002 | 0x00000004, // NIF_ICON | NIF_MESSAGE | NIF_TIP
                uCallbackMessage = WM_TRAYICON,
                hIcon = hIcon,
                szTip = "SmartTaskbar"
            };
            
            Shell_NotifyIcon(0, ref nid); // NIM_ADD
        }
        
        private void RemoveTrayIcon()
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _windowHandle,
                uID = 1
            };
            
            Shell_NotifyIcon(2, ref nid); // NIM_DELETE
        }
        
        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_TRAYICON)
            {
                switch (lParam.ToInt32())
                {
                    case WM_RBUTTONDOWN:
                        // Show context menu on right click
                        _dispatcherQueue.TryEnqueue(() => ShowContextMenu());
                        break;
                    case WM_LBUTTONDBLCLK:
                        // Handle double click
                        _dispatcherQueue.TryEnqueue(() => HandleDoubleClick());
                        break;
                }
            }
            
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }
        
        private void ShowContextMenu()
        {
            // Update menu item states
            _animationChecked = Fun.IsEnableTaskbarAnimation();
            _showBarOnExitChecked = UserSettings.ShowTaskbarWhenExit;
            _autoModeChecked = UserSettings.AutoModeType == AutoModeType.Auto;
            
            // Update UI from dispatcher thread
            if (_contextMenu != null)
            {
                if (_contextMenu.Items[1] is ToggleMenuFlyoutItem animationItem)
                    animationItem.IsChecked = _animationChecked;
                
                if (_contextMenu.Items[3] is ToggleMenuFlyoutItem autoModeItem)
                    autoModeItem.IsChecked = _autoModeChecked;
                
                if (_contextMenu.Items[5] is ToggleMenuFlyoutItem showBarOnExitItem)
                    showBarOnExitItem.IsChecked = _showBarOnExitChecked;
                
                // Get cursor position
                GetCursorPos(out POINT cursorPos);
                
                // Show menu at cursor position
                _contextMenu.ShowAt(new Point(cursorPos.X, cursorPos.Y));
            }
        }
        
        private void HandleDoubleClick()
        {
            UserSettings.AutoModeType = AutoModeType.None;
            Fun.ChangeAutoHide();
            HideBar();
        }
        
        private void AboutOnClick(object sender, RoutedEventArgs e)
        {
            _ = Launcher.LaunchUriAsync(new Uri("https://github.com/ChanpleCai/SmartTaskbar"));
        }
        
        private void AnimationInBarOnClick(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item)
            {
                _animationChecked = Fun.ChangeTaskbarAnimation();
                item.IsChecked = _animationChecked;
            }
        }
        
        private void AutoModeOnClick(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item)
            {
                if (_autoModeChecked)
                {
                    UserSettings.AutoModeType = AutoModeType.None;
                    HideBar();
                    _autoModeChecked = false;
                }
                else
                {
                    UserSettings.AutoModeType = AutoModeType.Auto;
                    _autoModeChecked = true;
                }
                
                item.IsChecked = _autoModeChecked;
            }
        }
        
        private void ShowBarOnExitOnClick(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item)
            {
                _showBarOnExitChecked = !_showBarOnExitChecked;
                UserSettings.ShowTaskbarWhenExit = _showBarOnExitChecked;
                item.IsChecked = _showBarOnExitChecked;
            }
        }
        
        private void ExitOnClick(object sender, RoutedEventArgs e)
        {
            if (UserSettings.ShowTaskbarWhenExit)
                Fun.CancelAutoHide();
            else
                HideBar();
            
            RemoveTrayIcon();
            DestroyWindow(_windowHandle);
            
            // Exit application
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                Process.GetCurrentProcess().Kill();
            });
        }
        
        private static void HideBar()
        {
            if (Fun.IsNotAutoHide())
                return;
            
            var taskbar = TaskbarHelper.InitTaskbar();
            
            if (taskbar.Handle != IntPtr.Zero)
                taskbar.HideTaskbar();
        }
        
        private void UISettingsOnColorValuesChanged(UISettings s, object e)
        {
            // Update tray icon based on theme
            var icon = Fun.IsLightTheme() ? IconResource.Logo_Black : IconResource.Logo_White;
            var hIcon = icon.Handle;
            
            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _windowHandle,
                uID = 1,
                uFlags = 0x00000001, // NIF_ICON
                hIcon = hIcon
            };
            
            Shell_NotifyIcon(1, ref nid); // NIM_MODIFY
        }
    }
}
