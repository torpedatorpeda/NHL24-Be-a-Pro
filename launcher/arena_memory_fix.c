// SPDX-License-Identifier: GPL-2.0-or-later
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct { uint32_t flags, heap; } MemoryType;
typedef struct { uint64_t size; uint32_t flags, padding; } MemoryHeap;
typedef struct {
    uint32_t type_count;
    MemoryType types[32];
    uint32_t heap_count;
    MemoryHeap heaps[16];
} MemoryProperties;
typedef struct {
    uint32_t type;
    const void *next;
    uint64_t size;
    uint32_t memory_type;
} AllocateInfo;
typedef struct {
    uint32_t type;
    const void *next;
    uint32_t flags, device_mask;
} AllocateFlags;
typedef int32_t (*AllocateFn)(void *, const AllocateInfo *, const void *, uint64_t *);
typedef struct Feature {
    uint32_t type;
    const void *next;
    uint32_t enabled;
} Feature;
typedef struct {
    uint32_t type;
    const void *next;
    uint32_t features[55];
} Features2;
typedef struct { char name[256]; uint32_t version; } Extension;
typedef struct {
    uint32_t type;
    const void *next;
    uint32_t flags, queue_count;
    const void *queues;
    uint32_t layer_count;
    const char *const *layers;
    uint32_t extension_count;
    const char *const *extensions;
    const void *features;
} DeviceInfo;
typedef int32_t (*CreateDeviceFn)(void *, const DeviceInfo *, const void *, void **);
typedef int32_t (*ExtensionsFn)(void *, const char *, uint32_t *, Extension *);
typedef void (*FeaturesFn)(void *, Features2 *);

typedef struct {
    uint32_t type;
    const void *next;
    uint32_t storage_buffer16, uniform_storage16, push_constant16, input_output16;
    uint32_t multiview, multiview_geometry, multiview_tessellation;
    uint32_t variable_pointers_storage, variable_pointers, protected_memory;
    uint32_t sampler_ycbcr, shader_draw_parameters;
} Vulkan11Features;

typedef struct {
    DeviceInfo device;
    Features2 core;
    Vulkan11Features vulkan11;
} Uniform16Request;

_Static_assert(sizeof(Vulkan11Features) == 64, "Vulkan 1.1 feature layout");

// The pinned renderer supplies Features2 -> Vulkan11Features at the head of
// its creation chain. Copy just this prefix; retain every requested feature,
// the remaining chain and caller memory, including on a driver failure.
static const DeviceInfo *prepare_uniform16(void *physical, const DeviceInfo *info,
                                          FeaturesFn query, Uniform16Request *request) {
    if (!query || !info->next) return info;
    const Features2 *core = info->next;
    if (core->type != 1000059000 || !core->next) return info;
    const Vulkan11Features *features = core->next;
    if (features->type != 49 || !features->storage_buffer16 || features->uniform_storage16) return info;
    Vulkan11Features supported11 = {.type = 49};
    Features2 supported = {1000059000, &supported11, {0}};
    query(physical, &supported);
    if (!supported11.uniform_storage16) return info;
    request->device = *info;
    request->core = *core;
    request->vulkan11 = *features;
    request->vulkan11.uniform_storage16 = 1;
    request->core.next = &request->vulkan11;
    request->device.next = &request->core;
    return &request->device;
}
typedef struct {
    uint64_t memory, size, used;
} Pool;

_Static_assert(sizeof(MemoryProperties) == 520, "Vulkan memory properties layout");
_Static_assert(sizeof(AllocateInfo) == 32, "Vulkan allocation layout");
_Static_assert(sizeof(DeviceInfo) == 72, "Vulkan device creation layout");
_Static_assert(sizeof(Features2) == 240, "Vulkan feature query layout");

static SRWLOCK lock = SRWLOCK_INIT;
static void *arena_device;
static MemoryProperties properties;
static Pool pools[32];
static uint32_t allowed_types, block_alignment;
static uint32_t primary_type = UINT32_MAX, host_type = UINT32_MAX;
static uint64_t local_limit, local_bytes, host_bytes, request_count, allocation_count;
static void *paging_device, *priority_device;

static uint64_t align_up(uint64_t n, uint64_t a) {
    return (n + a - 1) & ~(a - 1);
}

