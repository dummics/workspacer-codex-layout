#r "C:\Program Files\workspacer\workspacer.Shared.dll"
#r "System.Windows.Forms"
#r "System.Drawing"

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using workspacer;
using WinForms = System.Windows.Forms;
using WsKeys = workspacer.Keys;

static class CodexLayoutSettings
{
    public const string WorkspacePrefix = "codex-monitor-";
    public const int SoftColumnLimit = 5;
    public const int MinimumRecommendedPrimarySpanPx = 420;
    public const int MainWindowMinimumWidthPx = 800;
    public const int ActiveRelayoutThrottleMs = 120;
    public const bool DiagnosticsEnabled = false;

    public const WsKeys ToggleLayoutKey = WsKeys.F2;
    public static readonly KeyModifiers ToggleLayoutModifiers = KeyModifiers.Control;
}

static class CodexLayoutState
{
    public static readonly Dictionary<IntPtr, IWindowLocation> LastFloatingLocations = new Dictionary<IntPtr, IWindowLocation>();
    public static readonly Dictionary<IntPtr, int> LastKnownMonitorIndexes = new Dictionary<IntPtr, int>();
    public static readonly Dictionary<string, DateTime> LastLayoutCommitUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    public static readonly Dictionary<string, Dictionary<IntPtr, IWindowLocation>> LastCommittedLayoutLocationsByWorkspace =
        new Dictionary<string, Dictionary<IntPtr, IWindowLocation>>(StringComparer.OrdinalIgnoreCase);
    public static IntPtr PreferredMainHandle = IntPtr.Zero;
    public static bool PreferredMainHandleConfirmed;
}

static class CodexLayoutDiagnostics
{
    private static readonly Logger _logger = Logger.Create();
    private static readonly object _sync = new object();
    private static readonly Dictionary<string, string> _lastLayoutSignatures = new Dictionary<string, string>();
    private static readonly Dictionary<IntPtr, string> _lastRouteSignatures = new Dictionary<IntPtr, string>();
    private static string _traceFilePath;

    public static void ConfigLoaded(IConfigContext context)
    {
        if (!CodexLayoutSettings.DiagnosticsEnabled)
        {
            return;
        }

        var resolvedConfigDirectory = ResolveConfigDirectory();
        _traceFilePath = Path.Combine(resolvedConfigDirectory, "codex-layout.trace.log");
        WriteTrace($"config-loaded configDirectory={resolvedConfigDirectory}");
        WriteTrace($"monitor-topology {DescribeMonitorTopology(context.MonitorContainer.GetAllMonitors())}");
        _logger.Info("Codex config loaded. ConfigDirectory={0}", resolvedConfigDirectory);
    }

    public static void HotkeyFired(string name)
    {
        if (!CodexLayoutSettings.DiagnosticsEnabled)
        {
            return;
        }

        WriteTrace($"hotkey-fired name={name}");
        _logger.Info("Codex hotkey fired: {0}", name);
    }

    public static void LayoutSnapshot(string workspaceName, string monitorSignature, IEnumerable<IWindow> windows)
    {
        if (!CodexLayoutSettings.DiagnosticsEnabled)
        {
            return;
        }

        var snapshot = windows
            .Where(window => window != null)
            .Select(window => $"{window.Handle}:{window.Title}:{window.Location?.X},{window.Location?.Y},{window.Location?.Width}x{window.Location?.Height}")
            .ToList();

        var signature = string.Join(" | ", snapshot);
        lock (_sync)
        {
            if (_lastLayoutSignatures.TryGetValue(workspaceName, out var lastSignature) && signature == lastSignature)
            {
                return;
            }

            _lastLayoutSignatures[workspaceName] = signature;
        }

        monitorSignature = string.IsNullOrWhiteSpace(monitorSignature) ? "unknown" : monitorSignature;

        WriteTrace($"layout-snapshot workspace={workspaceName} monitor={monitorSignature} count={snapshot.Count} windows={signature}");
        _logger.Info("Codex layout snapshot workspace={0} monitor={1} ({2} windows): {3}", workspaceName, monitorSignature, snapshot.Count, signature);
    }

