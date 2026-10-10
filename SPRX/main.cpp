#include "headers.hpp"

namespace
{
constexpr uintptr_t kTitleProbe = 0x1312346;
constexpr uintptr_t kComFrame = 0xF0B8A0;

const uint8_t k_comFrame[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                               0x53, 0x48, 0x83, 0xE4, 0xE0, 0x48, 0x81, 0xEC, 0x40, 0x22, 0x00, 0x00 };

enum StartupPhase
{
    kPhaseModuleStart = 0,
    kPhaseWaitingForGame,
    kPhaseMountingStorage,
    kPhaseInstallingHooks,
    kPhaseReady,
    kPhaseFailed,
    kPhaseStopping
};

Detour g_frameDetour{};
void* g_frameOriginal = nullptr;
volatile uintptr_t g_gameBase = 0;
volatile int g_startupPhase = kPhaseModuleStart;
volatile bool g_frameHookInstalled = false;
volatile bool g_monitorRunning = true;
volatile int g_diagnosticMapCount = -1;
uint64_t g_frameCallCount = 0;
ScePthread g_diagnosticsThread{};
bool g_diagnosticsThreadCreated = false;
ScePthread g_initThread{};
volatile bool g_initThreadCreated = false;
volatile bool g_cancelInitialization = false;

using Hook_t = uint64_t (*)(uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t,
                            double, double, double, double, double, double, double, double);

void SetPhase(StartupPhase phase)
{
    __atomic_store_n(&g_startupPhase, (int)phase, __ATOMIC_RELEASE);
}

const char* PhaseName()
{
    switch (__atomic_load_n(&g_startupPhase, __ATOMIC_ACQUIRE))
    {
        case kPhaseModuleStart: return "module-start";
        case kPhaseWaitingForGame: return "waiting-for-bo3";
        case kPhaseMountingStorage: return "mounting-storage";
        case kPhaseInstallingHooks: return "installing-hooks";
        case kPhaseReady: return "ready";
        case kPhaseFailed: return "failed";
        case kPhaseStopping: return "stopping";
        default: return "unknown";
    }
}

uint64_t Frame_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                 double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    __atomic_add_fetch(&g_frameCallCount, (uint64_t)1, __ATOMIC_RELAXED);
    T7Lua_Tick();

