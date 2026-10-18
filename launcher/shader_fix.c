// SPDX-License-Identifier: GPL-2.0-or-later
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>

typedef struct { const uint32_t *data; size_t size; } ShaderSpan;
static LONG normalized_count;
static LONG observed_count;

static int diagnostics_enabled(void) {
    char value[8];
    return GetEnvironmentVariableA("NHL_SHADER_DIAGNOSTICS", value, sizeof(value)) == 1 && value[0] == '1';
}

static int readable(const void *ptr, size_t size) {
    uintptr_t p = (uintptr_t)ptr, end = p + size;
    if (end < p) return 0;
    while (p < end) {
        MEMORY_BASIC_INFORMATION m;
        if (!VirtualQuery((const void *)p, &m, sizeof(m)) || m.State != MEM_COMMIT ||
            (m.Protect & (PAGE_NOACCESS | PAGE_GUARD))) return 0;
        uintptr_t next = (uintptr_t)m.BaseAddress + m.RegionSize;
        if (next <= p) return 0;
        p = next;
    }
    return 1;
}

static void log_line(const char *line) {
    HANDLE f = CreateFileA("shader-fix.log", FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                           NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f != INVALID_HANDLE_VALUE) {
        DWORD done;
        WriteFile(f, line, (DWORD)strlen(line), &done, NULL);
        CloseHandle(f);
    }
}

static void dump_code(const char *name, const void *data, size_t bytes) {
    HANDLE f = CreateFileA(name, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS,
                           FILE_ATTRIBUTE_NORMAL, NULL);
    if (f != INVALID_HANDLE_VALUE) {
        DWORD done;
        WriteFile(f, data, (DWORD)bytes, &done, NULL);
        CloseHandle(f);
    }
}

static size_t instruction_words(uint32_t w) {
    uint32_t prefix = w & 0xff800000u;
    if (prefix == 0xbe800000u) return 1 + ((w & 255) == 255);
    if (prefix == 0xbf000000u) return 1 + ((w & 255) == 255 || ((w >> 8) & 255) == 255);
    if (prefix == 0xbf800000u) return 1;
    prefix = w & 0xfe000000u;
    if (prefix == 0x7e000000u || prefix == 0x7c000000u) {
        uint32_t src = w & 511;
        return 1 + (src == 255 || src == 249 || src == 250);
    }
    prefix = w & 0xfc000000u;
    if (prefix == 0xc8000000u) return 1;
    if (prefix == 0xd0000000u || prefix == 0xcc000000u || prefix == 0xd8000000u ||
        prefix == 0xe0000000u || prefix == 0xe8000000u || prefix == 0xf0000000u ||
        prefix == 0xf8000000u) return 2;
    if ((w & 0xf8000000u) == 0xc0000000u) return 1;
    if ((w & 0xf0000000u) == 0xb0000000u) return 1 + (((w >> 23) & 31) == 21);
    if ((w & 0xc0000000u) == 0x80000000u)
        return 1 + ((w & 255) == 255 || ((w >> 8) & 255) == 255);
    if ((w & 0x80000000u) == 0) {
        uint32_t src = w & 511, op = (w >> 25) & 63;
        return 1 + (src == 255 || src == 249 || src == 250 || op == 32 || op == 33);
    }
    return 0;
}

static int sop1(uint32_t w, unsigned op) {
    return (w & 0xff80ff00u) == (0xbe800000u | (op << 8));
}
static int sop2(uint32_t w, unsigned op) {
    return (w & 0xff800000u) == (0x80000000u | (op << 23));
}
static unsigned dst(uint32_t w) { return (w >> 16) & 127; }
static unsigned src0(uint32_t w) { return w & 255; }
static unsigned src1(uint32_t w) { return (w >> 8) & 255; }
static int pair_sources(uint32_t w, unsigned a, unsigned b) {
    return (src0(w) == a && src1(w) == b) || (src0(w) == b && src1(w) == a);
}
static int emit_branch(uint32_t *out, size_t at, size_t target, unsigned op) {
    int64_t delta = (int64_t)target - (int64_t)at - 1;
    if (delta < -32768 || delta > 32767) return 0;
    out[at] = 0xbf800000u | (op << 16) | (uint16_t)(int16_t)delta;
    return 1;
}

static int prefix_safe(const uint32_t *code, size_t at, unsigned offset_reg) {
    uint32_t w = code[at];
    if ((w & 0xffff0000u) == 0xbf8c0000u) return 1;
    if (!(w & 0x80000000u)) {
        unsigned source = w & 511;
        if (source < 128 || source == 249 || source == 250) return 0;
        if ((w & 0xfe000000u) == 0x7e000000u) return ((w >> 9) & 255) != 2;
        if ((w & 0xfe000000u) == 0x7c000000u) return 1;
        unsigned op = (w >> 25) & 63;
        return op != 1 && op != 2;
    }
    if ((w & 0xfc000000u) == 0xf0000000u) {
        unsigned resource = ((code[at + 1] >> 16) & 31) * 4;
        unsigned sampler = ((code[at + 1] >> 21) & 31) * 4;
        return !(offset_reg <= resource + 7 && offset_reg + 1 >= resource) &&
               !(offset_reg <= sampler + 3 && offset_reg + 1 >= sampler);
    }
    if ((w & 0xfc000000u) == 0xc8000000u) return 1;
    return 0;
}

