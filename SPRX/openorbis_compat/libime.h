#pragma once
#include <stdint.h>
#include <orbis/_types/ime_dialog.h>

typedef OrbisImeKeycode SceImeKeycode;
typedef union SceImeEventParam
{
    SceImeKeycode keycode;
    uint8_t reserved[64];
} SceImeEventParam;

typedef struct SceImeEvent
{
    int32_t id;
    SceImeEventParam param;
} SceImeEvent;

#define SCE_IME_KEYBOARD_EVENT_OPEN 0
#define SCE_IME_KEYBOARD_EVENT_KEYCODE_DOWN 1
#define SCE_IME_KEYBOARD_EVENT_KEYCODE_UP 2
#define SCE_IME_KEYBOARD_EVENT_KEYCODE_REPEAT 3
#define SCE_IME_KEYBOARD_EVENT_CLOSE 4
#define SCE_IME_KEYBOARD_EVENT_CONNECTION 5
#define SCE_IME_KEYBOARD_EVENT_DISCONNECTION 6
#define SCE_IME_KEYBOARD_EVENT_ABORT 7
#define SCE_IME_KEYCODE_STATE_KEYCODE_VALID 0x00000001u