    public static void RouteDecision(IWindow window, IMonitor monitor, IWorkspace workspace)
    {
        if (!CodexLayoutSettings.DiagnosticsEnabled)
        {
            return;
        }

        if (window == null || workspace == null)
        {
            return;
        }

        var monitorSignature = monitor == null
            ? "unknown"
            : $"{monitor.Name}#{monitor.Index}@{monitor.X},{monitor.Y} {monitor.Width}x{monitor.Height}";
        var signature = $"{workspace.Name}|{monitorSignature}";

        lock (_sync)
        {
            if (_lastRouteSignatures.TryGetValue(window.Handle, out var lastSignature) && lastSignature == signature)
            {
                return;
            }

            _lastRouteSignatures[window.Handle] = signature;
        }

        WriteTrace($"route window={window.Handle}:{window.Title} workspace={workspace.Name} monitor={monitorSignature}");
        _logger.Info("Codex route window={0}:{1} workspace={2} monitor={3}", window.Handle, window.Title, workspace.Name, monitorSignature);
    }

    private static void WriteTrace(string message)
    {
        if (!CodexLayoutSettings.DiagnosticsEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_traceFilePath))
        {
            return;
        }

        File.AppendAllText(
            _traceFilePath,
            $"{DateTime.Now:O} {message}{Environment.NewLine}");
    }

    private static string ResolveConfigDirectory()
    {
        var userFolder = Environment.GetEnvironmentVariable("WORKSPACER_CONFIG", EnvironmentVariableTarget.User);
        if (string.IsNullOrWhiteSpace(userFolder))
        {
            userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        var newConfigDirectory = Path.Combine(userFolder, ".config", "workspacer");
        if (Directory.Exists(newConfigDirectory))
        {
            return newConfigDirectory;
        }

        var oldConfigDirectory = Path.Combine(userFolder, ".workspacer");
        if (Directory.Exists(oldConfigDirectory))
        {
            return oldConfigDirectory;
        }

        return newConfigDirectory;
    }

    private static string DescribeMonitorTopology(IEnumerable<IMonitor> monitors)
    {
        return string.Join(
            " | ",
            monitors
                .Where(monitor => monitor != null)
                .OrderBy(monitor => monitor.Index)
                .Select(monitor => $"{monitor.Name}#{monitor.Index}@{monitor.X},{monitor.Y} {monitor.Width}x{monitor.Height}"));
    }
}

static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
}

static class CodexLayoutHelpers
{
    public static IWindowLocation CloneLocation(IWindowLocation location)
    {
        if (location == null)
        {
            return null;
        }

        return new WindowLocation(location.X, location.Y, location.Width, location.Height, WindowState.Normal);
    }

    public static void RememberFloatingLocations(IEnumerable<IWindow> windows, bool overwrite)
    {
        foreach (var window in windows.Where(window => window != null))
        {
            if (overwrite || !CodexLayoutState.LastFloatingLocations.ContainsKey(window.Handle))
            {
                CodexLayoutState.LastFloatingLocations[window.Handle] = CloneLocation(window.Location);
            }
        }
    }

    public static void SeedMonitorAssignmentsFromCurrentLocations(IConfigContext context, IEnumerable<IWindow> windows)
    {
        if (context == null || windows == null)
        {
            return;
        }

        foreach (var window in windows.Where(window => window != null))
        {
            var monitor = ResolveMonitorFromLocations(context, window);
            if (monitor != null)
            {
                CodexLayoutState.LastKnownMonitorIndexes[window.Handle] = monitor.Index;
            }
        }
    }

    public static string GetWorkspaceNameForMonitor(IMonitor monitor)
    {
        return $"{CodexLayoutSettings.WorkspacePrefix}{monitor.Index + 1}";
    }

