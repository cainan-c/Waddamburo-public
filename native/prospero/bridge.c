/* SPDX-License-Identifier: MIT -- native PS5 Vulkan display, no window-system library. */
#define VK_NO_PROTOTYPES
#include <vulkan/vulkan.h>
#include <ps5platform/klog.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
void* __dso_handle = &__dso_handle;
extern int sceUserServiceInitialize(const void*);
extern int sceSystemServiceHideSplashScreen(void);
extern int sceSystemServiceLoadExec(const char*, const char* const*);
extern int sceKernelUsleep(uint32_t);
extern VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vk_icdGetInstanceProcAddr(VkInstance, const char*);
extern VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vk_common_GetDeviceProcAddr(VkDevice, const char*);
void waddamburo_ps5_log(const char* message) {
    sceKernelDebugOutText(0,"[Waddamburo native] "); sceKernelDebugOutText(0,message); sceKernelDebugOutText(0,"\n");
}
extern void waddamburo_sqlite_trace_start(void);
void waddamburo_ps5_init(const char* title) {
    (void)title;
    waddamburo_ps5_log("managed entry reached; SDL-free platform");
    waddamburo_sqlite_trace_start();
    int32_t parameter=256; (void)sceUserServiceInitialize(&parameter);
    (void)ps5_klog_capture_stderr("[Waddamburo] ");
    setenv("XDG_CACHE_HOME","/data/Waddamburo/cache",1); sceSystemServiceHideSplashScreen();
}
void catchReturnFromMain(int status) {
    (void)status; sceSystemServiceLoadExec("exit",NULL); for (;;) sceKernelUsleep(100000);
}
PFN_vkVoidFunction waddamburo_ps5_vk_proc(const char* name) {
    if (!strcmp(name,"vkGetInstanceProcAddr")) return (PFN_vkVoidFunction)vk_icdGetInstanceProcAddr;
    if (!strcmp(name,"vkGetDeviceProcAddr")) return (PFN_vkVoidFunction)vk_common_GetDeviceProcAddr;
    return vk_icdGetInstanceProcAddr(VK_NULL_HANDLE,name);
}
typedef struct {
    VkPhysicalDevice physical; VkDisplayKHR display; VkDisplayModeKHR mode; VkExtent2D extent;
    uint32_t plane,stack; VkDisplayPlaneAlphaFlagBitsKHR alpha;
} NativeDisplay;
#define PROC(type,name) type name=(type)vk_icdGetInstanceProcAddr(instance,#name)
extern void waddamburo_display_set_high_refresh_available(int available);
static VkResult discover(VkInstance instance,NativeDisplay* out) {
    PROC(PFN_vkEnumeratePhysicalDevices,vkEnumeratePhysicalDevices);
    PROC(PFN_vkGetPhysicalDeviceDisplayPropertiesKHR,vkGetPhysicalDeviceDisplayPropertiesKHR);
    PROC(PFN_vkGetDisplayModePropertiesKHR,vkGetDisplayModePropertiesKHR);
    PROC(PFN_vkGetPhysicalDeviceDisplayPlanePropertiesKHR,vkGetPhysicalDeviceDisplayPlanePropertiesKHR);
    PROC(PFN_vkGetDisplayPlaneSupportedDisplaysKHR,vkGetDisplayPlaneSupportedDisplaysKHR);
    PROC(PFN_vkGetDisplayPlaneCapabilitiesKHR,vkGetDisplayPlaneCapabilitiesKHR);
    if (!vkEnumeratePhysicalDevices || !vkGetPhysicalDeviceDisplayPropertiesKHR || !vkGetDisplayModePropertiesKHR
        || !vkGetPhysicalDeviceDisplayPlanePropertiesKHR || !vkGetDisplayPlaneSupportedDisplaysKHR
        || !vkGetDisplayPlaneCapabilitiesKHR) return VK_ERROR_EXTENSION_NOT_PRESENT;
    uint32_t count=0;
    if (vkEnumeratePhysicalDevices(instance,&count,NULL)!=VK_SUCCESS || !count) return VK_ERROR_INITIALIZATION_FAILED;
    VkPhysicalDevice* devices=calloc(count,sizeof(*devices)); if (!devices) return VK_ERROR_OUT_OF_HOST_MEMORY;
    VkResult result=vkEnumeratePhysicalDevices(instance,&count,devices);
    if (result!=VK_SUCCESS) { free(devices); return result; }
    out->physical=devices[0]; free(devices); count=0;
    if (vkGetPhysicalDeviceDisplayPropertiesKHR(out->physical,&count,NULL)!=VK_SUCCESS || !count) return VK_ERROR_INITIALIZATION_FAILED;
    VkDisplayPropertiesKHR* displays=calloc(count,sizeof(*displays)); if (!displays) return VK_ERROR_OUT_OF_HOST_MEMORY;
    result=vkGetPhysicalDeviceDisplayPropertiesKHR(out->physical,&count,displays);
    if (result!=VK_SUCCESS) { free(displays); return result; }
    out->display=displays[0].display; free(displays); count=0;
    if (vkGetDisplayModePropertiesKHR(out->physical,out->display,&count,NULL)!=VK_SUCCESS || !count) return VK_ERROR_INITIALIZATION_FAILED;
    VkDisplayModePropertiesKHR* modes=calloc(count,sizeof(*modes)); if (!modes) return VK_ERROR_OUT_OF_HOST_MEMORY;
    result=vkGetDisplayModePropertiesKHR(out->physical,out->display,&count,modes);
    if (result!=VK_SUCCESS) { free(modes); return result; }
    int high_refresh=0;
    for (uint32_t i=0;i<count;i++) if (modes[i].parameters.refreshRate>=100000) high_refresh=1;
    waddamburo_display_set_high_refresh_available(high_refresh);
    out->mode=modes[0].displayMode; out->extent=modes[0].parameters.visibleRegion; free(modes); count=0;
    if (vkGetPhysicalDeviceDisplayPlanePropertiesKHR(out->physical,&count,NULL)!=VK_SUCCESS || !count) return VK_ERROR_INITIALIZATION_FAILED;
    VkDisplayPlanePropertiesKHR* planes=calloc(count,sizeof(*planes)); if (!planes) return VK_ERROR_OUT_OF_HOST_MEMORY;
    result=vkGetPhysicalDeviceDisplayPlanePropertiesKHR(out->physical,&count,planes);
    if (result!=VK_SUCCESS) { free(planes); return result; }
    int found=0;
    for (uint32_t i=0;i<count && !found;i++) {
        uint32_t supported_count=0;
        if (vkGetDisplayPlaneSupportedDisplaysKHR(out->physical,i,&supported_count,NULL)!=VK_SUCCESS) continue;
        VkDisplayKHR* supported=calloc(supported_count,sizeof(*supported)); if (!supported) continue;
        if (vkGetDisplayPlaneSupportedDisplaysKHR(out->physical,i,&supported_count,supported)==VK_SUCCESS)
            for (uint32_t j=0;j<supported_count;j++) if (supported[j]==out->display) {
                VkDisplayPlaneCapabilitiesKHR caps;
                if (vkGetDisplayPlaneCapabilitiesKHR(out->physical,out->mode,i,&caps)==VK_SUCCESS && caps.supportedAlpha) {
                    out->plane=i; out->stack=planes[i].currentStackIndex;
                    out->alpha=(VkDisplayPlaneAlphaFlagBitsKHR)(caps.supportedAlpha & (0u-caps.supportedAlpha)); found=1; break;
                }
            }
        free(supported);
    }
    free(planes); return found ? VK_SUCCESS : VK_ERROR_INITIALIZATION_FAILED;
}
int waddamburo_ps5_display_size(int* width,int* height) {
    if (!width || !height) return VK_ERROR_INITIALIZATION_FAILED;
    *width=*height=0;
    PFN_vkCreateInstance create=(PFN_vkCreateInstance)vk_icdGetInstanceProcAddr(VK_NULL_HANDLE,"vkCreateInstance");
    if (!create) return VK_ERROR_INITIALIZATION_FAILED;
    const char* extensions[]={VK_KHR_SURFACE_EXTENSION_NAME,VK_KHR_DISPLAY_EXTENSION_NAME};
    VkApplicationInfo app={.sType=VK_STRUCTURE_TYPE_APPLICATION_INFO,.pApplicationName="Waddamburo display",.apiVersion=VK_API_VERSION_1_0};
    VkInstanceCreateInfo info={.sType=VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO,.pApplicationInfo=&app,.enabledExtensionCount=2,.ppEnabledExtensionNames=extensions};
    VkInstance instance=VK_NULL_HANDLE; VkResult result=create(&info,NULL,&instance); if (result!=VK_SUCCESS) return result;
    NativeDisplay display={0}; result=discover(instance,&display);
    if (result==VK_SUCCESS) { *width=(int)display.extent.width; *height=(int)display.extent.height; }
    PROC(PFN_vkDestroyInstance,vkDestroyInstance); if (vkDestroyInstance) vkDestroyInstance(instance,NULL);
    return result;
}
VkResult waddamburo_ps5_vk_surface(void* unused,VkInstance instance,const VkAllocationCallbacks* allocator,VkSurfaceKHR* surface) {
    (void)unused; waddamburo_ps5_log("creating native Vulkan display surface");
    NativeDisplay display={0}; VkResult result=discover(instance,&display); if (result!=VK_SUCCESS) return result;
    PROC(PFN_vkCreateDisplayPlaneSurfaceKHR,vkCreateDisplayPlaneSurfaceKHR); if (!vkCreateDisplayPlaneSurfaceKHR) return VK_ERROR_EXTENSION_NOT_PRESENT;
    VkDisplaySurfaceCreateInfoKHR info={.sType=VK_STRUCTURE_TYPE_DISPLAY_SURFACE_CREATE_INFO_KHR,.displayMode=display.mode,
        .planeIndex=display.plane,.planeStackIndex=display.stack,.transform=VK_SURFACE_TRANSFORM_IDENTITY_BIT_KHR,
        .globalAlpha=1,.alphaMode=display.alpha,.imageExtent=display.extent};
    return vkCreateDisplayPlaneSurfaceKHR(instance,&info,allocator,surface);
}
