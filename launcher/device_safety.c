// SPDX-License-Identifier: GPL-2.0-or-later
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "../emulator-source/src/video_core/renderer_vulkan/wave64_scratch.h"

typedef struct { uint32_t type; const void *next; uint32_t flags; size_t bytes; const uint32_t *code; } SafetyShaderInfo;
typedef int32_t (*SafetyShaderFn)(void *, const SafetyShaderInfo *, const void *, uint64_t *);
typedef struct { uint32_t type; const void *next; uint32_t flags, queue_count; const void *queues;
    uint32_t layer_count; const char *const *layers; uint32_t extension_count;
    const char *const *extensions; const void *features; } SafetyDeviceInfo;
typedef struct { uint32_t type; const void *next; uint32_t features[55]; } SafetyCoreFeatures;
typedef struct { uint32_t type; const void *next; uint32_t features[12]; } Safety11Features;
typedef struct { uint32_t type; const void *next; uint32_t fault, binary; } SafetyFaultFeatures;
typedef struct { uint32_t type; const void *next; } SafetyChain;
typedef struct { char name[256]; uint32_t version; } SafetyExtension;
typedef int32_t (*SafetyCreateFn)(void *, const SafetyDeviceInfo *, const void *, void **);
typedef int32_t (*SafetyPagingFn)(void *, const SafetyDeviceInfo *, const void *, void **, SafetyCreateFn);
typedef int32_t (*SafetyExtensionsFn)(void *, const char *, uint32_t *, SafetyExtension *);
typedef void (*SafetyFeaturesFn)(void *, SafetyCoreFeatures *);
typedef void *(*SafetyProcFn)(void *, const char *);
typedef struct { uint32_t type; void *next; uint32_t addresses, vendors; uint64_t binary_size; } SafetyFaultCounts;
typedef struct { uint32_t kind; uint64_t address, precision; } SafetyFaultAddress;
typedef struct { char description[256]; uint64_t code, data; } SafetyFaultVendor;
typedef struct { uint32_t type; void *next; char description[256]; SafetyFaultAddress *addresses;
    SafetyFaultVendor *vendors; void *binary; } SafetyFaultInfo;
typedef int32_t (*SafetyFaultFn)(void *, SafetyFaultCounts *, SafetyFaultInfo *);
typedef struct { SafetyDeviceInfo info; SafetyCoreFeatures core; Safety11Features v11;
    SafetyFaultFeatures fault; const char **names; int enabled, binary; } SafetyFaultRequest;

_Static_assert(sizeof(SafetyShaderInfo) == 40, "shader ABI");
_Static_assert(sizeof(SafetyDeviceInfo) == 72, "device ABI");
_Static_assert(sizeof(SafetyCoreFeatures) == 240 && sizeof(Safety11Features) == 64, "feature ABI");
_Static_assert(sizeof(SafetyFaultCounts) == 32 && sizeof(SafetyFaultInfo) == 296, "fault ABI");
_Static_assert(sizeof(SafetyFaultAddress) == 24 && sizeof(SafetyFaultVendor) == 272, "fault arrays ABI");

static SRWLOCK fault_lock = SRWLOCK_INIT;
static void *fault_device;
static SafetyFaultFn fault_query;
static int fault_reported, fault_enabled, fault_binary;

static void safety_log(const char *event, int enabled, int binary, int32_t result) {
    char line[256];
    int n = snprintf(line, sizeof(line), "%s pid=%lu fault=%d binary=%d result=%d\r\n",
                     event, GetCurrentProcessId(), enabled, binary, result);
    HANDLE file = CreateFileA("gpu-safety.log", FILE_APPEND_DATA,
        FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file != INVALID_HANDLE_VALUE) { DWORD done; WriteFile(file, line, (DWORD)n, &done, NULL); CloseHandle(file); }
}

