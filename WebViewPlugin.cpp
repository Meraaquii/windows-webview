#include <windows.h>
#include <WebView2.h>
#include <WebView2EnvironmentOptions.h>
#include <wrl.h>
#include <string>
#include <vector>
#include <mutex>
#include <cassert>

using namespace Microsoft::WRL;

// Standard COM smart pointers
static ComPtr<ICoreWebView2Controller> webviewController;
static ComPtr<ICoreWebView2> webviewWindow;
static bool isInitialized = false;
static int initStatus = 0;

// Unity callback storage
static std::string g_unityCallbackObject;
static std::string g_unityCallbackMethod;

// Resolve UnitySendMessage at runtime
typedef void(__cdecl* UnitySendMessageFunc)(const char* obj, const char* method, const char* msg);
static UnitySendMessageFunc pUnitySendMessage = nullptr;
static std::mutex g_unityResolveMutex;

// Message-only window for marshalling
static HWND g_messageWnd = nullptr;
static DWORD g_controllerThreadId = 0;
static bool g_comInitializedOnCreatorThread = false;
static WNDPROC g_prevMessageWndProc = nullptr;

// Frame rate throttling globals (ADD THESE AT GLOBAL SCOPE)
static LARGE_INTEGER g_performanceFrequency = { 0 };
static LARGE_INTEGER g_lastFrameTime = { 0 };
static const int TARGET_FPS = 30;  // Lower rendering FPS but drag stays responsive
static const double FRAME_TIME_MS = 1000.0 / TARGET_FPS; 

// FPS tracking globals
static int g_frameCount = 0;
static double g_fpsAccumulator = 0.0;
static double g_currentFPS = 0.0;
static LARGE_INTEGER g_fpsCheckTime = { 0 };
static const double FPS_UPDATE_INTERVAL_MS = 1000.0;  // Update FPS display every 1 second

static void EnsureUnitySendMessageResolved() {
    std::lock_guard<std::mutex> lock(g_unityResolveMutex);
    if (pUnitySendMessage) return;
    HMODULE h = GetModuleHandleW(L"UnityPlayer.dll");
    if (!h) h = GetModuleHandleW(NULL);
    if (h) {
        pUnitySendMessage = (UnitySendMessageFunc)GetProcAddress(h, "UnitySendMessage");
    }
}

static void SendUnityMessageIfAvailable(const char* obj, const char* method, const char* msg) {
    EnsureUnitySendMessageResolved();
    if (pUnitySendMessage && obj && method && msg) {
        pUnitySendMessage(obj, method, msg);
    }
}

static void ForwardToUnity(const std::string& message) {
    if (!g_unityCallbackObject.empty() && !g_unityCallbackMethod.empty()) {
        SendUnityMessageIfAvailable(g_unityCallbackObject.c_str(), g_unityCallbackMethod.c_str(), message.c_str());
    }
}

static std::wstring ToWString(const char* str) {
    if (!str) return L"";
    int size_needed = MultiByteToWideChar(CP_UTF8, 0, str, -1, NULL, 0);
    if (size_needed <= 0) return L"";
    std::wstring w(size_needed, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, str, -1, &w[0], size_needed);
    if (!w.empty() && w.back() == L'\0') w.pop_back();
    return w;
}

static std::string ToUTF8(LPCWSTR wstr) {
    if (!wstr) return "";
    int size_needed = WideCharToMultiByte(CP_UTF8, 0, wstr, -1, NULL, 0, NULL, NULL);
    if (size_needed <= 0) return "";
    std::string s(size_needed, '\0');
    WideCharToMultiByte(CP_UTF8, 0, wstr, -1, &s[0], size_needed, NULL, NULL);
    if (!s.empty() && s.back() == '\0') s.pop_back();
    return s;
}

// Frame rate throttling helper (DEFINE AT GLOBAL SCOPE)
static bool ShouldThrottleFrame() {
    if (g_performanceFrequency.QuadPart == 0) {
        QueryPerformanceFrequency(&g_performanceFrequency);
    }
    
    LARGE_INTEGER currentTime;
    QueryPerformanceCounter(&currentTime);
    
    if (g_lastFrameTime.QuadPart == 0) {
        g_lastFrameTime = currentTime;
        return false;
    }
    
    double elapsedMs = (double)(currentTime.QuadPart - g_lastFrameTime.QuadPart) 
                       * 1000.0 / g_performanceFrequency.QuadPart;
    
    if (elapsedMs < FRAME_TIME_MS) {
        return true;
    }
    
    g_lastFrameTime = currentTime;
    return false;
}