    public static bool IsCodexWorkspace(IWorkspace workspace)
    {
        return workspace != null
            && !string.IsNullOrWhiteSpace(workspace.Name)
            && workspace.Name.StartsWith(CodexLayoutSettings.WorkspacePrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<IWorkspace> GetCodexWorkspaces(IConfigContext context)
    {
        return context.WorkspaceContainer
            .GetAllWorkspaces()
            .Where(IsCodexWorkspace)
            .OrderBy(workspace => context.WorkspaceContainer.GetCurrentMonitorForWorkspace(workspace)?.Index ?? int.MaxValue)
            .ThenBy(workspace => workspace.Name);
    }

    public static IEnumerable<IWindow> GetManagedCodexWindows(IConfigContext context)
    {
        return GetCodexWorkspaces(context)
            .SelectMany(workspace => workspace.ManagedWindows)
            .Where(window => window != null)
            .GroupBy(window => window.Handle)
            .Select(group => group.First())
            .ToList();
    }

    public static IMonitor GetPreferredMonitor(IConfigContext context, IWindow window)
    {
        if (context?.Enabled == true
            && window != null
            && CodexLayoutState.LastKnownMonitorIndexes.TryGetValue(window.Handle, out var stickyMonitorIndex))
        {
            var stickyMonitor = context.MonitorContainer.GetMonitorAtIndex(stickyMonitorIndex);
            if (stickyMonitor != null)
            {
                return stickyMonitor;
            }
        }

        var resolvedMonitor = ResolveMonitorFromLocations(context, window);
        if (resolvedMonitor != null)
        {
            CodexLayoutState.LastKnownMonitorIndexes[window.Handle] = resolvedMonitor.Index;
            return resolvedMonitor;
        }

        if (window != null
            && CodexLayoutState.LastKnownMonitorIndexes.TryGetValue(window.Handle, out var lastMonitorIndex))
        {
            var lastMonitor = context.MonitorContainer.GetMonitorAtIndex(lastMonitorIndex);
            if (lastMonitor != null)
            {
                return lastMonitor;
            }
        }

        return context.MonitorContainer.FocusedMonitor
            ?? context.MonitorContainer.GetMonitorAtIndex(0);
    }

    private static IMonitor ResolveMonitorFromLocations(IConfigContext context, IWindow window)
    {
        if (context == null || window == null)
        {
            return null;
        }

        if (CodexLayoutState.LastFloatingLocations.TryGetValue(window.Handle, out var savedLocation)
            && savedLocation != null
            && savedLocation.Width > 0
            && savedLocation.Height > 0)
        {
            var monitor = context.MonitorContainer.GetMonitorAtRect(
                savedLocation.X,
                savedLocation.Y,
                savedLocation.Width,
                savedLocation.Height)
                ?? context.MonitorContainer.GetMonitorAtPoint(
                    savedLocation.X + Math.Max(1, savedLocation.Width / 2),
                    savedLocation.Y + Math.Max(1, savedLocation.Height / 2));

            if (monitor != null)
            {
                return monitor;
            }
        }

        if (window.Location != null && window.Location.Width > 0 && window.Location.Height > 0)
        {
            return context.MonitorContainer.GetMonitorAtRect(
                window.Location.X,
                window.Location.Y,
                window.Location.Width,
                window.Location.Height)
                ?? context.MonitorContainer.GetMonitorAtPoint(
                    window.Location.X + Math.Max(1, window.Location.Width / 2),
                    window.Location.Y + Math.Max(1, window.Location.Height / 2));
        }

        return null;
    }

    public static IWorkspace GetWorkspaceForWindow(IConfigContext context, IWindow window)
    {
        var monitor = GetPreferredMonitor(context, window);
        if (monitor == null)
        {
            return null;
        }

        var workspace = context.WorkspaceContainer[GetWorkspaceNameForMonitor(monitor)];
        CodexLayoutDiagnostics.RouteDecision(window, monitor, workspace);
        return workspace;
    }

    public static bool IsOfficialCodexWindow(IWindow window)
    {
        if (window == null)
        {
            return false;
        }

        return window.ProcessName == "Codex"
            && window.ProcessFileName == "Codex.exe"
            && window.Class == "Chrome_WidgetWin_1"
            && !string.IsNullOrWhiteSpace(window.Title);
    }

    public static bool IsForegroundCodexWindow()
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            var process = Process.GetProcessById((int)processId);
            var className = new StringBuilder(256);
            var title = new StringBuilder(512);

            NativeMethods.GetClassName(handle, className, className.Capacity);
            NativeMethods.GetWindowText(handle, title, title.Capacity);

            return process.ProcessName == "Codex"
                && string.Equals(process.MainModule?.ModuleName, "Codex.exe", StringComparison.OrdinalIgnoreCase)
                && className.ToString() == "Chrome_WidgetWin_1"
                && !string.IsNullOrWhiteSpace(title.ToString());
        }
        catch
        {
            return false;
        }
    }

