# SPDX-License-Identifier: GPL-2.0-or-later
"""Compile the original tiling shader's pure address functions as a CPU probe.

Usage: python make_base_address_probe.py path/to/generated_probe.c
The constants are the Thin2DThin, 32-bit, single-sample non-Neo specialization
from TileManager::GetTilingPipeline / AmdGpu's tiling tables.
"""
import sys
from pathlib import Path

repo = Path(__file__).resolve().parents[2]
shader = (repo/'emulator-source/src/video_core/host_shaders/tiling.comp').read_text()
definitions = shader[shader.index('#define BYTES_PER_PIXEL'):shader.index('struct MipInfo')]
functions = shader[shader.index('uint32_t ComputePixelIndexWithinMicroTile'):shader.index('uint GetMipLevel')]
spec = {'NUM_SAMPLES':1,'MICRO_TILE_MODE':1,'MICRO_TILE_THICKNESS':1,'ARRAY_MODE':4,
        'PIPE_CONFIG':12,'BANK_WIDTH':1,'BANK_HEIGHT':1,'NUM_BANKS':16,'NUM_BANK_BITS':4,
        'TILE_SPLIT_BYTES':512,'MACRO_TILE_ASPECT':2,'BITS_PER_PIXEL':32,'IS_MACRO_TILED':1}
prefix = '''// SPDX-FileCopyrightText: Copyright 2025 shadPS4 Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
typedef uint32_t uint;
#define max(a,b) ((a)>(b)?(a):(b))
#define bitfieldExtract(v,b,n) (((v)>>(b))&((1u<<(n))-1))
static struct { uint bank_swizzle; } info = {0};
'''
prefix += ''.join(f'#define {key} {value}\n' for key,value in spec.items())
main = r'''
int main(void) {
    unsigned char* seen=calloc(0x880000/4,1);if(!seen)return 2;
    uint max_address=0;
    for(uint y=0;y<1080;++y)for(uint x=0;x<1920;++x){
        uint old=ComputeSurfaceAddrFromCoordMacroTiled(x,y,0,2048,1088,0);
        uint requested=ComputeSurfaceAddrFromCoordMacroTiled(x,y,0,2048,2048,0);
        if(old!=requested||old+4>0x880000||old%4||seen[old/4]){
            fprintf(stderr,"Address mismatch or overlap at %u,%u: %u vs %u\n",x,y,old,requested);return 3;}
        seen[old/4]=1;if(old>max_address)max_address=old;
    }
    free(seen);
    printf("Original shader address functions: all 2073600 visible texels have identical, unique addresses with padded heights 1088 and 2048; highest byte %u fits existing base mip\n",max_address+3);
    return 0;
}
'''
Path(sys.argv[1]).write_text(prefix+definitions+functions+main)