// Function to update FPS counter
static void UpdateFPSCounter() {
    if (g_performanceFrequency.QuadPart == 0) {
        QueryPerformanceFrequency(&g_performanceFrequency);
    }
    
    LARGE_INTEGER currentTime;
    QueryPerformanceCounter(&currentTime);
    
    if (g_fpsCheckTime.QuadPart == 0) {
        g_fpsCheckTime = currentTime;
        g_frameCount = 0;
        return;
    }
    
    g_frameCount++;
    double elapsedMs = (double)(currentTime.QuadPart - g_fpsCheckTime.QuadPart) 
                       * 1000.0 / g_performanceFrequency.QuadPart;
    
    if (elapsedMs >= FPS_UPDATE_INTERVAL_MS) {
        g_currentFPS = (g_frameCount * 1000.0) / elapsedMs;
        g_frameCount = 0;
        g_fpsCheckTime = currentTime;
    }
}

static LRESULT CALLBACK MessageWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);

enum class MarshalAction : UINT {
    ShowAndNavigate = WM_APP + 0,
    Navigate = WM_APP + 1,
    Resize = WM_APP + 2,
    Hide = WM_APP + 3,
    Close = WM_APP + 4,
    ExecuteScript = WM_APP + 5,
    PostWebMessage = WM_APP + 6,
    SetCookie = WM_APP + 7,
    ClearCookies = WM_APP + 8
};

struct MarshalPayload {
    MarshalAction action;
    wchar_t* text;
    int x, y, w, h;
};

static bool EnsureMessageWindow() {
    if (g_messageWnd) return true;
    HINSTANCE hInst = GetModuleHandle(NULL);
    g_messageWnd = CreateWindowExW(0, L"STATIC", L"", 0, 0, 0, 0, 0, HWND_MESSAGE, NULL, hInst, NULL);
    if (!g_messageWnd) return false;
    g_prevMessageWndProc = (WNDPROC)SetWindowLongPtrW(g_messageWnd, GWLP_WNDPROC, (LONG_PTR)MessageWndProc);
    return true;
}

static void FreeMarshalPayload(MarshalPayload* p) {
    if (!p) return;
    if (p->text) {
        delete[] p->text;
        p->text = nullptr;
    }
    delete p;
}

static void PostMarshal(MarshalPayload* payload) {
    if (!EnsureMessageWindow() || !g_messageWnd) {
        FreeMarshalPayload(payload);
        return;
    }
    PostMessageW(g_messageWnd, static_cast<UINT>(payload->action), 0, reinterpret_cast<LPARAM>(payload));
}

// FIXED: Don't throttle Resize (drag) - it needs immediate feedback
static void RunOrPostToControllerThread(MarshalPayload* payload) {
    // Only throttle non-interactive operations
    if (payload->action == MarshalAction::ShowAndNavigate) {
        if (ShouldThrottleFrame()) {
            FreeMarshalPayload(payload);
            return;
        }
    }
    // Resize (drag) is NOT throttled - always process immediately
    
    if (g_controllerThreadId == GetCurrentThreadId()) {
        SendMessageW(g_messageWnd, static_cast<UINT>(payload->action), 0, reinterpret_cast<LPARAM>(payload));
    } else {
        PostMarshal(payload);
    }
}

