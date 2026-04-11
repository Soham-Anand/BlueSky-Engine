# Task 4: Checkpoint Testing Instructions

## Overview

This document provides step-by-step instructions for verifying that the DirectX 9 texture lock bug has been fixed and that no regressions have been introduced.

## Prerequisites

- **Windows 7 or later** (DirectX 9 is Windows-only)
- **.NET 8.0 SDK** installed
- **DirectX 9 runtime** (should be pre-installed on Windows 7)
- Access to the BlueSky Engine repository

## Test Execution Steps

### Step 1: Build the Project

Open a command prompt or PowerShell in the repository root and run:

```bash
dotnet build BlueSky.RHI.Test/BlueSky.RHI.Test.csproj
```

**Expected Result**: Build should succeed with no errors (warnings about CsCheck version are OK).

### Step 2: Run Bug Condition Exploration Test

This test verifies that sampled textures can now be uploaded successfully (the original bug is fixed).

```bash
dotnet run --project BlueSky.RHI.Test/BlueSky.RHI.Test.csproj -- texture-lock-bug
```

**Expected Output** (AFTER FIX):
```
=== DirectX 9 Texture Lock Bug Condition Exploration Test ===

**CRITICAL**: This test is EXPECTED TO FAIL on unfixed code
Failure with HRESULT 0x8876086C confirms the bug exists

Running bug condition exploration tests...

Test Case 1: Font Atlas Creation (TextureUsage.Sampled, TextureFormat.R8Unorm)
  Creating 256x256 texture with R8Unorm...
  Uploading 65536 bytes of texture data...
  ✓ Font Atlas upload succeeded!

Test Case 2: RGBA Texture Creation (TextureUsage.Sampled, TextureFormat.RGBA8Unorm)
  Creating 256x256 texture with RGBA8Unorm...
  Uploading 262144 bytes of texture data...
  ✓ RGBA Texture upload succeeded!

Test Case 3: Small Texture (64x64, TextureUsage.Sampled)
  Creating 64x64 texture with RGBA8Unorm...
  Uploading 16384 bytes of texture data...
  ✓ Small Texture upload succeeded!

Test Case 4: Large Texture (2048x2048, TextureUsage.Sampled)
  Creating 2048x2048 texture with RGBA8Unorm...
  Uploading 16777216 bytes of texture data...
  ✓ Large Texture upload succeeded!

=== ALL TESTS PASSED ===
This means the bug has been FIXED or doesn't exist in this environment.
Expected behavior: Sampled textures can be locked and uploaded successfully.
```

**✅ SUCCESS CRITERIA**: All 4 test cases pass without throwing exceptions.