    public static List<IWindowLocation> CloneCurrentLocations(IEnumerable<IWindow> windows)
    {
        return windows
            .Where(window => window != null)
            .Select(window => CloneLocation(window.Location))
            .ToList();
    }

    public static Dictionary<IntPtr, IWindowLocation> CreateCommittedLayoutMap(
        IEnumerable<IWindow> windows,
        IEnumerable<IWindowLocation> locations)
    {
        return windows
            .Where(window => window != null)
            .Zip(locations, (window, location) => new
            {
                window.Handle,
                Location = CloneLocation(location)
            })
            .ToDictionary(item => item.Handle, item => item.Location);
    }

    public static List<IWindowLocation> TryGetCommittedLayoutLocations(string workspaceName, IEnumerable<IWindow> windows)
    {
        if (string.IsNullOrWhiteSpace(workspaceName)
            || windows == null
            || !CodexLayoutState.LastCommittedLayoutLocationsByWorkspace.TryGetValue(workspaceName, out var layoutMap)
            || layoutMap == null
            || layoutMap.Count == 0)
        {
            return null;
        }

        var snapshot = windows.Where(window => window != null).ToList();
        if (snapshot.Count == 0 || snapshot.Any(window => !layoutMap.ContainsKey(window.Handle)))
        {
            return null;
        }

        return snapshot
            .Select(window => CloneLocation(layoutMap[window.Handle]))
            .ToList();
    }
}

class CodexColumnsLayoutEngine : ILayoutEngine
{
    private readonly HorzLayoutEngine _columns = new HorzLayoutEngine();
    private readonly VertLayoutEngine _rows = new VertLayoutEngine();
    private readonly string _workspaceName;
    private readonly string _monitorSignature;
    private readonly int _monitorOriginX;
    private readonly int _monitorOriginY;
    private readonly int _softColumnLimit;
    private readonly int _minimumRecommendedPrimarySpanPx;
    private const double SingleWindowWidthRatio = 0.86;
    private const double SingleWindowHeightRatio = 0.88;

    private int _lastManagedWindowCount;
    private int _lastWarningWindowCount;

    public CodexColumnsLayoutEngine(
        string workspaceName,
        string monitorSignature,
        int monitorOriginX,
        int monitorOriginY,
        int softColumnLimit,
        int minimumRecommendedPrimarySpanPx)
    {
        _workspaceName = workspaceName;
        _monitorSignature = monitorSignature;
        _monitorOriginX = monitorOriginX;
        _monitorOriginY = monitorOriginY;
        _softColumnLimit = softColumnLimit;
        _minimumRecommendedPrimarySpanPx = minimumRecommendedPrimarySpanPx;
    }

    public string Name => "Codex Adaptive";

