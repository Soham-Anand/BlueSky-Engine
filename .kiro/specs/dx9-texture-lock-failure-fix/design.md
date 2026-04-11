# DirectX 9 Texture Lock Failure Bugfix Design

## Overview

The application crashes on Windows 7 when creating a font atlas because `D3D9Texture` creates all textures with `D3DPOOL_DEFAULT`, which cannot be locked for CPU access. The fix changes non-render-target textures (those with only `TextureUsage.Sampled`) to use `D3DPOOL_MANAGED` instead, allowing `UploadTexture()` to successfully lock and write texture data. This is a minimal, targeted fix that preserves existing behavior for render targets and depth buffers.

## Glossary

- **Bug_Condition (C)**: The condition that triggers the bug - when a texture is created with `TextureUsage.Sampled` (without RenderTarget or DepthStencil flags) and then `UploadTexture()` is called on it
- **Property (P)**: The desired behavior when the bug condition occurs - the texture should be created with `D3DPOOL_MANAGED` so it can be locked and uploaded successfully
- **Preservation**: Existing render target and depth buffer creation behavior that must remain unchanged by the fix
- **D3D9Texture**: The class in `BlueSkyEngine/RHI/DirectX9/D3D9Texture.cs` that creates DirectX 9 texture resources
- **UploadTexture**: The method in `D3D9Device` that locks a texture surface and copies CPU data to GPU memory
- **D3DPOOL_DEFAULT**: DirectX 9 memory pool for GPU-only resources that cannot be locked by CPU
- **D3DPOOL_MANAGED**: DirectX 9 memory pool for resources that can be accessed by both CPU and GPU
- **TextureUsage**: Flags enum that specifies how a texture will be used (Sampled, RenderTarget, DepthStencil, etc.)

## Bug Details

### Bug Condition

The bug manifests when a texture is created with only the `TextureUsage.Sampled` flag (no RenderTarget or DepthStencil), and then `UploadTexture()` is called to write data to it. The `D3D9Texture` constructor always uses `D3DPOOL_DEFAULT` regardless of usage flags, which makes the texture unlockable. When `UploadTexture()` attempts to lock the surface, DirectX 9 returns `D3DERR_INVALIDCALL` (0x8876086C).

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type TextureCreationRequest
  OUTPUT: boolean
  
  RETURN input.usage == TextureUsage.Sampled
         AND NOT input.usage.HasFlag(TextureUsage.RenderTarget)
         AND NOT input.usage.HasFlag(TextureUsage.DepthStencil)
         AND UploadTexture_will_be_called(input.texture)
