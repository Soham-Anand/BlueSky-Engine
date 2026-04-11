# Tasks: Native Windowing System Implementation

## Task Organization

Tasks are organized into phases for incremental development and testing. Each task includes acceptance criteria and dependencies.

---

## Phase 1: Project Setup and Core Interfaces

### Task 1.1: Create BlueSky.Platform Project
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 1 hour

**Description**: Create a new .NET 8.0 class library project for the platform abstraction layer.

**Acceptance Criteria**:
- [ ] BlueSky.Platform.csproj created with .NET 8.0 target
- [ ] Project added to BlueSky.sln solution file
- [ ] Project compiles successfully
- [ ] Basic folder structure created (macOS/, Windows/, Linux/, Events/, Input/)

**Dependencies**: None

---

### Task 1.2: Define IWindow Interface
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 2 hours

**Description**: Define the cross-platform window interface with properties, methods, and events.

**Acceptance Criteria**:
- [ ] IWindow.cs created with all required properties (Title, Size, Position, etc.)
- [ ] All required methods defined (Show, Hide, Close, ProcessEvents, GetNativeHandle)
- [ ] All required events defined (Resize, FramebufferResize, FocusGained, etc.)
- [ ] XML documentation comments added
- [ ] Interface compiles without errors

**Dependencies**: Task 1.1

---

### Task 1.3: Define IInputContext Interface
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 2 hours

**Description**: Define the cross-platform input interface for keyboard, mouse, and touch input.

**Acceptance Criteria**:
- [ ] IInputContext.cs created with keyboard methods (IsKeyDown, IsKeyPressed, etc.)
- [ ] Mouse methods defined (IsMouseButtonDown, MousePosition, etc.)
- [ ] Input events defined (KeyDown, KeyUp, MouseMove, etc.)
- [ ] XML documentation comments added
- [ ] Interface compiles without errors

**Dependencies**: Task 1.1

---

### Task 1.4: Create WindowOptions Structure
**Status**: pending  
**Priority**: medium  
**Estimated Effort**: 1 hour

**Description**: Define the window creation options structure with sensible defaults.

**Acceptance Criteria**:
- [ ] WindowOptions.cs created with all required fields
- [ ] Default property implemented with reasonable values
- [ ] XML documentation comments added
- [ ] Structure compiles without errors

**Dependencies**: Task 1.1

---

### Task 1.5: Define Event Classes
**Status**: pending  
**Priority**: medium  
**Estimated Effort**: 2 hours

**Description**: Create event argument classes for window and input events.

**Acceptance Criteria**:
- [ ] WindowEvent.cs created with event types
- [ ] KeyboardEvent.cs created with key code and modifiers
- [ ] MouseEvent.cs created with position and button info
- [ ] TouchEvent.cs created with touch point data
- [ ] All event classes compile without errors

**Dependencies**: Task 1.1

---

### Task 1.6: Define KeyCode Enumeration
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 1 hour

**Description**: Create unified key code enumeration covering all common keys.

**Acceptance Criteria**:
- [ ] KeyCode.cs created with letters, numbers, function keys
- [ ] Modifier keys included (Shift, Control, Alt, Super)
- [ ] Navigation keys included (arrows, Home, End, etc.)
- [ ] Editing keys included (Backspace, Delete, Enter, etc.)
- [ ] Enumeration compiles without errors

**Dependencies**: Task 1.1

---

### Task 1.7: Define MouseButton and ModifierKeys Enumerations
**Status**: pending  
**Priority**: medium  
**Estimated Effort**: 30 minutes

**Description**: Create enumerations for mouse buttons and modifier key states.

**Acceptance Criteria**:
- [ ] MouseButton.cs created (Left, Right, Middle, X1, X2)
- [ ] ModifierKeys.cs created (Shift, Control, Alt, Super)
- [ ] ModifierKeys supports flag combinations
- [ ] Enumerations compile without errors