    public IEnumerable<IWindowLocation> CalcLayout(IEnumerable<IWindow> windows, int spaceWidth, int spaceHeight)
    {
        var snapshot = windows.Where(window => window != null).ToList();
        CodexLayoutDiagnostics.LayoutSnapshot(
            _workspaceName,
            _monitorSignature,
            snapshot);

        if (snapshot.Count == 0)
        {
            _lastWarningWindowCount = 0;
            _lastManagedWindowCount = 0;
            CodexLayoutState.LastCommittedLayoutLocationsByWorkspace.Remove(_workspaceName);
            return Enumerable.Empty<IWindowLocation>();
        }

        var isForegroundCodex = CodexLayoutHelpers.IsForegroundCodexWindow();
        if (!isForegroundCodex
            && CodexLayoutState.LastLayoutCommitUtc.ContainsKey(_workspaceName))
        {
            _lastManagedWindowCount = snapshot.Count;
            return CodexLayoutHelpers.TryGetCommittedLayoutLocations(_workspaceName, snapshot)
                ?? CodexLayoutHelpers.CloneCurrentLocations(snapshot);
        }

        var shouldThrottle = isForegroundCodex
            && ShouldThrottleRelayout(CodexLayoutSettings.ActiveRelayoutThrottleMs);

        if (snapshot.Count == 1)
        {
            _lastWarningWindowCount = 0;
            CodexLayoutState.LastCommittedLayoutLocationsByWorkspace.Remove(_workspaceName);
            var loneWindow = snapshot[0];
            IWindowLocation savedLocation = null;
            var shouldRestoreFloatingLocation =
                _lastManagedWindowCount >= 2
                && CodexLayoutState.LastFloatingLocations.TryGetValue(loneWindow.Handle, out savedLocation)
                && savedLocation != null;
            var shouldReleaseResidualTile =
                _lastManagedWindowCount == 0
                && LooksLikeResidualTile(loneWindow, spaceWidth, spaceHeight, _monitorOriginX, _monitorOriginY);

            _lastManagedWindowCount = 1;

            if (shouldRestoreFloatingLocation)
            {
                return new[] { CodexLayoutHelpers.CloneLocation(savedLocation) };
            }

            if (shouldReleaseResidualTile)
            {
                return new[] { CreateFreeSingleWindowLocation(spaceWidth, spaceHeight, _monitorOriginX, _monitorOriginY) };
            }

            return Enumerable.Empty<IWindowLocation>();
        }

        if (shouldThrottle)
        {
            _lastManagedWindowCount = snapshot.Count;
            return CodexLayoutHelpers.TryGetCommittedLayoutLocations(_workspaceName, snapshot)
                ?? CodexLayoutHelpers.CloneCurrentLocations(snapshot);
        }

        // Never overwrite existing floating snapshots during tiling cycles.
        // This avoids replacing user positioning hints with transient tile coordinates.
        CodexLayoutHelpers.RememberFloatingLocations(snapshot, overwrite: false);

        var useRows = spaceHeight > spaceWidth;
        var primarySpan = Math.Max(1, (useRows ? spaceHeight : spaceWidth) / snapshot.Count);
        var aboveSoftLimit = _softColumnLimit > 0 && snapshot.Count > _softColumnLimit;
        var belowRecommendedSpan = primarySpan < _minimumRecommendedPrimarySpanPx;

        if ((aboveSoftLimit || belowRecommendedSpan) && _lastWarningWindowCount != snapshot.Count)
        {
            _lastWarningWindowCount = snapshot.Count;
        }
        else if (!aboveSoftLimit && !belowRecommendedSpan)
        {
            _lastWarningWindowCount = 0;
        }

        _lastManagedWindowCount = snapshot.Count;

        var orderedWindows = OrderWindowsForLayout(snapshot);
        var orderedLocations = useRows
            ? _rows.CalcLayout(orderedWindows, spaceWidth, spaceHeight)
            : CalcLandscapeLayout(orderedWindows, spaceWidth, spaceHeight);

        var mappedLocations = MapLocationsToOriginalOrder(snapshot, orderedWindows, orderedLocations);
        CodexLayoutState.LastLayoutCommitUtc[_workspaceName] = DateTime.UtcNow;
        CodexLayoutState.LastCommittedLayoutLocationsByWorkspace[_workspaceName] =
            CodexLayoutHelpers.CreateCommittedLayoutMap(snapshot, mappedLocations);
        return mappedLocations;
    }

