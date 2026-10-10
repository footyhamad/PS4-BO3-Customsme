#pragma once

#include <stdarg.h>
#include <stdint.h>

#define BO3_CUSTOMS_SPRX_VERSION "1.2.1.22"

enum BO3DiagLevel
{
    BO3_DIAG_INFO,
    BO3_DIAG_WARN,
    BO3_DIAG_ERROR,
    BO3_DIAG_FATAL
};

void BO3Diag_Init();
void BO3Diag_Log(BO3DiagLevel level, const char* component, const char* format, ...);
void BO3Diag_LogV(BO3DiagLevel level, const char* component, const char* format, va_list args);
uint64_t BO3Diag_UptimeUs();
void BO3Diag_Heartbeat(const char* phase, uintptr_t base, int mapCount, bool frameHook,
                       uint64_t frameCalls, bool frameStalled);
