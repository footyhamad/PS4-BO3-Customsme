#pragma once
#include <stdint.h>

/*
 * OpenOrbis v0.5.4 currently exposes mouse calls as incomplete prototypes.
 * These isolated declarations enable compilation without bundling Sony SDK
 * headers. Confirm structure ABI against the target SDK before runtime use.
 */
typedef struct SceMouseOpenParam
{
    uint32_t behaviorFlag;
    uint8_t reserved[0x3C];
} SceMouseOpenParam;

typedef struct SceMouseData
{
    uint32_t buttons;
    int32_t xAxis;
    int32_t yAxis;
    int32_t wheel;
    int32_t tilt;
    uint8_t connected;
    uint8_t reserved[3];
} SceMouseData;

#define SCE_MOUSE_OPEN_PARAM_MERGED 0
#define SCE_MOUSE_PORT_TYPE_STANDARD 0
#define SCE_MOUSE_BUTTON_INTERCEPTED 0x80000000u

#ifdef __cplusplus
extern "C" {
#endif
int32_t sceMouseInit(void);
int32_t sceMouseOpen(int32_t userId, int32_t portType, int32_t index, const SceMouseOpenParam* param);
int32_t sceMouseRead(int32_t handle, SceMouseData* data, int32_t count);
int32_t sceMouseClose(int32_t handle);
#ifdef __cplusplus
}
#endif
