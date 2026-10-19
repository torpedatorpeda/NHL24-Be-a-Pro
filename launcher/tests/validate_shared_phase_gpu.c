// SPDX-License-Identifier: GPL-2.0-or-later
#define WIN32_LEAN_AND_MEAN
#define VK_NO_PROTOTYPES
#include <windows.h>
#include <vulkan/vulkan_core.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#define APIS(X) X(vkCreateInstance) X(vkDestroyInstance) X(vkEnumeratePhysicalDevices) \
 X(vkGetPhysicalDeviceProperties) X(vkGetPhysicalDeviceMemoryProperties) X(vkGetPhysicalDeviceQueueFamilyProperties) \
 X(vkCreateDevice) X(vkDestroyDevice) X(vkGetDeviceQueue) X(vkCreateBuffer) X(vkDestroyBuffer) \
 X(vkGetBufferMemoryRequirements) X(vkAllocateMemory) X(vkFreeMemory) X(vkBindBufferMemory) X(vkMapMemory) X(vkUnmapMemory) \
 X(vkCreateShaderModule) X(vkDestroyShaderModule) X(vkCreateDescriptorSetLayout) X(vkDestroyDescriptorSetLayout) \
 X(vkCreateDescriptorPool) X(vkDestroyDescriptorPool) X(vkAllocateDescriptorSets) X(vkUpdateDescriptorSets) \
 X(vkCreatePipelineLayout) X(vkDestroyPipelineLayout) X(vkCreateComputePipelines) X(vkDestroyPipeline) \
 X(vkCreateCommandPool) X(vkDestroyCommandPool) X(vkAllocateCommandBuffers) X(vkBeginCommandBuffer) X(vkEndCommandBuffer) \
 X(vkCmdBindPipeline) X(vkCmdBindDescriptorSets) X(vkCmdDispatch) X(vkCmdPipelineBarrier) X(vkQueueSubmit) X(vkQueueWaitIdle)