**Dependencies**: Task 1.1

---

## Phase 2: macOS (Cocoa) Implementation

### Task 2.1: Create CocoaInterop Class
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 3 hours

**Description**: Implement Objective-C runtime interop for Cocoa APIs.

**Acceptance Criteria**:
- [ ] CocoaInterop.cs created with P/Invoke declarations
- [ ] objc_getClass, sel_registerName, objc_msgSend declared
- [ ] Helper methods for common operations (GetClass, GetSelector, SendMessage)
- [ ] Multiple objc_msgSend variants for different signatures
- [ ] All declarations compile without errors

**Dependencies**: Task 1.1

**Files to Create**: `BlueSky.Platform/macOS/CocoaInterop.cs`

---

### Task 2.2: Implement CocoaWindow Class
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 6 hours

**Description**: Implement IWindow interface using NSWindow and NSView.

**Acceptance Criteria**:
- [ ] CocoaWindow.cs created implementing IWindow
- [ ] NSWindow created with proper style mask
- [ ] NSView created as content view
- [ ] CAMetalLayer created with BGRA8Unorm pixel format (80)
- [ ] GetNativeHandle returns NSView pointer
- [ ] Show, Hide, Close methods implemented
- [ ] Property getters/setters implemented (Title, Size, Position)
- [ ] Dispose method properly releases all resources
- [ ] Window displays correctly on macOS

**Dependencies**: Task 1.2, Task 2.1

**Files to Create**: `BlueSky.Platform/macOS/CocoaWindow.cs`

---

### Task 2.3: Implement CocoaEventLoop Class
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 4 hours

**Description**: Implement event loop using NSApplication event polling.

**Acceptance Criteria**:
- [ ] CocoaEventLoop.cs created
- [ ] ProcessEvents method polls NSEvents using nextEventMatchingMask
- [ ] Events dispatched to appropriate handlers
- [ ] Window events (resize, focus, close) handled
- [ ] Event loop runs without blocking
- [ ] Event loop can be terminated gracefully

**Dependencies**: Task 2.2

**Files to Create**: `BlueSky.Platform/macOS/CocoaEventLoop.cs`

---

### Task 2.4: Implement CocoaInput Class
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 5 hours

**Description**: Implement IInputContext interface for macOS input handling.

**Acceptance Criteria**:
- [ ] CocoaInput.cs created implementing IInputContext
- [ ] Keyboard event handling (NSEvent keyDown, keyUp)
- [ ] Key code mapping from NSEvent keyCode to KeyCode
- [ ] Mouse event handling (mouseDown, mouseUp, mouseMoved)
- [ ] Mouse position tracking relative to window
- [ ] Scroll event handling (scrollWheel)
- [ ] Modifier key state tracking
- [ ] Character input handling for text entry
- [ ] All input events fire correctly

**Dependencies**: Task 1.3, Task 1.6, Task 2.2

**Files to Create**: `BlueSky.Platform/macOS/CocoaInput.cs`

---

### Task 2.5: Test macOS Implementation with bgfx
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 3 hours

**Description**: Verify that CocoaWindow works with bgfx Metal backend.

**Acceptance Criteria**:
- [ ] Create test application using CocoaWindow
- [ ] Initialize bgfx with NSView handle from GetNativeHandle()
- [ ] bgfx Metal backend initializes without errors
- [ ] Window displays with bgfx rendering
- [ ] No "invalid pixel format 0" errors
- [ ] Window resize updates bgfx viewport correctly

**Dependencies**: Task 2.2, Task 2.3, Task 2.4

---

## Phase 3: Windows (Win32) Implementation

### Task 3.1: Create Win32Interop Class
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 3 hours

**Description**: Implement Win32 API P/Invoke declarations.