**❌ FAILURE INDICATORS**:
- Exception with HRESULT 0x8876086C (means fix didn't work)
- Any other exceptions or crashes

### Step 3: Run Preservation Property Tests

This test verifies that render targets and depth buffers still work correctly (no regressions).

```bash
dotnet run --project BlueSky.RHI.Test/BlueSky.RHI.Test.csproj -- texture-pool-preservation
```

**Expected Output**:
```
=== DirectX 9 Texture Pool Preservation Property Tests ===

**IMPORTANT**: These tests verify baseline behavior on unfixed code
Tests should PASS, confirming behavior to preserve after fix

Running preservation property tests...

Property 2.1: Render Target Textures Use D3DPOOL_DEFAULT
  Testing render target texture creation...
    - Standard RGBA8 Render Target (256x256)
    - 512x512 Render Target (512x512)
    - 1024x1024 Render Target (1024x1024)
    - Small 64x64 Render Target (64x64)
  ✓ All render target textures created successfully with D3DPOOL_DEFAULT

Property 2.2: Depth/Stencil Textures Use D3DPOOL_DEFAULT
  Testing depth/stencil texture creation...
    - Depth24Stencil8 (256x256)
    - Depth32Float (512x512)
    - 1024x1024 Depth Buffer (1024x1024)
    - Small 64x64 Depth Buffer (64x64)
  ✓ All depth/stencil textures created successfully with D3DPOOL_DEFAULT

Property 2.3: Combined Usage Flags Use D3DPOOL_DEFAULT
  Testing combined usage flag textures...
    - Sampled | RenderTarget
    - RenderTarget | Sampled (reversed)
  ✓ All combined usage textures created successfully with D3DPOOL_DEFAULT

=== ALL PRESERVATION TESTS PASSED ===
Baseline behavior confirmed - these behaviors must be preserved after fix.
```

**✅ SUCCESS CRITERIA**: All preservation tests pass without exceptions.

**❌ FAILURE INDICATORS**:
- Any exceptions during render target creation
- Any exceptions during depth/stencil creation
- Any exceptions during combined usage texture creation

### Step 4: Test Full Application Initialization

This verifies that the original crash scenario (font atlas creation during app startup) is fixed.

```bash
dotnet run --project BlueSky.RHI.Test/BlueSky.RHI.Test.csproj
```

**Expected Behavior**:
- Application window opens successfully
- Font atlas is created without crashing
- UI text is rendered (you should see "BlueSky Engine - RHI Test" and FPS counter)
- No exceptions or crashes during initialization

**✅ SUCCESS CRITERIA**: 
- Application starts without crashing
- Window displays with UI text visible
- No error messages in console

**❌ FAILURE INDICATORS**:
- Application crashes during startup
- Exception with HRESULT 0x8876086C
- Window opens but no text is visible (font atlas failed)

### Step 5: Verify on Windows 7 Specifically

If possible, run all the above tests on **Windows 7** specifically, as that was the original environment where the bug was reported.

**Why Windows 7?**
- The original crash log came from Windows 7
- DirectX 9 behavior may differ slightly between Windows versions
- Windows 7 is the target environment for this fix

## Verification Checklist

Use this checklist to track your testing progress:

- [ ] Project builds successfully
- [ ] Bug condition test passes (all 4 test cases)
- [ ] Preservation tests pass (all 3 properties)
- [ ] Full application initializes without crashing
- [ ] Font atlas is created successfully
- [ ] UI text is rendered correctly
- [ ] Tested on Windows 7 (if available)

## Troubleshooting

### If Bug Condition Test Fails

**Symptom**: Exception with HRESULT 0x8876086C

**Possible Causes**:
1. Fix was not applied correctly in `D3D9Texture.cs`
2. Build is using cached/old binaries

**Solutions**:
1. Verify the fix in `BlueSkyEngine/RHI/DirectX9/D3D9Texture.cs` (lines 42-47)
2. Clean and rebuild: `dotnet clean && dotnet build`
3. Check that the pool selection logic is correct

### If Preservation Tests Fail

**Symptom**: Exceptions during render target or depth buffer creation

**Possible Causes**:
1. Fix incorrectly changed pool for render targets/depth buffers
2. Logic error in pool selection

**Solutions**:
1. Verify the pool selection logic includes the OR condition for RenderTarget and DepthStencil
2. Check that combined usage flags (Sampled | RenderTarget) use D3DPOOL_DEFAULT

### If Application Crashes on Startup

**Symptom**: Application crashes before window opens or during font atlas creation

**Possible Causes**:
1. Font file missing (`roboto.ttf`)
2. DirectX 9 runtime not installed
3. Graphics driver issues

**Solutions**:
1. Verify `roboto.ttf` exists in the output directory
2. Install DirectX 9 runtime from Microsoft
3. Update graphics drivers

## Expected Fix Verification

The fix should have made the following changes in `BlueSkyEngine/RHI/DirectX9/D3D9Texture.cs`:

```csharp
// Around line 37-47
uint usage = 0;
if (Usage.HasFlag(TextureUsage.RenderTarget))
    usage |= D3D9Interop.D3DUSAGE_RENDERTARGET;

// Determine pool based on usage
uint pool;
if (Usage.HasFlag(TextureUsage.RenderTarget) || Usage.HasFlag(TextureUsage.DepthStencil))
    pool = D3D9Interop.D3DPOOL_DEFAULT;  // GPU-only resources
else
    pool = D3D9Interop.D3DPOOL_MANAGED;  // CPU-accessible resources

var hr = CreateTexture(device.Device, desc.Width, desc.Height, 1, usage,
    D3D9Interop.ToD3DFormat(desc.Format), pool, out _texture, IntPtr.Zero);
```

**Key Points**:
- `pool` variable is declared
- Pool selection uses OR condition for RenderTarget and DepthStencil
- `pool` variable is passed to `CreateTexture` instead of hardcoded `D3DPOOL_DEFAULT`

## Reporting Results

After running all tests, please report back with:

1. **Test Results**: Pass/Fail for each test
2. **Console Output**: Copy the full console output from each test
3. **Windows Version**: Which version of Windows you tested on
4. **Any Issues**: Screenshots or error messages if tests failed

## Next Steps After Verification

Once all tests pass:
- ✅ Mark Task 4 as complete
- ✅ Update tasks.md to mark the bugfix as complete
- ✅ Consider the bugfix validated and ready for deployment

If any tests fail:
- ❌ Document the failure
- ❌ Investigate the root cause
- ❌ Apply additional fixes as needed
- ❌ Re-run tests until all pass
