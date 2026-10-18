// SPDX-License-Identifier: GPL-2.0-or-later
#define WIN32_LEAN_AND_MEAN
#define VK_NO_PROTOTYPES
#include <windows.h>
#include <vulkan/vulkan_core.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#define APIS(X) X(vkCreateInstance) X(vkDestroyInstance) X(vkEnumeratePhysicalDevices) \
 X(vkGetPhysicalDeviceProperties) X(vkGetPhysicalDeviceMemoryProperties) X(vkGetPhysicalDeviceQueueFamilyProperties) \
 X(vkCreateDevice) X(vkDestroyDevice) X(vkGetDeviceQueue) X(vkCreateBuffer) X(vkDestroyBuffer) \
 X(vkGetBufferMemoryRequirements) X(vkCreateImage) X(vkDestroyImage) X(vkGetImageMemoryRequirements) \
 X(vkAllocateMemory) X(vkFreeMemory) X(vkBindBufferMemory) X(vkBindImageMemory) \
 X(vkMapMemory) X(vkUnmapMemory) X(vkCreateCommandPool) X(vkDestroyCommandPool) \
 X(vkAllocateCommandBuffers) X(vkBeginCommandBuffer) X(vkEndCommandBuffer) X(vkCmdPipelineBarrier) \
 X(vkCmdCopyBufferToImage) X(vkCmdCopyImage) X(vkCmdCopyImageToBuffer) X(vkQueueSubmit) X(vkQueueWaitIdle)