#define DECLARE(n) static PFN_##n n;
APIS(DECLARE)
#define CHECK(x) do { VkResult r=(x);if(r!=VK_SUCCESS){fprintf(stderr,"%s failed %d at %d\n",#x,r,__LINE__);return 2;} } while(0)
typedef VkResult (*CheckedDevice)(VkPhysicalDevice,const VkDeviceCreateInfo *,const VkAllocationCallbacks *,VkDevice *,PFN_vkCreateDevice);
typedef VkResult (*CheckedShader)(VkDevice,const VkShaderModuleCreateInfo *,const VkAllocationCallbacks *,VkShaderModule *,PFN_vkCreateShaderModule);
int main(int argc,char **argv) {
    if(argc<3)return 2;
    int baseline=argc>3 && !strcmp(argv[3],"baseline");
    char path[4096];snprintf(path,sizeof(path),"%s\\nhl_memory_fix.dll",argv[1]);if(!LoadLibraryA(path))return 2;
    snprintf(path,sizeof(path),"%s\\nhl_device_fix.dll",argv[1]);HMODULE safety=LoadLibraryA(path);if(!safety)return 2;
    CheckedDevice create_checked=(CheckedDevice)GetProcAddress(safety,"CreateDeviceWithFaults");
    CheckedShader shader_checked=(CheckedShader)GetProcAddress(safety,"CreateShaderModuleChecked");if(!create_checked||!shader_checked)return 2;
    HMODULE loader=LoadLibraryA("vulkan-1.dll");if(!loader)return 2;
#define LOAD(n) n=(PFN_##n)GetProcAddress(loader,#n);if(!n)return 2;
    APIS(LOAD)
    VkApplicationInfo app={.sType=VK_STRUCTURE_TYPE_APPLICATION_INFO,.pApplicationName="Shared memory phase validation",.apiVersion=(1u<<22)|(3u<<12)};
    VkInstanceCreateInfo ici={.sType=VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO,.pApplicationInfo=&app};VkInstance instance;CHECK(vkCreateInstance(&ici,NULL,&instance));
    uint32_t count=8;VkPhysicalDevice physical[8];CHECK(vkEnumeratePhysicalDevices(instance,&count,physical));if(!count)return 2;
    VkPhysicalDeviceProperties props;vkGetPhysicalDeviceProperties(physical[0],&props);printf("GPU: %s\n",props.deviceName);
    VkQueueFamilyProperties families[64];count=64;vkGetPhysicalDeviceQueueFamilyProperties(physical[0],&count,families);uint32_t family=0;
    while(family<count&&!(families[family].queueFlags&VK_QUEUE_COMPUTE_BIT))++family;if(family==count)return 2;
    float priority=1;VkDeviceQueueCreateInfo qci={.sType=VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO,.queueFamilyIndex=family,.queueCount=1,.pQueuePriorities=&priority};
    VkPhysicalDeviceVulkan11Features v11={.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES,.storageBuffer16BitAccess=1};
    VkPhysicalDeviceFeatures2 features={.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,.pNext=&v11,.features={.robustBufferAccess=1}};
    VkDeviceCreateInfo dci={.sType=VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO,.pNext=&features,.queueCreateInfoCount=1,.pQueueCreateInfos=&qci};
    VkDevice device;CHECK(create_checked(physical[0],&dci,NULL,&device,vkCreateDevice));VkQueue queue;vkGetDeviceQueue(device,family,0,&queue);
    VkBufferCreateInfo bci={.sType=VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO,.size=256*sizeof(uint32_t),.usage=VK_BUFFER_USAGE_STORAGE_BUFFER_BIT};VkBuffer buffer;CHECK(vkCreateBuffer(device,&bci,NULL,&buffer));
    VkMemoryRequirements requirements;vkGetBufferMemoryRequirements(device,buffer,&requirements);VkPhysicalDeviceMemoryProperties memory_props;vkGetPhysicalDeviceMemoryProperties(physical[0],&memory_props);
    uint32_t memory_type=0;while(memory_type<memory_props.memoryTypeCount && (!(requirements.memoryTypeBits&(1u<<memory_type)) || (memory_props.memoryTypes[memory_type].propertyFlags&(VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT|VK_MEMORY_PROPERTY_HOST_COHERENT_BIT))!=(VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT|VK_MEMORY_PROPERTY_HOST_COHERENT_BIT)))++memory_type;
    if(memory_type==memory_props.memoryTypeCount)return 2;
    VkMemoryAllocateInfo mai={.sType=VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,.allocationSize=requirements.size,.memoryTypeIndex=memory_type};VkDeviceMemory memory;CHECK(vkAllocateMemory(device,&mai,NULL,&memory));CHECK(vkBindBufferMemory(device,buffer,memory,0));
    uint32_t *mapped;CHECK(vkMapMemory(device,memory,0,VK_WHOLE_SIZE,0,(void **)&mapped));for(uint32_t i=0;i<256;++i)mapped[i]=0xdeadbeef;
    FILE *file=fopen(argv[2],"rb");if(!file)return 2;fseek(file,0,SEEK_END);long bytes=ftell(file);rewind(file);uint32_t *code=malloc((size_t)bytes);if(!code||fread(code,1,(size_t)bytes,file)!=(size_t)bytes)return 2;fclose(file);
    VkShaderModuleCreateInfo sci={.sType=VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO,.codeSize=(size_t)bytes,.pCode=code};VkShaderModule module;
    CHECK(baseline?vkCreateShaderModule(device,&sci,NULL,&module):shader_checked(device,&sci,NULL,&module,vkCreateShaderModule));free(code);
    VkDescriptorSetLayoutBinding binding={.binding=0,.descriptorType=VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,.descriptorCount=1,.stageFlags=VK_SHADER_STAGE_COMPUTE_BIT};VkDescriptorSetLayoutCreateInfo slci={.sType=VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO,.bindingCount=1,.pBindings=&binding};VkDescriptorSetLayout set_layout;CHECK(vkCreateDescriptorSetLayout(device,&slci,NULL,&set_layout));
    VkDescriptorPoolSize pool_size={VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,1};VkDescriptorPoolCreateInfo dpci={.sType=VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO,.maxSets=1,.poolSizeCount=1,.pPoolSizes=&pool_size};VkDescriptorPool pool;CHECK(vkCreateDescriptorPool(device,&dpci,NULL,&pool));
    VkDescriptorSetAllocateInfo dsai={.sType=VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO,.descriptorPool=pool,.descriptorSetCount=1,.pSetLayouts=&set_layout};VkDescriptorSet set;CHECK(vkAllocateDescriptorSets(device,&dsai,&set));
    VkDescriptorBufferInfo dbi={buffer,0,256*sizeof(uint32_t)};VkWriteDescriptorSet write={.sType=VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET,.dstSet=set,.dstBinding=0,.descriptorCount=1,.descriptorType=VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,.pBufferInfo=&dbi};vkUpdateDescriptorSets(device,1,&write,0,NULL);
    VkPipelineLayoutCreateInfo plci={.sType=VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO,.setLayoutCount=1,.pSetLayouts=&set_layout};VkPipelineLayout layout;CHECK(vkCreatePipelineLayout(device,&plci,NULL,&layout));
    VkComputePipelineCreateInfo cpci={.sType=VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO,.stage={.sType=VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,.stage=VK_SHADER_STAGE_COMPUTE_BIT,.module=module,.pName="main"},.layout=layout};VkPipeline pipeline;CHECK(vkCreateComputePipelines(device,VK_NULL_HANDLE,1,&cpci,NULL,&pipeline));
    VkCommandPoolCreateInfo pci={.sType=VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,.queueFamilyIndex=family};VkCommandPool commands;CHECK(vkCreateCommandPool(device,&pci,NULL,&commands));
    VkCommandBufferAllocateInfo cai={.sType=VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,.commandPool=commands,.level=VK_COMMAND_BUFFER_LEVEL_PRIMARY,.commandBufferCount=1};VkCommandBuffer command;CHECK(vkAllocateCommandBuffers(device,&cai,&command));VkCommandBufferBeginInfo begin={.sType=VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};CHECK(vkBeginCommandBuffer(command,&begin));
    vkCmdBindPipeline(command,VK_PIPELINE_BIND_POINT_COMPUTE,pipeline);vkCmdBindDescriptorSets(command,VK_PIPELINE_BIND_POINT_COMPUTE,layout,0,1,&set,0,NULL);vkCmdDispatch(command,1,1,1);
    VkBufferMemoryBarrier barrier={.sType=VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER,.srcAccessMask=VK_ACCESS_SHADER_WRITE_BIT,.dstAccessMask=VK_ACCESS_HOST_READ_BIT,.srcQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED,.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED,.buffer=buffer,.size=VK_WHOLE_SIZE};vkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,VK_PIPELINE_STAGE_HOST_BIT,0,0,NULL,1,&barrier,0,NULL);CHECK(vkEndCommandBuffer(command));VkSubmitInfo submit={.sType=VK_STRUCTURE_TYPE_SUBMIT_INFO,.commandBufferCount=1,.pCommandBuffers=&command};CHECK(vkQueueSubmit(queue,1,&submit,VK_NULL_HANDLE));CHECK(vkQueueWaitIdle(queue));
    uint32_t failures=0;for(uint32_t i=0;i<256;++i)if(mapped[i]!=(i<128?1000+i:i))++failures;
    printf("Mode: %s; mismatches: %u of 256\n",baseline?"original":"repaired",failures);
    vkDestroyCommandPool(device,commands,NULL);vkDestroyPipeline(device,pipeline,NULL);vkDestroyPipelineLayout(device,layout,NULL);vkDestroyDescriptorPool(device,pool,NULL);vkDestroyDescriptorSetLayout(device,set_layout,NULL);vkDestroyShaderModule(device,module,NULL);vkUnmapMemory(device,memory);vkDestroyBuffer(device,buffer,NULL);vkFreeMemory(device,memory,NULL);vkDestroyDevice(device,NULL);vkDestroyInstance(instance,NULL);
    return !baseline&&failures?2:0;
}