static void log_memory(const char *event, uint64_t size, uint32_t type, int32_t result) {
    char line[384];
    int len = snprintf(line, sizeof(line),
        "%s request=%llu size=%llu type=%u result=%d pools=%llu local=%llu host=%llu limit=%llu\r\n",
        event, (unsigned long long)request_count, (unsigned long long)size, type, result,
        (unsigned long long)allocation_count, (unsigned long long)local_bytes,
        (unsigned long long)host_bytes, (unsigned long long)local_limit);
    HANDLE f = CreateFileA("memory-fix.log", FILE_APPEND_DATA,
        FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f != INVALID_HANDLE_VALUE) {
        DWORD done;
        WriteFile(f, line, (DWORD)len, &done, NULL);
        CloseHandle(f);
    }
}

__declspec(dllexport) int32_t CreateDeviceWithPaging(void *physical, const DeviceInfo *info,
    const void *callbacks, void **device, CreateDeviceFn original) {
    if (!original || !info) return -3;
    HMODULE loader = GetModuleHandleW(L"vulkan-1.dll");
    ExtensionsFn enumerate = loader ? (ExtensionsFn)GetProcAddress(loader,
        "vkEnumerateDeviceExtensionProperties") : NULL;
    FeaturesFn query = loader ? (FeaturesFn)GetProcAddress(loader,
        "vkGetPhysicalDeviceFeatures2") : NULL;
    Uniform16Request uniform16;
    const DeviceInfo *requested = prepare_uniform16(physical, info, query, &uniform16);
    if (requested != info) log_memory("uniform16-enabled", 0, 49, 0);
    info = requested;
    if (!enumerate || !query || info->extension_count > 4096) goto unchanged;
    uint32_t count = 0;
    if (enumerate(physical, NULL, &count, NULL) || !count || count > 4096) goto unchanged;
    Extension *extensions = calloc(count, sizeof(*extensions));
    if (!extensions) goto unchanged;
    int has_paging = 0, has_priority = 0;
    if (enumerate(physical, NULL, &count, extensions) == 0) {
        for (uint32_t i = 0; i < count; ++i) {
            if (!strcmp(extensions[i].name, "VK_EXT_pageable_device_local_memory")) has_paging = 1;
            if (!strcmp(extensions[i].name, "VK_EXT_memory_priority")) has_priority = 1;
        }
    }
    free(extensions);
    if (!has_paging || !has_priority) goto unchanged;
    Feature priority = {1000238000, NULL, 0};
    Feature paging = {1000412000, &priority, 0};
    Features2 supported = {1000059000, &paging, {0}};
    query(physical, &supported);
    if (!paging.enabled) goto unchanged;
    for (const Feature *chain = info->next; chain; chain = chain->next)
        if (chain->type == paging.type || chain->type == priority.type) goto unchanged;
    const char **names = malloc((info->extension_count + 2) * sizeof(*names));
    if (!names) goto unchanged;
    uint32_t n = info->extension_count;
    if (n) memcpy(names, info->extensions, n * sizeof(*names));
    has_paging = has_priority = 0;
    for (uint32_t i = 0; i < n; ++i) {
        if (!strcmp(names[i], "VK_EXT_pageable_device_local_memory")) has_paging = 1;
        if (!strcmp(names[i], "VK_EXT_memory_priority")) has_priority = 1;
    }
    if (!has_paging) names[n++] = "VK_EXT_pageable_device_local_memory";
    if (!has_priority) names[n++] = "VK_EXT_memory_priority";
    priority.next = info->next;
    paging.next = priority.enabled ? &priority : info->next;
    DeviceInfo changed = *info;
    changed.next = &paging;
    changed.extension_count = n;
    changed.extensions = names;
    int32_t result = original(physical, &changed, callbacks, device);
    free(names);
    if (result == -7 || result == -8) goto unchanged;
    if (result == 0) {
        paging_device = *device;
        priority_device = priority.enabled ? *device : NULL;
    }
    log_memory("pageable-device", priority.enabled, n, result);
    return result;
unchanged:
    log_memory("pageable-unavailable", 0, 0, 0);
    return original(physical, info, callbacks, device);
}