    public void ExpandPrimaryArea()
    {
        _columns.ExpandPrimaryArea();
        _rows.ExpandPrimaryArea();
    }

    public void ShrinkPrimaryArea()
    {
        _columns.ShrinkPrimaryArea();
        _rows.ShrinkPrimaryArea();
    }

    public void IncrementNumInPrimary()
    {
        _columns.IncrementNumInPrimary();
        _rows.IncrementNumInPrimary();
    }

    public void DecrementNumInPrimary()
    {
        _columns.DecrementNumInPrimary();
        _rows.DecrementNumInPrimary();
    }

    public void ResetPrimaryArea()
    {
        _columns.ResetPrimaryArea();
        _rows.ResetPrimaryArea();
    }

    private static bool LooksLikeResidualTile(IWindow window, int spaceWidth, int spaceHeight, int monitorOriginX, int monitorOriginY)
    {
        var location = window?.Location;
        if (location == null || spaceWidth <= 0 || spaceHeight <= 0)
        {
            return false;
        }

        var widthRatio = (double)location.Width / spaceWidth;
        var heightRatio = (double)location.Height / spaceHeight;
        var nearTop = Math.Abs(location.Y - monitorOriginY) <= 12;
        var nearLeft = Math.Abs(location.X - monitorOriginX) <= 12;
        var nearRight = Math.Abs((location.X + location.Width) - (monitorOriginX + spaceWidth)) <= 12;

        var looksFullscreen = widthRatio >= 0.95 && heightRatio >= 0.95;
        var looksColumn = heightRatio >= 0.95 && widthRatio <= 0.70 && (nearLeft || nearRight) && nearTop;

        return looksFullscreen || looksColumn;
    }

    private static IWindowLocation CreateFreeSingleWindowLocation(int spaceWidth, int spaceHeight, int monitorOriginX, int monitorOriginY)
    {
        var width = Math.Max(960, (int)Math.Round(spaceWidth * SingleWindowWidthRatio));
        var height = Math.Max(720, (int)Math.Round(spaceHeight * SingleWindowHeightRatio));
        width = Math.Min(width, spaceWidth);
        height = Math.Min(height, spaceHeight);

        var x = monitorOriginX + Math.Max(0, (spaceWidth - width) / 2);
        var y = monitorOriginY + Math.Max(0, (spaceHeight - height) / 2);

        return new WindowLocation(x, y, width, height, WindowState.Normal);
    }

    private IEnumerable<IWindowLocation> CalcLandscapeLayout(List<IWindow> orderedWindows, int spaceWidth, int spaceHeight)
    {
        if (!orderedWindows.Any())
        {
            return Enumerable.Empty<IWindowLocation>();
        }

        var mainWindow = orderedWindows.FirstOrDefault();
        var mainWindowIsPinned = IsPinnedMainWindow(mainWindow);
        if (!mainWindowIsPinned || orderedWindows.Count == 1)
        {
            return _columns.CalcLayout(orderedWindows, spaceWidth, spaceHeight);
        }

        var equalShare = Math.Max(1, spaceWidth / orderedWindows.Count);
        var mainWidth = Math.Min(spaceWidth, Math.Max(CodexLayoutSettings.MainWindowMinimumWidthPx, equalShare));
        var otherCount = orderedWindows.Count - 1;
        var remainingWidth = Math.Max(0, spaceWidth - mainWidth);
        var otherWidth = otherCount > 0 ? remainingWidth / otherCount : remainingWidth;

        var locations = new List<IWindowLocation>(orderedWindows.Count)
        {
            new WindowLocation(0, 0, mainWidth, spaceHeight, WindowState.Normal)
        };

        var currentX = mainWidth;
        for (var index = 0; index < otherCount; index++)
        {
            var width = index == otherCount - 1
                ? Math.Max(0, spaceWidth - currentX)
                : otherWidth;

            locations.Add(new WindowLocation(currentX, 0, width, spaceHeight, WindowState.Normal));
            currentX += width;
        }

        return locations;
    }

