/* Launcher-owned addition to cnc-ddraw 39f14721c998c937118615c1c4eb02ce20a4e638.
 * Include after dd.h/config.h in wndproc.c. Execute only on the game's GUI thread.
 * Protocol matches CncSpeedClient.cs; no remote pointers or binary offsets.
 * Native build and limiter timing are tested; real gameplay requires verification.
 */
#ifndef RA2_LAUNCHER_SPEED_H
#define RA2_LAUNCHER_SPEED_H

#define LAUNCHER_SPEED_COOKIE 0x52413253u
#define LAUNCHER_SPEED_REPLY  0x53410000u
#define LAUNCHER_SPEED_QUERY  0xFFFFFFFFu

static UINT launcher_speed_message(void)
{
    static UINT message;
    if (!message) message = RegisterWindowMessageA("Ra2ModeLauncher.CncSpeed.v1");
    return message;
}

static BOOL launcher_speed_allowed(void)
{
    char path[MAX_PATH], setting[16];
    DWORD length = GetModuleFileNameA(NULL, path, sizeof(path));
    char* filename;
    if (!length || length >= sizeof(path)) return FALSE;
    filename = strrchr(path, '\\');
    if (!filename || _stricmp(filename + 1, "gamemd-spawn.exe")) return FALSE;
    strcpy(filename + 1, "spawn.ini"); /* shorter than gamemd-spawn.exe */
    /* Explicit opt-in and fail closed when loading another game's configuration. */
    GetPrivateProfileStringA("Settings", "LauncherLiveSpeed", "false", setting, sizeof(setting), path);
    if (_stricmp(setting, "true")) return FALSE;
    GetPrivateProfileStringA("Settings", "ForceMultiplayer", "true", setting, sizeof(setting), path);
    return !_stricmp(setting, "false");
}

static BOOL launcher_speed_managed_launch(void)
{
    char path[MAX_PATH], setting[16];
    DWORD length = GetModuleFileNameA(NULL, path, sizeof(path));
    char* filename;
    if (!length || length >= sizeof(path)) return FALSE;
    filename = strrchr(path, '\\');
    if (!filename || _stricmp(filename + 1, "gamemd-spawn.exe")) return FALSE;
    strcpy(filename + 1, "spawn.ini");
    GetPrivateProfileStringA("Settings", "LauncherCncPacing", "false", setting, sizeof(setting), path);
    return !_stricmp(setting, "true");
}

static LRESULT launcher_speed_control(WPARAM request, LPARAM cookie)
{
    int rate;
    if ((DWORD)cookie != LAUNCHER_SPEED_COOKIE || GetCurrentThreadId() != g_ddraw.gui_thread_id) return 0;
    if ((DWORD)request == LAUNCHER_SPEED_QUERY)
    {
        rate = g_config.maxgameticks == -1 ? 0 : g_config.maxgameticks;
        if (g_config.maxgameticks == 0 || rate < 0 || rate > 1000) return 0;
        return LAUNCHER_SPEED_REPLY | (rate + 1);
    }
    if ((DWORD)request > 1000 || !launcher_speed_allowed()) return 0;
    rate = (int)request;
    if (rate && !g_ddraw.ticks_limiter.htimer)
    {
        typedef HANDLE(WINAPI* CREATE_TIMER)(LPSECURITY_ATTRIBUTES, LPCWSTR, DWORD, DWORD);
        CREATE_TIMER create_timer = (CREATE_TIMER)GetProcAddress(GetModuleHandleA("kernel32.dll"), "CreateWaitableTimerExW");
        HANDLE timer = create_timer ? create_timer(NULL, NULL, CREATE_WAITABLE_TIMER_MANUAL_RESET | CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS) : NULL;
        if (!timer) timer = CreateWaitableTimerA(NULL, TRUE, NULL);
        if (!timer) return 0;
        g_ddraw.ticks_limiter.htimer = timer;
    }
    if (g_ddraw.ticks_limiter.htimer) CancelWaitableTimer(g_ddraw.ticks_limiter.htimer);
    g_ddraw.ticks_limiter.due_time.QuadPart = 0;
    g_ddraw.ticks_limiter.tick_length_ns = rate ? (10000000LL / rate) : 0;
    g_ddraw.ticks_limiter.tick_length = rate ? (DWORD)((1000 + rate / 2) / rate) : 0;
    /* Prevent the independent legacy 60-Hz flip limiter from capping a managed target. */
    if (g_ddraw.flip_limiter.htimer) CancelWaitableTimer(g_ddraw.flip_limiter.htimer);
    g_ddraw.flip_limiter.tick_length = 0;
    g_ddraw.flip_limiter.tick_length_ns = 0;
    g_ddraw.flip_limiter.due_time.QuadPart = 0;
    g_config.maxgameticks = rate ? rate : -1;
    return LAUNCHER_SPEED_REPLY | (rate + 1);
}
#endif
