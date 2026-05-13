using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class LoadWindowsWebView : MonoBehaviour
{
    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void InitWebView(IntPtr hwnd, string dataPath);

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetInitStatus();

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ShowAndNavigate(string url);

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void NavigateTo(string url);

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void HideWebView();

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ResizeWebView(int x, int y, int width, int height);

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void CloseWebView();

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void RegisterCloseButtonCallback(string unityObjectName, string unityMethodName);

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ExecuteScript(string script);

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void PostWebMessage(string message);

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetCookie(string cookieData);

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ClearCookies();

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern double GetCurrentFPS();

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ShowFPSDebugOverlay([MarshalAs(UnmanagedType.U1)] bool show);

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetRenderResolutionScale(float scale);

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetMaxRenderResolution(int maxWidth);

    [Header("WebView Settings")]
    public string url = "https://www.google.com";
    public string dataPath = "";

    [Header("Render Resolution Cap (for 4K screens)")]
    [Tooltip("0 = unlimited native resolution. 1920 = 2K cap. 2560 = QHD cap.")]
    public int maxRenderWidth = 1920;

    [Tooltip("Overrides maxRenderWidth if > 0. 0.5 = half internal resolution.")]
    public float manualRenderScale = 0f;

    private bool _initialized = false;

    void Start()
    {
        StartCoroutine(Initialize());
    }

    private System.Collections.IEnumerator Initialize()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        IntPtr hwnd = GetUnityWindowHandle();
        if (hwnd == IntPtr.Zero)
        {
            Debug.LogError("[LoadWindowsWebView] Could not find Unity window handle.");
            yield break;
        }

        string path = string.IsNullOrEmpty(dataPath)
            ? System.IO.Path.Combine(Application.persistentDataPath, "WebViewData")
            : dataPath;
        System.IO.Directory.CreateDirectory(path);

        InitWebView(hwnd, path);

        while (GetInitStatus() == 0)
            yield return null;

        int status = GetInitStatus();
        if (status == 1)
        {
            _initialized = true;
            Debug.Log("[LoadWindowsWebView] Ready.");

            if (manualRenderScale > 0f)
                SetRenderResolutionScale(manualRenderScale);
            else if (maxRenderWidth > 0)
                SetMaxRenderResolution(maxRenderWidth);

            ShowAndNavigate(url);
        }
        else
        {
            Debug.LogError($"[LoadWindowsWebView] Init failed: {status}");
        }
#else
        Debug.LogWarning("[LoadWindowsWebView] Windows standalone builds only.");
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

    // ---- Public helpers ----

    public void Navigate(string targetUrl)
    {
        if (!_initialized) return;
        NavigateTo(targetUrl);
    }

    public void Show(string targetUrl)
    {
        if (!_initialized) return;
        ShowAndNavigate(targetUrl);
    }

    public void Hide()
    {
        if (!_initialized) return;
        HideWebView();
    }

    public void Resize(int x, int y, int width, int height)
    {
        if (!_initialized) return;
        ResizeWebView(x, y, width, height);
    }

    public void RunScript(string script)
    {
        if (!_initialized) return;
        ExecuteScript(script);
    }

    public void PostMessage(string message)
    {
        if (!_initialized) return;
        PostWebMessage(message);
    }

    public void SetCookieRaw(string name, string value, string domain,
        string path = "/", bool secure = false, bool httpOnly = false, long expiresEpoch = 0)
    {
        if (!_initialized) return;
        string data = $"{name}\n{value}\n{domain}\n{path}\n{(secure ? 1 : 0)}\n{(httpOnly ? 1 : 0)}\n{expiresEpoch}";
        SetCookie(data);
    }

    public void DeleteAllCookies()
    {
        if (!_initialized) return;
        ClearCookies();
    }

    public double FPS => _initialized ? GetCurrentFPS() : 0.0;

    public void ShowDebugFPS(bool show)
    {
        if (!_initialized) return;
        ShowFPSDebugOverlay(show);
    }

    public void SetScale(float scale)
    {
        if (!_initialized) return;
        SetRenderResolutionScale(scale);
    }

    public void SetMaxRes(int maxWidth)
    {
        if (!_initialized) return;
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
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        if (proc.MainWindowHandle != IntPtr.Zero)
            return proc.MainWindowHandle;

        string title = Application.productName;
        if (string.IsNullOrEmpty(title)) title = proc.ProcessName;

        var sb = new System.Text.StringBuilder(256);
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (p.Id == proc.Id && p.MainWindowHandle != IntPtr.Zero)
                {
                    GetWindowText(p.MainWindowHandle, sb, 256);
                    if (sb.ToString().IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0)
                        return p.MainWindowHandle;
                }
            }
            catch { }
        }
        return IntPtr.Zero;
    }
#endif
}

// ---- Data models for JSON payloads from WebView ----

[System.Serializable]
public class _FlatDetails
{
    public int apartment_id;
    public int tower_id;
    public string flatNo;
    public string subArea;
    public string bua;
    public string carpetArea;
    public int balcony;
    public string zoomUrl;
    public string isometricUrl;
    public Interior[] interiors;
}

[System.Serializable]
public class Interior
{
    public string area_name;
    public string dimension;
}

[System.Serializable]
public class SelectedInterior
{
    public string area_name;
    public string dimension;
}