END FUNCTION
```

### Examples

- **Font Atlas Creation**: `FontAtlas` creates a texture with `TextureUsage.Sampled` and `TextureFormat.R8Unorm`, then calls `UploadTexture()` with glyph bitmap data. This fails with HRESULT 0x8876086C because the texture was created with `D3DPOOL_DEFAULT`.

- **Any Sampled Texture Upload**: Any code path that creates a texture with only `TextureUsage.Sampled` and then uploads data will fail in the same way.

- **Render Target Creation**: A texture created with `TextureUsage.RenderTarget` should continue to use `D3DPOOL_DEFAULT` and should NOT be affected by this fix.

- **Depth Buffer Creation**: A texture created with `TextureUsage.DepthStencil` should continue to use `D3DPOOL_DEFAULT` and should NOT be affected by this fix.

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Render target textures must continue to be created with `D3DPOOL_DEFAULT` and `D3DUSAGE_RENDERTARGET`
- Depth stencil textures must continue to be created with `D3DPOOL_DEFAULT` and `D3DUSAGE_DEPTHSTENCIL`
- Buffer upload operations must continue to work exactly as before
- All other DirectX 9 device operations must remain unchanged

**Scope:**
All texture creation requests that include `TextureUsage.RenderTarget` or `TextureUsage.DepthStencil` flags should be completely unaffected by this fix. This includes:
- Render target textures for offscreen rendering
- Depth/stencil buffers for depth testing
- Any combined usage flags that include RenderTarget or DepthStencil

## Hypothesized Root Cause

Based on the bug description and code analysis, the root cause is clear:

1. **Incorrect Pool Selection**: The `D3D9Texture` constructor hardcodes `D3DPOOL_DEFAULT` for all textures on line 42, regardless of the `TextureUsage` flags. DirectX 9 requires different pools for different use cases:
   - `D3DPOOL_DEFAULT` is for GPU-only resources (render targets, depth buffers)
   - `D3DPOOL_MANAGED` is for resources that need CPU access (uploadable textures)

2. **Missing Usage-Based Logic**: The constructor checks for `TextureUsage.RenderTarget` to set `D3DUSAGE_RENDERTARGET` but doesn't use the usage flags to determine the appropriate memory pool.

3. **DirectX 9 API Constraint**: Unlike modern APIs, DirectX 9 strictly enforces that `D3DPOOL_DEFAULT` textures cannot be locked. The `LockRect` call in `UploadTexture()` fails with `D3DERR_INVALIDCALL` when attempted on such textures.

## Correctness Properties

Property 1: Bug Condition - Sampled Textures Use Managed Pool

_For any_ texture creation request where only `TextureUsage.Sampled` is specified (without RenderTarget or DepthStencil flags), the fixed `D3D9Texture` constructor SHALL create the texture with `D3DPOOL_MANAGED`, allowing `UploadTexture()` to successfully lock the surface and copy data without throwing an exception.

**Validates: Requirements 2.1, 2.2, 2.3, 2.4**

Property 2: Preservation - Render Target and Depth Buffer Pool

_For any_ texture creation request where `TextureUsage.RenderTarget` or `TextureUsage.DepthStencil` flags are specified, the fixed `D3D9Texture` constructor SHALL produce exactly the same behavior as the original constructor, creating the texture with `D3DPOOL_DEFAULT` and the appropriate usage flags.

**Validates: Requirements 3.1, 3.2, 3.3**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File**: `BlueSkyEngine/RHI/DirectX9/D3D9Texture.cs`

**Function**: `D3D9Texture` constructor (the one that takes `D3D9Device` and `TextureDesc`)

**Specific Changes**:
1. **Add Pool Selection Logic**: After the existing usage flag logic (around line 37-39), add logic to determine the appropriate pool based on texture usage:
   - If `Usage.HasFlag(TextureUsage.RenderTarget)` OR `Usage.HasFlag(TextureUsage.DepthStencil)`, use `D3DPOOL_DEFAULT`
   - Otherwise (sampled-only textures), use `D3DPOOL_MANAGED`

2. **Declare Pool Variable**: Add a `uint pool` variable to store the selected pool value.

3. **Pass Pool to CreateTexture**: Change the hardcoded `D3D9Interop.D3DPOOL_DEFAULT` parameter in the `CreateTexture` call to use the `pool` variable instead.

4. **Preserve Existing Usage Flags**: Keep the existing `D3DUSAGE_RENDERTARGET` logic unchanged to ensure render targets continue to work.

**Pseudocode**:
```
uint usage = 0;
uint pool;

if (Usage.HasFlag(TextureUsage.RenderTarget))
    usage |= D3DUSAGE_RENDERTARGET;

// Determine pool based on usage
if (Usage.HasFlag(TextureUsage.RenderTarget) || Usage.HasFlag(TextureUsage.DepthStencil))
    pool = D3DPOOL_DEFAULT;  // GPU-only resources
else
    pool = D3DPOOL_MANAGED;  // CPU-accessible resources

var hr = CreateTexture(device.Device, desc.Width, desc.Height, 1, usage,
    D3D9Interop.ToD3DFormat(desc.Format), pool, out _texture, IntPtr.Zero);
