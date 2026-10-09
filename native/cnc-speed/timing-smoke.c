#define COBJMACROS
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <initguid.h>
#include "ddraw.h"

typedef HRESULT(WINAPI* CREATE_DDRAW)(GUID*, LPDIRECTDRAW*, IUnknown*);
static UINT control_message;
static HWND window;

static void check(int ok, const char* message)
{
    if (!ok) { fprintf(stderr, "FAIL %s (win32=%lu)\n", message, GetLastError()); exit(1); }
    printf("PASS %s\n", message);
    fflush(stdout);
}
static DWORD control(DWORD request)
{
    return (DWORD)SendMessageA(window, control_message, request, 0x52413253u);
}
int main(void)
{
    WNDCLASSA cls = { 0 };
    cls.lpfnWndProc = DefWindowProcA;
    cls.hInstance = GetModuleHandleA(NULL);
    cls.lpszClassName = "LauncherSpeedTimingFixture";
    check(RegisterClassA(&cls) != 0, "register private hidden test window");
    window = CreateWindowA(cls.lpszClassName, "Native timing fixture", WS_OVERLAPPEDWINDOW, 0, 0, 640, 480, NULL, NULL, cls.hInstance, NULL);
    check(window != NULL, "create hidden fixture window");
    HMODULE dll = LoadLibraryA("ddraw.dll");
    check(dll != NULL, "load source-built cnc-ddraw DLL");
    CREATE_DDRAW create = (CREATE_DDRAW)GetProcAddress(dll, "DirectDrawCreate");
    LPDIRECTDRAW dd = NULL;
    check(create && SUCCEEDED(create(NULL, &dd, NULL)), "create actual DirectDraw wrapper");
    check(SUCCEEDED(IDirectDraw_SetCooperativeLevel(dd, window, DDSCL_NORMAL)), "attach wrapper to fixture window");
    LPDIRECTDRAW7 dd7 = NULL;
    check(SUCCEEDED(IDirectDraw_QueryInterface(dd, &IID_IDirectDraw7, (void**)&dd7)), "query actual DirectDraw7 timing interface");
    control_message = RegisterWindowMessageA("Ra2ModeLauncher.CncSpeed.v1");
    check(control(0xFFFFFFFFu) == (0x53410000u | 61), "read launch target through actual window protocol");
    check(SendMessageA(window, control_message, 120, 0) == 0, "invalid protocol cookie rejected");
    LARGE_INTEGER frequency, begin, end;
    QueryPerformanceFrequency(&frequency);
    QueryPerformanceCounter(&begin);
    for (int i = 0; i < 60; ++i) IDirectDraw_WaitForVerticalBlank(dd, DDWAITVB_BLOCKBEGIN, NULL);
    QueryPerformanceCounter(&end);
    check((double)(end.QuadPart - begin.QuadPart) / frequency.QuadPart < 0.25, "managed launch removes hidden 60-Hz flip cap");
    const int rates[] = { 30, 60, 120, 37, 1000, 1 };
    for (int r = 0; r < sizeof(rates) / sizeof(rates[0]); ++r)
    {
        int target = rates[r], count = target < 2 ? 2 : target;
        check(control(target) == (0x53410000u | (target + 1)), "apply and acknowledge live rate");
        check(control(0xFFFFFFFFu) == (0x53410000u | (target + 1)), "read back applied live rate");
        QueryPerformanceCounter(&begin);
        for (int i = 0; i < count; ++i) IDirectDraw7_TestCooperativeLevel(dd7);
        QueryPerformanceCounter(&end);
        double seconds = (double)(end.QuadPart - begin.QuadPart) / frequency.QuadPart;
        double actual = (count - 1) / seconds;
        printf("TIMING target=%d measured=%.2f seconds=%.4f\n", target, actual, seconds);
        check(seconds > 0.75 && seconds < 1.30, "actual limiter pacing within timing tolerance");
    }
    check(control(1001) == 0, "invalid target rejected by native endpoint");
    check(control(0) == (0x53410000u | 1), "live unlimited target acknowledged");
    QueryPerformanceCounter(&begin);
    for (int i = 0; i < 1000; ++i) IDirectDraw7_TestCooperativeLevel(dd7);
    QueryPerformanceCounter(&end);
    check((double)(end.QuadPart - begin.QuadPart) / frequency.QuadPart < 0.5, "unlimited removes actual timer waits");
    WritePrivateProfileStringA("Settings", "ForceMultiplayer", "true", ".\\spawn.ini");
    check(control(60) == 0 && control(0xFFFFFFFFu) == (0x53410000u | 1), "multiplayer cannot apply a unilateral live rate");
    WritePrivateProfileStringA("Settings", "ForceMultiplayer", "false", ".\\spawn.ini");
    WritePrivateProfileStringA("Settings", "LauncherLiveSpeed", "false", ".\\spawn.ini");
    check(control(60) == 0, "missing opt-in is rejected");
    IDirectDraw7_Release(dd7);
    IDirectDraw_Release(dd);
    DestroyWindow(window);
    printf("Actual cnc-ddraw timer and message path tested; no RA2 gameplay simulated.\n");
    return 0;
}
