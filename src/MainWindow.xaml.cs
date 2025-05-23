using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopMirror
{
    public partial class MainWindow : Window
    {
        private NotifyIcon trayIcon;
        private bool isVisible = false;
        private ObservableCollection<DesktopItem> desktopItems;
        private HwndSource source;
        private AppConfig config;
        private System.Text.RegularExpressions.Regex? hideRegex;
        private string currentSearch = "";
        private DateTime lastKeyPress = DateTime.MinValue;
        private const int SEARCH_TIMEOUT_MS = 1000; // Reset search after 1 second of no typing

        public bool ShowPasteArea
        {
            get => config.ShowPasteArea;
            set
            {
                if (config.ShowPasteArea != value)
                {
                    config.ShowPasteArea = value;
                    ConfigManager.SaveConfig(config);
                    OnPropertyChanged(nameof(ShowPasteArea));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr ExtractAssociatedIcon(IntPtr hInst, string lpIconPath, ref int lpiIcon);

        private const int HOTKEY_ID = 1;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_SHIFT = 0x0004;

        public MainWindow()
        {
            InitializeComponent();
            config = ConfigManager.LoadConfig();
            DataContext = this;
            System.Diagnostics.Debug.WriteLine($"Loaded config - HideRegex: {config.HideRegex}");
            if (!string.IsNullOrEmpty(config.HideRegex))
            {
                try
                {
                    hideRegex = new System.Text.RegularExpressions.Regex(config.HideRegex);
                    System.Diagnostics.Debug.WriteLine($"Successfully created regex with pattern: {config.HideRegex}");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Invalid hide regex: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            InitializeTrayIcon();
            desktopItems = new ObservableCollection<DesktopItem>();
            DesktopItemsListView.ItemsSource = desktopItems;
        }

        private void InitializeTrayIcon()
        {
            try
            {
                trayIcon = new NotifyIcon
                {
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    Visible = true,
                    Text = "Desktop Mirror"
                };

                var contextMenu = new System.Windows.Forms.ContextMenuStrip();
                var exitMenuItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
                exitMenuItem.Click += (s, e) =>
                {
                    trayIcon.Visible = false;
                    System.Windows.Application.Current.Shutdown();
                };
                contextMenu.Items.Add(exitMenuItem);
                trayIcon.ContextMenuStrip = contextMenu;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error initializing tray icon: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RegisterHotkey()
        {
            if (source != null)
            {
                // Unregister existing hotkey if any
                UnregisterHotKey(source.Handle, HOTKEY_ID);

                // Calculate modifiers
                uint modifiers = 0;
                if (config.UseCtrl) modifiers |= MOD_CONTROL;
                if (config.UseAlt) modifiers |= MOD_ALT;
                if (config.UseShift) modifiers |= MOD_SHIFT;

                // Get the virtual key code for the hotkey
                if (!string.IsNullOrEmpty(config.Hotkey) && config.Hotkey.Length == 1)
                {
                    char key = char.ToUpper(config.Hotkey[0]);
                    uint vk = (uint)key;

                    // Register the new hotkey
                    if (!RegisterHotKey(source.Handle, HOTKEY_ID, modifiers, vk))
                    {
                        System.Windows.MessageBox.Show(
                            $"Failed to register hotkey: {(config.UseCtrl ? "Ctrl+" : "")}{(config.UseAlt ? "Alt+" : "")}{(config.UseShift ? "Shift+" : "")}{config.Hotkey}",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(HwndHook);
            RegisterHotkey();
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleWindowVisibility();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ToggleWindowVisibility()
        {
            Dispatcher.Invoke(() =>
            {
                if (isVisible)
                {
                    Hide();
                    isVisible = false;
                }
                else
                {
                    CenterOnCursorScreen();
                    RefreshDesktopItems();
                    if (DesktopItemsListView.Items.Count > 0)
                    {
                        DesktopItemsListView.ScrollIntoView(DesktopItemsListView.Items[0]);
                    }
                    Show();
                    Activate();
                    isVisible = true;
                }
            });
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Hide();
            RefreshDesktopItems();
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            
            if (e.Key == System.Windows.Input.Key.Escape && config.CloseOnEscape)
            {
                Hide();
                isVisible = false;
                return;
            }

            // Only process text input if the window is visible and has focus
            if (!isVisible || !IsActive) return;

            // Handle special keys
            if (e.Key == System.Windows.Input.Key.Back)
            {
                if (currentSearch.Length > 0)
                {
                    currentSearch = currentSearch.Substring(0, currentSearch.Length - 1);
                    FindNextMatch();
                }
                return;
            }

            // Get the character from the key
            char? character = GetCharFromKey(e.Key);
            if (character.HasValue)
            {
                // Check if we need to reset the search
                if ((DateTime.Now - lastKeyPress).TotalMilliseconds > SEARCH_TIMEOUT_MS)
                {
                    currentSearch = "";
                }
                lastKeyPress = DateTime.Now;

                // Add the character to our search
                currentSearch += character.Value;
                FindNextMatch();
            }
        }

        private char? GetCharFromKey(System.Windows.Input.Key key)
        {
            // Convert the key to a character
            if (key >= System.Windows.Input.Key.A && key <= System.Windows.Input.Key.Z)
            {
                return (char)('a' + (key - System.Windows.Input.Key.A));
            }
            if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9)
            {
                return (char)('0' + (key - System.Windows.Input.Key.D0));
            }
            if (key >= System.Windows.Input.Key.NumPad0 && key <= System.Windows.Input.Key.NumPad9)
            {
                return (char)('0' + (key - System.Windows.Input.Key.NumPad0));
            }
            if (key == System.Windows.Input.Key.Space)
            {
                return ' ';
            }
            if (key == System.Windows.Input.Key.OemMinus || key == System.Windows.Input.Key.Subtract)
            {
                return '-';
            }
            if (key == System.Windows.Input.Key.OemPeriod || key == System.Windows.Input.Key.Decimal)
            {
                return '.';
            }
            return null;
        }

        private void FindNextMatch()
        {
            if (string.IsNullOrEmpty(currentSearch)) return;

            var items = desktopItems.ToList();
            var currentIndex = DesktopItemsListView.SelectedIndex;
            
            // Start searching from the next item after the current selection
            for (int i = 0; i < items.Count; i++)
            {
                int index = (currentIndex + 1 + i) % items.Count;
                var item = items[index];
                
                if (item.Name.StartsWith(currentSearch, StringComparison.OrdinalIgnoreCase))
                {
                    DesktopItemsListView.SelectedIndex = index;
                    DesktopItemsListView.ScrollIntoView(item);
                    return;
                }
            }
        }

        private void CenterOnCursorScreen()
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (config.TargetMonitor.HasValue && config.TargetMonitor.Value < screens.Length)
            {
                // Use specified monitor
                var screen = screens[config.TargetMonitor.Value];
                PositionWindowOnScreen(screen);
            }
            else
            {
                // Use current monitor
                var mousePos = System.Windows.Forms.Cursor.Position;
                var screen = System.Windows.Forms.Screen.FromPoint(mousePos);
                PositionWindowOnScreen(screen);
            }
        }

        private void PositionWindowOnScreen(System.Windows.Forms.Screen screen)
        {
            double dpiX = 1.0, dpiY = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }
            double screenLeft = screen.Bounds.Left / dpiX;
            double screenTop = screen.Bounds.Top / dpiY;
            double screenWidth = screen.Bounds.Width / dpiX;
            double screenHeight = screen.Bounds.Height / dpiY;
            Left = screenLeft + (screenWidth - Width) / 2;
            Top = screenTop + (screenHeight - Height) / 2;
        }

        private string GetDisplayName(string fileName)
        {
            if (config.HideExtensions == HideExtensionsMode.Always)
            {
                return Path.GetFileNameWithoutExtension(fileName);
            }
            else if (config.HideExtensions == HideExtensionsMode.ListedOnly && !string.IsNullOrEmpty(config.HideExtensionsList))
            {
                var extension = Path.GetExtension(fileName).TrimStart('.');
                if (config.HideExtensionsList.Split('|').Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    return Path.GetFileNameWithoutExtension(fileName);
                }
            }
            return Path.GetFileName(fileName);
        }

        private bool ShouldShowItem(string name)
        {
            if (hideRegex != null)
            {
                bool isMatch = hideRegex.IsMatch(name);
                System.Diagnostics.Debug.WriteLine($"Checking file: {name}, Regex match: {isMatch}, Pattern: {config.HideRegex}");
                return !isMatch;
            }
            return true;
        }

        private void RefreshDesktopItems()
        {
            try
            {
                desktopItems.Clear();
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                
                // Get all folders first
                var folders = Directory.GetDirectories(desktopPath, "*", SearchOption.TopDirectoryOnly)
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.Name);

                // Then get all files
                var files = Directory.GetFiles(desktopPath, "*", SearchOption.TopDirectoryOnly)
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.Name);

                // Add folders first
                foreach (var folder in folders)
                {
                    if (!ShouldShowItem(folder.Name)) continue;

                    try
                    {
                        var iconHandle = ExtractIcon(IntPtr.Zero, "shell32.dll", 3);
                        var icon = System.Drawing.Icon.FromHandle(iconHandle);
                        var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());

                        desktopItems.Add(new DesktopItem
                        {
                            Name = GetDisplayName(folder.Name),
                            Icon = bitmapSource,
                            FullPath = folder.FullName,
                            IsFolder = true
                        });
                    }
                    catch (Exception ex)
                    {
                        continue;
                    }
                }

                // Then add files
                foreach (var file in files)
                {
                    if (!ShouldShowItem(file.Name)) continue;

                    try
                    {
                        int iconIndex = 0;
                        var iconHandle = ExtractAssociatedIcon(IntPtr.Zero, file.FullName, ref iconIndex);
                        var icon = System.Drawing.Icon.FromHandle(iconHandle);
                        var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());

                        desktopItems.Add(new DesktopItem
                        {
                            Name = GetDisplayName(file.Name),
                            Icon = bitmapSource,
                            FullPath = file.FullName,
                            IsFolder = false
                        });
                    }
                    catch (Exception ex)
                    {
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error refreshing desktop items: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            isVisible = false;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (source != null)
            {
                UnregisterHotKey(source.Handle, HOTKEY_ID);
                source.RemoveHook(HwndHook);
            }
            base.OnClosed(e);
        }

        private void DesktopItemsListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            LaunchSelectedItem();
        }

        private void DesktopItemsListView_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                LaunchSelectedItem();
            }
        }

        private void LaunchSelectedItem()
        {
            var selectedItem = DesktopItemsListView.SelectedItem as DesktopItem;
            if (selectedItem != null)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = selectedItem.FullPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error launching item: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void TrayIcon_ConfigChanged(object sender, EventArgs e)
        {
            // Re-register hotkey when config changes
            RegisterHotkey();
        }

        private void PasteArea_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var contextMenu = new System.Windows.Controls.ContextMenu();
            var pasteMenuItem = new System.Windows.Controls.MenuItem { Header = "Paste" };
            
            pasteMenuItem.Click += (s, args) =>
            {
                try
                {
                    if (System.Windows.Clipboard.ContainsFileDropList())
                    {
                        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        var files = System.Windows.Clipboard.GetFileDropList();
                        
                        foreach (string file in files)
                        {
                            string fileName = Path.GetFileName(file);
                            string destinationPath = Path.Combine(desktopPath, fileName);
                            
                            if (File.Exists(file))
                            {
                                File.Copy(file, destinationPath, true);
                            }
                            else if (Directory.Exists(file))
                            {
                                CopyDirectory(file, destinationPath);
                            }
                        }
                        
                        RefreshDesktopItems();
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error pasting files: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            
            contextMenu.Items.Add(pasteMenuItem);
            contextMenu.IsOpen = true;
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(destDir, fileName);
                File.Copy(file, destFile, true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dir);
                string destSubDir = Path.Combine(destDir, dirName);
                CopyDirectory(dir, destSubDir);
            }
        }
    }

    public class DesktopItem
    {
        public string Name { get; set; }
        public ImageSource Icon { get; set; }
        public string FullPath { get; set; }
        public bool IsFolder { get; set; }
    }
} 