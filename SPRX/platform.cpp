#include "headers.hpp"
#include <string.h>
#include <stdarg.h>
#include <stdint.h>

namespace {
    uint64_t g_execStart = 0;
    uint64_t g_execEnd = 0;
    uint64_t g_moduleEnd = 0;

    enum NotificationType {
        kNotificationRequest = 0,
        kNotificationRequestWithIcon = 1,
    };

    struct BO3NotificationRequest {
        NotificationType type;
        int32_t reqId;
        int32_t priority;
        int32_t msgId;
        int32_t targetId;
        int32_t userId;
        int32_t unk1;
        int32_t unk2;
        int32_t appId;
        int32_t errorNum;
        int32_t unk3;
        uint8_t useIconImageUri;
        char message[1024];
        char iconUri[1024];
        char unk[1024];
    };

    using SendNotification_t = int (*)(int, void*, size_t, int);
    SendNotification_t g_sendNotification = nullptr;
    bool g_notificationReady = false;

#ifdef BO3_OPENORBIS
    // OpenOrbis v0.5.4 virtual-query ABI view. Some packaged headers expose
    // inconsistent field names, so access stable ABI offsets through this checked view.
    struct OpenOrbisVirtualQueryInfoView
    {
        void* start_addr;
        void* end_addr;
        int64_t offset;
        int32_t prot;
        int32_t mtype;
        unsigned isFlexibleMemory : 1;
        unsigned isDirectMemory : 1;
        unsigned isStack : 1;
        unsigned isPooledMemory : 1;
        unsigned isCommitted : 1;
        char name[32];
    };
    static_assert(sizeof(OpenOrbisVirtualQueryInfoView) == sizeof(SceKernelVirtualQueryInfo),
        "OpenOrbis virtual-query ABI size changed; update the view before building");
    static_assert(offsetof(OpenOrbisVirtualQueryInfoView, name) == offsetof(SceKernelVirtualQueryInfo, name),
        "OpenOrbis virtual-query name offset changed; update the view before building");

    static const OpenOrbisVirtualQueryInfoView& QueryView(const SceKernelVirtualQueryInfo& info)
    {
        return *reinterpret_cast<const OpenOrbisVirtualQueryInfoView*>(&info);
    }
#endif

    static uintptr_t QueryStart(const SceKernelVirtualQueryInfo& info)
    {
#ifdef BO3_OPENORBIS
        return (uintptr_t)QueryView(info).start_addr;
#else
        return (uintptr_t)info.start;
#endif
    }

    static uintptr_t QueryEnd(const SceKernelVirtualQueryInfo& info)
    {
#ifdef BO3_OPENORBIS
        return (uintptr_t)QueryView(info).end_addr;
#else
        return (uintptr_t)info.end;
#endif
    }

    static int QueryProtection(const SceKernelVirtualQueryInfo& info)
    {
#ifdef BO3_OPENORBIS
        return QueryView(info).prot;
#else
        return info.protection;
#endif
    }

    static const char* QueryName(const SceKernelVirtualQueryInfo& info)
    {
#ifdef BO3_OPENORBIS
        return QueryView(info).name;
#else
        return info.name;
#endif
    }
}

#ifndef BO3_OPENORBIS
extern "C" {
    int sceKernelSendNotificationRequest(int device, void* request, size_t size, int block);
    int mdbg_service(int command, void* arg1, void* arg2);
}
#endif

uint64_t GetBaseAddress() {
    static uint64_t cached = 0;

    if (cached)
        return cached;

    SceKernelVirtualQueryInfo info;
    void* address = nullptr;

    while (sceKernelVirtualQuery(address, SCE_KERNEL_VQ_FIND_NEXT, &info, sizeof(info)) >= 0) {
        const uintptr_t start = QueryStart(info);
        const uintptr_t end = QueryEnd(info);

        if (end <= start)
            break;

        address = (void*)end;

        if (QueryProtection(info) != 5 || strcmp(QueryName(info), "executable") != 0)
            continue;

        cached = start;
        g_execStart = start;
        g_execEnd = end;
        g_moduleEnd = end;

        SceKernelVirtualQueryInfo next;
        uintptr_t probe = end;

        for (int hop = 0; hop < 16; ++hop) {
            if (sceKernelVirtualQuery((void*)probe, 0, &next, sizeof(next)) < 0)
                break;

            const uintptr_t ns = QueryStart(next);
            const uintptr_t ne = QueryEnd(next);

            if (ne <= ns || ns > g_moduleEnd || (QueryProtection(next) & 1) == 0)
                break;

            g_moduleEnd = ne;
            probe = ne;
        }

        return cached;
    }

    return 0;
}

static uintptr_t ReadableEnd(uintptr_t addr, size_t need) {
    if (addr < 0x10000 || addr >= 0x0000800000000000ULL || addr + need < addr)
        return 0;

    SceKernelVirtualQueryInfo info;
    uintptr_t end = 0;
    uintptr_t probe = addr;

    for (int hop = 0; hop < 8; ++hop) {
        if (sceKernelVirtualQuery((void*)probe, 0, &info, sizeof(info)) < 0)
            break;

        if ((QueryProtection(info) & 1) == 0)
            break;

        const uintptr_t start = QueryStart(info);
        const uintptr_t stop = QueryEnd(info);

        if (stop <= start || probe < start || probe >= stop || (end && start > end))
            break;

        end = stop;
        probe = stop;

        if (end >= addr + need)
            break;
    }

    return end;
}

bool RangeReadable(uintptr_t addr, size_t span) {
    if (!addr || addr + span < addr)
        return false;

    const uintptr_t end = ReadableEnd(addr, span);
    return end != 0 && (addr + span) <= end;
}

bool SafeStrStr(uintptr_t addr, const char* target, size_t maxScan) {
    if (addr < 0x10000 || addr >= 0x0000800000000000ULL || !target)
        return false;

    const size_t length = strlen(target);

    if (!length || length > maxScan)
        return false;

    const uintptr_t end = ReadableEnd(addr, maxScan);

    if (!end || end <= addr)
        return false;

    size_t available = (size_t)(end - addr);

    if (available > maxScan)
        available = maxScan;

    if (available < length)
        return false;

    const char* const text = (const char*)addr;

    for (size_t i = 0; i + length <= available; ++i) {
        if (text[i] == 0)
            return false;

        size_t j = 0;

        while (j < length && text[i + j] == target[j])
            ++j;

        if (j == length)
            return true;
    }

    return false;
}

void Notify(const char* fmt, ...) {
    if (!g_notificationReady) {
        g_notificationReady = true;

        void* address = nullptr;

        if (sceKernelDlsym(0x2001, "sceKernelSendNotificationRequest", &address) == 0 && address)
            g_sendNotification = (SendNotification_t)address;
    }

    BO3NotificationRequest request{};

    va_list va;
    va_start(va, fmt);
    vsnprintf(request.message, sizeof(request.message), fmt, va);
    va_end(va);

    request.type = kNotificationRequest;
    request.targetId = -1;
    request.useIconImageUri = 1;
    snprintf(request.iconUri, sizeof(request.iconUri), "%s", "cxml://psnotification/tex_icon_ribbon");

    if (g_sendNotification)
        g_sendNotification(0, &request, sizeof(request), 0);
#ifndef BO3_OPENORBIS
    else
        sceKernelSendNotificationRequest(0, &request, sizeof(request), 0);
#endif
}