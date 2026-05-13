
#pragma once
#include <windows.h>

// Standard macro for exporting functions from the DLL
#define DLLExport __declspec(dllexport)

extern "C" {
    // Initializes the WebView2 environment and controller.
    // dataPath: Path for the browser cache/user data.
    DLLExport void InitWebView(HWND hwnd, const char* dataPath);

    // Returns the initialization status to Unity.
    // 0: Loading, 1: Ready, -1: Environment Error, -2: Controller Error.
    DLLExport int GetInitStatus();

    // Navigates to a URL and makes the WebView visible.
    DLLExport void ShowAndNavigate(const char* url);

    // Navigates to URL without changing visibility.
    DLLExport void NavigateTo(const char* url);

    // Hides the WebView without destroying the engine (Fast Re-open).
    DLLExport void HideWebView();

    // Updates the size and position of the WebView.
    DLLExport void ResizeWebView(int x, int y, int width, int height);

    // Completely shuts down the WebView and releases resources.
    DLLExport void CloseWebView();

    // Register a Unity callback to be invoked when the native close button is pressed.
    // (DEPRECATED - close button has been removed)
    DLLExport void RegisterCloseButtonCallback(const char* unityObjectName, const char* unityMethodName);

    // Execute raw JavaScript in the webview.
    DLLExport void ExecuteScript(const char* script);

    // Post a web message to the page (postMessage API).
    DLLExport void PostWebMessage(const char* message);

    // Set a cookie in the webview.
    // cookieData format: "name\nvalue\ndomain\npath\nsecure(0/1)\nhttpOnly(0/1)\nexpiresEpochSeconds"
    DLLExport void SetCookie(const char* cookieData);

    // Clear all cookies from the webview.
    DLLExport void ClearCookies();

    // Get current WebView FPS (updated every 1 second).
    DLLExport double GetCurrentFPS();

    // Show/hide FPS debug overlay in the webview (green text, top-left corner).
    DLLExport void ShowFPSDebugOverlay(bool show);

    // Set the internal render scale (0.25 - 2.0). Lower values reduce GPU load at high-DPI.
    // This uses native WebView2 rasterization scale (not CSS zoom).
    DLLExport void SetRenderResolutionScale(float scale);

    // Automatically scale down rendering so the internal width never exceeds maxWidth pixels.
    // Call this once after InitWebView; it applies on every Resize automatically.
    DLLExport void SetMaxRenderResolution(int maxWidth);
}