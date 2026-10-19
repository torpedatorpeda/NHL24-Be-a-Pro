// SPDX-License-Identifier: GPL-2.0-or-later
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>

typedef struct { uint32_t type, padding; uint64_t value; } Value;
typedef void *(*ReadFn)(void *, Value *, const void *);
typedef void (*WriteFn)(void *, const void *, const Value *);
typedef void *(*UnaryFn)(void *, Value *, const Value *);
typedef void *(*BinaryFn)(void *, Value *, const Value *, const Value *);
typedef void *(*CompareFn)(void *, Value *, const Value *, const Value *, unsigned char);
typedef void *(*ConvertFn)(void *, Value *, size_t, const Value *);
typedef void (*MissingFn)(void *, const void *);

__declspec(dllexport) void *ReadHalfSource(void *translator, Value *result,
                                         const unsigned char *operand, ReadFn original) {
    uint32_t selector = *(const uint32_t *)(operand + 28);
    if (selector == 4 || selector == 5 || selector == 6) {
        unsigned char selected[48];
        memcpy(selected, operand, sizeof(selected));
        selected[20] = selector == 5;
        return original(translator, result, selected);
    }
    return original(translator, result, operand);
}

typedef void (*SetRegisterFn)(void *, uint32_t, const Value *);
__declspec(dllexport) void WriteHalfDestination(void *translator,
        const unsigned char *operand, const Value *value, WriteFn original) {
    uint32_t selector = *(const uint32_t *)(operand + 28);
    uint32_t unused = *(const uint32_t *)(operand + 24);
    uint32_t field = *(const uint32_t *)operand;
    if ((selector == 4 || selector == 5 || selector == 6) &&
        (unused == 0 || unused == 2) && (field == 0 || field == 256)) {
        unsigned char selected[48];
        memcpy(selected, operand, sizeof(selected));
        selected[20] = selector == 5;
        if (unused == 0) {
            unsigned char *base = (unsigned char *)original - 0xcf4750;
            Value zero = {512, 0, 0};
            SetRegisterFn set = (SetRegisterFn)(base + (field == 0 ? 0xd5ced0 : 0xd5d050));
            set(translator, *(const uint32_t *)(operand + 44), &zero);
        }
        original(translator, selected, value);
        return;
    }
    original(translator, operand, value);
}

typedef int (__attribute__((sysv_abi)) *NeoFn)(void);
typedef void (*UpdateSizeFn)(void *);

/* VideoOut has no image descriptor from which to recover the Neo tile flag.
 * Its tiled surfaces must use the same layout as the game's color target. */
__declspec(dllexport) void UpdateVideoOutSize(unsigned char *info, UpdateSizeFn update,
                                            NeoFn is_neo) {
    uint32_t properties = *(const uint32_t *)(info + 0x20);
    info[0x125] = (properties & 2) && is_neo();
    update(info);
}

__declspec(dllexport) void *GetMixedSource(void *translator, Value *result,
                                          const unsigned char *operand, ReadFn original) {
    uint32_t field = *(const uint32_t *)operand;
    if (field == 107 || field == 124 || field == 126 || field == 127 || field == 253) {
        unsigned char selected[48];
        memcpy(selected, operand, sizeof(selected));
        selected[10] = selected[9];
        selected[9] = 0;
        unsigned char *base = (unsigned char *)original - 0xcf88c0;
        ReadFn read = (ReadFn)(base + (selected[21] ? 0xcf20f0 : 0xcf04b0));
        return read(translator, result, selected);
    }
    return original(translator, result, operand);
}
typedef struct { void *node; } Iterator;
typedef struct { const Value *first, *last; } Arguments;
typedef uint32_t (*UndefFn)(uint32_t, uint32_t);
typedef Iterator *(*PrependFn)(void *, Iterator *, const Iterator *, uint32_t, const Arguments *, uint32_t);

enum {
    Missing, GetSrc16, SetDst16, SetDst64, Recip, Sqrt, Log2, Exp2,
    Mul, Add, Ballot, Equal, NotEqual, Greater, GreaterEqual, Less,
    LessEqual, IsNan, LogicalOr, Convert, LogicalAnd, UndefOpcode, Prepend
};

static _Thread_local unsigned char *ssa_base;
static _Thread_local const uint32_t *ssa_api;

__declspec(dllexport) uint32_t SelectUndefOpcode(uint32_t kind, uint32_t type,
                                                const uint32_t *api, unsigned char *base) {
    ssa_base = base;
    ssa_api = api;
    if (kind == 367) {
        if (type == (1u << 20)) return 0x90;
        if (type == (1u << 21)) return 0x91;
        if (type == (1u << 22)) return 0x92;
    }
    return ((UndefFn)(base + api[UndefOpcode]))(kind, type);
}