**Acceptance Criteria**:
- [ ] Win32Interop.cs created with P/Invoke declarations
- [ ] CreateWindowEx, DestroyWindow, ShowWindow declared
- [ ] GetMessage, TranslateMessage, DispatchMessage declared
- [ ] Window message constants defined (WM_SIZE, WM_CLOSE, etc.)
- [ ] WNDCLASSEX structure defined
- [ ] All declarations compile without errors

**Dependencies**: Task 1.1

**Files to Create**: `BlueSky.Platform/Windows/Win32Interop.cs`

---

### Task 3.2: Implement Win32Window Class
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 6 hours

**Description**: Implement IWindow interface using Win32 HWND.

**Acceptance Criteria**:
- [ ] Win32Window.cs created implementing IWindow
- [ ] Window class registered with appropriate styles
- [ ] HWND created using CreateWindowEx
- [ ] GetNativeHandle returns HWND
- [ ] Show, Hide, Close methods implemented
- [ ] Property getters/setters implemented (Title, Size, Position)
- [ ] WndProc callback handles window messages
- [ ] Dispose method properly releases all resources
- [ ] Window displays correctly on Windows

**Dependencies**: Task 1.2, Task 3.1

**Files to Create**: `BlueSky.Platform/Windows/Win32Window.cs`

---

### Task 3.3: Implement Win32EventLoop Class
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 3 hours

**Description**: Implement event loop using GetMessage/DispatchMessage.

**Acceptance Criteria**:
- [ ] Win32EventLoop.cs created
- [ ] ProcessEvents method uses PeekMessage for non-blocking polling
- [ ] Messages translated and dispatched to WndProc
- [ ] Event loop runs without blocking
- [ ] Event loop can be terminated gracefully

**Dependencies**: Task 3.2

**Files to Create**: `BlueSky.Platform/Windows/Win32EventLoop.cs`

---

### Task 3.4: Implement Win32Input Class
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 5 hours

**Description**: Implement IInputContext interface for Windows input handling.

**Acceptance Criteria**:
- [ ] Win32Input.cs created implementing IInputContext
- [ ] Keyboard event handling (WM_KEYDOWN, WM_KEYUP)
- [ ] Key code mapping from Virtual-Key codes to KeyCode
- [ ] Mouse event handling (WM_LBUTTONDOWN, WM_MOUSEMOVE, etc.)
- [ ] Mouse position tracking relative to window
- [ ] Scroll event handling (WM_MOUSEWHEEL)
- [ ] Modifier key state tracking (GetKeyState)
- [ ] Character input handling (WM_CHAR)
- [ ] All input events fire correctly

**Dependencies**: Task 1.3, Task 1.6, Task 3.2

**Files to Create**: `BlueSky.Platform/Windows/Win32Input.cs`

---

### Task 3.5: Test Windows Implementation with bgfx
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 3 hours

**Description**: Verify that Win32Window works with bgfx DirectX backend.

**Acceptance Criteria**:
- [ ] Create test application using Win32Window
- [ ] Initialize bgfx with HWND from GetNativeHandle()
- [ ] bgfx DirectX 11 backend initializes without errors
- [ ] Window displays with bgfx rendering
- [ ] Window resize updates bgfx viewport correctly

**Dependencies**: Task 3.2, Task 3.3, Task 3.4

---

## Phase 4: Linux (X11) Implementation

### Task 4.1: Create X11Interop Class
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 3 hours

**Description**: Implement X11 API P/Invoke declarations.

**Acceptance Criteria**:
- [ ] X11Interop.cs created with P/Invoke declarations
- [ ] XOpenDisplay, XCloseDisplay declared
- [ ] XCreateSimpleWindow, XDestroyWindow declared
- [ ] XSelectInput, XPending, XNextEvent declared
- [ ] XEvent structure defined
- [ ] Event mask constants defined
- [ ] All declarations compile without errors

**Dependencies**: Task 1.1

**Files to Create**: `BlueSky.Platform/Linux/X11Interop.cs`

---

### Task 4.2: Implement X11Window Class
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 6 hours