    private bool ShouldThrottleRelayout(int throttleMs)
    {
        if (throttleMs <= 0)
        {
            return false;
        }

        if (!CodexLayoutState.LastLayoutCommitUtc.TryGetValue(_workspaceName, out var lastCommitUtc))
        {
            return false;
        }

        return (DateTime.UtcNow - lastCommitUtc).TotalMilliseconds < throttleMs;
    }

    private static List<IWindow> OrderWindowsForLayout(List<IWindow> windows)
    {
        var mainWindow = GetPinnedMainWindow(windows);

        if (mainWindow == null)
        {
            return windows
                .OrderBy(GetWindowPreferredX)
                .ThenBy(GetWindowPreferredY)
                .ThenBy(window => window.Title ?? string.Empty)
                .ToList();
        }

        CodexLayoutState.PreferredMainHandle = mainWindow.Handle;
        CodexLayoutState.PreferredMainHandleConfirmed = true;

        // Keep incoming workspace order for secondaries so swap/reorder operations can be reflected.
        var secondaryWindows = windows
            .Where(window => window != mainWindow)
            .ToList();

        return new[] { mainWindow }
            .Concat(secondaryWindows)
            .ToList();
    }

    private static IWindow GetPinnedMainWindow(List<IWindow> windows)
    {
        if (windows == null || windows.Count == 0)
        {
            return null;
        }

        var explicitMainWindow = windows
            .Where(window => CodexLayoutHelpers.IsOfficialCodexWindow(window))
            .Where(window => GetExplicitMainWindowRank(window) < int.MaxValue)
            .OrderBy(GetExplicitMainWindowRank)
            .ThenBy(GetWindowPreferredX)
            .ThenBy(GetWindowPreferredY)
            .FirstOrDefault();

        if (explicitMainWindow != null)
        {
            CodexLayoutState.PreferredMainHandle = explicitMainWindow.Handle;
            CodexLayoutState.PreferredMainHandleConfirmed = true;
            return explicitMainWindow;
        }

        if (!CodexLayoutState.PreferredMainHandleConfirmed
            || CodexLayoutState.PreferredMainHandle == IntPtr.Zero)
        {
            return null;
        }

        return windows.FirstOrDefault(window => window?.Handle == CodexLayoutState.PreferredMainHandle);
    }

    private static IEnumerable<IWindowLocation> MapLocationsToOriginalOrder(
        List<IWindow> originalWindows,
        List<IWindow> orderedWindows,
        IEnumerable<IWindowLocation> orderedLocations)
    {
        var locationsByHandle = orderedWindows
            .Zip(orderedLocations, (window, location) => new { window.Handle, Location = location })
            .ToDictionary(item => item.Handle, item => item.Location);

        return originalWindows.Select(window => locationsByHandle[window.Handle]).ToList();
    }

    private static int GetMainWindowRank(IWindow window)
    {
        if (window == null)
        {
            return int.MaxValue;
        }

        if (CodexLayoutState.PreferredMainHandleConfirmed
            && window.Handle == CodexLayoutState.PreferredMainHandle)
        {
            return 0;
        }

        return GetExplicitMainWindowRank(window);
    }

    private static bool IsPinnedMainWindow(IWindow window)
    {
        return GetMainWindowRank(window) < int.MaxValue;
    }

