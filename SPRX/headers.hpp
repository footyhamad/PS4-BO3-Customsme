#pragma once

#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>
#ifdef BO3_OPENORBIS
#include <time.h>
extern "C" int nanosleep(const struct timespec* request, struct timespec* remaining);
#endif
#include <string>

#include <kernel.h>
#ifndef BO3_OPENORBIS
#include <sys/mman.h>
#endif

#include "HDE64.h"
#include "Detour.hpp"
#include "libjbc.h"

#include "platform.hpp"
#include "diag.hpp"
#include "t7_maps.hpp"
#include "t7_mapimages.hpp"
#include "t7_lua.hpp"
#include "t7_log.hpp"
