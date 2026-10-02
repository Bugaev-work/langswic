#include "winapi_min.h"

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved) {
    (void)instance; (void)reason; (void)reserved;
    return TRUE;
}

/* Thread-specific hook for our language command only; no text or key capture.
   It executes ActivateKeyboardLayout on the recipient's UI thread. */
__declspec(dllexport) LRESULT CALLBACK LayoutHook(int code, WPARAM wp, LPARAM lp) {
    if (code >= 0 && lp) {
        const CWPSTRUCT *message = (const CWPSTRUCT *)lp;
        UINT command = RegisterWindowMessageW(L"FastSwitcher.ActivateInstalledLayout.v1");
        if (command && message->message == command) {
            HWND root = (HWND)message->wParam;
            DWORD thread = GetWindowThreadProcessId(message->hwnd, NULL);
            GUITHREADINFO info = {0};
            info.cbSize = sizeof(info);
            if (root && GetForegroundWindow() == root &&
                GetAncestor(message->hwnd, GA_ROOT) == root &&
                thread == GetCurrentThreadId() && GetGUIThreadInfo(thread, &info) &&
                (info.hwndFocus == message->hwnd ||
                 (!info.hwndFocus && message->hwnd == root))) {
                HKL layouts[64];
                int count = GetKeyboardLayoutList(64, layouts);
                for (int i = 0; i < count; ++i) {
                    if (layouts[i] == (HKL)message->lParam) {
                        ActivateKeyboardLayout(layouts[i], 0);
                        break;
                    }
                }
            }
        }
    }
    return CallNextHookEx(NULL, code, wp, lp);
}
