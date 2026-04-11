# Quick Test Guide - DirectX 9 Texture Lock Fix

## TL;DR - Run These Commands on Windows

```bash
# 1. Build the project
dotnet build BlueSky.RHI.Test/BlueSky.RHI.Test.csproj

# 2. Test bug fix (sampled textures can be uploaded)
dotnet run --project BlueSky.RHI.Test/BlueSky.RHI.Test.csproj -- texture-lock-bug

# 3. Test no regressions (render targets still work)
dotnet run --project BlueSky.RHI.Test/BlueSky.RHI.Test.csproj -- texture-pool-preservation

# 4. Test full application (font atlas creation)
dotnet run --project BlueSky.RHI.Test/BlueSky.RHI.Test.csproj
```

## What Should Happen

### Test 1: Bug Fix Test
✅ All 4 test cases pass (Font Atlas, RGBA, Small, Large textures)
✅ No HRESULT 0x8876086C exceptions

### Test 2: Preservation Test
✅ All 3 properties pass (Render Targets, Depth Buffers, Combined Usage)
✅ No exceptions during texture creation

### Test 3: Full Application
✅ Window opens successfully
✅ UI text is visible ("BlueSky Engine - RHI Test", FPS counter)
✅ No crashes during startup

## If Something Fails

1. **Clean and rebuild**: `dotnet clean && dotnet build`
2. **Check the fix** in `BlueSkyEngine/RHI/DirectX9/D3D9Texture.cs`
3. **Verify DirectX 9** is installed on your system
4. **Report the error** with full console output

## The Fix

The fix changes how textures are created based on their usage:

- **Sampled textures** → `D3DPOOL_MANAGED` (can be locked and uploaded)
- **Render targets** → `D3DPOOL_DEFAULT` (GPU-only, unchanged)
- **Depth buffers** → `D3DPOOL_DEFAULT` (GPU-only, unchanged)

This allows font atlas creation to work while preserving existing render target behavior.
