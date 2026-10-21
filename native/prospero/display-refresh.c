/* SPDX-License-Identifier: MIT -- control the existing Vulkan VideoOut handle. */
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <stdatomic.h>
#include <ps5platform/videoout.h>
extern int wsi_videoout_set_flip_rate(int rate);
extern void waddamburo_ps5_log(const char *message);
static atomic_int output_handle = -1;
static int active_refresh;
/* Only the driver's Open reference is redirected here. Never open a second
 * output or take ownership of the driver's handle/buffers. */
int waddamburo_videoout_open(int32_t user, int32_t bus, int32_t index, const void *parameter) {
    int handle = sceVideoOutOpen(user, bus, index, parameter);
    if (handle >= 0) {
        atomic_store_explicit(&output_handle, handle, memory_order_release);
        active_refresh = 0;
    }
    return handle;
}
static void log_error(const char *operation, int result) {
    char message[192];
    snprintf(message, sizeof(message), "PS5 output: %s returned 0x%08x.", operation, (unsigned)result);
    waddamburo_ps5_log(message);
}
int waddamburo_ps5_set_refresh_rate(int fps) {
    int handle = atomic_load_explicit(&output_handle, memory_order_acquire);
    if (handle < 0) {
        waddamburo_ps5_log("PS5 output: refresh request has no open VideoOut handle.");
        return -1;
    }
    int requested = fps == 120 ? 120 : 60;
    if (active_refresh == requested) return active_refresh;
    active_refresh = 0; // A failed transition must never leave a stale cached mode.
    if (requested == 120) {
        /* The driver caches its startup support/timing probe. The user may
         * select 120 FPS after that probe rejected or restored the mode.
         * Ask VideoOut directly: neither enumeration nor an idle vblank sample
         * may veto an explicit selection. ConfigureOutput is authoritative. */
        int result = sceVideoOutConfigureOutput(handle, PS5_VIDEO_OUT_MODE_HIGH_FRAME_RATE, NULL,NULL,NULL);
        if (result == 0) {
            result = wsi_videoout_set_flip_rate(0);
            if (result) {
                log_error("120 Hz flip pacing", result);
                int restored = sceVideoOutConfigureOutput(handle, PS5_VIDEO_OUT_MODE_RESTORE, NULL,NULL,NULL);
                if (restored) log_error("60 Hz rollback", restored);
                return result;
            }
            active_refresh = 120;
            waddamburo_ps5_log("PS5 output: 120 Hz mode accepted by VideoOut; VSync on, 120 FPS selected.");
            return 120;
        }
        log_error("120 Hz ConfigureOutput", result);
    }
    int result = sceVideoOutConfigureOutput(handle, PS5_VIDEO_OUT_MODE_RESTORE, NULL,NULL,NULL);
    if (result) { log_error("60 Hz ConfigureOutput", result); return result; }
    result = wsi_videoout_set_flip_rate(0);
    if (result) { log_error("60 Hz flip pacing", result); return result; }
    active_refresh = 60;
    waddamburo_ps5_log(requested == 120
        ? "PS5 output: VideoOut refused 120 Hz; using 60 Hz/60 FPS with VSync."
        : "PS5 output: 60 Hz; VSync on, 60 FPS selected.");
    return 60;
}