static const SafetyDeviceInfo *prepare_fault_request(const SafetyDeviceInfo *info,
        int available, int binary, SafetyFaultRequest *request) {
    memset(request, 0, sizeof(*request));
    if (!info || !available || info->extension_count > 4096) return info;
    const SafetyChain *chain = (const SafetyChain *)info->next;
    for (unsigned n = 0; chain && n < 64; ++n, chain = (const SafetyChain *)chain->next) {
        if (chain->type == 1000341000) {
            const SafetyFaultFeatures *existing = (const SafetyFaultFeatures *)chain;
            request->enabled = existing->fault != 0;
            request->binary = existing->binary != 0;
            return info;
        }
    }
    // Keep Features2 -> Vulkan11 at the head: the existing uniform16 adapter
    // copies this prefix and must continue to see all requested features.
    const SafetyCoreFeatures *core = (const SafetyCoreFeatures *)info->next;
    if (!core || core->type != 1000059000 || !core->next) return info;
    const Safety11Features *v11 = (const Safety11Features *)core->next;
    if (v11->type != 49) return info;
    request->names = (const char **)malloc((info->extension_count+1)*sizeof(char *));
    if (!request->names) return info;
    uint32_t count = info->extension_count;
    if (count) memcpy(request->names, info->extensions, count*sizeof(char *));
    int already = 0;
    for (uint32_t i = 0; i < count; ++i) if (!strcmp(request->names[i], "VK_EXT_device_fault")) already = 1;
    if (!already) request->names[count++] = "VK_EXT_device_fault";
    request->info = *info; request->core = *core; request->v11 = *v11;
    request->fault = (SafetyFaultFeatures){1000341000, v11->next, 1, binary != 0};
    request->v11.next = &request->fault;
    request->core.next = &request->v11;
    request->info.next = &request->core;
    request->info.extensions = request->names;
    request->info.extension_count = count;
    request->enabled = 1; request->binary = binary != 0;
    return &request->info;
}

__declspec(dllexport) int32_t CreateDeviceWithFaults(void *physical, const SafetyDeviceInfo *info,
        const void *callbacks, void **device, SafetyCreateFn original) {
    if (!original || !info || !device) return -3;
    HMODULE loader = GetModuleHandleW(L"vulkan-1.dll");
    HMODULE memory = GetModuleHandleW(L"nhl_memory_fix.dll");
    SafetyPagingFn paging = memory ? (SafetyPagingFn)GetProcAddress(memory, "CreateDeviceWithPaging") : NULL;
    if (!paging) return -3; // Never bypass the existing memory/readback support.
    SafetyExtensionsFn enumerate = loader ? (SafetyExtensionsFn)GetProcAddress(loader, "vkEnumerateDeviceExtensionProperties") : NULL;
    SafetyFeaturesFn query = loader ? (SafetyFeaturesFn)GetProcAddress(loader, "vkGetPhysicalDeviceFeatures2") : NULL;
    SafetyProcFn proc = loader ? (SafetyProcFn)GetProcAddress(loader, "vkGetDeviceProcAddr") : NULL;
    int available = 0, binary = 0;
    if (enumerate && query) {
        uint32_t count = 0;
        if (!enumerate(physical, NULL, &count, NULL) && count && count <= 4096) {
            SafetyExtension *extensions = (SafetyExtension *)calloc(count, sizeof(*extensions));
            if (extensions && !enumerate(physical, NULL, &count, extensions)) {
                for (uint32_t i = 0; i < count; ++i)
                    if (!strcmp(extensions[i].name, "VK_EXT_device_fault")) available = 1;
            }
            free(extensions);
        }
        if (available) {
            SafetyFaultFeatures supported_fault = {1000341000, NULL, 0, 0};
            SafetyCoreFeatures supported = {1000059000, &supported_fault, {0}};
            query(physical, &supported);
            available = supported_fault.fault != 0;
            binary = supported_fault.binary != 0;
        }
    }
    SafetyFaultRequest request;
    const SafetyDeviceInfo *selected = prepare_fault_request(info, available, binary, &request);
    int32_t result = paging(physical, selected, callbacks, device, original);
    if ((result == -7 || result == -8) && selected != info) {
        request.enabled = request.binary = 0;
        result = paging(physical, info, callbacks, device, original);
    }
    free(request.names);
    if (result == 0) {
        AcquireSRWLockExclusive(&fault_lock);
        fault_device = *device; fault_reported = 0;
        fault_enabled = request.enabled; fault_binary = request.binary;
        fault_query = fault_enabled && proc ? (SafetyFaultFn)proc(*device, "vkGetDeviceFaultInfoEXT") : NULL;
        ReleaseSRWLockExclusive(&fault_lock);
    }
    safety_log("device-fault-setup", request.enabled, request.binary, result);
    return result;
}