__declspec(dllexport) void ConfigureArenaMemory(const MemoryProperties *p, uint32_t bits,
                                               void *device, uint32_t alignment) {
    AcquireSRWLockExclusive(&lock);
    arena_device = NULL;
    primary_type = host_type = UINT32_MAX;
    memset(pools, 0, sizeof(pools));
    local_bytes = host_bytes = request_count = allocation_count = 0;
    if (!p || p->type_count > 32 || !p->type_count || p->heap_count > 16 ||
        !p->heap_count || !alignment || (alignment & (alignment - 1))) goto done;
    for (uint32_t i = 0; i < p->type_count; ++i) {
        if (p->types[i].heap >= p->heap_count) goto done;
        if (!(bits & (1u << i))) continue;
        uint32_t flags = p->types[i].flags;
        if ((flags & 1) && primary_type == UINT32_MAX) primary_type = i;
        if ((flags & 2) && !(flags & ~15u) &&
            !(p->heaps[p->types[i].heap].flags & 1) && host_type == UINT32_MAX) host_type = i;
    }
    if (primary_type == UINT32_MAX) goto done;
    properties = *p;
    allowed_types = bits;
    block_alignment = alignment;
    arena_device = device;
    local_limit = p->heaps[p->types[primary_type].heap].size / 4;
    if (local_limit > (4ull << 30)) local_limit = 4ull << 30;
    local_limit = local_limit / alignment * alignment;
    char value[32];
    DWORD count = GetEnvironmentVariableA("NHL_ARENA_LOCAL_MB", value, sizeof(value));
    if (count && count < sizeof(value)) {
        char *end;
        unsigned long long mb = strtoull(value, &end, 10);
        if (!*end && mb <= 16384) local_limit = mb << 20;
    }
    log_memory("configured", alignment, primary_type, (int32_t)host_type);
    log_memory(paging_device == device ? "driver-paging-active" : "driver-paging-inactive", bits, primary_type, 0);
done:
    ReleaseSRWLockExclusive(&lock);
}

static int32_t allocate_from_pool(AllocateFn original, void *device, const AllocateInfo *info,
                                 const void *callbacks, uint32_t type, uint64_t *memory,
                                 uint64_t *offset) {
    Pool *pool = &pools[type];
    uint64_t start = align_up(pool->used, block_alignment);
    if (pool->memory && start <= pool->size && info->size <= pool->size - start) {
        *memory = pool->memory;
        *offset = start;
        pool->used = start + info->size;
        return 0;
    }
    AllocateInfo request = *info;
    request.memory_type = type;
    struct { uint32_t type; const void *next; float priority; } priority =
        {1000238001, info->next, 0.25f};
    if (priority_device == device) request.next = &priority;
    uint64_t chunk_size = align_up(64ull << 20, block_alignment);
    request.size = info->size > chunk_size ? info->size : chunk_size;
    const uint32_t heap = properties.types[type].heap;
    if (request.size > properties.heaps[heap].size) request.size = info->size;
    if ((properties.heaps[heap].flags & 1) && host_type != UINT32_MAX &&
        (local_bytes >= local_limit || request.size > local_limit - local_bytes)) {
        if (info->size > local_limit || local_bytes > local_limit - info->size) return -2;
        request.size = info->size;
    }
    uint64_t handle = 0;
    int32_t result = original(device, &request, callbacks, &handle);
    if ((result == -2 || result == -1) && request.size != info->size) {
        request.size = info->size;
        result = original(device, &request, callbacks, &handle);
    }
    if (result != 0) {
        log_memory("allocation-failed", request.size, type, result);
        return result;
    }
    pool->memory = handle;
    pool->size = request.size;
    pool->used = info->size;
    *memory = handle;
    *offset = 0;
    ++allocation_count;
    if (properties.heaps[heap].flags & 1) local_bytes += request.size;
    else host_bytes += request.size;
    log_memory("new-pool", request.size, type, 0);
    return 0;
}

__declspec(dllexport) int32_t AllocateArenaMemory(void *device, const AllocateInfo *info,
    const void *callbacks, uint64_t *memory, AllocateFn original, uint64_t *offset) {
    *offset = 0;
    if (!original) return -2;
    const AllocateFlags *flags = info ? info->next : NULL;
    AcquireSRWLockExclusive(&lock);
    if (device != arena_device || !info || info->type != 5 || !info->size || callbacks ||
        primary_type >= 32 || info->memory_type != primary_type || !(allowed_types & (1u << primary_type)) ||
        info->size % block_alignment || !flags || flags->type != 1000060000 ||
        flags->next || flags->flags != 2 || flags->device_mask != 0) {
        ReleaseSRWLockExclusive(&lock);
        return original(device, info, callbacks, memory);
    }
    ++request_count;
    int32_t result = allocate_from_pool(original, device, info, callbacks, primary_type,
                                        memory, offset);
    if (result == -2 && host_type != UINT32_MAX) {
        result = allocate_from_pool(original, device, info, callbacks, host_type,
                                    memory, offset);
    }
    if (result != 0) log_memory("request-failed", info->size, info->memory_type, result);
    if (request_count == 1 || request_count % 1024 == 0)
        log_memory("requests", info->size, info->memory_type, result);
    ReleaseSRWLockExclusive(&lock);
    return result;
}