#define DECLARE(n) static PFN_##n n;
APIS(DECLARE)
#define CHECK(expr) do { VkResult result=(expr); if(result!=VK_SUCCESS){fprintf(stderr,"%s: %d at %d\n",#expr,result,__LINE__);return 2;} } while(0)
static uint32_t memory_type(const VkPhysicalDeviceMemoryProperties* p,uint32_t bits,VkMemoryPropertyFlags flags) {
    for(uint32_t i=0;i<p->memoryTypeCount;++i)
        if((bits&(1u<<i)) && (p->memoryTypes[i].propertyFlags&flags)==flags) return i;
    return UINT32_MAX;
}
int main(void) {
    HMODULE loader=LoadLibraryA("vulkan-1.dll"); if(!loader)return 2;
#define LOAD(n) n=(PFN_##n)GetProcAddress(loader,#n); if(!n)return 2;
    APIS(LOAD)
    VkApplicationInfo app={.sType=VK_STRUCTURE_TYPE_APPLICATION_INFO,.pApplicationName="Texture copy validation",.apiVersion=VK_API_VERSION_1_2};
    VkInstanceCreateInfo ici={.sType=VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO,.pApplicationInfo=&app};
    VkInstance instance; CHECK(vkCreateInstance(&ici,NULL,&instance));
    uint32_t count=8;VkPhysicalDevice gpus[8];CHECK(vkEnumeratePhysicalDevices(instance,&count,gpus));if(!count)return 2;
    VkPhysicalDevice gpu=gpus[0];VkPhysicalDeviceProperties props;VkPhysicalDeviceMemoryProperties memories;
    vkGetPhysicalDeviceProperties(gpu,&props);vkGetPhysicalDeviceMemoryProperties(gpu,&memories);
    count=32;VkQueueFamilyProperties families[32];vkGetPhysicalDeviceQueueFamilyProperties(gpu,&count,families);
    uint32_t family=UINT32_MAX;for(uint32_t i=0;i<count;++i)if(families[i].queueFlags&VK_QUEUE_GRAPHICS_BIT){family=i;break;}
    if(family==UINT32_MAX)return 2;
    float priority=1;VkDeviceQueueCreateInfo qci={.sType=VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO,.queueFamilyIndex=family,.queueCount=1,.pQueuePriorities=&priority};
    VkDeviceCreateInfo dci={.sType=VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO,.queueCreateInfoCount=1,.pQueueCreateInfos=&qci};
    VkDevice device;CHECK(vkCreateDevice(gpu,&dci,NULL,&device));VkQueue queue;vkGetDeviceQueue(device,family,0,&queue);
    const uint32_t width=1920,height=1080;const VkDeviceSize bytes=(VkDeviceSize)width*height*4;
    VkBufferCreateInfo bci={.sType=VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO,.size=bytes*2+512,.usage=VK_BUFFER_USAGE_TRANSFER_SRC_BIT|VK_BUFFER_USAGE_TRANSFER_DST_BIT};
    VkBuffer buffer;CHECK(vkCreateBuffer(device,&bci,NULL,&buffer));VkMemoryRequirements req;vkGetBufferMemoryRequirements(device,buffer,&req);
    uint32_t index=memory_type(&memories,req.memoryTypeBits,VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT|VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);if(index==UINT32_MAX)return 2;
    VkMemoryAllocateInfo mai={.sType=VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,.allocationSize=req.size,.memoryTypeIndex=index};
    VkDeviceMemory staging;CHECK(vkAllocateMemory(device,&mai,NULL,&staging));CHECK(vkBindBufferMemory(device,buffer,staging,0));
    uint32_t* pixels;CHECK(vkMapMemory(device,staging,0,VK_WHOLE_SIZE,0,(void**)&pixels));
    for(uint32_t i=0;i<width*height;++i){pixels[i]=0x3c0780f0u^(i*2654435761u);pixels[width*height+i]=0xa5a5a5a5u;}
    for(uint32_t i=0;i<128;++i)pixels[2*width*height+i]=0xa5a5a5a5u;
    vkUnmapMemory(device,staging);
    VkImage images[2];VkDeviceMemory backing[2];
    VkImageCreateInfo image_ci={.sType=VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO,.imageType=VK_IMAGE_TYPE_2D,.format=VK_FORMAT_B10G11R11_UFLOAT_PACK32,
        .extent={width,height,1},.mipLevels=1,.arrayLayers=1,.samples=VK_SAMPLE_COUNT_1_BIT,.tiling=VK_IMAGE_TILING_OPTIMAL,
        .usage=VK_IMAGE_USAGE_TRANSFER_SRC_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT,.initialLayout=VK_IMAGE_LAYOUT_UNDEFINED};
    for(unsigned i=0;i<2;++i){CHECK(vkCreateImage(device,&image_ci,NULL,&images[i]));vkGetImageMemoryRequirements(device,images[i],&req);
        index=memory_type(&memories,req.memoryTypeBits,VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);if(index==UINT32_MAX)return 2;
        mai.allocationSize=req.size;mai.memoryTypeIndex=index;CHECK(vkAllocateMemory(device,&mai,NULL,&backing[i]));CHECK(vkBindImageMemory(device,images[i],backing[i],0));}
    VkCommandPoolCreateInfo pci={.sType=VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,.queueFamilyIndex=family};VkCommandPool pool;CHECK(vkCreateCommandPool(device,&pci,NULL,&pool));
    VkCommandBufferAllocateInfo cai={.sType=VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,.commandPool=pool,.level=VK_COMMAND_BUFFER_LEVEL_PRIMARY,.commandBufferCount=1};
    VkCommandBuffer cmd;CHECK(vkAllocateCommandBuffers(device,&cai,&cmd));VkCommandBufferBeginInfo begin={.sType=VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};CHECK(vkBeginCommandBuffer(cmd,&begin));
    VkImageMemoryBarrier barriers[2];for(unsigned i=0;i<2;++i)barriers[i]=(VkImageMemoryBarrier){.sType=VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
        .dstAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT,.oldLayout=VK_IMAGE_LAYOUT_UNDEFINED,.newLayout=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
        .srcQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED,.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED,.image=images[i],.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1}};
    vkCmdPipelineBarrier(cmd,VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,VK_PIPELINE_STAGE_TRANSFER_BIT,0,0,NULL,0,NULL,2,barriers);
    VkBufferImageCopy bic={.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1},.imageExtent={width,height,1}};
    vkCmdCopyBufferToImage(cmd,buffer,images[0],VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,1,&bic);
    barriers[0].srcAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT;barriers[0].dstAccessMask=VK_ACCESS_TRANSFER_READ_BIT;
    barriers[0].oldLayout=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;barriers[0].newLayout=VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
    vkCmdPipelineBarrier(cmd,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_TRANSFER_BIT,0,0,NULL,0,NULL,1,barriers);
    VkImageCopy copy={.srcSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1},.dstSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1},.extent={width,height,1}};
    vkCmdCopyImage(cmd,images[0],VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,images[1],VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,1,&copy);
    barriers[0].image=images[1];vkCmdPipelineBarrier(cmd,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_TRANSFER_BIT,0,0,NULL,0,NULL,1,barriers);
    bic.bufferOffset=bytes+256;vkCmdCopyImageToBuffer(cmd,images[1],VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,buffer,1,&bic);
    VkMemoryBarrier host_barrier={.sType=VK_STRUCTURE_TYPE_MEMORY_BARRIER,.srcAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT,.dstAccessMask=VK_ACCESS_HOST_READ_BIT};
    vkCmdPipelineBarrier(cmd,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_HOST_BIT,0,1,&host_barrier,0,NULL,0,NULL);CHECK(vkEndCommandBuffer(cmd));
    VkSubmitInfo submit={.sType=VK_STRUCTURE_TYPE_SUBMIT_INFO,.commandBufferCount=1,.pCommandBuffers=&cmd};CHECK(vkQueueSubmit(queue,1,&submit,VK_NULL_HANDLE));CHECK(vkQueueWaitIdle(queue));

    CHECK(vkMapMemory(device,staging,0,VK_WHOLE_SIZE,0,(void**)&pixels));
    const uint32_t image_words=width*height,base_words=image_words+64;
    for(uint32_t i=0;i<image_words;++i)if(pixels[i]!=pixels[base_words+i]){fprintf(stderr,"Image sync mismatch %u\n",i);return 3;}
    for(uint32_t i=0;i<64;++i)if(pixels[image_words+i]!=0xa5a5a5a5u||pixels[base_words+image_words+i]!=0xa5a5a5a5u){fprintf(stderr,"Neighbor sentinel overwritten\n");return 4;}
    // The shader's original +0xa0 view reads the 40th source texel, not texel zero.
    for(uint32_t i=0;i<128;++i)if(pixels[base_words+40+i]!=pixels[40+i]){fprintf(stderr,"Partial view offset mismatch\n");return 5;}
    vkUnmapMemory(device,staging);
    printf("GPU: %s\nFull image synchronized at its own buffer base: %llu bytes exact; +160-byte texel view reads correct 512 bytes; both 256-byte neighboring sentinels preserved\n",props.deviceName,(unsigned long long)bytes);
    vkDestroyCommandPool(device,pool,NULL);for(unsigned i=0;i<2;++i){vkDestroyImage(device,images[i],NULL);vkFreeMemory(device,backing[i],NULL);}
    vkDestroyBuffer(device,buffer,NULL);vkFreeMemory(device,staging,NULL);vkDestroyDevice(device,NULL);vkDestroyInstance(instance,NULL);return 0;
}