static void write_report(HANDLE file, const char *text) {
    DWORD done; WriteFile(file, text, (DWORD)strlen(text), &done, NULL);
}

static void capture_device_lost(const char *site) {
    // All checked error paths wait for the first report to finish, preventing a
    // second renderer thread's assertion from terminating the process mid-dump.
    AcquireSRWLockExclusive(&fault_lock);
    if (fault_reported) { ReleaseSRWLockExclusive(&fault_lock); return; }
    fault_reported = 1;
    CreateDirectoryW(L"gpu-crashes", NULL);
    SYSTEMTIME time; GetSystemTime(&time);
    wchar_t path[160], binary_path[160];
    swprintf(path, 160, L"gpu-crashes\\%04u%02u%02u-%02u%02u%02u-%lu.txt", time.wYear, time.wMonth,
             time.wDay, time.wHour, time.wMinute, time.wSecond, GetCurrentProcessId());
    swprintf(binary_path, 160, L"gpu-crashes\\%04u%02u%02u-%02u%02u%02u-%lu.bin", time.wYear, time.wMonth,
             time.wDay, time.wHour, time.wMinute, time.wSecond, GetCurrentProcessId());
    HANDLE file = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) { safety_log("fault-report-open-failed", fault_enabled, fault_binary, (int32_t)GetLastError()); ReleaseSRWLockExclusive(&fault_lock); return; }
    char line[768];
    snprintf(line, sizeof(line), "NHL24 GPU fault report\r\nUTC: %04u-%02u-%02u %02u:%02u:%02u\r\nDetection: %s\r\nResult: VK_ERROR_DEVICE_LOST (-4)\r\nEnabled: %d; vendor binary: %d\r\n",
             time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute, time.wSecond, site, fault_enabled, fault_binary);
    write_report(file, line); FlushFileBuffers(file);
    if (!fault_enabled || !fault_query || !fault_device) {
        write_report(file, "Driver fault information unavailable on this device.\r\n");
    } else {
        SafetyFaultCounts counts = {1000341001, NULL, 0, 0, 0};
        int32_t result = fault_query(fault_device, &counts, NULL);
        snprintf(line, sizeof(line), "Count query: %d\r\nAddresses available: %u\r\nVendor entries available: %u\r\nBinary bytes available: %llu\r\n", result, counts.addresses, counts.vendors, (unsigned long long)counts.binary_size);
        write_report(file, line);
        if (result == 0) {
            const uint32_t address_cap = counts.addresses > 4096 ? 4096 : counts.addresses;
            const uint32_t vendor_cap = counts.vendors > 4096 ? 4096 : counts.vendors;
            const uint64_t binary_cap = counts.binary_size > (64ull << 20) ? 0 : counts.binary_size;
            SafetyFaultAddress *addresses = address_cap ? (SafetyFaultAddress *)calloc(address_cap, sizeof(*addresses)) : NULL;
            SafetyFaultVendor *vendors = vendor_cap ? (SafetyFaultVendor *)calloc(vendor_cap, sizeof(*vendors)) : NULL;
            void *binary_data = binary_cap ? malloc((size_t)binary_cap) : NULL;
            counts.addresses = addresses ? address_cap : 0;
            counts.vendors = vendors ? vendor_cap : 0;
            counts.binary_size = binary_data ? binary_cap : 0;
            SafetyFaultInfo details = {1000341002, NULL, {0}, addresses, vendors, binary_data};
            result = fault_query(fault_device, &counts, &details);
            snprintf(line, sizeof(line), "Details query: %d\r\nDescription: %.255s\r\n", result, details.description);
            write_report(file, line);
            if (result == 0 || result == 5) { // VK_INCOMPLETE still supplies bounded valid data.
                for (uint32_t i = 0; addresses && i < counts.addresses && i < address_cap; ++i) {
                    snprintf(line, sizeof(line), "Address %u: type=%u reported=0x%llx precision=0x%llx\r\n", i, addresses[i].kind, (unsigned long long)addresses[i].address, (unsigned long long)addresses[i].precision);
                    write_report(file, line);
                }
                for (uint32_t i = 0; vendors && i < counts.vendors && i < vendor_cap; ++i) {
                    snprintf(line, sizeof(line), "Vendor %u: code=0x%llx data=0x%llx description=%.255s\r\n", i, (unsigned long long)vendors[i].code, (unsigned long long)vendors[i].data, vendors[i].description);
                    write_report(file, line);
                }
                if (binary_data && counts.binary_size && counts.binary_size <= binary_cap) {
                    HANDLE output = CreateFileW(binary_path, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
                    if (output != INVALID_HANDLE_VALUE) { DWORD done; WriteFile(output, binary_data, (DWORD)counts.binary_size, &done, NULL); CloseHandle(output); }
                }
            }
            free(addresses); free(vendors); free(binary_data);
        }
    }
    FlushFileBuffers(file); CloseHandle(file);
    safety_log("device-lost-recorded", fault_enabled, fault_binary, -4);
    ReleaseSRWLockExclusive(&fault_lock);
}

