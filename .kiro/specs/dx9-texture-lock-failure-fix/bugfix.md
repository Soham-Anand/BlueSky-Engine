# Bugfix Requirements Document

## Introduction

The application crashes on Windows 7 when attempting to create a font atlas during initialization. The error occurs because `D3D9Device.UploadTexture()` attempts to lock a texture surface that was created with `D3DPOOL_DEFAULT`, which is not lockable in DirectX 9. The HRESULT `0x8876086C` corresponds to `D3DERR_INVALIDCALL`, indicating an invalid operation.

This bug prevents the application from running on Windows 7 systems, as the font atlas creation fails during `Program.OnLoad()` when initializing the editor.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a texture is created with `TextureUsage.Sampled` (non-render-target) THEN the system creates it with `D3DPOOL_DEFAULT`

1.2 WHEN `UploadTexture()` is called on a texture created with `D3DPOOL_DEFAULT` THEN the system attempts to lock the surface directly

1.3 WHEN the surface lock is attempted on a `D3DPOOL_DEFAULT` texture THEN the system fails with HRESULT `0x8876086C` (D3DERR_INVALIDCALL)

1.4 WHEN the lock fails THEN the system throws an exception "Failed to lock texture surface" and crashes

### Expected Behavior (Correct)

2.1 WHEN a texture is created with `TextureUsage.Sampled` (non-render-target) THEN the system SHALL create it with `D3DPOOL_MANAGED` to allow CPU access

2.2 WHEN `UploadTexture()` is called on a `D3DPOOL_MANAGED` texture THEN the system SHALL successfully lock the surface

2.3 WHEN the surface lock succeeds THEN the system SHALL copy the texture data and unlock the surface without errors

2.4 WHEN font atlas creation completes THEN the system SHALL continue initialization without crashing

### Unchanged Behavior (Regression Prevention)

3.1 WHEN a texture is created with `TextureUsage.RenderTarget` THEN the system SHALL CONTINUE TO create it with `D3DPOOL_DEFAULT` and `D3DUSAGE_RENDERTARGET`

3.2 WHEN a texture is created with `TextureUsage.DepthStencil` THEN the system SHALL CONTINUE TO create it with `D3DPOOL_DEFAULT` and `D3DUSAGE_DEPTHSTENCIL`

3.3 WHEN `UploadTexture()` is called on render target or depth textures THEN the system SHALL CONTINUE TO handle them appropriately (or fail gracefully if not supported)

3.4 WHEN existing buffer upload operations are performed THEN the system SHALL CONTINUE TO work as before