static int lower_one(ShaderSpan *span, size_t setpc, const unsigned char *boundary,
                     const uint32_t *original_code, size_t original_count) {
    const uint32_t *code = span->data;
    size_t count = span->size;
    if (setpc < 10 || !boundary[setpc - 3] || !boundary[setpc - 2] || !boundary[setpc - 1]) return 0;
    unsigned pc_reg = src0(code[setpc]);
    uint32_t getpc = code[setpc - 3], add = code[setpc - 2], carry = code[setpc - 1];
    if (pc_reg > 102 || !sop1(getpc, 31) || dst(getpc) != pc_reg ||
        !sop2(add, 0) || dst(add) != pc_reg || src0(add) != pc_reg || src1(add) > 102 ||
        !sop2(carry, 4) || dst(carry) != pc_reg + 1 ||
        !pair_sources(carry, pc_reg + 1, src1(add) + 1)) return 0;
    unsigned offset_reg = src1(add);

    for (size_t load = setpc - 4; load > 5 && setpc - load < 32; --load) {
        if (!boundary[load]) continue;
        uint32_t w = code[load];
        if ((w & 0xffc00100u) != 0xc0400000u || ((w >> 15) & 127) != offset_reg) continue;
        unsigned index_reg = w & 255, table_reg = ((w >> 9) & 63) * 2;
        if (index_reg > 103 || table_reg > 102 || load < 6) continue;
        if (index_reg == offset_reg || index_reg == offset_reg + 1 ||
            index_reg == pc_reg || index_reg == pc_reg + 1 ||
            index_reg == table_reg || index_reg == table_reg + 1 ||
            offset_reg == pc_reg || offset_reg == pc_reg + 1 || offset_reg + 1 == pc_reg) continue;
        size_t table_get = load - 4;
        if (!boundary[table_get] || !boundary[table_get + 1] || !boundary[load - 1]) continue;
        uint32_t table_add = code[table_get + 1], table_carry = code[load - 1];
        size_t shift_pos = table_get - 1;
        uint32_t bias = 0;
        if (!boundary[shift_pos] && table_get >= 4 && boundary[table_get - 2] &&
            sop2(code[table_get - 2], 0) && dst(code[table_get - 2]) == index_reg &&
            pair_sources(code[table_get - 2], index_reg, 255)) {
            bias = code[table_get - 1];
            if (bias > 65536 || (bias & 7)) continue;
            shift_pos = table_get - 3;
        }
        if (!boundary[shift_pos] || !boundary[shift_pos - 1]) continue;
        uint32_t shift = code[shift_pos], clamp = code[shift_pos - 1];
        if (!sop1(code[table_get], 31) || dst(code[table_get]) != table_reg ||
            !sop2(table_add, 0) || dst(table_add) != table_reg ||
            !pair_sources(table_add, table_reg, 255) ||
            !sop2(table_carry, 4) || dst(table_carry) != table_reg + 1 ||
            !pair_sources(table_carry, table_reg + 1, 128) ||
            !sop2(shift, 30) || dst(shift) != index_reg || src0(shift) != index_reg || src1(shift) != 131 ||
            !sop2(clamp, 7) || dst(clamp) != index_reg) continue;
        unsigned bound_code = src0(clamp);
        if (bound_code < 128 || bound_code > 191) bound_code = src1(clamp);
        if (bound_code < 128 || bound_code > 191) continue;
        unsigned entries = bound_code - 128 + 1;
        int clean = 1;
        for (size_t j = load + 1; j < setpc - 3;) {
            uint32_t inst = code[j];
            size_t length = instruction_words(inst);
            if (!length || !boundary[j] || j + length > setpc - 3) { clean = 0; break; }
            if (!prefix_safe(code, j, offset_reg)) { clean = 0; break; }
            j += length;
        }
        if (!clean) continue;
        int64_t table_byte = (int64_t)(table_get + 1) * 4 + code[table_get + 2] + bias;
        if (table_byte < 0 || table_byte > (int64_t)original_count * 4 + 65536 || (table_byte & 3)) continue;
        const unsigned char *table = (const unsigned char *)original_code + table_byte;
        if (!readable(table, (size_t)entries * 8)) continue;
        size_t targets[64];
        uint64_t offsets[64];
        for (unsigned k = 0; k < entries; ++k) {
            int64_t relative;
            memcpy(&relative, table + k * 8, sizeof(relative));
            offsets[k] = (uint64_t)relative;
            int64_t target = (int64_t)(setpc - 2) * 4 + relative;
            if (relative < -1048576 || relative > 1048576 || target < 0 ||
                target >= (int64_t)original_count * 4 || (target & 3) || !boundary[target / 4]) { clean = 0; break; }
            targets[k] = (size_t)target / 4;
        }
        if (!clean) continue;
        size_t added = entries * 10 - 2;
        if (count + added >= 32700) continue;
        uint32_t *out = (uint32_t *)malloc((count + added) * 4);
        if (!out) return 0;
        memcpy(out, code, count * 4);
        out[load] = 0xbf800000u;
        out[setpc - 2] = 0xbf800000u;
        out[setpc - 1] = 0xbf800000u;
        if (!emit_branch(out, setpc, count, 2)) clean = 0;
        size_t stubs = count + (entries - 1) * 3 + 1;
        for (unsigned k = 0; k + 1 < entries; ++k) {
            size_t pos = count + k * 3;
            out[pos] = 0xbf060000u | (255 << 8) | index_reg;
            out[pos + 1] = bias + k * 8;
            if (!emit_branch(out, pos + 2, stubs + k * 7, 5)) clean = 0;
        }
        if (!emit_branch(out, stubs - 1, stubs + (entries - 1) * 7, 2)) clean = 0;
        for (unsigned k = 0; k < entries; ++k) {
            size_t pos = stubs + k * 7;
            out[pos] = 0xbe8003ffu | (offset_reg << 16);
            out[pos + 1] = (uint32_t)offsets[k];
            out[pos + 2] = 0xbe8003ffu | ((offset_reg + 1) << 16);
            out[pos + 3] = (uint32_t)(offsets[k] >> 32);
            out[pos + 4] = add;
            out[pos + 5] = carry;
            if (!emit_branch(out, pos + 6, targets[k], 2)) clean = 0;
        }
        if (!clean) { free(out); continue; }
        LONG serial = InterlockedIncrement(&normalized_count);
        char message[512], filename[96];
        snprintf(message, sizeof(message), "Lowered bounded jump table: pc=0x%zx index=s%u entries=%u table=0x%llx words=%zu->%zu\r\n",
                 setpc * 4, index_reg, entries, (unsigned long long)table_byte, count, count + added);
        log_line(message);
        if (diagnostics_enabled()) {
            snprintf(filename, sizeof(filename), "normalized-%ld.bin", (long)serial);
            dump_code(filename, out, (count + added) * 4);
            snprintf(filename, sizeof(filename), "original-with-table-%ld.bin", (long)serial);
            size_t dump_bytes = original_count * 4;
            if ((size_t)table_byte + entries * 8 > dump_bytes) dump_bytes = (size_t)table_byte + entries * 8;
            if (readable(original_code, dump_bytes)) dump_code(filename, original_code, dump_bytes);
        }
        span->data = out;
        span->size = count + added;
        return 1;
    }
    return 0;
}