```

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bug on unfixed code, then verify the fix works correctly and preserves existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bug BEFORE implementing the fix. Confirm that the root cause is indeed the pool selection issue.

**Test Plan**: Write tests that create textures with `TextureUsage.Sampled` and attempt to upload data. Run these tests on the UNFIXED code to observe the `D3DERR_INVALIDCALL` failure and confirm the root cause.

**Test Cases**:
1. **Font Atlas Upload Test**: Create a texture with `TextureUsage.Sampled` and `TextureFormat.R8Unorm`, then call `UploadTexture()` with sample data (will fail on unfixed code with HRESULT 0x8876086C)
2. **RGBA Texture Upload Test**: Create a texture with `TextureUsage.Sampled` and `TextureFormat.RGBA8Unorm`, then upload RGBA data (will fail on unfixed code)
3. **Small Texture Upload Test**: Create a 64x64 sampled texture and upload data (will fail on unfixed code)
4. **Large Texture Upload Test**: Create a 2048x2048 sampled texture and upload data (will fail on unfixed code)

**Expected Counterexamples**:
- `UploadTexture()` throws exception "Failed to lock texture surface, HRESULT: 0x8876086C"
- Root cause confirmed: `D3DPOOL_DEFAULT` textures cannot be locked

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed function produces the expected behavior.

**Pseudocode:**
```
FOR ALL textureDesc WHERE isBugCondition(textureDesc) DO
  texture := D3D9Texture_fixed(device, textureDesc)
  result := UploadTexture(texture, sampleData)
  ASSERT result succeeds without exception
  ASSERT texture data is correctly uploaded
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed function produces the same result as the original function.

**Pseudocode:**
```
FOR ALL textureDesc WHERE NOT isBugCondition(textureDesc) DO
  ASSERT D3D9Texture_original(device, textureDesc).pool = D3D9Texture_fixed(device, textureDesc).pool
  ASSERT D3D9Texture_original(device, textureDesc).usage = D3D9Texture_fixed(device, textureDesc).usage
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many test cases automatically across the input domain
- It catches edge cases that manual unit tests might miss
- It provides strong guarantees that behavior is unchanged for all non-buggy inputs

**Test Plan**: Observe behavior on UNFIXED code first for render targets and depth buffers, then write property-based tests capturing that behavior.

**Test Cases**:
1. **Render Target Preservation**: Observe that render target textures are created with `D3DPOOL_DEFAULT` on unfixed code, then verify this continues after fix
2. **Depth Buffer Preservation**: Observe that depth/stencil textures are created with `D3DPOOL_DEFAULT` on unfixed code, then verify this continues after fix
3. **Combined Usage Preservation**: Observe that textures with combined flags (e.g., Sampled | RenderTarget) use `D3DPOOL_DEFAULT` on unfixed code, then verify this continues after fix
4. **Buffer Operations Preservation**: Verify that buffer upload operations continue to work identically before and after the fix

### Unit Tests

- Test sampled texture creation with various formats (R8Unorm, RGBA8Unorm, etc.)
- Test render target texture creation continues to use D3DPOOL_DEFAULT
- Test depth/stencil texture creation continues to use D3DPOOL_DEFAULT
- Test UploadTexture succeeds on sampled textures after fix
- Test edge cases (1x1 textures, maximum size textures)

### Property-Based Tests

- Generate random texture descriptors with Sampled usage and verify UploadTexture succeeds
- Generate random texture descriptors with RenderTarget usage and verify pool is D3DPOOL_DEFAULT
- Generate random texture descriptors with DepthStencil usage and verify pool is D3DPOOL_DEFAULT
- Generate random texture descriptors with combined usage flags and verify correct pool selection

### Integration Tests

- Test full font atlas creation flow (the original failing scenario)
- Test creating and uploading multiple sampled textures in sequence
- Test creating render targets and sampled textures in the same application
- Test that the application successfully initializes on Windows 7 without crashing