**Description**: Implement IWindow interface using X11 Window.

**Acceptance Criteria**:
- [ ] X11Window.cs created implementing IWindow
- [ ] X11 display opened with XOpenDisplay
- [ ] X11 window created with XCreateSimpleWindow
- [ ] GetNativeHandle returns X11 Window handle
- [ ] Show, Hide, Close methods implemented
- [ ] Property getters/setters implemented (Title, Size, Position)
- [ ] Dispose method properly releases all resources
- [ ] Window displays correctly on Linux

**Dependencies**: Task 1.2, Task 4.1

**Files to Create**: `BlueSky.Platform/Linux/X11Window.cs`

---

### Task 4.3: Implement X11EventLoop Class
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 3 hours

**Description**: Implement event loop using XPending/XNextEvent.

**Acceptance Criteria**:
- [ ] X11EventLoop.cs created
- [ ] ProcessEvents method uses XPending to check for events
- [ ] XNextEvent retrieves events from queue
- [ ] Events dispatched to appropriate handlers
- [ ] Event loop runs without blocking
- [ ] Event loop can be terminated gracefully

**Dependencies**: Task 4.2

**Files to Create**: `BlueSky.Platform/Linux/X11EventLoop.cs`

---

### Task 4.4: Implement X11Input Class
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 5 hours

**Description**: Implement IInputContext interface for X11 input handling.

**Acceptance Criteria**:
- [ ] X11Input.cs created implementing IInputContext
- [ ] Keyboard event handling (KeyPress, KeyRelease)
- [ ] Key code mapping from X11 KeySym to KeyCode
- [ ] Mouse event handling (ButtonPress, ButtonRelease, MotionNotify)
- [ ] Mouse position tracking relative to window
- [ ] Scroll event handling (Button4, Button5)
- [ ] Modifier key state tracking
- [ ] Character input handling using XLookupString
- [ ] All input events fire correctly

**Dependencies**: Task 1.3, Task 1.6, Task 4.2

**Files to Create**: `BlueSky.Platform/Linux/X11Input.cs`

---

### Task 4.5: Test Linux Implementation with bgfx
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 3 hours

**Description**: Verify that X11Window works with bgfx Vulkan/OpenGL backend.

**Acceptance Criteria**:
- [ ] Create test application using X11Window
- [ ] Initialize bgfx with X11 Window handle from GetNativeHandle()
- [ ] bgfx Vulkan or OpenGL backend initializes without errors
- [ ] Window displays with bgfx rendering
- [ ] Window resize updates bgfx viewport correctly

**Dependencies**: Task 4.2, Task 4.3, Task 4.4

---

## Phase 5: Window Factory and Platform Detection

### Task 5.1: Implement WindowFactory Class
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 2 hours

**Description**: Create factory class for platform-specific window instantiation.

**Acceptance Criteria**:
- [ ] WindowFactory.cs created with static Create method
- [ ] Platform detection using RuntimeInformation
- [ ] Returns CocoaWindow on macOS
- [ ] Returns Win32Window on Windows
- [ ] Returns X11Window on Linux
- [ ] Throws exception on unsupported platforms
- [ ] Factory compiles and works on all platforms

**Dependencies**: Task 2.2, Task 3.2, Task 4.2

**Files to Create**: `BlueSky.Platform/WindowFactory.cs`

---

### Task 5.2: Add CreateInput Extension Method
**Status**: pending  
**Priority**: medium  
**Estimated Effort**: 1 hour

**Description**: Add extension method to IWindow for creating input context.

**Acceptance Criteria**:
- [ ] Extension method CreateInput() added to IWindow
- [ ] Returns platform-specific IInputContext implementation
- [ ] Works correctly on all platforms
- [ ] Method compiles without errors

**Dependencies**: Task 2.4, Task 3.4, Task 4.4

---

## Phase 6: BlueSky.Rendering Integration

### Task 6.1: Update BgfxRenderer to Use IWindow
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 3 hours

