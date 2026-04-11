# DirectX 9 Texture Lock Bug Condition Exploration Test

## Overview

This document describes the bug condition exploration test for the DirectX 9 texture lock failure issue.

**Test File**: `TextureLockBugExplorationTest.cs`

**Validates**: Requirements 2.1, 2.2, 2.3, 2.4 from the bugfix specification

## Purpose

This test is designed to **surface counterexamples** that demonstrate the bug exists in the unfixed code. It is a **property-based exploration test** that verifies the bug condition:

- **Bug Condition**: Textures created with `TextureUsage.Sampled` (without RenderTarget or DepthStencil flags) fail when `UploadTexture()` is called because they are created with `D3DPOOL_DEFAULT`, which cannot be locked in DirectX 9.

## Expected Behavior

### On UNFIXED Code (Current State)

The test is **EXPECTED TO FAIL** with the following error:

```
Failed to lock texture surface, HRESULT: 0x8876086C
```

This HRESULT (`0x8876086C`) corresponds to `D3DERR_INVALIDCALL`, which confirms the root cause:
- The texture was created with `D3DPOOL_DEFAULT`
- DirectX 9 does not allow locking `D3DPOOL_DEFAULT` textures
- The `UploadTexture()` method attempts to lock the surface and fails

**This failure is CORRECT and EXPECTED** - it proves the bug exists!

### After Fix Implementation

Once the fix is implemented (changing sampled-only textures to use `D3DPOOL_MANAGED`), this test should **PASS** without any exceptions.

## How to Run

### Prerequisites

- Windows operating system (DirectX 9 is Windows-only)
- .NET 8.0 SDK installed
- The test project must be built successfully

### Running the Test

From the repository root, run:

```bash
dotnet run --project BlueSky.RHI.Test/BlueSky.RHI.Test.csproj -- texture-lock-bug
```

Or from the test project directory:

```bash
cd BlueSky.RHI.Test
dotnet run -- texture-lock-bug
```

## Test Cases

The test includes four specific test cases that cover different scenarios:

### 1. Font Atlas Creation
- **Format**: `TextureFormat.R8Unorm`
- **Size**: 256x256
- **Usage**: `TextureUsage.Sampled`
- **Purpose**: Reproduces the original bug scenario where font atlas creation fails

### 2. RGBA Texture Creation
- **Format**: `TextureFormat.RGBA8Unorm`
- **Size**: 256x256
- **Usage**: `TextureUsage.Sampled`
- **Purpose**: Tests the bug with a common RGBA texture format

### 3. Small Texture
- **Format**: `TextureFormat.RGBA8Unorm`
- **Size**: 64x64
- **Usage**: `TextureUsage.Sampled`
- **Purpose**: Tests the bug with a small texture size

### 4. Large Texture
- **Format**: `TextureFormat.RGBA8Unorm`
- **Size**: 2048x2048
- **Usage**: `TextureUsage.Sampled`
- **Purpose**: Tests the bug with a large texture size

## Counterexample Documentation

When run on unfixed code, the test will produce counterexamples showing:

1. **Exception Type**: `System.Exception`
2. **Exception Message**: "Failed to lock texture surface, HRESULT: 0x8876086C"
3. **Root Cause**: D3DPOOL_DEFAULT textures cannot be locked
4. **Expected Fix**: Use D3DPOOL_MANAGED for TextureUsage.Sampled textures

## Important Notes

- **DO NOT attempt to fix the test when it fails** - the failure is expected and correct
- **DO NOT attempt to fix the code yet** - this is just the exploration phase
- The test encodes the **expected behavior** after the fix
- This test will be used to validate the fix once it's implemented

## Next Steps

After running this test and confirming the bug exists:

1. Document the counterexamples found
2. Proceed to implement the fix in `D3D9Texture.cs`
3. Run this test again to verify the fix works
4. Implement preservation tests to ensure render targets and depth buffers still work correctly
