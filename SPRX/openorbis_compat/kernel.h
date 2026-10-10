#pragma once
/*
 * Compatibility shim for OpenOrbis PS4 Toolchain.
 * Keep Sony SDK names in app sources and map them to public OpenOrbis APIs.
 */
#include <orbis/libkernel.h>
#include <orbis/_types/user.h>
#include <fcntl.h>
#include <dirent.h>

// Sony SDK-compatible name for the FreeBSD-style directory records returned by sceKernelGetdents.
typedef struct dirent SceKernelDirent;

#ifndef DT_DIR
#define DT_DIR 4
#endif
#ifndef d_fileno
#define d_fileno d_ino
#endif

typedef OrbisKernelVirtualQueryInfo SceKernelVirtualQueryInfo;
typedef OrbisKernelStat SceKernelStat;
typedef OrbisPthread ScePthread;

#ifndef SCE_KERNEL_VQ_FIND_NEXT
#define SCE_KERNEL_VQ_FIND_NEXT 1
#endif
#ifndef SCE_KERNEL_O_RDONLY
#define SCE_KERNEL_O_RDONLY O_RDONLY
#endif
#ifndef SCE_KERNEL_O_WRONLY
#define SCE_KERNEL_O_WRONLY O_WRONLY
#endif
#ifndef SCE_KERNEL_O_APPEND
#define SCE_KERNEL_O_APPEND O_APPEND
#endif
#ifndef SCE_KERNEL_O_CREAT
#define SCE_KERNEL_O_CREAT O_CREAT
#endif
#ifndef SCE_KERNEL_O_TRUNC
#define SCE_KERNEL_O_TRUNC O_TRUNC
#endif
#ifndef SCE_KERNEL_O_DIRECTORY
#ifdef O_DIRECTORY
#define SCE_KERNEL_O_DIRECTORY O_DIRECTORY
#else
#define SCE_KERNEL_O_DIRECTORY 0x00020000
#endif
#endif
#ifndef SCE_KERNEL_DT_DIR
#define SCE_KERNEL_DT_DIR DT_DIR
#endif
#ifndef SCE_KERNEL_PRIO_FIFO_HIGHEST
#define SCE_KERNEL_PRIO_FIFO_HIGHEST ORBIS_KERNEL_PRIO_FIFO_HIGHEST
#endif
#ifndef PROT_READ
#define PROT_READ 0x1
#endif
#ifndef PROT_WRITE
#define PROT_WRITE 0x2
#endif
#ifndef PROT_EXEC
#define PROT_EXEC 0x4
#endif
