using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

public class WindowsWebView : MonoBehaviour
{
    private static WindowsWebView _instance;

    // Native DLL name (must match the built DLL, e.g. UnityWebView.dll)
    private const string DLL_NAME = "UnityWebView";

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void InitWebView(IntPtr hwnd, string dataPath);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetInitStatus();

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ShowAndNavigate(string url);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void NavigateTo(string url);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void HideWebView();

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ResizeWebView(int x, int y, int width, int height);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void CloseWebView();

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void RegisterCloseButtonCallback(string unityObjectName, string unityMethodName);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ExecuteScript(string script);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void PostWebMessage(string message);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetCookie(string cookieData);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ClearCookies();

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern double GetCurrentFPS();

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ShowFPSDebugOverlay([MarshalAs(UnmanagedType.U1)] bool show);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetRenderResolutionScale(float scale);

    [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetMaxRenderResolution(int maxWidth);

    public string Url = "https://www.google.com";
    public string DataPath = "";

    [Tooltip("Cap internal render width. 1920 for 2K, 2560 for QHD, 0 for native")]
    public int MaxRenderWidth = 1920;

    [Tooltip("Scale internal resolution. 0.5 = half-res. Overrides MaxRenderWidth if > 0")]
    public float ManualRenderScale = 0f;

    private bool _initialized = false;
    private IntPtr _hwnd = IntPtr.Zero;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    void Start()
    {
        StartCoroutine(InitializeWebView());
    }

    private System.Collections.IEnumerator InitializeWebView()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        _hwnd = GetUnityWindowHandle();
        if (_hwnd == IntPtr.Zero)
        {
            Debug.LogError("[WindowsWebView] Failed to find Unity game window.");
            yield break;
        }

        string path = string.IsNullOrEmpty(DataPath)
            ? System.IO.Path.Combine(Application.persistentDataPath, "WebViewData")
            : DataPath;

        System.IO.Directory.CreateDirectory(path);

        InitWebView(_hwnd, path);

        // Wait for async initialization
        while (GetInitStatus() == 0)
        {
            yield return null;
        }

        int status = GetInitStatus();
        if (status == 1)
        {
            _initialized = true;
            Debug.Log("[WindowsWebView] Initialized successfully.");

            if (ManualRenderScale > 0f)
                SetRenderResolutionScale(ManualRenderScale);
            else if (MaxRenderWidth > 0)
                SetMaxRenderResolution(MaxRenderWidth);

            ShowAndNavigate(Url);
        }
        else
        {
            Debug.LogError($"[WindowsWebView] Initialization failed with status: {status}");
        }
#else
        Debug.LogWarning("[WindowsWebView] Only supported on Windows standalone builds.");
        yield break;
#endif
    }

    void OnApplicationQuit()
    {
        CloseWebView();
    }

    void OnDestroy()
    {
        CloseWebView();
    }

    // ---- Public API ----

    public static void Navigate(string url)
    {
        if (!_instance?._initialized ?? false) return;
        NavigateTo(url);
    }

    public static void Show(string url)
    {
        if (!_instance?._initialized ?? false) return;
        ShowAndNavigate(url);
    }

    public static void Hide()
    {
        if (!_instance?._initialized ?? false) return;
        HideWebView();
    }

    public static void Resize(int x, int y, int width, int height)
    {
        if (!_instance?._initialized ?? false) return;
        ResizeWebView(x, y, width, height);
    }

    public static void RunJavaScript(string script)
    {
        if (!_instance?._initialized ?? false) return;
        ExecuteScript(script);
    }

    public static void SendMessage(string message)
    {
        if (!_instance?._initialized ?? false) return;
        PostWebMessage(message);
    }

    public static void AddCookie(string name, string value, string domain,
        string path = "/", bool secure = false, bool httpOnly = false, long expiresEpoch = 0)
    {
        if (!_instance?._initialized ?? false) return;
        string cookie = $"{name}\n{value}\n{domain}\n{path}\n{(secure ? 1 : 0)}\n{(httpOnly ? 1 : 0)}\n{expiresEpoch}";
        SetCookie(cookie);
    }

    public static void RemoveAllCookies()
    {
        if (!_instance?._initialized ?? false) return;
        ClearCookies();
    }

    public static double FPS => _instance?._initialized ?? false ? GetCurrentFPS() : 0.0;

    public static void ShowFPS(bool show)
    {
        if (!_instance?._initialized ?? false) return;
        ShowFPSDebugOverlay(show);
    }

    public static void SetQualityScale(float scale)
    {
        if (!_instance?._initialized ?? false) return;
        SetRenderResolutionScale(scale);
    }

    public static void SetMaxResolution(int maxWidth)
    {
        if (!_instance?._initialized ?? false) return;
        SetMaxRenderResolution(maxWidth);
    }

    // ---- Window handle helper ----

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    private static IntPtr GetUnityWindowHandle()
    {
        // Try by process + window title
        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        IntPtr hwnd = currentProcess.MainWindowHandle;
        if (hwnd != IntPtr.Zero) return hwnd;

        // Fallback: find by partial window title match
        string title = Application.productName;
        if (string.IsNullOrEmpty(title)) title = currentProcess.ProcessName;

        var sb = new System.Text.StringBuilder(256);
        foreach (var proc in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (proc.Id == currentProcess.Id && proc.MainWindowHandle != IntPtr.Zero)
                {
                    GetWindowText(proc.MainWindowHandle, sb, 256);
                    if (sb.ToString().IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0)
                        return proc.MainWindowHandle;
                }
            }
            catch { }
        }
        return IntPtr.Zero;
    }
#endif
}