    private static int GetExplicitMainWindowRank(IWindow window)
    {
        if (window == null)
        {
            return int.MaxValue;
        }

        if (string.Equals(window.Title, "Codex", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(window.Title)
            && window.Title.IndexOf("Codex", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return 2;
        }

        return int.MaxValue;
    }

    private static int GetWindowPreferredX(IWindow window)
    {
        if (window != null
            && CodexLayoutState.LastFloatingLocations.TryGetValue(window.Handle, out var savedLocation)
            && savedLocation != null)
        {
            return savedLocation.X;
        }

        return window?.Location?.X ?? int.MaxValue;
    }

    private static int GetWindowPreferredY(IWindow window)
    {
        if (window != null
            && CodexLayoutState.LastFloatingLocations.TryGetValue(window.Handle, out var savedLocation)
            && savedLocation != null)
        {
            return savedLocation.Y;
        }

        return window?.Location?.Y ?? int.MaxValue;
    }
}

void SnapshotFloatingLocations(IConfigContext context)
{
    CodexLayoutHelpers.RememberFloatingLocations(CodexLayoutHelpers.GetManagedCodexWindows(context), overwrite: true);
}

void RestoreFloatingLocations(IConfigContext context)
{
    var windows = CodexLayoutHelpers
        .GetManagedCodexWindows(context)
        .Where(window => CodexLayoutState.LastFloatingLocations.ContainsKey(window.Handle))
        .ToList();

    if (!windows.Any())
    {
        return;
    }

    using (var defer = context.Windows.DeferWindowsPos(windows.Count))
    {
        foreach (var window in windows)
        {
            defer.DeferWindowPos(window, CodexLayoutState.LastFloatingLocations[window.Handle]);
        }
    }
}

void ToggleCodexLayout(IConfigContext context)
{
    CodexLayoutDiagnostics.HotkeyFired("toggle-layout");

    if (context.Enabled)
    {
        context.Enabled = false;
        RestoreFloatingLocations(context);
    }
    else
    {
        SnapshotFloatingLocations(context);
        CodexLayoutHelpers.SeedMonitorAssignmentsFromCurrentLocations(context, CodexLayoutHelpers.GetManagedCodexWindows(context));
        context.Enabled = true;
        context.Workspaces.ForceWorkspaceUpdate();

        foreach (var workspace in CodexLayoutHelpers.GetCodexWorkspaces(context))
        {
            workspace.DoLayout();
        }
    }

}

Action<IConfigContext> doConfig = (context) =>
{
    context.Branch = Branch.None;
    context.CanMinimizeWindows = true;
    context.FileLogLevel = LogLevel.Fatal;

    foreach (var monitor in context.MonitorContainer.GetAllMonitors().OrderBy(monitor => monitor.Index))
    {
        var workspaceName = CodexLayoutHelpers.GetWorkspaceNameForMonitor(monitor);
        context.WorkspaceContainer.CreateWorkspace(
            workspaceName,
            new CodexColumnsLayoutEngine(
                workspaceName,
                $"{monitor.Name}#{monitor.Index}@{monitor.X},{monitor.Y} {monitor.Width}x{monitor.Height}",
                monitor.X,
                monitor.Y,
                CodexLayoutSettings.SoftColumnLimit,
                CodexLayoutSettings.MinimumRecommendedPrimarySpanPx));

        context.WorkspaceContainer.AssignWorkspaceToMonitor(context.WorkspaceContainer[workspaceName], monitor);
    }

    context.WindowRouter.AddFilter(CodexLayoutHelpers.IsOfficialCodexWindow);
    context.WindowRouter.AddRoute(window => CodexLayoutHelpers.IsOfficialCodexWindow(window) ? CodexLayoutHelpers.GetWorkspaceForWindow(context, window) : null);

    // Workspacer registers a large set of global Alt-based defaults in the keybind manager
    // constructor. For this Codex-only setup we want a single explicit hotkey and no
    // background mouse/key behavior that can trigger unrelated upstream paths.
    context.Keybinds.UnsubscribeAll();
    context.Keybinds.Subscribe(KeyModifiers.LControl, CodexLayoutSettings.ToggleLayoutKey, () => ToggleCodexLayout(context), "toggle Codex layout");
    context.Keybinds.Subscribe(KeyModifiers.RControl, CodexLayoutSettings.ToggleLayoutKey, () => ToggleCodexLayout(context), "toggle Codex layout");

    context.SystemTray.AddToContextMenu(
        "Toggle Codex layout (Ctrl+F2)",
        () => ToggleCodexLayout(context));

    CodexLayoutDiagnostics.ConfigLoaded(context);
};

return doConfig;
