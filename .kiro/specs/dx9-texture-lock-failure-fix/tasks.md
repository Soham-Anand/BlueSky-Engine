`# Implementation Plan

- [x] 1. Write bug condition exploration test
  - **Property 1: Bug Condition** - Sampled Textures Fail to Lock with D3DPOOL_DEFAULT
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate the bug exists
  - **Scoped PBT Approach**: Scope the property to concrete failing cases - textures with TextureUsage.Sampled that are uploaded
  - Test that creating a texture with TextureUsage.Sampled and calling UploadTexture() succeeds without throwing D3DERR_INVALIDCALL exception
  - Test cases to include:
    - Font atlas creation (TextureUsage.Sampled, TextureFormat.R8Unorm) with glyph bitmap data upload
    - RGBA texture creation (TextureUsage.Sampled, TextureFormat.RGBA8Unorm) with sample data upload
    - Small texture (64x64) with TextureUsage.Sampled and data upload
    - Large texture (2048x2048) with TextureUsage.Sampled and data upload
  - The test assertions should match the Expected Behavior Properties from design (Requirements 2.1-2.4)
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Test FAILS with "Failed to lock texture surface, HRESULT: 0x8876086C" (this is correct - it proves the bug exists)
  - Document counterexamples found to understand root cause
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 2.1, 2.2, 2.3, 2.4_

- [x] 2. Write preservation property tests (BEFORE implementing fix)
  - **Property 2: Preservation** - Render Target and Depth Buffer Pool Selection
  - **IMPORTANT**: Follow observation-first methodology
  - Observe behavior on UNFIXED code for non-buggy inputs (textures with RenderTarget or DepthStencil flags)
  - Write property-based tests capturing observed behavior patterns from Preservation Requirements:
    - Render target textures are created with D3DPOOL_DEFAULT and D3DUSAGE_RENDERTARGET
    - Depth/stencil textures are created with D3DPOOL_DEFAULT and D3DUSAGE_DEPTHSTENCIL
    - Combined usage flags (e.g., Sampled | RenderTarget) use D3DPOOL_DEFAULT
    - Buffer upload operations work identically
  - Property-based testing generates many test cases for stronger guarantees
  - Run tests on UNFIXED code
  - **EXPECTED OUTCOME**: Tests PASS (this confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.1, 3.2, 3.3, 3.4_

- [x] 3. Fix for DirectX 9 texture lock failure

  - [x] 3.1 Implement the fix in D3D9Texture constructor
    - Modify `BlueSkyEngine/RHI/DirectX9/D3D9Texture.cs` constructor
    - Add pool selection logic after existing usage flag logic (around line 37-39)
    - Declare `uint pool` variable to store the selected pool value
    - Add conditional logic:
      - If `Usage.HasFlag(TextureUsage.RenderTarget)` OR `Usage.HasFlag(TextureUsage.DepthStencil)`, set `pool = D3DPOOL_DEFAULT`
      - Otherwise (sampled-only textures), set `pool = D3DPOOL_MANAGED`
    - Change the hardcoded `D3D9Interop.D3DPOOL_DEFAULT` parameter in the `CreateTexture` call to use the `pool` variable
    - Preserve existing `D3DUSAGE_RENDERTARGET` logic unchanged
    - _Bug_Condition: isBugCondition(input) where input.usage == TextureUsage.Sampled AND NOT input.usage.HasFlag(TextureUsage.RenderTarget) AND NOT input.usage.HasFlag(TextureUsage.DepthStencil) AND UploadTexture_will_be_called(input.texture)_
    - _Expected_Behavior: Sampled textures SHALL be created with D3DPOOL_MANAGED, allowing UploadTexture() to successfully lock and upload data (Requirements 2.1-2.4)_
    - _Preservation: Render target and depth/stencil textures SHALL continue to use D3DPOOL_DEFAULT with appropriate usage flags (Requirements 3.1-3.4)_
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 3.1, 3.2, 3.3, 3.4_

  - [x] 3.2 Verify bug condition exploration test now passes
    - **Property 1: Expected Behavior** - Sampled Textures Use Managed Pool
    - **IMPORTANT**: Re-run the SAME test from task 1 - do NOT write a new test
    - The test from task 1 encodes the expected behavior
    - When this test passes, it confirms the expected behavior is satisfied
    - Run bug condition exploration test from step 1
    - **EXPECTED OUTCOME**: Test PASSES (confirms bug is fixed - sampled textures can now be locked and uploaded)
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 3.3 Verify preservation tests still pass
    - **Property 2: Preservation** - Render Target and Depth Buffer Pool
    - **IMPORTANT**: Re-run the SAME tests from task 2 - do NOT write new tests
    - Run preservation property tests from step 2
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions - render targets and depth buffers still use D3DPOOL_DEFAULT)
    - Confirm all tests still pass after fix (no regressions)
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

- [x] 4. Checkpoint - Ensure all tests pass
  - Verify bug condition test passes (sampled textures can be uploaded)
  - Verify preservation tests pass (render targets and depth buffers unchanged)
  - Test full font atlas creation flow on Windows 7
  - Ensure application initializes without crashing
  - Ask the user if questions arise
