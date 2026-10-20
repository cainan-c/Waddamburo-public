/* SPDX-License-Identifier: MIT -- control the existing Vulkan VideoOut handle. */
#include <stdint.h>
#include <stddef.h>
#include <stdatomic.h>
#include <ps5platform/videoout.h>
extern uint64_t sceKernelGetProcessTimeCounter(void);
extern uint64_t sceKernelGetProcessTimeCounterFrequency(void);
extern int wsi_videoout_set_flip_rate(int rate);
extern void waddamburo_ps5_log(const char *message);
static atomic_int output_handle = -1;
static atomic_int high_refresh_available;
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
void waddamburo_display_set_high_refresh_available(int available) {
    atomic_store_explicit(&high_refresh_available, available, memory_order_release);
}
static int output_is_120hz(int handle) {
    uint64_t frequency = sceKernelGetProcessTimeCounterFrequency();
    if (!frequency || sceVideoOutWaitVblank(handle)) return 0;
    uint64_t previous = sceKernelGetProcessTimeCounter(), periods[5];
    for (unsigned i=0;i<5;i++) {
        if (sceVideoOutWaitVblank(handle)) return 0;
        uint64_t now = sceKernelGetProcessTimeCounter();
        periods[i] = (now-previous)*1000000/frequency;
        previous = now;
    }
    for (unsigned i=1;i<5;i++) for(unsigned j=i;j && periods[j]<periods[j-1];j--) {
        uint64_t value=periods[j]; periods[j]=periods[j-1]; periods[j-1]=value;
    }
    return periods[2]>=6000 && periods[2]<=12500;
}
int waddamburo_ps5_set_refresh_rate(int fps) {
    int handle = atomic_load_explicit(&output_handle, memory_order_acquire);
    if (handle < 0) return -1;
    int requested = fps == 120 ? 120 : 60;
    if (active_refresh == requested) return active_refresh;
    active_refresh = 0; // A failed transition must never leave a stale cached mode.
    if (requested == 120 && atomic_load_explicit(&high_refresh_available, memory_order_acquire)
        && sceVideoOutIsOutputSupported(handle, PS5_VIDEO_OUT_MODE_HIGH_FRAME_RATE, NULL,NULL,NULL)>0
        && sceVideoOutConfigureOutput(handle, PS5_VIDEO_OUT_MODE_HIGH_FRAME_RATE, NULL,NULL,NULL)==0
        && output_is_120hz(handle)) {
        int result = wsi_videoout_set_flip_rate(0);
        if (result) {
            sceVideoOutConfigureOutput(handle, PS5_VIDEO_OUT_MODE_RESTORE, NULL,NULL,NULL);
            return result;
        }
        active_refresh = 120;
        waddamburo_ps5_log("PS5 output: 120 Hz confirmed; VSync on, 120 FPS selected.");
        return 120;
    }
    int result = sceVideoOutConfigureOutput(handle, PS5_VIDEO_OUT_MODE_RESTORE, NULL,NULL,NULL);
    if (result) return result;
    result = wsi_videoout_set_flip_rate(0);
    if (result) return result;
    active_refresh = 60;
    waddamburo_ps5_log(requested == 120
        ? "PS5 output: 120 Hz unavailable on this output; using 60 Hz/60 FPS with VSync."
        : "PS5 output: 60 Hz; VSync on, 60 FPS selected.");
    return 60;
}
