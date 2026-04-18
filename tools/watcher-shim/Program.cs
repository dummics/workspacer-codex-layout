using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace WorkspacerWatcherShim;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly object LogSync = new();
    private static readonly string LogPath = ResolveLogPath();

    private static bool _consoleVisible;
    private static bool _isRunning = true;

    [STAThread]
    private static void Main(string[] args)
    {
        WriteLog($"watcher-start pid={Environment.ProcessId} cwd={Environment.CurrentDirectory}");
        TryHideConsoleWindow();

        List<long>? activeHandles = null;

        while (_isRunning)
        {
            string? line;
            try
            {
                line = Console.ReadLine();
            }
            catch (Exception ex)
            {
                ShowFatalError("Errore lettura pipe watcher", ex.ToString(), allowRestart: true);
                break;
            }

            if (line is null)
            {
                WriteLog("stdin-eof");
                _isRunning = false;
                continue;
            }

            try
            {
                var response = JsonSerializer.Deserialize<LauncherResponse>(line, JsonOptions);
                if (response is null)
                {
                    continue;
                }

                switch (response.Action)
                {
                    case LauncherAction.Quit:
                        WriteLog("action=quit");
                        _isRunning = false;
                        CleanupWindowHandles(activeHandles);
                        Application.Exit();
                        break;
                    case LauncherAction.QuitWithException:
                        WriteLog($"action=quit-with-exception message={Sanitize(response.Message)}");
                        _isRunning = false;
                        CleanupWindowHandles(activeHandles);
                        ShowFatalError("Eccezione Workspacer", response.Message ?? "Eccezione sconosciuta.", allowRestart: true);
                        break;
                    case LauncherAction.Restart:
                        WriteLog("action=restart");
                        _isRunning = false;
                        CleanupWindowHandles(activeHandles);
                        RestartWorkspacer();
                        break;
                    case LauncherAction.RestartWithMessage:
                        WriteLog($"action=restart-with-message message={Sanitize(response.Message)}");
                        _isRunning = false;
                        CleanupWindowHandles(activeHandles);
                        ShowRestartNotice(response.Message ?? "Workspacer verra' riavviato.");
                        break;
                    case LauncherAction.UpdateHandles:
                        activeHandles = response.ActiveHandles;
                        WriteLog($"action=update-handles count={activeHandles?.Count ?? 0}");
                        break;
                    case LauncherAction.ToggleConsole:
                        WriteLog("action=toggle-console");
                        ToggleConsoleWindow();
                        break;
                    case LauncherAction.Log:
                        if (_consoleVisible && !string.IsNullOrWhiteSpace(response.Message))
                        {
                            Console.Write(response.Message);
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Azione watcher sconosciuta: {response.Action}");
                }
            }
            catch (Exception ex)
            {
                CleanupWindowHandles(activeHandles);
                WriteLog($"watcher-loop-exception error={Sanitize(ex.ToString())}");
                ShowFatalError("Eccezione watcher shim", ex.ToString(), allowRestart: true);
                break;
            }
        }

        CleanupWindowHandles(activeHandles);
        WriteLog("watcher-exit");
    }

    private static void CleanupWindowHandles(List<long>? handles)
    {
        if (handles is null)
        {
            return;
        }

        foreach (var value in handles)
        {
            var handle = new IntPtr(value);
            if (!IsWindow(handle))
            {
                continue;
            }

            if (IsIconic(handle))
            {
                ShowWindowAsync(handle, SwRestore);
            }
            else
            {
                ShowWindowAsync(handle, SwShow);
            }
        }
    }

    private static void RestartWorkspacer()
    {
        WriteLog("restart-workspacer begin");
        var processes = Process.GetProcessesByName("workspacer");
        while (processes.Length > 0)
        {
            Thread.Sleep(100);
            processes = Process.GetProcessesByName("workspacer");
        }

        var process = new Process
        {
            StartInfo =
            {
                FileName = "workspacer.exe",
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = true
            }
        };

        process.Start();
        WriteLog("restart-workspacer started-new-process");
        Application.Exit();
    }

    private static void ShowRestartNotice(string message)
    {
        MessageBox.Show(
            message,
            "workspacer",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        RestartWorkspacer();
    }

    private static void ShowFatalError(string messageTitle, string messageBody, bool allowRestart)
    {
        WriteLog($"fatal-error title={Sanitize(messageTitle)} allowRestart={allowRestart} body={Sanitize(messageBody)}");
        if (allowRestart)
        {
            var result = MessageBox.Show(
                $"{messageBody}\n\nVuoi riavviare Workspacer?",
                messageTitle,
                MessageBoxButtons.RetryCancel,
                MessageBoxIcon.Error);

            if (result == DialogResult.Retry)
            {
                RestartWorkspacer();
                return;
            }
        }

        Application.Exit();
    }

    private static void TryHideConsoleWindow()
    {
        var handle = GetConsoleWindow();
        if (handle == IntPtr.Zero)
        {
            WriteLog("console-handle=none");
            return;
        }

        ShowWindow(handle, SwHide);
        _consoleVisible = false;
        WriteLog("console-hidden");
    }

    private static void ToggleConsoleWindow()
    {
        var handle = GetConsoleWindow();
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _consoleVisible = !_consoleVisible;
        ShowWindow(handle, _consoleVisible ? SwShow : SwHide);
        WriteLog($"console-toggle visible={_consoleVisible}");
    }

    private static void WriteLog(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (LogSync)
            {
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    private static string ResolveLogPath()
    {
        var root = Environment.GetEnvironmentVariable("WORKSPACER_CONFIG", EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(root))
        {
            return Path.Combine(root, ".config", "workspacer", "watcher-shim.log");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".workspacer",
            "watcher-shim.log");
    }

    private static string Sanitize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Replace(Environment.NewLine, " | ").Replace("\n", " | ").Replace("\r", " ");
    }

    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
}

internal enum LauncherAction
{
    Quit = 0,
    QuitWithException = 1,
    Restart = 2,
    RestartWithMessage = 3,
    UpdateHandles = 4,
    ToggleConsole = 5,
    Log = 6,
}

internal sealed class LauncherResponse
{
    public LauncherAction Action { get; set; }
    public string? Message { get; set; }
    public List<long>? ActiveHandles { get; set; }
}