**Description**: Modify BgfxRenderer to accept IWindow instead of Silk.NET IWindow.

**Acceptance Criteria**:
- [ ] BgfxRenderer constructor accepts BlueSky.Platform.IWindow
- [ ] GetNativeHandle() used to retrieve platform-specific handle
- [ ] Platform data setup works with native handles
- [ ] bgfx initializes successfully on all platforms
- [ ] Renderer compiles without Silk.NET references

**Dependencies**: Task 5.1

**Files to Modify**: `BlueSky.Rendering/BgfxRenderer.cs`

---

### Task 6.2: Remove Silk.NET from BlueSky.Rendering
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 1 hour

**Description**: Remove all Silk.NET package references from BlueSky.Rendering project.

**Acceptance Criteria**:
- [ ] Silk.NET.Windowing package reference removed from .csproj
- [ ] All Silk.NET using statements removed
- [ ] Project compiles successfully
- [ ] No Silk.NET assemblies in output directory

**Dependencies**: Task 6.1

**Files to Modify**: `BlueSky.Rendering/BlueSky.Rendering.csproj`

---

### Task 6.3: Add BlueSky.Platform Reference to BlueSky.Rendering
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 30 minutes

**Description**: Add project reference to BlueSky.Platform in BlueSky.Rendering.

**Acceptance Criteria**:
- [ ] ProjectReference added to BlueSky.Rendering.csproj
- [ ] Project compiles successfully
- [ ] IWindow interface accessible in BgfxRenderer

**Dependencies**: Task 1.1

**Files to Modify**: `BlueSky.Rendering/BlueSky.Rendering.csproj`

---

## Phase 7: NotBSUI Integration

### Task 7.1: Update InputManager to Use IInputContext
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 4 hours

**Description**: Modify NotBSUI InputManager to use BlueSky.Platform.IInputContext.

**Acceptance Criteria**:
- [ ] InputManager constructor accepts BlueSky.Platform.IInputContext
- [ ] Event subscriptions updated (MouseMove, MouseDown, etc.)
- [ ] Input state queries updated (IsMouseButtonDown, MousePosition)
- [ ] UI hit testing works correctly
- [ ] InputManager compiles without Silk.NET references

**Dependencies**: Task 5.1

**Files to Modify**: `NotBSUI/Input/InputManager.cs`

---

### Task 7.2: Remove Silk.NET from NotBSUI
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 1 hour

**Description**: Remove all Silk.NET package references from NotBSUI project.

**Acceptance Criteria**:
- [ ] Silk.NET.Input package reference removed from .csproj
- [ ] All Silk.NET using statements removed
- [ ] Project compiles successfully
- [ ] No Silk.NET assemblies in output directory

**Dependencies**: Task 7.1

**Files to Modify**: `NotBSUI/NotBSUI.csproj`

---

### Task 7.3: Add BlueSky.Platform Reference to NotBSUI
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 30 minutes

**Description**: Add project reference to BlueSky.Platform in NotBSUI.

**Acceptance Criteria**:
- [ ] ProjectReference added to NotBSUI.csproj
- [ ] Project compiles successfully
- [ ] IInputContext interface accessible in InputManager

**Dependencies**: Task 1.1

**Files to Modify**: `NotBSUI/NotBSUI.csproj`

---

## Phase 8: BlueSky.Editor Integration

### Task 8.1: Update Program.cs to Use WindowFactory
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 4 hours

**Description**: Replace Silk.NET window creation with WindowFactory in editor.

**Acceptance Criteria**:
- [ ] Silk.NET Window.Create replaced with WindowFactory.Create
- [ ] WindowOptions configured with editor settings
- [ ] Event handlers updated (Resize, Render, Update, Closing)
- [ ] Input context created using CreateInput()
- [ ] Editor runs successfully with native windowing

**Dependencies**: Task 5.1, Task 6.1, Task 7.1

