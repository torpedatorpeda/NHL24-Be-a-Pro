# R7: enable supported 16-bit uniform-buffer access

Status: corrective build for a concrete Vulkan feature mismatch. The affected
RTX 5060 post-match coach scene has not been replayed. Its device-loss root
cause is not established, and this change is not proof that the crash is fixed.
The user requested proceeding without another diagnostic run on that machine.

A prior, isolated diagnostic startup of unchanged R6 on RTX 4060 Ti reported:

- VUID-RuntimeSpirv-uniformAndStorageBuffer16BitAccess-06332: the generated
  shader uses a 16-bit variable in a Uniform block, but the device feature
  was not enabled.
- VUID-VkShaderModuleCreateInfo-pCode-08740: the shader declares the matching
  capability without enabling its Vulkan requirement.

The device-creation source only copied storageBuffer16BitAccess and
shaderDrawParameters from the queried Vulkan 1.1 features. It omitted
uniformAndStorageBuffer16BitAccess. These are separate features; enabling
16-bit storage-buffer access does not enable 16-bit uniform-buffer access.
Reference: [VkPhysicalDeviceVulkan11Features](https://docs.vulkan.org/refpages/latest/refpages/source/VkPhysicalDeviceVulkan11Features.html).

The pinned executable already delegates device creation to the memory helper.
R7 corrects that helper, so no additional binary hook is required. It recognizes
the exact existing Features2 -> Vulkan11Features chain prefix, queries support,
and enables uniformAndStorageBuffer16BitAccess only if advertised and the
original 16-bit storage feature is requested. Already-enabled, unsupported,
absent-query and unrecognized-chain cases retain the original request.

The adapter copies the device-create record and the two prefix records on its
own stack. All original feature values, queue/extension lists and the remaining
chain are retained; caller memory is never modified. The corrected request is
used both with optional paging and on the existing no-paging fallback path.
The local memory log records uniform16-enabled when this adaptation applies.

The available source snapshot enables the same queried feature directly in
vk_instance.cpp. Existing R6 texel/mip handling, shader preprocessing and arena
allocation policies remain in the common package. The native nhl24.exe is
byte-for-byte R6; the memory helper and launcher are updated. Storage version
4 and the existing AppData profile are unchanged.

Fifteen short adapter checks cover supported/unsupported/already-enabled
features, unknown or absent prefixes, a missing query, cloning/pointer links
and byte-for-byte preservation of the input chain and every unrelated field.
The helper compiles with the existing Windows x64 Zig toolchain. These are
implementation checks, not a game reproduction or an RTX 5060 verification.

```powershell
zig cc -target x86_64-windows-gnu -O2 -Wall -Wextra launcher/tests/validate_uniform16.c -o validate_uniform16.exe
.\validate_uniform16.exe
```
