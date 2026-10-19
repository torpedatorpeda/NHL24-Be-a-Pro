// SPDX-License-Identifier: GPL-2.0-or-later
// Synthetic driver callbacks only: never reset or lose a real GPU.
#include "../device_safety.c"
#define REQUIRE(x) do { if (!(x)) { fprintf(stderr,"Failed %s:%d: %s\n",__FILE__,__LINE__,#x); return 2; } } while (0)
static volatile LONG queries, returns;
static int32_t fake_fault(void *device, SafetyFaultCounts *counts, SafetyFaultInfo *info) {
    if (device != (void *)0x1234 || counts->type != 1000341001) return -13;
    InterlockedIncrement(&queries);
    if (!info) { counts->addresses=2;counts->vendors=1;counts->binary_size=4;return 0; }
    Sleep(40); // Let the concurrent error paths reach the report lock.
    if (counts->addresses != 2 || counts->vendors != 1 || counts->binary_size != 4 || info->type != 1000341002) return -13;
    strcpy(info->description,"Synthetic GPU fault for report validation");
    info->addresses[0]=(SafetyFaultAddress){1,0xabcde000,4096};
    info->addresses[1]=(SafetyFaultAddress){2,0xfedcb000,256};
    strcpy(info->vendors[0].description,"Synthetic vendor fault");
    info->vendors[0].code=0x7654;info->vendors[0].data=0x3210;
    memcpy(info->binary,"TEST",4);
    return 0;
}
static int32_t fake_queue(void *queue, uint32_t count, const void *info, uint64_t fence) {
    if (queue!=(void *)0x11 || count!=1 || info!=(void *)0x22 || fence!=0x123456789ull) return -13;
    return -4;
}
static DWORD WINAPI error_thread(void *unused) {
    (void)unused;
    if (QueueSubmitChecked((void *)0x11,1,(void *)0x22,0x123456789ull,fake_queue)==-4)
        InterlockedIncrement(&returns);
    return 0;
}
int main(int argc,char **argv) {
    REQUIRE(argc==2 && CreateDirectoryA(argv[1],NULL) && SetCurrentDirectoryA(argv[1]));
    const char *extensions[]={"VK_KHR_swapchain"};
    SafetyChain tail={12345,NULL};
    Safety11Features v11={49,&tail,{1,0,1,0,1,0,1,0,1,0,1,0}};
    SafetyCoreFeatures core={1000059000,&v11,{0}};
    core.features[0]=1;core.features[10]=1;
    SafetyDeviceInfo original={.type=3,.next=&core,.extension_count=1,.extensions=extensions};
    SafetyDeviceInfo before_info=original;SafetyCoreFeatures before_core=core;Safety11Features before_v11=v11;
    SafetyFaultRequest request;
    const SafetyDeviceInfo *selected=prepare_fault_request(&original,1,1,&request);
    REQUIRE(selected==&request.info && selected->extension_count==2 && request.enabled && request.binary);
    REQUIRE(selected->next==&request.core && request.core.next==&request.v11);
    REQUIRE(request.v11.next==&request.fault && request.fault.next==&tail);
    REQUIRE(request.fault.type==1000341000 && request.fault.fault==1 && request.fault.binary==1);
    REQUIRE(!memcmp(&original,&before_info,sizeof(original)) && !memcmp(&core,&before_core,sizeof(core)) && !memcmp(&v11,&before_v11,sizeof(v11)));
    REQUIRE(!strcmp(selected->extensions[1],"VK_EXT_device_fault"));free(request.names);
    REQUIRE(prepare_fault_request(&original,0,1,&request)==&original && !request.enabled && !request.names);
    const char *existing_extensions[]={"VK_EXT_device_fault"};
    original.extensions=existing_extensions;
    selected=prepare_fault_request(&original,1,0,&request);
    REQUIRE(selected->extension_count==1 && request.enabled && !request.binary);free(request.names);
    SafetyFaultFeatures existing={1000341000,&tail,1,0};v11.next=&existing;
    REQUIRE(prepare_fault_request(&original,1,1,&request)==&original && request.enabled && !request.binary && !request.names);
    existing.fault=0;
    REQUIRE(prepare_fault_request(&original,1,1,&request)==&original && !request.enabled);
    v11.next=&tail;core.type=0;
    REQUIRE(prepare_fault_request(&original,1,1,&request)==&original && !request.enabled && !request.names);
    core.type=1000059000;
    fault_device=(void *)0x1234;fault_enabled=fault_binary=1;fault_query=fake_fault;fault_reported=0;
    HANDLE threads[4];for(unsigned i=0;i<4;++i){threads[i]=CreateThread(NULL,0,error_thread,NULL,0,NULL);REQUIRE(threads[i]);}
    REQUIRE(WaitForMultipleObjects(4,threads,TRUE,10000)==WAIT_OBJECT_0);
    for(unsigned i=0;i<4;++i)CloseHandle(threads[i]);
    REQUIRE(returns==4 && queries==2 && fault_reported);
    WIN32_FIND_DATAA entry;HANDLE search=FindFirstFileA("gpu-crashes\\*.txt",&entry);REQUIRE(search!=INVALID_HANDLE_VALUE);
    char path[512];snprintf(path,sizeof(path),"gpu-crashes\\%s",entry.cFileName);
    REQUIRE(!FindNextFileA(search,&entry));FindClose(search);
    FILE *report=fopen(path,"rb");REQUIRE(report);char text[4096]={0};fread(text,1,sizeof(text)-1,report);fclose(report);
    REQUIRE(strstr(text,"Synthetic GPU fault") && strstr(text,"0xabcde000") && strstr(text,"0xfedcb000") && strstr(text,"code=0x7654"));
    search=FindFirstFileA("gpu-crashes\\*.bin",&entry);REQUIRE(search!=INVALID_HANDLE_VALUE);
    snprintf(path,sizeof(path),"gpu-crashes\\%s",entry.cFileName);REQUIRE(!FindNextFileA(search,&entry));FindClose(search);
    report=fopen(path,"rb");REQUIRE(report);char data[5]={0};REQUIRE(fread(data,1,5,report)==4);fclose(report);REQUIRE(!memcmp(data,"TEST",4));
    puts("Device-fault checks passed: feature-chain preservation, supported/unsupported/duplicate cases, one full report from four concurrent errors, addresses/vendor/binary retained, original -4 preserved.");
    return 0;
}