extern "C" {
    __declspec(dllexport) int GetInitStatus() {
        return initStatus;
    }

    __declspec(dllexport) void CloseWebView() {
        MarshalPayload* p = new MarshalPayload();
        p->action = MarshalAction::Close;
        p->text = nullptr;
        p->x = p->y = p->w = p->h = 0;
        RunOrPostToControllerThread(p);
    }

    __declspec(dllexport) void InitWebView(HWND hwnd, const char* dataPath) {
        initStatus = 0;
        isInitialized = false;
        std::wstring userDataFolder = ToWString(dataPath ? dataPath : "");

        EnsureMessageWindow();

        HRESULT hr = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
        if (SUCCEEDED(hr) || hr == RPC_E_CHANGED_MODE) {
            if (SUCCEEDED(hr)) g_comInitializedOnCreatorThread = true;
        }

        auto options = Make<CoreWebView2EnvironmentOptions>();
        if (options) {
            // CPU-ONLY MODE: Disable GPU acceleration completely
            options->put_AdditionalBrowserArguments(
                L"--disable-gpu "
                L"--disable-gpu-compositing "
                L"--disable-gpu-rasterization "
                L"--enable-low-end-device-mode "
                L"--disable-features=CalculateNativeWinOcclusion,Vulkan"
            );
        }

        CreateCoreWebView2EnvironmentWithOptions(nullptr, userDataFolder.c_str(), options.Get(),
            Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
                [hwnd](HRESULT result, ICoreWebView2Environment* env) -> HRESULT {
                    if (FAILED(result)) {
                        initStatus = -1;
                        return result;
                    }

                    env->CreateCoreWebView2Controller(hwnd, Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
                        [hwnd](HRESULT result, ICoreWebView2Controller* controller) -> HRESULT {
                            if (FAILED(result) || controller == nullptr) {
                                initStatus = -2;
                                return result;
                            }

                            g_controllerThreadId = GetCurrentThreadId();
                            webviewController = controller;
                            webviewController->get_CoreWebView2(&webviewWindow);

                            ComPtr<ICoreWebView2Settings> settings;
                            if (SUCCEEDED(webviewWindow->get_Settings(&settings))) {
                                settings->put_IsStatusBarEnabled(FALSE);
                                settings->put_AreDefaultContextMenusEnabled(FALSE);
                                settings->put_IsWebMessageEnabled(TRUE);
                            }

                            EventRegistrationToken messageToken;
                            webviewWindow->add_WebMessageReceived(Callback<ICoreWebView2WebMessageReceivedEventHandler>(
                                [](ICoreWebView2* sender, ICoreWebView2WebMessageReceivedEventArgs* args) -> HRESULT {
                                    LPWSTR messageRaw = nullptr;
                                    if (SUCCEEDED(args->TryGetWebMessageAsString(&messageRaw)) && messageRaw) {
                                        ForwardToUnity(ToUTF8(messageRaw));
                                        CoTaskMemFree(messageRaw);
                                    }
                                    return S_OK;
                                }).Get(), &messageToken);

                            webviewController->put_IsVisible(FALSE);
                            RECT bounds = { 0, 0, 1, 1 };
                            webviewController->put_Bounds(bounds);
                            webviewWindow->Navigate(L"about:blank");

                            isInitialized = true;
                            initStatus = 1;
                            return S_OK;
                        }).Get());
                    return S_OK;
                }).Get());
    }

    __declspec(dllexport) void ShowAndNavigate(const char* url) {
        MarshalPayload* p = new MarshalPayload();
        p->action = MarshalAction::ShowAndNavigate;
        std::wstring w = ToWString(url ? url : "");                     
        p->text = new wchar_t[w.size() + 1];
        wcscpy_s(p->text, w.size() + 1, w.c_str());
        p->x = p->y = p->w = p->h = 0;
        RunOrPostToControllerThread(p);
    }

    __declspec(dllexport) void NavigateTo(const char* url) {
        MarshalPayload* p = new MarshalPayload();
        p->action = MarshalAction::Navigate;
        std::wstring w = ToWString(url ? url : "");
        p->text = new wchar_t[w.size() + 1];
        wcscpy_s(p->text, w.size() + 1, w.c_str());
        p->x = p->y = p->w = p->h = 0;
        RunOrPostToControllerThread(p);
    }

    __declspec(dllexport) void HideWebView() {
        MarshalPayload* p = new MarshalPayload();
        p->action = MarshalAction::Hide;
        p->text = nullptr;
        p->x = p->y = p->w = p->h = 0;
        RunOrPostToControllerThread(p);
    }

    __declspec(dllexport) void ResizeWebView(int x, int y, int width, int height) {
        MarshalPayload* p = new MarshalPayload();
        p->action = MarshalAction::Resize;
        p->text = nullptr;
        p->x = x;
        p->y = y;
        p->w = width < 0 ? 0 : width;
        p->h = height < 0 ? 0 : height;
        RunOrPostToControllerThread(p);
    }

    __declspec(dllexport) void ExecuteScript(const char* script) {
        MarshalPayload* p = new MarshalPayload();
        p->action = MarshalAction::ExecuteScript;
        std::wstring w = ToWString(script ? script : "");
        p->text = new wchar_t[w.size() + 1];
        wcscpy_s(p->text, w.size() + 1, w.c_str());
        p->x = p->y = p->w = p->h = 0;
        RunOrPostToControllerThread(p);
    }

    __declspec(dllexport) void PostWebMessage(const char* message) {
        MarshalPayload* p = new MarshalPayload();
        p->action = MarshalAction::PostWebMessage;
        std::wstring w = ToWString(message ? message : "");
        p->text = new wchar_t[w.size() + 1];
        wcscpy_s(p->text, w.size() + 1, w.c_str());
        p->x = p->y = p->w = p->h = 0;
        RunOrPostToControllerThread(p);
    }

    __declspec(dllexport) void SetCookie(const char* cookieData) {
        MarshalPayload* p = new MarshalPayload();
        p->action = MarshalAction::SetCookie;
        std::wstring w = ToWString(cookieData ? cookieData : "");
        p->text = new wchar_t[w.size() + 1];
        wcscpy_s(p->text, w.size() + 1, w.c_str());
        p->x = p->y = p->w = p->h = 0;
        RunOrPostToControllerThread(p);
    }

    __declspec(dllexport) void ClearCookies() {
        MarshalPayload* p = new MarshalPayload();
        p->action = MarshalAction::ClearCookies;
        p->text = nullptr;
        p->x = p->y = p->w = p->h = 0;
        RunOrPostToControllerThread(p);
    }

    __declspec(dllexport) double GetCurrentFPS() {
        return g_currentFPS;
    }

    __declspec(dllexport) void ShowFPSDebugOverlay(bool show) {
        if (!webviewWindow || !isInitialized) return;
        
        if (show) {
            // Inject debug overlay CSS and HTML
            std::string script = R"(
                if (!document.getElementById('fps-debug-overlay')) {
                    const overlay = document.createElement('div');
                    overlay.id = 'fps-debug-overlay';
                    overlay.style.cssText = `
                        position: fixed;
                        top: 10px;
                        left: 10px;
                        background: rgba(0, 0, 0, 0.7);
                        color: #0f0;
                        font-family: monospace;
                        font-size: 14px;
                        padding: 8px 12px;
                        border-radius: 4px;
                        z-index: 9999;
                        font-weight: bold;
                        border: 1px solid #0f0;
                    `;
                    overlay.textContent = 'FPS: 0';
                    document.body.appendChild(overlay);
                    
                    // Update FPS every 100ms
                    window.fpsUpdateInterval = setInterval(() => {
                        if (window.frameCount === undefined) window.frameCount = 0;
                        window.frameCount++;
                        
                        const now = performance.now();
                        if (window.lastFpsTime === undefined) {
                            window.lastFpsTime = now;
                            window.frameCount = 0;
                        }
                        
                        const elapsed = now - window.lastFpsTime;
                        if (elapsed >= 1000) {
                            const fps = Math.round((window.frameCount * 1000) / elapsed);
                            document.getElementById('fps-debug-overlay').textContent = `FPS: ${fps}`;
                            window.lastFpsTime = now;
                            window.frameCount = 0;
                        }
                    }, 100);
                }
            )";
            ExecuteScript(script.c_str());
        } else {
            // Remove debug overlay
            std::string script = R"(
                if (window.fpsUpdateInterval) {
                    clearInterval(window.fpsUpdateInterval);
                }
                const overlay = document.getElementById('fps-debug-overlay');
                if (overlay) overlay.remove();
            )";
            ExecuteScript(script.c_str());
        }
    }
}

