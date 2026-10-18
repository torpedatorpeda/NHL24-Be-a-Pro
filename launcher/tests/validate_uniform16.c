// SPDX-License-Identifier: GPL-2.0-or-later
#include "../arena_memory_fix.c"
static unsigned queries;
static uint32_t available;
static void query_features(void *physical, Features2 *features) {
    if (physical != (void *)0x1234 || features->type != 1000059000) abort();
    Vulkan11Features *v11 = (Vulkan11Features *)features->next;
    if (!v11 || v11->type != 49 || v11->next) abort();
    v11->uniform_storage16 = available;
    ++queries;
}
#define CHECK(expr) do { if (!(expr)) {fprintf(stderr,"line %d: %s\n",__LINE__,#expr);return 2;} } while(0)
int main(void) {
    Feature tail = {51, NULL, 1};
    Vulkan11Features v11 = {49, &tail, 1, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 1};
    Features2 core = {1000059000, &v11, {0}};
    core.features[0] = 1; core.features[53] = 1;
    DeviceInfo info = {3, &core, 0, 2, (void *)0x5678, 0, NULL, 0, NULL, NULL};
    Uniform16Request request;
    DeviceInfo info_before = info; Features2 core_before = core; Vulkan11Features v11_before = v11;
    available = 1;
    const DeviceInfo *result = prepare_uniform16((void *)0x1234, &info, query_features, &request);
    CHECK(result == &request.device && queries == 1);
    CHECK(request.core.next == &request.vulkan11 && result->next == &request.core);
    CHECK(request.vulkan11.uniform_storage16 == 1 && request.vulkan11.next == &tail);
    request.vulkan11.uniform_storage16 = 0;
    CHECK(!memcmp(&request.vulkan11, &v11_before, sizeof(v11)));
    request.core.next = core.next; CHECK(!memcmp(&request.core, &core_before, sizeof(core)));
    request.device.next = info.next; CHECK(!memcmp(&request.device, &info_before, sizeof(info)));
    CHECK(!memcmp(&info, &info_before, sizeof(info)) && !memcmp(&core, &core_before, sizeof(core)) && !memcmp(&v11, &v11_before, sizeof(v11)));
    available = 0;
    CHECK(prepare_uniform16((void *)0x1234, &info, query_features, &request) == &info);
    CHECK(prepare_uniform16(NULL, &info, NULL, &request) == &info);
    unsigned no_storage_queries = queries; v11.storage_buffer16 = 0;
    CHECK(prepare_uniform16(NULL, &info, query_features, &request) == &info && queries == no_storage_queries);
    v11.storage_buffer16 = 1;
    unsigned before = queries; v11.uniform_storage16 = 1;
    CHECK(prepare_uniform16(NULL, &info, query_features, &request) == &info && queries == before);
    v11.uniform_storage16 = 0; v11.type = 50;
    CHECK(prepare_uniform16(NULL, &info, query_features, &request) == &info && queries == before);
    v11.type = 49; core.type = 0;
    CHECK(prepare_uniform16(NULL, &info, query_features, &request) == &info && queries == before);
    core.type = 1000059000; core.next = NULL;
    CHECK(prepare_uniform16(NULL, &info, query_features, &request) == &info && queries == before);
    core.next = &v11; info.next = NULL;
    CHECK(prepare_uniform16(NULL, &info, query_features, &request) == &info && queries == before);
    puts("15 uniform16 adapter checks passed; original chain preserved");
    return 0;
}
