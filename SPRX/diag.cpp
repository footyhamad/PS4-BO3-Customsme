#include "headers.hpp"
#include "diag.hpp"

namespace
{
const char kPrimaryLogPath[] = "/data/BO3-Customs/diagnostics.log";
const char kFallbackLogPath[] = "/data/bo3customs-diagnostics.log";
volatile int g_logLock = 0;
uint64_t g_sessionStartUs = 0;
uint64_t g_lastHeartbeatUs = 0;
bool g_initialized = false;
int g_lastLogPath = 0; // 1 primary, 2 fallback, 0 not opened yet

const char* LevelName(BO3DiagLevel level)
{
    switch (level)
    {
        case BO3_DIAG_INFO: return "INFO";
        case BO3_DIAG_WARN: return "WARN";
        case BO3_DIAG_ERROR: return "ERROR";
        case BO3_DIAG_FATAL: return "FATAL";
        default: return "UNKNOWN";
    }
}

bool AppendLine(const char* path, const char* line, size_t size)
{
    const int fd = sceKernelOpen(path, SCE_KERNEL_O_WRONLY | SCE_KERNEL_O_CREAT | SCE_KERNEL_O_APPEND, 0777);
    if (fd < 0)
        return false;

    size_t written = 0;
    bool ok = true;
    while (written < size)
    {
        const int64_t rc = sceKernelWrite(fd, line + written, size - written);
        if (rc <= 0)
        {
            ok = false;
            break;
        }
        written += (size_t)rc;
    }

    const int closeRc = sceKernelClose(fd);
    return ok && written == size && closeRc >= 0;
}

void DebugFallback(const char* line)
{
#ifdef BO3_OPENORBIS
    sceKernelDebugOutText(0, "%s", line);
#else
    if (sceKernelDebugOutText)
        sceKernelDebugOutText(0, line);
#endif
}

void PersistLine(const char* line, size_t size)
{
    while (__sync_lock_test_and_set(&g_logLock, 1)) {}

    bool stored = false;
    if (g_lastLogPath == 1)
    {
        stored = AppendLine(kPrimaryLogPath, line, size);
        if (!stored && AppendLine(kFallbackLogPath, line, size))
        {
            g_lastLogPath = 2;
            stored = true;
        }
    }
    else if (g_lastLogPath == 2)
    {
        stored = AppendLine(kFallbackLogPath, line, size);
        if (!stored && AppendLine(kPrimaryLogPath, line, size))
        {
            g_lastLogPath = 1;
            stored = true;
        }
    }
    else
    {
        stored = AppendLine(kPrimaryLogPath, line, size);
        if (stored)
            g_lastLogPath = 1;
        else if (AppendLine(kFallbackLogPath, line, size))
        {
            g_lastLogPath = 2;
            stored = true;
        }
    }

    __sync_lock_release(&g_logLock);
    if (!stored)
        DebugFallback(line);
}
}

uint64_t BO3Diag_UptimeUs()
{
    return sceKernelGetProcessTime();
}

void BO3Diag_LogV(BO3DiagLevel level, const char* component, const char* format, va_list args)
{
    if (!format)
        return;

    const uint64_t now = BO3Diag_UptimeUs();
    if (!g_sessionStartUs)
        g_sessionStartUs = now;
    const uint64_t elapsed = now >= g_sessionStartUs ? now - g_sessionStartUs : 0;

    char line[2048];
    const int head = snprintf(line, sizeof(line), "[+%llu.%03llu] [%s] [%s] ",
        (unsigned long long)(elapsed / 1000000ull),
        (unsigned long long)((elapsed / 1000ull) % 1000ull),
        LevelName(level), (component && component[0]) ? component : "GENERAL");
    if (head <= 0 || (size_t)head >= sizeof(line) - 2)
        return;

    const size_t room = sizeof(line) - (size_t)head - 2;
    const int body = vsnprintf(line + head, room + 1, format, args);
    if (body < 0)
        return;

    size_t used = (size_t)head + ((size_t)body < room ? (size_t)body : room);
    line[used++] = '\n';
    PersistLine(line, used);
}

void BO3Diag_Log(BO3DiagLevel level, const char* component, const char* format, ...)
{
    va_list args;
    va_start(args, format);
    BO3Diag_LogV(level, component, format, args);
    va_end(args);
}

void BO3Diag_Init()
{
    if (g_initialized)
        return;
    g_initialized = true;
    g_sessionStartUs = BO3Diag_UptimeUs();

    const int mkdirRc = sceKernelMkdir("/data/BO3-Customs", 0777);
    BO3Diag_Log(BO3_DIAG_INFO, "BOOT",
        "diagnostics starting; revision=Fork %s; target=BO3 1.33 only; build=%s %s; mkdir_rc=0x%08X",
        BO3_CUSTOMS_SPRX_VERSION, __DATE__, __TIME__, (uint32_t)mkdirRc);
    if (g_lastLogPath == 2)
        BO3Diag_Log(BO3_DIAG_WARN, "BOOT", "primary path unavailable; fallback=%s", kFallbackLogPath);
    else if (g_lastLogPath == 0)
        BO3Diag_Log(BO3_DIAG_ERROR, "BOOT", "could not persist to %s or %s; trying kernel debug output",
            kPrimaryLogPath, kFallbackLogPath);
    else
        BO3Diag_Log(BO3_DIAG_INFO, "BOOT", "persistent log=%s; append-only; each line writes/closes immediately",
            kPrimaryLogPath);
}

void BO3Diag_Heartbeat(const char* phase, uintptr_t base, int mapCount, bool frameHook,
                       uint64_t frameCalls, bool frameStalled)
{
    const uint64_t now = BO3Diag_UptimeUs();
    if (g_lastHeartbeatUs && now >= g_lastHeartbeatUs && now - g_lastHeartbeatUs < 10000000ull)
        return;
    g_lastHeartbeatUs = now;

    const char* state = !frameHook ? "not-installed" :
        (frameStalled ? "stalled/no-frame-progress" : (frameCalls ? "advancing" : "waiting-for-first-frame"));
    BO3Diag_Log(BO3_DIAG_INFO, "HEARTBEAT",
        "alive=1 phase=%s uptime_s=%llu base=0x%llX target=BO3-1.33 maps=%d frame_hook=%s frame_calls=%llu frame_state=%s",
        (phase && phase[0]) ? phase : "unknown", (unsigned long long)(now / 1000000ull),
        (unsigned long long)base, mapCount, frameHook ? "installed" : "not-installed",
        (unsigned long long)frameCalls, state);
}