__declspec(dllexport) int NormalizeShaderCode(ShaderSpan *span) {
    if (!span || !readable(span, sizeof(*span)) || !span->data || span->size < 16 || span->size > 1048576 ||
        !readable(span->data, span->size * 4)) return 0;
    const uint32_t *original_code = span->data;
    size_t original_count = span->size;
    int diagnostic = diagnostics_enabled();
    for (size_t scan = 0; diagnostic && scan < original_count;) {
        size_t step = instruction_words(original_code[scan]);
        if (!step || scan + step > original_count) break;
        if (sop1(original_code[scan], 32)) {
            LONG serial = InterlockedIncrement(&observed_count);
            if (serial <= 256) {
                size_t bytes = original_count * 4;
                if (readable(original_code, bytes + 65536)) bytes += 65536;
                char filename[96], message[256];
                snprintf(filename, sizeof(filename), "observed-%ld.bin", (long)serial);
                dump_code(filename, original_code, bytes);
                snprintf(message, sizeof(message), "Observed GCN: pc=0x%zx words=%zu file=%s bytes=%zu\r\n", scan * 4, original_count, filename, bytes);
                log_line(message);
            }
            break;
        }
        scan += step;
    }
    int total = 0;
    for (unsigned pass = 0; pass < 32; ++pass) {
        unsigned char *boundary = (unsigned char *)calloc(span->size, 1);
        if (!boundary) return total;
        size_t cursor = 0;
        while (cursor < span->size) {
            size_t step = instruction_words(span->data[cursor]);
            if (!step || cursor + step > span->size) { free(boundary); return total; }
            boundary[cursor] = 1;
            cursor += step;
        }
        const uint32_t *previous = span->data;
        int changed = 0;
        for (size_t i = 0; i < original_count; ++i) {
            if (boundary[i] && sop1(span->data[i], 32) &&
                lower_one(span, i, boundary, original_code, original_count)) { changed = 1; break; }
        }
        free(boundary);
        if (!changed) return total;
        if (previous != original_code) free((void *)previous);
        ++total;
    }
    return total;
}