**Files to Modify**: `BlueSky.Editor/Program.cs`

---

### Task 8.2: Update Event Loop in Program.cs
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 2 hours

**Description**: Replace Silk.NET event loop with manual ProcessEvents loop.

**Acceptance Criteria**:
- [ ] Main loop calls _window.ProcessEvents()
- [ ] Update and Render callbacks invoked manually
- [ ] Frame timing calculated correctly
- [ ] Loop terminates when window closes
- [ ] Editor runs smoothly without frame drops

**Dependencies**: Task 8.1

**Files to Modify**: `BlueSky.Editor/Program.cs`

---

### Task 8.3: Remove Silk.NET from BlueSky.Editor
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 1 hour

**Description**: Remove all Silk.NET package references from BlueSky.Editor project.

**Acceptance Criteria**:
- [ ] Silk.NET.Windowing package reference removed from .csproj
- [ ] Silk.NET.Maths package reference removed (if present)
- [ ] All Silk.NET using statements removed
- [ ] Project compiles successfully
- [ ] No Silk.NET assemblies in output directory

**Dependencies**: Task 8.1, Task 8.2

**Files to Modify**: `BlueSky.Editor/BlueSky.Editor.csproj`

---

### Task 8.4: Add BlueSky.Platform Reference to BlueSky.Editor
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 30 minutes

**Description**: Add project reference to BlueSky.Platform in BlueSky.Editor.

**Acceptance Criteria**:
- [ ] ProjectReference added to BlueSky.Editor.csproj
- [ ] Project compiles successfully
- [ ] WindowFactory accessible in Program.cs

**Dependencies**: Task 1.1

**Files to Modify**: `BlueSky.Editor/BlueSky.Editor.csproj`

---

## Phase 9: Testing and Validation

### Task 9.1: Test on macOS Apple Silicon
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 3 hours

**Description**: Comprehensive testing on macOS with Apple Silicon (M1/M2/M3).

**Acceptance Criteria**:
- [ ] Editor launches successfully
- [ ] Window displays correctly
- [ ] bgfx Metal backend initializes without errors
- [ ] 3D viewport renders correctly
- [ ] UI renders correctly
- [ ] Keyboard input works
- [ ] Mouse input works
- [ ] Window resize works
- [ ] No crashes or errors in console

**Dependencies**: Task 8.1, Task 8.2, Task 8.3

---

### Task 9.2: Test on macOS Intel
**Status**: pending  
**Priority**: medium  
**Estimated Effort**: 3 hours

**Description**: Comprehensive testing on macOS with Intel processors.

**Acceptance Criteria**:
- [ ] Editor launches successfully
- [ ] Window displays correctly
- [ ] bgfx Metal or OpenGL backend initializes without errors
- [ ] 3D viewport renders correctly
- [ ] UI renders correctly
- [ ] Keyboard input works
- [ ] Mouse input works
- [ ] Window resize works
- [ ] No crashes or errors in console

**Dependencies**: Task 8.1, Task 8.2, Task 8.3

---

### Task 9.3: Test on Windows 10/11
**Status**: pending  
**Priority**: high  
**Estimated Effort**: 3 hours

**Description**: Comprehensive testing on Windows 10 and Windows 11.

**Acceptance Criteria**:
- [ ] Editor launches successfully
- [ ] Window displays correctly
- [ ] bgfx DirectX 11 backend initializes without errors
- [ ] 3D viewport renders correctly
- [ ] UI renders correctly
- [ ] Keyboard input works
- [ ] Mouse input works
- [ ] Window resize works
- [ ] No crashes or errors in console

**Dependencies**: Task 8.1, Task 8.2, Task 8.3

---

### Task 9.4: Test on Linux (Ubuntu/Fedora)
**Status**: pending  
**Priority**: medium  
**Estimated Effort**: 3 hours

**Description**: Comprehensive testing on Linux distributions.

