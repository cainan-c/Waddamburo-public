/* SPDX-License-Identifier: MIT -- declarations omitted by the strict POSIX SDK view. */
#pragma once
#include <sys/cdefs.h>
#undef __BSD_VISIBLE
#define __BSD_VISIBLE 1
#include <sys/types.h>
#include <time.h>
#ifdef __cplusplus
extern "C" {
#endif
time_t timegm(struct tm *value);
#ifdef __cplusplus
}
#endif