    // The detour is only installed after a non-null trampoline has been verified.
    return ((Hook_t)g_frameOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

bool IsBlackOps3(uintptr_t base)
{
    return SafeStrStr(base + kTitleProbe, "Multiplayer") || SafeStrStr(base + kTitleProbe, "multiProgress");
}

uintptr_t WaitForBlackOps3()
{
    SetPhase(kPhaseWaitingForGame);
    const uint64_t started = BO3Diag_UptimeUs();
    uintptr_t lastBase = 0;

    BO3Diag_Log(BO3_DIAG_INFO, "BOOT",
        "waiting for BO3 title probe offset=+0x%llX timeout_ms=10000 poll_ms=100",
        (unsigned long long)kTitleProbe);

    for (int attempt = 0; attempt < 100; ++attempt)
    {
        if (__atomic_load_n(&g_cancelInitialization, __ATOMIC_ACQUIRE))
        {
            BO3Diag_Log(BO3_DIAG_INFO, "BOOT", "BO3 detection cancelled before hooks were installed");
            return 0;
        }
        const uintptr_t base = (uintptr_t)GetBaseAddress();
        lastBase = base;

        if (base && IsBlackOps3(base))
        {
            if (__atomic_load_n(&g_cancelInitialization, __ATOMIC_ACQUIRE))
            {
                BO3Diag_Log(BO3_DIAG_INFO, "BOOT", "BO3 detected during cancellation; aborting before hooks");
                return 0;
            }
            __atomic_store_n(&g_gameBase, base, __ATOMIC_RELEASE);
            BO3Diag_Log(BO3_DIAG_INFO, "BOOT",
                "BO3 title probe matched base=0x%llX attempt=%d elapsed_ms=%llu",
                (unsigned long long)base, attempt + 1,
                (unsigned long long)((BO3Diag_UptimeUs() - started) / 1000ull));
            return base;
        }

        if (((attempt + 1) % 10) == 0)
            BO3Diag_Log(BO3_DIAG_INFO, "BOOT",
                "still waiting for BO3 attempt=%d/100 last_base=0x%llX title_probe=%s elapsed_ms=%llu",
                attempt + 1, (unsigned long long)lastBase,
                base ? "no-match" : "no-executable-base",
                (unsigned long long)((BO3Diag_UptimeUs() - started) / 1000ull));

        sceKernelUsleep(100 * 1000);
    }

    SetPhase(kPhaseFailed);
    BO3Diag_Log(BO3_DIAG_FATAL, "BOOT",
        "BO3 detection timed out after 100 attempts; last_base=0x%llX; no subsystem hooks were installed",
        (unsigned long long)lastBase);
    // This runs on the initialization worker, not inside the module loader's startup callback.
    Notify("BO3 Customs started, but BO3 1.33 was not detected. Check diagnostics.log");
    return 0;
}

static void* diagnostics_thread(void*)
{
    uint64_t lastObservedFrameCalls = 0;
    uint64_t lastFrameProgressUs = BO3Diag_UptimeUs();

    BO3Diag_Log(BO3_DIAG_INFO, "HEARTBEAT", "independent diagnostics monitor started interval_ms=1000 report_interval_ms=10000");
    while (__atomic_load_n(&g_monitorRunning, __ATOMIC_ACQUIRE))
    {
        const uint64_t now = BO3Diag_UptimeUs();
        const uint64_t frameCalls = __atomic_load_n(&g_frameCallCount, __ATOMIC_RELAXED);
        const bool frameHook = __atomic_load_n(&g_frameHookInstalled, __ATOMIC_ACQUIRE);
        const uintptr_t base = __atomic_load_n(&g_gameBase, __ATOMIC_ACQUIRE);

        if (frameCalls != lastObservedFrameCalls)
        {
            lastObservedFrameCalls = frameCalls;
            lastFrameProgressUs = now;
        }

        const bool frameStalled = frameHook && frameCalls > 0 &&
            now >= lastFrameProgressUs && now - lastFrameProgressUs >= 10000000ull;
        const int mapCount = __atomic_load_n(&g_diagnosticMapCount, __ATOMIC_ACQUIRE);
        BO3Diag_Heartbeat(PhaseName(), base, mapCount, frameHook, frameCalls, frameStalled);
        sceKernelUsleep(1000 * 1000);
    }

    BO3Diag_Log(BO3_DIAG_INFO, "HEARTBEAT", "independent diagnostics monitor stopped");
    return nullptr;
}
}

#if defined(BO3_OPENORBIS)
// OpenOrbis crtlib.o normally invokes this array from its own module_start.
// This fork keeps its custom module entrypoints, so it performs the same initialization explicitly.
extern "C" {
extern void (*__init_array_start[])(void);
extern void (*__init_array_end[])(void);
}
#endif

extern "C" const char* sceKernelGetFsSandboxRandomWord();

static void* start_thread(void*)
{
    const uint64_t initStarted = BO3Diag_UptimeUs();
    BO3Diag_Log(BO3_DIAG_INFO, "BOOT", "initialization worker entered; notification API is now called from worker context");
    Notify("BO3 Customs %s worker started; checking for BO3 1.33", BO3_CUSTOMS_SPRX_VERSION);

    const uintptr_t base = WaitForBlackOps3();
    if (!base)
        return nullptr;

    SetPhase(kPhaseMountingStorage);
    const char* rand = sceKernelGetFsSandboxRandomWord();
    if (!rand || !rand[0])
    {
        SetPhase(kPhaseFailed);
        BO3Diag_Log(BO3_DIAG_FATAL, "STORAGE",
            "sceKernelGetFsSandboxRandomWord returned an empty path component; refusing storage probing");
        return nullptr;
    }

    char path[256 * 2];
    const int pathLength = snprintf(path, sizeof(path), "/%s/common/lib/libSceNotification.sprx", rand);
    if (pathLength < 0 || (size_t)pathLength >= sizeof(path))
    {
        SetPhase(kPhaseFailed);
        BO3Diag_Log(BO3_DIAG_FATAL, "STORAGE", "console sandbox probe path overflow; path_length=%d", pathLength);
        return nullptr;
    }

    bool is_ps5 = false;
    const int fd = sceKernelOpen(path, SCE_KERNEL_O_RDONLY, 0);
    if (fd >= 0)
    {
        is_ps5 = true;
        sceKernelClose(fd);
    }
    BO3Diag_Log(is_ps5 ? BO3_DIAG_WARN : BO3_DIAG_INFO, "STORAGE",
        "console sandbox probe path=%s open_rc=0x%08X detected_platform=%s",
        path, (uint32_t)fd, is_ps5 ? "PS5-layout-marker-found; drive mounting skipped" : "PS5-layout-marker-not-found; attempting drive mounts");

#if defined(BO3_OPENORBIS)
    BO3Diag_Log(BO3_DIAG_WARN, "STORAGE",
        "OpenOrbis build excludes the bundled kernel credential/memory helper; automatic external-drive sandbox mounts are disabled. "
        "Local /data/BO3-Customs remains enabled; external roots must already be visible in this process sandbox.");
#else
    if (!is_ps5)
    {
        for (const char* const drive : k_driveRoots)
        {
            char mnt[128];
            int len = snprintf(mnt, sizeof(mnt), "/mnt/%s", drive);
            if (len < 0 || static_cast<size_t>(len) >= sizeof(mnt))
            {
                BO3Diag_Log(BO3_DIAG_WARN, "STORAGE", "skipping external root=%s: mount path overflow", drive);
                continue;
            }

            char sandboxPath[128];
            len = snprintf(sandboxPath, sizeof(sandboxPath), "/%s", drive);
            if (len < 0 || static_cast<size_t>(len) >= sizeof(sandboxPath))
            {
                BO3Diag_Log(BO3_DIAG_WARN, "STORAGE", "skipping external root=%s: sandbox path overflow", drive);
                continue;
            }

            const int mountRc = jbc_mount_in_sandbox(mnt, drive);
            BO3Diag_Log(mountRc == 0 ? BO3_DIAG_INFO : BO3_DIAG_WARN, "STORAGE",
                "mount root=%s sandbox=%s rc=0x%08X result=%s",
                mnt, sandboxPath, (uint32_t)mountRc, mountRc == 0 ? "OK" : "FAILED_OR_ALREADY_MOUNTED");
        }
    }
#endif

    SetPhase(kPhaseInstallingHooks);
    BO3Diag_Log(BO3_DIAG_INFO, "BOOT", "subsystem initialization begin base=0x%llX",
        (unsigned long long)base);

    BO3Diag_Log(BO3_DIAG_INFO, "BOOT", "starting legacy debug logger");
    T7Log_Install(base);

    BO3Diag_Log(BO3_DIAG_INFO, "BOOT", "starting custom map loader");
    T7Maps_Install(base);
    const int mapCountAfterInstall = T7Maps_MapCount();
    __atomic_store_n(&g_diagnosticMapCount, mapCountAfterInstall, __ATOMIC_RELEASE);
    BO3Diag_Log(mapCountAfterInstall >= 0 ? BO3_DIAG_INFO : BO3_DIAG_FATAL, "BOOT",
        "custom map loader returned map_count=%d build_state=%s",
        mapCountAfterInstall, mapCountAfterInstall >= 0 ? "BO3-1.33-accepted" : "unsupported-or-preflight-failed");

    BO3Diag_Log(BO3_DIAG_INFO, "BOOT", "starting map-image hooks");
    T7MapImages_Install(base);

    BO3Diag_Log(BO3_DIAG_INFO, "BOOT", "starting Lua/UI hooks and patches");
    T7Lua_Install(base);

    BO3Diag_Log(BO3_DIAG_INFO, "BOOT", "starting keyboard/mouse integration");
    T7Kbm_Install(base);
    T7Log_Write("[Maps] %d custom map(s) found", T7Maps_MapCount());

    const uintptr_t frameAddress = base + kComFrame;
    if (!T7Maps_IsBuildSupported(base))
    {
        BO3Diag_Log(BO3_DIAG_FATAL, "FRAME",
            "main frame hook skipped because BO3 1.33 mandatory preflight failed base=0x%llX",
            (unsigned long long)base);
    }
    else if (!RangeReadable(frameAddress, sizeof(k_comFrame)))
    {
        BO3Diag_Log(BO3_DIAG_ERROR, "FRAME",
            "Com_Frame signature unreadable offset=+0x%llX address=0x%llX bytes=%llu",
            (unsigned long long)kComFrame, (unsigned long long)frameAddress,
            (unsigned long long)sizeof(k_comFrame));
    }
    else if (memcmp((const void*)frameAddress, k_comFrame, sizeof(k_comFrame)) != 0)
    {
        char actual[96] = {};
        char expected[96] = {};
        size_t actualUsed = 0;
        size_t expectedUsed = 0;
        for (size_t i = 0; i < sizeof(k_comFrame); ++i)
        {
            actualUsed += (size_t)snprintf(actual + actualUsed, sizeof(actual) - actualUsed,
                "%s%02X", i ? " " : "", ((const uint8_t*)frameAddress)[i]);
            expectedUsed += (size_t)snprintf(expected + expectedUsed, sizeof(expected) - expectedUsed,
                "%s%02X", i ? " " : "", k_comFrame[i]);
        }
        BO3Diag_Log(BO3_DIAG_FATAL, "FRAME",
            "Com_Frame signature mismatch offset=+0x%llX expected=[%s] actual=[%s]; frame hook not installed",
            (unsigned long long)kComFrame, expected, actual);
    }
    else
    {
        BO3Diag_Log(BO3_DIAG_INFO, "FRAME",
            "Com_Frame signature matched offset=+0x%llX address=0x%llX; attempting main frame hook",
            (unsigned long long)kComFrame, (unsigned long long)frameAddress);
        const bool frameHook = Detour_Attach(&g_frameDetour, (uint64_t)frameAddress,
            (void*)Frame_h, &g_frameOriginal, "Main.ComFrame") != nullptr && g_frameOriginal != nullptr;
        __atomic_store_n(&g_frameHookInstalled, frameHook, __ATOMIC_RELEASE);
        BO3Diag_Log(frameHook ? BO3_DIAG_INFO : BO3_DIAG_FATAL, "FRAME",
            "main frame hook result=%s trampoline=%p target=0x%llX",
            frameHook ? "INSTALLED" : "FAILED", g_frameOriginal, (unsigned long long)frameAddress);
    }

    const int finalMapCount = T7Maps_MapCount();
    __atomic_store_n(&g_diagnosticMapCount, finalMapCount, __ATOMIC_RELEASE);
    if (finalMapCount < 0)
    {
        SetPhase(kPhaseFailed);
        Notify("BO3 Customs Mod loaded!\nCreated by ItsJokerZz. This game build is not supported - it needs version 1.33");
    }
    else
    {
        char buff[256];
        snprintf(buff, sizeof(buff), "BO3 Customs Mod loaded!\nCreated by ItsJokerZz.\nFound %d custom map(s)", finalMapCount);
        Notify(buff);
        SetPhase(kPhaseReady);
    }

    BO3Diag_Log(finalMapCount >= 0 ? BO3_DIAG_INFO : BO3_DIAG_FATAL, "BOOT",
        "initialization worker finished phase=%s total_elapsed_ms=%llu map_count=%d frame_hook=%s frame_calls=%llu",
        PhaseName(), (unsigned long long)((BO3Diag_UptimeUs() - initStarted) / 1000ull),
        finalMapCount, __atomic_load_n(&g_frameHookInstalled, __ATOMIC_ACQUIRE) ? "installed" : "not-installed",
        (unsigned long long)__atomic_load_n(&g_frameCallCount, __ATOMIC_RELAXED));
    return nullptr;
}

#if defined(BO3_OPENORBIS)
static bool RunCxxInitializers(const char* loaderName)
{
    const uintptr_t ctorStart = (uintptr_t)__init_array_start;
    const uintptr_t ctorEnd = (uintptr_t)__init_array_end;
    BO3Diag_Log(BO3_DIAG_INFO, "CRT",
        "%s init array begin=%p end=%p entries=%llu",
        loaderName, __init_array_start, __init_array_end,
        ctorEnd >= ctorStart ? (unsigned long long)((ctorEnd - ctorStart) / sizeof(void*)) : 0ull);
    if (ctorEnd < ctorStart || ((ctorEnd - ctorStart) % sizeof(void*)) != 0)
    {
        BO3Diag_Log(BO3_DIAG_FATAL, "CRT",
            "%s invalid init-array boundaries begin=%p end=%p; refusing constructor traversal",
            loaderName, __init_array_start, __init_array_end);
        // Do not call the notification API inside module_start: the loader may not
        // have completed module initialization yet. Persistent logs retain the failure.
        return false;
    }
    uint64_t ctorIndex = 0;
    for (void (**init)(void) = __init_array_start; init != __init_array_end; ++init, ++ctorIndex)
    {
        if (!*init)
        {
            BO3Diag_Log(BO3_DIAG_WARN, "CRT", "%s constructor index=%llu is null; skipped",
                loaderName, (unsigned long long)ctorIndex);
            continue;
        }
        BO3Diag_Log(BO3_DIAG_INFO, "CRT", "%s calling constructor index=%llu address=%p",
            loaderName, (unsigned long long)ctorIndex, (void*)*init);
        (*init)();
        BO3Diag_Log(BO3_DIAG_INFO, "CRT", "%s constructor returned index=%llu address=%p",
            loaderName, (unsigned long long)ctorIndex, (void*)*init);
    }
    BO3Diag_Log(BO3_DIAG_INFO, "CRT", "%s init array complete entries=%llu",
        loaderName, (unsigned long long)ctorIndex);
    return true;
}
#endif

static int StartBackgroundWorkers(bool waitForInitialization)
{
    __atomic_store_n(&g_cancelInitialization, false, __ATOMIC_RELEASE);
    __atomic_store_n(&g_monitorRunning, true, __ATOMIC_RELEASE);
    if (!g_diagnosticsThreadCreated)
    {
        const int monitorRc = scePthreadCreate(&g_diagnosticsThread, nullptr, diagnostics_thread, nullptr,
            "BO3-Customs Diag");
        if (monitorRc == 0)
        {
            g_diagnosticsThreadCreated = true;
            BO3Diag_Log(BO3_DIAG_INFO, "BOOT", "diagnostics monitor thread created successfully");
        }
        else
        {
            BO3Diag_Log(BO3_DIAG_ERROR, "BOOT", "diagnostics monitor thread creation failed rc=0x%08X",
                (uint32_t)monitorRc);
        }
    }
    if (__atomic_load_n(&g_initThreadCreated, __ATOMIC_ACQUIRE))
    {
        BO3Diag_Log(BO3_DIAG_WARN, "BOOT", "initialization worker already launched; duplicate launch rejected");
        return 0;
    }
    const int createRc = scePthreadCreate(&g_initThread, nullptr, start_thread, nullptr, "BO3-Customs Init");
    if (createRc != 0)
    {
        SetPhase(kPhaseFailed);
        BO3Diag_Log(BO3_DIAG_FATAL, "BOOT", "initialization thread creation failed rc=0x%08X",
            (uint32_t)createRc);
        return createRc;
    }
    __atomic_store_n(&g_initThreadCreated, true, __ATOMIC_RELEASE);
    BO3Diag_Log(BO3_DIAG_INFO, "BOOT", "initialization thread created wait=%s",
        waitForInitialization ? "yes" : "no");
    if (!waitForInitialization)
        return 0;
    const int joinRc = scePthreadJoin(g_initThread, nullptr);
    __atomic_store_n(&g_initThreadCreated, false, __ATOMIC_RELEASE);
    BO3Diag_Log(joinRc == 0 ? BO3_DIAG_INFO : BO3_DIAG_ERROR, "BOOT",
        "initialization thread join returned rc=0x%08X", (uint32_t)joinRc);
    return joinRc;
}

static int StopBackgroundWorkers(bool refuseIfGameDetected)
{
    // Hook trampolines and callbacks reside inside this module; unloading after
    // executable discovery could leave BO3 calling unmapped module code.
    const uintptr_t base = __atomic_load_n(&g_gameBase, __ATOMIC_ACQUIRE);
    if (refuseIfGameDetected && base)
    {
        BO3Diag_Log(BO3_DIAG_ERROR, "PLUGIN",
            "unload refused: BO3 was detected at base=0x%llX; hooks may reference this module. Exit BO3 before unloading.",
            (unsigned long long)base);
        Notify("BO3 Customs: exit BO3 before unloading this plugin");
        return -1;
    }
    __atomic_store_n(&g_cancelInitialization, true, __ATOMIC_RELEASE);
    SetPhase(kPhaseStopping);
    __atomic_store_n(&g_monitorRunning, false, __ATOMIC_RELEASE);
    if (__atomic_load_n(&g_initThreadCreated, __ATOMIC_ACQUIRE))
    {
        const int joinRc = scePthreadJoin(g_initThread, nullptr);
        __atomic_store_n(&g_initThreadCreated, false, __ATOMIC_RELEASE);
        BO3Diag_Log(joinRc == 0 ? BO3_DIAG_INFO : BO3_DIAG_ERROR, "BOOT",
            "initialization worker join on stop returned rc=0x%08X", (uint32_t)joinRc);
    }
    if (g_diagnosticsThreadCreated)
    {
        const int joinRc = scePthreadJoin(g_diagnosticsThread, nullptr);
        g_diagnosticsThreadCreated = false;
        BO3Diag_Log(joinRc == 0 ? BO3_DIAG_INFO : BO3_DIAG_ERROR, "BOOT",
            "diagnostics monitor join on stop returned rc=0x%08X", (uint32_t)joinRc);
    }
    return 0;
}

extern "C"
{
#if defined(BO3_GOLDHEN_PLUGIN)
__attribute__((visibility("default"))) const char* g_pluginName = "BO3 Customs";
__attribute__((visibility("default"))) const char* g_pluginDesc = "Black Ops III 1.33 custom map loader";
__attribute__((visibility("default"))) const char* g_pluginAuth = "BO3 Customsme";
__attribute__((visibility("default"))) uint32_t g_pluginVersion = 0x0102011F; // Fork 1.2.1.31
__attribute__((visibility("hidden")))
#endif
int module_start(size_t argc, const void* args)
{
    BO3Diag_Init();
    BO3Diag_Log(BO3_DIAG_INFO, "BOOT",
        "module_start entered argc=%llu args=%p build=%s",
        (unsigned long long)argc, args,
#if defined(BO3_GOLDHEN_PLUGIN)
        "GoldHEN-plugin"
#else
        "generic-OpenOrbis"
#endif
    );
    // Keep module_start minimal. Generic startup notifications happen on the worker;
    // the GoldHEN build notifies from plugin_load, after the loader calls back.
#if defined(BO3_OPENORBIS)
    if (!RunCxxInitializers(
#if defined(BO3_GOLDHEN_PLUGIN)
        "GoldHEN"
#else
        "OpenOrbis"
#endif
    ))
        return -1;
#endif
#if defined(BO3_GOLDHEN_PLUGIN)
    BO3Diag_Log(BO3_DIAG_INFO, "PLUGIN", "module_start complete; waiting for GoldHEN plugin_load callback");
    return 0;
#else
    BO3Diag_Log(BO3_DIAG_INFO, "BOOT", "generic SPRX C++ initialization passed; starting background workers");
    return StartBackgroundWorkers(true);
#endif
}

#if defined(BO3_GOLDHEN_PLUGIN)
__attribute__((visibility("default"))) int32_t plugin_load(int32_t argc, const char* argv[])
{
    BO3Diag_Init();
    BO3Diag_Log(BO3_DIAG_INFO, "PLUGIN",
        "GoldHEN plugin_load callback entered argc=%d argv=%p", argc, argv);
    Notify("BO3 Customs %s GoldHEN plugin loaded; checking BO3 1.33", BO3_CUSTOMS_SPRX_VERSION);
    return StartBackgroundWorkers(false);
}

__attribute__((visibility("default"))) int32_t plugin_unload(int32_t argc, const char* argv[])
{
    BO3Diag_Log(BO3_DIAG_WARN, "PLUGIN",
        "GoldHEN plugin_unload callback entered argc=%d argv=%p", argc, argv);
    return StopBackgroundWorkers(true);
}
#endif

#if defined(BO3_GOLDHEN_PLUGIN)
__attribute__((visibility("hidden")))
#endif
int module_stop(size_t argc, const void* args)
{
    BO3Diag_Log(BO3_DIAG_INFO, "BOOT", "module_stop entered argc=%llu args=%p",
        (unsigned long long)argc, args);
    const int rc = StopBackgroundWorkers(true);
    BO3Diag_Log(rc == 0 ? BO3_DIAG_INFO : BO3_DIAG_ERROR, "BOOT", "module_stop complete rc=0x%08X",
        (uint32_t)rc);
    return rc;
}
}