**Acceptance Criteria**:
- [ ] Editor launches successfully
- [ ] Window displays correctly
- [ ] bgfx Vulkan or OpenGL backend initializes without errors
- [ ] 3D viewport renders correctly
- [ ] UI renders correctly
- [ ] Keyboard input works
- [ ] Mouse input works
- [ ] Window resize works
- [ ] No crashes or errors in console

**Dependencies**: Task 8.1, Task 8.2, Task 8.3

---

### Task 9.5: Performance Profiling
**Status**: pending  
**Priority**: medium  
**Estimated Effort**: 4 hours

**Description**: Profile performance of native windowing system.

**Acceptance Criteria**:
- [ ] Event processing overhead measured
- [ ] Input latency measured
- [ ] Frame time consistency verified
- [ ] Memory usage profiled
- [ ] No memory leaks detected
- [ ] Performance meets or exceeds Silk.NET baseline

**Dependencies**: Task 9.1, Task 9.2, Task 9.3, Task 9.4

---

### Task 9.6: Multi-Window Testing
**Status**: pending  
**Priority**: low  
**Estimated Effort**: 2 hours

**Description**: Test creating and managing multiple windows simultaneously.

**Acceptance Criteria**:
- [ ] Multiple windows can be created
- [ ] Each window renders independently
- [ ] Input events routed to correct window
- [ ] Windows can be closed independently
- [ ] No resource leaks with multiple windows

**Dependencies**: Task 9.1, Task 9.2, Task 9.3

---

## Phase 10: Documentation and Cleanup

### Task 10.1: Write API Documentation
**Status**: pending  
**Priority**: medium  
**Estimated Effort**: 4 hours

**Description**: Write comprehensive XML documentation for all public APIs.

**Acceptance Criteria**:
- [ ] All public interfaces documented
- [ ] All public classes documented
- [ ] All public methods documented
- [ ] Code examples provided for common scenarios
- [ ] Documentation builds without warnings

**Dependencies**: All implementation tasks

---

### Task 10.2: Create Migration Guide
**Status**: pending  
**Priority**: medium  
**Estimated Effort**: 3 hours

**Description**: Write guide for migrating from Silk.NET to native windowing.

**Acceptance Criteria**:
- [ ] Migration guide created in docs/
- [ ] Step-by-step instructions provided
- [ ] Code examples showing before/after
- [ ] Common pitfalls documented
- [ ] FAQ section included

**Dependencies**: All implementation tasks

---

### Task 10.3: Update README
**Status**: pending  
**Priority**: low  
**Estimated Effort**: 1 hour

**Description**: Update project README with native windowing information.

**Acceptance Criteria**:
- [ ] README mentions native windowing system
- [ ] Platform requirements documented
- [ ] Build instructions updated
- [ ] Dependencies list updated

**Dependencies**: All implementation tasks

---

### Task 10.4: Clean Up Obsolete Code
**Status**: pending  
**Priority**: low  
**Estimated Effort**: 2 hours

**Description**: Remove any remaining Silk.NET-related code and comments.

**Acceptance Criteria**:
- [ ] All Silk.NET references removed from codebase
- [ ] Obsolete comments removed
- [ ] Dead code removed
- [ ] Code compiles cleanly on all platforms

**Dependencies**: Task 8.3, Task 6.2, Task 7.2

---

## Summary

**Total Tasks**: 54  
**Estimated Total Effort**: ~130 hours

**Critical Path**:
1. Phase 1: Project Setup (9 hours)
2. Phase 2: macOS Implementation (21 hours)
3. Phase 5: Window Factory (3 hours)
4. Phase 6: Rendering Integration (4.5 hours)
5. Phase 7: NotBSUI Integration (5.5 hours)
6. Phase 8: Editor Integration (7.5 hours)
7. Phase 9: Testing (15 hours)

**Minimum Viable Product** (macOS only): ~50 hours
**Full Cross-Platform Implementation**: ~130 hours
