// SPDX-License-Identifier: GPL-2.0-or-later
// Actual GPU control: reproduce a missing depth-store dependency, then repair it.
#define WIN32_LEAN_AND_MEAN
#define VK_NO_PROTOTYPES
#include <windows.h>
#include <vulkan/vulkan_core.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static unsigned errors;
static VKAPI_ATTR VkBool32 VKAPI_CALL message(VkDebugUtilsMessageSeverityFlagBitsEXT severity,
    VkDebugUtilsMessageTypeFlagsEXT type,const VkDebugUtilsMessengerCallbackDataEXT *data,void *user) {
    (void)type;(void)user;
    if(severity&VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT){++errors;fprintf(stderr,"%s\n",data->pMessageIdName);}
    return VK_FALSE;
}
#define APIS(X) X(vkCreateInstance) X(vkDestroyInstance) X(vkEnumeratePhysicalDevices) X(vkGetPhysicalDeviceProperties) X(vkGetPhysicalDeviceMemoryProperties) X(vkGetPhysicalDeviceQueueFamilyProperties) X(vkCreateDevice) X(vkDestroyDevice) X(vkGetDeviceQueue) X(vkGetDeviceProcAddr) X(vkCreateImage) X(vkDestroyImage) X(vkGetImageMemoryRequirements) X(vkAllocateMemory) X(vkFreeMemory) X(vkBindImageMemory) X(vkCreateImageView) X(vkDestroyImageView) X(vkCreateCommandPool) X(vkDestroyCommandPool) X(vkAllocateCommandBuffers) X(vkBeginCommandBuffer) X(vkEndCommandBuffer) X(vkQueueSubmit) X(vkQueueWaitIdle)
#define DECLARE(n) static PFN_##n n;
APIS(DECLARE)
#define CHECK(e) do{VkResult r=(e);if(r){fprintf(stderr,"%s=%d line %d\n",#e,r,__LINE__);return 2;}}while(0)
int main(int argc,char **argv) {
    if(argc!=3)return 2;
    const int repair=!strcmp(argv[2],"fixed");
    HMODULE loader=LoadLibraryW(L"vulkan-1.dll"),helper=LoadLibraryA(argv[1]);if(!loader||!helper)return 2;
#define LOAD(n) n=(PFN_##n)GetProcAddress(loader,#n);if(!n)return 2;
    APIS(LOAD)
    typedef void (*Repair)(VkCommandBuffer,const VkDependencyInfoKHR *,PFN_vkCmdPipelineBarrier2KHR);
    Repair submit=(Repair)GetProcAddress(helper,"SubmitImageBarriers");if(!submit)return 2;
    VkApplicationInfo app={.sType=VK_STRUCTURE_TYPE_APPLICATION_INFO,.pApplicationName="Depth store dependency validation",.apiVersion=((1u<<22)|(3u<<12))};
    const char *layers[]={"VK_LAYER_KHRONOS_validation"};const char *extensions[]={"VK_EXT_debug_utils"};
    VkValidationFeatureEnableEXT sync=VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT;
    VkDebugUtilsMessengerCreateInfoEXT debug={.sType=VK_STRUCTURE_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT,.messageSeverity=VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT,.messageType=VK_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT,.pfnUserCallback=message};
    VkValidationFeaturesEXT features={.sType=VK_STRUCTURE_TYPE_VALIDATION_FEATURES_EXT,.pNext=&debug,.enabledValidationFeatureCount=1,.pEnabledValidationFeatures=&sync};
    VkInstanceCreateInfo ici={.sType=VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO,.pNext=&features,.pApplicationInfo=&app,.enabledLayerCount=1,.ppEnabledLayerNames=layers,.enabledExtensionCount=1,.ppEnabledExtensionNames=extensions};
    VkInstance instance;CHECK(vkCreateInstance(&ici,NULL,&instance));
    PFN_vkGetInstanceProcAddr gip=(PFN_vkGetInstanceProcAddr)GetProcAddress(loader,"vkGetInstanceProcAddr");
    PFN_vkCreateDebugUtilsMessengerEXT create_debug=(PFN_vkCreateDebugUtilsMessengerEXT)gip(instance,"vkCreateDebugUtilsMessengerEXT");
    PFN_vkDestroyDebugUtilsMessengerEXT destroy_debug=(PFN_vkDestroyDebugUtilsMessengerEXT)gip(instance,"vkDestroyDebugUtilsMessengerEXT");
    VkDebugUtilsMessengerEXT messenger;CHECK(create_debug(instance,&debug,NULL,&messenger));
    uint32_t count=8;VkPhysicalDevice physical[8];CHECK(vkEnumeratePhysicalDevices(instance,&count,physical));if(!count)return 2;
    VkPhysicalDeviceProperties properties;vkGetPhysicalDeviceProperties(physical[0],&properties);
    VkQueueFamilyProperties queues[32];count=32;vkGetPhysicalDeviceQueueFamilyProperties(physical[0],&count,queues);
    uint32_t family=0;while(family<count&&!(queues[family].queueFlags&VK_QUEUE_GRAPHICS_BIT))++family;if(family==count)return 2;
    float priority=1;VkDeviceQueueCreateInfo qci={.sType=VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO,.queueFamilyIndex=family,.queueCount=1,.pQueuePriorities=&priority};
    VkPhysicalDeviceSynchronization2FeaturesKHR synchronization={.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES_KHR,.synchronization2=1};
    VkPhysicalDeviceDynamicRenderingFeaturesKHR dynamic={.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DYNAMIC_RENDERING_FEATURES_KHR,.pNext=&synchronization,.dynamicRendering=1};
    VkDeviceCreateInfo dci={.sType=VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO,.pNext=&dynamic,.queueCreateInfoCount=1,.pQueueCreateInfos=&qci};
    VkDevice device;CHECK(vkCreateDevice(physical[0],&dci,NULL,&device));VkQueue queue;vkGetDeviceQueue(device,family,0,&queue);
    PFN_vkCmdPipelineBarrier2KHR barrier=(PFN_vkCmdPipelineBarrier2KHR)vkGetDeviceProcAddr(device,"vkCmdPipelineBarrier2");
    PFN_vkCmdBeginRenderingKHR begin=(PFN_vkCmdBeginRenderingKHR)vkGetDeviceProcAddr(device,"vkCmdBeginRendering");
    PFN_vkCmdEndRenderingKHR end=(PFN_vkCmdEndRenderingKHR)vkGetDeviceProcAddr(device,"vkCmdEndRendering");if(!barrier||!begin||!end)return 2;
    VkImageCreateInfo image_info={.sType=VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO,.imageType=VK_IMAGE_TYPE_2D,.format=VK_FORMAT_D32_SFLOAT,.extent={16,16,1},.mipLevels=1,.arrayLayers=1,.samples=VK_SAMPLE_COUNT_1_BIT,.tiling=VK_IMAGE_TILING_OPTIMAL,.usage=VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT|VK_IMAGE_USAGE_SAMPLED_BIT};
    VkImage image;CHECK(vkCreateImage(device,&image_info,NULL,&image));VkMemoryRequirements requirements;vkGetImageMemoryRequirements(device,image,&requirements);
    VkPhysicalDeviceMemoryProperties memories;vkGetPhysicalDeviceMemoryProperties(physical[0],&memories);uint32_t memory_type=0;while(memory_type<memories.memoryTypeCount&&!(requirements.memoryTypeBits&(1u<<memory_type)))++memory_type;
    VkMemoryAllocateInfo allocation={.sType=VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,.allocationSize=requirements.size,.memoryTypeIndex=memory_type};VkDeviceMemory memory;CHECK(vkAllocateMemory(device,&allocation,NULL,&memory));CHECK(vkBindImageMemory(device,image,memory,0));
    VkImageViewCreateInfo view_info={.sType=VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO,.image=image,.viewType=VK_IMAGE_VIEW_TYPE_2D,.format=VK_FORMAT_D32_SFLOAT,.subresourceRange={VK_IMAGE_ASPECT_DEPTH_BIT,0,1,0,1}};VkImageView view;CHECK(vkCreateImageView(device,&view_info,NULL,&view));
    VkCommandPoolCreateInfo pci={.sType=VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,.queueFamilyIndex=family};VkCommandPool pool;CHECK(vkCreateCommandPool(device,&pci,NULL,&pool));
    VkCommandBufferAllocateInfo cai={.sType=VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,.commandPool=pool,.level=VK_COMMAND_BUFFER_LEVEL_PRIMARY,.commandBufferCount=1};VkCommandBuffer cmd;CHECK(vkAllocateCommandBuffers(device,&cai,&cmd));
    VkCommandBufferBeginInfo bi={.sType=VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};CHECK(vkBeginCommandBuffer(cmd,&bi));
    VkImageMemoryBarrier2KHR transition={.sType=VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2_KHR,.dstStageMask=VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT_KHR|VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT_KHR,.dstAccessMask=VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT_KHR,.oldLayout=VK_IMAGE_LAYOUT_UNDEFINED,.newLayout=VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL,.srcQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED,.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED,.image=image,.subresourceRange={VK_IMAGE_ASPECT_DEPTH_BIT,0,1,0,1}};
    VkDependencyInfoKHR dependency={.sType=VK_STRUCTURE_TYPE_DEPENDENCY_INFO_KHR,.imageMemoryBarrierCount=1,.pImageMemoryBarriers=&transition};barrier(cmd,&dependency);
    VkRenderingAttachmentInfoKHR attachment={.sType=VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO_KHR,.imageView=view,.imageLayout=VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL,.loadOp=VK_ATTACHMENT_LOAD_OP_CLEAR,.storeOp=VK_ATTACHMENT_STORE_OP_STORE,.clearValue={.depthStencil={0.5f,0}}};
    VkRenderingInfoKHR rendering={.sType=VK_STRUCTURE_TYPE_RENDERING_INFO_KHR,.renderArea={{0,0},{16,16}},.layerCount=1,.pDepthAttachment=&attachment};begin(cmd,&rendering);end(cmd);
    transition.srcStageMask=VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT_KHR;transition.srcAccessMask=VK_ACCESS_2_SHADER_READ_BIT_KHR;transition.dstStageMask=VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT_KHR;transition.dstAccessMask=VK_ACCESS_2_SHADER_READ_BIT_KHR;transition.oldLayout=VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;transition.newLayout=VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL;
    if(repair)submit(cmd,&dependency,barrier);else barrier(cmd,&dependency);
    CHECK(vkEndCommandBuffer(cmd));VkSubmitInfo si={.sType=VK_STRUCTURE_TYPE_SUBMIT_INFO,.commandBufferCount=1,.pCommandBuffers=&cmd};CHECK(vkQueueSubmit(queue,1,&si,VK_NULL_HANDLE));CHECK(vkQueueWaitIdle(queue));
    vkDestroyCommandPool(device,pool,NULL);vkDestroyImageView(device,view,NULL);vkDestroyImage(device,image,NULL);vkFreeMemory(device,memory,NULL);vkDestroyDevice(device,NULL);destroy_debug(instance,messenger,NULL);vkDestroyInstance(instance,NULL);
    printf("GPU=%s mode=%s validation_errors=%u\n",properties.deviceName,argv[2],errors);
    return repair ? (errors?3:0) : (errors?0:4);
}
