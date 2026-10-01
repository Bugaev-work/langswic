/* Windows x64 ABI declarations used by this module (WinSDK winuser.h).
   Keeping the small ABI explicit avoids linking or initializing a C runtime. */
typedef void *HWND, *HHOOK, *HINSTANCE, *HKL;
typedef unsigned long DWORD, UINT;
typedef int BOOL;
typedef unsigned long long WPARAM;
typedef long long LPARAM, LRESULT;
typedef void *LPVOID;
#define WINAPI __stdcall
#define CALLBACK __stdcall
#define TRUE 1
#define NULL ((void *)0)
#define GA_ROOT 2
typedef struct { long left, top, right, bottom; } RECT;
typedef struct {
    DWORD cbSize, flags;
    HWND hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
    RECT rcCaret;
} GUITHREADINFO;
typedef struct { LPARAM lParam; WPARAM wParam; UINT message; HWND hwnd; } CWPSTRUCT;
__declspec(dllimport) UINT WINAPI RegisterWindowMessageW(const unsigned short *name);
__declspec(dllimport) HWND WINAPI GetForegroundWindow(void);
__declspec(dllimport) HWND WINAPI GetAncestor(HWND hwnd, UINT flags);
__declspec(dllimport) DWORD WINAPI GetWindowThreadProcessId(HWND hwnd, DWORD *pid);
__declspec(dllimport) DWORD WINAPI GetCurrentThreadId(void);
__declspec(dllimport) BOOL WINAPI GetGUIThreadInfo(DWORD thread, GUITHREADINFO *info);
__declspec(dllimport) int WINAPI GetKeyboardLayoutList(int count, HKL *layouts);
__declspec(dllimport) HKL WINAPI ActivateKeyboardLayout(HKL layout, UINT flags);
__declspec(dllimport) LRESULT WINAPI CallNextHookEx(HHOOK hook, int code, WPARAM wp, LPARAM lp);