static LRESULT CALLBACK MessageWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    UpdateFPSCounter();  // ADD THIS LINE - update FPS on every message
    
    MarshalPayload* payload = reinterpret_cast<MarshalPayload*>(lParam);
    if (payload) {
        switch (payload->action) {
        case MarshalAction::ShowAndNavigate:
            if (webviewWindow && isInitialized) {
                webviewController->put_IsVisible(TRUE);
                if (payload->text) webviewWindow->Navigate(payload->text);
            }
            break;
        case MarshalAction::Navigate:
            if (webviewWindow && isInitialized && payload->text) {
                webviewWindow->Navigate(payload->text);
            }
            break;
        case MarshalAction::Resize:
            if (webviewController && isInitialized) {
                RECT bounds = { payload->x, payload->y, payload->x + payload->w, payload->y + payload->h };
                webviewController->put_Bounds(bounds);
            }
            break;
        case MarshalAction::Hide:
            if (webviewController) {
                webviewController->put_IsVisible(FALSE);
                webviewController->MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC);
            }
            break;
        case MarshalAction::Close:
            if (webviewController) {
                webviewController->Close();
                webviewController.Reset();
                webviewWindow.Reset();
                isInitialized = false;
                initStatus = 0;
            }
            if (g_messageWnd) {
                SetWindowLongPtrW(g_messageWnd, GWLP_WNDPROC, (LONG_PTR)DefWindowProcW);
                DestroyWindow(g_messageWnd);
                g_messageWnd = nullptr;
            }
            if (g_comInitializedOnCreatorThread) {
                CoUninitialize();
                g_comInitializedOnCreatorThread = false;
            }
            break;
        case MarshalAction::ExecuteScript:
            if (webviewWindow && isInitialized && payload->text) {
                webviewWindow->ExecuteScript(payload->text, Callback<ICoreWebView2ExecuteScriptCompletedHandler>(
                    [](HRESULT, LPCWSTR) -> HRESULT { return S_OK; }).Get());
            }
            break;
        case MarshalAction::PostWebMessage:
            if (webviewWindow && isInitialized && payload->text) {
                webviewWindow->PostWebMessageAsString(payload->text);
            }
            break;
        case MarshalAction::SetCookie:
            if (webviewWindow && isInitialized && payload->text) {
                ComPtr<ICoreWebView2> webview2 = webviewWindow;
                ComPtr<ICoreWebView2CookieManager> cookieManager;
                ComPtr<ICoreWebView2_2> webview2_2;
                HRESULT hr = webview2.As(&webview2_2);
                if (SUCCEEDED(hr) && webview2_2) {
                    hr = webview2_2->get_CookieManager(&cookieManager);
                }
                if (SUCCEEDED(hr) && cookieManager) {
                    std::wstring s = payload->text;
                    std::vector<std::wstring> parts;
                    size_t start = 0;
                    while (true) {
                        size_t pos = s.find(L'\n', start);
                        if (pos == std::wstring::npos) {
                            parts.push_back(s.substr(start));
                            break;
                        }
                        parts.push_back(s.substr(start, pos - start));
                        start = pos + 1;
                    }
                    if (parts.size() >= 3) {
                        ComPtr<ICoreWebView2Cookie> cookie;
                        cookieManager->CreateCookie(parts[0].c_str(), parts[1].c_str(), parts[2].c_str(), 
                                                    parts.size() > 3 ? parts[3].c_str() : L"/", &cookie);
                        if (cookie) {
                            if (parts.size() > 4 && parts[4] == L"1") cookie->put_IsSecure(TRUE);
                            if (parts.size() > 5 && parts[5] == L"1") cookie->put_IsHttpOnly(TRUE);
                            cookieManager->AddOrUpdateCookie(cookie.Get());
                        }
                    }
                }
            }
            break;
        case MarshalAction::ClearCookies:
            if (webviewWindow && isInitialized) {
                ComPtr<ICoreWebView2> webview2 = webviewWindow;
                ComPtr<ICoreWebView2CookieManager> cookieManager;
                ComPtr<ICoreWebView2_2> webview2_2;
                HRESULT hr = webview2.As(&webview2_2);
                if (SUCCEEDED(hr) && webview2_2) {
                    hr = webview2_2->get_CookieManager(&cookieManager);
                }
                if (SUCCEEDED(hr) && cookieManager) {
                    cookieManager->DeleteAllCookies();
                }
            }
            break;
        default:
            break;
        }
        FreeMarshalPayload(payload);
        return 0;
    }
    if (g_prevMessageWndProc) return CallWindowProcW(g_prevMessageWndProc, hwnd, msg, wParam, lParam);
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}