typedef int32_t (*SafetyQueueFn)(void *, uint32_t, const void *, uint64_t);
typedef int32_t (*SafetyWaitFn)(void *, const void *, uint64_t);
typedef int32_t (*SafetyCounterFn)(void *, uint64_t, uint64_t *);
__declspec(dllexport) int32_t QueueSubmitChecked(void *queue, uint32_t count, const void *info, uint64_t fence, SafetyQueueFn original) {
    int32_t result = original(queue, count, info, fence);
    if (result == -4) capture_device_lost("vkQueueSubmit");
    return result;
}
__declspec(dllexport) int32_t QueueBindSparseChecked(void *queue, uint32_t count, const void *info, uint64_t fence, SafetyQueueFn original) {
    int32_t result = original(queue, count, info, fence);
    if (result == -4) capture_device_lost("vkQueueBindSparse");
    return result;
}
__declspec(dllexport) int32_t WaitSemaphoresChecked(void *device, const void *info, uint64_t timeout, SafetyWaitFn original) {
    int32_t result = original(device, info, timeout);
    if (result == -4) capture_device_lost("vkWaitSemaphores");
    return result;
}
__declspec(dllexport) int32_t GetSemaphoreCounterChecked(void *device, uint64_t semaphore, uint64_t *counter, SafetyCounterFn original) {
    int32_t result = original(device, semaphore, counter);
    if (result == -4) capture_device_lost("vkGetSemaphoreCounterValue");
    return result;
}

__declspec(dllexport) int32_t CreateShaderModuleChecked(void *device, const SafetyShaderInfo *info,
        const void *callbacks, uint64_t *module, SafetyShaderFn original) {
    SafetyShaderInfo selected;
    size_t words = 0; uint32_t stores = 0;
    uint32_t *repaired = info && info->type == 16 && !(info->bytes & 3)
        ? nhl_wave64_scratch_atomic(info->code, info->bytes/4, &words, &stores) : NULL;
    if (repaired) { selected = *info; selected.code = repaired; selected.bytes = words*4; info = &selected; }
    size_t ordered_words = 0; uint32_t barriers = 0;
    uint32_t *ordered = info && info->type == 16 && !(info->bytes & 3)
        ? nhl_shared_read_write_barriers(info->code, info->bytes/4, &ordered_words, &barriers) : NULL;
    if (ordered) { selected = *info; selected.code = ordered; selected.bytes = ordered_words*4; info = &selected; }
    int32_t result = original(device, info, callbacks, module);
    if (repaired) { safety_log("wave64-scratch-stores", (int)stores, 0, result); free(repaired); }
    if (ordered) { safety_log("shared-read-write-barriers", (int)barriers, 0, result); free(ordered); }
    if (result == -4) capture_device_lost("vkCreateShaderModule");
    return result;
}
