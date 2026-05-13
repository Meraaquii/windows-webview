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
    private static extern void ShowFPSDebugOverlay([MarshalAs(UnmanagedType.U1)] bool show);

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern double GetCurrentFPS();

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetRenderResolutionScale(float scale);

    [DllImport("UnityWebView", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetMaxRenderResolution(int maxWidth);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    public string uri = "https://www.google.com";
    private string loaderUri = "https://interactive.meraaquii.com/webgl/meraaquiiLoader/";
    private bool _isReady = false;
    private bool _isVisible = false;
    private bool _fpsOverlayShown = false;
    private float _fpsLogTimer = 0f;
    private const float FPS_LOG_INTERVAL = 2f;

    [Header("Render Resolution Cap (for 4K screens)")]
    [Tooltip("0 = unlimited native resolution. 1920 = 2K cap. 2560 = QHD cap.")]
    public int maxRenderWidth = 1920;

    [Tooltip("Overrides maxRenderWidth if > 0. 0.5 = half internal resolution.")]
    public float manualRenderScale = 0f;

    void Start()
    {
#if UNITY_STANDALONE_WIN
        try
        {
            IntPtr hwnd = GetActiveWindow();
            string dataPath = System.IO.Path.Combine(Application.persistentDataPath, "WebViewCache");
            if (!System.IO.Directory.Exists(dataPath)) 
                System.IO.Directory.CreateDirectory(dataPath);

            Debug.Log("<color=cyan>WebView: Initializing...</color>");
            InitWebView(hwnd, dataPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>Failed to initialize WebView: {e.Message}</color>");
        }
#endif
    }

    void Update()
    {
#if !UNITY_STANDALONE_WIN
        return;
#endif

        if (!_isReady)
        {
            int status = GetInitStatus();
            if (status == 1)
            {
                _isReady = true;
                Debug.Log("<color=cyan>WebView status: Ready to use.</color>");

                if (manualRenderScale > 0f)
                    SetRenderResolutionScale(manualRenderScale);
                else if (maxRenderWidth > 0)
                    SetMaxRenderResolution(maxRenderWidth);
            }
            else if (status < 0)
            {
                Debug.LogError($"<color=red>WebView Init Failed! Code: {status}</color>");
                _isReady = true;
            }
        }

        // Update WebView size when visible
        if (_isVisible && _isReady && Time.frameCount % 30 == 0)
        {
            UpdateWebViewSize();
        }

        // Log FPS periodically (not every frame to avoid console spam)
        if (_isReady)
        {
            _fpsLogTimer += Time.deltaTime;
            if (_fpsLogTimer >= FPS_LOG_INTERVAL)
            {
                double fps = GetCurrentFPS();
                if (fps > 0)
                    Debug.Log($"<color=cyan>WebView FPS: {fps:F1}</color>");
                _fpsLogTimer = 0f;
            }
        }
    }

    public void OnViewOpenCalled(string url)
    {
#if !UNITY_STANDALONE_WIN
        Debug.LogWarning("WebView is only supported on Windows standalone builds.");
        return;
#endif

        if (!_isReady)
        {
            Debug.LogWarning("WebView not ready yet. Please wait...");
            return;
        }

        uri = url;
        Debug.Log($"<color=cyan>Opening WebView with URL:</color> {uri}");

        try
        {
            ShowAndNavigate(uri);
            UpdateWebViewSize();
            _isVisible = true;

            // Enable FPS overlay AFTER page loads (delay 1 second)
            if (!_fpsOverlayShown)
            {
                Invoke(nameof(EnableFPSOverlay), 1f);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>Failed to open WebView: {e.Message}</color>");
        }
    }

    private void EnableFPSOverlay()
    {
        if (_fpsOverlayShown) return;

        try
        {
            ShowFPSDebugOverlay(true);
            _fpsOverlayShown = true;
            Debug.Log("<color=green>FPS Debug Overlay Enabled</color>");
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>Failed to enable FPS overlay: {e.Message}</color>");
        }
    }

    private void DisableFPSOverlay()
    {
        if (!_fpsOverlayShown) return;

        try
        {
            ShowFPSDebugOverlay(false);
            _fpsOverlayShown = false;
            Debug.Log("<color=yellow>FPS Debug Overlay Disabled</color>");
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>Failed to disable FPS overlay: {e.Message}</color>");
        }
    }

    public void OnWebMessageReceived(string message)
    {
        Debug.Log($"<color=yellow>Raw message from WebView:</color> {message}");

        if (message == "close_button_pressed")
        {
            Debug.Log("Native close button pressed");
            OnCloseButtonClicked();
            return;
        }

        try
        {
            WebMessage webMsg = JsonUtility.FromJson<WebMessage>(message);
            Debug.Log($"<color=cyan>Parsed action:</color> {webMsg.action}");

            switch (webMsg.action)
            {
                case "close":
                    Debug.Log("React requested close");
                    OnCloseButtonClicked();
                    break;

                case "apartmentSelected":
                    Debug.Log("<color=green>Apartment data received!</color>");
                    HandleApartmentSelection(webMsg);
                    break;

                case "sendMessage":
                    Debug.Log($"React sent message: {webMsg.value}");
                    DisplayMessage(webMsg.value);
                    break;

                case "sendData":
                    Debug.Log($"React sent data - Name: {webMsg.name}, Value: {webMsg.value}");
                    HandleDataFromReact(webMsg.name, webMsg.value);
                    break;

                default:
                    Debug.LogWarning($"Unknown action: {webMsg.action}");
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>Error parsing web message: {e.Message}\nMessage: {message}</color>");
        }
    }

    private void HandleApartmentSelection(WebMessage msg)
    {
        Debug.Log("=== Apartment Selection Details ===");
        Debug.Log($"Tower: {msg.tower}");
        Debug.Log($"Floor: {msg.floor}");
        Debug.Log($"Apartment: {msg.apt}");
        Debug.Log($"Apartment Name: {msg.aptName}");
        Debug.Log($"Variant: {msg.variant}");

        if (msg.flatDetails != null)
        {
            Debug.Log($"Flat No: {msg.flatDetails.flatNo}");
            Debug.Log($"BUA: {msg.flatDetails.bua}");
            Debug.Log($"Carpet Area: {msg.flatDetails.carpetArea}");
            Debug.Log($"Balcony: {msg.flatDetails.balcony}");
        }

        if (msg.selectedInterior != null)
        {
            Debug.Log($"Selected Interior: {msg.selectedInterior.area_name}");
            Debug.Log($"Dimension: {msg.selectedInterior.dimension}");
        }

        Debug.Log("===================================");
    }

    private void DisplayMessage(string message)
    {
        Debug.Log($"<color=green>Displaying message:</color> {message}");
    }

    private void HandleDataFromReact(string name, string value)
    {
        Debug.Log($"<color=cyan>Processing data:</color> {name} = {value}");
    }

    public void OnCloseButtonClicked()
    {
        Debug.Log("<color=yellow>Closing WebView...</color>");
        DisableFPSOverlay();
        _isVisible = false;

#if UNITY_STANDALONE_WIN
        try
        {
            HideWebView();
            NavigateTo(loaderUri);
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>Error closing WebView: {e.Message}</color>");
        }
#endif
    }

    private void UpdateWebViewSize()
    {
        ResizeWebView(20, 100, Screen.width - 40, Screen.height - 150);
    }

    private void OnApplicationQuit()
    {
        Debug.Log("Application quitting, closing WebView...");
        try
        {
            CloseWebView();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error during WebView cleanup: {e.Message}");
        }
    }

    // ---- Resolution helpers ----

    public void SetScale(float scale)
    {
        if (!_isReady) return;
        SetRenderResolutionScale(scale);
    }

    public void SetMaxRes(int maxWidth)
    {
        if (!_isReady) return;
        SetMaxRenderResolution(maxWidth);
    }
}

// Data structures for parsing JSON messages from React
[System.Serializable]
public class WebMessage
{
    public string action;
    public string value;
    public string name;
    public string data;

    public string tower;
    public string floor;
    public string apt;
    public string aptName;
    public string variant;
    public _FlatDetails flatDetails;
    public SelectedInterior selectedInterior;
}

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