__declspec(dllexport) Iterator *BuildUndef(void *block, Iterator *result, const Iterator *position,
                                         uint32_t opcode, const Arguments *args, uint32_t flags) {
    Value zero[4] = {{4096,0,0},{4096,0,0},{4096,0,0},{4096,0,0}};
    Arguments selected = *args;
    if (opcode >= 0x90 && opcode <= 0x92 && args->first == args->last) {
        selected.first = zero;
        selected.last = zero + opcode - 0x90 + 2;
    }
    return ((PrependFn)(ssa_base + ssa_api[Prepend]))(block, result, position, opcode, &selected, flags);
}

static void note(uint32_t opcode) {
    static volatile LONG emitted[2048];
    if (opcode >= 2048 || InterlockedCompareExchange(emitted + opcode, 1, 0)) return;
    char line[96];
    int count = snprintf(line, sizeof(line), "Translated half-precision opcode %u\r\n", opcode);
    HANDLE file = CreateFileA("pro-fix.log", FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file != INVALID_HANDLE_VALUE) {
        DWORD written;
        WriteFile(file, line, (DWORD)count, &written, NULL);
        CloseHandle(file);
    }
}

__declspec(dllexport) void TranslateProHalf(void *translator, const unsigned char *inst,
                                           const uint32_t *api, unsigned char *base) {
    uint32_t opcode = *(const uint32_t *)inst;
    if (opcode == 1343) { /* V_SUB_CO_U32, VOP3B */
        unsigned char decoded[0x148];
        memcpy(decoded, inst, sizeof(decoded));
        uint32_t sdst = (*(const uint64_t *)(inst + 0x10) >> 8) & 127;
        *(uint32_t *)(decoded + 0x20) = 2;
        *(uint32_t *)(decoded + 0x114) = sdst <= 103 ? 0 : sdst;
        *(uint32_t *)(decoded + 0x118) = 4; /* Uint64 */
        *(uint32_t *)(decoded + 0x140) = sdst;
        decoded[0xe4 + 12] = 0; /* sdst bits are not a clamp modifier */
        for (unsigned i = 0; i < 3; ++i) decoded[0x24 + 48 * i + 10] = 0;
        ((MissingFn)(base + 0xcfece0))(translator, decoded);
        note(opcode);
        return;
    }
    int comparison = opcode >= 365 && opcode <= 372;
    int math = opcode == 557 || opcode == 558 || opcode == 560 || opcode == 561;
    if (!comparison && !math && opcode != 1392) {
        ((MissingFn)(base + api[Missing]))(translator, inst);
        return;
    }
    Value a = {0}, b = {0}, c = {0}, result = {0};
    ReadFn read = (ReadFn)(base + api[GetSrc16]);
    ReadHalfSource(translator, &a, inst + 0x24, read);
    if (comparison) {
        ReadHalfSource(translator, &b, inst + 0x54, read);
        uint32_t cond = opcode - 365;
        if (cond == 0) {
            result.type = 64;
            result.value = 0;
        } else if (cond == 7) {
            Value ca = {0}, cb = {0};
            ((CompareFn)(base + api[Equal]))(translator, &ca, &a, &a, 1);
            ((CompareFn)(base + api[Equal]))(translator, &cb, &b, &b, 1);
            ((BinaryFn)(base + api[LogicalAnd]))(translator, &result, &ca, &cb);
        } else {
            static const unsigned char functions[8] = {
                0, Less, Equal, LessEqual, Greater, NotEqual, GreaterEqual, 0
            };
            ((CompareFn)(base + api[functions[cond]]))(translator, &result, &a, &b, 1);
        }
        Value mask = {0};
        ((UnaryFn)(base + api[Ballot]))(translator, &mask, &result);
        ((WriteFn)(base + api[SetDst64]))(translator, inst + 0x114, &mask);
    } else if (math) {
        unsigned index = opcode == 557 ? Recip : opcode == 558 ? Sqrt : opcode == 560 ? Log2 : Exp2;
        ((UnaryFn)(base + api[index]))(translator, &result, &a);
        WriteHalfDestination(translator, inst + 0xe4, &result, (WriteFn)(base + api[SetDst16]));
    } else {
        Value product = {0}, product16 = {0}, rounded = {0};
        ReadHalfSource(translator, &b, inst + 0x54, read);
        ReadHalfSource(translator, &c, inst + 0x84, read);
        ((BinaryFn)(base + api[Mul]))(translator, &product, &a, &b);
        ((ConvertFn)(base + api[Convert]))(translator, &product16, 16, &product);
        ((ConvertFn)(base + api[Convert]))(translator, &rounded, 32, &product16);
        ((BinaryFn)(base + api[Add]))(translator, &result, &rounded, &c);
        WriteHalfDestination(translator, inst + 0xe4, &result, (WriteFn)(base + api[SetDst16]));
    }
    note(opcode);
}
